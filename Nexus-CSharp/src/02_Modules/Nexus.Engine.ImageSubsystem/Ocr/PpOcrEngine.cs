using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Nexus.Engine.ImageSubsystem.Ocr;

/// <summary>
/// Mesin inferensi OCR lokal berbasis PP-OCRv6 ONNX (DBNet Detection + SVTR CTC Recognition).
/// Menerjemahkan gambar desain cetak resolusi tinggi menjadi daftar OcrBoxModel dengan koordinat native.
/// </summary>
public sealed class PpOcrEngine : IDisposable
{
    private readonly InferenceSession _detSession;
    private readonly InferenceSession _recSession;
    private readonly List<string> _vocab;
    private readonly object _executionLock = new();
    private bool _isDisposed;

    public const string DefaultModelDirectory = @"D:\NEXUS\Models\AI Workspace\PP-OCRv6";
    public const string DefaultDetModelName = "ppocrv6_medium_det.onnx";
    public const string DefaultRecModelName = "ppocrv6_medium_rec.onnx";
    public const string DefaultYamlDictName = "rec_inference.yml";
    public const string DefaultTxtDictName = "ppocr_keys_v1.txt";

    public int VocabCount => _vocab.Count;

    public PpOcrEngine(string? modelDir = null, int numThreads = 6)
    {
        string dir = modelDir ?? DefaultModelDirectory;
        if (!Directory.Exists(dir))
            throw new DirectoryNotFoundException($"Direktori model PP-OCRv6 tidak ditemukan di: {dir}");

        string detPath = Path.Combine(dir, DefaultDetModelName);
        string recPath = Path.Combine(dir, DefaultRecModelName);

        if (!File.Exists(detPath))
            throw new FileNotFoundException($"Model deteksi ONNX tidak ditemukan: {detPath}");
        if (!File.Exists(recPath))
            throw new FileNotFoundException($"Model rekognisi ONNX tidak ditemukan: {recPath}");

        var sessionOptions = new SessionOptions
        {
            IntraOpNumThreads = numThreads,
            ExecutionMode = ExecutionMode.ORT_SEQUENTIAL
        };

        _detSession = new InferenceSession(detPath, sessionOptions);
        _recSession = new InferenceSession(recPath, sessionOptions);

        _vocab = LoadVocabulary(dir);
    }

    public PpOcrEngine(string detModelPath, string recModelPath, string vocabPath, int numThreads = 6)
    {
        if (!File.Exists(detModelPath))
            throw new FileNotFoundException($"Model deteksi ONNX tidak ditemukan: {detModelPath}");
        if (!File.Exists(recModelPath))
            throw new FileNotFoundException($"Model rekognisi ONNX tidak ditemukan: {recModelPath}");
        if (!File.Exists(vocabPath))
            throw new FileNotFoundException($"Kamus karakter tidak ditemukan: {vocabPath}");

        var sessionOptions = new SessionOptions
        {
            IntraOpNumThreads = numThreads,
            ExecutionMode = ExecutionMode.ORT_SEQUENTIAL
        };

        _detSession = new InferenceSession(detModelPath, sessionOptions);
        _recSession = new InferenceSession(recModelPath, sessionOptions);

        _vocab = LoadVocabularyFromFile(vocabPath);
    }

    private static List<string> LoadVocabulary(string dir)
    {
        string txtPath = Path.Combine(dir, DefaultTxtDictName);
        if (File.Exists(txtPath))
        {
            return LoadVocabularyFromFile(txtPath);
        }

        string ymlPath = Path.Combine(dir, DefaultYamlDictName);
        if (File.Exists(ymlPath))
        {
            return LoadVocabularyFromFile(ymlPath);
        }

        throw new FileNotFoundException($"Kamus karakter tidak ditemukan di {dir} (dicari .txt atau .yml).");
    }

    public static List<string> LoadVocabularyFromFile(string vocabPath)
    {
        var vocab = new List<string>();
        var lines = File.ReadAllLines(vocabPath);

        if (vocabPath.EndsWith(".yml", StringComparison.OrdinalIgnoreCase) ||
            vocabPath.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase))
        {
            bool inDict = false;
            foreach (var line in lines)
            {
                if (line.StartsWith("  character_dict:"))
                {
                    inDict = true;
                    continue;
                }

                if (inDict)
                {
                    if (line.StartsWith("  - ") || line.StartsWith("  -"))
                    {
                        string val = line.Substring(4).Trim();
                        if (val.StartsWith("'") && val.EndsWith("'") && val.Length >= 2)
                        {
                            val = val.Substring(1, val.Length - 2);
                        }
                        vocab.Add(val);
                    }
                    else if (!line.StartsWith("  "))
                    {
                        break;
                    }
                }
            }
        }
        else
        {
            foreach (var line in lines)
            {
                if (!string.IsNullOrEmpty(line))
                {
                    vocab.Add(line);
                }
            }
        }

