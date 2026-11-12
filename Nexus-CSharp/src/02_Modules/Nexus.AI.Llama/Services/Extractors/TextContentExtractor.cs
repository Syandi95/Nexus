using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UtfUnknown;

namespace Nexus.AI.Llama.Services.Extractors
{
    /// <summary>
    /// Implements 5-tier fallback text extraction faithfully ported from Open WebUI retrieval loader
    /// (backend/open_webui/retrieval/loaders/main.py:277-370).
    /// Tier 1: BOM detection (UTF-8, UTF-16, UTF-32)
    /// Tier 2: Strict UTF-8 decoding
    /// Tier 3: UtfUnknown detection as hint mapped to CJK superset family + CJK density validation
    /// Tier 4: Non-CJK detected hint from UtfUnknown / Windows-1252
    /// Tier 5: Ultimate Fallback: ISO-8859-1 (Latin-1, guaranteed zero-exception)
    /// </summary>
    public class TextContentExtractor : ITextContentExtractor
    {
        static TextContentExtractor()
        {
            // Register code pages provider to support GB18030, Big5, EUC-KR, Shift_JIS, Windows-1252 in .NET Core
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        }

        private static readonly Encoding StrictUtf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

        // Port of Open WebUI _ENC_FAMILY dictionary (main.py:318-327)
        private static readonly Dictionary<string, string> EncFamily = new(StringComparer.OrdinalIgnoreCase)
        {
            { "gb2312", "gb18030" },
            { "gb18030", "gb18030" },
            { "gbk", "gb18030" },
            { "big5", "big5" },
            { "euckr", "euc-kr" },
            { "eucjp", "euc-jp" },
            { "iso2022jp", "euc-jp" },
            { "shiftjis", "shift_jis" },
            { "sjis", "shift_jis" }
        };

        // Port of Open WebUI base_order (main.py:330)
        private static readonly string[] BaseCjkOrder = { "gb18030", "big5", "euc-kr", "shift_jis" };

        private static readonly HashSet<byte> Windows1252SpecificBytes = new()
        {
            0x80, 0x82, 0x83, 0x84, 0x85, 0x86, 0x87, 0x88, 0x89, 0x8A, 0x8B, 0x8C, 0x8E,
            0x91, 0x92, 0x93, 0x94, 0x95, 0x96, 0x97, 0x98, 0x99, 0x9A, 0x9B, 0x9C, 0x9E, 0x9F
        };

