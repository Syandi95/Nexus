using System.Collections.Generic;

namespace Nexus.Contracts.AiCheck;

/// <summary>
/// Status keputusan evaluasi AI Judge pre-print.
/// Mendukung 4 kategori deterministic Master Blueprint V3.0 dengan backward-compatibility.
/// </summary>
public enum AiCheckJudgmentStatus
{
    Aman = 0,             // VALID
    Koreksi = 1,          // TYPO
    PerluVerifikasi = 2,  // UNCERTAIN
    OcrError = 3,         // OCR_ERROR
    Unresolved = 4,       // UNRESOLVED (Gemma timeout / no response)

    // Aliases sesuai Master Blueprint V3.0
    Valid = Aman,
    Typo = Koreksi,
    Uncertain = PerluVerifikasi
}

/// <summary>
/// DTO untuk membawa temuan kandidat box/token dari Evidence Engine ke AI Judge.
/// Mendukung multi-BoxId untuk token yang ter-deduplikasi.
/// </summary>
public sealed class TriageCandidateDto
{
    private int _boxId;

    public int BoxId
    {
        get => _boxId;
        set
        {
            _boxId = value;
            if (BoxIds.Count == 0)
            {
                BoxIds.Add(value);
            }
            else if (!BoxIds.Contains(value))
            {
                BoxIds[0] = value;
            }
        }
    }

    public string CandidateId { get; set; } = string.Empty;
    public int TokenIndex { get; set; }

    /// <summary>
    /// Daftar BoxId yang memuat token ini (hasil deduplikasi multi-box).
    /// </summary>
    public List<int> BoxIds { get; set; } = new();

    public string OriginalText { get; set; } = string.Empty;
    public float DetScore { get; set; }
    public float RecConfidence { get; set; }
    public List<string> Reasons { get; set; } = new();
    public string? MatchedReference { get; set; }
    public int? LevenshteinDistance { get; set; }

    /// <summary>
    /// Daftar kemungkinan kata perbaikan yang diusulkan oleh Evidence Engine.
    /// </summary>
    public List<string> PossibleCorrections { get; set; } = new();

    /// <summary>
    /// Konteks baris spasial nyata dari bounding box.
    /// </summary>
    public string SpatialContext { get; set; } = string.Empty;

    /// <summary>
    /// Teks utuh dari bounding box OCR asli yang memuat kandidat ini.
    /// </summary>
    public string BoxText { get; set; } = string.Empty;

    /// <summary>
    /// Tindakan Evidence Engine (SUSPECT, SAFE_TO_SKIP, PASSIVE).
    /// </summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>
    /// Deskripsi ringkas bukti pendukung (soft evidence).
    /// </summary>
    public string EvidenceDescription { get; set; } = string.Empty;

    /// <summary>
    /// Menandakan apakah kandidat memiliki akumulasi bukti kuat dari Evidence Engine.
    /// </summary>
    public bool HasStrongEvidence { get; set; }

    /// <summary>
    /// Menandakan apakah kandidat merupakan near-match terhadap entitas referensi terstruktur resmi.
    /// </summary>
    public bool IsReferenceNearMatch { get; set; }

    /// <summary>
    /// Metadata kurir leksikal dan konteks dari EvidenceEngine (Pure Courier Pattern).
    /// </summary>
    public double PrimaryScore { get; set; }
    public double FuzzyMargin { get; set; }
    public bool HasFuzzyRunnerUp { get; set; }
    public double TrueFuzzyMargin { get; set; }
    public double G3LowMargin { get; set; }
    public string? Top1Suggestion { get; set; }
    public int? Top1Distance { get; set; }
    public string? MatchedReferenceContext { get; set; }
    public bool IsNameSignal { get; set; }
    public bool HasCharacterAnomaly { get; set; }
    public bool HasEntitySignal { get; set; }
    public bool IsStrongPersonContext { get; set; }
    public bool IsMixedCase { get; set; }
}

/// <summary>
/// Keputusan evaluasi pre-print dari AI Judge (Gemma 4 E4B) untuk satu kotak teks / token.
/// </summary>
public sealed class AiCheckJudgment
{
    public string CandidateId { get; set; } = string.Empty;
    public int TokenIndex { get; set; }
    public int BoxId { get; set; }
    public List<int> BoxIds { get; set; } = new();
    public string OriginalText { get; set; } = string.Empty;
    public string RecommendedText { get; set; } = string.Empty;
    public AiCheckJudgmentStatus Status { get; set; } = AiCheckJudgmentStatus.Aman;
    public string Reason { get; set; } = string.Empty;
    public long DurationMs { get; set; }

    public bool IsTypo => Status == AiCheckJudgmentStatus.Koreksi;
    public bool IsUncertain => Status == AiCheckJudgmentStatus.PerluVerifikasi;
    public bool IsValid => Status == AiCheckJudgmentStatus.Aman;
    public bool IsOcrError => Status == AiCheckJudgmentStatus.OcrError;
    public bool IsUnresolved => Status == AiCheckJudgmentStatus.Unresolved;

    public override string ToString()
    {
        string ids = BoxIds.Count > 1 ? $"BOX [{string.Join(",", BoxIds)}]" : $"BOX {BoxId:D2}";
        return $"{ids} ({CandidateId}): {OriginalText} -> {RecommendedText} | STATUS: {Status} | ALASAN: {Reason}";
    }
}
