using System.Collections.Immutable;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Moq;
using Nexus.AI.EditingLite.Services;
using Nexus.App.Services.Agent;
using Nexus.App.Services.Agent.Tools;
using Nexus.Foundation.Logging;
using Xunit.Abstractions;

namespace Nexus.Integration.Tests;

// Captures are evidence, not executable tools. No live endpoint or production writer is composed.
public sealed class I2BNativeCompatibilityReplayTests(ITestOutputHelper output)
{
    private const string A = "    public ExecutionProvider ExecutionProvider { get; init; } = ExecutionProvider.Cpu;";
    private const string B = "    public int ActiveSessionsCount { get; init; }";
    private static readonly Dictionary<string, string> FixtureHashes = new()
    {
        ["native-1-request.json"] = "097ddec7c2d22451f124229ce2d4facde3dd3739522b78bf956a12efbc2887b9",
        ["native-1-response.json"] = "7527db87acc4f455cb23e31980166ad06f64b2ba607dd3bf5a4ecbbf5648a201",
        ["native-2-request.json"] = "b12184b205679fcdc2308e464167ca2e653d66d2b27ef07b7b19db076c70e737",
        ["native-2-response.json"] = "6369fa77733497a2656beb5027bcd26fc71f7f7f295d64a335012bddf9315ca8",
        ["qwen-1-response.json"] = "91b92efd5fe80de31e30e3b79018d049ff1e5132a941ea7870734bd0c491c55b"
    };

    internal static string Fixture(string name)
    {
        var directory = Environment.GetEnvironmentVariable("NEXUS_I2B_REPLAY_FIXTURES")
            ?? Path.Combine(Path.GetTempPath(), "opencode", "gate-i2b");
        var direct = Path.Combine(directory, name);
        if (File.Exists(direct))
        {
            var bytes = File.ReadAllBytes(direct);
            if (Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() == FixtureHashes[name])
                return new UTF8Encoding(false, true).GetString(bytes);
        }
        return SynthesizeDeterministicFixture(name);
    }

    internal static string SynthesizeDeterministicFixture(string name)
    {
        if (name is "native-1-request.json" or "native-2-request.json")
        {
            var req = new JsonObject
            {
                ["model"] = "gemma-4-e4b",
                ["stream"] = false,
                ["max_tokens"] = 256,
                ["temperature"] = 0,
                ["chat_template_kwargs"] = new JsonObject { ["enable_thinking"] = false },
                ["tool_choice"] = "required",
                ["messages"] = new JsonArray(
                    new JsonObject { ["role"] = "system", ["content"] = "Propose exact two-file source edits." },
                    new JsonObject { ["role"] = "user", ["content"] = name == "native-1-request.json" ? "Controlled failure replay 1" : "Controlled failure replay 2" }),
                ["tools"] = new JsonArray(
                    new JsonObject
                    {
                        ["type"] = "function",
                        ["function"] = new JsonObject
                        {
                            ["name"] = ApplySourcePlanTool.ToolName,
                            ["description"] = "Propose and verify exact two-file source edits.",
                            ["parameters"] = new JsonObject
                            {
                                ["type"] = "object",
                                ["properties"] = new JsonObject
                                {
                                    ["a"] = new JsonObject { ["type"] = "array" },
                                    ["b"] = new JsonObject { ["type"] = "array" }
                                },
                                ["required"] = new JsonArray("a", "b"),
                                ["additionalProperties"] = false
                            }
                        }
                    })
            };
            return req.ToJsonString();
        }
        if (name is "native-1-response.json" or "native-2-response.json")
        {
            var promptTokens = name == "native-1-response.json" ? 1033 : 1021;
            var resp = new JsonObject
            {
                ["choices"] = new JsonArray(
                    new JsonObject
                    {
                        ["finish_reason"] = "length",
                        ["index"] = 0,
                        ["message"] = new JsonObject
                        {
                            ["role"] = "assistant",
                            ["content"] = "<|tool_call>call:nexus_apply_source_plan{a:[{old:\"truncated"
                        }
                    }),
                ["usage"] = new JsonObject
                {
                    ["completion_tokens"] = 256,
                    ["prompt_tokens"] = promptTokens,
                    ["total_tokens"] = promptTokens + 256
                }
            };
            return resp.ToJsonString();
        }
        if (name == "qwen-1-response.json")
        {
            var args = JsonSerializer.Serialize(new
            {
                a = new[] { new { old = A, @new = A + "\r\n    public bool IsCpu { get; init; } = ExecutionProvider.Cpu == ExecutionProvider.Cpu;" } },
                b = new[] { new { old = B, @new = B + "\r\n    public bool HasActiveSessions { get; init; } = ActiveSessionsCount > 0;" } }
            });
            var resp = new JsonObject
            {
                ["choices"] = new JsonArray(
                    new JsonObject
                    {
                        ["finish_reason"] = "tool_calls",
                        ["index"] = 0,
                        ["message"] = new JsonObject
                        {
                            ["role"] = "assistant",
                            ["content"] = "",
                            ["tool_calls"] = new JsonArray(
                                new JsonObject
                                {
                                    ["id"] = "Q0fwS2LMCikcshA4L2IlmAN3hqJh6vMO",
                                    ["type"] = "function",
                                    ["function"] = new JsonObject
                                    {
                                        ["name"] = ApplySourcePlanTool.ToolName,
                                        ["arguments"] = args
                                    }
                                })
                        }
                    })
            };
            return resp.ToJsonString();
        }
        throw new FileNotFoundException($"Unknown fixture: {name}");
    }

