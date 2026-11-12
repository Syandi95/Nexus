using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Moq;
using Nexus.AI.Llama.Models;
using Nexus.AI.Llama.Pipelines;
using Nexus.AI.Llama.Services;
using Nexus.App.Services;
using Nexus.App.Services.Agent;
using Nexus.App.Services.Agent.Tools;
using Nexus.Contracts;
using Nexus.Foundation.Logging;
using Nexus.Foundation.Registry;
using Xunit;

namespace Nexus.Integration.Tests;

public sealed class AgentModelIntegrationReadinessTests
{
    [Fact]
    public async Task Handoff_QwenNativeRoute_UsesActualFingerprintAndSelectedSnapshot_OfflineTransport()
    {
        using var harness=new SwitchTestHarness(realIdentity:true);
        harness.Handler.NativeMode=true;
        await harness.Workflow.SwitchModelAsync(harness.QwenModel);
        var result=await harness.Agent.ExecuteAsync(new AgentRequest("qwen-native-offline","Read the active model identity",EnableReadOnlyToolCall:true),CancellationToken.None);
        Assert.Equal(AgentRunOutcome.Completed,result.Outcome);
        Assert.True(result.Verification!.Passed);
        Assert.Equal("qwen3-4b",result.Context.Model!.ModelId);
        Assert.Equal(2,harness.Handler.NativeBodies.Count);
        Assert.All(harness.Handler.NativeBodies,b=>Assert.Equal("qwen3-4b",JsonDocument.Parse(b).RootElement.GetProperty("model").GetString()));
        using var final=JsonDocument.Parse(harness.Handler.NativeBodies[1]);
        Assert.Equal("none",final.RootElement.GetProperty("tool_choice").GetString());
        Assert.Empty(final.RootElement.GetProperty("tools").EnumerateArray());
        Assert.Equal(GetModelSnapshotTool.ToolName,Assert.Single(result.ToolDispatches!).ToolName);
    }
    [Fact]
    public void DefaultProductionCatalog_RetainsGemmaAsActiveModel()
    {
        var indexPath = @"D:\NEXUS\Models\AI Workspace\model-index.json";
        Assert.True(File.Exists(indexPath), "Production model-index.json must exist.");
        using var doc = JsonDocument.Parse(File.ReadAllText(indexPath));
        var root = doc.RootElement;
        var activeModelId = root.GetProperty("activeModelId").GetString();
        Assert.Equal("gemma-4-e4b", activeModelId);

        var modelIds = root.GetProperty("models")
            .EnumerateArray()
            .Select(m => m.GetProperty("id").GetString())
            .ToArray();
        Assert.Contains("gemma-4-e4b", modelIds);
        Assert.Contains("qwen3-4b", modelIds);
    }

    [Fact]
    public async Task ModelSwitching_GemmaToQwenToGemmaToQwen_UpdatesActiveSnapshotAndPreservesIsolation()
    {
        using var harness = new SwitchTestHarness();
        await harness.Workflow.StartActiveModelAsync();
        Assert.True(harness.Workflow.IsReady);

        // 1. Initial State: Gemma
        var resultGemma1 = await harness.Agent.ExecuteAsync(new AgentRequest("run-1", "ping"), CancellationToken.None);
        Assert.Equal(AgentRunOutcome.Completed, resultGemma1.Outcome);
        Assert.Equal("gemma-4-e4b", resultGemma1.Context.Model!.ModelId);
        Assert.Equal(harness.GemmaGgufPath, resultGemma1.Context.Model!.ModelPath);

        // 2. Switch to Qwen
        var qwenSnapshot = await harness.Workflow.SwitchModelAsync(harness.QwenModel);
        Assert.Equal("qwen3-4b", qwenSnapshot.ActiveModel!.Id);
        var resultQwen1 = await harness.Agent.ExecuteAsync(new AgentRequest("run-2", "ping"), CancellationToken.None);
        Assert.Equal(AgentRunOutcome.Completed, resultQwen1.Outcome);
        Assert.Equal("qwen3-4b", resultQwen1.Context.Model!.ModelId);
        Assert.Equal(harness.QwenGgufPath, resultQwen1.Context.Model!.ModelPath);

        // 3. Switch back to Gemma
        var gemmaSnapshot = await harness.Workflow.SwitchModelAsync(harness.GemmaModel);
        Assert.Equal("gemma-4-e4b", gemmaSnapshot.ActiveModel!.Id);
        var resultGemma2 = await harness.Agent.ExecuteAsync(new AgentRequest("run-3", "ping"), CancellationToken.None);
        Assert.Equal(AgentRunOutcome.Completed, resultGemma2.Outcome);
        Assert.Equal("gemma-4-e4b", resultGemma2.Context.Model!.ModelId);

        // 4. Switch back to Qwen
        var qwenSnapshot2 = await harness.Workflow.SwitchModelAsync(harness.QwenModel);
        Assert.Equal("qwen3-4b", qwenSnapshot2.ActiveModel!.Id);
        var resultQwen2 = await harness.Agent.ExecuteAsync(new AgentRequest("run-4", "ping"), CancellationToken.None);
        Assert.Equal(AgentRunOutcome.Completed, resultQwen2.Outcome);
        Assert.Equal("qwen3-4b", resultQwen2.Context.Model!.ModelId);
    }

