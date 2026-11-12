using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nexus.AI.Llama.Models;
using Nexus.AI.Llama.Services;
using Nexus.App.Services;
using Nexus.App.Services.Agent;
using Nexus.App.Services.Agent.Tools;
using Nexus.Foundation.Config;
using Xunit.Abstractions;

namespace Nexus.Integration.Tests;

public sealed class GateHP1NativeSelectionLiveTests(ITestOutputHelper output)
{
    // P1 adds protocol evidence to the existing explicit Gate G certification setup.
    // The candidate schema is constructed here, never registered for mutation.
    [GateGLiveFact]
    [Trait("Category","GateHP1Reliability")]
    public async Task CandidateArrays_ProductionAdapterAndDecoder_ReadOnlyColdWarmChat()
    {
        Assert.Empty(AlivePids());
        var parent=Environment.GetEnvironmentVariable("NEXUS_GATE_H_P1_EVIDENCE")!;Assert.True(Directory.Exists(parent));
        var directory=Path.Combine(parent,"array-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        var fixturePath=Environment.GetEnvironmentVariable("NEXUS_GATE_H_P1_PAYLOAD")!;
        var fixtureBytes=File.ReadAllBytes(fixturePath);
        var fixture=JsonNode.Parse(fixtureBytes)!;
        var messages=fixture["messages"]!.AsArray();
        var schema=SourcePlanArgumentValidator.Schema;
        Assert.Throws<ArgumentException>(()=>ToolArgumentValidator.ValidateSchema(schema)); // generic dialect stays scalar-only
        var services=ArchitectureGovernanceTests.CreateProductionServices();
        var config=new ConfigService(Path.Combine(directory,"fixture-config.json"));
        config.SaveConfig(new AppConfigRecord { ModelRootFolder=Environment.GetEnvironmentVariable("NEXUS_TEST_MODEL_ROOT")!,EngineRootFolder=Environment.GetEnvironmentVariable("NEXUS_TEST_ENGINE_ROOT")! });
        services.RemoveAll<IConfigService>();services.AddSingleton<IConfigService>(config);
        using var handler=new Measurement(directory);using var http=new HttpClient(handler);
        await using var provider=services.BuildServiceProvider();
        var descriptor=Assert.Single(provider.GetRequiredService<ToolRegistry>().Descriptors,d=>d.Name==ApplySourcePlanTool.ToolName);
        SourcePlanArgumentValidator.ValidateSchema(descriptor.ArgumentSchema);
        var runtime=provider.GetRequiredService<ConversationRuntimeWorkflow>();var server=provider.GetRequiredService<ILlamaServerManager>();
        var records=new List<object>();string? currentMode=null;string stage="RuntimeSetup";object? failure=null;
        try
        {
            await runtime.StartActiveModelAsync();
            using var lease=runtime.AcquireAdditionalConsumer();
            var model=new AgentModelSnapshot(lease.Model.Id,lease.Model.ResolvedGgufPath!,lease.Pid,lease.Endpoint,lease.RuntimeIdentity);
            var run=NativeToolSelectionTests.Context() with { Model=model,Limits=GateFRepairLiveProof.Limits() };
            var adapter=new NativeToolModelAdapter(http);
            foreach(var mode in new[] { "cold","warm","chat" })
            {
                currentMode=mode;stage="NativeGeneration";handler.Latest=null;
                using var cancellation=CancellationTokenSource.CreateLinkedTokenSource(lease.CancellationToken);cancellation.CancelAfter(TimeSpan.FromSeconds(120));
                var watch=Stopwatch.StartNew();
                var chat=mode=="chat" ? StreamChat(runtime,cancellation.Token) : Task.CompletedTask;
                var response=await adapter.GenerateAsync(run,messages,[descriptor],cancellation.Token,requireTool:true);
                var elapsed=watch.Elapsed.TotalSeconds;await chat;
                stage="NativeSuccessAssertions";
                Assert.Equal("tool_calls",response.FinishReason);Assert.Equal(ApplySourcePlanTool.ToolName,response.ToolCall!.Name);
                ValidateExactArrays(response.ToolCall.Arguments);
                stage="HostContractAssertions";
                var body=JsonNode.Parse(handler.LastRequest)!;
                Assert.Equal("required",body["tool_choice"]!.GetValue<string>());
                Assert.Equal(ApplySourcePlanTool.ToolName,Assert.Single(body["tools"]!.AsArray())!["function"]!["name"]!.GetValue<string>());
                Assert.Equal(256,body["max_tokens"]!.GetValue<int>());
                var raw=handler.Latest!;
                if(mode=="cold") Assert.Equal(0,raw["usage"]!["prompt_tokens_details"]!["cached_tokens"]!.GetValue<int>());
                Assert.True(elapsed<120);Assert.Equal(lease.Pid,server.OwnedPid);
                records.Add(new { mode,seconds=elapsed,usage=raw["usage"]!.DeepClone(),timings=raw["timings"]!.DeepClone(),
                    contextBytes=Encoding.UTF8.GetByteCount(messages.ToJsonString()),argumentBytes=Encoding.UTF8.GetByteCount(response.ToolCall.Arguments),
                    adequate=true,toolDispatches=0,approvalRequests=0,sourceWrites=0 });
            }
            Assert.Equal(fixtureBytes,File.ReadAllBytes(fixturePath));
        }
        catch(Exception error)
        {
            failure=new { mode=currentMode,stage,errorType=error.GetType().FullName,error.Message,
                reasonCode=(error as ToolProtocolException)?.ReasonCode,usage=handler.Latest?["usage"]?.DeepClone(),
                nativeSuccess=false,toolDispatches=0,approvalRequests=0,sourceWrites=0 };
            output.WriteLine("GATE_H_P1_FAILURE "+JsonSerializer.Serialize(failure));
            throw;
        }
        finally
        {
            await runtime.StopAsync();Assert.Null(server.OwnedPid);
            File.WriteAllText(Path.Combine(directory,"array-proof.json"),JsonSerializer.Serialize(new { fixtureSha256=Convert.ToHexString(SHA256.HashData(fixtureBytes)),records,failure },new JsonSerializerOptions { WriteIndented=true }));
            Assert.Empty(AlivePids());
            output.WriteLine("GATE_H_P1_ARRAY "+directory);
        }
    }

    private static void ValidateExactArrays(string arguments)
    {
        Assert.True(Encoding.UTF8.GetByteCount(arguments)<=4096);
        using var document=JsonDocument.Parse(arguments,new JsonDocumentOptions { MaxDepth=4 });
        var slots=document.RootElement.EnumerateObject().ToArray();
        Assert.Equal(2,slots.Length);Assert.Equal(new[] { "a","b" },slots.Select(s=>s.Name).OrderBy(n=>n,StringComparer.Ordinal));
        var count=0;
        foreach(var slot in slots)
        {
            Assert.Equal(JsonValueKind.Array,slot.Value.ValueKind);Assert.Equal(slot.Name=="a" ? 2 : 1,slot.Value.GetArrayLength());
            var index=0;
            foreach(var edit in slot.Value.EnumerateArray())
            {
                Assert.Equal(new[] { "new","old" },edit.EnumerateObject().Select(p=>p.Name).OrderBy(n=>n,StringComparer.Ordinal));
                var old=edit.GetProperty("old").GetString()!;var replacement=edit.GetProperty("new").GetString()!;
                Assert.InRange(old.Length,1,256);Assert.InRange(replacement.Length,1,256);
                var signature=slot.Name=="a" && index==0;
                Assert.Equal(signature ? "int Value()" : "return 1;",old);
                Assert.Equal(signature ? "int Value() => Core(); private static int Core()" : "return 2;",replacement);
                index++;count++;
            }
        }
        Assert.Equal(3,count);
    }
    private static async Task StreamChat(ConversationRuntimeWorkflow runtime,CancellationToken token)
    { await foreach(var _ in runtime.StreamAsync([new ChatMessagePayload { Role="user",Content="Reply READY only." }],false,new LlamaGenerationOptions { MaxTokens=64,Temperature=0 },token)) { } }
    private static int[] AlivePids()
    {
        var ids=new List<int>();
        foreach(var process in Process.GetProcessesByName("llama-server")) using(process)
        { try { if(!process.HasExited) ids.Add(process.Id); } catch(InvalidOperationException) { } }
        return ids.ToArray();
    }
    private sealed class Measurement(string directory) : DelegatingHandler(new HttpClientHandler())
    {
        internal JsonNode? Latest;internal string LastRequest="";private int calls;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            LastRequest=await request.Content!.ReadAsStringAsync(token);File.WriteAllText(Path.Combine(directory,$"request-{++calls}.json"),LastRequest);
            var response=await base.SendAsync(request,token);var raw=await response.Content.ReadAsStringAsync(token);
            File.WriteAllText(Path.Combine(directory,$"response-{calls}.json"),raw);Latest=JsonNode.Parse(raw);return response;
        }
    }
}
