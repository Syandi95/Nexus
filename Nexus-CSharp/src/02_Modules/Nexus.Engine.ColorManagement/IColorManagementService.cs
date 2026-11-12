using Nexus.Foundation.Logging;

namespace Nexus.Engine.ColorManagement;

public interface IColorManagementService : IDisposable
{
    /// <summary>Convert RGB image to CMYK with embedded ICC profile.
    /// Output format is specified by the caller (Jpeg or Tiff).
    /// Creates or overwrites output file per overwrite parameter.
    /// On cancellation the caller owns cleanup of partial output.</summary>
    ColorExportResult ExportToCmyk(
        string rgbInputPath,
        string cmykOutputPath,
        string iccProfilePath,
        ImageOutputFormat format,
        bool overwrite = true,
        CancellationToken ct = default);
}
