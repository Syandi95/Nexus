using Nexus.Engine.ColorManagement;

namespace Nexus.Engine.ImageSubsystem.SavePipeline;

public sealed record OutputConfig
{
    public ImageOutputFormat Format { get; init; }
    public OutputColorSpace ColorSpace { get; init; } = OutputColorSpace.Rgb;
}
