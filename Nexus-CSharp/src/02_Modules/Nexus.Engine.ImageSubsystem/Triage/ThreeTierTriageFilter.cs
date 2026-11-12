using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Nexus.Contracts.ReferenceData;
using Nexus.Engine.ImageSubsystem.Ocr;

namespace Nexus.Engine.ImageSubsystem.Triage;

/// <summary>
/// Filter Three-Tier Triage in-memory super cepat (15-90ms) untuk menyaring kotak teks hasil OCR sebelum dikirim ke Gemma 4 E4B.
/// Layer 1: Ambang batas kualitas OCR (DetScore & RecConfidence &lt; 0.70).
/// Layer 2: Heuristik anomali leksikal (awalan 'lj', CamelCase bertumpuk, angka di tengah kata).
/// Layer 3: Fuzzy matching terhadap database referensi resmi dengan aturan 'Exact Match Trumps Near-Miss'.
/// </summary>
public sealed class ThreeTierTriageFilter
{
    private static readonly Regex RxLowerLConsonant = new(@"\bl[bcdfghjklmnpqrstvwxyz]", RegexOptions.Compiled);
    private static readonly Regex RxCamelCaseInProse = new(@"[a-z][A-Z]", RegexOptions.Compiled);
    private static readonly Regex RxDigitInWord = new(@"[a-zA-Z]+[0-9]+[a-zA-Z]+", RegexOptions.Compiled);

    private static readonly char[] TokenSeparators = { ' ', ',', ':', '-', '.', '/', '–', ';' };

    public float MinDetScoreThreshold { get; set; } = 0.70f;
    public float MinRecConfidenceThreshold { get; set; } = 0.70f;
    public float MinReferenceSimilarityThreshold { get; set; } = 0.80f;

    /// <summary>
    /// Mengevaluasi daftar OcrBoxModel terhadap Three-Tier Triage.
    /// Mengembalikan daftar temuan untuk box yang ter-flag (bermasalah).
    /// </summary>
    public List<TriageFinding> Evaluate(
        IEnumerable<OcrBoxModel> boxes,
        ReferenceDataset? referenceDataset = null)
    {
        var refEntities = referenceDataset?.Entries?.Select(e => e.Value).ToList() ?? new List<string>();
        return Evaluate(boxes, refEntities);
    }

