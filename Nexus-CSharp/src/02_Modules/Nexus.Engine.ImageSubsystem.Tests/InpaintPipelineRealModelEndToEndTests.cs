using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using Nexus.Contracts;
using Nexus.Engine.ImageSubsystem.Pipelines;
using Nexus.Engine.ImageSubsystem.Services;
using Nexus.Foundation.Registry;
using Xunit;

namespace Nexus.Engine.ImageSubsystem.Tests;

public sealed class InpaintPipelineRealModelEndToEndTests
{
    private const string ModelPath = @"d:\NEXUS\Models\Inpaint\LaMa\model_fp32.onnx";

    [Fact]
    public async Task ExecuteAsync_RealModel_SinglePass_1080p_GeneratesCleanOutput()
    {
        if (!File.Exists(ModelPath))
        {
            return; // Skip if model not present
        }

        var registry = new ModelRegistry();
        var descriptor = new ResolvedModelDescriptor
        {
            ModelId = "big-lama-fp32",
            ModelPath = ModelPath,
            ModelType = ModelType.Onnx,
            InputTensorName = "image",
            OutputTensorName = "output",
            InputShape = [1, 3, 512, 512],
            OutputShape = [1, 3, 512, 512],
            TensorType = "float32",
            AdditionalInputs = new Dictionary<string, long[]>
            {
                ["mask"] = [1, 1, 512, 512]
            }
        };
        registry.RegisterModel(AICapability.InpaintingGeneral, descriptor);
        var policy = new DefaultModelSelectionPolicy(registry);

        await using var engine = new GenericImageEngine();
        var pipeline = new InpaintPipeline(policy, engine);

        // Buat gambar sintetis FHD 1920x1080 dengan gradien dan tekstur detail
        using var input = CreateSyntheticImage(1920, 1080);
        using var mask = new Bitmap(1920, 1080, PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(mask))
        {
            g.Clear(Color.Black);
            // Mask kotak di tengah kanvas (200x200)
            g.FillRectangle(Brushes.White, 860, 440, 200, 200);
        }

        input.Save(@"d:\NEXUS\test_e2e_input_1080p.png", ImageFormat.Png);
        mask.Save(@"d:\NEXUS\test_e2e_mask_1080p.png", ImageFormat.Png);

        using var result = await pipeline.ExecuteAsync(
            input,
            mask,
            InpaintExecutionTier.FastSinglePass);

        Assert.NotNull(result);
        Assert.Equal(1920, result.Width);
        Assert.Equal(1080, result.Height);

        result.Save(@"d:\NEXUS\test_e2e_output_1080p_csharp.png", ImageFormat.Png);

        // Verifikasi area di luar mask identik dengan input
        var pixelOutside = result.GetPixel(100, 100);
        var expectedOutside = input.GetPixel(100, 100);
        Assert.Equal(expectedOutside.R, pixelOutside.R);
        Assert.Equal(expectedOutside.G, pixelOutside.G);
        Assert.Equal(expectedOutside.B, pixelOutside.B);

        // Verifikasi area di dalam mask telah di-inpaint (bukan putih, bukan hitam, dan bukan artefak distorsi)
        var centerPixel = result.GetPixel(960, 540);
        Assert.False(centerPixel.R == 255 && centerPixel.G == 255 && centerPixel.B == 255, "Pusat mask tidak boleh tetap putih");
        Assert.False(centerPixel.R == 0 && centerPixel.G == 0 && centerPixel.B == 0, "Pusat mask tidak boleh hitam kosong");
    }

