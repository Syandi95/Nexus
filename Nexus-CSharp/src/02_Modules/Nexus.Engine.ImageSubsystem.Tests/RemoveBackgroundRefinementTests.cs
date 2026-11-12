using System.Drawing;
using System.Drawing.Imaging;
using Nexus.Engine.ImageSubsystem.Pipelines;
using Xunit;

namespace Nexus.Engine.ImageSubsystem.Tests;

public sealed class RemoveBackgroundRefinementTests
{
    [Fact]
    public void ExtractAlpha_ExtractsAlphaChannelCorrectly()
    {
        using var bmp = new Bitmap(2, 2, PixelFormat.Format32bppArgb);
        bmp.SetPixel(0, 0, Color.FromArgb(0, 255, 0, 0));
        bmp.SetPixel(1, 0, Color.FromArgb(128, 0, 255, 0));
        bmp.SetPixel(0, 1, Color.FromArgb(200, 0, 0, 255));
        bmp.SetPixel(1, 1, Color.FromArgb(255, 255, 255, 255));

        var alpha = RemoveBackgroundPipeline.ExtractAlpha(bmp);

        Assert.Equal(4, alpha.Length);
        Assert.Equal(0, alpha[0]);
        Assert.Equal(128, alpha[1]);
        Assert.Equal(200, alpha[2]);
        Assert.Equal(255, alpha[3]);
    }

    [Fact]
    public void UpdateAlpha_UpdatesTargetBitmapAlphaOnly()
    {
        using var bmp = new Bitmap(2, 2, PixelFormat.Format32bppArgb);
        bmp.SetPixel(0, 0, Color.FromArgb(255, 10, 20, 30));
        bmp.SetPixel(1, 0, Color.FromArgb(255, 40, 50, 60));
        bmp.SetPixel(0, 1, Color.FromArgb(255, 70, 80, 90));
        bmp.SetPixel(1, 1, Color.FromArgb(255, 100, 110, 120));

        byte[] newAlpha = [10, 20, 30, 40];
        RemoveBackgroundPipeline.UpdateAlpha(bmp, newAlpha);

        var p0 = bmp.GetPixel(0, 0);
        Assert.Equal(10, p0.A);
        Assert.Equal(10, p0.R);
        Assert.Equal(20, p0.G);
        Assert.Equal(30, p0.B);

        var p3 = bmp.GetPixel(1, 1);
        Assert.Equal(40, p3.A);
        Assert.Equal(100, p3.R);
        Assert.Equal(110, p3.G);
        Assert.Equal(120, p3.B);
    }

    [Fact]
    public void CalculateStrokeRoi_ClampsSafelyToImageBounds()
    {
        int imgW = 100;
        int imgH = 100;

        // Stroke melewati tepi kiri atas
        var roiLeftTop = RemoveBackgroundPipeline.CalculateStrokeRoi(new Rectangle(-20, -20, 30, 30), 5, imgW, imgH);
        Assert.Equal(0, roiLeftTop.X);
        Assert.Equal(0, roiLeftTop.Y);
        Assert.True(roiLeftTop.Width <= 20);
        Assert.True(roiLeftTop.Height <= 20);

        // Stroke melewati tepi kanan bawah
        var roiRightBottom = RemoveBackgroundPipeline.CalculateStrokeRoi(new Rectangle(90, 90, 30, 30), 5, imgW, imgH);
        Assert.Equal(85, roiRightBottom.X);
        Assert.Equal(85, roiRightBottom.Y);
        Assert.Equal(15, roiRightBottom.Width);
        Assert.Equal(15, roiRightBottom.Height);
    }

