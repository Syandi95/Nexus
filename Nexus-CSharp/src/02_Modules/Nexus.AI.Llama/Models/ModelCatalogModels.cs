namespace Nexus.AI.Llama.Models;

public enum ModelCatalogItemStatus
{
    Valid,
    MetadataNotFound,
    MetadataInvalid,
    MetadataVersionUnsupported,
    IdMismatch,
    GgufPathInvalid,
    GgufNotFound
}

public enum ModelCatalogErrorCode
{
    RootNotFound,
    IndexNotFound,
    IndexInvalid,
    IndexVersionUnsupported,
    ActiveModelNotRegistered
}

public sealed class ModelCatalogException : Exception
{
    public ModelCatalogException(
        ModelCatalogErrorCode code,
        string userMessage,
        Exception? innerException = null)
        : base(userMessage, innerException)
    {
        Code = code;
        UserMessage = userMessage;
    }

    public ModelCatalogErrorCode Code { get; }

    public string UserMessage { get; }
}

public sealed record ModelCatalogItem(
    string Id,
    string DisplayName,
    string MetadataFile,
    string? ResolvedGgufPath,
    ModelCatalogItemStatus Status,
    string? StatusMessage,
    bool IsActive)
{
    public bool IsAvailable => Status == ModelCatalogItemStatus.Valid;
}

public sealed record ModelCatalogSnapshot(
    string? ActiveModelId,
    IReadOnlyList<ModelCatalogItem> Models)
{
    public ModelCatalogItem? ActiveModel =>
        ActiveModelId is null
            ? null
            : Models.FirstOrDefault(model =>
                string.Equals(model.Id, ActiveModelId, StringComparison.Ordinal));
}
