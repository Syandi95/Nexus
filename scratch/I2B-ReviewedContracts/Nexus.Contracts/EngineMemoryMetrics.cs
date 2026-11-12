using System;

namespace Nexus.Contracts;

/// <summary>
/// DTO metrik pemantauan konsumsi memori nyata pada Generic Engine.
/// Didesain immutable (init-only) untuk snapshot pencatatan metrik.
/// </summary>
public class EngineMemoryMetrics
{
    public double WorkingSetMB { get; init; }
    public double PrivateBytesMB { get; init; }
    public double PeakPrivateBytesMB { get; init; }
    public double CommitSizeMB { get; init; }
    public int ActiveSessionsCount { get; init; }
    public DateTime RecordedAtUtc { get; init; } = DateTime.UtcNow;
}