    [Fact]
    public void ApplyStroke_AddMode_IncreasesAlphaInRoi_AndGeneratesPatch()
    {
        int w = 4;
        int h = 4;
        byte[] activeMask = new byte[w * h]; // Awalnya semua 0 (transparan)
        var roi = new Rectangle(1, 1, 2, 2);
        byte[] strokeMask = [255, 255, 255, 255]; // stroke 2x2 penuh

        var patch = RemoveBackgroundPipeline.ApplyStroke(activeMask, w, h, roi, strokeMask, isAdd: true);

        Assert.NotNull(patch);
        Assert.Equal(roi, patch.Bounds);
        Assert.All(patch.OldAlpha, a => Assert.Equal(0, a));
        Assert.All(patch.NewAlpha, a => Assert.Equal(255, a));

        // Verifikasi activeMask berubah di dalam ROI
        Assert.Equal(255, activeMask[1 * w + 1]);
        Assert.Equal(255, activeMask[1 * w + 2]);
        Assert.Equal(255, activeMask[2 * w + 1]);
        Assert.Equal(255, activeMask[2 * w + 2]);

        // Verifikasi di luar ROI tidak berubah
        Assert.Equal(0, activeMask[0]);
        Assert.Equal(0, activeMask[3 * w + 3]);
    }

    [Fact]
    public void ApplyStroke_RemoveMode_DecreasesAlphaInRoi()
    {
        int w = 4;
        int h = 4;
        byte[] activeMask = new byte[w * h];
        Array.Fill(activeMask, (byte)255); // Awalnya semua 255 (foreground)
        var roi = new Rectangle(1, 1, 2, 2);
        byte[] strokeMask = [255, 255, 255, 255]; // stroke 2x2 hapus

        var patch = RemoveBackgroundPipeline.ApplyStroke(activeMask, w, h, roi, strokeMask, isAdd: false);

        Assert.NotNull(patch);
        Assert.All(patch.OldAlpha, a => Assert.Equal(255, a));
        Assert.All(patch.NewAlpha, a => Assert.Equal(0, a));

        // Verifikasi activeMask berkurang di ROI
        Assert.Equal(0, activeMask[1 * w + 1]);
        Assert.Equal(0, activeMask[2 * w + 2]);

        // Di luar ROI tetap 255
        Assert.Equal(255, activeMask[0]);
    }

    [Fact]
    public void ApplyPatch_UndoAndRedo_RestoresAlphaCorrectly()
    {
        int w = 4;
        int h = 4;
        byte[] activeMask = new byte[w * h];
        var roi = new Rectangle(1, 1, 2, 2);
        byte[] strokeMask = [255, 255, 255, 255];

        var patch = RemoveBackgroundPipeline.ApplyStroke(activeMask, w, h, roi, strokeMask, isAdd: true);

        // Undo -> restore OldAlpha
        RemoveBackgroundPipeline.ApplyPatch(activeMask, w, h, patch, isRedo: false);
        Assert.Equal(0, activeMask[1 * w + 1]);
        Assert.Equal(0, activeMask[2 * w + 2]);

        // Redo -> restore NewAlpha
        RemoveBackgroundPipeline.ApplyPatch(activeMask, w, h, patch, isRedo: true);
        Assert.Equal(255, activeMask[1 * w + 1]);
        Assert.Equal(255, activeMask[2 * w + 2]);
    }

    [Fact]
    public void Reset_RestoresActiveMaskToBaseline_WithoutAiInference()
    {
        int w = 4;
        int h = 4;
        byte[] baselineAi = [0, 0, 255, 255, 0, 0, 255, 255, 0, 0, 255, 255, 0, 0, 255, 255];
        byte[] activeMask = (byte[])baselineAi.Clone();

        // User melakukan coretan Add Mask di sudut kiri
        var roi = new Rectangle(0, 0, 2, 2);
        byte[] stroke = [255, 255, 255, 255];
        RemoveBackgroundPipeline.ApplyStroke(activeMask, w, h, roi, stroke, isAdd: true);

        // Active mask sudah termodifikasi
        Assert.Equal(255, activeMask[0]);

        // Reset: salin ulang baseline ke activeMask
        Array.Copy(baselineAi, activeMask, baselineAi.Length);

        // Active mask kembali persis sama dengan baseline
        Assert.Equal(baselineAi, activeMask);
    }

    [Fact]
    public void ComputePhotosAlpha_SoftnessZero_YieldsHardEdge()
    {
        float radius = 10f;
        float softness = 0f;

        // Center
        Assert.Equal(255, RemoveBackgroundPipeline.ComputePhotosAlpha(0f, radius, softness));
        // Inside
        Assert.Equal(255, RemoveBackgroundPipeline.ComputePhotosAlpha(5f, radius, softness));
        // Edge
        Assert.Equal(255, RemoveBackgroundPipeline.ComputePhotosAlpha(10f, radius, softness));
        // Outside
        Assert.Equal(0, RemoveBackgroundPipeline.ComputePhotosAlpha(10.1f, radius, softness));
        Assert.Equal(0, RemoveBackgroundPipeline.ComputePhotosAlpha(15f, radius, softness));
    }

