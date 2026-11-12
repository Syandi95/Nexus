using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Nexus.AI.Llama.Models;
using Nexus.AI.Llama.Services.Extractors;

namespace Nexus.AI.Llama.Services
{
    public class AttachmentManagerService : IAttachmentManagerService
    {
        public const long DefaultMaxTextFileSizeBytes = 5 * 1024 * 1024;   // 5 MB limit for text/code
        public const long DefaultMaxPdfFileSizeBytes = 15 * 1024 * 1024;  // 15 MB limit for PDF
        public const int CharsPerToken = 3; // Calibrated conservative ratio from Cherry Studio

        private readonly ITextContentExtractor _textExtractor;
        private readonly IPdfContentExtractor? _pdfExtractor;
        private readonly long _maxTextFileSizeBytes;
        private readonly long _maxPdfFileSizeBytes;

        private static readonly HashSet<string> SupportedTextExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".txt", ".md", ".markdown", ".json", ".csv", ".log",
            ".yaml", ".yml", ".xml", ".cs", ".py", ".js", ".ts",
            ".html", ".htm", ".css", ".sql", ".sh", ".bat", ".cmd",
            ".ps1", ".ini", ".env", ".toml", ".cpp", ".c", ".h",
            ".hpp", ".java", ".rs", ".go"
        };

