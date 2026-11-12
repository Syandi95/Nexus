using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Nexus.Contracts;
using Nexus.Foundation.Registry;

namespace Nexus.Engine.ImageSubsystem.Pipelines;

/// <summary>
/// Menentukan tingkat eksekusi pipeline inpainting.
/// </summary>
public enum InpaintExecutionTier
{
    /// <summary>
    /// Menentukan tier secara otomatis berdasarkan dimensi citra:
    /// FastSinglePass untuk resolusi &lt; 2K, HighDetailDualPass untuk resolusi &gt;= 2K.
    /// </summary>
    Auto,

    /// <summary>
    /// Eksekusi cepat satu pass (512x512) dengan compositing kanvas asli (~2.0 detik).
    /// </summary>
    FastSinglePass,

    /// <summary>
    /// Eksekusi presisi tinggi dua pass (Jalan C: Global 512x512 + Local Crop 512x512 Blending) (~4.1 detik).
    /// Sangat efektif mempertahankan ketajaman frekuensi tinggi pada citra beresolusi tinggi.
    /// </summary>
    HighDetailDualPass
}

/// <summary>
/// Pipeline headless untuk preprocessing, inferensi LaMa ONNX, dan postprocessing
/// kapabilitas inpainting / object removal dengan arsitektur Adaptive Two-Tier.
/// </summary>
public sealed class InpaintPipeline
{
    private const string DefaultMaskTensorName = "mask";
    private const int DefaultModelDimension = 512;
    private const int CropPaddingMargin = 128;
    private const int TileOverlapMargin = 64;
    private const int MaxTilesLimit = 6;
    private const int TiledCropThreshold = 512;

    /// <summary>
    /// Catatan Kalibrasi: Bobot 0.65f (crop lokal tajam) dan 0.35f (konteks global) adalah
    /// nilai awal heuristik dari hasil uji Jalan C untuk menyeimbangkan ketajaman tekstur
    /// dengan konsistensi pencahayaan/warna makro.
    /// Nilai ini adalah titik awal yang dapat dikalibrasi ulang (tuning) berdasarkan evaluasi visual lanjutan.
    /// </summary>
    public const float DefaultLocalCropWeight = 0.65f;
    public const float DefaultGlobalContextWeight = 0.35f;

    private readonly IModelSelectionPolicy _modelSelectionPolicy;
    private readonly IGenericImageEngine _imageEngine;

    public InpaintPipeline(
        IModelSelectionPolicy modelSelectionPolicy,
        IGenericImageEngine imageEngine)
    {
        _modelSelectionPolicy = modelSelectionPolicy
            ?? throw new ArgumentNullException(nameof(modelSelectionPolicy));
        _imageEngine = imageEngine
            ?? throw new ArgumentNullException(nameof(imageEngine));
    }