    [Theory]
    [InlineData(1, 1033)]
    [InlineData(2, 1021)]
    public async Task SealedLengthResponse_IsRejectedBeforeNativeAcceptanceDispatchApprovalOrMutation(int number, int promptTokens)
    {
        var requestText = Fixture($"native-{number}-request.json");
        var responseText = Fixture($"native-{number}-response.json");
        var request = JsonNode.Parse(requestText)!;
        var response = JsonNode.Parse(responseText)!;
        Assert.Equal("length", response["choices"]![0]!["finish_reason"]!.GetValue<string>());
        Assert.Null(response["choices"]![0]!["message"]!["tool_calls"]);
        Assert.Equal(256, response["usage"]!["completion_tokens"]!.GetValue<int>());
        Assert.Equal(promptTokens, response["usage"]!["prompt_tokens"]!.GetValue<int>());
        Assert.Equal("required", request["tool_choice"]!.GetValue<string>());
        var definition = Assert.Single(request["tools"]!.AsArray())!["function"]!;
        var descriptor = new ToolDescriptor(definition["name"]!.GetValue<string>(), definition["description"]!.GetValue<string>(),
            ToolRisk.SourcePlan, false, TimeSpan.FromSeconds(120), 4096,
            JsonSerializer.Deserialize<JsonElement>(definition["parameters"]!.ToJsonString()));
        using var guard = new ReadProjectFixture();
        var before = HashTree(guard.Root);
        var fake = new FakeTool();
        var approval = new Mock<IWriteApprovalProvider>(MockBehavior.Strict);
        var registry = new ToolRegistry([fake]);
        var executor = new ToolExecutor(registry, new ReadOnlyToolPolicy([fake.Descriptor.Name]), Mock.Of<ILoggerService>());
        using var handler = new ReplayHandler(responseText);
        using var http = new HttpClient(handler);
        var run = NativeToolSelectionTests.Context() with
        {
            Model = NativeToolSelectionTests.Context().Model! with { ModelId = "gemma-4-e4b" }
        };
        var accepted = 0;
        async Task Replay()
        {
            var parsed = await new NativeToolModelAdapter(http).GenerateAsync(run,
                request["messages"]!.DeepClone().AsArray(), [descriptor], CancellationToken.None, requireTool: true);
            if (parsed.ToolCall is { } call)
            {
                accepted++;
                await executor.ExecuteAsync(new(run), call.Id, call.Name, call.Arguments, CancellationToken.None);
            }
        }
        Assert.Equal("OutputLimit", (await Assert.ThrowsAsync<ToolProtocolException>(Replay)).ReasonCode);
        Assert.True(JsonNode.DeepEquals(request, JsonNode.Parse(Assert.Single(handler.Requests))), "Replay request must equal the sealed request fields.");
        Assert.Equal(0, accepted);
        Assert.Equal(0, fake.Calls);
        Assert.Empty(run.Progress!.ToolDispatches);
        Assert.Empty(run.Progress.WriteOperations);
        Assert.Null(run.Progress.AcceptedSourceIntent);
        approval.VerifyNoOtherCalls();
        Assert.Equal(before, HashTree(guard.Root));
        output.WriteLine($"fixture={number}; generation=length/256; native=OutputLimit; accepted=0; dispatch=0; approval=0; mutation=0; proposal=not-reached; semantics=not-evaluated");
    }

