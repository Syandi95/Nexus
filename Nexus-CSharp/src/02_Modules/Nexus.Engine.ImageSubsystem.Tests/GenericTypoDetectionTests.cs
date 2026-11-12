using System;
using System.Collections.Generic;
using System.Linq;
using Nexus.Engine.ImageSubsystem.Evidence;
using Nexus.Engine.ImageSubsystem.Ocr;
using Xunit;
using Xunit.Abstractions;

namespace Nexus.Engine.ImageSubsystem.Tests;

/// <summary>
/// Rangkaian pengujian generik untuk Multi-Evidence Candidate Engine.
/// Menguji keseimbangan Recall (daya tangkap typo) dan Precision (penekanan false positive)
/// tanpa mengandalkan aturan perkabelan keras atas kata tertentu.
/// </summary>
public class GenericTypoDetectionTests
{
    private readonly ITestOutputHelper _output;
    private readonly EvidenceEngine _engine;

    public GenericTypoDetectionTests(ITestOutputHelper output)
    {
        _output = output;
        _engine = new EvidenceEngine();
        _engine.InitializeDefault();
    }

    // =========================================================================
    // 1. RECALL: Pengujian Daya Tangkap Pola Typo (Tanpa Hardcoded Rules)
    // =========================================================================

    [Fact]
    public void Recall_MissingCharacter_ProducesSuspectWithCorrection()
    {
        // Karakter hilang (deletion typo): 'PEMLIHAN' (kurang 'I' dari 'PEMILIHAN')
        var res = _engine.EvaluateToken("PEMLIHAN", new List<int> { 1 });

        _output.WriteLine($"Missing Char: Action={res.Action}, PossibleCorrections={string.Join(", ", res.PossibleCorrections.Select(c => $"{c.Word}(d={c.Distance})"))}");
        Assert.Equal(PipelineAction.SUSPECT, res.Action);
        Assert.Contains(res.PossibleCorrections, c => c.Word == "PEMILIHAN" && c.Distance == 1);
    }

    [Fact]
    public void Recall_ExcessCharacter_ProducesSuspectWithCorrection()
    {
        // Karakter berlebih (insertion typo): 'PANITIHA' (kelebihan 'H' dari 'PANITIA')
        var res = _engine.EvaluateToken("PANITIHA", new List<int> { 2 });

        _output.WriteLine($"Excess Char: Action={res.Action}, Top={res.PossibleCorrections.FirstOrDefault()?.Word}");
        Assert.Equal(PipelineAction.SUSPECT, res.Action);
        Assert.Contains(res.PossibleCorrections, c => c.Word == "PANITIA" && c.Distance == 1);
    }

    [Fact]
    public void Recall_SubstitutedCharacter_ProducesSuspectWithCorrection()
    {
        // Karakter salah (substitution typo): 'HASITL' (penggantian 'I'/'L' atau sisipan 'T' dari 'HASIL')
        var res = _engine.EvaluateToken("HASITL", new List<int> { 3 });

        _output.WriteLine($"Substituted Char: Action={res.Action}, Top={res.PossibleCorrections.FirstOrDefault()?.Word}");
        Assert.Equal(PipelineAction.SUSPECT, res.Action);
        Assert.Contains(res.PossibleCorrections, c => c.Word == "HASIL");
    }

    [Fact]
    public void Recall_TranspositionCharacter_DamerauLevenshteinDistance()
    {
        // Verifikasi matematis metrik Damerau-Levenshtein aktual
        int distTransposition = BKTree.DamerauLevenshteinDistance("TEAPT", "TEPAT");
        int distNormalLevenshtein = BKTree.LevenshteinDistance("TEAPT", "TEPAT");

        _output.WriteLine($"Transposition: Damerau={distTransposition}, Normal={distNormalLevenshtein}");
        Assert.Equal(1, distTransposition);
        Assert.Equal(2, distNormalLevenshtein);

        // Uji evaluasi token dengan transposisi
        var res = _engine.EvaluateToken("TEAPT", new List<int> { 4 });
        Assert.Equal(PipelineAction.SUSPECT, res.Action);
        Assert.Contains(res.PossibleCorrections, c => c.Word == "TEPAT" && c.Distance == 1);
    }

