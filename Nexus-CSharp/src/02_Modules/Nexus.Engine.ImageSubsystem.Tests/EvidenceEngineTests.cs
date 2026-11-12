using System;
using System.Collections.Generic;
using System.Linq;
using Nexus.Engine.ImageSubsystem.Evidence;
using Nexus.Engine.ImageSubsystem.Ocr;
using Xunit;
using Xunit.Abstractions;

namespace Nexus.Engine.ImageSubsystem.Tests;

public class EvidenceEngineTests
{
    private readonly ITestOutputHelper _output;
    private readonly EvidenceEngine _engine;

    public EvidenceEngineTests(ITestOutputHelper output)
    {
        _output = output;
        _engine = new EvidenceEngine();
        _engine.InitializeDefault();
    }

    [Fact]
    public void T01_HASITL_Produces_Suspect_With_Fuzzy_Hasil()
    {
        var res = _engine.EvaluateToken("HASITL", new List<int> { 1 });

        _output.WriteLine($"T01: Action={res.Action}, State={res.State}, Evidence={res.Evidence}");
        Assert.Equal(KnowledgeState.UNKNOWN, res.State);
        Assert.Equal(PipelineAction.SUSPECT, res.Action);
        Assert.True((res.Evidence & EvidenceTypes.FUZZY_STRONG) != 0);
        Assert.Contains(res.PossibleCorrections, c => c.Word == "HASIL" && (c.Distance == 1 || c.Distance == 2));
    }

    [Fact]
    public void T02_PENINJUAN_Produces_Suspect_With_Fuzzy_Peninjauan()
    {
        var res = _engine.EvaluateToken("PENINJUAN", new List<int> { 2 });

        _output.WriteLine($"T02: Action={res.Action}, State={res.State}, Evidence={res.Evidence}");
        Assert.Equal(KnowledgeState.UNKNOWN, res.State);
        Assert.Equal(PipelineAction.SUSPECT, res.Action);
        Assert.True((res.Evidence & EvidenceTypes.FUZZY_STRONG) != 0);
        Assert.Contains(res.PossibleCorrections, c => c.Word == "PENINJAUAN" && c.Distance == 1);
    }

    [Fact]
    public void T03_ASSECORISS_Produces_Suspect_With_Weak_Fuzzy_Aksesoris()
    {
        var res = _engine.EvaluateToken("ASSECORISS", new List<int> { 3 });

        _output.WriteLine($"T03: Action={res.Action}, State={res.State}, Evidence={res.Evidence}");
        Assert.Equal(KnowledgeState.UNKNOWN, res.State);
        Assert.Equal(PipelineAction.SUSPECT, res.Action);
        Assert.True((res.Evidence & EvidenceTypes.FUZZY_WEAK) != 0);
        Assert.Contains(res.PossibleCorrections, c => c.Word == "AKSESORIS" && c.Distance == 3);
        Assert.DoesNotContain(res.PossibleCorrections, c => c.Word == "ACCESSORIES");
    }

    [Fact]
    public void T04_TUKIMIN_Produces_Passive()
    {
        var res = _engine.EvaluateToken("TUKIMIN", new List<int> { 4 });

        _output.WriteLine($"T04: Action={res.Action}, State={res.State}, Evidence={res.Evidence}");
        Assert.Equal(KnowledgeState.UNKNOWN, res.State);
        Assert.Equal(PipelineAction.PASSIVE, res.Action);
    }

    [Fact]
    public void T05_LESING_Produces_Suspect_With_Fuzzy_Leasing()
    {
        var res = _engine.EvaluateToken("LESING", new List<int> { 5 });

        _output.WriteLine($"T05: Action={res.Action}, State={res.State}, Evidence={res.Evidence}");
        Assert.Equal(KnowledgeState.UNKNOWN, res.State);
        Assert.Equal(PipelineAction.SUSPECT, res.Action);
        Assert.True((res.Evidence & EvidenceTypes.FUZZY_STRONG) != 0);
        Assert.Contains(res.PossibleCorrections, c => c.Word == "LEASING" && c.Distance == 1);
    }

