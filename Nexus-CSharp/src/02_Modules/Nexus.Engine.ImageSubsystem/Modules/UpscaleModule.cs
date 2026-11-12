using Nexus.Engine.ColorManagement;
using Nexus.Engine.ImageSubsystem.SavePipeline;

namespace Nexus.Engine.ImageSubsystem.Modules;

public class UpscaleModule : IImageToolModule
{
    public bool SupportsColorSpace(ColorSpaceKind colorSpace) => true;

    public IEnumerable<ImageOutputFormat> GetAllowedFormats(ColorSpaceKind colorSpace) =>
        colorSpace == ColorSpaceKind.Rgb
            ? new[] { ImageOutputFormat.Jpeg, ImageOutputFormat.Png }
            : new[] { ImageOutputFormat.Jpeg, ImageOutputFormat.Tiff };

    public string GetFileSuffix(int? scale) => $"_upscaled_{scale ?? 4}x";

    public OutputConfig BuildOutput(ImageOutputFormat? format, OutputColorSpace? colorSpace)
    {
        return new OutputConfig
        {
            Format = format ?? ImageOutputFormat.Jpeg,
            ColorSpace = colorSpace ?? OutputColorSpace.Rgb
        };
    }
}