    [Fact]
    public async Task ExecuteAsync_RealModel_DualPass_2K_GeneratesCleanOutput()
    {
        if (!File.Exists(ModelPath))
        {
            return;
        }

        var registry = new ModelRegistry();
        var descriptor = new ResolvedModelDescriptor
        {
            ModelId = "big-lama-fp32",
            ModelPath = ModelPath,
            ModelType = ModelType.Onnx,
            InputTensorName = "image",
            OutputTensorName = "output",
            InputShape = [1, 3, 512, 512],
            OutputShape = [1, 3, 512, 512],
            TensorType = "float32",
            AdditionalInputs = new Dictionary<string, long[]>
            {
                ["mask"] = [1, 1, 512, 512]
            }
        };
        registry.RegisterModel(AICapability.InpaintingGeneral, descriptor);
        var policy = new DefaultModelSelectionPolicy(registry);

        await using var engine = new GenericImageEngine();
        var pipeline = new InpaintPipeline(policy, engine);

        // Buat gambar sintetis resolusi tinggi 2K (2560x1440)
        using var input = CreateSyntheticImage(2560, 1440);
        using var mask = new Bitmap(2560, 1440, PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(mask))
        {
            g.Clear(Color.Black);
            // Mask di tengah kanvas (260x260)
            g.FillRectangle(Brushes.White, 1150, 590, 260, 260);
        }

        input.Save(@"d:\NEXUS\test_e2e_input_2k.png", ImageFormat.Png);
        mask.Save(@"d:\NEXUS\test_e2e_mask_2k.png", ImageFormat.Png);

        using var result = await pipeline.ExecuteAsync(
            input,
            mask,
            InpaintExecutionTier.HighDetailDualPass);

        Assert.NotNull(result);
        Assert.Equal(2560, result.Width);
        Assert.Equal(1440, result.Height);

        result.Save(@"d:\NEXUS\test_e2e_output_2k_csharp.png", ImageFormat.Png);

        // Verifikasi area di luar mask
        var pixelOutside = result.GetPixel(200, 200);
        var expectedOutside = input.GetPixel(200, 200);
        Assert.Equal(expectedOutside.R, pixelOutside.R);
        Assert.Equal(expectedOutside.G, pixelOutside.G);
        Assert.Equal(expectedOutside.B, pixelOutside.B);

        // Verifikasi area di dalam mask
        var centerPixel = result.GetPixel(1280, 720);
        Assert.False(centerPixel.R == 255 && centerPixel.G == 255 && centerPixel.B == 255, "Pusat mask tidak boleh tetap putih");
        Assert.False(centerPixel.R == 0 && centerPixel.G == 0 && centerPixel.B == 0, "Pusat mask tidak boleh hitam kosong");
    }

    [Fact]
    public async Task ExecuteAsync_RealModels_MobileSamAutoSelect_CombinedWithManualBrush_InpaintEndToEnd_Succeeds()
    {
        var samDir = @"d:\NEXUS\Models\SegmentAnything";
        if (!File.Exists(ModelPath) || !Directory.Exists(samDir) ||
            !File.Exists(Path.Combine(samDir, "mobile_sam_image_encoder.onnx")) ||
            !File.Exists(Path.Combine(samDir, "sam_mask_decoder_single.onnx")))
        {
            return;
        }

        // 1. Setup LaMa Pipeline
        var registry = new ModelRegistry();
        var descriptor = new ResolvedModelDescriptor
        {
            ModelId = "big-lama-fp32",
            ModelPath = ModelPath,
            ModelType = ModelType.Onnx,
            InputTensorName = "image",
            OutputTensorName = "output",
            InputShape = [1, 3, 512, 512],
            OutputShape = [1, 3, 512, 512],
            TensorType = "float32",
            AdditionalInputs = new Dictionary<string, long[]>
            {
                ["mask"] = [1, 1, 512, 512]
            }
        };
        registry.RegisterModel(AICapability.InpaintingGeneral, descriptor);
        var policy = new DefaultModelSelectionPolicy(registry);

        await using var engine = new GenericImageEngine();
        var pipeline = new InpaintPipeline(policy, engine);

        // 2. Setup MobileSAM Service
        var samService = new MobileSamService(samDir);

        // 3. Create test image with two distinct shapes
        int width = 800;
        int height = 600;
        using var inputBmp = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(inputBmp))
        {
            g.Clear(Color.CornflowerBlue);
            // Shape 1: Red circle centered at (250, 300)
            using var redBrush = new SolidBrush(Color.Crimson);
            g.FillEllipse(redBrush, 175, 225, 150, 150);
            // Shape 2: Yellow square centered at (550, 300)
            using var yellowBrush = new SolidBrush(Color.Gold);
            g.FillRectangle(yellowBrush, 475, 225, 150, 150);
        }

        // 4. MobileSAM: Auto-select Shape 1 by clicking at (250, 300)
        using var samSession = await samService.EncodeImageAsync(inputBmp);
        using var autoMask = await samService.SegmentPointAsync(samSession, new PointF(250f, 300f));
        Assert.NotNull(autoMask);
        Assert.Equal(255, autoMask.GetPixel(250, 300).R);

