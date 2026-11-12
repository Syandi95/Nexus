using System;
using System.Collections.Generic;

namespace Nexus.Engine.ImageSubsystem.Ocr;

/// <summary>
/// Oriented 4-point quadrilateral in native image pixel coordinates.
/// Vertices are clockwise or counter-clockwise: (X1, Y1), (X2, Y2), (X3, Y3), (X4, Y4).
/// </summary>
public readonly struct OcrQuad
{
    public float X1 { get; }
    public float Y1 { get; }
    public float X2 { get; }
    public float Y2 { get; }
    public float X3 { get; }
    public float Y3 { get; }
    public float X4 { get; }
    public float Y4 { get; }

    public float MinX => Math.Min(Math.Min(X1, X2), Math.Min(X3, X4));
    public float MaxX => Math.Max(Math.Max(X1, X2), Math.Max(X3, X4));
    public float MinY => Math.Min(Math.Min(Y1, Y2), Math.Min(Y3, Y4));
    public float MaxY => Math.Max(Math.Max(Y1, Y2), Math.Max(Y3, Y4));
    public float Width => Math.Max(0, MaxX - MinX);
    public float Height => Math.Max(0, MaxY - MinY);

    public OcrQuad(float x1, float y1, float x2, float y2, float x3, float y3, float x4, float y4)
    {
        X1 = x1; Y1 = y1;
        X2 = x2; Y2 = y2;
        X3 = x3; Y3 = y3;
        X4 = x4; Y4 = y4;
    }

    /// <summary>
    /// Tests whether point (px, py) lies inside the 4-point convex quadrilateral.
    /// </summary>
    public bool ContainsPoint(float px, float py)
    {
        float c1 = Cross(X1, Y1, X2, Y2, px, py);
        float c2 = Cross(X2, Y2, X3, Y3, px, py);
        float c3 = Cross(X3, Y3, X4, Y4, px, py);
        float c4 = Cross(X4, Y4, X1, Y1, px, py);

        bool pos = c1 >= 0 && c2 >= 0 && c3 >= 0 && c4 >= 0;
        bool neg = c1 <= 0 && c2 <= 0 && c3 <= 0 && c4 <= 0;
        if (pos || neg) return true;

        // Fallback to bounding box with small tolerance
        return px >= MinX - 1.5f && px <= MaxX + 1.5f && py >= MinY - 1.5f && py <= MaxY + 1.5f;
    }

    private static float Cross(float ax, float ay, float bx, float by, float px, float py)
    {
        return (bx - ax) * (py - ay) - (by - ay) * (px - ax);
    }
}

/// <summary>
/// Word-level OCR record with true native quadrilateral geometry from OneOCR.
/// </summary>
public sealed class OneOcrWord
{
    public int GlobalIndex { get; set; }
    public int LineIndex { get; set; }
    public int WordIndex { get; set; }
    public string Text { get; set; } = string.Empty;
    public float Confidence { get; set; }
    public OcrQuad Quad { get; set; }
}

/// <summary>
/// Line-level OCR record containing its constituent words in reading order.
/// </summary>
public sealed class OneOcrLine
{
    public int LineIndex { get; set; }
    public string Text { get; set; } = string.Empty;
    public OcrQuad Quad { get; set; }
    public IReadOnlyList<OneOcrWord> Words { get; set; } = Array.Empty<OneOcrWord>();
}

/// <summary>
/// Root OCR Document containing all lines and words recognized by OneOCR.
/// </summary>
public sealed class OneOcrDocument
{
    public int ImageWidth { get; set; }
    public int ImageHeight { get; set; }
    public float ImageAngle { get; set; }
    public IReadOnlyList<OneOcrLine> Lines { get; set; } = Array.Empty<OneOcrLine>();
    public IReadOnlyList<OneOcrWord> AllWords { get; set; } = Array.Empty<OneOcrWord>();
    public string FullText { get; set; } = string.Empty;
    public long InferenceMs { get; set; }
}

/// <summary>
/// Exception thrown when OneOCR native binaries or models cannot be found or loaded.
/// </summary>
public class OneOcrNotAvailableException : Exception
{
    public OneOcrNotAvailableException(string message) : base(message) { }
    public OneOcrNotAvailableException(string message, Exception innerException) : base(message, innerException) { }
}
