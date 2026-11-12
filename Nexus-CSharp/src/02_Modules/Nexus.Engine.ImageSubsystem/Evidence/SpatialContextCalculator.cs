using System;
using System.Collections.Generic;
using System.Linq;
using Nexus.Engine.ImageSubsystem.Ocr;

namespace Nexus.Engine.ImageSubsystem.Evidence;

/// <summary>
/// Kalkulator konteks spasial berbasis geometri 2D bounding box (OcrBoxModel).
/// Menggunakan vertical overlap dan horizontal proximity untuk merangkai konteks baris nyata,
/// bukan sekadar indeks urutan OCR (index - 1 / index + 1).
/// </summary>
public static class SpatialContextCalculator
{
    public const float DefaultMinVerticalOverlap = 0.50f;

    /// <summary>
    /// Menghitung rasio tumpang tindih vertikal (vertical overlap) antara dua bounding box.
    /// Rumus: OverlapHeight / Min(HeightA, HeightB).
    /// </summary>
    public static float CalculateVerticalOverlap(OcrBoxModel a, OcrBoxModel b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);

        int yTop = Math.Max(a.Y1, b.Y1);
        int yBottom = Math.Min(a.Y2, b.Y2);
        int overlap = Math.Max(0, yBottom - yTop);

        int minHeight = Math.Min(a.Height, b.Height);
        if (minHeight <= 0) return 0f;

        return (float)overlap / minHeight;
    }

    /// <summary>
    /// Menemukan semua kotak OCR yang berada pada satu baris pembacaan yang sama
    /// dengan targetBox (vertical overlap >= minVerticalOverlap), diurutkan dari kiri ke kanan (X1).
    /// </summary>
    public static List<OcrBoxModel> GetSameLineBoxes(
        OcrBoxModel targetBox,
        IEnumerable<OcrBoxModel> allBoxes,
        float minVerticalOverlap = DefaultMinVerticalOverlap)
    {
        ArgumentNullException.ThrowIfNull(targetBox);
        ArgumentNullException.ThrowIfNull(allBoxes);

        var sameLine = new List<OcrBoxModel>();
        foreach (var b in allBoxes)
        {
            if (b.Id == targetBox.Id)
            {
                sameLine.Add(b);
                continue;
            }

            float overlap = CalculateVerticalOverlap(targetBox, b);
            if (overlap >= minVerticalOverlap)
            {
                sameLine.Add(b);
            }
        }

        // Urutkan kotak berdasarkan posisi horizontal kiri (X1)
        sameLine.Sort((a, b) => a.X1.CompareTo(b.X1));
        return sameLine;
    }

    /// <summary>
    /// Menghasilkan string konteks baris lengkap dari baris yang memuat targetBox,
    /// dengan targetBox ditandai menggunakan tanda kurung siku `[TEKS]`.
    /// </summary>
    public static string BuildLineContext(
        OcrBoxModel targetBox,
        IEnumerable<OcrBoxModel> allBoxes,
        float minVerticalOverlap = DefaultMinVerticalOverlap)
    {
        var lineBoxes = GetSameLineBoxes(targetBox, allBoxes, minVerticalOverlap);
        if (lineBoxes.Count == 0)
        {
            return $"[{targetBox.Text}]";
        }

        var tokens = lineBoxes.Select(b => b.Id == targetBox.Id ? $"[{b.Text}]" : b.Text);
        return string.Join(" ", tokens);
    }

    /// <summary>
    /// Mengambil tetangga terdekat di sebelah kiri dan kanan pada baris yang sama.
    /// </summary>
    public static (OcrBoxModel? Left, OcrBoxModel? Right) GetNearestNeighbors(
        OcrBoxModel targetBox,
        IEnumerable<OcrBoxModel> allBoxes,
        float minVerticalOverlap = DefaultMinVerticalOverlap)
    {
        var lineBoxes = GetSameLineBoxes(targetBox, allBoxes, minVerticalOverlap);
        int index = lineBoxes.FindIndex(b => b.Id == targetBox.Id);
        if (index < 0) return (null, null);

        OcrBoxModel? left = index > 0 ? lineBoxes[index - 1] : null;
        OcrBoxModel? right = index < lineBoxes.Count - 1 ? lineBoxes[index + 1] : null;

        return (left, right);
    }
}
