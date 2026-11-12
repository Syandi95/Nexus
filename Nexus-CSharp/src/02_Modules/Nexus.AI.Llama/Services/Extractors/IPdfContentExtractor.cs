using System.Threading;
using System.Threading.Tasks;

namespace Nexus.AI.Llama.Services.Extractors
{
    /// <summary>
    /// Contract for extracting textual content from PDF files.
    /// </summary>
    public interface IPdfContentExtractor
    {
        /// <summary>
        /// Extracts text from all readable pages in the specified PDF file.
        /// Returns empty string if the PDF is scanned or image-only without digital text.
        /// </summary>
        Task<string> ExtractTextAsync(string filePath, CancellationToken ct = default);
    }
}
