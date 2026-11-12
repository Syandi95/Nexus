using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using Docnet.Core;
using Docnet.Core.Models;
using Nexus.PdfUtilities;
using Xunit;
using Xunit.Abstractions;

namespace Nexus.PdfUtilities.Tests;

[Collection("PDF timing")]
public class PdfEngineServiceTests
{
    private readonly IPdfEngineService _pdfEngineService;
    private readonly ITestOutputHelper _output;

    public PdfEngineServiceTests(ITestOutputHelper output)
    {
        _output = output;
        _pdfEngineService = new PdfEngineService();
    }

    [Fact]
    public void Initialize_ShouldNotThrow()
    {
        var exception = Record.Exception(() => _pdfEngineService.Initialize());
        Assert.Null(exception);
    }

    [Fact]
    public void GetPageCount_ValidPdf_ReturnsCorrectCount()
    {
        var testPdfPath = Path.Combine(Path.GetTempPath(), "test_pagecount.pdf");
        CreateDummyPdf(testPdfPath, 3);
        
        try
        {
            var count = _pdfEngineService.GetPageCount(testPdfPath);
            Assert.Equal(3, count);
        }
        finally
        {
            if (File.Exists(testPdfPath)) File.Delete(testPdfPath);
        }
    }

    [Fact]
    public void Spike_Docnet_TextExtraction()
    {
        var testPdfPath = Path.Combine(Path.GetTempPath(), "test_text.pdf");
        var emptyPdfPath = Path.Combine(Path.GetTempPath(), "test_empty.pdf");

        const string rawPdf2Pages = "%PDF-1.4\n" +
                                    "1 0 obj << /Type /Catalog /Pages 2 0 R >> endobj\n" +
                                    "2 0 obj << /Type /Pages /Kids [3 0 R 6 0 R] /Count 2 >> endobj\n" +
                                    "3 0 obj << /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >> endobj\n" +
                                    "4 0 obj << /Length 53 >> stream\n" +
                                    "BT\n" +
                                    "/F1 12 Tf\n" +
                                    "100 700 Td\n" +
                                    "(Hello from PDFium Page 1) Tj\n" +
                                    "ET\n" +
                                    "endstream\n" +
                                    "endobj\n" +
                                    "5 0 obj << /Type /Font /Subtype /Type1 /BaseFont /Helvetica >> endobj\n" +
                                    "6 0 obj << /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 7 0 R /Resources << /Font << /F1 5 0 R >> >> >> endobj\n" +
                                    "7 0 obj << /Length 53 >> stream\n" +
                                    "BT\n" +
                                    "/F1 12 Tf\n" +
                                    "100 700 Td\n" +
                                    "(Hello from PDFium Page 2) Tj\n" +
                                    "ET\n" +
                                    "endstream\n" +
                                    "endobj\n" +
                                    "xref\n" +
                                    "0 8\n" +
                                    "0000000000 65535 f \n" +
                                    "0000000009 00000 n \n" +
                                    "0000000058 00000 n \n" +
                                    "0000000122 00000 n \n" +
                                    "0000000236 00000 n \n" +
                                    "0000000341 00000 n \n" +
                                    "0000000414 00000 n \n" +
                                    "0000000528 00000 n \n" +
                                    "trailer << /Size 8 /Root 1 0 R >>\n" +
                                    "startxref\n" +
                                    "633\n" +
                                    "%%EOF\n";

        File.WriteAllText(testPdfPath, rawPdf2Pages);
        CreateDummyPdf(emptyPdfPath, 2);

        try
        {
            var sw = Stopwatch.StartNew();

            using var docReader = DocLib.Instance.GetDocReader(testPdfPath, new PageDimensions());
            Assert.Equal(2, docReader.GetPageCount());

            var sb = new StringBuilder();
            for (int i = 0; i < docReader.GetPageCount(); i++)
            {
                using var pageReader = docReader.GetPageReader(i);
                var pageText = pageReader.GetText();
                sb.AppendLine(pageText);
            }
            sw.Stop();

            var fullText = sb.ToString();
            Assert.Contains("Hello from PDFium Page 1", fullText);
            Assert.Contains("Hello from PDFium Page 2", fullText);

            using var emptyDocReader = DocLib.Instance.GetDocReader(emptyPdfPath, new PageDimensions());
            for (int i = 0; i < emptyDocReader.GetPageCount(); i++)
            {
                using var pageReader = emptyDocReader.GetPageReader(i);
                var emptyText = pageReader.GetText();
                Assert.True(string.IsNullOrWhiteSpace(emptyText));
            }
        }
        finally
        {
            if (File.Exists(testPdfPath)) File.Delete(testPdfPath);
            if (File.Exists(emptyPdfPath)) File.Delete(emptyPdfPath);
        }
    }