    /// <summary>
    /// Mengevaluasi daftar OcrBoxModel terhadap daftar string entitas referensi resmi.
    /// </summary>
    public List<TriageFinding> Evaluate(
        IEnumerable<OcrBoxModel> boxes,
        IReadOnlyList<string>? referenceEntities)
    {
        ArgumentNullException.ThrowIfNull(boxes);

        var findings = new List<TriageFinding>();
        var refList = referenceEntities ?? Array.Empty<string>();

        // Pre-build exact set dan metadata kata untuk Layer 3
        var exactRefMap = new HashSet<string>(refList.Select(NormalizeText), StringComparer.OrdinalIgnoreCase);

        var parsedRefs = new List<RefEntityInfo>(refList.Count);
        foreach (var r in refList)
        {
            if (string.IsNullOrWhiteSpace(r)) continue;
            string clean = r.Trim();
            var tokens = clean.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            parsedRefs.Add(new RefEntityInfo
            {
                Original = clean,
                Normalized = NormalizeText(clean),
                WordCount = tokens.Length,
                Length = clean.Length
            });
        }

        foreach (var b in boxes)
        {
            // Abaikan noise deteksi OCR (teks kosong, <= 1 karakter, atau simbol non-alfanumerik)
            if (!PpOcrEngine.IsMeaningfulText(b.Text))
            {
                continue;
            }

            var finding = new TriageFinding { Box = b };
            string text = b.Text?.Trim() ?? string.Empty;

            // ==========================================
            // LAYER 1: Ambang Kualitas OCR
            // ==========================================
            if (b.DetScore < MinDetScoreThreshold)
            {
                finding.LayerFlags |= TriageLayerFlags.Layer1_LowDetScore;
                finding.Reasons.Add($"Layer 1: Low Detection Score ({b.DetScore:F2})");
            }

            if (b.RecConfidence < MinRecConfidenceThreshold)
            {
                finding.LayerFlags |= TriageLayerFlags.Layer1_LowRecConfidence;
                finding.Reasons.Add($"Layer 1: Low Recognition Confidence ({b.RecConfidence:F2})");
            }

            // ==========================================
            // LAYER 2: Heuristik Anomali Leksikal
            // ==========================================
            if (!string.IsNullOrEmpty(text))
            {
                // 2A: Huruf kecil 'l' diikuti konsonan (misal: 'ljin', 'lklan')
                if (RxLowerLConsonant.IsMatch(text))
                {
                    finding.LayerFlags |= TriageLayerFlags.Layer2A_SuspiciousPrefix;
                    var match = RxLowerLConsonant.Match(text);
                    finding.Reasons.Add($"Layer 2A: Anomali awalan 'l+konsonan' ('{match.Value}')");
                }

                // 2B: CamelCase ganjil di tengah kalimat (kecuali hashtag atau url)
                if (!text.StartsWith("#") &&
                    !text.Contains("http", StringComparison.OrdinalIgnoreCase) &&
                    !text.Contains("www", StringComparison.OrdinalIgnoreCase))
                {
                    if (RxCamelCaseInProse.IsMatch(text))
                    {
                        var matches = RxCamelCaseInProse.Matches(text);
                        // Anomali jika kapitalisasi bertumpuk (multiple transitions seperti 'YangMaJuMdaS')
                        // atau transisi tunggal di dalam kalimat prosa multi-kata
                        bool hasMultipleTransitions = matches.Count >= 2;
                        bool isMixedCaseInProse = matches.Count >= 1 && text.Contains(' ') && text.Length > 8;

                        if (hasMultipleTransitions || isMixedCaseInProse)
                        {
                            finding.LayerFlags |= TriageLayerFlags.Layer2B_IrregularCasing;
                            finding.Reasons.Add("Layer 2B: Kapitalisasi bertumpuk / teks gabungan");
                        }
                    }
                }

                // 2C: Angka menyusup di tengah kata alfabetik
                if (RxDigitInWord.IsMatch(text))
                {
                    finding.LayerFlags |= TriageLayerFlags.Layer2C_DigitInWord;
                    finding.Reasons.Add("Layer 2C: Angka menyusup di dalam kata");
                }
            }

            // ==========================================
            // LAYER 3: Fuzzy Match vs Reference Data
            // Aturan: 'Exact Match Trumps Near-Miss'
            // ==========================================
            if (parsedRefs.Count > 0 && !string.IsNullOrEmpty(text))
            {
                EvaluateLayer3(b, text, parsedRefs, exactRefMap, finding);
            }

            if (finding.IsFlagged)
            {
                findings.Add(finding);
            }
        }

        return findings;
    }

