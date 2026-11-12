using Xunit;
using Nexus.PdfUtilities;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Nexus.PdfUtilities.Tests;

public class PdfMergeSplitServiceTests
{
    private readonly IPdfMergeSplitService _mergeSplitService;

    public PdfMergeSplitServiceTests()
    {
        _mergeSplitService = new PdfMergeSplitService();
    }

    [Fact]
    public async Task MergePdfAsync_ValidPdfs_MergesSuccessfully()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(tempDir);
        var pdf1 = Path.Combine(tempDir, "1.pdf");
        var pdf2 = Path.Combine(tempDir, "2.pdf");
        var outPdf = Path.Combine(tempDir, "out.pdf");
        CreateDummyPdf(pdf1, 1);
        CreateDummyPdf(pdf2, 2);

        try
        {
            await _mergeSplitService.MergePdfAsync(new[] { pdf1, pdf2 }, outPdf, CancellationToken.None);
            Assert.True(File.Exists(outPdf));
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task SplitPdfAsync_ValidPdf_SplitsSuccessfully()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(tempDir);
        var pdf1 = Path.Combine(tempDir, "source.pdf");
        CreateDummyPdf(pdf1, 3);

        try
        {
            await _mergeSplitService.SplitPdfAsync(pdf1, tempDir, CancellationToken.None);
            var files = Directory.GetFiles(tempDir, "source_*.pdf");
            Assert.Equal(3, files.Length);
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
