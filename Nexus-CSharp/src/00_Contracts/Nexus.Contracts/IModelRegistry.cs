namespace Nexus.Contracts;

/// <summary>
/// Kontrak Single Source of Truth untuk pendaftaran dan pemetaan kapabilitas (AICapability)
/// menjadi deskriptor model fisik (ResolvedModelDescriptor).
/// </summary>
public interface IModelRegistry
{
    /// <summary>
    /// Menerjemahkan kapabilitas abstrak menjadi deskriptor model fisik.
    /// </summary>
    ResolvedModelDescriptor ResolveCapability(AICapability capability);

    /// <summary>
    /// Mendaftarkan deskriptor model AI ke dalam registry.
    /// </summary>
    void RegisterModel(AICapability capability, ResolvedModelDescriptor descriptor);

    /// <summary>
    /// Menentukan ID model default untuk kapabilitas tertentu.
    /// </summary>
    void SetDefaultModel(AICapability capability, string modelId);
}
