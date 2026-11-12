using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Nexus.Engine.ImageSubsystem.Services;

/// <summary>
/// Mewakili sesi embedding fitur gambar yang dihitung satu kali oleh MobileSAM Image Encoder.
/// </summary>
public sealed class MobileSamEmbeddingSession : IDisposable
{
    public Tensor<float> Embeddings { get; }
    public int OriginalWidth { get; }
    public int OriginalHeight { get; }
    public float Scale { get; }

    public MobileSamEmbeddingSession(Tensor<float> embeddings, int origW, int origH, float scale)
    {
        Embeddings = embeddings ?? throw new ArgumentNullException(nameof(embeddings));
        OriginalWidth = origW;
        OriginalHeight = origH;
        Scale = scale;
    }

    public void Dispose()
    {
        // Managed Tensor<float>, di-clear referensinya agar segera di-garbage collect
    }
}

/// <summary>
/// Layanan segmentasi objek interaktif berbasis MobileSAM (Encoder + Decoder).
/// </summary>
public interface IMobileSamService
{
    /// <summary>
    /// Menandakan apakah file model MobileSAM (Encoder & Decoder) tersedia di sistem.
    /// </summary>
    bool IsModelAvailable { get; }

    /// <summary>
    /// Mengekstrak embedding visual 1024-space dari gambar input menggunakan MobileSAM Image Encoder.
    /// Dijalankan di background thread (sekali per foto).
    /// </summary>
    Task<MobileSamEmbeddingSession> EncodeImageAsync(
        Bitmap image,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Menghasilkan mask biner pada resolusi asli gambar berdasarkan titik klik pengguna.
    /// Dijalankan secara instan (~46ms) per klik.
    /// </summary>
    Task<Bitmap> SegmentPointAsync(
        MobileSamEmbeddingSession session,
        PointF pointInOriginalImage,
        CancellationToken cancellationToken = default);
}