        // 5. Manual brush simulation: User draws a stroke over Shape 2 (550, 300)
        using var combinedMask = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(combinedMask))
        {
            g.Clear(Color.Black);
            using var whiteBrush = new SolidBrush(Color.White);
            g.FillRectangle(whiteBrush, 475, 225, 150, 150);
        }

        // Blend AutoMask into combinedMask via Math.Max (union)
        var autoData = autoMask.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
        var combData = combinedMask.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadWrite, PixelFormat.Format24bppRgb);
        try
        {
            int stride = Math.Abs(autoData.Stride);
            byte[] autoBytes = new byte[stride * height];
            byte[] combBytes = new byte[stride * height];
            System.Runtime.InteropServices.Marshal.Copy(autoData.Scan0, autoBytes, 0, autoBytes.Length);
            System.Runtime.InteropServices.Marshal.Copy(combData.Scan0, combBytes, 0, combBytes.Length);

            for (int i = 0; i < autoBytes.Length; i++)
            {
                combBytes[i] = Math.Max(combBytes[i], autoBytes[i]);
            }

            System.Runtime.InteropServices.Marshal.Copy(combBytes, 0, combData.Scan0, combBytes.Length);
        }
        finally
        {
            autoMask.UnlockBits(autoData);
            combinedMask.UnlockBits(combData);
        }

        // Both shapes should be marked in combinedMask
        Assert.Equal(255, combinedMask.GetPixel(250, 300).R);
        Assert.Equal(255, combinedMask.GetPixel(550, 300).R);
        Assert.Equal(0, combinedMask.GetPixel(50, 50).R);

        // 6. Execute inpainting with the combined mask
        using var result = await pipeline.ExecuteAsync(
            inputBmp,
            combinedMask,
            InpaintExecutionTier.Auto);

        Assert.NotNull(result);
        Assert.Equal(width, result.Width);
        Assert.Equal(height, result.Height);

        // Corner pixel outside mask should match original
        var cornerOrig = inputBmp.GetPixel(50, 50);
        var cornerResult = result.GetPixel(50, 50);
        Assert.Equal(cornerOrig.R, cornerResult.R);
        Assert.Equal(cornerOrig.G, cornerResult.G);
        Assert.Equal(cornerOrig.B, cornerResult.B);

        // Both object centers should have been inpainted
        var center1 = result.GetPixel(250, 300);
        var center2 = result.GetPixel(550, 300);
        Assert.False(center1.R == 255 && center1.G == 255 && center1.B == 255);
        Assert.False(center2.R == 255 && center2.G == 255 && center2.B == 255);
    }

    private static Bitmap CreateSyntheticImage(int width, int height)
    {
        var bmp = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        // Latar belakang gradien langit ke tanah
        using (var brush = new LinearGradientBrush(
            new Point(0, 0),
            new Point(0, height),
            Color.FromArgb(50, 120, 200),
            Color.FromArgb(80, 160, 70)))
        {
            g.FillRectangle(brush, 0, 0, width, height);
        }

        // Tambahkan garis kisi dan pola geometris frekuensi tinggi
        using var pen = new Pen(Color.FromArgb(210, 190, 150), 3);
        for (var x = 0; x < width; x += 80)
        {
            g.DrawLine(pen, x, 0, x, height);
        }
        for (var y = 0; y < height; y += 80)
        {
            g.DrawLine(pen, 0, y, width, y);
        }

        // Tambahkan beberapa lingkaran warna-warni di berbagai posisi
        using var circleBrush1 = new SolidBrush(Color.FromArgb(220, 60, 40));
        using var circleBrush2 = new SolidBrush(Color.FromArgb(240, 190, 30));
        using var circleBrush3 = new SolidBrush(Color.FromArgb(70, 80, 180));

        g.FillEllipse(circleBrush1, width / 4, height / 3, 180, 180);
        g.FillEllipse(circleBrush2, width / 2 - 50, height / 2 - 50, 240, 240);
        g.FillEllipse(circleBrush3, (width * 3) / 4, (height * 2) / 3, 200, 200);

        return bmp;
    }
}
