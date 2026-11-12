using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Nexus.Contracts;
using Nexus.Foundation.Registry;

namespace Nexus.Engine.ImageSubsystem.Pipelines;

/// <summary>
/// Pipeline headless untuk preprocessing, inferensi, dan postprocessing
/// kapabilitas penghapusan latar belakang.
/// </summary>
public sealed class RemoveBackgroundPipeline
{
    private static readonly float[] Mean = [0.485f, 0.456f, 0.406f];
    private static readonly float[] StandardDeviation = [0.229f, 0.224f, 0.225f];

    private readonly IModelSelectionPolicy _modelSelectionPolicy;
    private readonly IGenericImageEngine _imageEngine;
    private readonly Func<ResolvedModelDescriptor?>? _fallbackModelResolver;

    public RemoveBackgroundPipeline(
        IModelSelectionPolicy modelSelectionPolicy,
        IGenericImageEngine imageEngine,
        Func<ResolvedModelDescriptor?>? fallbackModelResolver = null)
    {
        _modelSelectionPolicy = modelSelectionPolicy
            ?? throw new ArgumentNullException(nameof(modelSelectionPolicy));
        _imageEngine = imageEngine
            ?? throw new ArgumentNullException(nameof(imageEngine));
        _fallbackModelResolver = fallbackModelResolver;
    }

