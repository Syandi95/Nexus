namespace Nexus.Engine.ImageSubsystem.SavePipeline;

public sealed record SaveRequest
{
    public string SourcePath { get; init; } = string.Empty;
    public string DestDir { get; init; } = string.Empty;
    public string BaseName { get; init; } = string.Empty;
    public string FileSuffix { get; init; } = string.Empty;
    public OutputConfig Output { get; init; } = new();
    public EncoderOptions? Encoder { get; init; }
}