    /// <summary>
    /// Menjalankan inpainting pada input bitmap berdasarkan mask yang diberikan.
    /// Menghasilkan bitmap 24bpp RGB baru. Bitmap input dan mask tetap dimiliki caller.
    /// </summary>
    /// <param name="input">Citra asli yang akan di-inpaint.</param>
    /// <param name="mask">
    /// KONTRAK UI: Mask harus berupa gambar biner / grayscale di mana piksel putih (nilai &gt; 128)
    /// menandai area objek yang akan dihapus (di-inpaint), dan piksel hitam (nilai &lt;= 128) menandai
    /// area kanvas asli yang dipertahankan. Pipeline mengevaluasi intensitas kanal (R/G/B atau Luminance).
    /// </param>
    /// <param name="tier">Tingkat eksekusi (Auto, FastSinglePass, atau HighDetailDualPass).</param>
    /// <param name="sessionOptions">Opsi sesi inferensi (opsional).</param>
    /// <param name="cancellationToken">Token pembatalan operasi.</param>
    public async Task<Bitmap> ExecuteAsync(
        Bitmap input,
        Bitmap mask,
        InpaintExecutionTier tier = InpaintExecutionTier.Auto,
        EngineSessionOptions? sessionOptions = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(mask);

        if (input.Width != mask.Width || input.Height != mask.Height)
        {
            throw new ArgumentException(
                $"Mask dimensions ({mask.Width}x{mask.Height}) must match input image dimensions ({input.Width}x{input.Height}).");
        }

        await using var jobScope = new PipelineJobScope(cancellationToken);
        var jobToken = jobScope.CancellationToken;
        jobToken.ThrowIfCancellationRequested();

        var options = sessionOptions ?? new EngineSessionOptions();
        var descriptor = _modelSelectionPolicy.SelectModel(
            AICapability.InpaintingGeneral,
            options);
        ValidateDescriptor(descriptor);

        var modelHeight = checked((int)descriptor.InputShape![2]);
        var modelWidth = checked((int)descriptor.InputShape[3]);

        var cropRect = CalculateMaskBoundingBox(mask, CropPaddingMargin);
        if (cropRect.IsEmpty)
        {
            return (Bitmap)input.Clone();
        }

        double areaRatio = (double)(cropRect.Width * cropRect.Height) / (input.Width * input.Height);
        bool isSmallObject = cropRect.Width <= modelWidth &&
                             cropRect.Height <= modelHeight &&
                             areaRatio <= 0.20 &&
                             (input.Width > modelWidth || input.Height > modelHeight);

        var sessionKey = await _imageEngine
            .LoadModelAsync(descriptor, options)
            .ConfigureAwait(false);

        try
        {
            jobToken.ThrowIfCancellationRequested();

            // Skenario 1: Objek Kecil di Foto Resolusi Tinggi (Auto Mode)
            // Menjalankan Local Native 1:1 Single-Pass (1 kali inferensi crop lokal tanpa downscale global)
            // Menghasilkan rekonstruksi tajam skala asli dalam ~2 detik
            if (tier == InpaintExecutionTier.Auto && isSmallObject)
            {
                int targetSize = Math.Min(modelWidth, Math.Min(input.Width, input.Height));
                int cx = cropRect.X + cropRect.Width / 2;
                int cy = cropRect.Y + cropRect.Height / 2;
                int x = Math.Clamp(cx - targetSize / 2, 0, Math.Max(0, input.Width - targetSize));
                int y = Math.Clamp(cy - targetSize / 2, 0, Math.Max(0, input.Height - targetSize));
                var localBox = new Rectangle(x, y, targetSize, targetSize);

                using var localInput = CropBitmap(input, localBox);
                using var localMask = CropBitmap(mask, localBox);

                var localInputs = await Task.Run(
                    () => PrepareInferenceInputs(descriptor, localInput, localMask),
                    jobToken).ConfigureAwait(false);

                jobToken.ThrowIfCancellationRequested();

                var localOutputs = await _imageEngine
                    .ExecuteInferenceAsync(sessionKey, localInputs)
                    .ConfigureAwait(false);

                jobToken.ThrowIfCancellationRequested();

                if (!localOutputs.TryGetValue(descriptor.OutputTensorName!, out var localOutObj) ||
                    localOutObj is not float[,,,] localOutTensor)
                {
                    throw new InvalidDataException(
                        $"Local output tensor '{descriptor.OutputTensorName}' was not a float32 NCHW tensor.");
                }

                return await Task.Run(
                    () => CompositeLocalNativePatch(input, mask, localBox, localOutTensor),
                    jobToken).ConfigureAwait(false);
            }

            var effectiveTier = ResolveTier(input.Width, input.Height, cropRect, tier);

            // Pass 1: Global Context (Downsample ke dimensi model)
            var globalInputs = await Task.Run(
                () => PrepareInferenceInputs(descriptor, input, mask),
                jobToken).ConfigureAwait(false);

            jobToken.ThrowIfCancellationRequested();

            var globalOutputs = await _imageEngine
                .ExecuteInferenceAsync(sessionKey, globalInputs)
                .ConfigureAwait(false);

            jobToken.ThrowIfCancellationRequested();

            if (!globalOutputs.TryGetValue(descriptor.OutputTensorName!, out var globalOutObj) ||
                globalOutObj is not float[,,,] globalOutTensor)
            {
                throw new InvalidDataException(
                    $"Output tensor '{descriptor.OutputTensorName}' was not a float32 NCHW tensor.");
            }

            // Jika Tier 1 atau jika mask tidak memerlukan crop pass 2
            if (effectiveTier == InpaintExecutionTier.FastSinglePass)
            {
                return await Task.Run(
                    () => CompositeSinglePass(input, mask, globalOutTensor),
                    jobToken).ConfigureAwait(false);
            }

            // Tier 2: Multi-Scale Dual-Pass (Pass 2 Local Crop / Tiled)
            if (cropRect.Width >= input.Width * 0.9 && cropRect.Height >= input.Height * 0.9)
            {
                // Mask mencakup hampir seluruh kanvas, Pass 1 global sudah optimal
                return await Task.Run(
                    () => CompositeSinglePass(input, mask, globalOutTensor),
                    jobToken).ConfigureAwait(false);
            }

            // Jika area crop cukup kecil (<= 768px), eksekusi single local crop efisien
            if (cropRect.Width <= TiledCropThreshold && cropRect.Height <= TiledCropThreshold)
            {
                using var cropInput = CropBitmap(input, cropRect);
                using var cropMask = CropBitmap(mask, cropRect);

                var cropInputs = await Task.Run(
                    () => PrepareInferenceInputs(descriptor, cropInput, cropMask),
                    jobToken).ConfigureAwait(false);

                jobToken.ThrowIfCancellationRequested();

                var cropOutputs = await _imageEngine
                    .ExecuteInferenceAsync(sessionKey, cropInputs)
                    .ConfigureAwait(false);

                jobToken.ThrowIfCancellationRequested();

                if (!cropOutputs.TryGetValue(descriptor.OutputTensorName!, out var cropOutObj) ||
                    cropOutObj is not float[,,,] cropOutTensor)
                {
                    throw new InvalidDataException(
                        $"Crop output tensor '{descriptor.OutputTensorName}' was not a float32 NCHW tensor.");
                }

                return await Task.Run(
                    () => CompositeDualPassBlend(input, mask, globalOutTensor, cropOutTensor, cropRect),
                    jobToken).ConfigureAwait(false);
            }

            // Jika area mask besar (> 768px), gunakan Parallel Tiled Local Crops (skala 1:1 tanpa downscaling)
            var tileSize = Math.Max(512, modelWidth);
            var tiles = GenerateMaskTiles(mask, cropRect, tileSize, TileOverlapMargin);
            if (tiles.Count == 0)
            {
                return await Task.Run(
                    () => CompositeSinglePass(input, mask, globalOutTensor),
                    jobToken).ConfigureAwait(false);
            }

            using var tileSemaphore = new SemaphoreSlim(2, 2);
            var tileTasks = tiles.Select(async tile =>
            {
                await tileSemaphore.WaitAsync(jobToken).ConfigureAwait(false);
                try
                {
                    jobToken.ThrowIfCancellationRequested();

                    using var tileInput = CropBitmap(input, tile);
                    using var tileMask = CropBitmap(mask, tile);

                    var tileInputs = await Task.Run(
                        () => PrepareInferenceInputs(descriptor, tileInput, tileMask),
                        jobToken).ConfigureAwait(false);

                    jobToken.ThrowIfCancellationRequested();

                    var tileOutputs = await _imageEngine
                        .ExecuteInferenceAsync(sessionKey, tileInputs)
                        .ConfigureAwait(false);

                    jobToken.ThrowIfCancellationRequested();

                    if (!tileOutputs.TryGetValue(descriptor.OutputTensorName!, out var tileOutObj) ||
                        tileOutObj is not float[,,,] tileOutTensor)
                    {
                        throw new InvalidDataException(
                            $"Tile output tensor '{descriptor.OutputTensorName}' was not a float32 NCHW tensor.");
                    }

                    return new TileInferenceResult(tile, tileOutTensor);
                }
                finally
                {
                    tileSemaphore.Release();
                }
            });

            var tileResults = await Task.WhenAll(tileTasks).ConfigureAwait(false);

            return await Task.Run(
                () => CompositeTiledDualPassBlend(input, mask, globalOutTensor, tileResults),
                jobToken).ConfigureAwait(false);
        }
        finally
        {
            await _imageEngine
                .UnloadModelAsync(sessionKey)
                .ConfigureAwait(false);
        }
    }