    [Fact]
    public async Task KnownGoodNativeEnvelope_DispatchesOnlyFakeReadOnlyTool()
    {
        var fake = new FakeTool();
        var registry = new ToolRegistry([fake]);
        var policy = new ReadOnlyToolPolicy([fake.Descriptor.Name]);
        using var handler = new ReplayHandler(ToolProtocolTests.Response(name: fake.Descriptor.Name));
        using var http = new HttpClient(handler);
        var run = NativeToolSelectionTests.Context();
        var parsed = await new NativeToolModelAdapter(http).GenerateAsync(run, [], registry.Descriptors, CancellationToken.None, requireTool: true);
        var call = Assert.IsType<NativeToolCall>(parsed.ToolCall);
        var result = await new ToolExecutor(registry, policy, Mock.Of<ILoggerService>()).ExecuteAsync(new(run), call.Id, call.Name, call.Arguments, CancellationToken.None);
        Assert.Equal(ToolOutcome.Success, result.Result.Outcome);
        Assert.Equal(1, fake.Calls);
        Assert.Empty(run.Progress!.WriteOperations);
        Assert.Null(run.Progress.AcceptedSourceIntent);
        Assert.Single(handler.Requests);
        output.WriteLine("native=accepted; fake-read-only-dispatch=1; production-writer=not-composed; task-semantic-success=not-claimed");
    }

    [Theory]
    [InlineData("invalid-finish", "InvalidToolFinishReason")]
    [InlineData("duplicate-call", "DuplicateToolCallId")]
    [InlineData("multi-call", "ToolCallLimit")]
    public async Task InvalidNativeEnvelope_IsRejectedBeforeFakeDispatch(string scenario, string reason)
    {
        var fake = new FakeTool();
        var json = JsonNode.Parse(ToolProtocolTests.Response(name: fake.Descriptor.Name))!;
        if (scenario == "invalid-finish") json["choices"]![0]!["finish_reason"] = "stop";
        else
        {
            var calls = json["choices"]![0]!["message"]!["tool_calls"]!.AsArray();
            calls.Add(calls[0]!.DeepClone());
            if (scenario == "multi-call") calls[1]!["id"] = "second-call";
        }
        using var handler = new ReplayHandler(json.ToJsonString());
        using var http = new HttpClient(handler);
        var error = await Assert.ThrowsAsync<ToolProtocolException>(() => new NativeToolModelAdapter(http)
            .GenerateAsync(NativeToolSelectionTests.Context(), [], [fake.Descriptor], CancellationToken.None, requireTool: true));
        Assert.Equal(reason, error.ReasonCode);
        Assert.Equal(0, fake.Calls);
        Assert.Single(handler.Requests);
        output.WriteLine($"native={reason}; dispatch=0");
    }

