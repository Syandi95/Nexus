using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace Nexus.PdfUtilities;

public class PdfMergeSplitService : IPdfMergeSplitService
{
    public Task MergePdfAsync(IEnumerable<string> inputPaths, string outputPath, CancellationToken cancellationToken)
    {
        if (inputPaths == null) throw new ArgumentNullException(nameof(inputPaths));
        if (string.IsNullOrWhiteSpace(outputPath)) throw new ArgumentException("Output path cannot be empty", nameof(outputPath));

        return Task.Run(() =>
        {
            using var outDoc = new PdfDocument();
            foreach (var pdfPath in inputPaths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var inDoc = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Import);
                for (int i = 0; i < inDoc.PageCount; i++)
                {
                    outDoc.AddPage(inDoc.Pages[i]);
                }
            }
            outDoc.Save(outputPath);
        }, cancellationToken);
    }

    public Task SplitPdfAsync(string inputPath, string outputDir, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(inputPath)) throw new ArgumentException("Input path cannot be empty", nameof(inputPath));
        if (string.IsNullOrWhiteSpace(outputDir)) throw new ArgumentException("Output dir cannot be empty", nameof(outputDir));

        if (!File.Exists(inputPath)) throw new FileNotFoundException("PDF not found", inputPath);

        if (!Directory.Exists(outputDir))
            Directory.CreateDirectory(outputDir);

        return Task.Run(() =>
        {
            var fileName = Path.GetFileNameWithoutExtension(inputPath);
            using var inDoc = PdfReader.Open(inputPath, PdfDocumentOpenMode.Import);
            for (int i = 0; i < inDoc.PageCount; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var outDoc = new PdfDocument();
                outDoc.AddPage(inDoc.Pages[i]);
                var outPath = Path.Combine(outputDir, $"{fileName}_{i + 1}.pdf");
                outDoc.Save(outPath);
            }
        }, cancellationToken);
    }
}