    private static InpaintExecutionTier ResolveTier(
        int imageWidth,
        int imageHeight,
        Rectangle cropRect,
        InpaintExecutionTier requestedTier)
    {
        if (requestedTier != InpaintExecutionTier.Auto)
        {
            return requestedTier;
        }

        // Jika foto sudah seukuran model (misal <= 512x512), 1 pass native sudah 1:1
        if (imageWidth <= DefaultModelDimension && imageHeight <= DefaultModelDimension)
        {
            return InpaintExecutionTier.FastSinglePass;
        }

        // Rasio luas area crop terhadap ukuran total kanvas foto
        double areaRatio = (double)(cropRect.Width * cropRect.Height) / (imageWidth * imageHeight);

        // Jika area mask mencakup mayoritas foto (misal >= 70% luas kanvas, atau >= 85% dimensi lebar & tinggi),
        // Pass 1 global context sudah optimal karena crop lokal tidak memberikan peningkatan resolusi ekstra
        if (areaRatio >= 0.70 || (cropRect.Width >= imageWidth * 0.85 && cropRect.Height >= imageHeight * 0.85))
        {
            return InpaintExecutionTier.FastSinglePass;
        }

        // Untuk objek terlokalisasi (stiker, orang, teks, watermark, objek kecil hingga menengah):
        // Selalu gunakan HighDetailDualPass untuk menjaga ketajaman resolusi asli
        return InpaintExecutionTier.HighDetailDualPass;
    }

    private static string ResolveMaskTensorName(ResolvedModelDescriptor descriptor)
    {
        if (descriptor.AdditionalInputs != null && descriptor.AdditionalInputs.Count > 0)
        {
            foreach (var key in descriptor.AdditionalInputs.Keys)
            {
                if (key.Contains("mask", StringComparison.OrdinalIgnoreCase))
                {
                    return key;
                }
            }
            return descriptor.AdditionalInputs.Keys.First();
        }

        return DefaultMaskTensorName;
    }