    [Fact]
    public void ComputePhotosAlpha_SoftnessOne_FollowsHermiteSmoothstepCurve()
    {
        float radius = 10f;
        float softness = 1.0f;

        // Center: full alpha
        Assert.Equal(255, RemoveBackgroundPipeline.ComputePhotosAlpha(0f, radius, softness));

        // Half radius (dist = 5, r = 0.5): Hermite smoothstep s(0.5) = 0.25*(3-1) = 0.5 -> alpha = 0.5 * 255 = 128
        byte midAlpha = RemoveBackgroundPipeline.ComputePhotosAlpha(5f, radius, softness);
        Assert.InRange(midAlpha, 126, 129);

        // Outer boundary: zero alpha
        Assert.Equal(0, RemoveBackgroundPipeline.ComputePhotosAlpha(10f, radius, softness));
        Assert.Equal(0, RemoveBackgroundPipeline.ComputePhotosAlpha(12f, radius, softness));
    }

    [Fact]
    public void ComputePhotosAlpha_SoftnessHalf_HasFlatCoreAndSmoothstepEdge()
    {
        float radius = 10f;
        float softness = 0.5f;

        // Flat core for dist <= 0.5 * radius (dist <= 5)
        Assert.Equal(255, RemoveBackgroundPipeline.ComputePhotosAlpha(0f, radius, softness));
        Assert.Equal(255, RemoveBackgroundPipeline.ComputePhotosAlpha(2.5f, radius, softness));
        Assert.Equal(255, RemoveBackgroundPipeline.ComputePhotosAlpha(5f, radius, softness));

        // Smooth transition for dist > 5
        byte transitionAlpha = RemoveBackgroundPipeline.ComputePhotosAlpha(7.5f, radius, softness);
        Assert.True(transitionAlpha > 0 && transitionAlpha < 255);

        // Edge
        Assert.Equal(0, RemoveBackgroundPipeline.ComputePhotosAlpha(10f, radius, softness));
    }

    [Fact]
    public void RasterizePillStroke_ContinuousStroke_ProducesCapsuleMask()
    {
        var points = new PointF[]
        {
            new(5f, 5f),
            new(15f, 5f)
        };
        var roi = new Rectangle(0, 0, 20, 10);
        float diameter = 6f; // radius 3
        float softness = 0f;

        var mask = RemoveBackgroundPipeline.RasterizePillStroke(points, diameter, softness, roi);

        Assert.Equal(200, mask.Length);

        // Center line along stroke should be 255
        for (int x = 5; x <= 15; x++)
        {
            Assert.Equal(255, mask[5 * 20 + x]);
        }

        // Far away should be 0
        Assert.Equal(0, mask[0]);
        Assert.Equal(0, mask[9 * 20 + 0]);
        Assert.Equal(0, mask[9 * 20 + 19]);
    }

    [Fact]
    public void CreateMaskVisualization_ForegroundClear_BackgroundTinted()
    {
        int w = 4;
        int h = 4;
        // Half foreground (255), half background (0)
        byte[] mask = [
            255, 255, 0, 0,
            255, 255, 0, 0,
            255, 255, 0, 0,
            255, 255, 0, 0
        ];

        var bgra = RemoveBackgroundPipeline.CreateMaskVisualization(mask, w, h);

        Assert.Equal(w * h * 4, bgra.Length);

        // Left column (foreground kept): alpha must be 0 (completely clear)
        for (int y = 0; y < h; y++)
        {
            int p = (y * w + 0) * 4;
            Assert.Equal(0, bgra[p + 3]); // Alpha = 0
        }

        // Right column (background removed): alpha must be > 0 and colors from cyan palette
        for (int y = 0; y < h; y++)
        {
            int p = (y * w + 3) * 4;
            Assert.True(bgra[p + 3] > 0); // Alpha > 0
            Assert.True(bgra[p + 0] >= 240); // Blue component high (cyan/blue)
        }
    }
}