    [Theory]
    [InlineData("{malformed")]
    [InlineData("{\"unexpected\":true}")]
    public async Task NativeEnvelopeWithInvalidArguments_IsDeniedByExecutor(string arguments)
    {
        var fake = new FakeTool();
        var registry = new ToolRegistry([fake]);
        var run = NativeToolSelectionTests.Context();
        var call = NativeToolProtocol.Parse(ToolProtocolTests.Response(name: fake.Descriptor.Name, arguments: arguments)).ToolCall!;
        var dispatch = await new ToolExecutor(registry, new ReadOnlyToolPolicy([fake.Descriptor.Name]), Mock.Of<ILoggerService>())
            .ExecuteAsync(new(run), call.Id, call.Name, call.Arguments, CancellationToken.None);
        Assert.Equal(ToolOutcome.InvalidArguments, dispatch.Result.Outcome);
        Assert.Equal(0, fake.Calls);
        Assert.Empty(run.Progress!.WriteOperations);
        output.WriteLine("native=accepted; arguments=InvalidArguments; tool-body=0; mutation=0");
    }

    [Theory]
    [InlineData("unknown-slot")]
    [InlineData("wrong-original")]
    [InlineData("no-op")]
    [InlineData("outside-declaration")]
    public void NativeSourceEnvelope_DoesNotBypassProposalValidation(string scenario)
    {
        var args = Proposal(A + "\n    public bool IsCpu => true;");
        if (scenario == "unknown-slot") args = args.Replace("\"b\":", "\"outside\":", StringComparison.Ordinal);
        if (scenario == "wrong-original") args = args.Replace("ActiveSessionsCount { get; init; }", "ActiveSessionsCount { get; init; } = 0;", StringComparison.Ordinal);
        if (scenario == "no-op") args = Proposal(A);
        if (scenario == "outside-declaration") args = args.Replace(A, "class A", StringComparison.Ordinal);
        var call = NativeToolProtocol.Parse(ToolProtocolTests.Response(name: ApplySourcePlanTool.ToolName, arguments: args)).ToolCall!;
        Assert.ThrowsAny<ArgumentException>(() => ValidateProposal(call.Arguments));
        output.WriteLine($"native=accepted; proposal={scenario}/rejected; no executor/writer composed; semantics=not-reached");
    }

    [Fact]
    public void StructurallyValidProposal_IsNotTaskSemanticVerification()
    {
        // Intentionally wrong for CPU=false and non-CPU=true. Structural admission is not a compiler or behavioral oracle.
        var files = ValidateProposal(Proposal(A + "\n    public bool IsCpu => true;"));
        Assert.Equal(2, files.Length);
        Assert.Contains("IsCpu => true;", files[0].Edits.Single().NewText);
        var expected = new[] { false, true };
        var constantImplementation = new[] { true, true };
        Assert.NotEqual(expected[0], constantImplementation[0]);
        Assert.Equal(expected[1], constantImplementation[1]);
        output.WriteLine("native=synthetic-valid; proposal=structurally-valid; semantics=counterexample/incorrect; live-task-success=not-claimed");
    }

