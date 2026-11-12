using System;
using System.Collections.Generic;
using System.Linq;
using Nexus.Contracts.ReferenceData;
using Nexus.Engine.ImageSubsystem.Evidence;
using Nexus.Engine.ImageSubsystem.Ocr;
using Xunit;
using Xunit.Abstractions;

namespace Nexus.Engine.ImageSubsystem.Tests;

/// <summary>
/// Unit tests pembuktian Entity Reference sebagai dimensi bukti baru pada NEXUS AI Check.
/// Memverifikasi 9 kriteria formal:
/// 1. Exact entity match -> Strong evidence & occurrence-specific protection
/// 2. Ambiguous alias -> Evidence only, NO auto-protection
/// 3. Near entity -> Passes through existing candidate gate (tidak langsung SUSPECT)
/// 4. Constituent isolation -> Kata konstituen ("BABEL") sendiri tidak mendapat kekebalan global
/// 5. Unambiguous alias -> ENTITY_ALIAS_MATCH & occurrence protection
/// 6. Category neutrality -> Engine generik tanpa hardcode kategori/kata
/// 7. Candidate ranking mathematical dominance guard -> Delta P > 0.01 mutlak mengalahkan entity bonus
/// 8. Explicit comparator PrimaryScore vs Frequency -> Frequency tidak membalik ranking jika Delta P > 0.01
/// 9. Existing regression suite -> Hargga, Berkulitas, Miyag, Botan, Sukamju, Tanjuung, Lain-Lain tetap utuh
/// </summary>
public class EntityReferenceEvidenceTests
{
    private readonly ITestOutputHelper _output;
    private readonly EvidenceEngine _engine;

    public EntityReferenceEvidenceTests(ITestOutputHelper output)
    {
        _output = output;
        _engine = new EvidenceEngine();
        _engine.InitializeDefault();
    }

    [Fact]
    public void Test01_ExactEntity_Produces_StrongEntityEvidence_And_OccurrenceProtection()
    {
        // Setup: Box berisi frasa nama entitas canonical lengkap "BANK SUMSEL BABEL"
        var box = new OcrBoxModel
        {
            Id = 1,
            Text = "BANK SUMSEL BABEL",
            RecConfidence = 0.95f,
            DetScore = 0.95f
        };

        var results = _engine.ProcessBoxes(new[] { box });

        Assert.Equal(3, results.Count);
        foreach (var r in results)
        {
            _output.WriteLine($"T01: Token='{r.RawToken}', Action={r.Action}, State={r.State}, Evidence={r.Evidence}");
            Assert.Equal(PipelineAction.SAFE_TO_SKIP, r.Action);
            Assert.Equal(KnowledgeState.KNOWN_REFERENCE, r.State);
            Assert.True((r.Evidence & EvidenceTypes.ENTITY_EXACT_MATCH) != 0, $"Expected ENTITY_EXACT_MATCH on {r.RawToken}");
        }
    }

    [Fact]
    public void Test02_AmbiguousAlias_DoesNotAutoProtect()
    {
        // Setup: Entitas dengan alias ambigu "XX" (panjang 2)
        var customEngine = new EvidenceEngine();
        customEngine.LoadBaseLexicon(new[] { "CONTOH", "KATA", "BAKU" });
        customEngine.LoadEntityReferences(new[]
        {
            new EntityReferenceItem
            {
                Id = "test-operator",
                Name = "TEST OPERATOR",
                Category = "MobileOperator",
                AmbiguousAliases = new List<string> { "XX" },
                Active = true
            }
        });

        var box = new OcrBoxModel
        {
            Id = 2,
            Text = "XX",
            RecConfidence = 0.92f,
            DetScore = 0.92f
        };

        var results = customEngine.ProcessBoxes(new[] { box });

        Assert.Single(results);
        var r = results[0];
        _output.WriteLine($"T02: Token='{r.RawToken}', Action={r.Action}, State={r.State}, Evidence={r.Evidence}");

        // Verifikasi: "XX" memperoleh flag ENTITY_ALIAS_MATCH (evidence only),
        // tetapi TIDAK otomatis dilindungi sebagai SAFE_TO_SKIP via KNOWN_REFERENCE!
        Assert.True((r.Evidence & EvidenceTypes.ENTITY_ALIAS_MATCH) != 0, "Harus memperoleh evidence ENTITY_ALIAS_MATCH");
        Assert.NotEqual(KnowledgeState.KNOWN_REFERENCE, r.State);
    }

