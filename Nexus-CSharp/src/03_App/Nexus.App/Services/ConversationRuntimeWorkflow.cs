using System.IO;
using Nexus.AI.Llama.Models;
using Nexus.AI.Llama.Pipelines;
using Nexus.AI.Llama.Services;
using Nexus.Contracts;
using System.Runtime.CompilerServices;

namespace Nexus.App.Services;

public interface IConversationRuntimeWorkflow
{
    bool IsReady { get; }

    Task<ModelCatalogItem> StartActiveModelAsync();

    Task<ModelCatalogSnapshot> SwitchModelAsync(ModelCatalogItem requestedModel);

    IAsyncEnumerable<LlamaStreamChunk> StreamAsync(
        IEnumerable<ChatMessagePayload> messages,
        bool enableThinking,
        CancellationToken cancellationToken);

    IAsyncEnumerable<LlamaStreamChunk> StreamAsync(
        IEnumerable<ChatMessagePayload> messages,
        bool enableThinking,
        LlamaGenerationOptions? options,
        CancellationToken cancellationToken);

    void CancelStreaming();

    Task StopAsync();
}

public sealed class ConversationRuntimeWorkflow : IConversationRuntimeWorkflow
{
    private readonly IModelCatalogService _modelCatalog;
    private readonly ImageApplicationModelSelectionPolicy _modelSelectionPolicy;
    private readonly IGenericLlamaEngine _llamaEngine;
    private readonly LlamaConversationPipeline _conversationPipeline;
    private readonly ILlamaServerManager? _serverManager;
    private readonly SemaphoreSlim _lifecycleLock = new(1, 1);
    private readonly object _consumerSync = new();
    private RuntimeConsumerLease? _additionalConsumer;
    private bool _acceptAdditionalConsumer;

    public ConversationRuntimeWorkflow(
        IModelCatalogService modelCatalog,
        ImageApplicationModelSelectionPolicy modelSelectionPolicy,
        IGenericLlamaEngine llamaEngine,
        LlamaConversationPipeline conversationPipeline,
        ILlamaServerManager? serverManager = null)
    {
        _modelCatalog = modelCatalog ?? throw new ArgumentNullException(nameof(modelCatalog));
        _modelSelectionPolicy = modelSelectionPolicy
            ?? throw new ArgumentNullException(nameof(modelSelectionPolicy));
        _llamaEngine = llamaEngine ?? throw new ArgumentNullException(nameof(llamaEngine));
        _conversationPipeline = conversationPipeline
            ?? throw new ArgumentNullException(nameof(conversationPipeline));
        _serverManager = serverManager;
    }

    public bool IsReady { get; private set; }