    [Fact]
    public void T06_BANDA_ACEH_Produces_SafeToSkip()
    {
        _engine.LoadLocations(new[] { "BANDA ACEH" });
        var res = _engine.EvaluateToken("BANDA ACEH", new List<int> { 6 });

        _output.WriteLine($"T06: Action={res.Action}, State={res.State}, Evidence={res.Evidence}");
        Assert.Equal(KnowledgeState.KNOWN_LOKASI, res.State);
        Assert.Equal(PipelineAction.SAFE_TO_SKIP, res.Action);
        Assert.True((res.Evidence & EvidenceTypes.LOKASI_MATCH) != 0);
    }

    [Fact]
    public void T07_TANGAN_TANGAN_Produces_Suspect_With_RepetitionAnomaly()
    {
        var res = _engine.EvaluateToken("TANGAN TANGAN", new List<int> { 7 });

        _output.WriteLine($"T07: Action={res.Action}, State={res.State}, Evidence={res.Evidence}");
        Assert.Equal(PipelineAction.SUSPECT, res.Action);
        Assert.True((res.Evidence & EvidenceTypes.REPETITION_ANOMALY) != 0);
    }

    [Fact]
    public void T08_PANITIHA_Produces_Suspect_With_Fuzzy_Panitia()
    {
        var res = _engine.EvaluateToken("PANITIHA", new List<int> { 8 });

        _output.WriteLine($"T08: Action={res.Action}, State={res.State}, Evidence={res.Evidence}");
        Assert.Equal(KnowledgeState.UNKNOWN, res.State);
        Assert.Equal(PipelineAction.SUSPECT, res.Action);
        Assert.True((res.Evidence & EvidenceTypes.FUZZY_STRONG) != 0);
        Assert.Contains(res.PossibleCorrections, c => c.Word == "PANITIA" && c.Distance == 1);
    }

    [Fact]
    public void T09_Valid_Brand_AZZUQICELL_Produces_Passive()
    {
        var res = _engine.EvaluateToken("AZZUQICELL", new List<int> { 9 });

        _output.WriteLine($"T09: Action={res.Action}, State={res.State}, Evidence={res.Evidence}");
        Assert.Equal(PipelineAction.PASSIVE, res.Action);
    }

    [Fact]
    public void T10_SpatialContext_Calculates_Geometric_Line_Overlap()
    {
        var boxA = new OcrBoxModel { Id = 10, X1 = 100, Y1 = 200, X2 = 250, Y2 = 240, Text = "BAYAR" };
        var boxB = new OcrBoxModel { Id = 11, X1 = 260, Y1 = 202, X2 = 450, Y2 = 242, Text = "KREDIT" };
        var boxC = new OcrBoxModel { Id = 12, X1 = 460, Y1 = 198, X2 = 680, Y2 = 239, Text = "LESING" };
        var boxD = new OcrBoxModel { Id = 13, X1 = 100, Y1 = 300, X2 = 300, Y2 = 340, Text = "DAPATKAN CASHBACK" }; // Beda baris

        var boxes = new List<OcrBoxModel> { boxA, boxB, boxC, boxD };

        float overlapAB = SpatialContextCalculator.CalculateVerticalOverlap(boxA, boxB);
        float overlapAC = SpatialContextCalculator.CalculateVerticalOverlap(boxA, boxC);
        float overlapAD = SpatialContextCalculator.CalculateVerticalOverlap(boxA, boxD);

        string lineContextC = SpatialContextCalculator.BuildLineContext(boxC, boxes, 0.50f);

        _output.WriteLine($"T10 Overlap A-B: {overlapAB:F2}, A-C: {overlapAC:F2}, A-D: {overlapAD:F2}");
        _output.WriteLine($"T10 Context C: {lineContextC}");

        Assert.True(overlapAB >= 0.80f);
        Assert.True(overlapAC >= 0.80f);
        Assert.Equal(0.0f, overlapAD);
        Assert.Contains("BAYAR", lineContextC);
        Assert.Contains("KREDIT", lineContextC);
        Assert.Contains("[LESING]", lineContextC);
        Assert.DoesNotContain("DAPATKAN CASHBACK", lineContextC);
    }

