using Nexus.Contracts;

namespace Nexus.Foundation.Registry;

/// <summary>
/// Kebijakan default yang mendelegasikan pemilihan descriptor ke model default registry.
/// </summary>
public sealed class DefaultModelSelectionPolicy : IModelSelectionPolicy
{
    private readonly IModelRegistry _modelRegistry;

    public DefaultModelSelectionPolicy(IModelRegistry modelRegistry)
    {
        _modelRegistry = modelRegistry ?? throw new ArgumentNullException(nameof(modelRegistry));
    }

    public ResolvedModelDescriptor SelectModel(
        AICapability capability,
        EngineSessionOptions sessionOptions)
    {
        ArgumentNullException.ThrowIfNull(sessionOptions);
        return _modelRegistry.ResolveCapability(capability);
    }
}
