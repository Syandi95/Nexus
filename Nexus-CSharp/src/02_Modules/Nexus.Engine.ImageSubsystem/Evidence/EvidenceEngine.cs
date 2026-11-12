using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Nexus.Contracts.ReferenceData;
using Nexus.Engine.ImageSubsystem.Ocr;

namespace Nexus.Engine.ImageSubsystem.Evidence;

/// <summary>
/// Parameter pembobotan bukti evaluasi kandidat.
/// Nilai awal merupakan baseline eksperimen yang dapat diuji terhadap benchmark precision/recall.
/// </summary>
public sealed class CandidateScoringParameters
{
    public double SimilarityWeight { get; set; } = 0.4275;
    public double ReferenceRelevanceWeight { get; set; } = 0.1900;
    public double CharacterAnomalyWeight { get; set; } = 0.1425;
    public double ContextRelevanceWeight { get; set; } = 0.0950;
    public double OcrConfidenceWeight { get; set; } = 0.0475;
    public double TokenShapeWeight { get; set; } = 0.0285;
    public double EntityRelevanceWeight { get; set; } = 0.0500;
    public double FrequencyTieBreakerWeight { get; set; } = 0.0190;
}

/// <summary>
/// Rincian skor multi-dimensi per kandidat koreksi.
/// </summary>
public sealed class CandidateScoreResult
{
    public double PrimaryScore { get; set; }
    public double FinalScore { get; set; }
    public double EntityRelevance { get; set; }
    public double LexicalScore { get; set; }
    public double ReferenceScore { get; set; }
    public double AnomalyScore { get; set; }
    public double ContextScore { get; set; }
    public double OcrScore { get; set; }
    public double ShapeScore { get; set; }
    public double FrequencyScore { get; set; }
}

/// <summary>
/// Evidence Engine (Radar) untuk menyaring dan mengemas kandidat anomali pra-cetak sebelum dikirim ke AI Judge (Gemma 4 E4B).
/// Berfungsi murni sebagai radar pengumpul bukti (soft evidence) dan pembuat kandidat, TIDAK memutuskan typo final atau auto-correct.
/// Mengimplementasikan Lapisan Referensi: Base Language Lexicon, User Dictionary, Structured Reference Entities, dan Entity Reference.
/// </summary>
public sealed class EvidenceEngine
{
    private sealed class ParsedEntity
    {
        public required EntityReferenceItem Item { get; init; }
        public required string[] CanonicalTokens { get; init; }
        public required List<string[]> UnambiguousAliasTokens { get; init; }
        public required List<string[]> AmbiguousAliasTokens { get; init; }
    }

    private readonly HashSet<string> _exactDict = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _userDict = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _locationDict = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<StructuredReferencePhrase> _structuredReferences = new();
    private readonly BKTree _bkTree = new();
    private readonly Dictionary<string, int> _wordFrequencyRank = new(StringComparer.OrdinalIgnoreCase);

    private readonly List<ParsedEntity> _entityReferences = new();
    private readonly HashSet<string> _entitySingleWordSet = new(StringComparer.OrdinalIgnoreCase);
    private BKTree _entityBKTree = new();

    public int MaxDistance { get; set; } = 3;
    public float MinVerticalOverlap { get; set; } = SpatialContextCalculator.DefaultMinVerticalOverlap;
    public float MinOcrConfidenceThreshold { get; set; } = 0.70f;
    public float MinSimilarityThreshold { get; set; } = 0.80f;

    public CandidateScoringParameters ScoringParams { get; set; } = new();

    private readonly HashSet<string> _domainDict = new(StringComparer.OrdinalIgnoreCase);

    public int DictionarySize => _exactDict.Count;
    public int BKTreeCount => _bkTree.Count;
    public int LocationCount => _locationDict.Count;
    public int DomainDictionaryCount => _domainDict.Count;
    public int UserDictionaryCount => _userDict.Count;
    public int StructuredReferenceCount => _structuredReferences.Count;
    public int EntityReferenceCount => _entityReferences.Count;

