using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Nexus.AI.Llama.Services.Extractors;
using Xunit;

namespace Nexus.AI.Llama.Tests
{
    public class PdfContentExtractorTests
    {
        private readonly PdfContentExtractor _extractor = new();

        [Fact]
        public async Task ExtractTextAsync_SinglePdf_ExtractsTextAccurately()
        {
            var tempPdf = Path.Combine(Path.GetTempPath(), $"pdf_single_{Guid.NewGuid():N}.pdf");
            GenerateSimplePdf(tempPdf, "Single PDF content for basic extraction test.");

            try
            {
                var text = await _extractor.ExtractTextAsync(tempPdf);
                Assert.Contains("Single PDF content for basic extraction test.", text);
            }
            finally
            {
                if (File.Exists(tempPdf)) File.Delete(tempPdf);
            }
        }

        [Fact]
        public async Task ExtractTextAsync_EmptyOrScannedPdf_ReturnsEmptyWithoutThrowing()
        {
            var emptyPdf = Path.Combine(Path.GetTempPath(), $"pdf_empty_{Guid.NewGuid():N}.pdf");
            GenerateEmptyPdf(emptyPdf);

            try
            {
                var text = await _extractor.ExtractTextAsync(emptyPdf);
                Assert.True(string.IsNullOrWhiteSpace(text));
            }
            finally
            {
                if (File.Exists(emptyPdf)) File.Delete(emptyPdf);
            }
        }

        [Fact]
        public async Task ExtractTextAsync_NonExistentFile_ThrowsFileNotFoundException()
        {
            var nonExistent = Path.Combine(Path.GetTempPath(), $"missing_{Guid.NewGuid():N}.pdf");
            await Assert.ThrowsAsync<FileNotFoundException>(() => _extractor.ExtractTextAsync(nonExistent));
        }

        [Fact]
        public async Task ExtractTextAsync_ConcurrentPdfExtraction_PreventsAccessViolationViaSemaphore()
        {
            // Generates 5 distinct PDF files with unique markers to test concurrent extraction
            const int fileCount = 5;
            var filePaths = new List<string>();
            var expectedTokens = new List<string>();

            for (int i = 0; i < fileCount; i++)
            {
                var uniqueMarker = $"UNIQUE_CONCURRENT_PDF_TOKEN_{i}_{Guid.NewGuid():N}";
                var path = Path.Combine(Path.GetTempPath(), $"concurrent_pdf_{i}_{Guid.NewGuid():N}.pdf");
                GenerateSimplePdf(path, $"Document {i}: {uniqueMarker}. Paragraph of technical content.");
                filePaths.Add(path);
                expectedTokens.Add(uniqueMarker);
            }

            try
            {
                // Run 10 iterations of simultaneous parallel extraction to stress-test race conditions
                for (int iteration = 0; iteration < 10; iteration++)
                {
                    var tasks = filePaths.Select(async (path, index) =>
                    {
                        var text = await _extractor.ExtractTextAsync(path);
                        return (Index: index, Text: text);
                    }).ToArray();

                    var results = await Task.WhenAll(tasks);

                    // Verify each parallel extraction got its exact corresponding content without native memory corruption
                    foreach (var result in results)
                    {
                        Assert.False(string.IsNullOrWhiteSpace(result.Text));
                        Assert.Contains(expectedTokens[result.Index], result.Text);
                    }
                }
            }
            finally
            {
                foreach (var path in filePaths)
                {
                    if (File.Exists(path)) File.Delete(path);
                }
            }
        }

        private static void GenerateSimplePdf(string filePath, string textContent)
        {
            var contentStream = $"BT\n/F1 12 Tf\n50 750 Td\n({textContent}) Tj\nET\n";
            var streamBytes = Encoding.ASCII.GetBytes(contentStream);

            var rawPdf = "%PDF-1.4\n" +
                         "1 0 obj << /Type /Catalog /Pages 2 0 R >> endobj\n" +
                         "2 0 obj << /Type /Pages /Kids [3 0 R] /Count 1 >> endobj\n" +
                         "3 0 obj << /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >> endobj\n" +
                         $"4 0 obj << /Length {streamBytes.Length} >> stream\n" +
                         contentStream +
                         "endstream\n" +
                         "endobj\n" +
                         "5 0 obj << /Type /Font /Subtype /Type1 /BaseFont /Helvetica >> endobj\n" +
                         "xref\n" +
                         "0 6\n" +
                         "0000000000 65535 f \n" +
                         "0000000009 00000 n \n" +
                         "0000000058 00000 n \n" +
                         "0000000115 00000 n \n" +
                         "0000000229 00000 n \n" +
                         $"0000000{300 + streamBytes.Length:D3} 00000 n \n" +
                         "trailer << /Size 6 /Root 1 0 R >>\n" +
                         "startxref\n" +
                         $"{400 + streamBytes.Length}\n" +
                         "%%EOF\n";

            File.WriteAllText(filePath, rawPdf);
        }

        private static void GenerateEmptyPdf(string filePath)
        {
            const string rawPdf = "%PDF-1.4\n" +
                                  "1 0 obj << /Type /Catalog /Pages 2 0 R >> endobj\n" +
                                  "2 0 obj << /Type /Pages /Kids [3 0 R] /Count 1 >> endobj\n" +
                                  "3 0 obj << /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] >> endobj\n" +
                                  "xref\n" +
                                  "0 4\n" +
                                  "0000000000 65535 f \n" +
                                  "0000000009 00000 n \n" +
                                  "0000000058 00000 n \n" +
                                  "0000000115 00000 n \n" +
                                  "trailer << /Size 4 /Root 1 0 R >>\n" +
                                  "startxref\n" +
                                  "185\n" +
                                  "%%EOF\n";

            File.WriteAllText(filePath, rawPdf);
        }
    }
}
