using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Nexus.Engine.ImageSubsystem.Services;

/// <summary>
/// Layanan produksi segmentasi objek interaktif menggunakan model MobileSAM (Acly FP32 ONNX).
/// </summary>
public sealed class MobileSamService : IMobileSamService, IDisposable
{
    /// <summary>
    /// Rasio heuristik awal untuk pembersihan bercak noise terputus (stray specks).
    /// CATATAN AUDIT: Ini adalah nilai heuristik baseline awal, bukan konstanta mutlak final.
    /// Nilai ini menskalakan ambang batas luas piksel secara proporsional terhadap resolusi gambar:
    /// Ambang area = Math.Max(MinNoiseComponentAreaPixels, (int)Math.Round(totalPixels * RelativeNoiseAreaRatio));
    /// Contoh:
    /// - Pada gambar 1080p (~2.07 MP): threshold ~ 62 piksel.
    /// - Pada gambar 4K (~8.29 MP): threshold ~ 248 piksel.
    /// - Pada gambar resolusi rendah (< 1 MP): dibatasi batas bawah minimal 32 piksel.
    /// </summary>
    public const double DefaultRelativeNoiseAreaRatio = 0.00003;

    /// <summary>
    /// Batas bawah minimal area piksel noise yang akan dibuang.
    /// Nilai heuristik awal untuk mencegah pembuangan detail mikroskopis pada gambar beresolusi sangat rendah.
    /// </summary>
    public const int DefaultMinNoiseAreaPixels = 32;

    private readonly string _encoderPath;
    private readonly string _decoderPath;
    private readonly TimeSpan _idleTimeout;
    private readonly int _threads;

    private readonly object _syncLock = new();
    private InferenceSession? _encoderSession;
    private InferenceSession? _decoderSession;
    private System.Threading.Timer? _idleTimer;
    private DateTime _lastAccessTime;
    private bool _isDisposed;

    public bool IsModelAvailable => File.Exists(_encoderPath) && File.Exists(_decoderPath);

    public MobileSamService(
        string? modelsRootFolder = null,
        TimeSpan? idleTimeout = null,
        int? threads = null)
    {
        _idleTimeout = idleTimeout ?? TimeSpan.FromMinutes(5);
        _threads = threads ?? 6;

        var baseFolder = ResolveModelDirectory(modelsRootFolder);
        _encoderPath = Path.Combine(baseFolder, "mobile_sam_image_encoder.onnx");
        _decoderPath = Path.Combine(baseFolder, "sam_mask_decoder_single.onnx");
    }

    private static string ResolveModelDirectory(string? customRoot)
    {
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(customRoot))
        {
            candidates.Add(Path.Combine(customRoot, "SegmentAnything"));
            candidates.Add(customRoot);
        }

        candidates.Add(@"d:\NEXUS\Models\SegmentAnything");
        candidates.Add(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Models", "SegmentAnything"));
        candidates.Add(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "Models", "SegmentAnything"));

        foreach (var c in candidates)
        {
            try
            {
                var full = Path.GetFullPath(c);
                if (Directory.Exists(full) &&
                    File.Exists(Path.Combine(full, "mobile_sam_image_encoder.onnx")) &&
                    File.Exists(Path.Combine(full, "sam_mask_decoder_single.onnx")))
                {
                    return full;
                }
            }
            catch { }
        }

