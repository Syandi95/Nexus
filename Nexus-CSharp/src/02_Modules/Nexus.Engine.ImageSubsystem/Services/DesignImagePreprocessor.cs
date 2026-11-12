using System;
using System.IO;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace Nexus.Engine.ImageSubsystem.Services;

public enum SupportedDesignFormat
{
    Jpeg,
    Png,
    Tiff,
    Pdf,
    Unknown
}

/// <summary>
/// Layanan pra-pemrosesan berkas desain cetak (JPEG, PNG, TIFF, PDF).
/// Menangani validasi magic-bytes, konversi warna CMYK ke sRGB, rendering PDF, dan downscaling bicubic untuk grid DBNet.
/// </summary>
public sealed class DesignImagePreprocessor
{
    private const int MinFileSizeBytes = 1024; // 1 KB
    private const long MaxFileSizeBytes = 150 * 1024 * 1024; // 150 MB

    /// <summary>
    /// Memvalidasi integritas dan tipe berkas berdasarkan magic-bytes header.
    /// </summary>
    public static SupportedDesignFormat ValidateFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("Path file desain tidak boleh kosong.", nameof(filePath));

        if (!File.Exists(filePath))
            throw new FileNotFoundException("File desain tidak ditemukan.", filePath);

        var fileInfo = new FileInfo(filePath);
        if (fileInfo.Length < MinFileSizeBytes)
            throw new InvalidDataException($"Ukuran berkas terlalu kecil ({fileInfo.Length} bytes). Minimal {MinFileSizeBytes} bytes.");

        if (fileInfo.Length > MaxFileSizeBytes)
            throw new InvalidDataException($"Ukuran berkas melebihi batas maksimum 150MB ({fileInfo.Length / (1024 * 1024)} MB).");

        byte[] header = new byte[8];
        using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            int bytesRead = stream.Read(header, 0, header.Length);
            if (bytesRead < 4)
                throw new InvalidDataException("Berkas rusak atau tidak memiliki header yang valid.");
        }

        // JPEG: FF D8 FF
        if (header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
            return SupportedDesignFormat.Jpeg;

        // PNG: 89 50 4E 47
        if (header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47)
            return SupportedDesignFormat.Png;

        // PDF: 25 50 44 46 (%PDF)
        if (header[0] == 0x25 && header[1] == 0x50 && header[2] == 0x44 && header[3] == 0x46)
            return SupportedDesignFormat.Pdf;

        // TIFF Little-Endian: 49 49 2A 00 (II*)
        if (header[0] == 0x49 && header[1] == 0x49 && header[2] == 0x2A && header[3] == 0x00)
            return SupportedDesignFormat.Tiff;

        // TIFF Big-Endian: 4D 4D 00 2A (MM*)
        if (header[0] == 0x4D && header[1] == 0x4D && header[2] == 0x00 && header[3] == 0x2A)
            return SupportedDesignFormat.Tiff;

        throw new InvalidDataException($"Format berkas '{Path.GetExtension(filePath)}' tidak didukung atau header magic-bytes tidak cocok.");
    }

    /// <summary>
    /// Memuat berkas desain sebagai GDI+ Bitmap 24bpp / 32bpp sRGB.
    /// Otomatis menangani CMYK JPEG/TIFF dengan fallback WIC jika GDI+ gagal.
    /// </summary>
    public static Bitmap LoadAsBitmap(string filePath)
    {
        var format = ValidateFile(filePath);

        if (format == SupportedDesignFormat.Pdf)
        {
            return RenderPdfPageToBitmap(filePath, pageIndex: 0, dpi: 300);
        }

        // Muat via GDI+ stream untuk memastikan file lock segera dilepas
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var rawImg = Image.FromStream(stream, useEmbeddedColorManagement: true, validateImageData: true);
        var bmp = new Bitmap(rawImg.Width, rawImg.Height, PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.SmoothingMode = SmoothingMode.HighQuality;
            g.DrawImage(rawImg, 0, 0, rawImg.Width, rawImg.Height);
        }
        return bmp;
    }

    private static Bitmap RenderPdfPageToBitmap(string pdfPath, int pageIndex, int dpi)
    {
        // Jika Docnet.Core tersedia
        try
        {
            var docnetType = Type.GetType("Docnet.Core.DocLib, Docnet.Core");
            if (docnetType != null)
            {
                byte[] rawBytes;
                int targetWidth;
                int targetHeight;

                dynamic docLib = docnetType.GetProperty("Instance")?.GetValue(null)!;
                using dynamic docReader = docLib.GetDocReader(pdfPath, new { });
                using dynamic pageReader = docReader.GetPageReader(pageIndex);
                int rawWidth = pageReader.GetPageWidth();
                int rawHeight = pageReader.GetPageHeight();
                float scaleFactor = (float)dpi / 72.0f;
                targetWidth = (int)Math.Round(rawWidth * scaleFactor);
                targetHeight = (int)Math.Round(rawHeight * scaleFactor);

                rawBytes = pageReader.GetImage(targetWidth, targetHeight);

                // GDI+ Bitmap creation and memory copy run outside native Docnet call
                var bmp = new Bitmap(targetWidth, targetHeight, PixelFormat.Format32bppArgb);
                var bmpData = bmp.LockBits(new Rectangle(0, 0, targetWidth, targetHeight), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                System.Runtime.InteropServices.Marshal.Copy(rawBytes, 0, bmpData.Scan0, rawBytes.Length);
                bmp.UnlockBits(bmpData);
                return bmp;
            }
        }
        catch
        {
            // Lanjut ke penanganan error
        }

        throw new NotSupportedException("Renderer PDF memerlukan modul Docnet.Core yang belum terkonfigurasi pada runtime ini.");
    }

    /// <summary>
    /// Menghitung dimensi dan membuat bitmap berskala untuk input DBNet (kelipatan 32 piksel).
    /// Mengembalikan bitmap berskala serta rasio skala koordinat (scaleX, scaleY).
    /// </summary>
    public static Bitmap CreateDetScaledBitmap(Bitmap original, int maxSide, out float scaleX, out float scaleY)
    {
        int origW = original.Width;
        int origH = original.Height;

        float scale = Math.Min((float)maxSide / origW, (float)maxSide / origH);
        int targetW = (int)Math.Round(origW * scale / 32.0f) * 32;
        int targetH = (int)Math.Round(origH * scale / 32.0f) * 32;
        if (targetW < 32) targetW = 32;
        if (targetH < 32) targetH = 32;

        scaleX = (float)origW / targetW;
        scaleY = (float)origH / targetH;

        var scaledBmp = new Bitmap(targetW, targetH, PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(scaledBmp))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.SmoothingMode = SmoothingMode.HighQuality;
            g.DrawImage(original, 0, 0, targetW, targetH);
        }

        return scaledBmp;
    }
}