        return vocab;
    }

    /// <summary>
    /// Menentukan apakah teks bermakna (memiliki minimal 2 karakter alfanumerik Latin dan tidak didominasi noise visual non-Latin).
    /// Membuang box dengan teks kosong, panjang &lt;= 1 karakter, simbol murni, atau karakter China/non-Latin dari noise grafis daun/ornamen (BUG 3).
    /// </summary>
    public static bool IsMeaningfulText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        int latinAlnumCount = 0;
        int nonLatinLetterCount = 0;

        foreach (char c in text)
        {
            if (char.IsWhiteSpace(c)) continue;

            // Karakter Latin dasar & angka [a-zA-Z0-9] atau Latin-1 Suplemen
            if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') ||
                (c >= 0x00C0 && c <= 0x024F))
            {
                latinAlnumCount++;
            }
            else if (char.IsLetter(c))
            {
                // Huruf di luar alfabet Latin (seperti CJK / Kanji / Hanzi / Cyrillic)
                nonLatinLetterCount++;
            }
        }

        // Harus memiliki minimal 2 karakter alfanumerik Latin
        if (latinAlnumCount < 2)
            return false;

        // Pemeriksaan kewajaran bahasa (BUG 3): jika karakter non-Latin > 30% dari total huruf,
        // buang sebagai noise visual / artefak tekstur daun PP-OCRv6.
        int totalLetters = latinAlnumCount + nonLatinLetterCount;
        if (totalLetters > 0 && ((float)nonLatinLetterCount / totalLetters) > 0.30f)
            return false;

        return true;
    }

    /// <summary>
    /// Menjalankan pipeline lengkap: Deteksi DBNet -> Ekstraksi Kotak -> Rekognisi SVTR CTC.
    /// Mengembalikan daftar kotak teks lengkap dengan posisi piksel asli dan teks terdeteksi.
    /// Memfilter box sampah/noise yang tidak bermakna sebelum masuk triage/AI Judge.
    /// </summary>
    public List<OcrBoxModel> RunPipeline(
        Bitmap originalBmp,
        int maxSide = 1536,
        float thresh = 0.20f,
        float boxThresh = 0.45f,
        float unclipRatio = 1.40f,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ct.ThrowIfCancellationRequested();

        lock (_executionLock)
        {
            ThrowIfDisposed();
            ct.ThrowIfCancellationRequested();

            var boxes = Detect(originalBmp, maxSide, thresh, boxThresh, unclipRatio, out _, ct);
            ct.ThrowIfCancellationRequested();

            var filteredBoxes = new List<OcrBoxModel>();
            int assignedId = 1;

            for (int i = 0; i < boxes.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                RecognizeBox(originalBmp, boxes[i], ct);
                if (IsMeaningfulText(boxes[i].Text))
                {
                    boxes[i].Id = assignedId++;
                    filteredBoxes.Add(boxes[i]);
                }
            }

            return filteredBoxes;
        }
    }

    /// <summary>
    /// Menjalankan inferensi deteksi teks DBNet pada gambar asli.
    /// </summary>
    public List<OcrBoxModel> Detect(
        Bitmap originalBmp,
        int maxSide,
        float thresh,
        float boxThresh,
        float unclipRatio,
        out long detMs,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ct.ThrowIfCancellationRequested();

        int origW = originalBmp.Width;
        int origH = originalBmp.Height;

        float scale = Math.Min((float)maxSide / origW, (float)maxSide / origH);
        int targetW = (int)Math.Round(origW * scale / 32.0f) * 32;
        int targetH = (int)Math.Round(origH * scale / 32.0f) * 32;
        if (targetW < 32) targetW = 32;
        if (targetH < 32) targetH = 32;

        float scaleX = (float)origW / targetW;
        float scaleY = (float)origH / targetH;

        float[] tensorData = new float[3 * targetH * targetW];
        using (var resized = new Bitmap(targetW, targetH, PixelFormat.Format24bppRgb))
        {
            using (var g = Graphics.FromImage(resized))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.SmoothingMode = SmoothingMode.HighQuality;
                g.DrawImage(originalBmp, 0, 0, targetW, targetH);
            }

            var bmpData = resized.LockBits(
                new Rectangle(0, 0, targetW, targetH),
                ImageLockMode.ReadOnly,
                PixelFormat.Format24bppRgb);

            unsafe
            {
                byte* ptr = (byte*)bmpData.Scan0;
                int stride = bmpData.Stride;
                for (int y = 0; y < targetH; y++)
                {
                    byte* row = ptr + y * stride;
                    for (int x = 0; x < targetW; x++)
                    {
                        float b = row[x * 3 + 0] / 255.0f;
                        float g = row[x * 3 + 1] / 255.0f;
                        float r = row[x * 3 + 2] / 255.0f;

                        // Det uses BGR channel order with ImageNet mean/std
                        tensorData[0 * targetH * targetW + y * targetW + x] = (b - 0.485f) / 0.229f;
                        tensorData[1 * targetH * targetW + y * targetW + x] = (g - 0.456f) / 0.224f;
                        tensorData[2 * targetH * targetW + y * targetW + x] = (r - 0.406f) / 0.225f;
                    }
                }
            }
            resized.UnlockBits(bmpData);
        }

        var dims = new[] { 1, 3, targetH, targetW };
        var tensor = new DenseTensor<float>(tensorData, dims);
        var inputs = new List<NamedOnnxValue> { NamedOnnxValue.CreateFromTensor("x", tensor) };

        ct.ThrowIfCancellationRequested();
        var sw = Stopwatch.StartNew();
        using var results = _detSession.Run(inputs);
        sw.Stop();
        detMs = sw.ElapsedMilliseconds;
        ct.ThrowIfCancellationRequested();

        var predTensor = results.First().AsTensor<float>();
        float[,] predMap = new float[targetH, targetW];
        bool[,] binMap = new bool[targetH, targetW];

        int idx = 0;
        for (int y = 0; y < targetH; y++)
        {
            for (int x = 0; x < targetW; x++)
            {
                float v = predTensor.GetValue(idx++);
                predMap[y, x] = v;
                binMap[y, x] = (v >= thresh);
            }
        }

        // Connected Components BFS
        int[,] labelMap = new int[targetH, targetW];
        int currentLabel = 0;
        var boxes = new List<OcrBoxModel>();

        int[] dx = { 1, -1, 0, 0, 1, 1, -1, -1 };
        int[] dy = { 0, 0, 1, -1, 1, -1, 1, -1 };

        for (int y = 0; y < targetH; y++)
        {
            for (int x = 0; x < targetW; x++)
            {
                if (binMap[y, x] && labelMap[y, x] == 0)
                {
                    currentLabel++;
                    int minX = x, maxX = x, minY = y, maxY = y;
                    int count = 0;
                    float scoreSum = 0;

                    var q = new Queue<Point>();
                    q.Enqueue(new Point(x, y));
                    labelMap[y, x] = currentLabel;

                    while (q.Count > 0)
                    {
                        var p = q.Dequeue();
                        count++;
                        scoreSum += predMap[p.Y, p.X];
                        if (p.X < minX) minX = p.X;
                        if (p.X > maxX) maxX = p.X;
                        if (p.Y < minY) minY = p.Y;
                        if (p.Y > maxY) maxY = p.Y;

                        for (int k = 0; k < 8; k++)
                        {
                            int nx = p.X + dx[k];
                            int ny = p.Y + dy[k];
                            if (nx >= 0 && nx < targetW && ny >= 0 && ny < targetH)
                            {
                                if (binMap[ny, nx] && labelMap[ny, nx] == 0)
                                {
                                    labelMap[ny, nx] = currentLabel;
                                    q.Enqueue(new Point(nx, ny));
                                }
                            }
                        }
                    }

                    int bw = maxX - minX + 1;
                    int bh = maxY - minY + 1;
                    if (bw < 3 || bh < 3) continue;

                    float meanScore = scoreSum / count;
                    if (meanScore < boxThresh) continue;

                    // Unclip expansion
                    float perimeter = 2.0f * (bw + bh);
                    float distance = (count * unclipRatio) / Math.Max(1.0f, perimeter);
                    int expMinX = (int)Math.Max(0, minX - distance);
                    int expMaxX = (int)Math.Min(targetW - 1, maxX + distance);
                    int expMinY = (int)Math.Max(0, minY - distance);
                    int expMaxY = (int)Math.Min(targetH - 1, maxY + distance);

                    // Scale back to original coordinates
                    int origX1 = (int)Math.Max(0, Math.Round(expMinX * scaleX));
                    int origY1 = (int)Math.Max(0, Math.Round(expMinY * scaleY));
                    int origX2 = (int)Math.Min(origW, Math.Round(expMaxX * scaleX));
                    int origY2 = (int)Math.Min(origH, Math.Round(expMaxY * scaleY));

                    if (origX2 > origX1 && origY2 > origY1)
                    {
                        boxes.Add(new OcrBoxModel
                        {
                            X1 = origX1,
                            Y1 = origY1,
                            X2 = origX2,
                            Y2 = origY2,
                            DetScore = meanScore
                        });
                    }
                }
            }
        }

        ct.ThrowIfCancellationRequested();
        // Sort boxes top-to-bottom (20px vertical band threshold), left-to-right
        boxes.Sort((a, b) =>
        {
            if (Math.Abs(a.Y1 - b.Y1) > 20) return a.Y1.CompareTo(b.Y1);
            return a.X1.CompareTo(b.X1);
        });

        return boxes;
    }

    /// <summary>
    /// Menjalankan rekognisi teks SVTR CTC pada satu crop box dari gambar asli.
    /// </summary>
    public void RecognizeBox(Bitmap srcImg, OcrBoxModel box, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ct.ThrowIfCancellationRequested();

        int w = box.Width;
        int h = box.Height;
        if (w <= 0 || h <= 0) return;

        int recH = 48;
        int recW = Math.Max(16, (int)Math.Round((float)w * (float)recH / (float)h));
        if (recW > 1280) recW = 1280;

        float[] tensorData = new float[3 * recH * recW];
        using (var crop = new Bitmap(w, h, PixelFormat.Format24bppRgb))
        {
            using (var g = Graphics.FromImage(crop))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.SmoothingMode = SmoothingMode.HighQuality;
                g.DrawImage(srcImg, new Rectangle(0, 0, w, h), new Rectangle(box.X1, box.Y1, w, h), GraphicsUnit.Pixel);
            }

            using (var resized = new Bitmap(recW, recH, PixelFormat.Format24bppRgb))
            {
                using (var g = Graphics.FromImage(resized))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.SmoothingMode = SmoothingMode.HighQuality;
                    g.DrawImage(crop, 0, 0, recW, recH);
                }

                var bmpData = resized.LockBits(
                    new Rectangle(0, 0, recW, recH),
                    ImageLockMode.ReadOnly,
                    PixelFormat.Format24bppRgb);

                unsafe
                {
                    byte* ptr = (byte*)bmpData.Scan0;
                    int stride = bmpData.Stride;
                    for (int y = 0; y < recH; y++)
                    {
                        byte* row = ptr + y * stride;
                        for (int x = 0; x < recW; x++)
                        {
                            float b = row[x * 3 + 0] / 255.0f;
                            float g = row[x * 3 + 1] / 255.0f;
                            float r = row[x * 3 + 2] / 255.0f;

                            // Rec uses BGR normalized to [-1, 1]
                            tensorData[0 * recH * recW + y * recW + x] = (b - 0.5f) / 0.5f;
                            tensorData[1 * recH * recW + y * recW + x] = (g - 0.5f) / 0.5f;
                            tensorData[2 * recH * recW + y * recW + x] = (r - 0.5f) / 0.5f;
                        }
                    }
                }
                resized.UnlockBits(bmpData);
            }
        }

        var dims = new[] { 1, 3, recH, recW };
        var tensor = new DenseTensor<float>(tensorData, dims);
        var inputs = new List<NamedOnnxValue> { NamedOnnxValue.CreateFromTensor("x", tensor) };

        ct.ThrowIfCancellationRequested();
        var sw = Stopwatch.StartNew();
        using var results = _recSession.Run(inputs);
        sw.Stop();
        box.RecMs = sw.ElapsedMilliseconds;
        ct.ThrowIfCancellationRequested();

        var outTensor = results.First().AsTensor<float>();
        int[] outDims = outTensor.Dimensions.ToArray();
        int seqLen = outDims[1];
        int numClasses = outDims[2];

        int prevIdx = -1;
        var chars = new List<string>();
        float totalConf = 0;
        int validCount = 0;

        for (int t = 0; t < seqLen; t++)
        {
            int maxK = 0;
            float maxVal = float.MinValue;
            for (int k = 0; k < numClasses; k++)
            {
                float v = outTensor.GetValue(t * numClasses + k);
                if (v > maxVal)
                {
                    maxVal = v;
                    maxK = k;
                }
            }

            if (maxK != 0 && maxK != prevIdx)
            {
                if (maxK - 1 < _vocab.Count)
                {
                    chars.Add(_vocab[maxK - 1]);
                    totalConf += maxVal;
                    validCount++;
                }
                else
                {
                    chars.Add(" ");
                }
            }
            prevIdx = maxK;
        }

        box.Text = string.Join("", chars);
        box.RecConfidence = validCount > 0 ? (totalConf / validCount) : 0f;
    }

    private void ThrowIfDisposed()
    {
        if (_isDisposed)
            throw new ObjectDisposedException(nameof(PpOcrEngine));
    }

    public void Dispose()
    {
        if (!_isDisposed)
        {
            _detSession?.Dispose();
            _recSession?.Dispose();
            _isDisposed = true;
        }
    }
}