    [Fact]
    public void Test03_NearEntity_PassesThrough_ExistingCandidateGate()
    {
        // Setup engine khusus dengan entitas "INDOMARET"
        var customEngine = new EvidenceEngine();
        customEngine.InitializeDefault();
        customEngine.LoadEntityReferences(new[]
        {
            new EntityReferenceItem
            {
                Id = "cul-indomaret",
                Name = "INDOMARET",
                Category = "Culinary",
                Active = true
            }
        });

        // Kasus A: Token near-match yang MEMENUHI gate candidate existing
        // Token "INDOMRT" (panjang 7, initial 'I' sama dengan 'INDOMARET', distance 2: hilang 'A' dan 'E')
        // Diberi tetangga valid agar memenuhi konvergensi bukti
        var boxValid = new OcrBoxModel
        {
            Id = 3,
            Text = "TOKO INDOMRT",
            RecConfidence = 0.88f,
            DetScore = 0.90f
        };
        var resValid = customEngine.ProcessBoxes(new[] { boxValid });
        var targetTok = resValid.FirstOrDefault(t => t.RawToken.Equals("INDOMRT", StringComparison.OrdinalIgnoreCase));

        Assert.NotNull(targetTok);
        _output.WriteLine($"T03 Valid: Token='{targetTok.RawToken}', Action={targetTok.Action}, Corrections={string.Join(", ", targetTok.PossibleCorrections.Select(c => c.Word))}");
        Assert.Equal(PipelineAction.SUSPECT, targetTok.Action);
        Assert.Contains(targetTok.PossibleCorrections, c => c.Word == "INDOMARET");

        // Kasus B: Token near-match yang MELANGGAR gate candidate existing (huruf awal tidak sama)
        // Untuk distance 2: "XNDOMRT" (jarak 2, huruf awal berbeda 'X' vs 'I')
        // Gate distance 2 mensyaratkan validMediumMatches dengan c.Word[0] == norm[0].
        var boxInvalid = new OcrBoxModel
        {
            Id = 4,
            Text = "XNDOMRT",
            RecConfidence = 0.88f,
            DetScore = 0.90f
        };
        var resInvalid = customEngine.ProcessBoxes(new[] { boxInvalid });
        var invalidTok = resInvalid.FirstOrDefault(t => t.RawToken.Equals("XNDOMRT", StringComparison.OrdinalIgnoreCase));

        Assert.NotNull(invalidTok);
        _output.WriteLine($"T03 Invalid: Token='{invalidTok.RawToken}', Action={invalidTok.Action}");
        // Karena huruf awal berbeda pada distance 2, tidak lolos gate medium -> PASSIVE
        Assert.Equal(PipelineAction.PASSIVE, invalidTok.Action);
    }

    [Fact]
    public void Test04_ConstituentIsolation_BABEL_Alone_DoesNotGet_GlobalSafe()
    {
        // Setup: Box berisi hanya "BABEL" tanpa "BANK SUMSEL"
        var box = new OcrBoxModel
        {
            Id = 5,
            Text = "BABEL",
            RecConfidence = 0.95f,
            DetScore = 0.95f
        };

        var results = _engine.ProcessBoxes(new[] { box });

        Assert.Single(results);
        var r = results[0];
        _output.WriteLine($"T04: Token='{r.RawToken}', Action={r.Action}, State={r.State}, Evidence={r.Evidence}");

        // Verifikasi Occurrence-Specific Protection:
        // "BABEL" yang berdiri sendiri TIDAK memperoleh status KNOWN_REFERENCE atau SAFE_TO_SKIP karena entitas
        Assert.NotEqual(KnowledgeState.KNOWN_REFERENCE, r.State);
        Assert.False((r.Evidence & EvidenceTypes.ENTITY_EXACT_MATCH) != 0, "BABEL sendiri tidak boleh ENTITY_EXACT_MATCH");
    }

    [Fact]
    public void Test05_Alias_Unambiguous_Produces_EntityAliasMatch()
    {
        // Setup: Box berisi alias tidak ambigu "BCA" (alias dari "BANK BCA")
        var box = new OcrBoxModel
        {
            Id = 6,
            Text = "BCA",
            RecConfidence = 0.95f,
            DetScore = 0.95f
        };

        var results = _engine.ProcessBoxes(new[] { box });

        Assert.Single(results);
        var r = results[0];
        _output.WriteLine($"T05: Token='{r.RawToken}', Action={r.Action}, State={r.State}, Evidence={r.Evidence}");

        Assert.Equal(PipelineAction.SAFE_TO_SKIP, r.Action);
        Assert.Equal(KnowledgeState.KNOWN_REFERENCE, r.State);
        Assert.True((r.Evidence & EvidenceTypes.ENTITY_ALIAS_MATCH) != 0, "Harus memperoleh ENTITY_ALIAS_MATCH");
    }

