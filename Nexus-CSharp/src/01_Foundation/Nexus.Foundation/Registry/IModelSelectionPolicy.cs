using Nexus.Contracts;

namespace Nexus.Foundation.Registry;

/// <summary>
/// Memilih descriptor model terdaftar untuk kapabilitas dan opsi session yang diminta.
/// </summary>
public interface IModelSelectionPolicy
{
    ResolvedModelDescriptor SelectModel(
        AICapability capability,
        EngineSessionOptions sessionOptions);
}
