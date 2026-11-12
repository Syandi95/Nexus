using Nexus.AI.Llama.Models;

namespace Nexus.AI.Llama.Services;

public interface IModelCatalogService
{
    ModelCatalogSnapshot? CurrentSnapshot { get; }

    ModelCatalogSnapshot Load();

    Task<ModelCatalogSnapshot> UpdateActiveModelAsync(
        string modelId,
        CancellationToken cancellationToken = default);

    Task<ModelCatalogSnapshot> SyncCatalogAsync(
        CancellationToken cancellationToken = default);

    Task<ModelCatalogSnapshot> AddModelAsync(
        string sourceGgufPath,
        string? customDisplayName = null,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default);

    Task<ModelCatalogSnapshot> DeleteModelAsync(
        string modelId,
        CancellationToken cancellationToken = default);

    Task<ModelCatalogSnapshot> ReorderModelAsync(
        string modelId,
        ModelOrderDirection direction,
        CancellationToken cancellationToken = default);
}

public enum ModelOrderDirection
{
    ToTop,
    Up,
    Down
}

