using System;
using System.Collections.Generic;
using System.Linq;

namespace Nexus.Engine.ImageSubsystem.Evidence;

/// <summary>
/// Status pengetahuan leksikal dari kata/token yang diperiksa.
/// Dipisahkan secara tegas dari PipelineAction.
/// </summary>
public enum KnowledgeState
{
    /// <summary>
    /// Kata tidak ditemukan di kamus umum maupun referensi resmi (out-of-vocabulary).
    /// UNKNOWN bukan berarti otomatis SUSPECT.
    /// </summary>
    UNKNOWN,

    /// <summary>
    /// Kata ditemukan secara persis di dalam kamus bahasa Indonesia (20K Leipzig/KBBI) atau User Dictionary.
    /// </summary>
    KNOWN_DICT,

    /// <summary>
    /// Kata ditemukan di dalam User Dictionary.
    /// </summary>
    KNOWN_USER_DICT,

    /// <summary>
    /// Kata/frasa terdaftar sebagai entitas referensi terstruktur resmi (Desa, Kecamatan, Puskesmas, Instansi, dll.).
    /// </summary>
    KNOWN_REFERENCE,

    // Backward compatibility
    KNOWN_LOKASI = KNOWN_REFERENCE
}

/// <summary>
/// Tindakan routing yang ditentukan oleh Evidence Engine (Radar).
/// Hanya SUSPECT yang akan dikirim ke Gemma 4 E4B.
/// </summary>
public enum PipelineAction
{
    /// <summary>
    /// Token aman/valid tanpa anomali yang perlu diperiksa AI Judge.
    /// SAFE_TO_SKIP bukan berarti VALID final semantik, melainkan tidak memenuhi kriteria anomali.
    /// </summary>
    SAFE_TO_SKIP,

    /// <summary>
    /// Token out-of-vocabulary tetapi tidak memiliki bukti anomali yang cukup (misal nama wajar, akronim wajar).
    /// Disimpan secara pasif tanpa membebani LLM.
    /// </summary>
    PASSIVE,

    /// <summary>
    /// Memiliki bukti kuat/sedang anomali typo, repetisi, atau distorsi OCR.
    /// Wajib dievaluasi oleh Gemma 4 E4B.
    /// </summary>
    SUSPECT
}

/// <summary>
/// Bitwise flags jenis bukti pendukung (soft evidence) yang dikumpulkan oleh Evidence Engine.
/// </summary>
[Flags]
public enum EvidenceTypes
{
    None = 0,
    DICT_ID_MATCH = 1 << 0,
    DICT_EN_MATCH = 1 << 1,
    REFERENCE_MATCH = 1 << 2,
    LOKASI_MATCH = REFERENCE_MATCH, // Alias backward-compatibility
    FUZZY_STRONG = 1 << 3,          // distance 1
    FUZZY_MEDIUM = 1 << 4,          // distance 2
    FUZZY_WEAK = 1 << 5,            // distance 3
    REPETITION_ANOMALY = 1 << 6,
    LOW_OCR_CONFIDENCE = 1 << 7,
    HIGH_OCR_CONFIDENCE = 1 << 8,
    WEAK_NAME_SIGNAL = 1 << 9,
    SPATIAL_ANOMALY = 1 << 10,
    REFERENCE_NEAR_MATCH = 1 << 11, // Near-match pada entitas referensi terstruktur
    ACRONYM_SIGNAL = 1 << 12,       // Pola akronim/singkatan (noise reduction evidence)
    CHARACTER_ANOMALY = 1 << 13,    // Anomali karakter (vokal/konsonan tak lazim, inversi)
    USER_DICT_MATCH = 1 << 14,      // Cocok dengan kamus pengguna (User Dictionary)
    ENTITY_EXACT_MATCH = 1 << 15,   // Exact match pada entitas referensi resmi
    ENTITY_ALIAS_MATCH = 1 << 16,   // Alias match pada entitas referensi resmi
    ENTITY_NEAR_MATCH = 1 << 17,    // Near-match pada entitas referensi resmi (variasi typo entitas)
    IRREGULAR_CASING = 1 << 18,     // Mixed-case abnormal pada token non-kamus (khusus G3)
    STRONG_PERSON_CONTEXT = 1 << 19 // Konteks gelar/peran orang kuat (khusus proteksi R3D)
}

