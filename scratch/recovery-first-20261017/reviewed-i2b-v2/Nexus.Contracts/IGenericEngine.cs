using System;
using System.Threading.Tasks;

namespace Nexus.Contracts;

/// <summary>
/// Kontrak dasar generik untuk seluruh engine subsystem (Image, LLM, PDF).
/// Mengelola lifecycle engine dan pembersihan memori native/unmanaged.
/// </summary>
public interface IGenericEngine : IAsyncDisposable
{
    string EngineName { get; }
    bool IsLoaded { get; }

    /// <summary>
    /// Memuat engine / runtime fisik.
    /// </summary>
    Task LoadEngineAsync();

    /// <summary>
    /// Menghentikan engine dan membebaskan 100% memori native/unmanaged yang digunakan oleh model spesifik.
    /// </summary>
    Task UnloadModelAsync(string sessionKey);

    /// <summary>
    /// Menghentikan engine dan membebaskan seluruh session yang aktif di RAM/VRAM.
    /// </summary>
    Task UnloadAllModelsAsync();

    /// <summary>
    /// Mengambil metrik snapshot penggunaan memori nyata pada engine saat ini.
    /// </summary>
    EngineMemoryMetrics GetMemoryMetrics();
}
