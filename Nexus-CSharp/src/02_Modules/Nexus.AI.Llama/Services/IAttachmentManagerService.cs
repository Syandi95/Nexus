using System.Threading;
using System.Threading.Tasks;
using Nexus.AI.Llama.Models;

namespace Nexus.AI.Llama.Services
{
    /// <summary>
    /// Contract for attachment management: validation, text extraction dispatch, and token estimation.
    /// </summary>
    public interface IAttachmentManagerService
    {
        /// <summary>
        /// Validates and processes a file attachment, extracting its textual content and estimating tokens.
        /// </summary>
        Task<ExtractedAttachmentResult> ProcessAttachmentAsync(string filePath, CancellationToken ct = default);

        /// <summary>
        /// Checks whether the file extension is supported for attachment extraction.
        /// </summary>
        bool IsSupportedExtension(string extension);

        /// <summary>
        /// Validates that the file exists and is within permissible size boundaries.
        /// </summary>
        bool ValidateFileSize(string filePath, out long sizeBytes, out string? errorMessage);

        /// <summary>
        /// Checks the file header (magic bytes) to reject executable binaries (MZ, ELF) and archive formats (ZIP, 7z, RAR).
        /// </summary>
        bool ValidateMagicBytes(string filePath, out string? errorMessage);

        /// <summary>
        /// Returns true if the file extension corresponds to media (images or audio) that require multimodal or dedicated capabilities.
        /// </summary>
        bool IsUnsupportedMediaExtension(string extension);

        /// <summary>
        /// Returns a capability-aware rejection message for unsupported media extensions.
        /// </summary>
        string GetCapabilityErrorMessage(string extension);
    }
}
