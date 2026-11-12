namespace Nexus.Contracts;

/// <summary>
/// DTO konfigurasi opsi inisialisasi session pada Generic Engine.
/// Didesain immutable (init-only) untuk menjamin keamanan antar-thread.
/// </summary>
public class EngineSessionOptions
{
    public int InterOpNumThreads { get; init; } = 1;
    public int IntraOpNumThreads { get; init; } = 4;
    public GraphOptimizationLevel GraphOptimization { get; init; } = GraphOptimizationLevel.EnableAll;
    public ExecutionProvider ExecutionProvider { get; init; } = ExecutionProvider.Cpu;
}