    [Fact]
    public void Test06_CategoryNeutrality_DoesNotBypass_Typo()
    {
        // Setup: Token typo nyata pada konteks perbankan atau kuliner tidak diabaikan hanya karena letaknya dekat entitas
        // "BNK" (typo dari "BANK", distance 1)
        var box = new OcrBoxModel
        {
            Id = 7,
            Text = "BNK MANDIRI",
            RecConfidence = 0.90f,
            DetScore = 0.90f
        };

        var results = _engine.ProcessBoxes(new[] { box });
        var bnkToken = results.FirstOrDefault(t => t.RawToken.Equals("BNK", StringComparison.OrdinalIgnoreCase));
        var mandiriToken = results.FirstOrDefault(t => t.RawToken.Equals("MANDIRI", StringComparison.OrdinalIgnoreCase));

        Assert.NotNull(bnkToken);
        Assert.NotNull(mandiriToken);

        _output.WriteLine($"T06 BNK: Action={bnkToken.Action}, Mandiri: Action={mandiriToken.Action}");
        // "MANDIRI" terlindungi oleh entity alias match
        Assert.Equal(PipelineAction.SAFE_TO_SKIP, mandiriToken.Action);
        // "BNK" tidak dibypass dan dievaluasi secara generik
        Assert.NotEqual(KnowledgeState.KNOWN_REFERENCE, bnkToken.State);
    }

    [Fact]
    public void Test07_CandidateRanking_MathematicalDominance_Guard()
    {
        // Verifikasi guard formal:
        // Jika |PrimaryScore_A - PrimaryScore_B| > 0.01, kandidat dengan PrimaryScore lebih tinggi
        // MUTLAK menempati urutan teratas, bahkan jika kandidat lain memiliki EntityRelevance maksimal (1.0).
        var candNonEntity = ("KATA_A", 1, new CandidateScoreResult
        {
            PrimaryScore = 0.820,
            FinalScore = 0.820,
            EntityRelevance = 0.0
        });

        var candEntity = ("KATA_B", 2, new CandidateScoreResult
        {
            PrimaryScore = 0.780, // Selisih delta = 0.040 > 0.01
            FinalScore = 0.799,
            EntityRelevance = 1.0
        });

        int comp = EvidenceEngine.CompareCandidates(candNonEntity, candEntity);
        _output.WriteLine($"T07: Delta={candNonEntity.Item3.PrimaryScore - candEntity.Item3.PrimaryScore:F3}, Comp={comp}");

        // comp < 0 berarti candNonEntity muncul sebelum candEntity (rank #1)
        Assert.True(comp < 0, "Kandidat non-entity dengan PrimaryScore lebih tinggi > 0.01 harus tetap ranking #1");
    }

    [Fact]
    public void Test08_Frequency_ExplicitComparator_DoesNotInvert_When_DeltaPrimary_GreaterThan_0_01()
    {
        // Kasus 1: Delta Primary > 0.01 (misal 0.015).
        // Candidate A: Primary = 0.720, Frequency = 0.000 -> Final = 0.720
        // Candidate B: Primary = 0.705, Frequency = 1.000 -> Final = 0.705 + 0.019 = 0.724
        // Meskipun FinalScore B (0.724) > FinalScore A (0.720), comparator PrimaryScore
        // TIDAK BOLEH membalikkan urutan karena Delta Primary > 0.01!
        var candA = ("KANDIDAT_A", 1, new CandidateScoreResult
        {
            PrimaryScore = 0.720,
            FinalScore = 0.720
        });

        var candB = ("KANDIDAT_B", 1, new CandidateScoreResult
        {
            PrimaryScore = 0.705,
            FinalScore = 0.724
        });

        int comp1 = EvidenceEngine.CompareCandidates(candA, candB);
        _output.WriteLine($"T08 Kasus 1: Comp={comp1} (Expected < 0)");
        Assert.True(comp1 < 0, "Frequency tidak boleh membalikkan urutan saat Delta Primary > 0.01");

        // Kasus 2: Delta Primary <= 0.01 (misal 0.005).
        // Candidate C: Primary = 0.720, Frequency = 0.000 -> Final = 0.720
        // Candidate D: Primary = 0.715, Frequency = 1.000 -> Final = 0.715 + 0.019 = 0.734
        // Karena Delta Primary <= 0.01, Frequency bertindak sebagai tie-breaker sah -> Candidate D unggul!
        var candC = ("KANDIDAT_C", 1, new CandidateScoreResult
        {
            PrimaryScore = 0.720,
            FinalScore = 0.720
        });

        var candD = ("KANDIDAT_D", 1, new CandidateScoreResult
        {
            PrimaryScore = 0.715,
            FinalScore = 0.734
        });

        int comp2 = EvidenceEngine.CompareCandidates(candC, candD);
        _output.WriteLine($"T08 Kasus 2: Comp={comp2} (Expected > 0)");
        Assert.True(comp2 > 0, "Frequency sah sebagai tie-breaker saat Delta Primary <= 0.01");
    }

