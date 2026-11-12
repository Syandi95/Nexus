using System.IO;
using Nexus.AI.Llama.Models;
using Nexus.AI.Llama.Services;

namespace Nexus.App.Services;

internal sealed class LegacyConversationRuntimeWorkflow : IConversationRuntimeWorkflow
{
    private readonly ILlamaClientService _client;
    private readonly IModelCatalogService? _catalog;
    private readonly ILlamaServerManager? _server;

    public LegacyConversationRuntimeWorkflow(
        ILlamaClientService client,
        IModelCatalogService? catalog,
        ILlamaServerManager? server)
    {
        _client = client;
        _catalog = catalog;
        _server = server;
    }

    public bool IsReady =>
        _server is null || _server.State == ServerState.Ready;

    public async Task<ModelCatalogItem> StartActiveModelAsync()
    {
        var active = (_catalog?.CurrentSnapshot ?? _catalog?.Load())?.ActiveModel
            ?? throw new InvalidOperationException("Model AI tidak tersedia.");
        if (_server is not null &&
            !string.IsNullOrWhiteSpace(active.ResolvedGgufPath) &&
            _server.State != ServerState.Ready)
        {
            _server.Initialize(_server.ResolveEngineDir(), 8080);
            await _server.StartServerAsync(
                active.ResolvedGgufPath,
                "auto",
                8192,
                CancellationToken.None);
        }

        return active;
    }

    public async Task<ModelCatalogSnapshot> SwitchModelAsync(
        ModelCatalogItem requestedModel)
    {
        var snapshot = _catalog?.CurrentSnapshot
            ?? throw new InvalidOperationException(
                "Pengelolaan model AI tidak tersedia.");
        var target = snapshot.Models.FirstOrDefault(model =>
            string.Equals(model.Id, requestedModel.Id, StringComparison.Ordinal));
        if (target is null ||
            !target.IsAvailable ||
            string.IsNullOrWhiteSpace(target.ResolvedGgufPath))
        {
            throw new InvalidOperationException(
                target?.StatusMessage ?? "Model AI tidak tersedia.");
        }

        if (!File.Exists(target.ResolvedGgufPath))
        {
            throw new FileNotFoundException(
                "File model GGUF tidak ditemukan.",
                target.ResolvedGgufPath);
        }

        if (_server is null)
        {
            throw new InvalidOperationException(
                "Pengelolaan model AI tidak tersedia.");
        }

        if (_server.State == ServerState.Ready &&
            PathsEqual(_server.RuntimeModelPath, target.ResolvedGgufPath) &&
            await _server.IsServerHealthyAsync())
        {
            return snapshot;
        }

        await _server.StopServerAsync();
        await _server.StartServerAsync(
            target.ResolvedGgufPath,
            "auto",
            8192,
            CancellationToken.None);
        return await _catalog.UpdateActiveModelAsync(target.Id);
    }

    public IAsyncEnumerable<LlamaStreamChunk> StreamAsync(
        IEnumerable<ChatMessagePayload> messages,
        bool enableThinking,
        CancellationToken cancellationToken) =>
        enableThinking
            ? _client.StreamChatCompletionsAsync(
                messages,
                enableThinking: true,
                cancellationToken)
            : _client.StreamChatCompletionsAsync(messages, cancellationToken);

    public IAsyncEnumerable<LlamaStreamChunk> StreamAsync(
        IEnumerable<ChatMessagePayload> messages,
        bool enableThinking,
        LlamaGenerationOptions? options,
        CancellationToken cancellationToken) =>
        StreamAsync(messages, enableThinking, cancellationToken);

    public void CancelStreaming() => _client.CancelStreaming();

    public Task StopAsync() =>
        _server?.StopServerAsync() ?? Task.CompletedTask;

    private static bool PathsEqual(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) ||
            string.IsNullOrWhiteSpace(right))
        {
            return false;
        }

        return string.Equals(
            Path.GetFullPath(left),
            Path.GetFullPath(right),
            StringComparison.OrdinalIgnoreCase);
    }
}
