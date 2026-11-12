using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Nexus.AI.Llama.Services;
using Nexus.AI.Llama.Services.Extractors;
using Xunit;

namespace Nexus.AI.Llama.Tests
{
    public class AttachmentManagerServiceTests
    {
        private readonly Mock<ITextContentExtractor> _mockTextExtractor = new();
        private readonly Mock<IPdfContentExtractor> _mockPdfExtractor = new();

        [Theory]
        [InlineData(".txt", true)]
        [InlineData(".md", true)]
        [InlineData(".cs", true)]
        [InlineData(".json", true)]
        [InlineData(".pdf", true)]
        [InlineData(".PDF", true)]
        [InlineData("txt", true)]
        [InlineData(".exe", false)]
        [InlineData(".dll", false)]
        [InlineData(".docx", false)]
        [InlineData(".png", false)]
        [InlineData(".mp3", false)]
        public void IsSupportedExtension_ChecksSupportedFormats(string ext, bool expected)
        {
            var service = new AttachmentManagerService(_mockTextExtractor.Object);
            Assert.Equal(expected, service.IsSupportedExtension(ext));
        }

        [Fact]
        public void ValidateFileSize_NonExistentFile_ReturnsFalse()
        {
            var service = new AttachmentManagerService(_mockTextExtractor.Object);
            var result = service.ValidateFileSize(@"C:\non_existent_folder\file.txt", out var size, out var error);

            Assert.False(result);
            Assert.Contains("File not found", error);
            Assert.Equal(0, size);
        }

