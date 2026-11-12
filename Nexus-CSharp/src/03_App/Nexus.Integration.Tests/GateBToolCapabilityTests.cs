using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nexus.AI.Llama.Pipelines;
using Nexus.AI.Llama.Services;
using Nexus.App.Services;
using Nexus.Contracts;
using Nexus.Foundation.Config;
using Xunit.Abstractions;

namespace Nexus.Integration.Tests;

public sealed class GateBProbeFactAttribute : FactAttribute
{
    public GateBProbeFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("NEXUS_GATE_B_PROBE") != "1")
            Skip = "Explicit active-model/b11057 protocol probe; see GATE_B_REPORT.md.";
    }
}

public sealed class GateBToolCapabilityTests(ITestOutputHelper output)
{
    [GateBProbeFact]
    public async Task ActualActiveModel_NativeToolCallAndStreamedRoundTrip()
    {
        Assert.Empty(AlivePids());
        var root = Environment.GetEnvironmentVariable("NEXUS_GATE_B_EVIDENCE")!;
        Assert.True(Directory.Exists(root));
        var runId = Guid.NewGuid();
        var directory = Path.Combine(root, runId.ToString("N"));
        Directory.CreateDirectory(directory);
        var services = ArchitectureGovernanceTests.CreateProductionServices();
        var config = new ConfigService(Path.Combine(directory, "fixture-config.json"));
        config.SaveConfig(new AppConfigRecord
        {
            ModelRootFolder = Required("NEXUS_TEST_MODEL_ROOT"),
            EngineRootFolder = Required("NEXUS_TEST_ENGINE_ROOT")
        });
        services.RemoveAll<IConfigService>();
        services.AddSingleton<IConfigService>(config);
        await using var provider = services.BuildServiceProvider();
        var runtime = provider.GetRequiredService<IConversationRuntimeWorkflow>();
        var server = provider.GetRequiredService<ILlamaServerManager>();
        var http = provider.GetRequiredService<HttpClient>();
        using var transportTrace = new GateBTransportTrace();
        ((LlamaServerManager)server).OnEvent += transportTrace.RecordServer;
        var started = DateTimeOffset.UtcNow;
        var timer = Stopwatch.StartNew();
        var capability = new List<object>();
        var deferredEvidence = new List<Action>();
        string? errorCategory = null;
        int? pid = null;
        string? modelId = null;
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        try
        {
            var model = await runtime.StartActiveModelAsync();
            modelId = model.Id;
            pid = Assert.Single(AlivePids());
            Assert.Equal(pid, server.OwnedPid);
            Assert.Equal("b11057", new DirectoryInfo(server.ResolveEngineDir()).Name);
            Assert.Same(provider.GetRequiredService<LlamaServerManager>(),server);
            Assert.Same(server, Field(provider.GetRequiredService<GenericLlamaEngine>(),"_serverManager"));
            Assert.Same(http, Field(provider.GetRequiredService<LlamaConversationPipeline>(),"_httpClient"));
            Assert.Same(http, Field(provider.GetRequiredService<LlamaClientService>(),"_httpClient"));
            Assert.Same(server, Field(provider.GetRequiredService<LlamaClientService>(),"_serverManager"));
            await runtime.StartActiveModelAsync();
            Assert.Equal(pid,Assert.Single(AlivePids()));
            var endpoint = $"http://127.0.0.1:{provider.GetRequiredService<IGenericLlamaEngine>().Port}/v1/chat/completions";
            Save(directory,"identity.json",new
            {
                runId, started, modelId, modelPath=server.RuntimeModelPath, endpoint, pid, state=server.State.ToString(),
                runtimeIdentity=RuntimeHelpers.GetHashCode(server), engineIdentity=RuntimeHelpers.GetHashCode(provider.GetRequiredService<IGenericLlamaEngine>()),
                httpIdentity=RuntimeHelpers.GetHashCode(http),
                executableSha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(server.ResolveEngineDir(),"llama-server.exe"))))
            });
            File.WriteAllText(Path.Combine(directory,"props.response.json"),await http.GetStringAsync("http://127.0.0.1:8080/props",deadline.Token));

            for (var attempt=1; attempt<=2; attempt++)
            {
                // Automatic tool choice deliberately probes actual template/model
                // behavior, rather than assuming OpenAI compatibility means support.
                var request=Payload(model.Id,false);
                var raw=await SendRaw(http,endpoint,request,directory,$"nonstream-{attempt}",deadline.Token,deferredEvidence);
                var response=JsonNode.Parse(raw)!;
                var message=response["choices"]![0]!["message"]!;
                var call=ValidateToolMessage(message);
                var id=call!["id"]!.GetValue<string>();
                Assert.False(string.IsNullOrWhiteSpace(id));
                Assert.Equal("get_test_value",call["function"]!["name"]!.GetValue<string>());
                ValidateArguments(call["function"]!["arguments"]!.GetValue<string>());
                Assert.Equal("tool_calls",response["choices"]![0]!["finish_reason"]!.GetValue<string>());
                var followup=Payload(model.Id,false);
                followup["messages"]!.AsArray().Add(message.DeepClone());
                followup["messages"]!.AsArray().Add(new JsonObject { ["role"]="tool", ["tool_call_id"]=id, ["content"]="{\"value\":42}" });
                var followupRaw=await SendRaw(http,endpoint,followup,directory,$"followup-{attempt}",deadline.Token,deferredEvidence);
                var final=JsonNode.Parse(followupRaw)!["choices"]![0]!["message"]!;
                Assert.True(final["tool_calls"] is null || final["tool_calls"]!.AsArray().Count==0);
                Assert.Contains("42",final["content"]!.GetValue<string>());
                Assert.Equal(pid,Assert.Single(AlivePids()));
                capability.Add(new { attempt, mode="nonstream", toolCallId=id, roundTrip=true });
            }

            var streamed=await SendRaw(http,endpoint,Payload(model.Id,true),directory,"stream",deadline.Token,deferredEvidence);
            var idBuilder=new StringBuilder();
            var nameBuilder=new StringBuilder();
            var arguments=new StringBuilder();
            var deltaCount=0;
            var finished=false;
            foreach(var line in streamed.Split('\n'))
            {
                if(!line.StartsWith("data:",StringComparison.Ordinal)) continue;
                var data=line[5..].Trim();
                if(data=="[DONE]") { finished=true; continue; }
                var json=JsonNode.Parse(data)!;
                var choice=json["choices"]?.AsArray().FirstOrDefault();
                var calls=choice?["delta"]?["tool_calls"]?.AsArray();
                if(calls is null) continue;
                foreach(var call in calls)
                {
                    Assert.Equal(0,call!["index"]!.GetValue<int>());
                    idBuilder.Append(call["id"]?.GetValue<string>());
                    nameBuilder.Append(call["function"]?["name"]?.GetValue<string>());
                    arguments.Append(call["function"]?["arguments"]?.GetValue<string>());
                    deltaCount++;
                }
            }
            Assert.True(finished);
            Assert.True(deltaCount>0,"No native streamed tool-call delta observed.");
            Assert.NotEmpty(idBuilder.ToString());
            Assert.Equal("get_test_value",nameBuilder.ToString());
            ValidateArguments(arguments.ToString());
            var streamFollowup=Payload(modelId!,false);
            streamFollowup["messages"]!.AsArray().Add(new JsonObject
            {
                ["role"]="assistant", ["content"]="", ["tool_calls"]=new JsonArray(new JsonObject
                {
                    ["id"]=idBuilder.ToString(), ["type"]="function", ["function"]=new JsonObject
                    { ["name"]=nameBuilder.ToString(), ["arguments"]=arguments.ToString() }
                })
            });
            streamFollowup["messages"]!.AsArray().Add(new JsonObject { ["role"]="tool", ["tool_call_id"]=idBuilder.ToString(), ["content"]="{\"value\":42}" });
            var streamFinal=JsonNode.Parse(await SendRaw(http,endpoint,streamFollowup,directory,"stream-followup",deadline.Token,deferredEvidence))!["choices"]![0]!["message"]!;
            Assert.Contains("42",streamFinal["content"]!.GetValue<string>());
            Assert.True(streamFinal["tool_calls"] is null || streamFinal["tool_calls"]!.AsArray().Count==0);
            capability.Add(new { mode="stream", deltaCount, toolCallId=idBuilder.ToString(), validArguments=true, roundTrip=true });
        }
        catch(Exception error)
        {
            errorCategory=error is OperationCanceledException ? "TimeoutOrCancellation" : error is HttpRequestException ? "HttpOrUnsupported" : "NativeProtocolValidation";
            File.WriteAllText(Path.Combine(directory,"failure.txt"),error.ToString());
            throw;
        }
        finally
        {
            await runtime.StopAsync();
            ((LlamaServerManager)server).OnEvent -= transportTrace.RecordServer;
            Save(directory,"transport-timeline.json",transportTrace.Events);
            foreach (var write in deferredEvidence) write();
            Assert.Null(server.OwnedPid);
            Assert.Empty(AlivePids());
            Save(directory,"result.json",new { runId, started, ended=DateTimeOffset.UtcNow, durationMs=timer.ElapsedMilliseconds,
                pid, modelId, capability, errorCategory, cleanup=true });
            output.WriteLine($"GATE_B_PROBE {directory} error={errorCategory ?? "none"}");
        }
    }

    internal static JsonObject Payload(string modelId,bool stream) => JsonNode.Parse(JsonSerializer.Serialize(new
    {
        model=modelId, stream, max_tokens=256, temperature=0,
        chat_template_kwargs=new { enable_thinking=false }, tool_choice="auto",
        messages=new[] { new { role="system",content="Use get_test_value exactly once to get the requested value. After the tool result reply TEST_VALUE=42 only; do not call another tool." },
            new { role="user",content="Call get_test_value with key gate-b. Do not invent its result." } },
        tools=new[] { new { type="function", function=new { name="get_test_value",description="No-side-effect protocol test; retrieves a fixed test value.",
            parameters=new { type="object", properties=new { key=new { type="string", @enum=new[] { "gate-b" } } }, required=new[] { "key" }, additionalProperties=false } } } }
    }))!.AsObject();

    internal static void ValidateArguments(string arguments)
    {
        using var json=JsonDocument.Parse(arguments);
        Assert.Equal(JsonValueKind.Object,json.RootElement.ValueKind);
        Assert.Single(json.RootElement.EnumerateObject());
        Assert.Equal("gate-b",json.RootElement.GetProperty("key").GetString());
    }

    internal static JsonNode ValidateToolMessage(JsonNode message)
    {
        if(message["tool_calls"] is not JsonArray calls || calls.Count==0)
            throw new InvalidDataException("NativeToolCallingUnsupported: response contains no native tool_calls.");
        var call=Assert.Single(calls)!;
        Assert.Equal("function",call["type"]!.GetValue<string>());
        Assert.Equal("get_test_value",call["function"]!["name"]!.GetValue<string>());
        Assert.NotEmpty(call["id"]!.GetValue<string>());
        ValidateArguments(call["function"]!["arguments"]!.GetValue<string>());
        return call;
    }

    [Theory]
    [InlineData("{bad")]
    [InlineData("[]")]
    [InlineData("{\"key\":\"wrong\"}")]
    [InlineData("{\"key\":\"gate-b\",\"path\":\"anything\"}")]
    public void MalformedOrUnexpectedArguments_AreRejected(string value) =>
        Assert.ThrowsAny<Exception>(()=>ValidateArguments(value));

    [Fact]
    public void PlainTextResponse_IsUnsupportedRatherThanParserFallback()
    {
        var message=JsonNode.Parse("{\"role\":\"assistant\",\"content\":\"get_test_value(key=gate-b)\"}")!;
        var error=Assert.Throws<InvalidDataException>(()=>ValidateToolMessage(message));
        Assert.Contains("NativeToolCallingUnsupported",error.Message);
    }

    private static async Task<string> SendRaw(HttpClient http,string endpoint,JsonObject payload,string directory,string name,CancellationToken token,List<Action> deferredEvidence)
    {
        var raw=payload.ToJsonString(new JsonSerializerOptions { WriteIndented=true });
        File.WriteAllText(Path.Combine(directory,name+".request.json"),raw);
        using var request=new HttpRequestMessage(HttpMethod.Post,endpoint) { Content=new StringContent(raw,Encoding.UTF8,"application/json") };
        var sw=Stopwatch.StartNew();
        var body=new StringBuilder();
        var timeline = new List<object>();
        void Record(string stage, object? detail = null) => timeline.Add(new { stage, utc=DateTimeOffset.UtcNow,
            elapsedMs=sw.Elapsed.TotalMilliseconds, cancellationRequested=token.IsCancellationRequested, detail });
        Record("request-start", new { name, stream=payload["stream"]?.GetValue<bool>(), connectionClose=request.Headers.ConnectionClose });
        try
        {
            using var response=await http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,token);
            Record("response-headers", new { status=(int)response.StatusCode, headers=response.Headers.ToString(), contentHeaders=response.Content.Headers.ToString() });
            using var stream=await response.Content.ReadAsStreamAsync(token);
            using var reader=new StreamReader(stream);
            while(await reader.ReadLineAsync(token) is { } line)
            {
                body.AppendLine(line);
                Record("response-line", line);
                Assert.True(body.Length<2*1024*1024,"Probe response exceeded 2 MiB bound.");
            }
            Record("response-stream-eof");
            Save(directory,name+".http.json",new { status=(int)response.StatusCode,durationMs=sw.ElapsedMilliseconds,response.Headers });
            response.EnsureSuccessStatusCode();
            Record("normal-return");
            return body.ToString();
        }
        catch (Exception error)
        {
            Record("transport-exception", error.ToString());
            throw;
        }
        finally
        {
            Record("reader-stream-response-request-disposal-scope-exit");
            File.WriteAllText(Path.Combine(directory,name+".response.txt"),body.ToString());
            deferredEvidence.Add(() => Save(directory,name+".timeline.json",timeline));
        }
    }

    private static object? Field(object target,string name) => target.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(target);
    private static string Required(string name) => Environment.GetEnvironmentVariable(name) ?? throw new InvalidOperationException($"Fixture {name} required.");
    private static void Save(string directory,string name,object value) => File.WriteAllText(Path.Combine(directory,name),JsonSerializer.Serialize(value,new JsonSerializerOptions { WriteIndented=true }));
    private static int[] AlivePids()
    {
        var result=new List<int>();
        foreach(var process in Process.GetProcessesByName("llama-server")) using(process)
        {
            try { if(!process.HasExited) result.Add(process.Id); } catch(InvalidOperationException) { }
        }
        return result.ToArray();
    }
}

internal sealed class GateBTransportTrace : EventListener
{
    public ConcurrentQueue<object> Events { get; } = new();
    protected override void OnEventSourceCreated(EventSource source)
    {
        if (source.Name is "System.Net.Http" or "System.Net.Sockets")
            EnableEvents(source, EventLevel.Verbose);
    }
    protected override void OnEventWritten(EventWrittenEventArgs data)
    {
        Events?.Enqueue(new { utc=DateTimeOffset.UtcNow, source=data.EventSource.Name,
            name=data.EventName, activity=data.ActivityId, payloadNames=data.PayloadNames, payload=data.Payload });
    }
    public void RecordServer(ServerEvent data) => Events.Enqueue(new { utc=DateTimeOffset.UtcNow, server=data });
}
