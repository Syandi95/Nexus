using System.Collections.Generic;
using System.Threading.Tasks;

namespace Nexus.Contracts;

/// <summary>
/// Kontrak generik untuk Image Engine Subsystem (ONNX Runtime Execution).
/// Bersifat feature-agnostic (tidak terikat nama fitur UI seperti RemoveBG / Upscale).
/// </summary>
public interface IGenericImageEngine : IGenericEngine
{
    /// <summary>
    /// Memuat model yang telah di-resolve ke dalam memory session.
    /// Descriptor diperlakukan sebagai immutable dan tidak diubah oleh engine.
    /// Mengembalikan SessionId opaque yang hanya digunakan untuk eksekusi
    /// dan unload session; nilai ini bukan model path.
    /// </summary>
    Task<string> LoadModelAsync(
        ResolvedModelDescriptor descriptor,
        EngineSessionOptions? options = null);

    /// <summary>
    /// Menjalankan inferensi generik menggunakan session key yang terdaftar.
    /// </summary>
    Task<IReadOnlyDictionary<string, object>> ExecuteInferenceAsync(
        string sessionKey,
        IReadOnlyDictionary<string, object> inputs);
}