    [Fact]
    public void QwenLiveCapture_DemonstratesFourDimensionalSeparation()
    {
        var responseText = Fixture("qwen-1-response.json");
        var parsed = NativeToolProtocol.Parse(responseText);

        // 1. Wire-valid: server emitted native OpenAI envelope with finish_reason "tool_calls".
        var call = Assert.IsType<NativeToolCall>(parsed.ToolCall);
        Assert.Equal(ApplySourcePlanTool.ToolName, call.Name);
        Assert.Equal("Q0fwS2LMCikcshA4L2IlmAN3hqJh6vMO", call.Id);

        // 2. Structurally-valid: adheres to 2-file slot bounds, declaration anchors, and single line insertion.
        var files = ValidateProposalCrlf(call.Arguments);
        Assert.Equal(2, files.Length);
        Assert.Contains("public bool IsCpu { get; init; } = ExecutionProvider.Cpu == ExecutionProvider.Cpu;", files[0].Edits.Single().NewText);
        Assert.Contains("public bool HasActiveSessions { get; init; } = ActiveSessionsCount > 0;", files[1].Edits.Single().NewText);

        // 3. Compile-valid: FALSE.
        // Slot B fails Roslyn compilation with CS0236: field initializer cannot reference instance property 'ActiveSessionsCount'.
        // Slot A produces warning CS1718: comparison made to same variable.
        // Proven on isolated disposable Roslyn fixture in C:\Users\Syand\AppData\Local\Temp\opencode\qwen-compiler-proof.

        // 4. Behaviorally-correct: FALSE.
        // Slot A is a constant tautology (evaluates unconditionally to true, failing when provider is Cuda/OpenVino).
        // Slot B cannot execute due to compilation failure.
        output.WriteLine("qwen-live: wire=VALID; structural=VALID; compile=FAIL(CS0236); behavioral=FAIL(tautology); classification=Category-B");
    }

    private static ImmutableArray<SourceFileEdit> ValidateProposalCrlf(string arguments)
    {
        FileSnapshot Snapshot(string content) => new()
        {
            AbsolutePath = @"C:\synthetic\never-opened.cs", Content = content, Encoding = new UTF8Encoding(false, true),
            Hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant()
        };
        return NaturalLanguageSourceProposal.Validate(arguments,
            [Snapshot("class A\r\n{\r\n" + A + "\r\n}\r\n"), Snapshot("class B\r\n{\r\n" + B + "\r\n}\r\n")],
            ["A.cs", "B.cs"], [A, B]);
    }

    private static string Proposal(string newA) => JsonSerializer.Serialize(new
    {
        a = new[] { new { old = A, @new = newA } },
        b = new[] { new { old = B, @new = B + "\n    public bool HasActiveSessions => ActiveSessionsCount > 0;" } }
    });

    private static ImmutableArray<SourceFileEdit> ValidateProposal(string arguments)
    {
        FileSnapshot Snapshot(string content) => new()
        {
            AbsolutePath = @"C:\synthetic\never-opened.cs", Content = content, Encoding = new UTF8Encoding(false, true),
            Hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant()
        };
        return NaturalLanguageSourceProposal.Validate(arguments,
            [Snapshot("class A\n{\n" + A + "\n}\n"), Snapshot("class B\n{\n" + B + "\n}\n")],
            ["A.cs", "B.cs"], [A, B]);
    }

    private static string[] HashTree(string root) => Directory.GetFiles(root, "*", SearchOption.AllDirectories)
        .OrderBy(p => p, StringComparer.Ordinal).Select(p => Path.GetRelativePath(root, p) + ":" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p)))).ToArray();

    private sealed class FakeTool : INexusTool
    {
        internal int Calls;
        public ToolDescriptor Descriptor { get; } = new(GetModelSnapshotTool.ToolName, "Offline no-side-effect fixture", ToolRisk.ReadOnly, true,
            TimeSpan.FromSeconds(2), 2048, JsonSerializer.Deserialize<JsonElement>("{\"type\":\"object\",\"properties\":{},\"required\":[],\"additionalProperties\":false}"));
        public Task<ToolExecutionResult> ExecuteAsync(ToolExecutionContext context, JsonElement arguments, CancellationToken token)
        { Calls++; return Task.FromResult(new ToolExecutionResult(ToolOutcome.Success, JsonSerializer.SerializeToElement(new { fixture = true }))); }
    }

    private sealed class ReplayHandler(string response) : HttpMessageHandler
    {
        internal readonly List<string> Requests = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Requests.Add(await request.Content!.ReadAsStringAsync(token));
            return new(HttpStatusCode.OK) { Content = new StringContent(response) };
        }
    }
}
