using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Nexus.AI.Llama.Models;
using Nexus.AI.Llama.Pipelines;
using Nexus.AI.Llama.Services;
using Nexus.App.Services;
using Nexus.Contracts;
using Nexus.Engine.ImageSubsystem.Services;
using Nexus.Foundation.Registry;
using Xunit;

namespace Nexus.Integration.Tests;

public sealed class ConversationRuntimeWorkflowTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _gemmaPath;
    private readonly string _qwenPath;

    public ConversationRuntimeWorkflowTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "Nexus_WorkflowTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _gemmaPath = Path.Combine(_tempDir, "gemma-2b.gguf");
        _qwenPath = Path.Combine(_tempDir, "qwen-4b.gguf");
        File.WriteAllBytes(_gemmaPath, [1, 2, 3]);
        File.WriteAllBytes(_qwenPath, [4, 5, 6]);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }
        catch
        {
            // Ignore cleanup failures in test temp
        }
    }

    private (ConversationRuntimeWorkflow workflow,
             Mock<IModelCatalogService> catalogMock,
             Mock<IGenericLlamaEngine> engineMock,
             Mock<ILlamaServerManager> serverMock)
        CreateWorkflow(
            ModelCatalogItem gemma,
            ModelCatalogItem qwen,
            Func<ServerState> getState,
            Func<string?> getRuntimePath,
            Func<bool> getIsHealthy)
    {
        var snapshot = new ModelCatalogSnapshot(gemma.Id, new[] { gemma, qwen });
        var catalogMock = new Mock<IModelCatalogService>();
        catalogMock.SetupGet(c => c.CurrentSnapshot).Returns(() => snapshot);
        catalogMock.Setup(c => c.Load()).Returns(() => snapshot);
        catalogMock.Setup(c => c.UpdateActiveModelAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<string, CancellationToken>((id, ct) =>
            {
                snapshot = new ModelCatalogSnapshot(id, new[]
                {
                    gemma with { IsActive = id == gemma.Id },
                    qwen with { IsActive = id == qwen.Id }
                });
                return Task.FromResult(snapshot);
            });

        var engineMock = new Mock<IGenericLlamaEngine>();
        engineMock.Setup(e => e.LoadEngineAsync()).Returns(Task.CompletedTask);
        engineMock.Setup(e => e.StartServerAsync(It.IsAny<string>(), It.IsAny<int>())).ReturnsAsync(true);
        engineMock.Setup(e => e.StopServerAsync()).Returns(Task.CompletedTask);

        var serverMock = new Mock<ILlamaServerManager>();
        serverMock.SetupGet(s => s.State).Returns(getState);
        serverMock.SetupGet(s => s.RuntimeModelPath).Returns(getRuntimePath);
        serverMock.Setup(s => s.IsServerHealthyAsync()).ReturnsAsync(getIsHealthy);

        var registry = new ModelRegistry();
        var dummyMeta = Path.Combine(_tempDir, "metadata.json");
        File.WriteAllText(dummyMeta, "{}");
        var policy = new ImageApplicationModelSelectionPolicy(
            registry,
            new ModelMetadataParser(),
            _tempDir,
            dummyMeta);

        var pipeline = new LlamaConversationPipeline(new HttpClient(), policy, engineMock.Object);

        var workflow = new ConversationRuntimeWorkflow(
            catalogMock.Object,
            policy,
            engineMock.Object,
            pipeline,
            serverMock.Object);

        return (workflow, catalogMock, engineMock, serverMock);
    }

    [Fact]
    public async Task StartActiveModelAsync_InitialStartup_StartsModelWith8192Context()
    {
        var gemma = new ModelCatalogItem("gemma-2b", "Gemma 2B", "gemma.json", _gemmaPath, ModelCatalogItemStatus.Valid, null, true);
        var qwen = new ModelCatalogItem("qwen-4b", "Qwen 4B", "qwen.json", _qwenPath, ModelCatalogItemStatus.Valid, null, false);
        var state = ServerState.Stopped;
        string? runtimePath = null;

        var (workflow, catalogMock, engineMock, _) = CreateWorkflow(
            gemma,
            qwen,
            () => state,
            () => runtimePath,
            () => true);

        Assert.False(workflow.IsReady);

        var active = await workflow.StartActiveModelAsync();

        Assert.Same(gemma, active);
        Assert.True(workflow.IsReady);
        engineMock.Verify(e => e.StartServerAsync(_gemmaPath, 8192), Times.Once);
        engineMock.Verify(e => e.StopServerAsync(), Times.Never);
    }

    [Fact]
    public async Task SwitchModelAsync_SameModelHealthyAndReady_DoesNotRestartServer()
    {
        var gemma = new ModelCatalogItem("gemma-2b", "Gemma 2B", "gemma.json", _gemmaPath, ModelCatalogItemStatus.Valid, null, true);
        var qwen = new ModelCatalogItem("qwen-4b", "Qwen 4B", "qwen.json", _qwenPath, ModelCatalogItemStatus.Valid, null, false);
        var state = ServerState.Ready;
        var runtimePath = _gemmaPath;

        var (workflow, catalogMock, engineMock, serverMock) = CreateWorkflow(
            gemma,
            qwen,
            () => state,
            () => runtimePath,
            () => true);

        // First, startup to mark IsReady
        await workflow.StartActiveModelAsync();
        engineMock.Invocations.Clear();

        // Switch to the same model
        var sameGemma = gemma with { };
        var result = await workflow.SwitchModelAsync(sameGemma);

        Assert.NotNull(result);
        Assert.Equal(gemma.Id, result.ActiveModelId);
        Assert.True(workflow.IsReady);

        // Fast path verification: NO stop, NO start, NO catalog update
        engineMock.Verify(e => e.StopServerAsync(), Times.Never);
        engineMock.Verify(e => e.StartServerAsync(It.IsAny<string>(), It.IsAny<int>()), Times.Never);
        catalogMock.Verify(c => c.UpdateActiveModelAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SwitchModelAsync_SameModelUnhealthy_RestartsServerToRecover()
    {
        var gemma = new ModelCatalogItem("gemma-2b", "Gemma 2B", "gemma.json", _gemmaPath, ModelCatalogItemStatus.Valid, null, true);
        var qwen = new ModelCatalogItem("qwen-4b", "Qwen 4B", "qwen.json", _qwenPath, ModelCatalogItemStatus.Valid, null, false);
        var state = ServerState.Ready;
        var runtimePath = _gemmaPath;
        bool isHealthy = true;

        var (workflow, catalogMock, engineMock, serverMock) = CreateWorkflow(
            gemma,
            qwen,
            () => state,
            () => runtimePath,
            () => isHealthy);

        // Initial startup
        await workflow.StartActiveModelAsync();
        engineMock.Invocations.Clear();

        // Server becomes unhealthy
        isHealthy = false;

        var result = await workflow.SwitchModelAsync(gemma);

        Assert.NotNull(result);
        Assert.Equal(gemma.Id, result.ActiveModelId);
        Assert.True(workflow.IsReady);

        // Recovery path verification: server stopped then restarted with 8192
        engineMock.Verify(e => e.StopServerAsync(), Times.Once);
        engineMock.Verify(e => e.StartServerAsync(_gemmaPath, 8192), Times.Once);
        catalogMock.Verify(c => c.UpdateActiveModelAsync(gemma.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SwitchModelAsync_DifferentModel_SwitchesNormally()
    {
        var gemma = new ModelCatalogItem("gemma-2b", "Gemma 2B", "gemma.json", _gemmaPath, ModelCatalogItemStatus.Valid, null, true);
        var qwen = new ModelCatalogItem("qwen-4b", "Qwen 4B", "qwen.json", _qwenPath, ModelCatalogItemStatus.Valid, null, false);
        var state = ServerState.Ready;
        var runtimePath = _gemmaPath;

        var (workflow, catalogMock, engineMock, serverMock) = CreateWorkflow(
            gemma,
            qwen,
            () => state,
            () => runtimePath,
            () => true);

        // Initial startup
        await workflow.StartActiveModelAsync();
        engineMock.Invocations.Clear();

        // Switch to different model (Qwen)
        var result = await workflow.SwitchModelAsync(qwen);

        Assert.NotNull(result);
        Assert.Equal(qwen.Id, result.ActiveModelId);
        Assert.True(workflow.IsReady);

        // Normal switch: server stopped, started with new model and 8192, catalog updated
        engineMock.Verify(e => e.StopServerAsync(), Times.Once);
        engineMock.Verify(e => e.StartServerAsync(_qwenPath, 8192), Times.Once);
        catalogMock.Verify(c => c.UpdateActiveModelAsync(qwen.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StartActiveModelAsync_WhenAlreadyHealthyAndReady_DoesNotRestart()
    {
        var gemma = new ModelCatalogItem("gemma-2b", "Gemma 2B", "gemma.json", _gemmaPath, ModelCatalogItemStatus.Valid, null, true);
        var qwen = new ModelCatalogItem("qwen-4b", "Qwen 4B", "qwen.json", _qwenPath, ModelCatalogItemStatus.Valid, null, false);
        var state = ServerState.Ready;
        var runtimePath = _gemmaPath;

        var (workflow, _, engineMock, _) = CreateWorkflow(
            gemma,
            qwen,
            () => state,
            () => runtimePath,
            () => true);

        await workflow.StartActiveModelAsync();
        engineMock.Invocations.Clear();

        // Calling StartActiveModelAsync again when healthy and ready
        var active = await workflow.StartActiveModelAsync();

        Assert.Same(gemma, active);
        engineMock.Verify(e => e.StartServerAsync(It.IsAny<string>(), It.IsAny<int>()), Times.Never);
        engineMock.Verify(e => e.StopServerAsync(), Times.Never);
    }
}
