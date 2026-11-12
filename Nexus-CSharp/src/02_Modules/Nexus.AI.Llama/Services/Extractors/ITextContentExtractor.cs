using System.Threading;
using System.Threading.Tasks;

namespace Nexus.AI.Llama.Services.Extractors
{
    /// <summary>
    /// Contract for text content extraction with multi-tier fallback encoding detection.
    /// </summary>
    public interface ITextContentExtractor
    {
        /// <summary>
        /// Extracts text from raw bytes using a robust 5-tier fallback encoding strategy.
        /// Returns the decoded text and the detected encoding name.
        /// </summary>
        (string Text, string DetectedEncoding) ExtractText(byte[] rawBytes);

        /// <summary>
        /// Reads a file asynchronously and extracts text using 5-tier fallback encoding.
        /// </summary>
        Task<(string Text, string DetectedEncoding)> ExtractTextFromFileAsync(string filePath, CancellationToken ct = default);
    }
}
