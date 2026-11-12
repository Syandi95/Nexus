using Nexus.Contracts;

namespace Nexus.Foundation.Registry;

/// <summary>
/// Registry in-memory untuk pemetaan kapabilitas AI ke deskriptor model
/// yang diregistrasikan oleh composition root.
/// </summary>
public sealed class ModelRegistry : IModelRegistry
{
    private readonly object _syncRoot = new();
    private readonly Dictionary<AICapability, Dictionary<string, ResolvedModelDescriptor>> _models = new();
    private readonly Dictionary<AICapability, string> _defaultModelIds = new();

    public void RegisterModel(AICapability capability, ResolvedModelDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        if (string.IsNullOrWhiteSpace(descriptor.ModelId))
        {
            throw new ArgumentException("ModelId is required.", nameof(descriptor));
        }

        if (string.IsNullOrWhiteSpace(descriptor.ModelPath))
        {
            throw new ArgumentException("ModelPath is required.", nameof(descriptor));
        }

        lock (_syncRoot)
        {
            if (!_models.TryGetValue(capability, out var modelsForCapability))
            {
                modelsForCapability = new Dictionary<string, ResolvedModelDescriptor>(StringComparer.Ordinal);
                _models.Add(capability, modelsForCapability);
            }

            modelsForCapability[descriptor.ModelId] = descriptor;
            _defaultModelIds.TryAdd(capability, descriptor.ModelId);
        }
    }

    public ResolvedModelDescriptor ResolveCapability(AICapability capability)
    {
        lock (_syncRoot)
        {
            if (!_defaultModelIds.TryGetValue(capability, out var defaultModelId) ||
                !_models.TryGetValue(capability, out var modelsForCapability) ||
                !modelsForCapability.TryGetValue(defaultModelId, out var descriptor))
            {
                throw new InvalidOperationException($"No model is registered for capability '{capability}'.");
            }

            return descriptor;
        }
    }

    public void SetDefaultModel(AICapability capability, string modelId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);

        lock (_syncRoot)
        {
            if (!_models.TryGetValue(capability, out var modelsForCapability) ||
                !modelsForCapability.ContainsKey(modelId))
            {
                throw new InvalidOperationException(
                    $"Model '{modelId}' is not registered for capability '{capability}'.");
            }

            _defaultModelIds[capability] = modelId;
        }
    }
}
