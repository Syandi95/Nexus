using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Nexus.AI.Llama.Services.Extractors;
using Xunit;

namespace Nexus.AI.Llama.Tests
{
    public class TextContentExtractorTests
    {
        private readonly TextContentExtractor _extractor = new();

        [Fact]
        public void ExtractText_EmptyBytes_ReturnsEmptyUtf8()
        {
            var (text, encoding) = _extractor.ExtractText(Array.Empty<byte>());
            Assert.Equal(string.Empty, text);
            Assert.Equal("utf-8", encoding);
        }

        [Fact]
        public void ExtractText_Tier1_Utf8WithBom_ReturnsUtf8Bom()
        {
            var rawText = "Hello World with UTF-8 BOM!";
            var bom = new byte[] { 0xEF, 0xBB, 0xBF };
            var contentBytes = Encoding.UTF8.GetBytes(rawText);
            var fullBytes = new byte[bom.Length + contentBytes.Length];
            Buffer.BlockCopy(bom, 0, fullBytes, 0, bom.Length);
            Buffer.BlockCopy(contentBytes, 0, fullBytes, bom.Length, contentBytes.Length);

            var (text, encoding) = _extractor.ExtractText(fullBytes);
            Assert.Equal(rawText, text);
            Assert.Equal("utf-8-bom", encoding);
        }

        [Fact]
        public void ExtractText_Tier1_Utf16LeWithBom_ReturnsUtf16Le()
        {
            var rawText = "NEXUS Attachment UTF-16 LE Test";
            var bom = new byte[] { 0xFF, 0xFE };
            var contentBytes = Encoding.Unicode.GetBytes(rawText);
            var fullBytes = new byte[bom.Length + contentBytes.Length];
            Buffer.BlockCopy(bom, 0, fullBytes, 0, bom.Length);
            Buffer.BlockCopy(contentBytes, 0, fullBytes, bom.Length, contentBytes.Length);

            var (text, encoding) = _extractor.ExtractText(fullBytes);
            Assert.Equal(rawText, text);
            Assert.Equal("utf-16le", encoding);
        }

        [Fact]
        public void ExtractText_Tier1_Utf16BeWithBom_ReturnsUtf16Be()
        {
            var rawText = "NEXUS Attachment UTF-16 BE Test";
            var bom = new byte[] { 0xFE, 0xFF };
            var contentBytes = Encoding.BigEndianUnicode.GetBytes(rawText);
            var fullBytes = new byte[bom.Length + contentBytes.Length];
            Buffer.BlockCopy(bom, 0, fullBytes, 0, bom.Length);
            Buffer.BlockCopy(contentBytes, 0, fullBytes, bom.Length, contentBytes.Length);

            var (text, encoding) = _extractor.ExtractText(fullBytes);
            Assert.Equal(rawText, text);
            Assert.Equal("utf-16be", encoding);
        }

        [Fact]
        public void ExtractText_Tier2_StrictUtf8WithoutBom_ReturnsUtf8()
        {
            var rawText = "Plain UTF-8 text: Halo dunia, selamat datang di NEXUS!";
            var bytes = Encoding.UTF8.GetBytes(rawText);

            var (text, encoding) = _extractor.ExtractText(bytes);
            Assert.Equal(rawText, text);
            Assert.Equal("utf-8", encoding);
        }

        [Fact]
        public void ExtractText_Tier3_Gb18030_DecodesAccurately()
        {
            // Chinese sentence in GB18030
            var cjkText = "这是一段用于测试 GB18030 编码的中文文本，验证 CJK 密度与解码。";
            var gb18030Enc = Encoding.GetEncoding("gb18030");
            var bytes = gb18030Enc.GetBytes(cjkText);

            var (text, encoding) = _extractor.ExtractText(bytes);
            Assert.Equal(cjkText, text);
            Assert.Equal("gb18030", encoding);
        }

        [Fact]
        public void ExtractText_Tier3_ShiftJis_DecodesAccurately()
        {
            // Japanese text in Shift_JIS
            var japaneseText = "これは日本語のシフトJISエンコーディングのテストです。";
            var shiftJisEnc = Encoding.GetEncoding(932);
            var bytes = shiftJisEnc.GetBytes(japaneseText);

            var (text, encoding) = _extractor.ExtractText(bytes);
            Assert.Equal(japaneseText, text);
            Assert.Equal("shift_jis", encoding);
        }

