using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Nexus.Foundation.Logging;

namespace Nexus.Engine.ColorManagement;

public class WpfColorManagementService : IColorManagementService
{
    private static readonly Uri S_RgbProfileUri = new(GetSystemSrgbProfilePath());
    private readonly ILoggerService _logger;
    private bool _isDisposed;

    public WpfColorManagementService(ILoggerService logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public ColorExportResult ExportToCmyk(
        string rgbInputPath,
        string cmykOutputPath,
        string iccProfilePath,
        ImageOutputFormat format,
        bool overwrite = true,
        CancellationToken ct = default)
    {
        if (_isDisposed) throw new ObjectDisposedException(nameof(WpfColorManagementService));
        if (string.IsNullOrEmpty(rgbInputPath)) throw new ArgumentNullException(nameof(rgbInputPath));
        if (string.IsNullOrEmpty(cmykOutputPath)) throw new ArgumentNullException(nameof(cmykOutputPath));
        if (string.IsNullOrEmpty(iccProfilePath)) throw new ArgumentNullException(nameof(iccProfilePath));
        if (!File.Exists(rgbInputPath)) throw new FileNotFoundException("Source image not found", rgbInputPath);
        if (!File.Exists(iccProfilePath))
            throw new ColorManagementException(ColorManagementErrorCode.ProfileNotFound, iccProfilePath, "ICC profile file not found.");

        if (!overwrite && File.Exists(cmykOutputPath))
            throw new IOException($"Output file already exists: {cmykOutputPath}");

        if (!Directory.Exists(Path.GetDirectoryName(cmykOutputPath)))
            Directory.CreateDirectory(Path.GetDirectoryName(cmykOutputPath)!);

        ct.ThrowIfCancellationRequested();

        var sw = Stopwatch.StartNew();
        var profileName = Path.GetFileNameWithoutExtension(iccProfilePath);

        try
        {
            var source = BitmapFrame.Create(new Uri(rgbInputPath), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            ct.ThrowIfCancellationRequested();

            var srcContext = new ColorContext(S_RgbProfileUri);
            var dstContext = new ColorContext(new Uri(iccProfilePath));
            ct.ThrowIfCancellationRequested();

            var ccb = new ColorConvertedBitmap(source, srcContext, dstContext, PixelFormats.Cmyk32);
            ct.ThrowIfCancellationRequested();

            var iccBytes = File.ReadAllBytes(iccProfilePath);

            using var fileStream = new FileStream(cmykOutputPath, FileMode.Create, FileAccess.Write, FileShare.None);

            if (format == ImageOutputFormat.Jpeg)
            {
                var encoder = new JpegBitmapEncoder
                {
                    QualityLevel = 95
                };

                var cmykCtx = new ColorContext(new Uri(iccProfilePath));
                var frame = BitmapFrame.Create(ccb, null, null,
                    new ReadOnlyCollection<ColorContext>(new[] { cmykCtx }));
                encoder.Frames.Add(frame);
                encoder.Save(fileStream);
            }
            else
            {
                var metadata = new BitmapMetadata("tiff");
                metadata.SetQuery("/ifd/{uint=34675}", iccBytes);

                var encoder = new TiffBitmapEncoder
                {
                    Compression = TiffCompressOption.Lzw
                };

                var frame = BitmapFrame.Create(ccb, null, metadata, null);
                encoder.Frames.Add(frame);
                encoder.Save(fileStream);
            }

            sw.Stop();
            var fileSize = new FileInfo(cmykOutputPath).Length;

            _logger.LogInfo("CMYK_EXPORT",
                $"Profile={profileName}, Input={new FileInfo(rgbInputPath).Length >> 20}MB, " +
                $"Output={fileSize >> 20}MB, Duration={sw.ElapsedMilliseconds}ms, Success");

            return new ColorExportResult
            {
                OutputPath = cmykOutputPath,
                UsedProfilePath = iccProfilePath,
                ProfileWasEmbedded = true,
                FileSizeBytes = fileSize
            };
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            _logger.LogWarning("CMYK_EXPORT", $"Cancelled at {sw.ElapsedMilliseconds}ms. Partial output at {cmykOutputPath} — caller owns cleanup.");
            throw;
        }
        catch (FileNotFoundException)
        {
            throw;
        }
        catch (IOException)
        {
            throw;
        }
        catch (ColorManagementException)
        {
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            sw.Stop();
            _logger.LogError("CMYK_EXPORT", $"Failed: {ex.GetType().Name} — {ex.Message}. Profile={profileName}, Duration={sw.ElapsedMilliseconds}ms");
            throw new ColorManagementException(
                ColorManagementErrorCode.ConversionFailed,
                iccProfilePath,
                $"CMYK conversion failed: {ex.Message}", ex);
        }
    }

    private static string GetSystemSrgbProfilePath()
    {
        var system32 = Environment.GetFolderPath(Environment.SpecialFolder.System);
        var candidate = Path.Combine(system32, "spool", "drivers", "color", "sRGB Color Space Profile.icm");
        if (File.Exists(candidate)) return candidate;
        throw new ColorManagementException(ColorManagementErrorCode.ProfileNotFound, candidate,
            "System sRGB profile not found. Windows color management may be misconfigured.");
    }

    public void Dispose()
    {
        _isDisposed = true;
    }
}