        [Fact]
        public void ValidateFileSize_TextExceeds5Mb_ReturnsFalse()
        {
            var tempFile = Path.Combine(Path.GetTempPath(), $"large_text_{Guid.NewGuid():N}.txt");
            File.WriteAllBytes(tempFile, new byte[6 * 1024 * 1024]); // 6 MB (exceeds 5 MB)

            try
            {
                var service = new AttachmentManagerService(_mockTextExtractor.Object);
                var result = service.ValidateFileSize(tempFile, out var size, out var error);

                Assert.False(result);
                Assert.Contains("Text/code file size", error);
                Assert.Contains("exceeds maximum allowed limit of 5.00 MB", error);
                Assert.Equal(6 * 1024 * 1024, size);
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
        }

        [Fact]
        public void ValidateFileSize_PdfWithin15Mb_ReturnsTrue()
        {
            var tempFile = Path.Combine(Path.GetTempPath(), $"valid_pdf_{Guid.NewGuid():N}.pdf");
            File.WriteAllBytes(tempFile, new byte[6 * 1024 * 1024]); // 6 MB (valid for PDF, which allows 15 MB)

            try
            {
                var service = new AttachmentManagerService(_mockTextExtractor.Object);
                var result = service.ValidateFileSize(tempFile, out var size, out var error);

                Assert.True(result);
                Assert.Null(error);
                Assert.Equal(6 * 1024 * 1024, size);
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
        }

        [Fact]
        public void ValidateFileSize_PdfExceeds15Mb_ReturnsFalse()
        {
            var tempFile = Path.Combine(Path.GetTempPath(), $"oversized_pdf_{Guid.NewGuid():N}.pdf");
            File.WriteAllBytes(tempFile, new byte[16 * 1024 * 1024]); // 16 MB (exceeds 15 MB)

            try
            {
                var service = new AttachmentManagerService(_mockTextExtractor.Object);
                var result = service.ValidateFileSize(tempFile, out var size, out var error);

                Assert.False(result);
                Assert.Contains("PDF file size", error);
                Assert.Contains("exceeds maximum allowed limit of 15.00 MB", error);
                Assert.Equal(16 * 1024 * 1024, size);
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
        }

        [Fact]
        public async Task ProcessAttachmentAsync_TextFile_ExtractsSuccessfullyAndEstimatesTokens()
        {
            var tempFile = Path.Combine(Path.GetTempPath(), $"sample_{Guid.NewGuid():N}.md");
            const string content = "0123456789"; // 10 chars -> ceil(10 / 3) = 4 tokens
            await File.WriteAllTextAsync(tempFile, content);

            _mockTextExtractor
                .Setup(x => x.ExtractTextFromFileAsync(tempFile, It.IsAny<CancellationToken>()))
                .ReturnsAsync((content, "utf-8"));

            var service = new AttachmentManagerService(_mockTextExtractor.Object);

            try
            {
                var result = await service.ProcessAttachmentAsync(tempFile);

                Assert.True(result.IsSuccess);
                Assert.Equal(content, result.ExtractedText);
                Assert.Equal("utf-8", result.DetectedEncoding);
                Assert.Equal(10, result.CharacterCount);
                Assert.Equal(4, result.EstimatedTokens);
                Assert.False(result.IsTruncated);
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
        }

        [Fact]
        public async Task ProcessAttachmentAsync_PdfFile_WithPdfExtractor_DelegatesProperly()
        {
            var tempFile = Path.Combine(Path.GetTempPath(), $"sample_{Guid.NewGuid():N}.pdf");
            await File.WriteAllTextAsync(tempFile, "%PDF-1.4 dummy");

            const string pdfText = "Extracted PDF content from pages.";
            _mockPdfExtractor
                .Setup(x => x.ExtractTextAsync(tempFile, It.IsAny<CancellationToken>()))
                .ReturnsAsync(pdfText);

            var service = new AttachmentManagerService(_mockTextExtractor.Object, _mockPdfExtractor.Object);

            try
            {
                var result = await service.ProcessAttachmentAsync(tempFile);

                Assert.True(result.IsSuccess);
                Assert.Equal(pdfText, result.ExtractedText);
                Assert.Equal("pdf-stream", result.DetectedEncoding);
                Assert.Equal(pdfText.Length, result.CharacterCount);
                Assert.Equal((int)Math.Ceiling(pdfText.Length / 3.0), result.EstimatedTokens);
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
        }

        [Fact]
        public async Task ProcessAttachmentAsync_PdfFile_WithoutPdfExtractor_ReturnsFriendlyError()
        {
            var tempFile = Path.Combine(Path.GetTempPath(), $"sample_{Guid.NewGuid():N}.pdf");
            await File.WriteAllTextAsync(tempFile, "%PDF-1.4 dummy");

            var service = new AttachmentManagerService(_mockTextExtractor.Object, pdfExtractor: null, registerDefaultPdfExtractor: false);

            try
            {
                var result = await service.ProcessAttachmentAsync(tempFile);

                Assert.False(result.IsSuccess);
                Assert.Contains("PDF extraction is not currently enabled", result.ErrorMessage);
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
        }

        [Fact]
        public async Task ProcessAttachmentAsync_UnsupportedExtension_ReturnsError()
        {
            var tempFile = Path.Combine(Path.GetTempPath(), $"sample_{Guid.NewGuid():N}.xyz");
            await File.WriteAllTextAsync(tempFile, "some unknown content");

            var service = new AttachmentManagerService(_mockTextExtractor.Object);

            try
            {
                var result = await service.ProcessAttachmentAsync(tempFile);

                Assert.False(result.IsSuccess);
                Assert.Contains("Unsupported file extension", result.ErrorMessage);
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
        }

        [Fact]
        public void ValidateMagicBytes_RejectsExecutablePE_MZ()
        {
            var tempFile = Path.Combine(Path.GetTempPath(), $"fake_pe_{Guid.NewGuid():N}.txt");
            byte[] mzHeader = new byte[] { 0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00 };
            File.WriteAllBytes(tempFile, mzHeader);

            var service = new AttachmentManagerService(_mockTextExtractor.Object);

            try
            {
                var isValid = service.ValidateMagicBytes(tempFile, out var error);
                Assert.False(isValid);
                Assert.NotNull(error);
                Assert.Contains("PE/EXE", error);
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
        }

        [Fact]
        public void ValidateMagicBytes_RejectsExecutableLinux_ELF()
        {
            var tempFile = Path.Combine(Path.GetTempPath(), $"fake_elf_{Guid.NewGuid():N}.txt");
            byte[] elfHeader = new byte[] { 0x7F, 0x45, 0x4C, 0x46, 0x02, 0x01, 0x01, 0x00 };
            File.WriteAllBytes(tempFile, elfHeader);

            var service = new AttachmentManagerService(_mockTextExtractor.Object);

            try
            {
                var isValid = service.ValidateMagicBytes(tempFile, out var error);
                Assert.False(isValid);
                Assert.NotNull(error);
                Assert.Contains("ELF", error);
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
        }

        [Theory]
        [InlineData(new byte[] { 0x50, 0x4B, 0x03, 0x04, 0x14, 0x00, 0x00, 0x00 }, "ZIP")]
        [InlineData(new byte[] { 0x50, 0x4B, 0x05, 0x06, 0x00, 0x00, 0x00, 0x00 }, "ZIP")]
        [InlineData(new byte[] { 0x50, 0x4B, 0x07, 0x08, 0x00, 0x00, 0x00, 0x00 }, "ZIP")]
        [InlineData(new byte[] { 0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C, 0x00, 0x03 }, "7z")]
        [InlineData(new byte[] { 0x52, 0x61, 0x72, 0x21, 0x1A, 0x07, 0x00, 0x00 }, "RAR")]
        public void ValidateMagicBytes_RejectsArchiveSignatures(byte[] header, string archiveType)
        {
            var tempFile = Path.Combine(Path.GetTempPath(), $"fake_archive_{Guid.NewGuid():N}.log");
            File.WriteAllBytes(tempFile, header);

            var service = new AttachmentManagerService(_mockTextExtractor.Object);

            try
            {
                var isValid = service.ValidateMagicBytes(tempFile, out var error);
                Assert.False(isValid);
                Assert.NotNull(error);
                Assert.Contains(archiveType, error);
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
        }

        [Fact]
        public void ValidateMagicBytes_AcceptsCleanTextFile()
        {
            var tempFile = Path.Combine(Path.GetTempPath(), $"clean_{Guid.NewGuid():N}.txt");
            File.WriteAllText(tempFile, "Hello, this is a perfectly normal text file.");

            var service = new AttachmentManagerService(_mockTextExtractor.Object);

            try
            {
                var isValid = service.ValidateMagicBytes(tempFile, out var error);
                Assert.True(isValid);
                Assert.Null(error);
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
        }

        [Fact]
        public async Task ProcessAttachmentAsync_DisguisedExeAsTxt_FailsMagicBytesValidation()
        {
            var tempFile = Path.Combine(Path.GetTempPath(), $"disguised_{Guid.NewGuid():N}.txt");
            byte[] mzHeader = new byte[] { 0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00 };
            File.WriteAllBytes(tempFile, mzHeader);

            var service = new AttachmentManagerService(_mockTextExtractor.Object);

            try
            {
                var result = await service.ProcessAttachmentAsync(tempFile);
                Assert.False(result.IsSuccess);
                Assert.Contains("PE/EXE", result.ErrorMessage);
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
        }

        [Theory]
        [InlineData(".png")]
        [InlineData(".jpg")]
        [InlineData(".jpeg")]
        [InlineData(".webp")]
        [InlineData(".bmp")]
        [InlineData(".mp3")]
        [InlineData(".wav")]
        public async Task ProcessAttachmentAsync_ImageOrAudio_ReturnsRevisedCapabilityMessage(string ext)
        {
            var tempFile = Path.Combine(Path.GetTempPath(), $"media_{Guid.NewGuid():N}{ext}");
            File.WriteAllBytes(tempFile, new byte[] { 0x01, 0x02, 0x03 });

            var service = new AttachmentManagerService(_mockTextExtractor.Object);

            try
            {
                var result = await service.ProcessAttachmentAsync(tempFile);
                Assert.False(result.IsSuccess);
                Assert.Equal(AttachmentManagerService.MediaCapabilityErrorMessage, result.ErrorMessage);
                Assert.Contains("Gemma 4 E4B", result.ErrorMessage);
                Assert.Contains("Fase 2 via proyektor multimodal mmproj", result.ErrorMessage);
                Assert.Contains("audio belum didukung", result.ErrorMessage);
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
        }

        [Fact]
        public void FormatTruncatedAttachment_ProducesExactContractFormat()
        {
            string formatted = AttachmentManagerService.FormatTruncatedAttachment(
                fileName: "database.sql",
                slicedText: "SELECT * FROM users;",
                allocatedTokens: 100,
                totalTokens: 250,
                charCap: 300,
                totalChars: 750);

            string expected = "[--- Lampiran terpotong: database.sql (dialokasikan 100/250 token, 300/750 karakter) ---]\nSELECT * FROM users;";
            Assert.Equal(expected, formatted);
        }

        [Fact]
        public void FormatAttachmentPayload_WrapsInXmlTag()
        {
            string payload = AttachmentManagerService.FormatAttachmentPayload("test.txt", "content here");
            Assert.Equal("<lampiran nama=\"test.txt\">\ncontent here\n</lampiran>", payload);
        }

        [Fact]
        public async Task ProcessAttachmentAsync_NonExistentFile_ReturnsFailureWithFileNotFoundMessage()
        {
            var nonExistentPath = Path.Combine(Path.GetTempPath(), $"deleted_source_{Guid.NewGuid():N}.txt");
            var service = new AttachmentManagerService(_mockTextExtractor.Object);

            var result = await service.ProcessAttachmentAsync(nonExistentPath);

            Assert.False(result.IsSuccess);
            Assert.NotNull(result.ErrorMessage);
            Assert.Contains("File not found", result.ErrorMessage);
            Assert.Equal(Path.GetFileName(nonExistentPath), result.FileName);
            Assert.Equal(nonExistentPath, result.FilePath);
            Assert.Empty(result.ExtractedText);
        }
    }
}