        public (string Text, string DetectedEncoding) ExtractText(byte[] rawBytes)
        {
            if (rawBytes == null || rawBytes.Length == 0)
            {
                return (string.Empty, "utf-8");
            }

            // Tier 1: Check Byte Order Mark (BOM)
            if (TryDetectBom(rawBytes, out var bomText, out var bomEncoding))
            {
                return (bomText, bomEncoding);
            }

            // Tier 2: Strict UTF-8 decoding (fast path)
            try
            {
                string text = StrictUtf8.GetString(rawBytes);
                return (text, "utf-8");
            }
            catch (DecoderFallbackException)
            {
                // Not valid UTF-8, proceed to Tier 3
            }

            // Tier 3: Use UtfUnknown (C# equivalent of chardet in Open WebUI) as a hint, not ground truth
            string? detectedName = null;
            try
            {
                var detectionResult = CharsetDetector.DetectFromBytes(rawBytes);
                detectedName = detectionResult?.Detected?.EncodingName;
            }
            catch
            {
                // UtfUnknown analysis failed, proceed to base order
            }

            string detectedKey = (detectedName ?? string.Empty).ToLowerInvariant().Replace("-", "").Replace("_", "");
            EncFamily.TryGetValue(detectedKey, out var hintedCodec);

            // Reorder priority list: UtfUnknown-hinted codec first, then remaining CJK (Open WebUI lines 329-335)
            var ordered = new List<string>();
            if (!string.IsNullOrEmpty(hintedCodec) && Array.Exists(BaseCjkOrder, e => string.Equals(e, hintedCodec, StringComparison.OrdinalIgnoreCase)))
            {
                ordered.Add(hintedCodec);
                foreach (var enc in BaseCjkOrder)
                {
                    if (!string.Equals(enc, hintedCodec, StringComparison.OrdinalIgnoreCase))
                    {
                        ordered.Add(enc);
                    }
                }
            }
            else
            {
                ordered.AddRange(BaseCjkOrder);
            }

            foreach (var codecName in ordered)
            {
                try
                {
                    var enc = Encoding.GetEncoding(codecName, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
                    string text = enc.GetString(rawBytes);

                    if (!string.IsNullOrWhiteSpace(text) && HasCjkCharacters(text, 0.05) && !HasExcessivePuaOrSmallForms(text))
                    {
                        return (text, enc.WebName.ToLowerInvariant());
                    }
                }
                catch (Exception)
                {
                    continue;
                }
            }

            // Tier 4: If UtfUnknown gave a non-CJK answer, try it directly (Open WebUI lines 351-364)
            if (!string.IsNullOrEmpty(detectedName))
            {
                try
                {
                    var nonCjkEnc = Encoding.GetEncoding(detectedName, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
                    string candidate = nonCjkEnc.GetString(rawBytes);
                    return (candidate, nonCjkEnc.WebName.ToLowerInvariant());
                }
                catch (Exception)
                {
                    // Proceed to Western check / Latin-1
                }
            }

            // Check Windows-1252 printable extension bytes
            bool hasWin1252Specific = false;
            for (int i = 0; i < rawBytes.Length; i++)
            {
                if (Windows1252SpecificBytes.Contains(rawBytes[i]))
                {
                    hasWin1252Specific = true;
                    break;
                }
            }

            if (hasWin1252Specific)
            {
                try
                {
                    var win1252 = Encoding.GetEncoding(1252, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
                    string candidate = win1252.GetString(rawBytes);
                    return (candidate, "windows-1252");
                }
                catch (Exception)
                {
                    // Fall through to Latin-1
                }
            }

            // Tier 5: Ultimate Fallback (Latin-1 / ISO-8859-1) - Every byte 0x00..0xFF is valid Unicode (Open WebUI line 366)
            string latin1Text = Encoding.Latin1.GetString(rawBytes);
            return (latin1Text, "iso-8859-1");
        }

        public async Task<(string Text, string DetectedEncoding)> ExtractTextFromFileAsync(string filePath, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("File path cannot be null or empty", nameof(filePath));

            if (!File.Exists(filePath))
                throw new FileNotFoundException($"File not found: {filePath}", filePath);

            byte[] bytes = await File.ReadAllBytesAsync(filePath, ct).ConfigureAwait(false);
            return ExtractText(bytes);
        }

        private static bool TryDetectBom(byte[] bytes, out string text, out string encodingName)
        {
            text = string.Empty;
            encodingName = string.Empty;

            if (bytes.Length >= 4)
            {
                // UTF-32 LE: FF FE 00 00
                if (bytes[0] == 0xFF && bytes[1] == 0xFE && bytes[2] == 0x00 && bytes[3] == 0x00)
                {
                    text = Encoding.UTF32.GetString(bytes, 4, bytes.Length - 4);
                    encodingName = "utf-32le";
                    return true;
                }
                // UTF-32 BE: 00 00 FE FF
                if (bytes[0] == 0x00 && bytes[1] == 0x00 && bytes[2] == 0xFE && bytes[3] == 0xFF)
                {
                    var utf32Be = new UTF32Encoding(bigEndian: true, byteOrderMark: true);
                    text = utf32Be.GetString(bytes, 4, bytes.Length - 4);
                    encodingName = "utf-32be";
                    return true;
                }
            }

            if (bytes.Length >= 3)
            {
                // UTF-8 BOM: EF BB BF
                if (bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                {
                    text = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
                    encodingName = "utf-8-bom";
                    return true;
                }
            }

            if (bytes.Length >= 2)
            {
                // UTF-16 LE: FF FE
                if (bytes[0] == 0xFF && bytes[1] == 0xFE)
                {
                    text = Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
                    encodingName = "utf-16le";
                    return true;
                }
                // UTF-16 BE: FE FF
                if (bytes[0] == 0xFE && bytes[1] == 0xFF)
                {
                    text = Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
                    encodingName = "utf-16be";
                    return true;
                }
            }

            return false;
        }

        private static bool HasExcessivePuaOrSmallForms(string text)
        {
            if (string.IsNullOrEmpty(text))
                return false;

            int badCount = 0;
            int total = 0;

            for (int i = 0; i < text.Length; i++)
            {
                char ch = text[i];
                if (char.IsWhiteSpace(ch))
                    continue;

                total++;
                int cp = char.ConvertToUtf32(text, i);
                if (char.IsSurrogate(ch))
                {
                    i++;
                }

                if ((cp >= 0xE000 && cp <= 0xF8FF) || (cp >= 0xFE50 && cp <= 0xFE6F))
                {
                    badCount++;
                }
            }

            return total > 0 && ((double)badCount / total) > 0.01;
        }

        /// <summary>
        /// Checks if decoded text contains a meaningful proportion of CJK characters.
        /// Faithful port of Open WebUI _has_cjk_characters (backend/open_webui/retrieval/loaders/main.py:373-410).
        /// </summary>
        public static bool HasCjkCharacters(string text, double threshold = 0.05)
        {
            if (string.IsNullOrEmpty(text))
                return false;

            int cjkCount = 0;
            int total = 0;

            for (int i = 0; i < text.Length; i++)
            {
                char ch = text[i];
                if (char.IsWhiteSpace(ch))
                    continue;

                total++;
                int cp = char.ConvertToUtf32(text, i);
                if (char.IsSurrogate(ch))
                {
                    i++;
                }

                if ((cp >= 0x4E00 && cp <= 0x9FFF)     // CJK Unified Ideographs
                    || (cp >= 0x3400 && cp <= 0x4DBF)   // CJK Extension A
                    || (cp >= 0x20000 && cp <= 0x2A6DF) // CJK Extension B
                    || (cp >= 0x2A700 && cp <= 0x2B73F) // CJK Extension C
                    || (cp >= 0x2B740 && cp <= 0x2B81F) // CJK Extension D
                    || (cp >= 0xF900 && cp <= 0xFAFF)   // CJK Compatibility Ideographs
                    || (cp >= 0x3000 && cp <= 0x303F)   // CJK Symbols and Punctuation
                    || (cp >= 0x3040 && cp <= 0x309F)   // Hiragana
                    || (cp >= 0x30A0 && cp <= 0x30FF)   // Katakana
                    || (cp >= 0xAC00 && cp <= 0xD7AF)   // Hangul Syllables
                    || (cp >= 0xFF00 && cp <= 0xFFEF))  // Halfwidth and Fullwidth Forms
                {
                    cjkCount++;
                }
            }

            if (total == 0)
                return false;

            return ((double)cjkCount / total) >= threshold;
        }
    }
}