        return @"d:\NEXUS\Models\SegmentAnything";
    }

    private InferenceSession GetOrCreateEncoderSession()
    {
        lock (_syncLock)
        {
            ThrowIfDisposed();
            if (_encoderSession != null)
            {
                TouchSessionLocked();
                return _encoderSession;
            }

            if (!File.Exists(_encoderPath))
            {
                throw new FileNotFoundException($"MobileSAM Encoder ONNX tidak ditemukan di '{_encoderPath}'.", _encoderPath);
            }

            var opts = new SessionOptions
            {
                GraphOptimizationLevel = Microsoft.ML.OnnxRuntime.GraphOptimizationLevel.ORT_ENABLE_ALL,
                IntraOpNumThreads = _threads
            };

            _encoderSession = new InferenceSession(_encoderPath, opts);
            TouchSessionLocked();
            return _encoderSession;
        }
    }

    private InferenceSession GetOrCreateDecoderSession()
    {
        lock (_syncLock)
        {
            ThrowIfDisposed();
            if (_decoderSession != null)
            {
                TouchSessionLocked();
                return _decoderSession;
            }

            if (!File.Exists(_decoderPath))
            {
                throw new FileNotFoundException($"MobileSAM Decoder ONNX tidak ditemukan di '{_decoderPath}'.", _decoderPath);
            }

            var opts = new SessionOptions
            {
                GraphOptimizationLevel = Microsoft.ML.OnnxRuntime.GraphOptimizationLevel.ORT_ENABLE_ALL,
                IntraOpNumThreads = _threads
            };

            _decoderSession = new InferenceSession(_decoderPath, opts);
            TouchSessionLocked();
            return _decoderSession;
        }
    }

    private void TouchSessionLocked()
    {
        _lastAccessTime = DateTime.UtcNow;
        if (_idleTimer == null)
        {
            _idleTimer = new System.Threading.Timer(OnIdleTimerTick, null, _idleTimeout, _idleTimeout);
        }
        else
        {
            _idleTimer.Change(_idleTimeout, _idleTimeout);
        }
    }

    private void OnIdleTimerTick(object? state)
    {
        lock (_syncLock)
        {
            if (_isDisposed) return;
            if (DateTime.UtcNow - _lastAccessTime >= _idleTimeout)
            {
                UnloadSessionsLocked();
            }
        }
    }

    private void UnloadSessionsLocked()
    {
        _encoderSession?.Dispose();
        _encoderSession = null;
        _decoderSession?.Dispose();
        _decoderSession = null;
        _idleTimer?.Dispose();
        _idleTimer = null;
    }

    /// <inheritdoc />
    public async Task<MobileSamEmbeddingSession> EncodeImageAsync(
        Bitmap image,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(image);

        // KONFIRMASI AUDIT: Dijalankan sepenuhnya di Task.Run (background thread pool)
        // agar tidak memblokir UI Dispatcher thread sama sekali.
        return await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            int origW = image.Width;
            int origH = image.Height;

            // 1. Skala gambar menjaga aspect ratio agar sisi terpanjang maksimal 1024
            float scale = 1024f / Math.Max(origW, origH);
            int targetW = (int)Math.Round(origW * scale);
            int targetH = (int)Math.Round(origH * scale);

            using var resizedBmp = new Bitmap(targetW, targetH, PixelFormat.Format24bppRgb);
            using (var g = Graphics.FromImage(resizedBmp))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                lock (image)
                {
                    g.DrawImage(image, 0, 0, targetW, targetH);
                }
            }

            cancellationToken.ThrowIfCancellationRequested();

            // 2. Buat tensor RGB [targetH, targetW, 3] dalam rentang [0..255]
            var inputTensor = new DenseTensor<float>(new[] { targetH, targetW, 3 });
            var bmpData = resizedBmp.LockBits(new Rectangle(0, 0, targetW, targetH), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
            try
            {
                int stride = Math.Abs(bmpData.Stride);
                byte[] bytes = new byte[stride * targetH];
                System.Runtime.InteropServices.Marshal.Copy(bmpData.Scan0, bytes, 0, bytes.Length);

                for (int y = 0; y < targetH; y++)
                {
                    int rOff = y * stride;
                    for (int x = 0; x < targetW; x++)
                    {
                        int p = rOff + (x * 3);
                        // GDI+ 24bppRgb adalah BGR: bytes[p]=B, bytes[p+1]=G, bytes[p+2]=R
                        inputTensor[y, x, 0] = bytes[p + 2]; // R
                        inputTensor[y, x, 1] = bytes[p + 1]; // G
                        inputTensor[y, x, 2] = bytes[p];     // B
                    }
                }
            }
            finally
            {
                resizedBmp.UnlockBits(bmpData);
            }

            cancellationToken.ThrowIfCancellationRequested();

            // 3. Jalankan Image Encoder ONNX
            var encoder = GetOrCreateEncoderSession();
            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor("input_image", inputTensor)
            };

            Tensor<float> embeddingsClone;
            lock (encoder)
            {
                using var results = encoder.Run(inputs);
                var emb = results.First(o => o.Name == "image_embeddings").AsTensor<float>();
                embeddingsClone = emb.Clone();
            }

            return new MobileSamEmbeddingSession(embeddingsClone, origW, origH, scale);
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Bitmap> SegmentPointAsync(
        MobileSamEmbeddingSession session,
        PointF pointInOriginalImage,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(session);

        return await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            int origW = session.OriginalWidth;
            int origH = session.OriginalHeight;
            float scale = session.Scale;

            // Koordinat ditransformasi ke ruang representasi 1024-space
            float ptX = pointInOriginalImage.X * scale;
            float ptY = pointInOriginalImage.Y * scale;

            var coordsTensor = new DenseTensor<float>(new float[] { ptX, ptY }, new[] { 1, 1, 2 });
            var labelsTensor = new DenseTensor<float>(new float[] { 1f }, new[] { 1, 1 }); // 1 = Foreground prompt
            var maskInputTensor = new DenseTensor<float>(new[] { 1, 1, 256, 256 });
            var hasMaskTensor = new DenseTensor<float>(new float[] { 0f }, new[] { 1 });
            var origImSizeTensor = new DenseTensor<float>(new float[] { origH, origW }, new[] { 2 });

            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor("image_embeddings", session.Embeddings),
                NamedOnnxValue.CreateFromTensor("point_coords", coordsTensor),
                NamedOnnxValue.CreateFromTensor("point_labels", labelsTensor),
                NamedOnnxValue.CreateFromTensor("mask_input", maskInputTensor),
                NamedOnnxValue.CreateFromTensor("has_mask_input", hasMaskTensor),
                NamedOnnxValue.CreateFromTensor("orig_im_size", origImSizeTensor)
            };

            cancellationToken.ThrowIfCancellationRequested();

            var decoder = GetOrCreateDecoderSession();
            Tensor<float> masksTensor;
            lock (decoder)
            {
                using var results = decoder.Run(inputs);
                masksTensor = results.First(o => o.Name == "masks").AsTensor<float>().Clone();
            }

            cancellationToken.ThrowIfCancellationRequested();

            // Ekstrak mask biner awal pada resolusi asli
            byte[] rawMask = new byte[origW * origH];
            for (int y = 0; y < origH; y++)
            {
                int row = y * origW;
                for (int x = 0; x < origW; x++)
                {
                    if (masksTensor[0, 0, y, x] > 0.0f)
                    {
                        rawMask[row + x] = 255;
                    }
                }
            }

            // Post-processing: Filter komponen terputus (stray specks / noise)
            var (cleanedMask, _) = FilterDetachedSmallComponents(
                rawMask,
                origW,
                origH,
                pointInOriginalImage,
                DefaultRelativeNoiseAreaRatio,
                DefaultMinNoiseAreaPixels);

            // Bangun System.Drawing.Bitmap 24bppRgb (Format standar InpaintPipeline)
            var maskBmp = new Bitmap(origW, origH, PixelFormat.Format24bppRgb);
            var outData = maskBmp.LockBits(new Rectangle(0, 0, origW, origH), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
            try
            {
                int s = Math.Abs(outData.Stride);
                byte[] outPix = new byte[s * origH];

                for (int y = 0; y < origH; y++)
                {
                    int rOff = y * s;
                    int mOff = y * origW;
                    for (int x = 0; x < origW; x++)
                    {
                        byte val = cleanedMask[mOff + x];
                        int p = rOff + (x * 3);
                        outPix[p] = val;
                        outPix[p + 1] = val;
                        outPix[p + 2] = val;
                    }
                }
                System.Runtime.InteropServices.Marshal.Copy(outPix, 0, outData.Scan0, outPix.Length);
            }
            finally
            {
                maskBmp.UnlockBits(outData);
            }

            return maskBmp;
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Membersihkan komponen piksel terpisah yang sangat kecil (noise specks) menggunakan
    /// Connected Component Analysis (CCA) adaptif terhadap resolusi citra.
    /// Komponen yang memuat titik klik pengguna dijamin tidak akan pernah dihapus.
    /// </summary>
    public static (byte[] CleanedMask, int RemovedComponentsCount) FilterDetachedSmallComponents(
        byte[] srcMask,
        int width,
        int height,
        PointF primaryClickPoint,
        double relativeRatio = DefaultRelativeNoiseAreaRatio,
        int minPixels = DefaultMinNoiseAreaPixels)
    {
        int totalPixels = width * height;
        // Skalakan ambang batas piksel secara proporsional terhadap ukuran resolusi citra
        int minAreaThreshold = Math.Max(minPixels, (int)Math.Round(totalPixels * relativeRatio));

        byte[] output = (byte[])srcMask.Clone();
        bool[] visited = new bool[width * height];
        int removedCount = 0;

        int clickX = Math.Clamp((int)Math.Round(primaryClickPoint.X), 0, width - 1);
        int clickY = Math.Clamp((int)Math.Round(primaryClickPoint.Y), 0, height - 1);
        int clickIndex = clickY * width + clickX;

        int[] dx = { -1, 1, 0, 0 };
        int[] dy = { 0, 0, -1, 1 };

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int idx = y * width + x;
                if (srcMask[idx] == 0 || visited[idx]) continue;

                var componentIndices = new List<int>();
                var queue = new Queue<int>();
                queue.Enqueue(idx);
                visited[idx] = true;
                bool containsClickPoint = false;

                while (queue.Count > 0)
                {
                    int curr = queue.Dequeue();
                    componentIndices.Add(curr);
                    if (curr == clickIndex)
                    {
                        containsClickPoint = true;
                    }

                    int cx = curr % width;
                    int cy = curr / width;

                    for (int k = 0; k < 4; k++)
                    {
                        int nx = cx + dx[k];
                        int ny = cy + dy[k];
                        if (nx >= 0 && nx < width && ny >= 0 && ny < height)
                        {
                            int nIdx = ny * width + nx;
                            if (srcMask[nIdx] > 0 && !visited[nIdx])
                            {
                                visited[nIdx] = true;
                                queue.Enqueue(nIdx);
                            }
                        }
                    }
                }

                // Jika komponen tidak memuat titik klik DAN luasnya di bawah ambang batas heuristik, buang sebagai noise
                if (!containsClickPoint && componentIndices.Count < minAreaThreshold)
                {
                    removedCount++;
                    foreach (var p in componentIndices)
                    {
                        output[p] = 0;
                    }
                }
            }
        }

        return (output, removedCount);
    }

    private void ThrowIfDisposed()
    {
        if (_isDisposed)
        {
            throw new ObjectDisposedException(nameof(MobileSamService));
        }
    }

    public void Dispose()
    {
        lock (_syncLock)
        {
            if (_isDisposed) return;
            _isDisposed = true;
            UnloadSessionsLocked();
        }
    }
}