        [Fact]
        public void ExtractText_Tier3_Big5_DecodesAccurately()
        {
            // Traditional Chinese in Big5
            var big5Text = "這是一段繁體中文的測試內容，用於驗證 Big5 編碼檢測。";
            var big5Enc = Encoding.GetEncoding(950);
            var bytes = big5Enc.GetBytes(big5Text);

            var (text, encoding) = _extractor.ExtractText(bytes);
            Assert.Equal(big5Text, text);
            Assert.Equal("big5", encoding);
        }

        [Fact]
        public void ExtractText_Tier3_EucKr_DecodesAccurately()
        {
            // Korean in EUC-KR
            var koreanText = "안녕하세요. 이것은 한국어 EUC-KR 인코딩 테스트입니다.";
            var eucKrEnc = Encoding.GetEncoding(51949);
            var bytes = eucKrEnc.GetBytes(koreanText);

            var (text, encoding) = _extractor.ExtractText(bytes);
            Assert.Equal(koreanText, text);
            Assert.Equal("euc-kr", encoding);
        }

        [Fact]
        public void ExtractText_Tier4_Windows1252_WithSmartQuotes()
        {
            // Windows-1252 text containing smart quotes (0x93, 0x94) which are invalid UTF-8 and non-CJK
            var win1252Enc = Encoding.GetEncoding(1252);
            var rawText = "This has “smart quotes” and € currency.";
            var bytes = win1252Enc.GetBytes(rawText);

            var (text, encoding) = _extractor.ExtractText(bytes);
            Assert.Equal(rawText, text);
            Assert.Equal("windows-1252", encoding);
        }

        [Fact]
        public void ExtractText_Tier5_Latin1_GuaranteedZeroException()
        {
            // High byte array that fails UTF-8, fails CJK (invalid trail bytes in GB18030/Big5),
            // and contains undefined Windows-1252 bytes (0x81, 0x8D, etc.), decoded safely by Latin-1
            var exoticBytes = new byte[] { 0x81, 0x1F, 0x8D, 0x05, 0x8F, 0x12, 0x90, 0x02 };

            var (text, encoding) = _extractor.ExtractText(exoticBytes);
            Assert.False(string.IsNullOrEmpty(text));
            Assert.Equal("iso-8859-1", encoding);
        }

        [Fact]
        public void HasCjkCharacters_Evaluation()
        {
            Assert.False(TextContentExtractor.HasCjkCharacters("Just normal English sentence with no CJK"));
            Assert.True(TextContentExtractor.HasCjkCharacters("你好世界"));
            Assert.True(TextContentExtractor.HasCjkCharacters("Hello world! 你好世界 这是中文测试。"));

            // Below 5% threshold: 1 Chinese char in a long 100-char string
            var longText = new string('A', 100) + "中";
            Assert.False(TextContentExtractor.HasCjkCharacters(longText, 0.05));
        }

        [Fact]
        public async Task ExtractTextFromFileAsync_ValidFile_ReturnsText()
        {
            var tempFile = Path.Combine(Path.GetTempPath(), $"nexus_test_{Guid.NewGuid():N}.txt");
            const string content = "Sample file content for async extraction test.";
            await File.WriteAllTextAsync(tempFile, content, new UTF8Encoding(false));

            try
            {
                var (text, encoding) = await _extractor.ExtractTextFromFileAsync(tempFile);
                Assert.Equal(content, text);
                Assert.Equal("utf-8", encoding);
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
        }

        [Fact]
        public async Task ExtractTextFromFileAsync_MissingFile_ThrowsFileNotFoundException()
        {
            var nonExistentPath = Path.Combine(Path.GetTempPath(), $"missing_{Guid.NewGuid():N}.txt");
            await Assert.ThrowsAsync<FileNotFoundException>(() => _extractor.ExtractTextFromFileAsync(nonExistentPath));
        }
    }
}