    [Fact]
    public void Recall_CharacterRepetition_StructuralAnomalyFlagged()
    {
        // Repetisi karakter ganda non-lazim: 'ASSECORISS'
        var res = _engine.EvaluateToken("ASSECORISS", new List<int> { 5 });

        _output.WriteLine($"Char Repetition: Action={res.Action}, Evidence={res.Evidence}");
        Assert.True((res.Evidence & EvidenceTypes.CHARACTER_ANOMALY) != 0);
        Assert.Equal(PipelineAction.SUSPECT, res.Action);
    }

    [Fact]
    public void Recall_AdjacentWordRepetition_GeneratesSingleAnomalyInstance()
    {
        // Uji repetisi kata pada token sequence:
        // "NAMA DAN DAN NOMOR" -> 'DAN' ke-1 suspect, 'DAN' ke-2 disupresi
        var boxes = new List<OcrBoxModel>
        {
            new() { Id = 10, Text = "NAMA DAN DAN NOMOR", DetScore = 0.95f, RecConfidence = 0.95f }
        };

        var results = _engine.ProcessBoxes(boxes);

        var danResults = results.Where(r => r.RawToken.Equals("DAN", StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.Equal(2, danResults.Count);

        // Token pertama harus diangkat sebagai SUSPECT
        Assert.Equal(PipelineAction.SUSPECT, danResults[0].Action);
        Assert.True((danResults[0].Evidence & EvidenceTypes.REPETITION_ANOMALY) != 0);

        // Token kedua harus disupresi (SAFE_TO_SKIP) agar tidak menghasilkan 2 kartu di UI
        Assert.Equal(PipelineAction.SAFE_TO_SKIP, danResults[1].Action);
        Assert.Contains("disupresi", danResults[1].Notes, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Recall_RepetitionDetection_IndependentOfStructuredPhrases()
    {
        // Pengujian independensi: buat instance engine bersih tanpa structured reference phrases
        var cleanEngine = new EvidenceEngine();
        cleanEngine.InitializeDefault();

        var boxes = new List<OcrBoxModel>
        {
            new() { Id = 20, Text = "NAMA DAN TANGAN TANGAN SAKSI CALON", DetScore = 0.95f, RecConfidence = 0.95f }
        };

        var results = cleanEngine.ProcessBoxes(boxes);

        var tanganSuspects = results.Where(r => r.RawToken.Equals("TANGAN", StringComparison.OrdinalIgnoreCase) && r.Action == PipelineAction.SUSPECT).ToList();

        // Repetisi harus tetap terdeteksi secara otonom menghasilkan 1 suspect
        Assert.Single(tanganSuspects);
        Assert.True((tanganSuspects[0].Evidence & EvidenceTypes.REPETITION_ANOMALY) != 0);

        // Token kedua harus berstatus SAFE_TO_SKIP
        var tanganSkipped = results.Where(r => r.RawToken.Equals("TANGAN", StringComparison.OrdinalIgnoreCase) && r.Action == PipelineAction.SAFE_TO_SKIP).ToList();
        Assert.Single(tanganSkipped);
    }

    [Fact]
    public void Recall_StructuredReferenceNearMatch_AlignsTargetWord()
    {
        _engine.LoadStructuredReferences(new[]
        {
            new StructuredReferencePhrase
            {
                Text = "Desa Tanjung Baru",
                Category = "Wilayah",
                DatasetId = "wilayah-oku"
            }
        });

        var boxes = new List<OcrBoxModel>
        {
            new() { Id = 30, Text = "DESA TANJUUNG BARU", DetScore = 0.95f, RecConfidence = 0.95f }
        };

        var results = _engine.ProcessBoxes(boxes);

        var suspect = results.FirstOrDefault(r => r.Action == PipelineAction.SUSPECT);
        Assert.NotNull(suspect);
        Assert.Equal("TANJUUNG", suspect.RawToken);
        Assert.Equal("TANJUNG", suspect.PossibleCorrections.FirstOrDefault()?.Word);
    }

    // =========================================================================
    // 2. PRECISION: Penekanan False Positive (Bukan Kandidat Typo)
    // =========================================================================

    [Fact]
    public void Precision_ValidOovName_SuppressedAsPassive()
    {
        // Nama diri wajar non-kamus: 'TUKIMIN' -> TIDAK boleh menjadi SUSPECT
        var res = _engine.EvaluateToken("TUKIMIN", new List<int> { 40 });

        _output.WriteLine($"Valid Name: Action={res.Action}, State={res.State}");
        Assert.Equal(PipelineAction.PASSIVE, res.Action);
    }

    [Fact]
    public void Precision_ValidBrandPattern_SuppressedAsPassive()
    {
        // Pola brand/nama toko: 'AZZUQICELL' -> TIDAK boleh menjadi SUSPECT
        var res = _engine.EvaluateToken("AZZUQICELL", new List<int> { 41 });

        _output.WriteLine($"Valid Brand: Action={res.Action}");
        Assert.Equal(PipelineAction.PASSIVE, res.Action);
    }

    [Fact]
    public void Precision_ValidAcronyms_SuppressedAsPassive()
    {
        // Akronim dan singkatan umum tanpa vokal atau konsonan pendek
        string[] acronyms = { "KTP", "NPSN", "BPJS", "BPD", "TPS", "RW", "RT" };

        foreach (var acr in acronyms)
        {
            var res = _engine.EvaluateToken(acr, new List<int> { 42 });
            _output.WriteLine($"Acronym '{acr}': Action={res.Action}");
            Assert.NotEqual(PipelineAction.SUSPECT, res.Action);
            Assert.True(res.Action is PipelineAction.PASSIVE or PipelineAction.SAFE_TO_SKIP);
        }
    }

    [Fact]
    public void Precision_TieBreaker_CannotOverturnStrongerPrimaryEvidence()
    {
        // Uji batas tie-breaker:
        // Kata A: Distance = 1, Similarity = 0.80, Rank = 10.000
        // Kata B: Distance = 2, Similarity = 0.60, Rank = 1 (kata paling sering: "yang")
        // Skor Kata A harus tetap lebih tinggi daripada Kata B karena bukti primernya (jarak & kesamaan) jauh lebih kuat!
        double scoreCandidateA = _engine.CalculateCandidateScore("CONTOX", "CONTOH", 1, false, false, "", 1.0f);
        double scoreCandidateB = _engine.CalculateCandidateScore("CONTOX", "YANG", 2, false, false, "", 1.0f);

        _output.WriteLine($"Score Candidate A (d=1, rank rendah): {scoreCandidateA:F4}");
        _output.WriteLine($"Score Candidate B (d=2, rank #1 'yang'): {scoreCandidateB:F4}");

        Assert.True(scoreCandidateA > scoreCandidateB, "Tie-breaker frekuensi tidak boleh mengalahkan bukti primer kemiripan string.");
    }

    [Fact]
    public void Benchmark_ContextDesia_RanksDesaAboveDesis()
    {
        // Benchmark Konteks: Evaluasi candidate scoring pada query 'DESIA'
        var res = _engine.EvaluateToken("DESIA", new List<int> { 50 }, spatialContext: "PANITIA PEMILIHAN BPD DESIA MARKISA");

        _output.WriteLine($"Benchmark DESIA: Action={res.Action}");
        foreach (var c in res.PossibleCorrections.Take(5))
        {
            _output.WriteLine($"  - Candidate: {c.Word} (d={c.Distance}, Grade={c.Grade})");
        }

        Assert.True(res.PossibleCorrections.Count >= 2);
        // Dalam benchmark konteks ini, ranking evidence harus menempatkan 'DESA' di urutan teratas sebelum 'DESIS'
        Assert.Equal("DESA", res.PossibleCorrections[0].Word);
        Assert.Contains(res.PossibleCorrections, c => c.Word == "DESIS");
    }
}
