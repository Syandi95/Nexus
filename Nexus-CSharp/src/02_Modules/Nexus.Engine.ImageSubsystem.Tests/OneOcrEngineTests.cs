using System;
using System.Drawing;
using System.IO;
using System.Linq;
using Nexus.Engine.ImageSubsystem.Ocr;
using Xunit;
using Xunit.Abstractions;

namespace Nexus.Engine.ImageSubsystem.Tests;

public class OneOcrEngineTests
{
    private readonly ITestOutputHelper _output;

    private const string BenchmarkDir = @"D:\NEXUS\test\OcrBenchmark";
    private static readonly string DocumentPath = Path.Combine(BenchmarkDir, "5_document_indonesian.png");
    private static readonly string BannerPath = Path.Combine(BenchmarkDir, "3_banner_poster.png");
    private static readonly string ScreenshotPath = Path.Combine(BenchmarkDir, "2_ui_screenshot.png");

    public OneOcrEngineTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void Test_OneOcrEngine_InitializesAndDisposesCleanly()
    {
        using var engine = new OneOcrEngine();
        Assert.NotNull(engine);
        _output.WriteLine("OneOcrEngine initialized and disposed cleanly.");
    }

    [Fact]
    public void Test_OneOcrEngine_Disposed_ThrowsObjectDisposedException()
    {
        var engine = new OneOcrEngine();
        engine.Dispose();

        using var bmp = new Bitmap(100, 100);
        Assert.Throws<ObjectDisposedException>(() => engine.RunPipeline(bmp));
    }

    [Fact]
    public void Test_OneOcrEngine_Document_Indonesian_WordLevelGeometry()
    {
        if (!File.Exists(DocumentPath))
        {
            _output.WriteLine($"Skipping test: {DocumentPath} not found.");
            return;
        }

        using var engine = new OneOcrEngine();
        using var bmp = new Bitmap(DocumentPath);

        var doc = engine.RunPipeline(bmp);

        _output.WriteLine($"Document OCR completed in {doc.InferenceMs} ms.");
        _output.WriteLine($"Lines: {doc.Lines.Count}, Words: {doc.AllWords.Count}, Angle: {doc.ImageAngle:F2}");

        Assert.True(doc.Lines.Count > 0, "Lines count must be > 0");
        Assert.True(doc.AllWords.Count > 0, "Words count must be > 0");
        Assert.False(string.IsNullOrWhiteSpace(doc.FullText), "Full text must not be empty");

        // Verify word geometries
        foreach (var word in doc.AllWords)
        {
            Assert.False(string.IsNullOrWhiteSpace(word.Text), "Word text must not be empty");
            Assert.True(word.Confidence > 0, $"Word confidence should be > 0 for '{word.Text}', got {word.Confidence}");
            Assert.True(word.Quad.Width > 0, $"Word quad width must be > 0 for '{word.Text}'");
            Assert.True(word.Quad.Height > 0, $"Word quad height must be > 0 for '{word.Text}'");
        }

        // Print first 5 words
        for (int i = 0; i < Math.Min(5, doc.AllWords.Count); i++)
        {
            var w = doc.AllWords[i];
            _output.WriteLine($"Word[{i}]: '{w.Text}' (Conf: {w.Confidence:F2}) Quad: [({w.Quad.X1:F1},{w.Quad.Y1:F1}) -> ({w.Quad.X3:F1},{w.Quad.Y3:F1})]");
        }
    }

    [Fact]
    public void Test_OneOcrEngine_BannerPoster_WordLevelGeometry()
    {
        if (!File.Exists(BannerPath))
        {
            _output.WriteLine($"Skipping test: {BannerPath} not found.");
            return;
        }

        using var engine = new OneOcrEngine();
        using var bmp = new Bitmap(BannerPath);

        var doc = engine.RunPipeline(bmp);

        _output.WriteLine($"Banner OCR completed in {doc.InferenceMs} ms.");
        _output.WriteLine($"Lines: {doc.Lines.Count}, Words: {doc.AllWords.Count}");

        Assert.True(doc.Lines.Count > 0);
        Assert.True(doc.AllWords.Count > 0);

        foreach (var word in doc.AllWords)
        {
            Assert.True(word.Quad.Width > 0);
            Assert.True(word.Quad.Height > 0);
        }
    }

    [Fact]
    public void Test_OneOcrEngine_Screenshot_WordLevelGeometry()
    {
        if (!File.Exists(ScreenshotPath))
        {
            _output.WriteLine($"Skipping test: {ScreenshotPath} not found.");
            return;
        }

        using var engine = new OneOcrEngine();
        using var bmp = new Bitmap(ScreenshotPath);

        var doc = engine.RunPipeline(bmp);

        _output.WriteLine($"Screenshot OCR completed in {doc.InferenceMs} ms.");
        _output.WriteLine($"Lines: {doc.Lines.Count}, Words: {doc.AllWords.Count}");

        Assert.True(doc.Lines.Count > 0);
        Assert.True(doc.AllWords.Count > 0);
    }

    [Fact]
    public void Test_OcrQuad_ContainsPoint_HitTesting()
    {
        var quad = new OcrQuad(10, 10, 100, 10, 100, 50, 10, 50);

        // Inside
        Assert.True(quad.ContainsPoint(50, 30));
        Assert.True(quad.ContainsPoint(11, 11));
        Assert.True(quad.ContainsPoint(99, 49));

        // Outside
        Assert.False(quad.ContainsPoint(5, 5));
        Assert.False(quad.ContainsPoint(150, 30));
        Assert.False(quad.ContainsPoint(50, 70));
    }
}