    public async Task<ModelCatalogItem> StartActiveModelAsync()
    {
        await _lifecycleLock.WaitAsync().ConfigureAwait(false);
        try
        {
            var snapshot = _modelCatalog.CurrentSnapshot ?? _modelCatalog.Load();
            var activeModel = RequireAvailableModel(snapshot.ActiveModel);
            if (await IsHealthySameModelAsync(snapshot, activeModel).ConfigureAwait(false))
            {
                return activeModel;
            }

            await CancelAdditionalConsumerAsync().ConfigureAwait(false);
            await StartModelCoreAsync(activeModel).ConfigureAwait(false);
            return activeModel;
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    public async Task<ModelCatalogSnapshot> SwitchModelAsync(
        ModelCatalogItem requestedModel)
    {
        ArgumentNullException.ThrowIfNull(requestedModel);
        await _lifecycleLock.WaitAsync().ConfigureAwait(false);
        try
        {
            var snapshot = _modelCatalog.CurrentSnapshot ?? _modelCatalog.Load();
            var target = snapshot.Models.FirstOrDefault(model =>
                string.Equals(model.Id, requestedModel.Id, StringComparison.Ordinal));
            target = RequireAvailableModel(target);

            if (await IsHealthySameModelAsync(snapshot, target).ConfigureAwait(false))
            {
                if (string.Equals(snapshot.ActiveModel?.Id, target.Id, StringComparison.Ordinal))
                {
                    return snapshot;
                }

                return await _modelCatalog
                    .UpdateActiveModelAsync(target.Id)
                    .ConfigureAwait(false);
            }

            await CancelAdditionalConsumerAsync().ConfigureAwait(false);
            IsReady = false;
            await _llamaEngine.StopServerAsync().ConfigureAwait(false);
            try
            {
                await StartModelCoreAsync(target).ConfigureAwait(false);
                return await _modelCatalog
                    .UpdateActiveModelAsync(target.Id)
                    .ConfigureAwait(false);
            }
            catch
            {
                await StopCoreAsync().ConfigureAwait(false);
                throw;
            }
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    public IAsyncEnumerable<LlamaStreamChunk> StreamAsync(
        IEnumerable<ChatMessagePayload> messages,
        bool enableThinking,
        CancellationToken cancellationToken) =>
        StreamAsync(messages, enableThinking, null, cancellationToken);

    public IAsyncEnumerable<LlamaStreamChunk> StreamAsync(
        IEnumerable<ChatMessagePayload> messages,
        bool enableThinking,
        LlamaGenerationOptions? options,
        CancellationToken cancellationToken)
    {
        if (!IsReady)
        {
            throw new InvalidOperationException("Model AI belum siap.");
        }

        return _conversationPipeline.StreamAsync(
            messages,
            enableThinking,
            generationOptions: options,
            cancellationToken: cancellationToken);
    }

    public async Task StopAsync()
    {
        await _lifecycleLock.WaitAsync().ConfigureAwait(false);
        try
        {
            await StopCoreAsync().ConfigureAwait(false);
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    public void CancelStreaming()
    {
        // Streaming dihentikan melalui CancellationToken milik ViewModel.
    }

    private async Task StartModelCoreAsync(ModelCatalogItem model)
    {
        _modelSelectionPolicy.UseChatModel(
            model.Id,
            model.ResolvedGgufPath!,
            model.DisplayName);
        await _llamaEngine.LoadEngineAsync().ConfigureAwait(false);
        IsReady = await _llamaEngine
            .StartServerAsync(model.ResolvedGgufPath!, 8192)
            .ConfigureAwait(false);
        if (!IsReady)
        {
            throw new InvalidOperationException(
                $"Runtime tidak siap untuk model '{model.DisplayName}'.");
        }
        lock(_consumerSync) _acceptAdditionalConsumer = true;
    }

    private async Task StopCoreAsync()
    {
        await CancelAdditionalConsumerAsync().ConfigureAwait(false);
        try
        {
            await _llamaEngine.StopServerAsync().ConfigureAwait(false);
        }
        finally
        {
            IsReady = false;
        }
    }

    internal RuntimeConsumerLease AcquireAdditionalConsumer()
    {
        lock(_consumerSync)
        {
            var model = _modelCatalog.CurrentSnapshot?.ActiveModel;
            if(!_acceptAdditionalConsumer || !IsReady || _additionalConsumer is not null ||
               model is null || _serverManager?.State!=ServerState.Ready ||
               !PathsEqual(_serverManager.RuntimeModelPath,model.ResolvedGgufPath))
                throw new InvalidOperationException("Active workspace runtime unavailable or proof consumer already running.");
            var lease = new RuntimeConsumerLease(model,_serverManager.OwnedPid,
                RuntimeHelpers.GetHashCode(_serverManager),$"http://127.0.0.1:{_llamaEngine.Port}/v1/chat/completions",ReleaseConsumer);
            _additionalConsumer = lease;
            return lease;
        }
    }

    private void ReleaseConsumer(RuntimeConsumerLease lease)
    {
        lock(_consumerSync)
            if(ReferenceEquals(_additionalConsumer,lease)) _additionalConsumer=null;
    }

    private async Task CancelAdditionalConsumerAsync()
    {
        RuntimeConsumerLease? lease;
        lock(_consumerSync)
        {
            _acceptAdditionalConsumer=false;
            IsReady=false;
            lease=_additionalConsumer;
        }
        // Revoke admission before cancellation, then drain the existing request
        // before killing/switching the model. Cancellation never touches Chat CTS.
        lease?.Cancel();
        if(lease is not null) await lease.Completed.ConfigureAwait(false);
    }

    private static ModelCatalogItem RequireAvailableModel(ModelCatalogItem? model)
    {
        if (model is null ||
            !model.IsAvailable ||
            string.IsNullOrWhiteSpace(model.ResolvedGgufPath))
        {
            throw new InvalidOperationException(
                model?.StatusMessage ?? "Model AI tidak tersedia.");
        }

        if (!File.Exists(model.ResolvedGgufPath))
        {
            throw new FileNotFoundException(
                "File model GGUF tidak ditemukan.",
                model.ResolvedGgufPath);
        }

        return model;
    }

    private async Task<bool> IsHealthySameModelAsync(
        ModelCatalogSnapshot snapshot,
        ModelCatalogItem target)
    {
        if (!IsReady)
        {
            return false;
        }

        var runtimePath = _serverManager?.RuntimeModelPath ?? _llamaEngine.ActiveModelPath;
        if (!PathsEqual(runtimePath, target.ResolvedGgufPath))
        {
            return false;
        }

        if (_serverManager is not null)
        {
            if (_serverManager.State != ServerState.Ready)
            {
                return false;
            }

            return await _serverManager.IsServerHealthyAsync().ConfigureAwait(false);
        }

        return true;
    }

    private static bool PathsEqual(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) ||
            string.IsNullOrWhiteSpace(right))
        {
            return false;
        }

        try
        {
            return string.Equals(
                Path.GetFullPath(left),
                Path.GetFullPath(right),
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}