        private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".png", ".jpg", ".jpeg", ".webp", ".bmp", ".gif", ".ico", ".tiff", ".tif", ".svg"
        };

        private static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".mp3", ".wav", ".ogg", ".flac", ".m4a", ".aac", ".wma"
        };

        public const string MediaCapabilityErrorMessage =
            "Model Gemma 4 E4B saat ini beroperasi dalam mode Teks. Lampiran gambar dinonaktifkan sementara (akan diaktifkan pada Fase 2 via proyektor multimodal mmproj), dan format audio belum didukung. Silakan lampirkan berkas berbasis teks, kode, atau dokumen PDF.";

        public AttachmentManagerService(
            ITextContentExtractor textExtractor,
            IPdfContentExtractor? pdfExtractor = null,
            long maxTextFileSizeBytes = DefaultMaxTextFileSizeBytes,
            long maxPdfFileSizeBytes = DefaultMaxPdfFileSizeBytes,
            bool registerDefaultPdfExtractor = true)
        {
            _textExtractor = textExtractor ?? throw new ArgumentNullException(nameof(textExtractor));
            _pdfExtractor = pdfExtractor ?? (registerDefaultPdfExtractor ? new PdfContentExtractor() : null);
            _maxTextFileSizeBytes = maxTextFileSizeBytes > 0 ? maxTextFileSizeBytes : DefaultMaxTextFileSizeBytes;
            _maxPdfFileSizeBytes = maxPdfFileSizeBytes > 0 ? maxPdfFileSizeBytes : DefaultMaxPdfFileSizeBytes;
        }

        public bool IsSupportedExtension(string extension)
        {
            if (string.IsNullOrWhiteSpace(extension))
                return false;

            if (!extension.StartsWith('.'))
                extension = "." + extension;

            return SupportedTextExtensions.Contains(extension) ||
                   string.Equals(extension, ".pdf", StringComparison.OrdinalIgnoreCase);
        }

        public bool IsUnsupportedMediaExtension(string extension)
        {
            if (string.IsNullOrWhiteSpace(extension))
                return false;

            if (!extension.StartsWith('.'))
                extension = "." + extension;

            return ImageExtensions.Contains(extension) || AudioExtensions.Contains(extension);
        }

        public string GetCapabilityErrorMessage(string extension)
        {
            if (IsUnsupportedMediaExtension(extension))
                return MediaCapabilityErrorMessage;

            if (string.Equals(extension, ".docx", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(extension, ".xlsx", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(extension, ".pptx", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(extension, ".doc", StringComparison.OrdinalIgnoreCase))
            {
                return $"Format dokumen Office '{extension}' belum didukung pada Fase 1. Silakan ekspor ke PDF atau teks.";
            }

            return $"Unsupported file extension '{extension}'. Only plain text documents and PDFs are supported.";
        }

        public bool ValidateMagicBytes(string filePath, out string? errorMessage)
        {
            errorMessage = null;

            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                errorMessage = $"File not found: {filePath}";
                return false;
            }

            try
            {
                var fileInfo = new FileInfo(filePath);
                if (fileInfo.Length < 2)
                {
                    // Files smaller than 2 bytes cannot match executable or archive magic signatures
                    return true;
                }

                byte[] header = new byte[8];
                int bytesRead;
                using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    bytesRead = fs.Read(header, 0, (int)Math.Min(header.Length, fileInfo.Length));
                }

                if (bytesRead >= 2)
                {
                    // Windows PE / DOS Executable ("MZ")
                    if (header[0] == 0x4D && header[1] == 0x5A)
                    {
                        errorMessage = $"File '{Path.GetFileName(filePath)}' berisi format biner eksekusi (PE/EXE) yang dilarang demi keamanan.";
                        return false;
                    }
                }

                if (bytesRead >= 4)
                {
                    // Linux ELF ("\x7fELF")
                    if (header[0] == 0x7F && header[1] == 0x45 && header[2] == 0x4C && header[3] == 0x46)
                    {
                        errorMessage = $"File '{Path.GetFileName(filePath)}' berisi format biner eksekusi (ELF) yang dilarang demi keamanan.";
                        return false;
                    }

                    // ZIP Archive signatures ("PK\x03\x04", "PK\x05\x06", "PK\x07\x08")
                    if (header[0] == 0x50 && header[1] == 0x4B)
                    {
                        if ((header[2] == 0x03 && header[3] == 0x04) ||
                            (header[2] == 0x05 && header[3] == 0x06) ||
                            (header[2] == 0x07 && header[3] == 0x08))
                        {
                            errorMessage = $"File '{Path.GetFileName(filePath)}' merupakan arsip biner terkompresi (ZIP) yang tidak didukung pada Fase 1.";
                            return false;
                        }
                    }

                    // RAR Archive ("Rar!\x1A\x07")
                    if (header[0] == 0x52 && header[1] == 0x61 && header[2] == 0x72 && header[3] == 0x21)
                    {
                        errorMessage = $"File '{Path.GetFileName(filePath)}' merupakan arsip biner terkompresi (RAR) yang tidak didukung pada Fase 1.";
                        return false;
                    }
                }

                if (bytesRead >= 6)
                {
                    // 7-Zip Archive ("7z\xBC\xAF\x27\x1C")
                    if (header[0] == 0x37 && header[1] == 0x7A && header[2] == 0xBC &&
                        header[3] == 0xAF && header[4] == 0x27 && header[5] == 0x1C)
                    {
                        errorMessage = $"File '{Path.GetFileName(filePath)}' merupakan arsip biner terkompresi (7z) yang tidak didukung pada Fase 1.";
                        return false;
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                errorMessage = $"Gagal memvalidasi header file: {ex.Message}";
                return false;
            }
        }

        public bool ValidateFileSize(string filePath, out long sizeBytes, out string? errorMessage)
        {
            sizeBytes = 0;
            errorMessage = null;

            if (string.IsNullOrWhiteSpace(filePath))
            {
                errorMessage = "File path cannot be null or empty.";
                return false;
            }

            if (!File.Exists(filePath))
            {
                errorMessage = $"File not found: {filePath}";
                return false;
            }

            var fileInfo = new FileInfo(filePath);
            sizeBytes = fileInfo.Length;

            var ext = Path.GetExtension(filePath);
            bool isPdf = string.Equals(ext, ".pdf", StringComparison.OrdinalIgnoreCase);
            long limitBytes = isPdf ? _maxPdfFileSizeBytes : _maxTextFileSizeBytes;
            string fileTypeLabel = isPdf ? "PDF" : "Text/code";

            if (sizeBytes > limitBytes)
            {
                double sizeMb = sizeBytes / (1024.0 * 1024.0);
                double maxMb = limitBytes / (1024.0 * 1024.0);
                errorMessage = $"{fileTypeLabel} file size ({sizeMb.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)} MB) exceeds maximum allowed limit of {maxMb.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)} MB.";
                return false;
            }

            return true;
        }

        public async Task<ExtractedAttachmentResult> ProcessAttachmentAsync(string filePath, CancellationToken ct = default)
        {
            var fileName = Path.GetFileName(filePath);
            var result = new ExtractedAttachmentResult
            {
                FileName = fileName,
                FilePath = filePath
            };

            if (!ValidateFileSize(filePath, out var sizeBytes, out var sizeError))
            {
                result.ErrorMessage = sizeError;
                return result;
            }

            result.FileSizeBytes = sizeBytes;
            var ext = Path.GetExtension(filePath);

            if (IsUnsupportedMediaExtension(ext))
            {
                result.ErrorMessage = MediaCapabilityErrorMessage;
                return result;
            }

            if (!IsSupportedExtension(ext))
            {
                result.ErrorMessage = GetCapabilityErrorMessage(ext);
                return result;
            }

            if (!ValidateMagicBytes(filePath, out var magicError))
            {
                result.ErrorMessage = magicError;
                return result;
            }

            try
            {
                if (string.Equals(ext, ".pdf", StringComparison.OrdinalIgnoreCase))
                {
                    if (_pdfExtractor == null)
                    {
                        result.ErrorMessage = "PDF extraction is not currently enabled.";
                        return result;
                    }

                    result.ExtractedText = await _pdfExtractor.ExtractTextAsync(filePath, ct).ConfigureAwait(false);
                    result.DetectedEncoding = "pdf-stream";
                }
                else
                {
                    var (text, encoding) = await _textExtractor.ExtractTextFromFileAsync(filePath, ct).ConfigureAwait(false);
                    result.ExtractedText = text;
                    result.DetectedEncoding = encoding;
                }

                result.CharacterCount = result.ExtractedText.Length;
                result.EstimatedTokens = EstimateTokens(result.ExtractedText);
                result.IsTruncated = false;
                result.ErrorMessage = null;
            }
            catch (Exception ex)
            {
                result.ErrorMessage = $"Failed to extract attachment: {ex.Message}";
            }

            return result;
        }

        public static int EstimateTokens(string text)
        {
            if (string.IsNullOrEmpty(text))
                return 0;

            return (int)Math.Ceiling(text.Length / (double)CharsPerToken);
        }

        public static string FormatTruncatedAttachment(
            string fileName,
            string slicedText,
            int allocatedTokens,
            int totalTokens,
            int charCap,
            int totalChars)
        {
            return $"[--- Lampiran terpotong: {fileName} (dialokasikan {allocatedTokens}/{totalTokens} token, {charCap}/{totalChars} karakter) ---]\n{slicedText}";
        }

        public static string FormatAttachmentPayload(string fileName, string content)
        {
            return $"<lampiran nama=\"{fileName}\">\n{content}\n</lampiran>";
        }
    }
}
