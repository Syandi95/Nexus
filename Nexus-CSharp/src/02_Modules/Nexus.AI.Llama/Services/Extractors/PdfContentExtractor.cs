using System;
using System.Threading;
using System.Threading.Tasks;
using Nexus.PdfUtilities;

namespace Nexus.AI.Llama.Services.Extractors
{
    /// <summary>
    /// Thin adapter implementing IPdfContentExtractor by delegating all PDF extraction
    /// to the central IPdfEngineService in Nexus.PdfUtilities.
    /// Thread-safety and unmanaged PDFium lifecycle are encapsulated in IPdfEngineService.
    /// </summary>
    public class PdfContentExtractor : IPdfContentExtractor
    {
        private readonly IPdfEngineService _pdfEngineService;

        public PdfContentExtractor(IPdfEngineService? pdfEngineService = null)
        {
            _pdfEngineService = pdfEngineService ?? new PdfEngineService();
        }

        public Task<string> ExtractTextAsync(string filePath, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("File path cannot be null or empty", nameof(filePath));

            return _pdfEngineService.ExtractTextAsync(filePath, ct);
        }
    }
}
