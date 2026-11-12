namespace Nexus.Contracts;

/// <summary>
/// Penyedia eksekusi perangkat keras untuk Generic Engine.
/// </summary>
public enum ExecutionProvider
{
    Cpu,
    Cuda,
    DirectMl
}
