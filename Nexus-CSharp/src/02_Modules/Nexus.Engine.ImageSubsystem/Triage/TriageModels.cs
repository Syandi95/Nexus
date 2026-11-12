using System;
using System.Collections.Generic;
using Nexus.Engine.ImageSubsystem.Ocr;

namespace Nexus.Engine.ImageSubsystem.Triage;

[Flags]
public enum TriageLayerFlags
{
    None = 0,
    Layer1_LowDetScore = 1 << 0,
    Layer1_LowRecConfidence = 1 << 1,
    Layer2A_SuspiciousPrefix = 1 << 2,
    Layer2B_IrregularCasing = 1 << 3,
    Layer2C_DigitInWord = 1 << 4,
    Layer3_ReferenceMismatch = 1 << 5
}

/// <summary>
/// Hasil evaluasi penyaringan Triage untuk satu OcrBox.
/// </summary>
public sealed class TriageFinding
{
    public OcrBoxModel Box { get; init; } = null!;
    public TriageLayerFlags LayerFlags { get; set; } = TriageLayerFlags.None;
    public List<string> Reasons { get; } = new();

    // Field khusus Layer 3 (Reference Match)
    public string? MatchedWindow { get; set; }
    public string? MatchedReferenceEntity { get; set; }
    public int LevenshteinDistance { get; set; }
    public float SimilarityScore { get; set; }

    public bool IsFlagged => LayerFlags != TriageLayerFlags.None;

    public override string ToString()
    {
        return $"[Box {Box.Id:D2}] Flags: {LayerFlags} | Reasons: {string.Join("; ", Reasons)}";
    }
}