    [Fact]
    public void Benchmark_Docnet_RealWorldPdf()
    {
        // 351 KB real-world PDF asset
        var realPdfPath = @"D:\NEXUS\Audit\llama.cpp-master\tools\ui\tests\stories\fixtures\assets\example.pdf";
        if (!File.Exists(realPdfPath))
        {
            _output.WriteLine("Real world PDF not found, skipping.");
            return;
        }

        var fileInfo = new FileInfo(realPdfPath);
        int pageCount = _pdfEngineService.GetPageCount(realPdfPath);
        var (text, p95) = MeasureDenseTextPdf(realPdfPath, pageCount);
        int totalChars = text.Length;

        _output.WriteLine($"[RealWorldPdf Benchmark]");
        _output.WriteLine($"  File Size: {fileInfo.Length / 1024.0:F2} KB");
        _output.WriteLine($"  Pages: {pageCount}");
        _output.WriteLine($"  Characters Extracted: {totalChars}");
        _output.WriteLine($"  Warm P95 Latency: {p95:F3} ms");

        Assert.True(pageCount > 0);
        Assert.True(p95 < 100, $"Real world PDF warm P95 must be < 100 ms, actual: {p95:F3} ms");
    }

    [Fact]
    public void Benchmark_Docnet_DenseText_10Pages()
    {
        // Target: 10 pages dense text document < 100 ms
        var tempFile = Path.Combine(Path.GetTempPath(), $"dense_10pages_{Guid.NewGuid():N}.pdf");
        GenerateDenseTextPdf(tempFile, 10, linesPerPage: 50);

        try
        {
            var fileInfo = new FileInfo(tempFile);
            var (fullText, p95) = MeasureDenseTextPdf(tempFile, 10);
            _output.WriteLine($"[Dense 10 Pages Benchmark]");
            _output.WriteLine($"  File Size: {fileInfo.Length / 1024.0:F2} KB");
            _output.WriteLine("  Pages: 10");
            _output.WriteLine($"  Characters Extracted: {fullText.Length}");
            _output.WriteLine($"  Warm P95 Latency: {p95:F3} ms");

            Assert.Contains("Page 01 Line 01", fullText);
            Assert.Contains("Page 10 Line 50", fullText);
            Assert.True(p95 < 100, $"10-page dense PDF warm P95 must be < 100 ms, actual: {p95:F3} ms");
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void Benchmark_Docnet_DenseText_50Pages()
    {
        // 50 pages dense text document (~200,000 characters)
        var tempFile = Path.Combine(Path.GetTempPath(), $"dense_50pages_{Guid.NewGuid():N}.pdf");
        GenerateDenseTextPdf(tempFile, 50, linesPerPage: 50);

        try
        {
            var fileInfo = new FileInfo(tempFile);
            var (fullText, p95) = MeasureDenseTextPdf(tempFile, 50);
            _output.WriteLine($"[Dense 50 Pages Benchmark]");
            _output.WriteLine($"  File Size: {fileInfo.Length / 1024.0:F2} KB");
            _output.WriteLine("  Pages: 50");
            _output.WriteLine($"  Characters Extracted: {fullText.Length}");
            _output.WriteLine($"  Warm P95 Latency: {p95:F3} ms");

            Assert.Contains("Page 01 Line 01", fullText);
            Assert.Contains("Page 50 Line 50", fullText);
            Assert.True(p95 < 500, $"50-page dense PDF warm P95 must be < 500 ms, actual: {p95:F3} ms");
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void Benchmark_Docnet_LargeFile_Approaching15Mb()
    {
        // Generates a large PDF ~12-14 MB approaching the 15MB limit
        var tempFile = Path.Combine(Path.GetTempPath(), $"large_14mb_{Guid.NewGuid():N}.pdf");
        GenerateLargePdfApproaching15Mb(tempFile, targetMegabytes: 13);

        try
        {
            var fileInfo = new FileInfo(tempFile);
            _output.WriteLine($"[Large File Approaching 15MB Benchmark]");
            _output.WriteLine($"  Target Size: ~13 MB, Actual Size: {fileInfo.Length / (1024.0 * 1024.0):F2} MB ({fileInfo.Length} bytes)");

            long initialMemory = GC.GetTotalMemory(forceFullCollection: true);

            var sw = Stopwatch.StartNew();
            using var docReader = DocLib.Instance.GetDocReader(tempFile, new PageDimensions());
            int pageCount = docReader.GetPageCount();

            int totalChars = 0;
            for (int i = 0; i < pageCount; i++)
            {
                using var pageReader = docReader.GetPageReader(i);
                var text = pageReader.GetText();
                totalChars += text?.Length ?? 0;
            }
            sw.Stop();

            long finalMemory = GC.GetTotalMemory(forceFullCollection: false);
            long memoryDeltaMb = (finalMemory - initialMemory) / (1024 * 1024);

            _output.WriteLine($"  Pages: {pageCount}");
            _output.WriteLine($"  Characters Extracted: {totalChars}");
            _output.WriteLine($"  Extraction Latency: {sw.ElapsedMilliseconds} ms");
            _output.WriteLine($"  Memory Delta: {memoryDeltaMb} MB");

            Assert.True(sw.ElapsedMilliseconds < 2000, $"Large 13 MB PDF extraction must complete within 2000 ms, actual: {sw.ElapsedMilliseconds} ms");
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    private (string Text, double P95) MeasureDenseTextPdf(string path, int pages)
    {
        // Gate A policy explicitly approved after the cold/warm profile:
        // report cold separately; 3 warm-up reads, then 20 measured reads.
        var samples = new List<double>();
        string? expected = null;
        double coldMs = 0;
        for (int run = 0; run < 23; run++)
        {
            var sw = Stopwatch.StartNew();
            using var doc = DocLib.Instance.GetDocReader(path, new PageDimensions());
            Assert.Equal(pages, doc.GetPageCount());
            var text = new StringBuilder();
            for (int page = 0; page < pages; page++)
            {
                using var reader = doc.GetPageReader(page);
                text.Append(reader.GetText());
            }
            sw.Stop();
            var content = text.ToString();
            expected ??= content;
            Assert.Equal(expected, content);
            if (run == 0) coldMs = sw.Elapsed.TotalMilliseconds;
            if (run >= 3) samples.Add(sw.Elapsed.TotalMilliseconds);
        }
        var sorted = samples.Order().ToArray();
        var mean = samples.Average();
        var p95 = sorted[(int)Math.Ceiling(0.95 * sorted.Length) - 1];
        _output.WriteLine("GATE_A_PDF_TIMING " + System.Text.Json.JsonSerializer.Serialize(new
        {
            pages, coldMs, samples, mean, median = (sorted[9] + sorted[10]) / 2,
            p95, min = sorted[0], max = sorted[^1],
            sampleStdDev = Math.Sqrt(samples.Sum(x => (x - mean) * (x - mean)) / (samples.Count - 1))
        }));
        return (expected!, p95);
    }

    private static void GenerateDenseTextPdf(string filePath, int pageCount, int linesPerPage = 50)
    {
        using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write);
        using var writer = new StreamWriter(fs, Encoding.ASCII);

        var offsets = new List<long>();
        void WriteObject(int objNum, string body)
        {
            writer.Flush();
            offsets.Add(fs.Position);
            writer.Write($"{objNum} 0 obj\n{body}\nendobj\n");
        }

        writer.Write("%PDF-1.4\n");
        offsets.Add(0);

        WriteObject(1, "<< /Type /Catalog /Pages 2 0 R >>");

        var kids = new StringBuilder();
        for (int i = 0; i < pageCount; i++)
        {
            int pageObjNum = 4 + (i * 2);
            kids.Append($"{pageObjNum} 0 R ");
        }
        WriteObject(2, $"<< /Type /Pages /Kids [{kids}] /Count {pageCount} >>");
        WriteObject(3, "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>");

        for (int i = 0; i < pageCount; i++)
        {
            int pageObjNum = 4 + (i * 2);
            int contentObjNum = pageObjNum + 1;

            WriteObject(pageObjNum, $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents {contentObjNum} 0 R /Resources << /Font << /F1 3 0 R >> >> >>");

            var sbContent = new StringBuilder();
            sbContent.Append("BT\n/F1 10 Tf\n14 TL\n50 750 Td\n");
            for (int line = 1; line <= linesPerPage; line++)
            {
                sbContent.Append($"(Page {i + 1:D2} Line {line:D2}: Dense text payload simulating real document content with technical specifications and code listings.) '\n");
            }
            sbContent.Append("ET\n");

            var contentBytes = Encoding.ASCII.GetBytes(sbContent.ToString());
            WriteObject(contentObjNum, $"<< /Length {contentBytes.Length} >>\nstream\n{sbContent}endstream");
        }

        writer.Flush();
        long startXref = fs.Position;
        int totalObjs = 4 + (pageCount * 2);
        writer.Write($"xref\n0 {totalObjs}\n");
        writer.Write("0000000000 65535 f \n");
        for (int i = 1; i < totalObjs; i++)
        {
            writer.Write($"{offsets[i]:D10} 00000 n \n");
        }
        writer.Write($"trailer\n<< /Size {totalObjs} /Root 1 0 R >>\nstartxref\n{startXref}\n%%EOF\n");
        writer.Flush();
    }

    [Theory]
    [InlineData(10)]
    [InlineData(50)]
    public void GateA_ProfileDensePdf_ColdWarmAndStageDistribution(int pages)
    {
        var path = Path.Combine(Path.GetTempPath(), $"nexus-profile-{Guid.NewGuid():N}.pdf");
        GenerateDenseTextPdf(path, pages);
        try
        {
            var records = new List<object>();
            string? expected = null;
            for (int run = 0; run < 23; run++)
            {
                var gcBefore = Enumerable.Range(0, 3).Select(GC.CollectionCount).ToArray();
                var total = Stopwatch.StartNew();
                var stage = Stopwatch.StartNew();
                using var doc = DocLib.Instance.GetDocReader(path, new PageDimensions());
                var openMs = stage.Elapsed.TotalMilliseconds;
                double pageMs = 0, textMs = 0;
                var text = new StringBuilder();
                Assert.Equal(pages, doc.GetPageCount());
                for (int page = 0; page < pages; page++)
                {
                    stage.Restart();
                    using var reader = doc.GetPageReader(page);
                    pageMs += stage.Elapsed.TotalMilliseconds;
                    stage.Restart();
                    text.Append(reader.GetText());
                    textMs += stage.Elapsed.TotalMilliseconds;
                }
                total.Stop();
                var content = text.ToString();
                expected ??= content;
                Assert.Equal(expected, content);
                Assert.Contains($"Page {pages:D2} Line 50", content);
                records.Add(new { run, phase = run == 0 ? "cold" : run < 3 ? "warmup" : "sample",
                    totalMs = total.Elapsed.TotalMilliseconds, openMs, pageMs, textMs,
                    gc = Enumerable.Range(0, 3).Select(g => GC.CollectionCount(g) - gcBefore[g]).ToArray(), chars = content.Length });
            }
            _output.WriteLine("GATE_A_PDF_PROFILE " + System.Text.Json.JsonSerializer.Serialize(new { pages, records }));
        }
        finally { File.Delete(path); }
    }

    private static void GenerateLargePdfApproaching15Mb(string filePath, int targetMegabytes)
    {
        using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write);
        using var writer = new StreamWriter(fs, Encoding.ASCII);

        var offsets = new List<long>();
        void WriteObject(int objNum, string body)
        {
            writer.Flush();
            offsets.Add(fs.Position);
            writer.Write($"{objNum} 0 obj\n{body}\nendobj\n");
        }

        writer.Write("%PDF-1.4\n");
        offsets.Add(0);

        WriteObject(1, "<< /Type /Catalog /Pages 2 0 R >>");

        int pageCount = 20;
        var kids = new StringBuilder();
        for (int i = 0; i < pageCount; i++)
        {
            int pageObjNum = 4 + (i * 2);
            kids.Append($"{pageObjNum} 0 R ");
        }
        WriteObject(2, $"<< /Type /Pages /Kids [{kids}] /Count {pageCount} >>");
        WriteObject(3, "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>");

        // Distribute target megabytes across 20 pages in content streams
        int bytesPerPage = (targetMegabytes * 1024 * 1024) / pageCount;

        for (int i = 0; i < pageCount; i++)
        {
            int pageObjNum = 4 + (i * 2);
            int contentObjNum = pageObjNum + 1;

            WriteObject(pageObjNum, $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents {contentObjNum} 0 R /Resources << /Font << /F1 3 0 R >> >> >>");

            // Build stream with real text lines + comment padding to achieve exact megabytes
            var sbContent = new StringBuilder();
            sbContent.Append("BT\n/F1 10 Tf\n14 TL\n50 750 Td\n");
            for (int line = 1; line <= 50; line++)
            {
                sbContent.Append($"(Page {i + 1:D2} Line {line:D2}: Heavy technical document paragraph containing architecture descriptions, database schemas, and configuration parameters.) '\n");
            }
            sbContent.Append("ET\n");

            // Pad stream with valid PDF stream comment padding to reach target size
            int currentLength = sbContent.Length;
            int paddingNeeded = Math.Max(0, bytesPerPage - currentLength - 50);
            if (paddingNeeded > 0)
            {
                sbContent.Append("% ");
                sbContent.Append(new string('X', paddingNeeded));
                sbContent.Append("\n");
            }

            var contentBytes = Encoding.ASCII.GetBytes(sbContent.ToString());
            WriteObject(contentObjNum, $"<< /Length {contentBytes.Length} >>\nstream\n{sbContent}endstream");
        }

        writer.Flush();
        long startXref = fs.Position;
        int totalObjs = 4 + (pageCount * 2);
        writer.Write($"xref\n0 {totalObjs}\n");
        writer.Write("0000000000 65535 f \n");
        for (int i = 1; i < totalObjs; i++)
        {
            writer.Write($"{offsets[i]:D10} 00000 n \n");
        }
        writer.Write($"trailer\n<< /Size {totalObjs} /Root 1 0 R >>\nstartxref\n{startXref}\n%%EOF\n");
        writer.Flush();
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