    [Fact]
    public void T11_InstanceCandidateId_UniquePerTokenAcrossBoxes()
    {
        var boxes = new List<OcrBoxModel>
        {
            new() { Id = 12, X1 = 100, Y1 = 100, X2 = 200, Y2 = 130, Text = "PANITIHA", DetScore = 0.95f, RecConfidence = 0.95f },
            new() { Id = 15, X1 = 100, Y1 = 150, X2 = 200, Y2 = 180, Text = "HASITL", DetScore = 0.95f, RecConfidence = 0.95f },
            new() { Id = 45, X1 = 100, Y1 = 200, X2 = 200, Y2 = 230, Text = "PANITIHA", DetScore = 0.95f, RecConfidence = 0.92f },
            new() { Id = 88, X1 = 100, Y1 = 250, X2 = 200, Y2 = 280, Text = "PANITIHA", DetScore = 0.95f, RecConfidence = 0.90f },
            new() { Id = 90, X1 = 100, Y1 = 300, X2 = 200, Y2 = 330, Text = "HASIL", DetScore = 0.95f, RecConfidence = 0.98f }
        };

        var results = _engine.ProcessBoxes(boxes);

        var panitihaCandidates = results.Where(r => r.RawToken.Equals("PANITIHA", StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.Equal(3, panitihaCandidates.Count);
        Assert.Contains(panitihaCandidates, c => c.CandidateId == "12_0" && c.Action == PipelineAction.SUSPECT);
        Assert.Contains(panitihaCandidates, c => c.CandidateId == "45_0" && c.Action == PipelineAction.SUSPECT);
        Assert.Contains(panitihaCandidates, c => c.CandidateId == "88_0" && c.Action == PipelineAction.SUSPECT);

        var hasitlCandidate = results.FirstOrDefault(r => r.RawToken.Equals("HASITL", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(hasitlCandidate);
        Assert.Equal("15_0", hasitlCandidate.CandidateId);
        Assert.Equal(PipelineAction.SUSPECT, hasitlCandidate.Action);

        var hasilCandidate = results.FirstOrDefault(r => r.RawToken.Equals("HASIL", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(hasilCandidate);
        Assert.Equal(PipelineAction.SAFE_TO_SKIP, hasilCandidate.Action);
    }

    [Fact]
    public void T12_Bug3_ChineseCharactersAndVisualNoise_RejectedAsSafeToSkip()
    {
        // BUG 3: Teks dekoratif/daun terbaca karakter China PP-OCRv6 seperti '上新R司'
        var resChina = _engine.EvaluateToken("上新R司", new List<int> { 91 });
        Assert.Equal(PipelineAction.SAFE_TO_SKIP, resChina.Action);
        Assert.Contains("noise visual", resChina.Notes, StringComparison.OrdinalIgnoreCase);

        // MeaningfulText unit tests
        Assert.False(PpOcrEngine.IsMeaningfulText("上新R司"));
        Assert.False(PpOcrEngine.IsMeaningfulText("🍃🌿"));
        Assert.False(PpOcrEngine.IsMeaningfulText("A"));
        Assert.False(PpOcrEngine.IsMeaningfulText("   "));
        Assert.True(PpOcrEngine.IsMeaningfulText("Selamat Datang"));
        Assert.True(PpOcrEngine.IsMeaningfulText("Puskesmas Grogol"));
        Assert.True(PpOcrEngine.IsMeaningfulText("OK"));
    }

    [Fact]
    public void T13_StructuredReference_ExactMatch_ProtectsConstituentTokens()
    {
        _engine.LoadStructuredReferences(new[]
        {
            new StructuredReferencePhrase
            {
                Text = "Kecamatan Baturaja Timur",
                Category = "Wilayah",
                DatasetId = "wilayah-oku"
            }
        });

        var boxes = new List<OcrBoxModel>
        {
            new() { Id = 10, Text = "KECAMATAN BATURAJA TIMUR", DetScore = 0.95f, RecConfidence = 0.95f }
        };

        var results = _engine.ProcessBoxes(boxes);

        // Semua token dalam frasa exact match harus dilindungi (SAFE_TO_SKIP)
        var baturaja = results.FirstOrDefault(r => r.RawToken.Equals("BATURAJA", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(baturaja);
        Assert.Equal(PipelineAction.SAFE_TO_SKIP, baturaja.Action);
        Assert.Equal(KnowledgeState.KNOWN_REFERENCE, baturaja.State);
        Assert.True((baturaja.Evidence & EvidenceTypes.REFERENCE_MATCH) != 0);
    }

    [Fact]
    public void T14_StructuredReference_NearMatch_AlignsChangedToken()
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
            new() { Id = 10, Text = "DESA TANJUUNG BARU", DetScore = 0.95f, RecConfidence = 0.95f }
        };

        var results = _engine.ProcessBoxes(boxes);

        // TANJUUNG harus diisolasi sebagai SUSPECT dengan target TANJUNG
        var tanjuung = results.FirstOrDefault(r => r.RawToken.Equals("TANJUUNG", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(tanjuung);
        Assert.Equal(PipelineAction.SUSPECT, tanjuung.Action);
        Assert.Equal("10_1", tanjuung.CandidateId);
        Assert.Equal(1, tanjuung.TokenIndex);
        Assert.True((tanjuung.Evidence & EvidenceTypes.REFERENCE_NEAR_MATCH) != 0);
        Assert.Contains(tanjuung.PossibleCorrections, c => c.Word == "TANJUNG");

        // DESA dan BARU harus dilindungi
        var desa = results.FirstOrDefault(r => r.RawToken.Equals("DESA", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(desa);
        Assert.Equal(PipelineAction.SAFE_TO_SKIP, desa.Action);

        var baru = results.FirstOrDefault(r => r.RawToken.Equals("BARU", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(baru);
        Assert.Equal(PipelineAction.SAFE_TO_SKIP, baru.Action);
    }

    [Fact]
    public void T15_StableCandidateId_UniquePerTokenInstance()
    {
        var boxes = new List<OcrBoxModel>
        {
            new() { Id = 10, Text = "TANJUUNG BATURAJA", DetScore = 0.95f, RecConfidence = 0.95f }
        };

        var results = _engine.ProcessBoxes(boxes);

        Assert.Equal(2, results.Count);
        Assert.Equal("10_0", results[0].CandidateId);
        Assert.Equal(0, results[0].TokenIndex);
        Assert.Equal("10_1", results[1].CandidateId);
        Assert.Equal(1, results[1].TokenIndex);
    }

    [Fact]
    public void T16_Acronym_Signal_RemainsPassive()
    {
        // NPSN dan PLS adalah akronim valid tanpa anomali karakter
        var resNpsn = _engine.EvaluateToken("NPSN", new List<int> { 1 });
        Assert.Equal(PipelineAction.PASSIVE, resNpsn.Action);
        Assert.True((resNpsn.Evidence & EvidenceTypes.ACRONYM_SIGNAL) != 0);

        var resPls = _engine.EvaluateToken("PLS", new List<int> { 2 });
        Assert.Equal(PipelineAction.PASSIVE, resPls.Action);
        Assert.True((resPls.Evidence & EvidenceTypes.ACRONYM_SIGNAL) != 0);
    }

    [Fact]
    public void T17_UnknownToken_WithoutConvergingEvidence_RemainsPassive()
    {
        // Kata OOV tanpa bukti anomali konvergen (tidak ada di 20K tapi bukan typo)
        var res = _engine.EvaluateToken("BATURAJA", new List<int> { 1 });
        Assert.Equal(PipelineAction.PASSIVE, res.Action);
    }

    [Fact]
    public void T18_HarggaTerjangkau_CandidateSegmentation_SegmentsCorrectly()
    {
        var boxes = new List<OcrBoxModel>
        {
            new() { Id = 5, Text = "HarggaTerjangkau", DetScore = 0.91f, RecConfidence = 0.94f }
        };

        var results = _engine.ProcessBoxes(boxes);

        Assert.Equal(2, results.Count);

        var hargga = results.FirstOrDefault(r => r.RawToken.Equals("Hargga", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(hargga);
        Assert.Equal(PipelineAction.SUSPECT, hargga.Action);
        Assert.Contains(hargga.PossibleCorrections, c => c.Word == "HARGA");

        var terjangkau = results.FirstOrDefault(r => r.RawToken.Equals("Terjangkau", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(terjangkau);
        Assert.Equal(PipelineAction.SAFE_TO_SKIP, terjangkau.Action);
    }

    [Fact]
    public void T19_MixedCase_BrandNames_NotSplit_RemainIntact()
    {
        string[] brands = { "iPhone", "eBay", "PowerPoint", "YouTube", "OpenAI", "McDonald's", "WiFi" };
        foreach (var b in brands)
        {
            bool split = _engine.TryEvaluateCaseTransitionSegmentation(b, out var segs);
            Assert.False(split);
        }
    }

    [Fact]
    public void T20_Berkulitas_WholeTokenContext_Ranks_Berkualitas_Above_Berkulit()
    {
        string token = "Berkulitas";
        string context = "[Dengan Rasa yang Enak dan Berkulitas]";
        var res = _engine.EvaluateToken(token, new List<int> { 9 }, 0.99f, context);

        Assert.Equal(PipelineAction.SUSPECT, res.Action);
        Assert.True(res.PossibleCorrections.Count >= 2);

        int idxBerkualitas = res.PossibleCorrections.FindIndex(c => c.Word == "BERKUALITAS");
        int idxBerkulit = res.PossibleCorrections.FindIndex(c => c.Word == "BERKULIT");

        Assert.True(idxBerkualitas >= 0, "BERKUALITAS harus ada di daftar koreksi");
        if (idxBerkulit >= 0)
        {
            Assert.True(idxBerkualitas < idxBerkulit,
                $"BERKUALITAS (idx={idxBerkualitas}) harus di atas BERKULIT (idx={idxBerkulit})");
        }
        Assert.Equal("BERKUALITAS", res.PossibleCorrections[0].Word);
    }

    [Fact]
    public void T21_LainLain_HyphenatedReduplication_NotRepetitionAnomaly()
    {
        var boxes = new List<OcrBoxModel>
        {
            new() { Id = 19, Text = "Lain - Lain", DetScore = 0.95f, RecConfidence = 0.98f }
        };

        var results = _engine.ProcessBoxes(boxes);

        Assert.True(results.Count > 0);
        foreach (var r in results)
        {
            Assert.NotEqual(PipelineAction.SUSPECT, r.Action);
            Assert.False((r.Evidence & EvidenceTypes.REPETITION_ANOMALY) != 0,
                $"Token '{r.RawToken}' tidak boleh diflag REPETITION_ANOMALY karena bertanda hubung");
        }
    }

    [Fact]
    public void T22_MiyagGoreng_Distance2_WithValidNeighbor_ProducesSuspect()
    {
        var boxes = new List<OcrBoxModel>
        {
            new() { Id = 34, Text = "Miyag Goreng", DetScore = 0.94f, RecConfidence = 1.0f }
        };

        var results = _engine.ProcessBoxes(boxes);

        var miyag = results.FirstOrDefault(r => r.RawToken.Equals("Miyag", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(miyag);
        Assert.Equal(PipelineAction.SUSPECT, miyag.Action);
        Assert.Contains(miyag.PossibleCorrections, c => c.Word == "MINYAK");
    }

    [Fact]
    public void T23_MinumanBotan_SemanticRegionGating_PrefersBotolOverBatang()
    {
        var boxes = new List<OcrBoxModel>
        {
            new() { Id = 44, Text = "Minuman Botan", DetScore = 0.95f, RecConfidence = 1.0f }
        };

        var results = _engine.ProcessBoxes(boxes);

        var botan = results.FirstOrDefault(r => r.RawToken.Equals("Botan", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(botan);
        Assert.True(botan.PossibleCorrections.Count > 0);

        int idxBotol = botan.PossibleCorrections.FindIndex(c => c.Word == "BOTOL");
        int idxBatang = botan.PossibleCorrections.FindIndex(c => c.Word == "BATANG");

        Assert.True(idxBotol >= 0, "BOTOL harus ada di daftar kandidat");
        if (idxBatang >= 0)
        {
            Assert.True(idxBotol < idxBatang,
                $"BOTOL (idx={idxBotol}) harus di atas BATANG (idx={idxBatang}) karena baris bukan wilayah administratif");
        }
    }

    [Fact]
    public void T24_TempePenyet_CulinaryDomain_Safe()
    {
        var boxes = new List<OcrBoxModel>
        {
            new() { Id = 28, Text = "Tempe Penyet", DetScore = 0.92f, RecConfidence = 1.0f }
        };

        var results = _engine.ProcessBoxes(boxes);

        var penyet = results.FirstOrDefault(r => r.RawToken.Equals("Penyet", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(penyet);
        Assert.Equal(PipelineAction.SAFE_TO_SKIP, penyet.Action);
    }

    [Fact]
    public void T25_UserDictionary_MultiWordPhrase_Protects_Phrase_Via_StructuredReferences()
    {
        // Frasa multi-kata dari User Dictionary didaftarkan ke StructuredReferences (sesuai kontrak arsitektur di AiCheckViewModel)
        _engine.LoadStructuredReferences(new[]
        {
            new StructuredReferencePhrase { Text = "OGAN KOMERING ULU", DatasetId = "user-dictionary", Category = "UserDictionary" },
            new StructuredReferencePhrase { Text = "SEBIMBING SEKUNDANG", DatasetId = "user-dictionary", Category = "UserDictionary" }
        });
        _engine.LoadUserDictionary(new[] { "SRIWIJAYA" });

        var boxes = new List<OcrBoxModel>
        {
            new() { Id = 1, Text = "OGAN KOMERING ULU", DetScore = 0.94f, RecConfidence = 1.0f },
            new() { Id = 2, Text = "PEMERINTAH KABUPATEN OGAN KOMERING ULU", DetScore = 0.97f, RecConfidence = 1.0f },
            new() { Id = 3, Text = "SRIWIJAYA", DetScore = 0.90f, RecConfidence = 1.0f }
        };

        var results = _engine.ProcessBoxes(boxes);

        // Pastikan kemunculan frasa utuh "OGAN KOMERING ULU" di Box 1 dan Box 2 terlindungi (SAFE_TO_SKIP)
        var oganTokens = results.Where(r => r.RawToken.Equals("OGAN", StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.NotEmpty(oganTokens);
        foreach (var ogan in oganTokens)
        {
            Assert.Equal(PipelineAction.SAFE_TO_SKIP, ogan.Action);
        }

        var sriwijaya = results.FirstOrDefault(r => r.RawToken.Equals("SRIWIJAYA", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(sriwijaya);
        Assert.Equal(PipelineAction.SAFE_TO_SKIP, sriwijaya.Action);
    }

    [Fact]
    public void UserDictionary_ConstituentToken_DoesNotGrant_GlobalImmunity()
    {
        // User memasukkan frasa multi-kata "OGAN KOMERING ULU" ke StructuredReferences
        _engine.LoadStructuredReferences(new[]
        {
            new StructuredReferencePhrase { Text = "OGAN KOMERING ULU", DatasetId = "user-dictionary", Category = "UserDictionary" }
        });
        // UserDictionary flat hanya berisi entri jika pengguna memasukkannya langsung, bukan memecah frasa
        _engine.LoadUserDictionary(new[] { "OGAN KOMERING ULU" });

        // Box 1: Frasa utuh terlindungi
        // Box 2: Kata "OGAN" berdiri sendiri tanpa konteks frasa referensi
        var boxes = new List<OcrBoxModel>
        {
            new() { Id = 1, Text = "OGAN KOMERING ULU", DetScore = 0.95f, RecConfidence = 1.0f },
            new() { Id = 2, Text = "OGAN", DetScore = 0.85f, RecConfidence = 0.75f }
        };

        var results = _engine.ProcessBoxes(boxes);

        // Token OGAN pada frasa utuh (Box 1) terlindungi SAFE_TO_SKIP
        var phraseOgan = results.FirstOrDefault(r => r.BoxIds.Contains(1) && r.RawToken.Equals("OGAN", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(phraseOgan);
        Assert.Equal(PipelineAction.SAFE_TO_SKIP, phraseOgan.Action);

        // Token OGAN berdiri sendiri (Box 2) TIDAK kebal secara leksikal global; dievaluasi normal
        var isolatedOgan = results.FirstOrDefault(r => r.BoxIds.Contains(2) && r.RawToken.Equals("OGAN", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(isolatedOgan);
        // Isolated OGAN dievaluasi secara normal (tidak berstatus KNOWN_USER_DICT karena bukan kata tunggal yang dimasukkan ke kamus)
        Assert.NotEqual(KnowledgeState.KNOWN_USER_DICT, isolatedOgan.State);
    }

    [Fact]
    public void WholeTokenContext_DoesNotMatch_Substring_BatuVsBaturaja()
    {
        _engine.LoadStructuredReferences(new[]
        {
            new StructuredReferencePhrase { Text = "KECAMATAN BATURAJA TIMUR", DatasetId = "wilayah", Category = "Administrasi" }
        });

        // "BATU" berdekatan dengan "TIMUR", tetapi dalam referensi kata sebenarnya adalah "BATURAJA", bukan "BATU"
        // Dengan pencocokan exact token, candidate "BATU" tidak boleh cocok dengan "BATURAJA" hanya karena substring
        var boxes = new List<OcrBoxModel>
        {
            new() { Id = 1, Text = "BATU TIMUR", DetScore = 0.90f, RecConfidence = 0.80f }
        };

        var results = _engine.ProcessBoxes(boxes);
        var batuToken = results.FirstOrDefault(r => r.RawToken.Equals("BATU", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(batuToken);
        // BATU adalah kata kamus bahasa Indonesia ("batu") -> SAFE_TO_SKIP tanpa context false boost
        Assert.Equal(PipelineAction.SAFE_TO_SKIP, batuToken.Action);
    }

    [Fact]
    public void RepeatedCharacter_IsDetectedStructurally()
    {
        // "TANJUUNG" memiliki vokal ganda berturut-turut "UU" pada kata non-kamus
        var resTanjuung = _engine.EvaluateToken("TANJUUNG", new List<int> { 1 });
        Assert.Equal(PipelineAction.SUSPECT, resTanjuung.Action);
        Assert.True((resTanjuung.Evidence & EvidenceTypes.CHARACTER_ANOMALY) != 0);
        Assert.Contains(resTanjuung.PossibleCorrections, c => c.Word == "TANJUNG");

        // "HARGGA" memiliki konsonan ganda non-lazim "GG"
        var resHargga = _engine.EvaluateToken("HARGGA", new List<int> { 2 });
        Assert.Equal(PipelineAction.SUSPECT, resHargga.Action);
        Assert.True((resHargga.Evidence & EvidenceTypes.CHARACTER_ANOMALY) != 0);
        Assert.Contains(resHargga.PossibleCorrections, c => c.Word == "HARGA");

        // Kata kamus valid dengan vokal kembar seperti "SAAT" tidak diflag anomali karakter
        var resSaat = _engine.EvaluateToken("SAAT", new List<int> { 3 });
        Assert.Equal(PipelineAction.SAFE_TO_SKIP, resSaat.Action);
        Assert.False((resSaat.Evidence & EvidenceTypes.CHARACTER_ANOMALY) != 0);
    }

    [Fact]
    public void ThreeTierTriageFilter_GenericBehavior_WithoutPaninBank()
    {
        var filter = new Nexus.Engine.ImageSubsystem.Triage.ThreeTierTriageFilter();

        // Token tunggal dengan 1 transisi camelCase wajar (seperti "PaninBank" atau "IndoFood")
        // tidak boleh diflag sebagai anomali huruf bertumpuk
        var boxSingleTransition = new OcrBoxModel { Id = 1, Text = "PaninBank", DetScore = 0.95f, RecConfidence = 0.95f };
        var findings1 = filter.Evaluate(new[] { boxSingleTransition }, (IReadOnlyList<string>?)null);
        Assert.Empty(findings1);

        // Teks dengan transisi berulang / bertumpuk ("YangMaJuMdaS") harus diflag Layer 2B
        var boxMultipleTransitions = new OcrBoxModel { Id = 2, Text = "YangMaJuMdaS", DetScore = 0.95f, RecConfidence = 0.95f };
        var findings2 = filter.Evaluate(new[] { boxMultipleTransitions }, (IReadOnlyList<string>?)null);
        Assert.Single(findings2);
        Assert.True((findings2[0].LayerFlags & Nexus.Engine.ImageSubsystem.Triage.TriageLayerFlags.Layer2B_IrregularCasing) != 0);
    }

    [Fact]
    public void ProductionDomainLexicon_LoadsSuccessfully()
    {
        var engine = new EvidenceEngine();
        engine.LoadDomainLexicon(Nexus.Contracts.ReferenceData.ReferenceConstants.BuiltInDomainPhrases);

        // Pastikan kata leksikon domain kuliner/retail diakui di domain dictionary
        Assert.True(engine.DomainDictionaryCount >= 10);
        var resGeprek = engine.EvaluateToken("GEPREK", new List<int> { 1 });
        Assert.Equal(PipelineAction.SAFE_TO_SKIP, resGeprek.Action);
    }

    [Fact]
    public void Test_ProcessBoxes_CourierProperties_SourceDerivation()
    {
        var boxes = new List<OcrBoxModel>
        {
            new()
            {
                Id = 42,
                Text = "HASITL PENILAIAN",
                DetScore = 0.88f,
                RecConfidence = 0.92f
            }
        };

        var results = _engine.ProcessBoxes(boxes);
        Assert.NotEmpty(results);

        foreach (var r in results)
        {
            Assert.Equal(42, r.PrimaryBoxId);
            Assert.Equal(0.88f, r.DetScore);
            Assert.Equal(0.92f, r.MinRecConfidence);
            Assert.Equal("HASITL PENILAIAN", r.BoxText);
        }
    }

    [Fact]
    public void Test_EvaluateToken_MarginSeparation_WithoutRedirect()
    {
        var res = _engine.EvaluateToken("HASITL", new List<int> { 1 });

        Assert.True(res.PossibleCorrections.Count > 1, "HASITL harus menghasilkan > 1 koreksi");
        Assert.True(res.HasFuzzyRunnerUp);
        Assert.Equal(res.PossibleCorrections[0].PrimaryScore - res.PossibleCorrections[1].PrimaryScore, res.TrueFuzzyMargin, 5);
        Assert.Equal(res.FuzzyMargin, res.G3LowMargin);
        Assert.Equal(res.PossibleCorrections[0].Word, res.Top1Suggestion);
        Assert.Equal(res.PossibleCorrections[0].Distance, res.Top1Distance);
        Assert.Equal(res.PossibleCorrections[0].Distance, res.LevenshteinDistance);
        Assert.Equal(res.PossibleCorrections.Select(c => c.Word).ToList(), res.PossibleCorrectionWords);
    }

    [Fact]
    public void Test_EvaluateToken_HasStrongEvidence_Parity()
    {
        // 1. Token SUSPECT dengan FUZZY_STRONG -> HasStrongEvidence true
        var resSuspect1 = _engine.EvaluateToken("HASITL", new List<int> { 1 });
        Assert.Equal(PipelineAction.SUSPECT, resSuspect1.Action);
        Assert.True(resSuspect1.HasStrongEvidence);

        var resSuspect2 = _engine.EvaluateToken("PENINJUAN", new List<int> { 2 });
        Assert.Equal(PipelineAction.SUSPECT, resSuspect2.Action);
        Assert.True(resSuspect2.HasStrongEvidence);

        // 2. Token non-kamus tanpa evidensi leksikal kuat (PASSIVE) -> HasStrongEvidence false
        var resPassive = _engine.EvaluateToken("XYZABCQWERTY", new List<int> { 3 });
        Assert.Equal(PipelineAction.PASSIVE, resPassive.Action);
        Assert.False(resPassive.HasStrongEvidence);
    }
}

