using Xunit;
using Nexus.PdfUtilities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SkiaSharp;

namespace Nexus.PdfUtilities.Tests;

public class PdfConversionServiceTests
{
    private readonly IPdfConversionService _pdfConversionService;

    public PdfConversionServiceTests()
    {
        _pdfConversionService = new PdfConversionService();
    }

    [Fact]
    public async Task ConvertAsync_ReplacesExistingOutputAfterEncoding_AndNoPartialFileRemains()
    {
        var dir=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
        try
        {
            var source=Path.Combine(dir,"source.pdf");CreateDummyPdf(source,1);
            var sourceBytes=await File.ReadAllBytesAsync(source);
            var output=Path.Combine(dir,"source_1.png");await File.WriteAllTextAsync(output,"keep existing output");
            await _pdfConversionService.ConvertAsync(source,dir,"png",72,100,CancellationToken.None);
            using var added=SKBitmap.Decode(output);Assert.NotNull(added);
            Assert.False(File.Exists(Path.Combine(dir,"source_1 (2).png")));
            Assert.Equal(sourceBytes,await File.ReadAllBytesAsync(source));
            Assert.Empty(Directory.GetFiles(dir,"*.partial"));
        }
        finally{Directory.Delete(dir,true);}
    }

    [Fact]
    public async Task ConvertAsync_CanceledBeforeCommit_PreservesExistingOutput()
    {
        var dir=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
        try
        {
            var source=Path.Combine(dir,"source.pdf");CreateDummyPdf(source,1);
            var output=Path.Combine(dir,"source_1.png");await File.WriteAllTextAsync(output,"previous completed output");
            using var cts=new CancellationTokenSource();
            bool reachedRender=false;
            var progress=new InlineProgress(report=>{if(report.CurrentPage==1&&string.IsNullOrEmpty(report.OutputFilePath)){reachedRender=true;cts.Cancel();}});
            await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>_pdfConversionService.ConvertAsync(source,dir,"png",72,100,0,null,progress,cts.Token));
            Assert.True(reachedRender);
            Assert.Equal("previous completed output",await File.ReadAllTextAsync(output));
            Assert.Empty(Directory.GetFiles(dir,"*.partial"));
        }
        finally{Directory.Delete(dir,true);}
    }

    private sealed class InlineProgress(Action<PdfConversionProgressReport> report) : IProgress<PdfConversionProgressReport>
    {
        public void Report(PdfConversionProgressReport value)=>report(value);
    }

    [Theory]
    [InlineData("png", 3)]
    [InlineData("jpg", 4)]
    public async Task PreflightAsync_UsesSelectedDpiFormatAndPagesWithoutCreatingOutput(string format, int multiplier)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(tempDir);
        var pdfPath = Path.Combine(tempDir, "preflight.pdf");
        CreateDummyPdf(pdfPath, 2);

        try
        {
            var result = await _pdfConversionService.PreflightAsync(
                pdfPath,
                format,
                144,
                new List<int> { 2 },
                CancellationToken.None);

            long rasterBytes = checked((long)result.WidthPixels * result.HeightPixels * 4L);
            Assert.Equal(2, result.HighestRiskPage);
            Assert.Equal(144, result.SelectedDpi);
            Assert.Equal(format, result.OutputFormat);
            Assert.Equal(checked(rasterBytes * multiplier), result.EstimatedMemoryBytes);
            Assert.Empty(Directory.GetFiles(tempDir, "*.png"));
            Assert.Empty(Directory.GetFiles(tempDir, "*.jpg"));
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task ConvertAsync_ValidPdf_GeneratesImages()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(tempDir);
        
        var testPdfPath = Path.Combine(tempDir, "test_convert.pdf");
        CreateDummyPdf(testPdfPath, 2); // 2 pages

        try
        {
            // Act
            await _pdfConversionService.ConvertAsync(testPdfPath, tempDir, "png", 72, 100, CancellationToken.None);

            // Assert
            var generatedFiles = Directory.GetFiles(tempDir, "*.png");
            Assert.Equal(2, generatedFiles.Length);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task ConvertAsync_TransparentPdf_UsesWhiteForJpegAndPreservesPngAlpha()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(tempDir);
        var testPdfPath = Path.Combine(tempDir, "transparent.pdf");
        CreateDummyPdf(testPdfPath, 1);

        try
        {
            await _pdfConversionService.ConvertAsync(testPdfPath, tempDir, "jpg", 72, 100, CancellationToken.None);
            await _pdfConversionService.ConvertAsync(testPdfPath, tempDir, "png", 72, 100, CancellationToken.None);

            using var jpeg = SKBitmap.Decode(Path.Combine(tempDir, "transparent_1.jpg"));
            using var png = SKBitmap.Decode(Path.Combine(tempDir, "transparent_1.png"));
            var jpegPixel = jpeg.GetPixel(jpeg.Width / 2, jpeg.Height / 2);
            var pngPixel = png.GetPixel(png.Width / 2, png.Height / 2);

            Assert.True(
                jpegPixel.Red >= 250 && jpegPixel.Green >= 250 && jpegPixel.Blue >= 250,
                $"Expected a white JPEG background, but found {jpegPixel}.");
            Assert.Equal((byte)0, pngPixel.Alpha);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task ConvertAsync_10Pages_ReportsProgressFromStartTo100()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(tempDir);
        var testPdfPath = Path.Combine(tempDir, "test_10pages.pdf");
        CreateDummyPdf(testPdfPath, 10);

        var reportedPercents = new System.Collections.Concurrent.ConcurrentBag<double>();
        var progress = new System.Progress<PdfConversionProgressReport>(r =>
        {
            reportedPercents.Add(r.Percent);
        });

        try
        {
            await _pdfConversionService.ConvertAsync(
                testPdfPath,
                tempDir,
                "png",
                72,
                100,
                0,
                null,
                progress,
                CancellationToken.None);

            var files = Directory.GetFiles(tempDir, "*.png");
            Assert.Equal(10, files.Length);
            Assert.NotEmpty(reportedPercents);
            Assert.All(reportedPercents, p => Assert.InRange(p, 0.0, 100.0));
            Assert.Contains(100.0, reportedPercents);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task ConvertAsync_ConcurrentlyWithExtractTextAndEngine_CompletesSuccessfully()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(tempDir);
        var testPdfPath = Path.Combine(tempDir, "test_convert.pdf");
        var otherPdfPath = Path.Combine(tempDir, "test_other.pdf");
        CreateDummyPdf(testPdfPath, 2);
        CreateDummyPdf(otherPdfPath, 2);

        var engine = new PdfEngineService();

        try
        {
            var convertTask = _pdfConversionService.ConvertAsync(
                testPdfPath,
                tempDir,
                "png",
                72,
                100,
                CancellationToken.None);

            var textTask = engine.ExtractTextAsync(otherPdfPath);
            var pageCount = engine.GetPageCount(otherPdfPath);

            await Task.WhenAll(convertTask, textTask);

            Assert.Equal(2, pageCount);
            Assert.True(File.Exists(Path.Combine(tempDir, "test_convert_1.png")));
            Assert.True(File.Exists(Path.Combine(tempDir, "test_convert_2.png")));
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void Architecture_PdfEngineService_DoesNotExposeNativePdfiumLock()
    {
        var lockField = typeof(PdfEngineService).GetField("NativePdfiumLock", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.Null(lockField);
    }

    [Fact]
    public async Task ConvertAsync_ConcurrentConversions_CompleteSuccessfullyWithoutRaceOrCorruption()
    {
        var tempDir1 = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var tempDir2 = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(tempDir1);
        Directory.CreateDirectory(tempDir2);

        var testPdf1 = Path.Combine(tempDir1, "doc1.pdf");
        var testPdf2 = Path.Combine(tempDir2, "doc2.pdf");
        CreateDummyPdf(testPdf1, 3);
        CreateDummyPdf(testPdf2, 3);

        try
        {
            var task1 = _pdfConversionService.ConvertAsync(testPdf1, tempDir1, "png", 72, 100, CancellationToken.None);
            var task2 = _pdfConversionService.ConvertAsync(testPdf2, tempDir2, "jpg", 72, 100, CancellationToken.None);

            await Task.WhenAll(task1, task2);

            var files1 = Directory.GetFiles(tempDir1, "*.png");
            var files2 = Directory.GetFiles(tempDir2, "*.jpg");

            Assert.Equal(3, files1.Length);
            Assert.Equal(3, files2.Length);
        }
        finally
        {
            if (Directory.Exists(tempDir1)) Directory.Delete(tempDir1, true);
            if (Directory.Exists(tempDir2)) Directory.Delete(tempDir2, true);
        }
    }

    [Fact]
    public async Task ConvertAsync_AllowsOtherPdfEngineOperationsConcurrently()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(tempDir);
        var testPdfPath = Path.Combine(tempDir, "multi_page.pdf");
        CreateDummyPdf(testPdfPath, 5);

        var otherPdfPath = Path.Combine(tempDir, "other.pdf");
        CreateDummyPdf(otherPdfPath, 2);

        var engine = new PdfEngineService();

        try
        {
            // Start a multi-page conversion
            var convertTask = _pdfConversionService.ConvertAsync(
                testPdfPath,
                tempDir,
                "jpg",
                72,
                80,
                CancellationToken.None);

            // While conversion is running, concurrent engine operations complete successfully
            var count1 = engine.GetPageCount(otherPdfPath);
            var thumb = engine.GetPageImage(otherPdfPath, 100);
            var count2 = engine.GetPageCount(testPdfPath);

            Assert.Equal(2, count1);
            Assert.NotNull(thumb);
            Assert.Equal(5, count2);

            await convertTask;

            var files = Directory.GetFiles(tempDir, "multi_page_*.jpg");
            Assert.Equal(5, files.Length);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    private void CreateDummyPdf(string path, int pageCount)
    {
        using var document = new PdfSharp.Pdf.PdfDocument();
        for (int i = 0; i < pageCount; i++)
        {
            document.AddPage();
        }
        document.Save(path);
    }
}