    private void EvaluateLayer3(
        OcrBoxModel box,
        string text,
        List<RefEntityInfo> parsedRefs,
        HashSet<string> exactRefMap,
        TriageFinding finding)
    {
        string normText = NormalizeText(text);

        // 1. Exact Match Short-Circuit di level box
        if (exactRefMap.Contains(normText))
        {
            // Teks box cocok 100% dengan entitas resmi -> kebal dari penandaan Layer 3
            return;
        }

        var tokens = text.Split(TokenSeparators, StringSplitOptions.RemoveEmptyEntries);
        int tokenCount = tokens.Length;

        string? bestWindow = null;
        string? bestRef = null;
        int bestDist = int.MaxValue;
        float bestSim = 0f;
        bool exactMatchFound = false;

        foreach (var refItem in parsedRefs)
        {
            int refLen = refItem.Length;
            int refWordCount = refItem.WordCount;

            // Direct comparison jika panjang mirip
            int textLen = text.Length;
            if (Math.Abs(textLen - refLen) <= 2)
            {
                int directDist = LevenshteinDistance(normText, refItem.Normalized);
                if (directDist == 0)
                {
                    exactMatchFound = true;
                    break;
                }

                float directSim = CalculateSimilarity(normText, refItem.Normalized, directDist);
                int maxAllowedDist = refLen < 8 ? 1 : 2;
                if (directDist <= maxAllowedDist && directSim >= MinReferenceSimilarityThreshold)
                {
                    if (directDist < bestDist)
                    {
                        bestDist = directDist;
                        bestSim = directSim;
                        bestWindow = text;
                        bestRef = refItem.Original;
                    }
                }
            }

            // Sliding window token untuk multi-word entities
            if (tokenCount >= refWordCount)
            {
                for (int i = 0; i <= tokenCount - refWordCount; i++)
                {
                    string window = string.Join(" ", tokens.Skip(i).Take(refWordCount));
                    string normWindow = NormalizeText(window);

                    // Cek exact match short-circuit pada window
                    if (exactRefMap.Contains(normWindow))
                    {
                        exactMatchFound = true;
                        break;
                    }

                    int winLen = window.Length;
                    if (Math.Abs(winLen - refLen) > 2) continue;

                    int winDist = LevenshteinDistance(normWindow, refItem.Normalized);
                    if (winDist == 0)
                    {
                        exactMatchFound = true;
                        break;
                    }

                    float winSim = CalculateSimilarity(normWindow, refItem.Normalized, winDist);
                    int maxAllowedDist = refLen < 8 ? 1 : 2;
                    if (winDist <= maxAllowedDist && winSim >= MinReferenceSimilarityThreshold)
                    {
                        if (winDist < bestDist)
                        {
                            bestDist = winDist;
                            bestSim = winSim;
                            bestWindow = window;
                            bestRef = refItem.Original;
                        }
                    }
                }

                if (exactMatchFound) break;
            }
        }

        // Flag HANYA jika TIDAK ada exact match dan ditemukan near-miss berjarak > 0
        if (!exactMatchFound && bestWindow != null && bestDist > 0 && bestRef != null)
        {
            finding.LayerFlags |= TriageLayerFlags.Layer3_ReferenceMismatch;
            finding.MatchedWindow = bestWindow;
            finding.MatchedReferenceEntity = bestRef;
            finding.LevenshteinDistance = bestDist;
            finding.SimilarityScore = bestSim;
            finding.Reasons.Add($"Layer 3: Kemungkinan typo entitas referensi ('{bestWindow}' vs resmi '{bestRef}', dist={bestDist}, sim={bestSim:P0})");
        }
    }

    public static string NormalizeText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var chars = text.Trim().ToLowerInvariant().Where(c => !char.IsPunctuation(c)).ToArray();
        return new string(chars).Replace("  ", " ").Trim();
    }

    public static int LevenshteinDistance(string s, string t)
    {
        if (string.IsNullOrEmpty(s)) return t?.Length ?? 0;
        if (string.IsNullOrEmpty(t)) return s.Length;

        int n = s.Length;
        int m = t.Length;
        var d = new int[n + 1, m + 1];

        for (int i = 0; i <= n; i++) d[i, 0] = i;
        for (int j = 0; j <= m; j++) d[0, j] = j;

        for (int i = 1; i <= n; i++)
        {
            for (int j = 1; j <= m; j++)
            {
                int cost = (t[j - 1] == s[i - 1]) ? 0 : 1;
                d[i, j] = Math.Min(
                    Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                    d[i - 1, j - 1] + cost);
            }
        }

        return d[n, m];
    }

    public static float CalculateSimilarity(string s, string t, int distance)
    {
        int maxLen = Math.Max(s.Length, t.Length);
        if (maxLen == 0) return 1.0f;
        return 1.0f - ((float)distance / maxLen);
    }

    private sealed class RefEntityInfo
    {
        public required string Original { get; init; }
        public required string Normalized { get; init; }
        public required int WordCount { get; init; }
        public required int Length { get; init; }
    }
}