    [Fact]
    public async Task ModelSwitching_DuringActiveAgentRequest_CancelsAndDrainsConsumerBeforeStopping()
    {
        using var harness = new SwitchTestHarness();
        await harness.Workflow.StartActiveModelAsync();

        // Launch in-flight blocking agent request on Gemma
        var agentTask = harness.Agent.ExecuteAsync(new AgentRequest("session-switch", "agent-block"), CancellationToken.None);
        await harness.Handler.AgentStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        bool agentWasDrainedBeforeStop = false;
        harness.OnStop = () =>
        {
            agentWasDrainedBeforeStop = agentTask.IsCompleted;
        };

        // Switch to Qwen while Gemma agent is still running
        var switched = await harness.Workflow.SwitchModelAsync(harness.QwenModel).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("qwen3-4b", switched.ActiveModel!.Id);
        Assert.True(agentWasDrainedBeforeStop, "Active agent consumer must be cancelled and drained before runtime stop.");

        var agentResult = await agentTask;
        Assert.Equal(AgentRunOutcome.Cancelled, agentResult.Outcome);
        Assert.Equal("WorkspaceRuntimeDeactivated", agentResult.ErrorCategory);
        Assert.Equal("gemma-4-e4b", agentResult.Context.Model!.ModelId);

        // Next request on Qwen proceeds cleanly
        var nextResult = await harness.Agent.ExecuteAsync(new AgentRequest("session-next", "quick"), CancellationToken.None);
        Assert.Equal(AgentRunOutcome.Completed, nextResult.Outcome);
        Assert.Equal("qwen3-4b", nextResult.Context.Model!.ModelId);
    }

    [Fact]
    public async Task AgentOrchestration_RejectsUnsupportedOrMismatchedModel_FailClosed()
    {
        using var harness = new SwitchTestHarness();
        var unknownModel = new ModelCatalogItem("unsupported-7b", "Unsupported 7B", "unsupported.json",
            Path.Combine(harness.Root, "unsupported.gguf"), ModelCatalogItemStatus.Valid, null, false);
        File.WriteAllBytes(unknownModel.ResolvedGgufPath!, [99]);

        harness.AddCatalogModel(unknownModel);
        await harness.Workflow.SwitchModelAsync(unknownModel);

        var result = await harness.Agent.ExecuteAsync(new AgentRequest("session-unknown", "test-tool", EnableReadOnlyToolCall: true), CancellationToken.None);
        Assert.Equal(AgentRunOutcome.Failed, result.Outcome);
        Assert.Equal(AgentFailureCategory.NativeToolUnsupported, result.Failure);
        Assert.Null(result.Tool);
    }

