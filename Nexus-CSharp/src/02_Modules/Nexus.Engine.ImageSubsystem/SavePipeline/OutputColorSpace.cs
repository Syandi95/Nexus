using Nexus.Engine.ColorManagement;

namespace Nexus.Engine.ImageSubsystem.SavePipeline;

public sealed record OutputColorSpace
{
    public ColorSpaceKind Kind { get; init; }
    public string? IccProfilePath { get; init; }

    public static OutputColorSpace Rgb => new() { Kind = ColorSpaceKind.Rgb };
    public static OutputColorSpace Cmyk(string iccProfilePath) => new() { Kind = ColorSpaceKind.Cmyk, IccProfilePath = iccProfilePath };
}
