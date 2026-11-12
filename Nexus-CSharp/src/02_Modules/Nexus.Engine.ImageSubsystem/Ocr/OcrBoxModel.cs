using System;

namespace Nexus.Engine.ImageSubsystem.Ocr;

/// <summary>
/// Model data untuk satu kotak teks hasil deteksi dan rekognisi PP-OCRv6.
/// Memuat koordinat piksel native, skor deteksi DBNet, skor confidence CTC, dan teks hasil ekstraksi.
/// </summary>
public sealed class OcrBoxModel
{
    /// <summary>
    /// Nomor urut box (1-indexed berdasarkan urutan baca: atas ke bawah, kiri ke kanan).
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Koordinat X sisi kiri (piksel gambar asli).
    /// </summary>
    public int X1 { get; set; }

    /// <summary>
    /// Koordinat Y sisi atas (piksel gambar asli).
    /// </summary>
    public int Y1 { get; set; }

    /// <summary>
    /// Koordinat X sisi kanan (piksel gambar asli).
    /// </summary>
    public int X2 { get; set; }

    /// <summary>
    /// Koordinat Y sisi bawah (piksel gambar asli).
    /// </summary>
    public int Y2 { get; set; }

    /// <summary>
    /// Skor probabilitas deteksi rata-rata DBNet pada area teks [0.0 - 1.0].
    /// </summary>
    public float DetScore { get; set; }

    /// <summary>
    /// Skor keyakinan rata-rata hasil CTC greedy decoding SVTR [0.0 - 1.0].
    /// </summary>
    public float RecConfidence { get; set; }

    /// <summary>
    /// Teks hasil ekstraksi pengenalan karakter.
    /// </summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// Waktu inferensi rekognisi untuk box ini (milidetik).
    /// </summary>
    public long RecMs { get; set; }

    /// <summary>
    /// Lebar kotak teks dalam piksel.
    /// </summary>
    public int Width => Math.Max(0, X2 - X1);

    /// <summary>
    /// Tinggi kotak teks dalam piksel.
    /// </summary>
    public int Height => Math.Max(0, Y2 - Y1);

    public override string ToString()
    {
        return $"[{Id:D2}] ({X1},{Y1})-({X2},{Y2}) [det:{DetScore:F2}, conf:{RecConfidence:F2}]: {Text}";
    }
}