/// <summary>
/// Representasi entitas referensi terstruktur multi-kata dengan metadata kategori.
/// </summary>
public sealed class StructuredReferencePhrase
{
    public string Text { get; set; } = string.Empty;
    public string NormalizedText { get; set; } = string.Empty;
    public string DatasetId { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string[] Tokens { get; set; } = Array.Empty<string>();
}

/// <summary>
/// Representasi kandidat koreksi fuzzy leksikal dengan skor bukti multi-dimensi.
/// </summary>
public sealed class FuzzyCandidate
{
    public required string Word { get; init; }
    public required int Distance { get; init; }
    public required string Grade { get; init; } // STRONG, MEDIUM, WEAK
    public double PrimaryScore { get; init; }
    public double FinalScore { get; init; }
    public double EntityRelevance { get; init; }
}

/// <summary>
/// Hasil evaluasi lengkap dari Evidence Engine untuk satu token instance.
/// </summary>
public sealed class EvidenceEvaluationResult
{
    // Identifikasi & Lokasi
    public string CandidateId { get; set; } = string.Empty;
    public int TokenIndex { get; set; }
    public required string RawToken { get; init; }
    public List<int> BoxIds { get; init; } = new();
    public int PrimaryBoxId => BoxIds[0]; // Invariant: Kandidat SUSPECT wajib memiliki minimal 1 BoxId (tidak ada fallback 0)
    public string BoxText { get; set; } = string.Empty; // Sourced strictly from OcrBoxModel.Text
    public string SpatialContext { get; set; } = string.Empty;
    public float MinRecConfidence { get; set; }          // Sourced strictly from OcrBoxModel.RecConfidence
    public float DetScore { get; set; }                  // Sourced strictly from OcrBoxModel.DetScore
    public string Notes { get; set; } = string.Empty;

    // Status Pipeline
    public KnowledgeState State { get; set; } = KnowledgeState.UNKNOWN;
    public PipelineAction Action { get; set; } = PipelineAction.PASSIVE;
    public string ActionName => Action.ToString();
    public EvidenceTypes Evidence { get; set; } = EvidenceTypes.None;

    // Koreksi Leksikal & Proyeksi Kanonikal (Tanpa default buatan)
    public List<FuzzyCandidate> PossibleCorrections { get; init; } = new();
    public List<string> PossibleCorrectionWords => PossibleCorrections.Select(c => c.Word).ToList();
    public double Top1PrimaryScore { get; set; }
    public string? Top1Suggestion { get; set; }
    public int? Top1Distance { get; set; }               // Nullable: direct assignment dari top1.Distance bila ada, null bila tidak ada
    public int? LevenshteinDistance => Top1Distance;     // Tidak ada fallback 0 atau sentinel buatan
    public string? MatchedReferenceContext { get; set; }

    // Margin Semantics: Legacy 100% Untouched & No Redirect
    public double FuzzyMargin { get; set; }     // Existing legacy calculation preserved exactly
    public bool HasFuzzyRunnerUp { get; set; }  // PossibleCorrections.Count > 1
    public double TrueFuzzyMargin { get; set; } // Signed difference Top1 - Top2 (0.0 for N=1)
    public double G3LowMargin { get; set; }     // Equal to legacy FuzzyMargin

    // Bukti Pendukung untuk Courier Murni (Formula identik dengan logika lama ViewModel)
    public bool HasStrongEvidence { get; set; }
    public bool IsReferenceNearMatch => (Evidence & EvidenceTypes.REFERENCE_NEAR_MATCH) != 0;

    // Context & Casing Flags
    public bool IsNameSignal => (Evidence & EvidenceTypes.WEAK_NAME_SIGNAL) != 0;
    public bool HasCharacterAnomaly => (Evidence & EvidenceTypes.CHARACTER_ANOMALY) != 0;
    public bool HasEntitySignal => (Evidence & (EvidenceTypes.ENTITY_NEAR_MATCH | EvidenceTypes.ENTITY_EXACT_MATCH | 
                                                EvidenceTypes.REFERENCE_NEAR_MATCH | EvidenceTypes.REFERENCE_MATCH)) != 0;
    public bool IsStrongPersonContext => (Evidence & EvidenceTypes.STRONG_PERSON_CONTEXT) != 0;
    public bool IsMixedCase => (Evidence & EvidenceTypes.IRREGULAR_CASING) != 0;
}
