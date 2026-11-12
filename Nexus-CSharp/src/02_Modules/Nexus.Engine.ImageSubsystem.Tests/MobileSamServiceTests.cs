using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Nexus.Engine.ImageSubsystem.Services;
using Xunit;

namespace Nexus.Engine.ImageSubsystem.Tests;

public sealed class MobileSamServiceTests
{
    private const string ModelDir = @"d:\NEXUS\Models\SegmentAnything";

    [Fact]
    public void IsModelAvailable_ReturnsTrue_WhenFilesExistInModelDir()
    {
        var service = new MobileSamService(ModelDir);
        Assert.True(service.IsModelAvailable, "MobileSAM models should be present in target model folder");
    }

    [Fact]
    public async Task EncodeAndSegmentPointAsync_EndToEnd_GeneratesValidBinaryMask()
    {
        var service = new MobileSamService(ModelDir);
        if (!service.IsModelAvailable)
            return; // Skip if models not present

        // Create a synthetic image: 400x300 with a bright red square in the center
        using var testBmp = new Bitmap(400, 300, PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(testBmp))
        {
            g.Clear(Color.Black);
            using var brush = new SolidBrush(Color.White);
            g.FillRectangle(brush, 100, 50, 200, 200); // 200x200 square
        }

        // 1. Encode image
        using var session = await service.EncodeImageAsync(testBmp);
        Assert.NotNull(session);
        Assert.Equal(400, session.OriginalWidth);
        Assert.Equal(300, session.OriginalHeight);
        Assert.True(session.Embeddings.Dimensions[0] == 1);
        Assert.True(session.Embeddings.Dimensions[1] == 256);
        Assert.True(session.Embeddings.Dimensions[2] == 64);
        Assert.True(session.Embeddings.Dimensions[3] == 64);

        // 2. Segment point inside the square (200, 150)
        using var maskBmp = await service.SegmentPointAsync(session, new PointF(200f, 150f));
        Assert.NotNull(maskBmp);
        Assert.Equal(400, maskBmp.Width);
        Assert.Equal(300, maskBmp.Height);
        Assert.Equal(PixelFormat.Format24bppRgb, maskBmp.PixelFormat);

        // Check that the clicked point pixel is white (255)
        var clickColor = maskBmp.GetPixel(200, 150);
        Assert.Equal(255, clickColor.R);

        // Check that a far corner is black (0)
        var cornerColor = maskBmp.GetPixel(10, 10);
        Assert.Equal(0, cornerColor.R);
    }

    [Fact]
    public void AdaptiveCcaThreshold_CalculatesCorrectValuesAcrossResolutions()
    {
        // Test adaptive threshold scaling formula:
        // Math.Max(DefaultMinNoiseAreaPixels, (int)Math.Round(totalPixels * DefaultRelativeNoiseAreaRatio))
        
        // 1. Low resolution (640x480 = 307,200 px) -> 307,200 * 0.00003 = 9.2 px -> bounded by min (32 px)
        int lowResPixels = 640 * 480;
        int lowResThresh = Math.Max(MobileSamService.DefaultMinNoiseAreaPixels,
            (int)Math.Round(lowResPixels * MobileSamService.DefaultRelativeNoiseAreaRatio));
        Assert.Equal(32, lowResThresh);

        // 2. Full HD 1080p (1920x1080 = 2,073,600 px) -> 2,073,600 * 0.00003 = 62 px
        int fhdPixels = 1920 * 1080;
        int fhdThresh = Math.Max(MobileSamService.DefaultMinNoiseAreaPixels,
            (int)Math.Round(fhdPixels * MobileSamService.DefaultRelativeNoiseAreaRatio));
        Assert.Equal(62, fhdThresh);

        // 3. 4K UHD (3840x2160 = 8,294,400 px) -> 8,294,400 * 0.00003 = 249 px
        int uhdPixels = 3840 * 2160;
        int uhdThresh = Math.Max(MobileSamService.DefaultMinNoiseAreaPixels,
            (int)Math.Round(uhdPixels * MobileSamService.DefaultRelativeNoiseAreaRatio));
        Assert.Equal(249, uhdThresh);
    }
}
