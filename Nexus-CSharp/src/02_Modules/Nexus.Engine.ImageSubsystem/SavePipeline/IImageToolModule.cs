using Nexus.Engine.ColorManagement;

namespace Nexus.Engine.ImageSubsystem.SavePipeline;

public interface IImageToolModule
{
    string GetFileSuffix(int? scale);

    OutputConfig BuildOutput(ImageOutputFormat? format, OutputColorSpace? colorSpace);

    IEnumerable<ImageOutputFormat> GetAllowedFormats(ColorSpaceKind colorSpace);

    bool SupportsColorSpace(ColorSpaceKind colorSpace);
}
