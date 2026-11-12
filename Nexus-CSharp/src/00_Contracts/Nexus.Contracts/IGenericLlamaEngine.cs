using System.Threading.Tasks;

namespace Nexus.Contracts;

/// <summary>
/// Kontrak generik untuk LLM Engine Subsystem (llama-server.exe process management).
/// </summary>
public interface IGenericLlamaEngine : IGenericEngine
{
    string? ActiveModelPath { get; }
    int Port { get; }

    /// <summary>
    /// Memulai server LLM eksternal dengan model GGUF dan konteks yang ditentukan.
    /// </summary>
    Task<bool> StartServerAsync(string modelPath, int contextSize = 8192);

    /// <summary>
    /// Menghentikan server LLM eksternal dan membunuh prosesnya dari RAM/VRAM.
    /// </summary>
    Task StopServerAsync();
}
