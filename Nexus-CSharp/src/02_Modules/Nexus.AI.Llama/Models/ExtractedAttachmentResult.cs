using System;

namespace Nexus.AI.Llama.Models
{
    /// <summary>
    /// Represents the extracted text and metadata of a processed attachment.
    /// </summary>
    public class ExtractedAttachmentResult
    {
        public string FileName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public string ExtractedText { get; set; } = string.Empty;
        public int EstimatedTokens { get; set; }
        public int CharacterCount { get; set; }
        public string DetectedEncoding { get; set; } = string.Empty;
        public bool IsTruncated { get; set; }
        public long FileSizeBytes { get; set; }
        public string? ErrorMessage { get; set; }
        public bool IsSuccess => string.IsNullOrEmpty(ErrorMessage);
    }
}
