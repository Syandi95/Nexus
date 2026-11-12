using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Nexus.Contracts.AiCheck;

/// <summary>
/// Kontrak orkestrasi AI Judge (Gemma 4 E4B) untuk mengevaluasi kotak teks yang ter-flag oleh Three-Tier Triage.
/// </summary>
public interface IAiCheckJudgeService
{
    /// <summary>
    /// Mengevaluasi sekumpulan kandidat box bermasalah menggunakan prompt ringkas terstruktur ke LLM.
    /// </summary>
    Task<IReadOnlyList<AiCheckJudgment>> EvaluateFindingsAsync(
        string documentContext,
        IReadOnlyList<TriageCandidateDto> candidates,
        CancellationToken ct = default);

    /// <summary>
    /// Mengevaluasi satu kandidat box secara progresif dan non-blocking.
    /// </summary>
    Task<AiCheckJudgment> EvaluateSingleCandidateAsync(
        string documentContext,
        TriageCandidateDto candidate,
        CancellationToken ct = default);
}