    private static void ValidateDescriptor(ResolvedModelDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        if (descriptor.ModelType != ModelType.Onnx)
        {
            throw new InvalidDataException("Inpainting requires an ONNX model descriptor.");
        }

        if (!string.Equals(descriptor.TensorType, "float32", StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException($"Tensor type '{descriptor.TensorType}' is not supported.");
        }

        if (string.IsNullOrWhiteSpace(descriptor.InputTensorName) ||
            string.IsNullOrWhiteSpace(descriptor.OutputTensorName))
        {
            throw new InvalidDataException("The model descriptor must define input and output tensor names.");
        }

        if (descriptor.InputShape is not [1, 3, > 0, > 0])
        {
            throw new InvalidDataException("Inpainting requires a positive NCHW input shape [1,3,H,W].");
        }

        if (descriptor.AdditionalInputs != null)
        {
            foreach (var (name, shape) in descriptor.AdditionalInputs)
            {
                if (shape is not [1, 1 or 3, > 0, > 0])
                {
                    throw new InvalidDataException(
                        $"Additional input tensor '{name}' requires positive NCHW shape with 1 or 3 channels.");
                }
            }
        }
    }

    private static Dictionary<string, object> PrepareInferenceInputs(
        ResolvedModelDescriptor descriptor,
        Bitmap image,
        Bitmap mask)
    {
        var inputs = new Dictionary<string, object>(StringComparer.Ordinal);

        // 1. Primary input
        if (descriptor.InputTensorName != null && descriptor.InputShape is [1, var c, var h, var w])
        {
            if (c == 3)
            {
                inputs[descriptor.InputTensorName] = CreateImageNchw(image, checked((int)w), checked((int)h));
            }
            else if (c == 1)
            {
                inputs[descriptor.InputTensorName] = CreateMaskNchw(mask, checked((int)w), checked((int)h));
            }
            else
            {
                throw new InvalidDataException(
                    $"Unsupported channel count {c} for primary input tensor '{descriptor.InputTensorName}'.");
            }
        }

        // 2. Additional inputs
        if (descriptor.AdditionalInputs != null && descriptor.AdditionalInputs.Count > 0)
        {
            foreach (var (tensorName, shape) in descriptor.AdditionalInputs)
            {
                if (shape is [1, var ac, var ah, var aw])
                {
                    if (ac == 3)
                    {
                        inputs[tensorName] = CreateImageNchw(image, checked((int)aw), checked((int)ah));
                    }
                    else if (ac == 1)
                    {
                        inputs[tensorName] = CreateMaskNchw(mask, checked((int)aw), checked((int)ah));
                    }
                    else
                    {
                        throw new InvalidDataException(
                            $"Unsupported channel count {ac} for additional input tensor '{tensorName}'.");
                    }
                }
                else
                {
                    throw new InvalidDataException($"Input tensor '{tensorName}' has invalid NCHW shape.");
                }
            }
        }
        else
        {
            var maskTensorName = ResolveMaskTensorName(descriptor);
            if (!inputs.ContainsKey(maskTensorName))
            {
                var defaultH = checked((int)descriptor.InputShape![2]);
                var defaultW = checked((int)descriptor.InputShape![3]);
                inputs[maskTensorName] = CreateMaskNchw(mask, defaultW, defaultH);
            }
        }

        return inputs;
    }

    private static float[,,,] CreateImageNchw(Bitmap input, int width, int height)
    {
        using var resized = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        resized.SetResolution(input.HorizontalResolution, input.VerticalResolution);
        using (var graphics = Graphics.FromImage(resized))
        {
            graphics.CompositingMode = CompositingMode.SourceCopy;
            graphics.InterpolationMode = InterpolationMode.HighQualityBilinear;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.DrawImage(input, new Rectangle(0, 0, width, height), 0, 0, input.Width, input.Height, GraphicsUnit.Pixel);
        }

        var tensor = new float[1, 3, height, width];
        var rectangle = new Rectangle(0, 0, width, height);
        var data = resized.LockBits(rectangle, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
        try
        {
            var stride = Math.Abs(data.Stride);
            var pixels = new byte[stride * height];
            Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);

            for (var y = 0; y < height; y++)
            {
                var row = data.Stride >= 0 ? y * stride : (height - 1 - y) * stride;
                for (var x = 0; x < width; x++)
                {
                    var pixel = row + (x * 3);
                    tensor[0, 0, y, x] = pixels[pixel + 2] / 255f; // R
                    tensor[0, 1, y, x] = pixels[pixel + 1] / 255f; // G
                    tensor[0, 2, y, x] = pixels[pixel] / 255f;     // B
                }
            }
        }
        finally
        {
            resized.UnlockBits(data);
        }

        return tensor;
    }

    private static float[,,,] CreateMaskNchw(Bitmap mask, int width, int height)
    {
        using var resized = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        resized.SetResolution(mask.HorizontalResolution, mask.VerticalResolution);
        using (var graphics = Graphics.FromImage(resized))
        {
            graphics.CompositingMode = CompositingMode.SourceCopy;
            graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
            graphics.PixelOffsetMode = PixelOffsetMode.Half;
            graphics.DrawImage(mask, new Rectangle(0, 0, width, height), 0, 0, mask.Width, mask.Height, GraphicsUnit.Pixel);
        }

        var tensor = new float[1, 1, height, width];
        var rectangle = new Rectangle(0, 0, width, height);
        var data = resized.LockBits(rectangle, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
        try
        {
            var stride = Math.Abs(data.Stride);
            var pixels = new byte[stride * height];
            Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);

            for (var y = 0; y < height; y++)
            {
                var row = data.Stride >= 0 ? y * stride : (height - 1 - y) * stride;
                for (var x = 0; x < width; x++)
                {
                    var pixel = row + (x * 3);
                    // Mendukung grayscale maupun brush berwarna: evaluasi intensitas maksimum RGB > 128
                    var maxChan = Math.Max(pixels[pixel], Math.Max(pixels[pixel + 1], pixels[pixel + 2]));
                    tensor[0, 0, y, x] = maxChan > 128 ? 1.0f : 0.0f;
                }
            }
        }
        finally
        {
            resized.UnlockBits(data);
        }

        return tensor;
    }

    private static Bitmap TensorToBitmap(float[,,,] tensor)
    {
        var height = tensor.GetLength(2);
        var width = tensor.GetLength(3);

        // Evaluasi rentang nilai output secara global (bukan per-piksel) untuk mencegah bug double-scaling.
        // LaMa FP32 menghasilkan rentang [0.0, 255.0]. Jika seluruh nilai <= 1.5f, berarti model dalam rentang [0.0, 1.0].
        var maxSample = 0.0f;
        var stepY = Math.Max(1, height / 16);
        var stepX = Math.Max(1, width / 16);
        for (var y = 0; y < height && maxSample <= 1.5f; y += stepY)
        {
            for (var x = 0; x < width && maxSample <= 1.5f; x += stepX)
            {
                var r = tensor[0, 0, y, x];
                var g = tensor[0, 1, y, x];
                var b = tensor[0, 2, y, x];
                var m = Math.Max(r, Math.Max(g, b));
                if (m > maxSample) maxSample = m;
            }
        }

        var isNormalizedZeroToOne = maxSample <= 1.5f;

        var bmp = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        var rect = new Rectangle(0, 0, width, height);
        var data = bmp.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
        try
        {
            var stride = Math.Abs(data.Stride);
            var pixels = new byte[stride * height];

            for (var y = 0; y < height; y++)
            {
                var row = data.Stride >= 0 ? y * stride : (height - 1 - y) * stride;
                for (var x = 0; x < width; x++)
                {
                    var pixel = row + (x * 3);
                    var r = tensor[0, 0, y, x];
                    var g = tensor[0, 1, y, x];
                    var b = tensor[0, 2, y, x];

                    if (isNormalizedZeroToOne)
                    {
                        r *= 255f;
                        g *= 255f;
                        b *= 255f;
                    }

                    pixels[pixel] = (byte)Math.Clamp((int)Math.Round(b), 0, 255);
                    pixels[pixel + 1] = (byte)Math.Clamp((int)Math.Round(g), 0, 255);
                    pixels[pixel + 2] = (byte)Math.Clamp((int)Math.Round(r), 0, 255);
                }
            }

            Marshal.Copy(pixels, 0, data.Scan0, pixels.Length);
        }
        finally
        {
            bmp.UnlockBits(data);
        }

        return bmp;
    }

    private static Bitmap CompositeLocalNativePatch(
        Bitmap input,
        Bitmap mask,
        Rectangle localRect,
        float[,,,] localTensor)
    {
        using var rawInpaint = TensorToBitmap(localTensor);
        using var upscaledPatch = (rawInpaint.Width == localRect.Width && rawInpaint.Height == localRect.Height)
            ? (Bitmap)rawInpaint.Clone()
            : new Bitmap(localRect.Width, localRect.Height, PixelFormat.Format24bppRgb);

        if (rawInpaint.Width != localRect.Width || rawInpaint.Height != localRect.Height)
        {
            upscaledPatch.SetResolution(input.HorizontalResolution, input.VerticalResolution);
            using var g = Graphics.FromImage(upscaledPatch);
            g.CompositingMode = CompositingMode.SourceCopy;
            g.InterpolationMode = InterpolationMode.HighQualityBilinear;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.DrawImage(rawInpaint, new Rectangle(0, 0, localRect.Width, localRect.Height), 0, 0, rawInpaint.Width, rawInpaint.Height, GraphicsUnit.Pixel);
        }

        var result = (Bitmap)input.Clone();
        var dResult = result.LockBits(new Rectangle(0, 0, input.Width, input.Height), ImageLockMode.ReadWrite, PixelFormat.Format24bppRgb);
        var dMask = mask.LockBits(new Rectangle(0, 0, input.Width, input.Height), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
        var dPatch = upscaledPatch.LockBits(new Rectangle(0, 0, localRect.Width, localRect.Height), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);

        try
        {
            int sRes = Math.Abs(dResult.Stride);
            int sMask = Math.Abs(dMask.Stride);
            int sPatch = Math.Abs(dPatch.Stride);
            byte[] resPix = new byte[sRes * input.Height];
            byte[] maskPix = new byte[sMask * input.Height];
            byte[] patchPix = new byte[sPatch * localRect.Height];

            Marshal.Copy(dResult.Scan0, resPix, 0, resPix.Length);
            Marshal.Copy(dMask.Scan0, maskPix, 0, maskPix.Length);
            Marshal.Copy(dPatch.Scan0, patchPix, 0, patchPix.Length);

            for (int py = 0; py < localRect.Height; py++)
            {
                int gy = localRect.Y + py;
                if (gy >= input.Height) break;
                int rRes = gy * sRes;
                int rMask = gy * sMask;
                int rPatch = py * sPatch;

                for (int px = 0; px < localRect.Width; px++)
                {
                    int gx = localRect.X + px;
                    if (gx >= input.Width) break;
                    int pRes = rRes + (gx * 3);
                    int pMask = rMask + (gx * 3);
                    int pPatch = rPatch + (px * 3);

                    if (Math.Max(maskPix[pMask], Math.Max(maskPix[pMask + 1], maskPix[pMask + 2])) > 128)
                    {
                        resPix[pRes] = patchPix[pPatch];
                        resPix[pRes + 1] = patchPix[pPatch + 1];
                        resPix[pRes + 2] = patchPix[pPatch + 2];
                    }
                }
            }

            Marshal.Copy(resPix, 0, dResult.Scan0, resPix.Length);
        }
        finally
        {
            result.UnlockBits(dResult);
            mask.UnlockBits(dMask);
            upscaledPatch.UnlockBits(dPatch);
        }

        return result;
    }

    private static Bitmap CompositeSinglePass(Bitmap input, Bitmap mask, float[,,,] outputTensor)
    {
        using var rawInpaint = TensorToBitmap(outputTensor);
        using var upscaledInpaint = new Bitmap(input.Width, input.Height, PixelFormat.Format24bppRgb);
        upscaledInpaint.SetResolution(input.HorizontalResolution, input.VerticalResolution);
        using (var g = Graphics.FromImage(upscaledInpaint))
        {
            g.CompositingMode = CompositingMode.SourceCopy;
            g.InterpolationMode = InterpolationMode.HighQualityBilinear;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.DrawImage(rawInpaint, new Rectangle(0, 0, input.Width, input.Height), 0, 0, rawInpaint.Width, rawInpaint.Height, GraphicsUnit.Pixel);
        }

        var result = new Bitmap(input.Width, input.Height, PixelFormat.Format24bppRgb);
        result.SetResolution(input.HorizontalResolution, input.VerticalResolution);
        var rect = new Rectangle(0, 0, input.Width, input.Height);

        var dInp = input.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
        var dMask = mask.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
        var dRec = upscaledInpaint.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
        var dOut = result.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);

        try
        {
            var stride = Math.Abs(dInp.Stride);
            var maskStride = Math.Abs(dMask.Stride);
            var len = stride * input.Height;
            var maskLen = maskStride * input.Height;
            var inBytes = new byte[len];
            var maskBytes = new byte[maskLen];
            var recBytes = new byte[len];
            var outBytes = new byte[len];

            Marshal.Copy(dInp.Scan0, inBytes, 0, len);
            Marshal.Copy(dMask.Scan0, maskBytes, 0, maskLen);
            Marshal.Copy(dRec.Scan0, recBytes, 0, len);

            for (var y = 0; y < input.Height; y++)
            {
                var row = dInp.Stride >= 0 ? y * stride : (input.Height - 1 - y) * stride;
                var maskRow = dMask.Stride >= 0 ? y * maskStride : (input.Height - 1 - y) * maskStride;
                for (var x = 0; x < input.Width; x++)
                {
                    var p = row + (x * 3);
                    var pMask = maskRow + (x * 3);
                    // Mendukung grayscale murni maupun brush berwarna: evaluasi intensitas maksimum kanal RGB > 128
                    var isMasked = Math.Max(maskBytes[pMask], Math.Max(maskBytes[pMask + 1], maskBytes[pMask + 2])) > 128;
                    if (isMasked)
                    {
                        outBytes[p] = recBytes[p];
                        outBytes[p + 1] = recBytes[p + 1];
                        outBytes[p + 2] = recBytes[p + 2];
                    }
                    else
                    {
                        outBytes[p] = inBytes[p];
                        outBytes[p + 1] = inBytes[p + 1];
                        outBytes[p + 2] = inBytes[p + 2];
                    }
                }
            }

            Marshal.Copy(outBytes, 0, dOut.Scan0, len);
        }
        finally
        {
            input.UnlockBits(dInp);
            mask.UnlockBits(dMask);
            upscaledInpaint.UnlockBits(dRec);
            result.UnlockBits(dOut);
        }

        return result;
    }

    private static Bitmap CompositeDualPassBlend(
        Bitmap input,
        Bitmap mask,
        float[,,,] globalTensor,
        float[,,,] cropTensor,
        Rectangle cropRect)
    {
        // 1. Dapatkan hasil pass 1 global penuh
        var globalComposite = CompositeSinglePass(input, mask, globalTensor);

        try
        {
            // 2. Upscale pass 2 crop lokal ke ukuran crop bounding box aslinya
            using var rawCropInpaint = TensorToBitmap(cropTensor);
            using var upscaledCrop = new Bitmap(cropRect.Width, cropRect.Height, PixelFormat.Format24bppRgb);
            using (var g = Graphics.FromImage(upscaledCrop))
            {
                g.CompositingMode = CompositingMode.SourceCopy;
                g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(rawCropInpaint, 0, 0, cropRect.Width, cropRect.Height);
            }

            // 3. Ambil potongan gambar global dan mask berukuran identik (cropRect.Width x cropRect.Height)
            using var globalCropPatch = CropBitmap(globalComposite, cropRect);
            using var maskCropPatch = CropBitmap(mask, cropRect);

            var dUpscaled = upscaledCrop.LockBits(new Rectangle(0, 0, cropRect.Width, cropRect.Height), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
            var dGlobalPatch = globalCropPatch.LockBits(new Rectangle(0, 0, cropRect.Width, cropRect.Height), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
            var dMaskPatch = maskCropPatch.LockBits(new Rectangle(0, 0, cropRect.Width, cropRect.Height), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
            var dGlobalComp = globalComposite.LockBits(new Rectangle(0, 0, globalComposite.Width, globalComposite.Height), ImageLockMode.ReadWrite, PixelFormat.Format24bppRgb);

            try
            {
                var stridePatch = Math.Abs(dUpscaled.Stride);
                var strideMask = Math.Abs(dMaskPatch.Stride);
                var strideGlobal = Math.Abs(dGlobalComp.Stride);

                var lenPatch = stridePatch * cropRect.Height;
                var lenMask = strideMask * cropRect.Height;
                var lenGlobal = strideGlobal * globalComposite.Height;

                var upscaledBytes = new byte[lenPatch];
                var globalBytes = new byte[lenPatch];
                var maskBytes = new byte[lenMask];
                var globalCompBytes = new byte[lenGlobal];

                Marshal.Copy(dUpscaled.Scan0, upscaledBytes, 0, lenPatch);
                Marshal.Copy(dGlobalPatch.Scan0, globalBytes, 0, lenPatch);
                Marshal.Copy(dMaskPatch.Scan0, maskBytes, 0, lenMask);
                Marshal.Copy(dGlobalComp.Scan0, globalCompBytes, 0, lenGlobal);

                for (var y = 0; y < cropRect.Height; y++)
                {
                    int gy = cropRect.Y + y;
                    if (gy >= globalComposite.Height) break;

                    var rowPatch = y * stridePatch;
                    var rowMask = y * strideMask;
                    var rowGlob = gy * strideGlobal;

                    for (var x = 0; x < cropRect.Width; x++)
                    {
                        int gx = cropRect.X + x;
                        if (gx >= globalComposite.Width) break;

                        var p = rowPatch + (x * 3);
                        var pMask = rowMask + (x * 3);
                        var pGlob = rowGlob + (gx * 3);

                        var isMasked = Math.Max(maskBytes[pMask], Math.Max(maskBytes[pMask + 1], maskBytes[pMask + 2])) > 128;
                        if (isMasked)
                        {
                            // Multi-scale blend: DefaultLocalCropWeight (65%) + DefaultGlobalContextWeight (35%)
                            globalCompBytes[pGlob] = (byte)Math.Clamp((int)Math.Round(DefaultLocalCropWeight * upscaledBytes[p] + DefaultGlobalContextWeight * globalBytes[p]), 0, 255);
                            globalCompBytes[pGlob + 1] = (byte)Math.Clamp((int)Math.Round(DefaultLocalCropWeight * upscaledBytes[p + 1] + DefaultGlobalContextWeight * globalBytes[p + 1]), 0, 255);
                            globalCompBytes[pGlob + 2] = (byte)Math.Clamp((int)Math.Round(DefaultLocalCropWeight * upscaledBytes[p + 2] + DefaultGlobalContextWeight * globalBytes[p + 2]), 0, 255);
                        }
                    }
                }

                Marshal.Copy(globalCompBytes, 0, dGlobalComp.Scan0, lenGlobal);
            }
            finally
            {
                upscaledCrop.UnlockBits(dUpscaled);
                globalCropPatch.UnlockBits(dGlobalPatch);
                maskCropPatch.UnlockBits(dMaskPatch);
                globalComposite.UnlockBits(dGlobalComp);
            }

            return globalComposite;
        }
        catch
        {
            globalComposite.Dispose();
            throw;
        }
    }

    private static Rectangle CalculateMaskBoundingBox(Bitmap mask, int padding)
    {
        var rect = new Rectangle(0, 0, mask.Width, mask.Height);
        var data = mask.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
        int minX = int.MaxValue, minY = int.MaxValue;
        int maxX = int.MinValue, maxY = int.MinValue;

        try
        {
            var stride = Math.Abs(data.Stride);
            var pixels = new byte[stride * mask.Height];
            Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);

            for (var y = 0; y < mask.Height; y++)
            {
                var row = y * stride;
                for (var x = 0; x < mask.Width; x++)
                {
                    var p = row + (x * 3);
                    var isMasked = Math.Max(pixels[p], Math.Max(pixels[p + 1], pixels[p + 2])) > 128;
                    if (isMasked)
                    {
                        if (x < minX) minX = x;
                        if (x > maxX) maxX = x;
                        if (y < minY) minY = y;
                        if (y > maxY) maxY = y;
                    }
                }
            }
        }
        finally
        {
            mask.UnlockBits(data);
        }

        if (minX > maxX || minY > maxY)
        {
            return Rectangle.Empty;
        }

        var clampedX = Math.Max(0, minX - padding);
        var clampedY = Math.Max(0, minY - padding);
        var clampedMaxX = Math.Min(mask.Width, maxX + padding + 1);
        var clampedMaxY = Math.Min(mask.Height, maxY + padding + 1);

        return new Rectangle(clampedX, clampedY, clampedMaxX - clampedX, clampedMaxY - clampedY);
    }

    private static Bitmap CropBitmap(Bitmap src, Rectangle area)
    {
        int x = Math.Clamp(area.X, 0, Math.Max(0, src.Width - 1));
        int y = Math.Clamp(area.Y, 0, Math.Max(0, src.Height - 1));
        int w = Math.Clamp(area.Width, 1, src.Width - x);
        int h = Math.Clamp(area.Height, 1, src.Height - y);
        lock (src)
        {
            return (Bitmap)src.Clone(new Rectangle(x, y, w, h), PixelFormat.Format24bppRgb);
        }
    }

    private sealed record TileInferenceResult(Rectangle TileRect, float[,,,] OutputTensor);

    private static List<Rectangle> GenerateMaskTiles(
        Bitmap mask,
        Rectangle boundingBox,
        int tileSize = DefaultModelDimension,
        int overlap = TileOverlapMargin)
    {
        var tiles = new List<Rectangle>();
        int tileStride = Math.Max(64, tileSize - overlap);
        int imgW = mask.Width;
        int imgH = mask.Height;

        int x0 = Math.Max(0, boundingBox.Left);
        int y0 = Math.Max(0, boundingBox.Top);
        int x1 = Math.Min(imgW, boundingBox.Right);
        int y1 = Math.Min(imgH, boundingBox.Bottom);

        var data = mask.LockBits(new Rectangle(0, 0, imgW, imgH), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
        byte[] maskBytes;
        int stride;
        try
        {
            stride = Math.Abs(data.Stride);
            maskBytes = new byte[stride * imgH];
            Marshal.Copy(data.Scan0, maskBytes, 0, maskBytes.Length);
        }
        finally
        {
            mask.UnlockBits(data);
        }

        for (int y = y0; y < y1; y += tileStride)
        {
            int tileY = y;
            if (tileY + tileSize > imgH)
            {
                tileY = Math.Max(0, imgH - tileSize);
            }

            for (int x = x0; x < x1; x += tileStride)
            {
                int tileX = x;
                if (tileX + tileSize > imgW)
                {
                    tileX = Math.Max(0, imgW - tileSize);
                }

                int tileW = Math.Min(tileSize, imgW - tileX);
                int tileH = Math.Min(tileSize, imgH - tileY);
                var rect = new Rectangle(tileX, tileY, tileW, tileH);

                if (!tiles.Contains(rect) && HasMaskPixelsInBuffer(maskBytes, stride, rect))
                {
                    tiles.Add(rect);
                    if (tiles.Count >= MaxTilesLimit)
                    {
                        return tiles;
                    }
                }

                if (tileX + tileSize >= imgW) break;
            }

            if (tileY + tileSize >= imgH) break;
        }

        return tiles;
    }

    private static bool HasMaskPixelsInBuffer(byte[] maskBytes, int stride, Rectangle rect)
    {
        int endY = rect.Y + rect.Height;
        int endX = rect.X + rect.Width;

        for (int y = rect.Y; y < endY; y++)
        {
            int row = y * stride;
            for (int x = rect.X; x < endX; x++)
            {
                int p = row + (x * 3);
                if (Math.Max(maskBytes[p], Math.Max(maskBytes[p + 1], maskBytes[p + 2])) > 128)
                {
                    return true;
                }
            }
        }
        return false;
    }

    private static Bitmap CompositeTiledDualPassBlend(
        Bitmap input,
        Bitmap mask,
        float[,,,] globalTensor,
        IReadOnlyList<TileInferenceResult> tileResults)
    {
        var globalComposite = CompositeSinglePass(input, mask, globalTensor);
        if (tileResults.Count == 0)
        {
            return globalComposite;
        }

        try
        {
            int W = input.Width, H = input.Height;
            float[] accumR = new float[W * H];
            float[] accumG = new float[W * H];
            float[] accumB = new float[W * H];
            float[] accumW = new float[W * H];

            foreach (var result in tileResults)
            {
                var tile = result.TileRect;
                using var rawTile = TensorToBitmap(result.OutputTensor);
                using var upscaledTile = (rawTile.Width == tile.Width && rawTile.Height == tile.Height)
                    ? (Bitmap)rawTile.Clone()
                    : new Bitmap(tile.Width, tile.Height, PixelFormat.Format24bppRgb);

                if (rawTile.Width != tile.Width || rawTile.Height != tile.Height)
                {
                    upscaledTile.SetResolution(input.HorizontalResolution, input.VerticalResolution);
                    using var g = Graphics.FromImage(upscaledTile);
                    g.CompositingMode = CompositingMode.SourceCopy;
                    g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.DrawImage(rawTile, new Rectangle(0, 0, tile.Width, tile.Height), 0, 0, rawTile.Width, rawTile.Height, GraphicsUnit.Pixel);
                }

                var dTile = upscaledTile.LockBits(new Rectangle(0, 0, tile.Width, tile.Height), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
                try
                {
                    int stride = Math.Abs(dTile.Stride);
                    byte[] pix = new byte[stride * tile.Height];
                    Marshal.Copy(dTile.Scan0, pix, 0, pix.Length);

                    int overlap = TileOverlapMargin;
                    for (int ty = 0; ty < tile.Height; ty++)
                    {
                        int gy = tile.Y + ty;
                        if (gy >= H) break;

                        float wy = (tile.Height > overlap)
                            ? (ty < overlap ? (float)ty / overlap : (ty > tile.Height - overlap ? (float)(tile.Height - ty) / overlap : 1.0f))
                            : 1.0f;
                        int rOff = ty * stride;

                        for (int tx = 0; tx < tile.Width; tx++)
                        {
                            int gx = tile.X + tx;
                            if (gx >= W) break;

                            float wx = (tile.Width > overlap)
                                ? (tx < overlap ? (float)tx / overlap : (tx > tile.Width - overlap ? (float)(tile.Width - tx) / overlap : 1.0f))
                                : 1.0f;
                            float weight = Math.Max(0.01f, wx * wy);

                            int p = rOff + (tx * 3);
                            int gIdx = (gy * W) + gx;

                            accumB[gIdx] += pix[p] * weight;
                            accumG[gIdx] += pix[p + 1] * weight;
                            accumR[gIdx] += pix[p + 2] * weight;
                            accumW[gIdx] += weight;
                        }
                    }
                }
                finally
                {
                    upscaledTile.UnlockBits(dTile);
                }
            }

            // Final stitch onto globalComposite
            var dGlobal = globalComposite.LockBits(new Rectangle(0, 0, W, H), ImageLockMode.ReadWrite, PixelFormat.Format24bppRgb);
            var dMask = mask.LockBits(new Rectangle(0, 0, W, H), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);

            try
            {
                int s = Math.Abs(dGlobal.Stride);
                int sMask = Math.Abs(dMask.Stride);
                byte[] globPix = new byte[s * H];
                byte[] maskPix = new byte[sMask * H];
                Marshal.Copy(dGlobal.Scan0, globPix, 0, globPix.Length);
                Marshal.Copy(dMask.Scan0, maskPix, 0, maskPix.Length);

                for (int y = 0; y < H; y++)
                {
                    int rOff = y * s;
                    int rMask = y * sMask;
                    for (int x = 0; x < W; x++)
                    {
                        int p = rOff + (x * 3);
                        int pMask = rMask + (x * 3);
                        int gIdx = (y * W) + x;

                        bool isMasked = Math.Max(maskPix[pMask], Math.Max(maskPix[pMask + 1], maskPix[pMask + 2])) > 128;
                        if (isMasked && accumW[gIdx] > 0)
                        {
                            float w = accumW[gIdx];
                            byte locB = (byte)Math.Clamp((int)Math.Round(accumB[gIdx] / w), 0, 255);
                            byte locG = (byte)Math.Clamp((int)Math.Round(accumG[gIdx] / w), 0, 255);
                            byte locR = (byte)Math.Clamp((int)Math.Round(accumR[gIdx] / w), 0, 255);

                            globPix[p] = (byte)Math.Clamp((int)Math.Round(DefaultLocalCropWeight * locB + DefaultGlobalContextWeight * globPix[p]), 0, 255);
                            globPix[p + 1] = (byte)Math.Clamp((int)Math.Round(DefaultLocalCropWeight * locG + DefaultGlobalContextWeight * globPix[p + 1]), 0, 255);
                            globPix[p + 2] = (byte)Math.Clamp((int)Math.Round(DefaultLocalCropWeight * locR + DefaultGlobalContextWeight * globPix[p + 2]), 0, 255);
                        }
                    }
                }

                Marshal.Copy(globPix, 0, dGlobal.Scan0, globPix.Length);
            }
            finally
            {
                globalComposite.UnlockBits(dGlobal);
                mask.UnlockBits(dMask);
            }

            return globalComposite;
        }
        catch
        {
            globalComposite.Dispose();
            throw;
        }
    }
}