    [Fact]
    public void Test09_ExistingRegression_Remains_Intact()
    {
        // 1. "Hargga" -> SUSPECT
        var rHargga = _engine.EvaluateToken("Hargga", new List<int> { 10 }, 0.90f);
        _output.WriteLine($"Hargga: Action={rHargga.Action}, Top={rHargga.PossibleCorrections.FirstOrDefault()?.Word}");
        Assert.Equal(PipelineAction.SUSPECT, rHargga.Action);
        Assert.Contains(rHargga.PossibleCorrections, c => c.Word == "HARGA");

        // 2. "Berkulitas" -> SUSPECT
        var rBerkulitas = _engine.EvaluateToken("Berkulitas", new List<int> { 11 }, 0.90f);
        _output.WriteLine($"Berkulitas: Action={rBerkulitas.Action}, Top={rBerkulitas.PossibleCorrections.FirstOrDefault()?.Word}");
        Assert.Equal(PipelineAction.SUSPECT, rBerkulitas.Action);
        Assert.Contains(rBerkulitas.PossibleCorrections, c => c.Word == "BERKUALITAS");

        // 3. "Miyag" with neighbor "Goreng" -> SUSPECT
        var rMiyag = _engine.EvaluateToken("Miyag", new List<int> { 12 }, 0.90f, rightNeighbor: "GORENG", hasValidNeighborContext: true);
        _output.WriteLine($"Miyag: Action={rMiyag.Action}, Top={rMiyag.PossibleCorrections.FirstOrDefault()?.Word}");
        Assert.Equal(PipelineAction.SUSPECT, rMiyag.Action);
        Assert.Contains(rMiyag.PossibleCorrections, c => c.Word == "MINYAK");

        // 4. "Botan" with neighbor "Minyak" -> SUSPECT
        var rBotan = _engine.EvaluateToken("Botan", new List<int> { 13 }, 0.90f, leftNeighbor: "MINYAK", hasValidNeighborContext: true);
        _output.WriteLine($"Botan: Action={rBotan.Action}, Top={rBotan.PossibleCorrections.FirstOrDefault()?.Word}");
        Assert.Equal(PipelineAction.SUSPECT, rBotan.Action);
        Assert.Contains(rBotan.PossibleCorrections, c => c.Word == "BOTOL");

        // Muat referensi terstruktur (lokasi/wilayah) secara deklaratif
        _engine.LoadStructuredReferences(new[]
        {
            new StructuredReferencePhrase
            {
                Text = "SUKAMAJU",
                DatasetId = "wilayah"
            },
            new StructuredReferencePhrase
            {
                Text = "DESA TANJUNG BARU",
                DatasetId = "test-loc"
            }
        });

        // 5. "Sukamju" (near match on structured ref "Sukamaju") -> SUSPECT
        var rSukamju = _engine.EvaluateToken("Sukamju", new List<int> { 14 }, 0.90f);
        _output.WriteLine($"Sukamju: Action={rSukamju.Action}, Top={rSukamju.PossibleCorrections.FirstOrDefault()?.Word}");
        Assert.Equal(PipelineAction.SUSPECT, rSukamju.Action);
        Assert.Contains(rSukamju.PossibleCorrections, c => c.Word == "SUKAMAJU");

        // 6. "Tanjuung" in "DESA TANJUUNG BARU" -> SUSPECT via StructuredReference near match
        var boxTanjung = new OcrBoxModel
        {
            Id = 15,
            Text = "DESA TANJUUNG BARU",
            RecConfidence = 0.90f
        };
        var rTanjuung = _engine.ProcessBoxes(new[] { boxTanjung });
        var tanjuungTok = rTanjuung.FirstOrDefault(t => t.RawToken.Equals("TANJUUNG", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(tanjuungTok);
        _output.WriteLine($"Tanjuung: Action={tanjuungTok.Action}, Top={tanjuungTok.PossibleCorrections.FirstOrDefault()?.Word}");
        Assert.Equal(PipelineAction.SUSPECT, tanjuungTok.Action);
        Assert.Contains(tanjuungTok.PossibleCorrections, c => c.Word == "TANJUNG");

        // 7. "Lain-Lain" -> SAFE_TO_SKIP (Kata ulang bertanda hubung sah)
        var boxLain = new OcrBoxModel
        {
            Id = 16,
            Text = "Lain-Lain",
            RecConfidence = 0.90f
        };
        var rLain = _engine.ProcessBoxes(new[] { boxLain });
        _output.WriteLine($"Lain-Lain: Action={rLain[0].Action}, Notes={rLain[0].Notes}");
        Assert.Equal(PipelineAction.SAFE_TO_SKIP, rLain[0].Action);
    }
}