    [Fact]
    public async Task AgentCapabilityCache_MaintainsIsolationAcrossModelSwitches()
    {
        var root = Path.Combine(Path.GetTempPath(), "nexus-cache-isolation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "b11057"));
        var gemmaFile = Path.Combine(root, "gemma-4-E4B-it-Q4_K_M.gguf");
        var qwenFile = Path.Combine(root, "Qwen3-4B-Q4_K_M.gguf");
        var engineFile = Path.Combine(root, "b11057", "llama-server.exe");
        File.WriteAllBytes(gemmaFile, [10]);
        File.WriteAllBytes(qwenFile, [20]);
        File.WriteAllBytes(engineFile, [30]);

        var server = new Mock<ILlamaServerManager>();
        server.Setup(s => s.ResolveEngineDir()).Returns(Path.GetDirectoryName(engineFile)!);
        var capability = new NativeToolCapability(server.Object);

        var gemmaSnapshot = new AgentModelSnapshot("gemma-4-e4b", gemmaFile, 123, "http://127.0.0.1:8080/v1/chat/completions", 1);
        var qwenSnapshot = new AgentModelSnapshot("qwen3-4b", qwenFile, 123, "http://127.0.0.1:8080/v1/chat/completions", 1);

        try
        {
            // Initial evaluations compute each model's fingerprint
            await capability.GetSupportAsync(gemmaSnapshot, CancellationToken.None);
            Assert.Equal(1, capability.FingerprintComputationCount);
            Assert.Equal(0, capability.CacheHitCount);

            await capability.GetSupportAsync(qwenSnapshot, CancellationToken.None);
            Assert.Equal(2, capability.FingerprintComputationCount);
            Assert.Equal(0, capability.CacheHitCount);

            // Re-evaluating Gemma hits Gemma's independent cache entry
            await capability.GetSupportAsync(gemmaSnapshot, CancellationToken.None);
            Assert.Equal(2, capability.FingerprintComputationCount);
            Assert.Equal(1, capability.CacheHitCount);

            // Re-evaluating Qwen hits Qwen's independent cache entry
            await capability.GetSupportAsync(qwenSnapshot, CancellationToken.None);
            Assert.Equal(2, capability.FingerprintComputationCount);
            Assert.Equal(2, capability.CacheHitCount);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private sealed class SwitchTestHarness : IDisposable
    {
        public readonly string Root = Path.Combine(Path.GetTempPath(), "nexus-switch-tests-" + Guid.NewGuid().ToString("N"));
        private readonly HttpClient _http;
        private ModelCatalogSnapshot _snapshot;
        private string? _runningModelPath;
        public ServerState State { get; private set; }
        public Action? OnStop { get; set; }
        public ModelCatalogItem GemmaModel { get; }
        public ModelCatalogItem QwenModel { get; }
        public string GemmaGgufPath { get; }
        public string QwenGgufPath { get; }
        public ControlledSwitchHandler Handler { get; } = new();
        public ConversationRuntimeWorkflow Workflow { get; }
        public SharedRuntimeAgent Agent { get; }
        private readonly List<ModelCatalogItem> _allModels = [];

        public SwitchTestHarness(bool realIdentity=false)
        {
            Directory.CreateDirectory(Root);
            GemmaGgufPath = realIdentity ? @"D:\NEXUS\Models\AI Workspace\Gemma-4-E4B\gemma-4-E4B-it-Q4_K_M.gguf" : Path.Combine(Root, "gemma-4-E4B-it-Q4_K_M.gguf");
            QwenGgufPath = realIdentity ? @"D:\NEXUS\Models\AI Workspace\Qwen3-4B\Qwen3-4B-Q4_K_M.gguf" : Path.Combine(Root, "Qwen3-4B-Q4_K_M.gguf");
            if(!realIdentity) { File.WriteAllBytes(GemmaGgufPath, [1]);File.WriteAllBytes(QwenGgufPath, [2]); }

            GemmaModel = new ModelCatalogItem("gemma-4-e4b", "Gemma 4 E4B", "gemma.json", GemmaGgufPath, ModelCatalogItemStatus.Valid, null, true);
            QwenModel = new ModelCatalogItem("qwen3-4b", "Qwen3 4B Instruct", "qwen.json", QwenGgufPath, ModelCatalogItemStatus.Valid, null, false);
            _allModels.Add(GemmaModel);
            _allModels.Add(QwenModel);
            _snapshot = new ModelCatalogSnapshot(GemmaModel.Id, _allModels);

            var catalog = new Mock<IModelCatalogService>();
            catalog.SetupGet(c => c.CurrentSnapshot).Returns(() => _snapshot);
            catalog.Setup(c => c.Load()).Returns(() => _snapshot);
            catalog.Setup(c => c.UpdateActiveModelAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns<string, CancellationToken>((id, _) =>
                {
                    _allModels.ForEach(m => {});
                    _snapshot = new ModelCatalogSnapshot(id, _allModels.Select(m => m with { IsActive = m.Id == id }).ToList());
                    return Task.FromResult(_snapshot);
                });

            var server = new Mock<ILlamaServerManager>();
            server.SetupGet(s => s.State).Returns(() => State);
            server.SetupGet(s => s.RuntimeModelPath).Returns(() => _runningModelPath);
            server.SetupGet(s => s.OwnedPid).Returns(() => State == ServerState.Ready ? 456 : null);
            server.Setup(s => s.IsServerHealthyAsync()).ReturnsAsync(true);
            server.Setup(s => s.ResolveEngineDir()).Returns(realIdentity ? @"D:\NEXUS\Engine\LlamaServer\b11057" : Path.Combine(Root, "b11057"));

            var engine = new Mock<IGenericLlamaEngine>();
            engine.SetupGet(e => e.Port).Returns(8080);
            engine.Setup(e => e.LoadEngineAsync()).Returns(Task.CompletedTask);
            engine.Setup(e => e.StartServerAsync(It.IsAny<string>(), It.IsAny<int>()))
                .Returns<string, int>((path, _) =>
                {
                    _runningModelPath = path;
                    State = ServerState.Ready;
                    return Task.FromResult(true);
                });
            engine.Setup(e => e.StopServerAsync()).Returns(() =>
            {
                OnStop?.Invoke();
                State = ServerState.Stopped;
                _runningModelPath = null;
                return Task.CompletedTask;
            });

            _http = new HttpClient(Handler);
            var policy = new ImageApplicationModelSelectionPolicy(new ModelRegistry(), new ModelMetadataParser(), Root, Path.Combine(Root, "unused.json"));
            var pipeline = new LlamaConversationPipeline(_http, policy, engine.Object);
            Workflow = new ConversationRuntimeWorkflow(catalog.Object, policy, engine.Object, pipeline, server.Object);

            var logger = new Mock<ILoggerService>();

            // Setup orchestrator with capability routing
            var toolRegistry = new ToolRegistry(realIdentity ? [new GetModelSnapshotTool(server.Object)] : [new DummyReadOnlyTool()]);
            var toolPolicy = new ReadOnlyToolPolicy([realIdentity ? GetModelSnapshotTool.ToolName : "dummy_tool"]);
            var toolExecutor = new ToolExecutor(toolRegistry, toolPolicy, logger.Object);
            var capability = new NativeToolCapability(server.Object);

            // In harness, support function delegates to capability or returns Supported for registered fixture models
            Func<AgentModelSnapshot, CancellationToken, Task<NativeToolSupport>> supportFunc = (snapshot, _) =>
            {
                if (snapshot.ModelId is "gemma-4-e4b" or "qwen3-4b")
                    return Task.FromResult(NativeToolSupport.Supported);
                return Task.FromResult(NativeToolSupport.Unknown);
            };

            var orchestrator = realIdentity
                ? new AgentOrchestrator(new NativeToolModelAdapter(_http),toolRegistry,toolPolicy,toolExecutor,capability,logger.Object)
                : new AgentOrchestrator(new NativeToolModelAdapter(_http), toolRegistry, toolPolicy, toolExecutor, supportFunc, logger.Object);
            Agent = new SharedRuntimeAgent(Workflow, server.Object, logger.Object, orchestrator);
        }

        public void AddCatalogModel(ModelCatalogItem model)
        {
            _allModels.Add(model);
            _snapshot = new ModelCatalogSnapshot(_snapshot.ActiveModelId, _allModels);
        }

        public void Dispose()
        {
            _http.Dispose();
            if (Directory.Exists(Root)) Directory.Delete(Root, true);
        }
    }

    private sealed class DummyReadOnlyTool : INexusTool
    {
        public ToolDescriptor Descriptor { get; } = new("dummy_tool", "Dummy read only tool", ToolRisk.ReadOnly, true,
            TimeSpan.FromSeconds(5), 2048, JsonSerializer.Deserialize<JsonElement>("{\"type\":\"object\",\"properties\":{},\"required\":[],\"additionalProperties\":false}"));
        public Task<ToolExecutionResult> ExecuteAsync(ToolExecutionContext context, JsonElement arguments, CancellationToken token)
            => Task.FromResult(new ToolExecutionResult(ToolOutcome.Success, JsonSerializer.SerializeToElement(new { dummy = true })));
    }

    private sealed class ControlledSwitchHandler : HttpMessageHandler
    {
        public bool NativeMode;
        public List<string> NativeBodies {get;}=[];
        public TaskCompletionSource AgentStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var body = await request.Content!.ReadAsStringAsync(token);
            if(NativeMode)
            {
                NativeBodies.Add(body);
                using var json=JsonDocument.Parse(body);
                var raw=json.RootElement.GetProperty("tools").GetArrayLength()>0
                    ? ToolProtocolTests.Response(name:GetModelSnapshotTool.ToolName,arguments:"{}")
                    : ToolProtocolTests.Response(JsonSerializer.Serialize(new {modelId="qwen3-4b",runtimeState="Ready"}),null);
                return new(HttpStatusCode.OK) {Content=new StringContent(raw)};
            }
            if (body.Contains("agent-block", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StreamContent(new BlockingStream(AgentStarted))
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("data: {\"choices\":[{\"delta\":{\"content\":\"pong\"},\"finish_reason\":\"stop\"}]}\n\ndata: [DONE]\n\n")
            };
        }
    }

    private sealed class BlockingStream(TaskCompletionSource started) : Stream
    {
        private readonly byte[] _first = Encoding.UTF8.GetBytes("data: {\"choices\":[{\"delta\":{\"content\":\"waiting\"}}]}\n\n");
        private bool _sent;
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
        {
            if (!_sent)
            {
                _sent = true;
                _first.AsMemory().CopyTo(buffer);
                started.TrySetResult();
                return _first.Length;
            }
            await Task.Delay(Timeout.Infinite, token);
            return 0;
        }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
