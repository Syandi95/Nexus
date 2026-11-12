namespace Nexus.Engine.ColorManagement;

public enum ColorSpaceKind
{
    Rgb,
    Cmyk
}

public enum ImageOutputFormat
{
    Jpeg,
    Png,
    Tiff
}

public class ColorExportResult
{
    public string OutputPath { get; init; } = string.Empty;
    public string UsedProfilePath { get; init; } = string.Empty;
    public bool ProfileWasEmbedded { get; init; }
    public long FileSizeBytes { get; init; }
}

public enum ColorManagementErrorCode
{
    ProfileNotFound,
    ProfileInvalid,
    ConversionFailed,
    FileWriteFailed
}

public class ColorManagementException : Exception
{
    public string ProfilePath { get; }
    public ColorManagementErrorCode ErrorCode { get; }

    public ColorManagementException(
        ColorManagementErrorCode errorCode,
        string profilePath,
        string message,
        Exception? inner = null)
        : base(message, inner)
    {
        ErrorCode = errorCode;
        ProfilePath = profilePath;
    }
}

public class CmykProfileInfo
{
    public string Id { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string FilePath { get; init; } = string.Empty;
    public bool IsBuiltIn { get; init; }
}
