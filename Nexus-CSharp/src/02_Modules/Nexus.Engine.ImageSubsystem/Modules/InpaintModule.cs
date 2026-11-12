using Nexus.Engine.ColorManagement;
using Nexus.Engine.ImageSubsystem.SavePipeline;

namespace Nexus.Engine.ImageSubsystem.Modules;

public class InpaintModule : IImageToolModule
{
    public bool SupportsColorSpace(ColorSpaceKind colorSpace) => colorSpace == ColorSpaceKind.Rgb;

    public IEnumerable<ImageOutputFormat> GetAllowedFormats(ColorSpaceKind colorSpace) =>
        colorSpace == ColorSpaceKind.Rgb
            ? new[] { ImageOutputFormat.Png, ImageOutputFormat.Jpeg }
            : Array.Empty<ImageOutputFormat>();

    public string GetFileSuffix(int? scale) => "_inpainted";

    public OutputConfig BuildOutput(ImageOutputFormat? format, OutputColorSpace? colorSpace)
    {
        return new OutputConfig
        {
            Format = format ?? ImageOutputFormat.Png,
            ColorSpace = colorSpace ?? OutputColorSpace.Rgb
        };
    }
}
