using Nexus.Engine.ColorManagement;
using Nexus.Engine.ImageSubsystem.SavePipeline;

namespace Nexus.Engine.ImageSubsystem.Modules;

public class RemoveBackgroundModule : IImageToolModule
{
    public bool SupportsColorSpace(ColorSpaceKind colorSpace) => colorSpace == ColorSpaceKind.Rgb;

    public IEnumerable<ImageOutputFormat> GetAllowedFormats(ColorSpaceKind colorSpace) =>
        colorSpace == ColorSpaceKind.Rgb ? new[] { ImageOutputFormat.Png } : Array.Empty<ImageOutputFormat>();

    public string GetFileSuffix(int? scale) => "_removed";

    public OutputConfig BuildOutput(ImageOutputFormat? format, OutputColorSpace? colorSpace)
    {
        return new OutputConfig
        {
            Format = ImageOutputFormat.Png,
            ColorSpace = OutputColorSpace.Rgb
        };
    }
}
