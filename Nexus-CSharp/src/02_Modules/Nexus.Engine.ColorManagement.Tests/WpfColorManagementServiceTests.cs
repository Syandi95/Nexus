using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using FluentAssertions;
using NSubstitute;
using Nexus.Engine.ColorManagement;
using Nexus.Foundation.Logging;
using Xunit;

namespace Nexus.Engine.ColorManagement.Tests;

public class WpfColorManagementServiceTests
{
    private readonly ILoggerService _logger;
    private readonly string _tempDir;

    public WpfColorManagementServiceTests()
    {
        _logger = Substitute.For<ILoggerService>();
        _tempDir = Path.Combine(Path.GetTempPath(), $"cms-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public void Constructor_WithNullLogger_ShouldThrow()
    {
        Action act = () => new WpfColorManagementService(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ExportToCmyk_WithMissingSource_ShouldThrowFileNotFound()
    {
        using var service = new WpfColorManagementService(_logger);
        Action act = () => service.ExportToCmyk("nonexistent.png", "out.tif", "profile.icc", ImageOutputFormat.Tiff);
        act.Should().Throw<FileNotFoundException>();
    }

    [Fact]
    public void ExportToCmyk_WithMissingProfile_ShouldThrowColorManagementException()
    {
        var sourcePath = CreateTestRgbImage("source.png");
        using var service = new WpfColorManagementService(_logger);
        Action act = () => service.ExportToCmyk(sourcePath, "out.tif", "nonexistent.icc", ImageOutputFormat.Tiff);
        act.Should().Throw<ColorManagementException>()
            .Which.ErrorCode.Should().Be(ColorManagementErrorCode.ProfileNotFound);
    }

    [Fact]
    public void ExportToCmyk_WithExistingOutputAndOverwriteFalse_ShouldThrow()
    {
        var sourcePath = CreateTestRgbImage("source.png");
        var outputPath = Path.Combine(_tempDir, "existing.tif");
        var profilePath = CopySystemSrgbProfile();
        File.WriteAllText(outputPath, "dummy");

        using var service = new WpfColorManagementService(_logger);
        Action act = () => service.ExportToCmyk(sourcePath, outputPath, profilePath, ImageOutputFormat.Tiff, overwrite: false);
        act.Should().Throw<IOException>();
    }

    [Fact]
    public void Dispose_ShouldNotThrow()
    {
        var service = new WpfColorManagementService(_logger);
        service.Dispose();
        service.Dispose(); // double dispose should not throw
    }

    private static string GetSystemSrgbProfilePath()
    {
        var system32 = Environment.GetFolderPath(Environment.SpecialFolder.System);
        return Path.Combine(system32, "spool", "drivers", "color", "sRGB Color Space Profile.icm");
    }

    private string CopySystemSrgbProfile()
    {
        var src = GetSystemSrgbProfilePath();
        var dst = Path.Combine(_tempDir, "srgb.icc");
        File.Copy(src, dst);
        return dst;
    }

    private string CreateTestRgbImage(string fileName)
    {
        var path = Path.Combine(_tempDir, fileName);
        using var bmp = new Bitmap(100, 100, PixelFormat.Format24bppRgb);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.Red);
        g.FillEllipse(Brushes.Blue, 10, 10, 80, 80);
        bmp.Save(path, ImageFormat.Png);
        return path;
    }
}