    /// <summary>
    /// Menghasilkan bitmap ARGB baru. Bitmap input tetap dimiliki caller.
    /// </summary>
    public async Task<Bitmap> ExecuteAsync(
        Bitmap input,
        EngineSessionOptions? sessionOptions = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        await using var jobScope = new PipelineJobScope(cancellationToken);
        var jobToken = jobScope.CancellationToken;
        jobToken.ThrowIfCancellationRequested();

        var options = sessionOptions ?? new EngineSessionOptions();
        var descriptor = _modelSelectionPolicy.SelectModel(
            AICapability.BackgroundRemovalGeneral,
            options);

        try
        {
            return await ExecuteCoreAsync(input, descriptor, options, jobToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (!jobToken.IsCancellationRequested &&
                                  string.Equals(descriptor?.ModelId, "PhotosSeg", StringComparison.OrdinalIgnoreCase))
        {
            System.Diagnostics.Trace.WriteLine(
                $"[WARN] PhotosSeg execution failed ({ex.Message}). Attempting single-point fallback to MVANet.");

            var fallbackDescriptor = _fallbackModelResolver?.Invoke();
            if (fallbackDescriptor != null)
            {
                return await ExecuteCoreAsync(input, fallbackDescriptor, options, jobToken).ConfigureAwait(false);
            }

            throw;
        }
    }

    private async Task<Bitmap> ExecuteCoreAsync(
        Bitmap input,
        ResolvedModelDescriptor descriptor,
        EngineSessionOptions options,
        CancellationToken jobToken)
    {
        ValidateDescriptor(descriptor);

        var swTotal = System.Diagnostics.Stopwatch.StartNew();
        const int defaultDynamicDimension = 1024;
        var height = descriptor.InputShape![2] > 0
            ? checked((int)descriptor.InputShape[2])
            : defaultDynamicDimension;
        var width = descriptor.InputShape[3] > 0
            ? checked((int)descriptor.InputShape[3])
            : defaultDynamicDimension;
        var isPhotosSeg = string.Equals(descriptor.ModelId, "PhotosSeg", StringComparison.OrdinalIgnoreCase);

        var swPreprocess = System.Diagnostics.Stopwatch.StartNew();
        var tensor = await Task.Run(
            () => CreateNormalizedNchw(input, width, height, bypassStandardization: isPhotosSeg),
            jobToken).ConfigureAwait(false);
        swPreprocess.Stop();

        var swSession = System.Diagnostics.Stopwatch.StartNew();
        var sessionKey = await _imageEngine
            .LoadModelAsync(descriptor, options)
            .ConfigureAwait(false);
        swSession.Stop();

        long inferenceMs = 0;
        long postprocessMs = 0;
        long unloadMs = 0;
        try
        {
            jobToken.ThrowIfCancellationRequested();

            var swInference = System.Diagnostics.Stopwatch.StartNew();
            var outputs = await _imageEngine
                .ExecuteInferenceAsync(
                    sessionKey,
                    new Dictionary<string, object>(StringComparer.Ordinal)
                    {
                        [descriptor.InputTensorName!] = tensor
                    })
                .ConfigureAwait(false);
            swInference.Stop();
            inferenceMs = swInference.ElapsedMilliseconds;

            jobToken.ThrowIfCancellationRequested();

            if (!outputs.TryGetValue(descriptor.OutputTensorName!, out var output) || output is null)
            {
                throw new InvalidDataException(
                    $"Output tensor '{descriptor.OutputTensorName}' was not found.");
            }

            float[,,,] alphaTensor;
            if (output is float[,,,] nchw)
            {
                alphaTensor = nchw;
            }
            else if (output is float[,,] chw && chw.GetLength(0) == 1)
            {
                // PhotosSeg returns [1, H, W] -> map into [1, 1, H, W]
                var maskH = chw.GetLength(1);
                var maskW = chw.GetLength(2);
                alphaTensor = new float[1, 1, maskH, maskW];
                Buffer.BlockCopy(chw, 0, alphaTensor, 0, chw.Length * sizeof(float));
            }
            else
            {
                throw new InvalidDataException(
                    $"Output tensor '{descriptor.OutputTensorName}' was not a float32 NCHW or [1,H,W] tensor.");
            }

            var swPost = System.Diagnostics.Stopwatch.StartNew();
            var result = await Task.Run(
                () => ApplyAlphaMask(input, alphaTensor),
                jobToken).ConfigureAwait(false);
            swPost.Stop();
            postprocessMs = swPost.ElapsedMilliseconds;

            return result;
        }
        finally
        {
            var swUnload = System.Diagnostics.Stopwatch.StartNew();
            await _imageEngine
                .UnloadModelAsync(sessionKey)
                .ConfigureAwait(false);
            swUnload.Stop();
            unloadMs = swUnload.ElapsedMilliseconds;
            swTotal.Stop();

            System.Diagnostics.Trace.WriteLine(
                $"[PERF_AUDIT] Pipeline: Model={descriptor.ModelId} InputRes={width}x{height} " +
                $"Preprocess={swPreprocess.ElapsedMilliseconds}ms SessionLoad={swSession.ElapsedMilliseconds}ms " +
                $"Inference={inferenceMs}ms Postprocess={postprocessMs}ms Unload={unloadMs}ms " +
                $"TotalPipeline={swTotal.ElapsedMilliseconds}ms");
        }
    }

    private static void ValidateDescriptor(ResolvedModelDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        if (descriptor.ModelType != ModelType.Onnx)
        {
            throw new InvalidDataException(
                "Background removal requires an ONNX model descriptor.");
        }

        if (!string.Equals(descriptor.TensorType, "float32", StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException(
                $"Tensor type '{descriptor.TensorType}' is not supported.");
        }

        if (string.IsNullOrWhiteSpace(descriptor.InputTensorName) ||
            string.IsNullOrWhiteSpace(descriptor.OutputTensorName))
        {
            throw new InvalidDataException(
                "The model descriptor must define input and output tensor names.");
        }

        if (descriptor.InputShape is not [1, 3, > 0 or -1, > 0 or -1])
        {
            throw new InvalidDataException(
                "Background removal requires an NCHW input shape [1,3,H,W] with positive or dynamic spatial dimensions.");
        }
    }

    private static float[,,,] CreateNormalizedNchw(
        Bitmap input,
        int width,
        int height,
        bool bypassStandardization = false)
    {
        using var resized = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        using (var graphics = Graphics.FromImage(resized))
        {
            graphics.CompositingMode = CompositingMode.SourceCopy;
            graphics.InterpolationMode = InterpolationMode.HighQualityBilinear;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.DrawImage(input, 0, 0, width, height);
        }

        var tensor = new float[1, 3, height, width];
        var rectangle = new Rectangle(0, 0, width, height);
        var data = resized.LockBits(
            rectangle,
            ImageLockMode.ReadOnly,
            PixelFormat.Format24bppRgb);
        try
        {
            var stride = Math.Abs(data.Stride);
            var pixels = new byte[stride * height];
            Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);

            for (var y = 0; y < height; y++)
            {
                var row = data.Stride >= 0
                    ? y * stride
                    : (height - 1 - y) * stride;
                for (var x = 0; x < width; x++)
                {
                    var pixel = row + (x * 3);
                    if (bypassStandardization)
                    {
                        // PhotosSeg contains internal ImageNet Sub/Div normalization in ONNX graph
                        tensor[0, 0, y, x] = pixels[pixel + 2] / 255f;
                        tensor[0, 1, y, x] = pixels[pixel + 1] / 255f;
                        tensor[0, 2, y, x] = pixels[pixel] / 255f;
                    }
                    else
                    {
                        tensor[0, 0, y, x] =
                            ((pixels[pixel + 2] / 255f) - Mean[0]) / StandardDeviation[0];
                        tensor[0, 1, y, x] =
                            ((pixels[pixel + 1] / 255f) - Mean[1]) / StandardDeviation[1];
                        tensor[0, 2, y, x] =
                            ((pixels[pixel] / 255f) - Mean[2]) / StandardDeviation[2];
                    }
                }
            }
        }
        finally
        {
            resized.UnlockBits(data);
        }

        return tensor;
    }

    private static Bitmap ApplyAlphaMask(Bitmap input, float[,,,] alphaTensor)
    {
        if (alphaTensor.GetLength(0) != 1 ||
            alphaTensor.GetLength(1) != 1 ||
            alphaTensor.GetLength(2) <= 0 ||
            alphaTensor.GetLength(3) <= 0)
        {
            throw new InvalidDataException(
                "Background removal requires an NCHW output shape [1,1,H,W].");
        }

        var maskHeight = alphaTensor.GetLength(2);
        var maskWidth = alphaTensor.GetLength(3);
        var requiresSigmoid = RequiresSigmoid(alphaTensor);

        var result = new Bitmap(input.Width, input.Height, PixelFormat.Format32bppArgb);
        try
        {
            using (var graphics = Graphics.FromImage(result))
            {
                graphics.CompositingMode = CompositingMode.SourceCopy;
                graphics.DrawImageUnscaled(input, 0, 0);
            }

            var rectangle = new Rectangle(0, 0, result.Width, result.Height);
            var data = result.LockBits(
                rectangle,
                ImageLockMode.ReadWrite,
                PixelFormat.Format32bppArgb);
            try
            {
                var stride = Math.Abs(data.Stride);
                unsafe
                {
                    byte* scan0 = (byte*)data.Scan0.ToPointer();
                    for (var y = 0; y < result.Height; y++)
                    {
                        var sourceY = result.Height == 1
                            ? 0
                            : y * (maskHeight - 1f) / (result.Height - 1f);
                        var y0 = (int)Math.Floor(sourceY);
                        var y1 = Math.Min(maskHeight - 1, y0 + 1);
                        var fy = sourceY - y0;
                        var row = data.Stride >= 0
                            ? y * stride
                            : (result.Height - 1 - y) * stride;

                        for (var x = 0; x < result.Width; x++)
                        {
                            var sourceX = result.Width == 1
                                ? 0
                                : x * (maskWidth - 1f) / (result.Width - 1f);
                            var x0 = (int)Math.Floor(sourceX);
                            var x1 = Math.Min(maskWidth - 1, x0 + 1);
                            var fx = sourceX - x0;
                            var top =
                                alphaTensor[0, 0, y0, x0] * (1f - fx) +
                                alphaTensor[0, 0, y0, x1] * fx;
                            var bottom =
                                alphaTensor[0, 0, y1, x0] * (1f - fx) +
                                alphaTensor[0, 0, y1, x1] * fx;
                            var value = top * (1f - fy) + bottom * fy;
                            var probability = requiresSigmoid
                                ? Sigmoid(value)
                                : Math.Clamp(value, 0f, 1f);
                            scan0[row + (x * 4) + 3] =
                                (byte)Math.Clamp(
                                    (int)Math.Round(probability * 255f),
                                    0,
                                    255);
                        }
                    }
                }
            }
            finally
            {
                result.UnlockBits(data);
            }

            return result;
        }
        catch
        {
            result.Dispose();
            throw;
        }
    }

    private static bool RequiresSigmoid(float[,,,] tensor)
    {
        foreach (var value in tensor)
        {
            if (value is < 0f or > 1f)
            {
                return true;
            }
        }

        return false;
    }

    private static float Sigmoid(float value) =>
        value >= 0f
            ? 1f / (1f + MathF.Exp(-value))
            : MathF.Exp(value) / (1f + MathF.Exp(value));

    /// <summary>
    /// Mengekstrak alpha channel (A) dari Format32bppArgb Bitmap menjadi array byte 1D.
    /// </summary>
    public static byte[] ExtractAlpha(Bitmap argb)
    {
        ArgumentNullException.ThrowIfNull(argb);
        int width = argb.Width;
        int height = argb.Height;
        var alpha = new byte[width * height];
        var rectangle = new Rectangle(0, 0, width, height);
        var data = argb.LockBits(
            rectangle,
            ImageLockMode.ReadOnly,
            PixelFormat.Format32bppArgb);
        try
        {
            var stride = Math.Abs(data.Stride);
            unsafe
            {
                byte* scan0 = (byte*)data.Scan0.ToPointer();
                for (var y = 0; y < height; y++)
                {
                    var row = data.Stride >= 0
                        ? y * stride
                        : (height - 1 - y) * stride;
                    var alphaRow = y * width;
                    for (var x = 0; x < width; x++)
                    {
                        alpha[alphaRow + x] = scan0[row + (x * 4) + 3];
                    }
                }
            }
        }
        finally
        {
            argb.UnlockBits(data);
        }

        return alpha;
    }

    /// <summary>
    /// Memperbarui channel alpha pada target Format32bppArgb Bitmap dari array byte.
    /// Mendukung pembaruan full image maupun ROI terlokalisasi tanpa memengaruhi channel RGB.
    /// </summary>
    public static void UpdateAlpha(Bitmap targetArgb, byte[] alpha, Rectangle? roi = null)
    {
        ArgumentNullException.ThrowIfNull(targetArgb);
        ArgumentNullException.ThrowIfNull(alpha);

        var targetRect = roi ?? new Rectangle(0, 0, targetArgb.Width, targetArgb.Height);
        targetRect.Intersect(new Rectangle(0, 0, targetArgb.Width, targetArgb.Height));
        if (targetRect.Width <= 0 || targetRect.Height <= 0)
        {
            return;
        }

        var data = targetArgb.LockBits(
            targetRect,
            ImageLockMode.WriteOnly,
            PixelFormat.Format32bppArgb);
        try
        {
            var stride = Math.Abs(data.Stride);
            bool isFullAlpha = alpha.Length == targetArgb.Width * targetArgb.Height;
            bool isRoiAlpha = alpha.Length == targetRect.Width * targetRect.Height;

            if (!isFullAlpha && !isRoiAlpha)
            {
                throw new ArgumentException(
                    $"Alpha array length {alpha.Length} does not match bitmap dimensions {targetArgb.Width}x{targetArgb.Height} or ROI {targetRect.Width}x{targetRect.Height}.",
                    nameof(alpha));
            }

            unsafe
            {
                byte* scan0 = (byte*)data.Scan0.ToPointer();
                for (var y = 0; y < targetRect.Height; y++)
                {
                    var row = data.Stride >= 0
                        ? y * stride
                        : (targetRect.Height - 1 - y) * stride;

                    for (var x = 0; x < targetRect.Width; x++)
                    {
                        byte a = isFullAlpha
                            ? alpha[(targetRect.Y + y) * targetArgb.Width + (targetRect.X + x)]
                            : alpha[y * targetRect.Width + x];

                        scan0[row + (x * 4) + 3] = a;
                    }
                }
            }
        }
        finally
        {
            targetArgb.UnlockBits(data);
        }
    }

    /// <summary>
    /// Menghitung ROI yang di-clamp secara aman ke batas gambar dengan padding tambahan.
    /// </summary>
    public static Rectangle CalculateStrokeRoi(Rectangle strokeBounds, int padding, int imageWidth, int imageHeight)
    {
        if (imageWidth <= 0 || imageHeight <= 0)
        {
            return Rectangle.Empty;
        }

        int minX = Math.Max(0, strokeBounds.Left - padding);
        int minY = Math.Max(0, strokeBounds.Top - padding);
        int maxX = Math.Min(imageWidth, strokeBounds.Right + padding);
        int maxY = Math.Min(imageHeight, strokeBounds.Bottom + padding);

        int width = Math.Max(0, maxX - minX);
        int height = Math.Max(0, maxY - minY);

        return new Rectangle(minX, minY, width, height);
    }

    /// <summary>
    /// Menerapkan coretan kuas lokal (Add atau Remove) ke activeMask dalam ROI tertentu
    /// dan menghasilkan AlphaMaskPatch untuk mendukung Undo/Redo tanpa re-inferensi AI.
    /// </summary>
    public static AlphaMaskPatch ApplyStroke(
        byte[] activeMask,
        int imageWidth,
        int imageHeight,
        Rectangle roi,
        byte[] localStrokeMask,
        bool isAdd)
    {
        ArgumentNullException.ThrowIfNull(activeMask);
        ArgumentNullException.ThrowIfNull(localStrokeMask);

        if (roi.Width <= 0 || roi.Height <= 0)
        {
            return new AlphaMaskPatch(roi, [], []);
        }

        int roiSize = checked(roi.Width * roi.Height);
        if (localStrokeMask.Length != roiSize)
        {
            throw new ArgumentException($"localStrokeMask length {localStrokeMask.Length} does not match ROI size {roiSize}.", nameof(localStrokeMask));
        }

        var oldAlpha = new byte[roiSize];
        var newAlpha = new byte[roiSize];

        for (int y = 0; y < roi.Height; y++)
        {
            int imgY = roi.Y + y;
            int imgRow = imgY * imageWidth + roi.X;
            int roiRow = y * roi.Width;

            for (int x = 0; x < roi.Width; x++)
            {
                byte oldVal = activeMask[imgRow + x];
                byte strokeVal = localStrokeMask[roiRow + x];
                byte newVal;

                if (isAdd)
                {
                    newVal = (byte)Math.Clamp(oldVal + strokeVal, 0, 255);
                }
                else
                {
                    newVal = (byte)Math.Clamp(oldVal - strokeVal, 0, 255);
                }

                oldAlpha[roiRow + x] = oldVal;
                newAlpha[roiRow + x] = newVal;
                activeMask[imgRow + x] = newVal;
            }
        }

        return new AlphaMaskPatch(roi, oldAlpha, newAlpha);
    }

    /// <summary>
    /// Menerapkan AlphaMaskPatch ke activeMask (Undo: isRedo=false, Redo: isRedo=true).
    /// </summary>
    public static void ApplyPatch(
        byte[] activeMask,
        int imageWidth,
        int imageHeight,
        AlphaMaskPatch patch,
        bool isRedo)
    {
        ArgumentNullException.ThrowIfNull(activeMask);
        ArgumentNullException.ThrowIfNull(patch);

        var source = isRedo ? patch.NewAlpha : patch.OldAlpha;
        var roi = patch.Bounds;
        if (roi.Width <= 0 || roi.Height <= 0 || source.Length == 0)
        {
            return;
        }

        for (int y = 0; y < roi.Height; y++)
        {
            int imgY = roi.Y + y;
            if (imgY < 0 || imgY >= imageHeight) continue;

            int imgRow = imgY * imageWidth + roi.X;
            int patchRow = y * roi.Width;

            Array.Copy(source, patchRow, activeMask, imgRow, roi.Width);
        }
    }

    /// <summary>
    /// Menghitung jarak Euclidean dari titik p ke segmen garis tertutup a-b.
    /// </summary>
    public static float DistanceToSegment(PointF p, PointF a, PointF b)
    {
        float dx = b.X - a.X;
        float dy = b.Y - a.Y;
        float lenSq = dx * dx + dy * dy;
        if (lenSq < 1e-6f)
        {
            float px = p.X - a.X;
            float py = p.Y - a.Y;
            return MathF.Sqrt(px * px + py * py);
        }

        float t = Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / lenSq, 0f, 1f);
        float projX = a.X + t * dx;
        float projY = a.Y + t * dy;
        float diffX = p.X - projX;
        float diffY = p.Y - projY;
        return MathF.Sqrt(diffX * diffX + diffY * diffY);
    }

    /// <summary>
    /// Menghitung nilai alpha kuas berdasarkan model matematika PillShapedMask &amp; Softness
    /// Microsoft Photos (module 98330) menggunakan kurva cubic Hermite smoothstep.
    /// </summary>
    public static byte ComputePhotosAlpha(float dist, float radius, float softness)
    {
        if (dist > radius || radius <= 0f) return 0;

        float diameter = radius * 2f;
        float normalizedDist = dist / diameter; // rentang [0, 0.5]
        float c = 0.5f * (1.0f - Math.Clamp(softness, 0f, 1f));
        float l = normalizedDist - c;

        if (l <= 0f)
        {
            return 255;
        }

        float range = 0.5f - c;
        float r = range > 1e-5f ? Math.Clamp(l / range, 0f, 1f) : 1f;
        float s = r * r * (3f - 2f * r); // Cubic Hermite smoothstep: s(e, t, n)
        float alpha = (1.0f - s) * 255f;
        return (byte)Math.Clamp(MathF.Round(alpha), 0, 255);
    }

    /// <summary>
    /// Me-rasterisasi serangkaian titik kuas (stroke) menjadi array mask lokal (ukuran ROI)
    /// dengan diameter piksel gambar dan kelembutan tepi kontinu (Microsoft Photos parity).
    /// </summary>
    public static byte[] RasterizePillStroke(
        IReadOnlyList<PointF> points,
        float brushDiameter,
        float softness,
        Rectangle roi)
    {
        ArgumentNullException.ThrowIfNull(points);
        if (roi.Width <= 0 || roi.Height <= 0)
        {
            return [];
        }

        var mask = new byte[roi.Width * roi.Height];
        if (points.Count == 0 || brushDiameter <= 0f)
        {
            return mask;
        }

        float radius = brushDiameter / 2f;

        if (points.Count == 1)
        {
            var pt = points[0];
            int minX = Math.Max(0, (int)MathF.Floor(pt.X - radius) - roi.X);
            int maxX = Math.Min(roi.Width - 1, (int)MathF.Ceiling(pt.X + radius) - roi.X);
            int minY = Math.Max(0, (int)MathF.Floor(pt.Y - radius) - roi.Y);
            int maxY = Math.Min(roi.Height - 1, (int)MathF.Ceiling(pt.Y + radius) - roi.Y);

            for (int y = minY; y <= maxY; y++)
            {
                float py = roi.Y + y + 0.5f;
                int row = y * roi.Width;
                for (int x = minX; x <= maxX; x++)
                {
                    float px = roi.X + x + 0.5f;
                    float d = MathF.Sqrt((px - pt.X) * (px - pt.X) + (py - pt.Y) * (py - pt.Y));
                    byte a = ComputePhotosAlpha(d, radius, softness);
                    if (a > mask[row + x]) mask[row + x] = a;
                }
            }
            return mask;
        }

        for (int i = 0; i < points.Count - 1; i++)
        {
            var p0 = points[i];
            var p1 = points[i + 1];

            int minX = Math.Max(0, (int)MathF.Floor(MathF.Min(p0.X, p1.X) - radius) - roi.X);
            int maxX = Math.Min(roi.Width - 1, (int)MathF.Ceiling(MathF.Max(p0.X, p1.X) + radius) - roi.X);
            int minY = Math.Max(0, (int)MathF.Floor(MathF.Min(p0.Y, p1.Y) - radius) - roi.Y);
            int maxY = Math.Min(roi.Height - 1, (int)MathF.Ceiling(MathF.Max(p0.Y, p1.Y) + radius) - roi.Y);

            for (int y = minY; y <= maxY; y++)
            {
                float py = roi.Y + y + 0.5f;
                int row = y * roi.Width;
                for (int x = minX; x <= maxX; x++)
                {
                    float px = roi.X + x + 0.5f;
                    float d = DistanceToSegment(new PointF(px, py), p0, p1);
                    byte a = ComputePhotosAlpha(d, radius, softness);
                    if (a > mask[row + x]) mask[row + x] = a;
                }
            }
        }

        return mask;
    }

    /// <summary>
    /// Menghasilkan array piksel BGRA32 untuk layer visualisasi mask refinement
    /// (Microsoft Photos MaskLayerVisualizationShader parity):
    /// Area foreground (mask=255) transparan penuh (0,0,0,0) sehingga foto asli terlihat jernih.
    /// Area background yang dihapus (mask=0) diberi aksen warna cyan bergaris diagonal (40% opacity).
    /// </summary>
    public static byte[] CreateMaskVisualization(
        byte[] activeMask,
        int width,
        int height,
        Rectangle? roi = null,
        byte[]? existingBgra = null)
    {
        ArgumentNullException.ThrowIfNull(activeMask);
        if (width <= 0 || height <= 0) return [];

        int totalBytes = checked(width * height * 4);
        var bgra = existingBgra ?? new byte[totalBytes];
        if (bgra.Length != totalBytes)
        {
            bgra = new byte[totalBytes];
        }

        var rect = roi ?? new Rectangle(0, 0, width, height);
        rect.Intersect(new Rectangle(0, 0, width, height));
        if (rect.Width <= 0 || rect.Height <= 0) return bgra;

        for (int y = rect.Y; y < rect.Bottom; y++)
        {
            int rowMask = y * width;
            int rowBgra = y * width * 4;

            for (int x = rect.X; x < rect.Right; x++)
            {
                byte alpha = activeMask[rowMask + x];
                int p = rowBgra + (x * 4);

                if (alpha == 255)
                {
                    // Foreground kept: Completely clear
                    bgra[p + 0] = 0;
                    bgra[p + 1] = 0;
                    bgra[p + 2] = 0;
                    bgra[p + 3] = 0;
                }
                else
                {
                    int removed = 255 - alpha;
                    bool isStripe = ((x + y) / 12) % 2 == 0;

                    // Microsoft Photos cyan palette (#0093F9 and #98ECFE)
                    byte b = isStripe ? (byte)249 : (byte)254;
                    byte g = isStripe ? (byte)147 : (byte)236;
                    byte r = isStripe ? (byte)0 : (byte)152;

                    byte overlayAlpha = (byte)Math.Clamp((int)MathF.Round((removed / 255f) * 105f), 0, 255);

                    bgra[p + 0] = b;
                    bgra[p + 1] = g;
                    bgra[p + 2] = r;
                    bgra[p + 3] = overlayAlpha;
                }
            }
        }

        return bgra;
    }
}

/// <summary>
/// Patch perbedaan alpha mask lokal untuk riwayat Undo/Redo refinement tanpa alokasi memori penuh.
/// </summary>
public sealed record AlphaMaskPatch(Rectangle Bounds, byte[] OldAlpha, byte[] NewAlpha);