    private static readonly HashSet<string> LocationRegionMarkers = new(
        ReferenceConstants.AdministrativeRegionMarkers,
        StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Mengecek apakah sebuah string konteks spasial/baris merupakan area administratif atau alamat resmi.
    /// Digunakan untuk semantic region gating bonus entitas referensi/lokasi.
    /// </summary>
    public static bool IsSemanticLocationRegion(string spatialContext)
    {
        if (string.IsNullOrWhiteSpace(spatialContext)) return false;
        var tokens = spatialContext.Split(WordDelimiters, StringSplitOptions.RemoveEmptyEntries);
        foreach (var t in tokens)
        {
            string norm = t.Trim().ToUpperInvariant();
            if (LocationRegionMarkers.Contains(norm))
                return true;
        }
        return false;
    }

    private static readonly char[] WordDelimiters = { ' ', ',', ':', ';', '.', '—', '-', '(', ')', '[', ']', '\"', '\'', '!', '?' };

    public EvidenceEngine()
    {
    }

    /// <summary>
    /// Memuat domain lexicon kustom (istilah kuliner, perdagangan, sembako umum) secara deklaratif.
    /// Kata-kata ini diakui sebagai kosakata valid di Base Lexicon & BKTree tanpa merusak berkas 20K.
    /// </summary>
    public void LoadDomainLexicon(IEnumerable<string> words)
    {
        ArgumentNullException.ThrowIfNull(words);
        foreach (var raw in words)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            string w = raw.Trim().ToUpperInvariant();
            if (w.Length > 0)
            {
                _domainDict.Add(w);
                if (_exactDict.Add(w))
                {
                    _bkTree.Add(w);
                    if (!_wordFrequencyRank.ContainsKey(w))
                    {
                        _wordFrequencyRank[w] = 10000;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Memuat kamus 20K kata bawaan sistem ke dalam Base Lexicon dan BK-Tree.
    /// Menyimpan urutan baris sebagai rank frekuensi leksikon. Menghilangkan duplikasi secara otomatis.
    /// </summary>
    public void LoadBaseLexicon(IEnumerable<string> words)
    {
        ArgumentNullException.ThrowIfNull(words);
        int rank = 0;
        foreach (var raw in words)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            string w = raw.Trim().ToUpperInvariant();
            if (w.Length > 0)
            {
                rank++;
                if (_exactDict.Add(w))
                {
                    _bkTree.Add(w);
                    _wordFrequencyRank[w] = rank;
                }
                else if (!_wordFrequencyRank.ContainsKey(w))
                {
                    _wordFrequencyRank[w] = rank;
                }
            }
        }
    }

    /// <summary>
    /// Kompatibilitas mundur untuk memuat kamus 20K kata.
    /// </summary>
    public void LoadDictionary(IEnumerable<string> words) => LoadBaseLexicon(words);

    /// <summary>
    /// Memuat kamus kata dari file teks (baris demi baris).
    /// </summary>
    public void LoadDictionaryFromFile(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Berkas kamus tidak ditemukan di: {filePath}");

        LoadDictionary(File.ReadLines(filePath));
    }

    /// <summary>
    /// Memuat kamus kustom pengguna (User Dictionary) untuk menghilangkan status OOV.
    /// Hanya menyimpan entri yang diinput pengguna tanpa memecah token konstituen.
    /// Perlindungan frasa multi-kata ditangani secara terstruktur via LoadStructuredReferences.
    /// </summary>
    public void LoadUserDictionary(IEnumerable<string> words)
    {
        ArgumentNullException.ThrowIfNull(words);
        _userDict.Clear();
        foreach (var raw in words)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            string norm = raw.Trim().ToUpperInvariant();
            if (norm.Length == 0) continue;

            _userDict.Add(norm);
        }
    }

    /// <summary>
    /// Memuat entitas referensi terstruktur (frasa multi-kata seperti Desa, Kecamatan, Puskesmas, Instansi).
    /// Entitas diurutkan berdasarkan jumlah token terpanjang untuk menjamin pencocokan greedy.
    /// </summary>
    public void LoadStructuredReferences(IEnumerable<StructuredReferencePhrase> phrases)
    {
        ArgumentNullException.ThrowIfNull(phrases);
        _structuredReferences.Clear();
        foreach (var p in phrases)
        {
            if (p == null || string.IsNullOrWhiteSpace(p.Text)) continue;
            string norm = p.Text.Trim().ToUpperInvariant();
            var tokens = norm.Split(WordDelimiters, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0) continue;

            p.NormalizedText = norm;
            p.Tokens = tokens;
            _structuredReferences.Add(p);

            // Jika entitas referensi adalah kata tunggal (misal "Sukamaju"),
            // daftarkan ke _locationDict dan _bkTree agar dapat dicari saat terjadi typo spelling
            if (tokens.Length == 1)
            {
                string singleWord = tokens[0];
                _locationDict.Add(singleWord);
                if (_exactDict.Add(singleWord))
                {
                    _bkTree.Add(singleWord);
                }
            }
        }

        // Urutkan dari frasa terpanjang (token terbanyak) agar exact match memprioritaskan frasa lengkap
        _structuredReferences.Sort((a, b) => b.Tokens.Length.CompareTo(a.Tokens.Length));
    }

    /// <summary>
    /// Memuat entitas referensi resmi lintas 6 kategori (Bank, MobileOperator, TopUpPayment, EWallet, Culinary, InternetProvider).
    /// Mengimplementasikan Occurrence-Specific Protection: constituent multi-kata tidak ditambahkan ke leksikon global.
    /// BK-tree untuk pencarian fuzzy efisien pada ukuran dataset target, diverifikasi melalui benchmark.
    /// </summary>
    public void LoadEntityReferences(IEnumerable<EntityReferenceItem> entities)
    {
        ArgumentNullException.ThrowIfNull(entities);
        _entityReferences.Clear();
        _entitySingleWordSet.Clear();
        _entityBKTree = new BKTree();

        foreach (var item in entities)
        {
            if (item == null || !item.Active || string.IsNullOrWhiteSpace(item.Name))
                continue;

            string normName = item.Name.Trim().ToUpperInvariant();
            var canonicalTokens = normName.Split(WordDelimiters, StringSplitOptions.RemoveEmptyEntries);
            if (canonicalTokens.Length == 0) continue;

            var unambiguousAliases = new List<string[]>();
            if (item.Aliases != null)
            {
                foreach (var a in item.Aliases)
                {
                    if (string.IsNullOrWhiteSpace(a)) continue;
                    var aTokens = a.Trim().ToUpperInvariant().Split(WordDelimiters, StringSplitOptions.RemoveEmptyEntries);
                    if (aTokens.Length > 0)
                    {
                        unambiguousAliases.Add(aTokens);
                        if (aTokens.Length == 1)
                        {
                            _entitySingleWordSet.Add(aTokens[0]);
                            _entityBKTree.Add(aTokens[0]);
                        }
                    }
                }
            }

            var ambiguousAliases = new List<string[]>();
            if (item.AmbiguousAliases != null)
            {
                foreach (var a in item.AmbiguousAliases)
                {
                    if (string.IsNullOrWhiteSpace(a)) continue;
                    var aTokens = a.Trim().ToUpperInvariant().Split(WordDelimiters, StringSplitOptions.RemoveEmptyEntries);
                    if (aTokens.Length > 0)
                    {
                        ambiguousAliases.Add(aTokens);
                        if (aTokens.Length == 1)
                        {
                            _entitySingleWordSet.Add(aTokens[0]);
                            _entityBKTree.Add(aTokens[0]);
                        }
                    }
                }
            }

            // Single token canonical name dimasukkan ke indeks kata tunggal & BKTree entitas
            // Multi-token constituent words TIDAK dimasukkan ke BKTree leksikon mandiri
            if (canonicalTokens.Length == 1)
            {
                _entitySingleWordSet.Add(canonicalTokens[0]);
                _entityBKTree.Add(canonicalTokens[0]);
            }

            _entityReferences.Add(new ParsedEntity
            {
                Item = item,
                CanonicalTokens = canonicalTokens,
                UnambiguousAliasTokens = unambiguousAliases,
                AmbiguousAliasTokens = ambiguousAliases
            });
        }

        // Urutkan dari token terbanyak menurun untuk greedy matching
        _entityReferences.Sort((a, b) => b.CanonicalTokens.Length.CompareTo(a.CanonicalTokens.Length));
    }

    /// <summary>
    /// Inisialisasi otomatis menggunakan kamus default 20K kata jika belum dimuat.
    /// Mencari di direktori Assets modul, AppContext.BaseDirectory, atau scratch path.
    /// </summary>
    public void InitializeDefault(string? customDictionaryPath = null)
    {
        if (DictionarySize > 0) return;

        string[] candidatePaths =
        {
            customDictionaryPath ?? string.Empty,
            Path.Combine(AppContext.BaseDirectory, "Assets", "DictionaryMatch_20K.txt"),
            Path.Combine(AppContext.BaseDirectory, "DictionaryMatch_20K.txt"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "DictionaryMatch_20K.txt"),
            @"D:\NEXUS\Nexus-CSharp\src\02_Modules\Nexus.Engine.ImageSubsystem\Assets\DictionaryMatch_20K.txt",
            @"C:\Users\Syand\.gemini\antigravity\brain\61fe7a18-0a5c-4a7e-86e1-ba9bffbcf9e7\scratch\DictionaryMatch_20K.txt"
        };

        foreach (var p in candidatePaths)
        {
            if (!string.IsNullOrWhiteSpace(p) && File.Exists(p))
            {
                LoadDictionaryFromFile(p);
                break;
            }
        }

        // Muat kata kunci administratif bawaan dari ReferenceConstants
        LoadLocations(ReferenceConstants.BuiltInAdministrativeKeywords);

        // Muat domain lexicon bawaan deklaratif dari ReferenceConstants sebagai recovery/seed default
        LoadDomainLexicon(ReferenceConstants.BuiltInDomainPhrases);

        // Muat entity reference bawaan deklaratif dari ReferenceConstants sebagai recovery/seed default
        LoadEntityReferences(ReferenceConstants.BuiltInEntityReferences);
    }

    /// <summary>
    /// Memuat daftar entitas lokasi resmi ke dalam indeks lokasi sederhana.
    /// </summary>
    public void LoadLocations(IEnumerable<string> locations)
    {
        ArgumentNullException.ThrowIfNull(locations);
        foreach (var raw in locations)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            string loc = raw.Trim().ToUpperInvariant();
            if (loc.Length > 0)
            {
                _locationDict.Add(loc);
            }
        }
    }

    /// <summary>
    /// Mencoba hipotesis candidate segmentation pada token yang memiliki transisi huruf kecil ke kapital (camelCase/PascalCase).
    /// Segmentasi HANYA di-commit jika memenuhi konvergensi bukti:
    /// salah satu segmen merupakan kata kamus/referensi valid, dan segmen pasangannya memiliki bukti anomali/typo kuat.
    /// Token merek/teknis ("iPhone", "eBay", "PowerPoint", "YouTube", "OpenAI", "McDonald's", "WiFi") dipertahankan utuh.
    /// </summary>
    public bool TryEvaluateCaseTransitionSegmentation(
        string rawToken,
        out (string SubToken1, string SubToken2) segments)
    {
        segments = default;
        if (string.IsNullOrWhiteSpace(rawToken) || rawToken.Length < 4)
            return false;

        string normFull = rawToken.Trim().ToUpperInvariant();

        // 1. Jika token utuh sudah dikenal dalam kamus/user/lokasi, jangan pernah memecah
        if (_exactDict.Contains(normFull) || _userDict.Contains(normFull) || _locationDict.Contains(normFull))
            return false;

        // 2. Cari batas transisi huruf kecil -> huruf kapital [a-z][A-Z]
        // Contoh: "HarggaTerjangkau" -> split antara 'a' (idx 5) dan 'T' (idx 6)
        int splitIdx = -1;
        for (int i = 0; i < rawToken.Length - 1; i++)
        {
            if (char.IsLower(rawToken[i]) && char.IsUpper(rawToken[i + 1]))
            {
                // Awalan 1 huruf kecil: "iPhone", "eBay", "eCommerce" -> bukan typo penggabungan kata
                if (i == 0)
                {
                    return false;
                }
                splitIdx = i + 1;
                break;
            }
        }

        if (splitIdx <= 1 || splitIdx >= rawToken.Length - 1)
            return false;

        string s1 = rawToken.Substring(0, splitIdx);
        string s2 = rawToken.Substring(splitIdx);

        // Kedua segmen harus memiliki panjang wajar (minimal 3 huruf untuk kata dasar yang sah)
        if (s1.Length < 3 || s2.Length < 3)
            return false;

        string norm1 = s1.ToUpperInvariant();
        string norm2 = s2.ToUpperInvariant();

        bool s1Valid = _exactDict.Contains(norm1) || _userDict.Contains(norm1) || _locationDict.Contains(norm1) || _domainDict.Contains(norm1);
        bool s2Valid = _exactDict.Contains(norm2) || _userDict.Contains(norm2) || _locationDict.Contains(norm2) || _domainDict.Contains(norm2);

        // Kasus A: Jika KEDUA segmen valid (seperti "PowerPoint", "YouTube", "OpenAI"):
        // Tidak ada anomali typo! Ini adalah gabungan kata valid atau nama brand majemuk.
        if (s1Valid && s2Valid)
            return false;

        // Kasus B: Satu segmen valid, dan segmen lainnya memiliki anomali typo nyata
        bool s1Suspicious = !s1Valid && IsSuspiciousTypoSegment(s1);
        bool s2Suspicious = !s2Valid && IsSuspiciousTypoSegment(s2);

        if ((s1Valid && s2Suspicious) || (s2Valid && s1Suspicious))
        {
            segments = (s1, s2);
            return true;
        }

        return false;
    }

    private bool IsSuspiciousTypoSegment(string seg)
    {
        if (string.IsNullOrWhiteSpace(seg) || seg.Length < 3)
            return false;

        // Akronim murni huruf kapital (seperti "AI" di "OpenAI", "ID", "IT", "UI") bukan typo kata dasar
        if (IsAcronymSignal(seg))
            return false;

        // 1. Memiliki anomali karakter fonotaktik nyata (seperti "gg" di "Hargga", triple letter, dll)
        if (DetectCharacterAnomaly(seg))
            return true;

        // 2. Jika tanpa anomali karakter, harus berjarak 1 ke kata kamus dengan panjang >= 4 dan huruf awal sama
        string upper = seg.ToUpperInvariant();
        if (seg.Length >= 4)
        {
            var matches = _bkTree.Search(upper, 1);
            if (matches.Any(m => m.Word.Length > 0 && m.Word[0] == upper[0]))
            {
                return true;
            }
        }

        return false;
    }

    private bool HasFuzzyCandidateWithin(string norm, int maxDist)
    {
        var matches = _bkTree.Search(norm, maxDist);
        return matches.Any(m => BKTree.DamerauLevenshteinDistance(norm, m.Word) <= maxDist);
    }

    /// <summary>
    /// Mengevaluasi satu token terhadap aturan Multi-Evidence Scoring Engine.
    /// </summary>
    public EvidenceEvaluationResult EvaluateToken(
        string token,
        List<int> boxIds,
        float minConfidence = 1.0f,
        string spatialContext = "",
        bool hasSequenceRepetition = false,
        bool hasValidNeighborContext = false,
        string? leftNeighbor = null,
        string? rightNeighbor = null,
        string? entityNearTarget = null,
        EvidenceTypes initialEvidence = EvidenceTypes.None)
    {
        ArgumentNullException.ThrowIfNull(token);
        string norm = token.Trim().ToUpperInvariant();

        var result = new EvidenceEvaluationResult
        {
            RawToken = token,
            BoxIds = boxIds,
            SpatialContext = spatialContext,
            MinRecConfidence = minConfidence,
            Evidence = initialEvidence
        };

        // 0. Non-Latin / Chinese Noise Check
        int nonLatinCount = token.Count(c => char.IsLetter(c) && !((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= 0x00C0 && c <= 0x024F)));
        int totalLetterCount = token.Count(char.IsLetter);
        if (totalLetterCount > 0 && ((float)nonLatinCount / totalLetterCount) > 0.30f)
        {
            result.Action = PipelineAction.SAFE_TO_SKIP;
            result.Notes = "Kemungkinan noise visual / karakter non-Latin (diabaikan)";
            return result;
        }

        // 1. OCR Confidence Evidence
        if (minConfidence < MinOcrConfidenceThreshold)
        {
            result.Evidence |= EvidenceTypes.LOW_OCR_CONFIDENCE;
        }
        else
        {
            result.Evidence |= EvidenceTypes.HIGH_OCR_CONFIDENCE;
        }

        // 2. Repetition & Character Anomaly Signals
        if (hasSequenceRepetition || DetectRepetitionAnomaly(token))
        {
            result.Evidence |= EvidenceTypes.REPETITION_ANOMALY;
        }
        if (DetectCharacterAnomaly(token))
        {
            result.Evidence |= EvidenceTypes.CHARACTER_ANOMALY;
        }

        // 3. Acronym / Abbreviation Signal (Noise Reduction Evidence)
        if (IsAcronymSignal(norm))
        {
            result.Evidence |= EvidenceTypes.ACRONYM_SIGNAL;
        }

        // 4. Name / Brand Pattern Signal
        if (IsNameOrBrandPattern(token))
        {
            result.Evidence |= EvidenceTypes.WEAK_NAME_SIGNAL;
        }

        // 4.1. Mixed-case abnormal check (khusus G3 noise guard)
        if (DetectIrregularCasing(token))
        {
            result.Evidence |= EvidenceTypes.IRREGULAR_CASING;
        }

        // 4.2. Strong person context check (khusus proteksi R3D)
        if (DetectStrongPersonContext(token, leftNeighbor, rightNeighbor, spatialContext))
        {
            result.Evidence |= EvidenceTypes.STRONG_PERSON_CONTEXT;
        }

        // 5. Check Structured Reference / Location Matches
        if (_locationDict.Contains(norm) || IsKnownLocationPhrase(norm))
        {
            result.State = KnowledgeState.KNOWN_LOKASI;
            result.Evidence |= EvidenceTypes.LOKASI_MATCH;

            if ((result.Evidence & EvidenceTypes.REPETITION_ANOMALY) == 0 &&
                (result.Evidence & EvidenceTypes.CHARACTER_ANOMALY) == 0)
            {
                result.Action = PipelineAction.SAFE_TO_SKIP;
                result.Notes = "Entitas lokasi resmi tanpa anomali";
                return result;
            }
        }

        // 6. User Dictionary Match (Menghilangkan OOV)
        if (_userDict.Contains(norm))
        {
            result.State = KnowledgeState.KNOWN_USER_DICT;
            result.Evidence |= EvidenceTypes.USER_DICT_MATCH;

            if ((result.Evidence & EvidenceTypes.REPETITION_ANOMALY) == 0 &&
                (result.Evidence & EvidenceTypes.CHARACTER_ANOMALY) == 0)
            {
                result.Action = PipelineAction.SAFE_TO_SKIP;
                result.Notes = "Kata dikenal dalam User Dictionary";
                return result;
            }
        }

        // 7. Base Lexicon 20K Exact Match
        if (_exactDict.Contains(norm))
        {
            result.State = KnowledgeState.KNOWN_DICT;
            result.Evidence |= EvidenceTypes.DICT_ID_MATCH;

            if ((result.Evidence & EvidenceTypes.REPETITION_ANOMALY) != 0)
            {
                // Repetisi kata kamus: melalui structural / context gate
                if (norm.Length >= 2 && norm.All(char.IsLetter))
                {
                    result.Action = PipelineAction.SUSPECT;
                    result.Notes = $"Kata kamus ter-flag anomali pengulangan berdampingan ('{token} {token}')";

                    // Cari kandidat alternatif berjarak dekat (d <= 2) untuk kemungkinan koreksi frasa
                    var repMatches = _bkTree.Search(norm, 2);
                    var scoredRepCands = new List<(string Word, int Distance, double Score)>();
                    foreach (var rm in repMatches.Where(x => !x.Word.Equals(norm, StringComparison.OrdinalIgnoreCase)))
                    {
                        int effDist = BKTree.DamerauLevenshteinDistance(norm, rm.Word);
                        if (effDist > 2) continue;
                        bool isDom = _locationDict.Contains(rm.Word) ||
                                     _structuredReferences.Any(p => p.NormalizedText == rm.Word || p.Tokens.Contains(rm.Word));
                        double score = CalculateCandidateScore(norm, rm.Word, effDist, true, isDom, spatialContext, minConfidence);
                        scoredRepCands.Add((rm.Word, effDist, score));
                    }

                    foreach (var sc in scoredRepCands.OrderByDescending(x => x.Score).ThenBy(x => x.Distance))
                    {
                        result.PossibleCorrections.Add(new FuzzyCandidate
                        {
                            Word = sc.Word,
                            Distance = sc.Distance,
                            Grade = sc.Distance <= 1 ? "STRONG" : "MEDIUM"
                        });
                    }

                    return result;
                }
            }

            result.Action = PipelineAction.SAFE_TO_SKIP;
            result.Notes = "Cocok persis dengan kamus bahasa Indonesia";
            return result;
        }

        // 8. UNKNOWN Token: BK-Tree Fuzzy Search & Multi-Evidence Gating
        result.State = KnowledgeState.UNKNOWN;
        var rawMatches = _bkTree.Search(norm, MaxDistance);

        var scoredCandidates = new List<(string Word, int Distance, CandidateScoreResult Score)>();
        bool hasCharAnomaly = (result.Evidence & EvidenceTypes.CHARACTER_ANOMALY) != 0;

        foreach (var m in rawMatches)
        {
            // Gunakan Damerau-Levenshtein distance untuk menangkap kemungkinan transposisi karakter
            int damerauDist = BKTree.DamerauLevenshteinDistance(norm, m.Word);
            int effectiveDist = Math.Min(m.Distance, damerauDist);
            if (effectiveDist > MaxDistance) continue;

            bool isDomainOrRef = _locationDict.Contains(m.Word) ||
                                 _structuredReferences.Any(p => p.NormalizedText == m.Word || p.Tokens.Contains(m.Word));

            var scoreRes = CalculateCandidateScoreDetailed(norm, m.Word, effectiveDist, hasCharAnomaly, isDomainOrRef, spatialContext, minConfidence, leftNeighbor, rightNeighbor);
            scoredCandidates.Add((m.Word, effectiveDist, scoreRes));
        }

        // Entity Near Target dari Sliding Window (Multi-token entity near match)
        if (!string.IsNullOrEmpty(entityNearTarget) && !scoredCandidates.Any(sc => sc.Word.Equals(entityNearTarget, StringComparison.OrdinalIgnoreCase)))
        {
            int effDist = BKTree.LevenshteinDistance(norm, entityNearTarget);
            if (effDist <= MaxDistance)
            {
                var scoreRes = CalculateCandidateScoreDetailed(norm, entityNearTarget, effDist, hasCharAnomaly, false, spatialContext, minConfidence, leftNeighbor, rightNeighbor, entityMatch: 0.70);
                scoredCandidates.Add((entityNearTarget, effDist, scoreRes));
                result.Evidence |= EvidenceTypes.ENTITY_NEAR_MATCH;
            }
        }

        // Entity BK-Tree Fuzzy Search (Single-token entity near match)
        if (_entityBKTree.Count > 0 && norm.Length >= 3)
        {
            var entityMatches = _entityBKTree.Search(norm, 2);
            foreach (var em in entityMatches)
            {
                if (!scoredCandidates.Any(sc => sc.Word.Equals(em.Word, StringComparison.OrdinalIgnoreCase)))
                {
                    int effDist = em.Distance;
                    var scoreRes = CalculateCandidateScoreDetailed(norm, em.Word, effDist, hasCharAnomaly, false, spatialContext, minConfidence, leftNeighbor, rightNeighbor, entityMatch: 0.70);
                    scoredCandidates.Add((em.Word, effDist, scoreRes));
                    result.Evidence |= EvidenceTypes.ENTITY_NEAR_MATCH;
                }
            }
        }

        // Urutkan berdasarkan CandidateScore (evidence-weighted) dengan comparator eksplisit:
        // Jika |PrimaryScore_A - PrimaryScore_B| > 0.01, PrimaryScore mutlak menentukan urutan.
        // Frequency (FinalScore) hanya tie-breaker jika selisih <= 0.01.
        scoredCandidates.Sort(CompareCandidates);

        foreach (var sc in scoredCandidates)
        {
            string grade = sc.Distance switch
            {
                1 => "STRONG",
                2 => "MEDIUM",
                3 => "WEAK",
                _ => "NONE"
            };

            result.PossibleCorrections.Add(new FuzzyCandidate
            {
                Word = sc.Word,
                Distance = sc.Distance,
                Grade = grade,
                PrimaryScore = sc.Score.PrimaryScore,
                FinalScore = sc.Score.FinalScore,
                EntityRelevance = sc.Score.EntityRelevance
            });

            if (sc.Distance == 1) result.Evidence |= EvidenceTypes.FUZZY_STRONG;
            else if (sc.Distance == 2) result.Evidence |= EvidenceTypes.FUZZY_MEDIUM;
            else if (sc.Distance == 3) result.Evidence |= EvidenceTypes.FUZZY_WEAK;
        }

        // Populate metadata exposure fields (Top1, Margin, Scores)
        if (result.PossibleCorrections.Count > 0)
        {
            var top1 = result.PossibleCorrections[0];
            result.Top1Suggestion = top1.Word;
            result.Top1Distance = top1.Distance;
            result.Top1PrimaryScore = top1.PrimaryScore;
            result.FuzzyMargin = result.PossibleCorrections.Count > 1
                ? top1.PrimaryScore - result.PossibleCorrections[1].PrimaryScore
                : top1.PrimaryScore;

            result.HasFuzzyRunnerUp = result.PossibleCorrections.Count > 1;

            result.TrueFuzzyMargin = result.HasFuzzyRunnerUp
                ? top1.PrimaryScore - result.PossibleCorrections[1].PrimaryScore
                : 0.0;

            result.G3LowMargin = result.FuzzyMargin;
        }

        result.HasStrongEvidence =
            (result.Evidence &
             (EvidenceTypes.REFERENCE_NEAR_MATCH | EvidenceTypes.FUZZY_STRONG)) != 0 ||
            result.PossibleCorrections.Any(c => c.Grade == "STRONG");

        // GATE EVALUATION: Konvergensi Bukti Multidimensi
        // Aturan: UNKNOWN ≠ SUSPECT. OOV hanya sinyal, bukan vonis typo.
        if ((result.Evidence & EvidenceTypes.REPETITION_ANOMALY) != 0)
        {
            result.Action = PipelineAction.SUSPECT;
            result.Notes = "Anomali repetisi terdeteksi";
            return result;
        }

        // A. Sinyal Akronim murni tanpa anomali karakter -> PASSIVE (Noise reduction)
        // Mencegah NPSN, PLS, KTP, dll. terangkat sebagai typo
        if ((result.Evidence & EvidenceTypes.ACRONYM_SIGNAL) != 0 &&
            (result.Evidence & EvidenceTypes.CHARACTER_ANOMALY) == 0)
        {
            result.Action = PipelineAction.PASSIVE;
            result.Notes = "Pola akronim/singkatan tanpa anomali karakter";
            return result;
        }

        bool hasEntityNearMatch = (result.Evidence & EvidenceTypes.ENTITY_NEAR_MATCH) != 0;

        // B. Distance 1 (FUZZY_STRONG)
        if ((result.Evidence & EvidenceTypes.FUZZY_STRONG) != 0)
        {
            var bestCand = result.PossibleCorrections.FirstOrDefault(c => c.Distance == 1);
            if (bestCand != null)
            {
                float sim = 1.0f - 1.0f / Math.Max(norm.Length, bestCand.Word.Length);
                bool sameFirstLetter = norm.Length > 0 && bestCand.Word.Length > 0 && norm[0] == bestCand.Word[0];
                bool hasAnomaly = (result.Evidence & EvidenceTypes.CHARACTER_ANOMALY) != 0;
                bool hasNameSignal = (result.Evidence & EvidenceTypes.WEAK_NAME_SIGNAL) != 0;
                bool isDomainOrRef = _domainDict.Contains(bestCand.Word) ||
                                     _locationDict.Contains(bestCand.Word) ||
                                     _structuredReferences.Any(p => p.NormalizedText == bestCand.Word || p.Tokens.Contains(bestCand.Word));

                bool isStrongPerson = (result.Evidence & EvidenceTypes.STRONG_PERSON_CONTEXT) != 0;

                // Sinyal nama menurunkan kecurigaan: jika berpola nama (TitleCase) tanpa konteks tetangga leksikal valid,
                // kesamaan huruf awal saja tidak cukup untuk memvonis SUSPECT tanpa anomali karakter,
                // near-match entitas, atau relevansi leksikon domain/referensi pendukung.
                // Jika memiliki konteks nama orang kuat (STRONG_PERSON_CONTEXT), tetangga valid (seperti nama orang lain)
                // tidak dianggap sebagai bukti typo pendukung.
                bool strongEvidence = hasAnomaly || hasEntityNearMatch || isDomainOrRef || (hasValidNeighborContext && !isStrongPerson);
                bool qualifies = hasNameSignal
                    ? strongEvidence
                    : (sameFirstLetter || strongEvidence);

                // Memerlukan panjang >= 4 dan kemiripan >= 0.75, serta kualifikasi bukti
                if (norm.Length >= 4 && sim >= 0.75f && qualifies)
                {
                    result.Action = PipelineAction.SUSPECT;
                    var cands = result.PossibleCorrections.Where(c => c.Distance == 1).Take(3).Select(c => c.Word);
                    result.Notes = $"Fuzzy strong anomaly (d=1: {string.Join(", ", cands)})";
                    return result;
                }
            }

            result.Action = PipelineAction.PASSIVE;
            result.Notes = (result.Evidence & EvidenceTypes.WEAK_NAME_SIGNAL) != 0
                ? "Jarak 1 pada pola nama/brand tanpa anomali karakter pendukung"
                : "Jarak 1 pada token pendek atau tanpa bukti penguat cukup";
            return result;
        }

        // C. Distance 2 (FUZZY_MEDIUM)
        // Sesuai konvergensi bukti: Distance = 2, Initial character agreement,
        // (Character anomaly OR Entity near match OR Domain/Reference collocation match with valid neighbor).
        // hasValidNeighborContext murni TIDAK BOLEH meloloskan token jarak 2 tanpa bukti anomali atau referensi.
        if ((result.Evidence & EvidenceTypes.FUZZY_MEDIUM) != 0)
        {
            var validMediumMatches = result.PossibleCorrections
                .Where(c => c.Distance == 2 && c.Word.Length > 0 && norm.Length > 0 && c.Word[0] == norm[0])
                .ToList();

            bool isDomainOrRefCollocation = hasValidNeighborContext && validMediumMatches.Any(m =>
                _domainDict.Contains(m.Word) ||
                _locationDict.Contains(m.Word) ||
                _structuredReferences.Any(p => p.NormalizedText == m.Word || p.Tokens.Contains(m.Word)));

            bool hasSupportingEvidence = hasCharAnomaly || hasEntityNearMatch || isDomainOrRefCollocation;

            // OCR confidence sebagai pembobotan pengaman (mengabaikan artefak visual blur < 0.60)
            if (validMediumMatches.Count > 0 && norm.Length >= 4 && hasSupportingEvidence && minConfidence >= 0.60f)
            {
                result.Action = PipelineAction.SUSPECT;
                result.Notes = $"Fuzzy medium anomaly dengan konvergensi bukti (d=2: {string.Join(", ", validMediumMatches.Take(3).Select(c => c.Word))})";
                return result;
            }

            result.Action = PipelineAction.PASSIVE;
            result.Notes = "Jarak 2 tanpa konvergensi bukti cukup (diperlakukan sebagai nama/istilah wajar)";
            return result;
        }

        // D. Distance 3 (FUZZY_WEAK)
        // Hanya untuk token panjang (>= 8), huruf awal sama, ada anomali karakter nyata, tanpa sinyal nama
        if ((result.Evidence & EvidenceTypes.FUZZY_WEAK) != 0)
        {
            var validWeakMatches = result.PossibleCorrections
                .Where(c => c.Distance == 3 && c.Word.Length > 0 && norm.Length > 0 && c.Word[0] == norm[0])
                .ToList();

            if (norm.Length >= 8 && validWeakMatches.Count > 0 &&
                (result.Evidence & EvidenceTypes.CHARACTER_ANOMALY) != 0 &&
                (result.Evidence & EvidenceTypes.WEAK_NAME_SIGNAL) == 0)
            {
                result.Action = PipelineAction.SUSPECT;
                result.Notes = $"Fuzzy weak anomaly dengan anomali karakter (d=3: {string.Join(", ", validWeakMatches.Take(3).Select(c => c.Word))})";
                return result;
            }

            result.Action = PipelineAction.PASSIVE;
            result.Notes = "Jarak 3 tanpa anomali karakter pendukung";
            return result;
        }

        // E. Jarak > 3 atau OOV murni tanpa kandidat
        result.Action = PipelineAction.PASSIVE;
        result.Notes = (result.Evidence & EvidenceTypes.WEAK_NAME_SIGNAL) != 0
            ? "Token di luar kamus dengan bentuk nama/brand wajar"
            : "Token di luar kamus tanpa kandidat terdekat";

        return result;
    }

    /// <summary>
    /// Memproses seluruh kotak OCR menggunakan deteksi entitas referensi terstruktur dua tahap,
    /// tokenisasi final, dan akumulasi bukti leksikal-spasial deterministik per-token.
    /// Setiap kandidat diberi CandidateId unik berformat "{BoxId}_{FinalTokenIndex}".
    /// </summary>
    public IReadOnlyList<EvidenceEvaluationResult> ProcessBoxes(
        IReadOnlyList<OcrBoxModel> boxes,
        IEnumerable<string>? customLocations = null)
    {
        if (boxes == null || boxes.Count == 0)
            return Array.Empty<EvidenceEvaluationResult>();

        if (customLocations != null)
        {
            LoadLocations(customLocations);
        }

        var results = new List<EvidenceEvaluationResult>();

        foreach (var b in boxes)
        {
            if (!PpOcrEngine.IsMeaningfulText(b.Text)) continue;

            string raw = b.Text.Trim();
            var rawSplit = raw.Split(WordDelimiters, StringSplitOptions.RemoveEmptyEntries);
            if (rawSplit.Length == 0) continue;

            // Uji candidate segmentation untuk kasus transisi camelCase/PascalCase (misal "HarggaTerjangkau")
            var rawTokensList = new List<string>();
            foreach (var tok in rawSplit)
            {
                if (TryEvaluateCaseTransitionSegmentation(tok, out var segs))
                {
                    rawTokensList.Add(segs.SubToken1);
                    rawTokensList.Add(segs.SubToken2);
                }
                else
                {
                    rawTokensList.Add(tok);
                }
            }
            var rawTokens = rawTokensList.ToArray();
            if (rawTokens.Length == 0) continue;

            // Tracking alignment entitas referensi untuk setiap token pada box ini
            int tokenCount = rawTokens.Length;
            var isProtected = new bool[tokenCount];
            var protectedReason = new string[tokenCount];
            var isNearMatch = new bool[tokenCount];
            var targetWord = new string[tokenCount];
            var matchedPhrase = new StructuredReferencePhrase?[tokenCount];

            var isEntityProtected = new bool[tokenCount];
            var entityProtectedReason = new string[tokenCount];
            var entityEvidence = new EvidenceTypes[tokenCount];
            var isEntityNearMatch = new bool[tokenCount];
            var entityNearTarget = new string[tokenCount];
            var matchedEntity = new ParsedEntity?[tokenCount];

            // -------------------------------------------------------------
            // TAHAP 1: Exact Phrase Detection pada Structured References
            // Frasa utuh yang cocok persis melindungi seluruh token penyusunnya
            // Contoh: "Kecamatan Baturaja Timur" melindungi "BATURAJA"
            // -------------------------------------------------------------
            foreach (var phrase in _structuredReferences)
            {
                int k = phrase.Tokens.Length;
                if (k > tokenCount || k == 0) continue;

                for (int i = 0; i <= tokenCount - k; i++)
                {
                    bool match = true;
                    for (int j = 0; j < k; j++)
                    {
                        if (!rawTokens[i + j].Equals(phrase.Tokens[j], StringComparison.OrdinalIgnoreCase))
                        {
                            match = false;
                            break;
                        }
                    }

                    if (match)
                    {
                        for (int j = 0; j < k; j++)
                        {
                            isProtected[i + j] = true;
                            protectedReason[i + j] = phrase.Text;
                            matchedPhrase[i + j] = phrase;
                        }
                    }
                }
            }

            // -------------------------------------------------------------
            // TAHAP 1.5: Exact Entity Reference Detection (Canonical & Aliases)
            // Entitas referensi yang cocok persis melindungi token dalam jendela kemunculannya (Occurrence-Specific Protection)
            // Alias ambigu (AmbiguousAliases) HANYA memberikan evidence, TIDAK otomatis memberikan protection!
            // -------------------------------------------------------------
            foreach (var pe in _entityReferences)
            {
                // A. Canonical Match
                int k = pe.CanonicalTokens.Length;
                if (k > 0 && k <= tokenCount)
                {
                    for (int i = 0; i <= tokenCount - k; i++)
                    {
                        bool match = true;
                        for (int j = 0; j < k; j++)
                        {
                            if (!rawTokens[i + j].Equals(pe.CanonicalTokens[j], StringComparison.OrdinalIgnoreCase))
                            {
                                match = false;
                                break;
                            }
                        }

                        if (match)
                        {
                            for (int j = 0; j < k; j++)
                            {
                                bool isSingleAmbiguous = (k == 1 && (pe.Item.Ambiguity >= EntityAmbiguityLevel.High ||
                                                                    (pe.Item.Ambiguity == EntityAmbiguityLevel.Moderate && tokenCount == 1)));
                                if (!isSingleAmbiguous)
                                {
                                    isEntityProtected[i + j] = true;
                                    entityProtectedReason[i + j] = pe.Item.Name;
                                }
                                entityEvidence[i + j] |= EvidenceTypes.ENTITY_EXACT_MATCH;
                                matchedEntity[i + j] = pe;
                            }
                        }
                    }
                }

                // B. Unambiguous Aliases Match
                foreach (var alias in pe.UnambiguousAliasTokens)
                {
                    int ak = alias.Length;
                    if (ak > 0 && ak <= tokenCount)
                    {
                        for (int i = 0; i <= tokenCount - ak; i++)
                        {
                            bool match = true;
                            for (int j = 0; j < ak; j++)
                            {
                                if (!rawTokens[i + j].Equals(alias[j], StringComparison.OrdinalIgnoreCase))
                                {
                                    match = false;
                                    break;
                                }
                            }

                            if (match)
                            {
                                for (int j = 0; j < ak; j++)
                                {
                                    bool isSingleAmbiguous = (ak == 1 && (pe.Item.Ambiguity >= EntityAmbiguityLevel.High ||
                                                                         (pe.Item.Ambiguity == EntityAmbiguityLevel.Moderate && tokenCount == 1)));
                                    if (!isSingleAmbiguous)
                                    {
                                        isEntityProtected[i + j] = true;
                                        entityProtectedReason[i + j] = pe.Item.Name;
                                    }
                                    entityEvidence[i + j] |= EvidenceTypes.ENTITY_ALIAS_MATCH;
                                    matchedEntity[i + j] = pe;
                                }
                            }
                        }
                    }
                }

                // C. Ambiguous Aliases Match (Evidence Only, NO automatic protection)
                foreach (var alias in pe.AmbiguousAliasTokens)
                {
                    int ak = alias.Length;
                    if (ak > 0 && ak <= tokenCount)
                    {
                        for (int i = 0; i <= tokenCount - ak; i++)
                        {
                            bool match = true;
                            for (int j = 0; j < ak; j++)
                            {
                                if (!rawTokens[i + j].Equals(alias[j], StringComparison.OrdinalIgnoreCase))
                                {
                                    match = false;
                                    break;
                                }
                            }

                            if (match)
                            {
                                for (int j = 0; j < ak; j++)
                                {
                                    entityEvidence[i + j] |= EvidenceTypes.ENTITY_ALIAS_MATCH;
                                    matchedEntity[i + j] = pe;
                                    // PENTING: Jangan set isEntityProtected = true untuk alias ambigu!
                                }
                            }
                        }
                    }
                }
            }

            // -------------------------------------------------------------
            // TAHAP 2: Near-Match Phrase Alignment pada Structured References
            // Bila sebuah jendela frasa memiliki tepat 1 kata yang berbeda dengan jarak <= 2,
            // kata tersebut diisolasi sebagai kandidat koreksi ke target referensi.
            // Contoh: "DESA TANJUUNG BARU" -> "TANJUUNG" menjadi kandidat ke "TANJUNG"
            // -------------------------------------------------------------
            foreach (var phrase in _structuredReferences)
            {
                int k = phrase.Tokens.Length;
                if (k < 2 || k > tokenCount) continue;

                for (int i = 0; i <= tokenCount - k; i++)
                {
                    int diffCount = 0;
                    int diffIndex = -1;

                    for (int j = 0; j < k; j++)
                    {
                        if (!rawTokens[i + j].Equals(phrase.Tokens[j], StringComparison.OrdinalIgnoreCase))
                        {
                            diffCount++;
                            diffIndex = j;
                        }
                    }

                    // Tepat 1 kata berbeda dalam frasa referensi
                    if (diffCount == 1 && diffIndex >= 0)
                    {
                        int diffPos = i + diffIndex;
                        if (!isProtected[diffPos] && !isNearMatch[diffPos])
                        {
                            string actualTok = rawTokens[diffPos].ToUpperInvariant();
                            string targetTok = phrase.Tokens[diffIndex].ToUpperInvariant();
                            int dist = BKTree.LevenshteinDistance(actualTok, targetTok);

                            if (dist >= 1 && dist <= 2)
                            {
                                isNearMatch[diffPos] = true;
                                targetWord[diffPos] = targetTok;
                                matchedPhrase[diffPos] = phrase;

                                // Lindungi token-token lain dalam frasa yang cocok persis
                                for (int j = 0; j < k; j++)
                                {
                                    if (j != diffIndex)
                                    {
                                        isProtected[i + j] = true;
                                        protectedReason[i + j] = phrase.Text;
                                    }
                                }
                            }
                        }
                    }
                }
            }

            // -------------------------------------------------------------
            // TAHAP 2.2: Near-Match Phrase Alignment pada Entity References
            // Bila sebuah jendela frasa entitas memiliki tepat 1 kata yang berbeda dengan jarak <= 2,
            // kata tersebut diidentifikasi sebagai kandidat near-match entitas.
            // Tidak langsung divonis SUSPECT, melainkan dievaluasi melalui candidate scoring dan gate existing.
            // -------------------------------------------------------------
            foreach (var pe in _entityReferences)
            {
                // Periksa canonical tokens (hanya multi-token >= 2)
                if (pe.CanonicalTokens.Length >= 2 && pe.CanonicalTokens.Length <= tokenCount)
                {
                    int k = pe.CanonicalTokens.Length;
                    for (int i = 0; i <= tokenCount - k; i++)
                    {
                        int diffCount = 0;
                        int diffIndex = -1;

                        for (int j = 0; j < k; j++)
                        {
                            if (!rawTokens[i + j].Equals(pe.CanonicalTokens[j], StringComparison.OrdinalIgnoreCase))
                            {
                                diffCount++;
                                diffIndex = j;
                            }
                        }

                        if (diffCount == 1 && diffIndex >= 0)
                        {
                            int diffPos = i + diffIndex;
                            if (!isProtected[diffPos] && !isNearMatch[diffPos] && !isEntityProtected[diffPos] && !isEntityNearMatch[diffPos])
                            {
                                string actualTok = rawTokens[diffPos].ToUpperInvariant();
                                string targetTok = pe.CanonicalTokens[diffIndex].ToUpperInvariant();
                                int dist = BKTree.LevenshteinDistance(actualTok, targetTok);

                                if (dist >= 1 && dist <= 2)
                                {
                                    isEntityNearMatch[diffPos] = true;
                                    entityNearTarget[diffPos] = targetTok;
                                    entityEvidence[diffPos] |= EvidenceTypes.ENTITY_NEAR_MATCH;
                                    matchedEntity[diffPos] = pe;

                                    // Lindungi token-token lain dalam frasa yang cocok persis
                                    for (int j = 0; j < k; j++)
                                    {
                                        if (j != diffIndex)
                                        {
                                            isEntityProtected[i + j] = true;
                                            entityProtectedReason[i + j] = pe.Item.Name;
                                            entityEvidence[i + j] |= EvidenceTypes.ENTITY_EXACT_MATCH;
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }

            // -------------------------------------------------------------
            // TAHAP 2.5: Deteksi Repetisi Token Berdampingan (Token Sequence Analysis)
            // Mengidentifikasi pasangan token berulang (AdjacentDuplicate) sebagai sinyal bukti REPETITION_ANOMALY.
            // Token kedua otomatis disupresi agar selalu menghasilkan tepat 1 anomaly instance (anti-duplikasi finding).
            // -------------------------------------------------------------
            var isRepetitionCandidate = new bool[tokenCount];
            var isRepetitionSuppressed = new bool[tokenCount];

            for (int i = 0; i < tokenCount - 1; i++)
            {
                if (isProtected[i] || isNearMatch[i] || isProtected[i + 1] || isNearMatch[i + 1] ||
                    isEntityProtected[i] || isEntityNearMatch[i] || isEntityProtected[i + 1] || isEntityNearMatch[i + 1])
                    continue;

                string t1 = rawTokens[i];
                string t2 = rawTokens[i + 1];

                // Kata alfabetik >= 2 huruf yang berulang berdampingan
                if (t1.Length >= 2 && t1.Equals(t2, StringComparison.OrdinalIgnoreCase) && t1.All(char.IsLetter))
                {
                    // Pengecualian kata ulang bahasa Indonesia bertanda hubung: "Lain - Lain", "Lain-Lain", "anak-anak"
                    bool isHyphenated = System.Text.RegularExpressions.Regex.IsMatch(
                        raw,
                        $@"\b{System.Text.RegularExpressions.Regex.Escape(t1)}\s*[-—]\s*{System.Text.RegularExpressions.Regex.Escape(t2)}\b",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                    if (!isHyphenated)
                    {
                        isRepetitionCandidate[i] = true;
                        isRepetitionSuppressed[i + 1] = true;
                    }
                }
            }

            // -------------------------------------------------------------
            // TAHAP 3: Final Tokenization & Multi-Evidence Evaluation
            // Menghasilkan CandidateId stabil "{BoxId}_{FinalTokenIndex}"
            // -------------------------------------------------------------
            string spatialCtx = SpatialContextCalculator.BuildLineContext(b, boxes, MinVerticalOverlap);

            for (int idx = 0; idx < tokenCount; idx++)
            {
                string tok = rawTokens[idx];
                if (tok.Length < 2) continue;
                if (tok.All(char.IsDigit)) continue;

                string candidateId = $"{b.Id}_{idx}";

                // Kasus 0A: Format mata uang/nominal numerik (e.g. Rp50, Rp50.000)
                if (IsMonetaryToken(tok))
                {
                    results.Add(new EvidenceEvaluationResult
                    {
                        CandidateId = candidateId,
                        TokenIndex = idx,
                        RawToken = tok,
                        BoxIds = new List<int> { b.Id },
                        BoxText = b.Text,
                        DetScore = b.DetScore,
                        SpatialContext = spatialCtx,
                        MinRecConfidence = b.RecConfidence,
                        State = KnowledgeState.KNOWN_DICT,
                        Action = PipelineAction.SAFE_TO_SKIP,
                        Evidence = EvidenceTypes.None,
                        Notes = "Format nominal mata uang valid"
                    });
                    continue;
                }

                // Kasus 0B: Token disupresi karena merupakan pasangan duplikat kedua dari repetisi berdampingan
                if (isRepetitionSuppressed[idx])
                {
                    results.Add(new EvidenceEvaluationResult
                    {
                        CandidateId = candidateId,
                        TokenIndex = idx,
                        RawToken = tok,
                        BoxIds = new List<int> { b.Id },
                        BoxText = b.Text,
                        DetScore = b.DetScore,
                        SpatialContext = spatialCtx,
                        MinRecConfidence = b.RecConfidence,
                        State = KnowledgeState.KNOWN_DICT,
                        Action = PipelineAction.SAFE_TO_SKIP,
                        Evidence = EvidenceTypes.REPETITION_ANOMALY,
                        Notes = "Kata duplikat berdampingan yang disupresi (anomali dievaluasi pada token pertama)"
                    });
                    continue;
                }

                // Kasus 1: Dilindungi oleh Exact Match Entitas Referensi Terstruktur (Lokasi / Administrasi)
                if (isProtected[idx])
                {
                    results.Add(new EvidenceEvaluationResult
                    {
                        CandidateId = candidateId,
                        TokenIndex = idx,
                        RawToken = tok,
                        BoxIds = new List<int> { b.Id },
                        BoxText = b.Text,
                        DetScore = b.DetScore,
                        SpatialContext = spatialCtx,
                        MinRecConfidence = b.RecConfidence,
                        State = KnowledgeState.KNOWN_REFERENCE,
                        Action = PipelineAction.SAFE_TO_SKIP,
                        Evidence = EvidenceTypes.REFERENCE_MATCH,
                        Notes = $"Bagian dari entitas referensi resmi: '{matchedPhrase[idx]?.Text ?? protectedReason[idx]}'"
                    });
                    continue;
                }

                // Kasus 1.5: Dilindungi oleh Exact Match Entitas Bisnis / Kategori (Occurrence-Specific Protection)
                if (isEntityProtected[idx])
                {
                    results.Add(new EvidenceEvaluationResult
                    {
                        CandidateId = candidateId,
                        TokenIndex = idx,
                        RawToken = tok,
                        BoxIds = new List<int> { b.Id },
                        BoxText = b.Text,
                        DetScore = b.DetScore,
                        SpatialContext = spatialCtx,
                        MinRecConfidence = b.RecConfidence,
                        State = KnowledgeState.KNOWN_REFERENCE,
                        Action = PipelineAction.SAFE_TO_SKIP,
                        Evidence = entityEvidence[idx],
                        Notes = $"Bagian dari entitas referensi resmi: '{matchedEntity[idx]?.Item.Name ?? entityProtectedReason[idx]}'"
                    });
                    continue;
                }

                // Kasus 2: Terisolasi sebagai Near-Match Entitas Referensi Terstruktur
                if (isNearMatch[idx])
                {
                    string target = targetWord[idx];
                    int dist = BKTree.LevenshteinDistance(tok.ToUpperInvariant(), target);
                    var evalResult = new EvidenceEvaluationResult
                    {
                        CandidateId = candidateId,
                        TokenIndex = idx,
                        RawToken = tok,
                        BoxIds = new List<int> { b.Id },
                        BoxText = b.Text,
                        DetScore = b.DetScore,
                        SpatialContext = spatialCtx,
                        MinRecConfidence = b.RecConfidence,
                        State = KnowledgeState.KNOWN_REFERENCE,
                        Action = PipelineAction.SUSPECT,
                        Evidence = EvidenceTypes.REFERENCE_NEAR_MATCH | EvidenceTypes.FUZZY_STRONG,
                        Notes = $"Near-match pada entitas referensi '{matchedPhrase[idx]?.Text}' (target: {target})"
                    };
                    evalResult.MatchedReferenceContext = matchedPhrase[idx]?.Text;
                    evalResult.Top1Suggestion = target;
                    evalResult.Top1Distance = dist;
                    evalResult.Top1PrimaryScore = 0.95;
                    evalResult.FuzzyMargin = 0.95;
                    evalResult.G3LowMargin = evalResult.FuzzyMargin;
                    evalResult.HasFuzzyRunnerUp = false;
                    evalResult.TrueFuzzyMargin = 0.0;
                    evalResult.HasStrongEvidence = true;
                    evalResult.PossibleCorrections.Add(new FuzzyCandidate
                    {
                        Word = target,
                        Distance = dist,
                        Grade = "STRONG"
                    });
                    results.Add(evalResult);
                    continue;
                }

                // Evaluasi Konteks Tetangga Valid (Valid Neighbor Context)
                string? leftNeighbor = idx > 0 ? rawTokens[idx - 1] : null;
                string? rightNeighbor = idx < tokenCount - 1 ? rawTokens[idx + 1] : null;
                bool hasValidNeighbor = false;

                if (!string.IsNullOrEmpty(leftNeighbor))
                {
                    string normLeft = leftNeighbor.Trim().ToUpperInvariant();
                    if (_exactDict.Contains(normLeft) || _locationDict.Contains(normLeft) || _userDict.Contains(normLeft) || _domainDict.Contains(normLeft))
                        hasValidNeighbor = true;
                }
                if (!hasValidNeighbor && !string.IsNullOrEmpty(rightNeighbor))
                {
                    string normRight = rightNeighbor.Trim().ToUpperInvariant();
                    if (_exactDict.Contains(normRight) || _locationDict.Contains(normRight) || _userDict.Contains(normRight) || _domainDict.Contains(normRight))
                        hasValidNeighbor = true;
                }

                // Kasus 3: Evaluasi Multi-Evidence Standar
                var eval = EvaluateToken(
                    tok,
                    new List<int> { b.Id },
                    b.RecConfidence,
                    spatialCtx,
                    isRepetitionCandidate[idx],
                    hasValidNeighborContext: hasValidNeighbor,
                    leftNeighbor: leftNeighbor,
                    rightNeighbor: rightNeighbor,
                    entityNearTarget: isEntityNearMatch[idx] ? entityNearTarget[idx] : null,
                    initialEvidence: entityEvidence[idx]);

                eval.CandidateId = candidateId;
                eval.TokenIndex = idx;
                eval.BoxText = b.Text;
                eval.DetScore = b.DetScore;
                eval.MatchedReferenceContext = matchedEntity[idx]?.Item.Name ?? matchedPhrase[idx]?.Text;
                results.Add(eval);
            }
        }

        return results;
    }

    /// <summary>
    /// Menghitung rincian skor gabungan kandidat koreksi berbasis 8 dimensi bukti.
    /// S_primary = 0.4275L + 0.1900R + 0.1425A + 0.0950C + 0.0475O + 0.0285T + 0.0500E
    /// S_final = S_primary + 0.0190F
    /// Frequency (F) hanya digunakan sebagai tie-breaker dan tidak boleh membalikkan ranking jika delta primary > 0.01.
    /// </summary>
    public CandidateScoreResult CalculateCandidateScoreDetailed(
        string token,
        string candidate,
        int editDistance,
        bool hasCharacterAnomaly,
        bool isDomainOrRefMatch,
        string context,
        float minConfidence,
        string? leftNeighbor = null,
        string? rightNeighbor = null,
        double entityMatch = 0.0,
        double entityContext = 0.75)
    {
        var result = new CandidateScoreResult();
        int maxLen = Math.Max(token.Length, candidate.Length);
        if (maxLen == 0) return result;

        // 1. Normalized Similarity L (0.0 - 1.0) dengan peredaman non-linear berbasis jarak edit
        double simRatio = 1.0 - (double)editDistance / maxLen;
        double similarity = editDistance switch
        {
            1 => simRatio,
            2 => simRatio * 0.85,
            _ => simRatio * 0.50 // Jarak 3 (WEAK) diredam agar tidak mengalahkan kandidat jarak 1 (STRONG)
        };
        result.LexicalScore = similarity * ScoringParams.SimilarityWeight;

        // 2. Reference / Domain Relevance R (EXCLUDING Entity Reference!)
        // Mutually exclusive: EntityReference tidak dihitung di R (menghindari double counting)
        if (_domainDict.Contains(candidate))
        {
            result.ReferenceScore = 1.0 * ScoringParams.ReferenceRelevanceWeight;
        }
        else if (isDomainOrRefMatch)
        {
            bool isLocationRegion = IsSemanticLocationRegion(context);
            if (isLocationRegion)
            {
                result.ReferenceScore = 1.0 * ScoringParams.ReferenceRelevanceWeight;
            }
        }

        // 3. Character Anomaly Resolution A
        // Sinyal indikatif: kandidat memulihkan panjang kata atau struktur karakter anomali
        if (hasCharacterAnomaly && candidate.Length != token.Length)
        {
            result.AnomalyScore = 1.0 * ScoringParams.CharacterAnomalyWeight;
        }

        // 4. Context Relevance C (Structural Neighbor Collocation)
        // Mengevaluasi kesesuaian kandidat dengan tetangga terdekat dalam frasa referensi terdaftar
        bool contextMatch = false;
        if (!string.IsNullOrEmpty(leftNeighbor) && _structuredReferences.Any(p => HasAdjacentTokens(p.Tokens, leftNeighbor, candidate)))
        {
            contextMatch = true;
        }
        else if (!string.IsNullOrEmpty(rightNeighbor) && _structuredReferences.Any(p => HasAdjacentTokens(p.Tokens, candidate, rightNeighbor)))
        {
            contextMatch = true;
        }

        if (contextMatch)
        {
            result.ContextScore = 1.0 * ScoringParams.ContextRelevanceWeight;
        }

        // 5. OCR Confidence Evidence O
        result.OcrScore = (double)Math.Clamp(minConfidence, 0f, 1f) * ScoringParams.OcrConfidenceWeight;

        // 6. Token Shape T (Prefix LCP + Preservasi Batas Karakter)
        double shapeMatch = 0.0;
        int lcp = 0;
        while (lcp < token.Length && lcp < candidate.Length && token[lcp] == candidate[lcp])
        {
            lcp++;
        }
        double prefixRatio = (double)lcp / maxLen;
        shapeMatch += prefixRatio * 0.7;

        if (token.Length > 0 && candidate.Length > 0 && token[^1] == candidate[^1])
        {
            shapeMatch += 0.3;
        }
        result.ShapeScore = shapeMatch * ScoringParams.TokenShapeWeight;

        // 7. Entity Relevance E = E_match * E_context
        double effEmatch = entityMatch;
        if (effEmatch <= 0.0 && _entitySingleWordSet.Contains(candidate))
        {
            effEmatch = 0.70; // Near/match via leksikon entitas
        }

        if (effEmatch > 0.0)
        {
            double effContext = contextMatch ? 1.00 : entityContext;
            result.EntityRelevance = effEmatch * effContext;
        }

        // 8. PrimaryScore P = 0.4275L + 0.1900R + 0.1425A + 0.0950C + 0.0475O + 0.0285T + 0.0500E
        result.PrimaryScore = result.LexicalScore +
                              result.ReferenceScore +
                              result.AnomalyScore +
                              result.ContextScore +
                              result.OcrScore +
                              result.ShapeScore +
                              (result.EntityRelevance * ScoringParams.EntityRelevanceWeight);

        // 9. Frequency Tie-Breaker F (Skala mikro logaritmik teredam)
        // Dibatasi ketat oleh FrequencyTieBreakerWeight (maks 0.0190)
        if (_wordFrequencyRank.TryGetValue(candidate, out int rank) && rank > 0)
        {
            double tieBreaker = (1.0 / (1.0 + Math.Log10(1 + rank))) * ScoringParams.FrequencyTieBreakerWeight;
            result.FrequencyScore = tieBreaker;
        }

        result.FinalScore = result.PrimaryScore + result.FrequencyScore;
        return result;
    }

    /// <summary>
    /// Menghitung skor akhir kandidat (backward-compatible overload).
    /// </summary>
    public double CalculateCandidateScore(
        string token,
        string candidate,
        int editDistance,
        bool hasCharacterAnomaly,
        bool isDomainOrRefMatch,
        string context,
        float minConfidence,
        string? leftNeighbor = null,
        string? rightNeighbor = null)
    {
        var detailed = CalculateCandidateScoreDetailed(
            token, candidate, editDistance, hasCharacterAnomaly,
            isDomainOrRefMatch, context, minConfidence,
            leftNeighbor, rightNeighbor);
        return detailed.FinalScore;
    }

    /// <summary>
    /// Comparator eksplisit untuk pemeringkatan kandidat:
    /// Jika |PrimaryScore_A - PrimaryScore_B| > 0.01, kandidat dengan PrimaryScore lebih tinggi mutlak menang.
    /// Frequency (FinalScore) hanya bertindak sebagai tie-breaker jika selisih primary <= 0.01.
    /// </summary>
    public static int CompareCandidates(
        (string Word, int Distance, CandidateScoreResult Score) a,
        (string Word, int Distance, CandidateScoreResult Score) b)
    {
        double delta = Math.Abs(a.Score.PrimaryScore - b.Score.PrimaryScore);
        if (delta > 0.01)
        {
            return a.Score.PrimaryScore > b.Score.PrimaryScore ? -1 : 1;
        }
        double finalDelta = Math.Abs(a.Score.FinalScore - b.Score.FinalScore);
        if (finalDelta > 1e-6)
        {
            return a.Score.FinalScore > b.Score.FinalScore ? -1 : 1;
        }
        return a.Distance.CompareTo(b.Distance);
    }

    private static bool DetectRepetitionAnomaly(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;

        // Pengecualian kata ulang bahasa Indonesia bertanda hubung: "Lain - Lain", "Lain-Lain", "anak-anak"
        // Konstruksi morfologis baku dilindungi dan tidak diflag sebagai pengulangan visual typo
        if (System.Text.RegularExpressions.Regex.IsMatch(text, @"\b(\w+)\s*[-—]\s*\1\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
        {
            return false;
        }

        var words = text.Split(new[] { ' ', ',', ':', ';', '.', '(', ')', '[', ']', '\"', '\'' }, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length >= 2)
        {
            for (int i = 0; i < words.Length - 1; i++)
            {
                if (words[i].Equals(words[i + 1], StringComparison.OrdinalIgnoreCase) && words[i].Length >= 2)
                {
                    return true;
                }
            }
        }
        return false;
    }

    private static bool HasAdjacentTokens(string[]? tokens, string first, string second)
    {
        if (tokens == null || tokens.Length < 2) return false;
        for (int i = 0; i < tokens.Length - 1; i++)
        {
            if (tokens[i].Equals(first, StringComparison.OrdinalIgnoreCase) &&
                tokens[i + 1].Equals(second, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Sinyal anomali karakter berbasis struktur (fallible signal).
    /// Mengidentifikasi pola yang berdeviasi dari fonotaktik lazim: triple letter, konsonan ganda, vokal ganda tak lazim.
    /// </summary>
    private bool DetectCharacterAnomaly(string token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length < 3) return false;
        string upper = token.ToUpperInvariant();

        // 1. Triple consecutive identical letters: "AAA", "SSS"
        for (int i = 0; i < upper.Length - 2; i++)
        {
            if (upper[i] == upper[i + 1] && upper[i] == upper[i + 2] && char.IsLetter(upper[i]))
                return true;
        }

        // 2. Konsonan ganda berturut-turut pada posisi fonotaktik non-lazim (fallible signal)
        for (int i = 0; i < upper.Length - 1; i++)
        {
            char c1 = upper[i];
            char c2 = upper[i + 1];
            if (char.IsLetter(c1) && c1 == c2 && !IsVowel(c1))
            {
                return true;
            }
        }

        // 3. Pengulangan vokal identik berturut-turut pada kata non-kamus (misal: "TANJUUNG", "SUKAA", "MAAA")
        // Kata valid dengan vokal kembar seperti "SAAT", "TAAT", "MAAF", "KEADAAN" ada di kamus sehingga diabaikan
        if (!_exactDict.Contains(upper))
        {
            for (int i = 0; i < upper.Length - 1; i++)
            {
                char c1 = upper[i];
                char c2 = upper[i + 1];
                if (char.IsLetter(c1) && c1 == c2 && IsVowel(c1))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool IsVowel(char c) => c is 'A' or 'I' or 'U' or 'E' or 'O';

    private static bool IsAcronymSignal(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) return false;
        string upper = token.ToUpperInvariant();
        if (upper.Length < 2 || upper.Length > 6) return false;
        if (!upper.All(char.IsLetter)) return false;

        // Hitung vokal
        int vowels = upper.Count(c => c is 'A' or 'I' or 'U' or 'E' or 'O');
        if (vowels == 0) return true; // e.g. NPSN, PLS, BPJS, KTP, SIM, SMP, SMA, LRT, MRT

        // Singkatan pendek dengan rasio konsonan tinggi (>= 75%)
        float consonantRatio = (float)(upper.Length - vowels) / upper.Length;
        if (upper.Length <= 4 && consonantRatio >= 0.75f) return true;

        return false;
    }

    private bool IsKnownLocationPhrase(string norm)
    {
        var tokens = norm.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length >= 2)
        {
            if (_locationDict.Contains(norm)) return true;
            if (tokens.All(t => _locationDict.Contains(t) || _exactDict.Contains(t)))
            {
                if (tokens.Any(t => _locationDict.Contains(t))) return true;
            }
        }
        return false;
    }

    private static bool IsNameOrBrandPattern(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;

        // Pola TitleCase Nama Orang / Entitas: "Andi", "Aprilianto", "Tukimin", "Sudirman"
        if (text.Length >= 3 && char.IsUpper(text[0]) && text.Substring(1).All(char.IsLower))
        {
            // Kata berimbuhan morfologis baku bahasa Indonesia (ber-, ter-, per-, pem-, peng-, men-, meng-, mem-)
            // adalah kata kerja/sifat umum kalimat, bukan entitas nama orang
            string upper = text.ToUpperInvariant();
            if (upper.StartsWith("BER") || upper.StartsWith("TER") ||
                upper.StartsWith("MENG") || upper.StartsWith("MEM") || upper.StartsWith("MEN") ||
                upper.StartsWith("PENG") || upper.StartsWith("PEM") || upper.StartsWith("PER"))
            {
                return false;
            }

            return true;
        }

        return false;
    }

    private static readonly System.Text.RegularExpressions.Regex MonetaryPattern = new(
        @"^(?:Rp|IDR)\.?\d+([.,]\d+)*(?:[kKmMbBtT])?$",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

    private static bool IsMonetaryToken(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        return MonetaryPattern.IsMatch(text);
    }

    private static readonly HashSet<string> PersonHonorificsAndTitles = new(StringComparer.OrdinalIgnoreCase)
    {
        "DRS", "DRS.", "DRA", "DRA.", "DR", "DR.", "IR", "IR.", "PROF", "PROF.",
        "H", "H.", "HJ", "HJ.", "KH", "K.H.", "KYAI", "USTADZ", "UST", "UST.",
        "SE", "S.E.", "SH", "S.H.", "SPD", "S.PD", "SKOM", "S.KOM", "MSI", "M.SI", "MM", "M.M.", "MH", "M.H.",
        "BPK", "BPK.", "BAPAK", "IBU", "SDR", "SDR.", "SAUDARA", "TUAN", "NY", "NY.", "NONA",
        "NUR", "EKA", "RIA", "JOHAN", "I", "NI", "IDA", "ANAK", "GUSTI", "RADEN", "SITI",
        "MUHAMMAD", "MOH", "MOH.", "MOHD", "ACHMAD", "AHMAD", "ABDUL", "BIN", "BINTI"
    };

    private static bool DetectIrregularCasing(string token)
    {
        if (string.IsNullOrEmpty(token) || token.Length < 2)
            return false;

        int upperCount = 0;
        int lowerCount = 0;
        for (int i = 0; i < token.Length; i++)
        {
            if (char.IsUpper(token[i])) upperCount++;
            else if (char.IsLower(token[i])) lowerCount++;
        }

        if (upperCount == 0 || lowerCount == 0)
            return false;

        // Standard TitleCase (e.g. "Septina", "Downy")
        if (char.IsUpper(token[0]) && upperCount == 1)
            return false;

        return true;
    }

    private static bool DetectStrongPersonContext(string token, string? leftNeighbor, string? rightNeighbor, string? spatialContext)
    {
        if (!string.IsNullOrEmpty(leftNeighbor))
        {
            string cleanLeft = leftNeighbor.Trim().TrimEnd('.', ',');
            if (PersonHonorificsAndTitles.Contains(cleanLeft) || PersonHonorificsAndTitles.Contains(leftNeighbor.Trim()))
                return true;
        }

        if (!string.IsNullOrEmpty(rightNeighbor))
        {
            string cleanRight = rightNeighbor.Trim().TrimEnd('.', ',');
            if (PersonHonorificsAndTitles.Contains(cleanRight) || PersonHonorificsAndTitles.Contains(rightNeighbor.Trim()))
                return true;
        }

        if (!string.IsNullOrEmpty(spatialContext))
        {
            string scUpper = spatialContext.ToUpperInvariant();
            if (scUpper.Contains("DRS.") || scUpper.Contains("DRS ") || scUpper.Contains("DRA.") ||
                scUpper.Contains("H. ") || scUpper.Contains("HJ. ") || scUpper.Contains("NUR ") ||
                scUpper.Contains("EKA ") || scUpper.Contains("JOHAN "))
            {
                return true;
            }
        }

        return false;
    }
}
