using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Xunit;
using Xunit.Abstractions;

namespace Nexus.Integration.Tests;

public sealed class PerfAuditBenchmarkTests
{
    private readonly ITestOutputHelper _output;

    public PerfAuditBenchmarkTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private static string GetModelPath()
    {
        var path = @"D:\NEXUS\Models\RemoveBackground\BiRefNet_lite\model.onnx";
        if (!File.Exists(path))
            throw new FileNotFoundException($"Model not found at {path}");
        return path;
    }

    private static string GetLamaModelPath()
    {
        var path = @"D:\NEXUS\Models\Inpaint\LaMa\model_fp32.onnx";
        if (!File.Exists(path))
            throw new FileNotFoundException($"LaMa model not found at {path}");
        return path;
    }

    [Fact]
    public void Benchmark_BiRefNetLite_ColdVsWarm_And_StageBreakdown()
    {
        var modelPath = GetModelPath();
        int testWidth = 1920;
        int testHeight = 1080;

        // 1. Prepare sample bitmap
        using var sampleBitmap = new Bitmap(testWidth, testHeight, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(sampleBitmap))
        {
            g.Clear(Color.SkyBlue);
            using var brush = new SolidBrush(Color.DarkOrange);
            g.FillEllipse(brush, 400, 200, 800, 700);
        }

        var tempDir = Path.Combine(Path.GetTempPath(), "NexusPerfAudit_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var inputImagePath = Path.Combine(tempDir, "benchmark_input.png");
        sampleBitmap.Save(inputImagePath, ImageFormat.Png);

        _output.WriteLine("================================================================================");
        _output.WriteLine("  NEXUS REMOVE BACKGROUND PERFORMANCE AUDIT — COLD VS WARM BREAKDOWN");
        _output.WriteLine("  Hardware: Intel Core i5-10400 (6 Cores, 12 Threads), 32 GB RAM, CPU Execution");
        _output.WriteLine($"  Test Image: {testWidth}x{testHeight} ARGB (1080p Standard Photo)");
        _output.WriteLine($"  Model: BiRefNet_lite (224 MB, swin_v1_tiny), Input Tensor: [1, 3, 1024, 1024]");
        _output.WriteLine("================================================================================");

        // STAGE BREAKDOWN - COLD RUN
        var swTotalCold = Stopwatch.StartNew();

        // Stage 1: Load Image & Decode
        var swLoad = Stopwatch.StartNew();
        using var loadedBmp = new Bitmap(inputImagePath);
        swLoad.Stop();

        // Stage 2: Preprocess (Resize to 1024x1024 + NCHW Normalization)
        var swPre = Stopwatch.StartNew();
        var tensor = CreateNormalizedNchw(loadedBmp, 1024, 1024);
        swPre.Stop();

        // Stage 3: Session Initialization (COLD)
        var swSessionCold = Stopwatch.StartNew();
        var sessionOptions = new SessionOptions
        {
            IntraOpNumThreads = 6, // 6 physical cores on i5-10400
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL
        };
        using var session = new InferenceSession(modelPath, sessionOptions);
        swSessionCold.Stop();

        // Stage 4: Inference (COLD)
        var swInferCold = Stopwatch.StartNew();
        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("input_image", tensor)
        };
        using var outputsCold = session.Run(inputs);
        swInferCold.Stop();

        // Stage 5: Postprocess & Sigmoid
        var swPost = Stopwatch.StartNew();
        var outputTensor = outputsCold.First().AsTensor<float>();
        swPost.Stop();

        // Stage 6: Alpha Mask Compositing
        var swComp = Stopwatch.StartNew();
        using var compositeBmp = ApplyAlphaMask(loadedBmp, outputTensor);
        swComp.Stop();

        // Stage 7: PNG Disk Save
        var outputPath = Path.Combine(tempDir, "benchmark_output.png");
        var swSave = Stopwatch.StartNew();
        compositeBmp.Save(outputPath, ImageFormat.Png);
        swSave.Stop();

        swTotalCold.Stop();

        _output.WriteLine($"\n[COLD RUN STAGE BREAKDOWN]");
        _output.WriteLine($"  1. Load Image & Decode   : {swLoad.ElapsedMilliseconds,6} ms ({swLoad.ElapsedMilliseconds / 1000.0:F2} s)");
        _output.WriteLine($"  2. Preprocess (1024 NCHW): {swPre.ElapsedMilliseconds,6} ms ({swPre.ElapsedMilliseconds / 1000.0:F2} s)");
        _output.WriteLine($"  3. Session Creation (COLD): {swSessionCold.ElapsedMilliseconds,6} ms ({swSessionCold.ElapsedMilliseconds / 1000.0:F2} s)  <-- COLD START PENALTY");
        _output.WriteLine($"  4. ONNX Inference (COLD) : {swInferCold.ElapsedMilliseconds,6} ms ({swInferCold.ElapsedMilliseconds / 1000.0:F2} s)");
        _output.WriteLine($"  5. Postprocess / Sigmoid : {swPost.ElapsedMilliseconds,6} ms ({swPost.ElapsedMilliseconds / 1000.0:F2} s)");
        _output.WriteLine($"  6. Alpha Mask Composite  : {swComp.ElapsedMilliseconds,6} ms ({swComp.ElapsedMilliseconds / 1000.0:F2} s)");
        _output.WriteLine($"  7. Save PNG to Disk      : {swSave.ElapsedMilliseconds,6} ms ({swSave.ElapsedMilliseconds / 1000.0:F2} s)");
        _output.WriteLine($"  --------------------------------------------------");
        _output.WriteLine($"  TOTAL COLD PIPELINE      : {swTotalCold.ElapsedMilliseconds,6} ms ({swTotalCold.ElapsedMilliseconds / 1000.0:F2} s)");

        // 3x WARM RUNS (Using pre-created session and cached input)
        _output.WriteLine($"\n[WARM RUNS — SESSION REUSE]");
        var warmInferenceTimes = new List<long>();
        var warmTotalTimes = new List<long>();

        for (int run = 1; run <= 3; run++)
        {
            var swTotalWarm = Stopwatch.StartNew();

            var swWarmPre = Stopwatch.StartNew();
            var warmTensor = CreateNormalizedNchw(loadedBmp, 1024, 1024);
            swWarmPre.Stop();

            var swWarmInfer = Stopwatch.StartNew();
            var warmInputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor("input_image", warmTensor)
            };
            using var warmOutputs = session.Run(warmInputs);
            swWarmInfer.Stop();

            var swWarmComp = Stopwatch.StartNew();
            var warmOutTensor = warmOutputs.First().AsTensor<float>();
            using var warmCompBmp = ApplyAlphaMask(loadedBmp, warmOutTensor);
            swWarmComp.Stop();

            swTotalWarm.Stop();

            warmInferenceTimes.Add(swWarmInfer.ElapsedMilliseconds);
            warmTotalTimes.Add(swTotalWarm.ElapsedMilliseconds);

            _output.WriteLine($"  Run {run}: Inference = {swWarmInfer.ElapsedMilliseconds,5} ms | Pre+Comp = {swWarmPre.ElapsedMilliseconds + swWarmComp.ElapsedMilliseconds,4} ms | Total Pipeline = {swTotalWarm.ElapsedMilliseconds,5} ms");
        }

        var avgWarmInfer = warmInferenceTimes.Average();
        var avgWarmTotal = warmTotalTimes.Average();
        _output.WriteLine($"  --> Average WARM Inference Latency: {avgWarmInfer:F1} ms ({avgWarmInfer / 1000.0:F2} s)");
        _output.WriteLine($"  --> Average WARM Total Pipeline   : {avgWarmTotal:F1} ms ({avgWarmTotal / 1000.0:F2} s)");

        try { Directory.Delete(tempDir, true); } catch { }
    }

    [Fact]
    public void Benchmark_BiRefNetLite_ThreadingComparison()
    {
        var modelPath = GetModelPath();
        using var dummyBmp = new Bitmap(1024, 1024, PixelFormat.Format32bppArgb);
        var tensor = CreateNormalizedNchw(dummyBmp, 1024, 1024);

        _output.WriteLine("================================================================================");
        _output.WriteLine("  CPU THREADING SCALING BENCHMARK (Intel i5-10400: 6 Cores, 12 Threads)");
        _output.WriteLine("================================================================================");

        int[] threadConfigs = [1, 2, 4, 6, 8, 12];
        foreach (var threads in threadConfigs)
        {
            var options = new SessionOptions
            {
                IntraOpNumThreads = threads,
                GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL
            };
            using var session = new InferenceSession(modelPath, options);

            // Warmup
            var inputs = new List<NamedOnnxValue> { NamedOnnxValue.CreateFromTensor("input_image", tensor) };
            using (var _ = session.Run(inputs)) { }

            // Timed run
            var sw = Stopwatch.StartNew();
            using (var _ = session.Run(inputs)) { }
            sw.Stop();

            _output.WriteLine($"  IntraOpNumThreads = {threads,2}  --> Inference = {sw.ElapsedMilliseconds,5} ms ({sw.ElapsedMilliseconds / 1000.0:F2} s)");
        }
    }

    [Fact]
    public void Benchmark_ObjectEraser_LaMa_Performance()
    {
        var lamaPath = GetLamaModelPath();

        _output.WriteLine("================================================================================");
        _output.WriteLine("  OBJECT ERASER BENCHMARK — LaMa Inpainting FP32 (512x512)");
        _output.WriteLine("================================================================================");

        // Cold load
        var swCold = Stopwatch.StartNew();
        var options = new SessionOptions
        {
            IntraOpNumThreads = 6,
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL
        };
        using var session = new InferenceSession(lamaPath, options);
        swCold.Stop();

        // Create 512x512 tensors
        var imgTensor = new DenseTensor<float>(new[] { 1, 3, 512, 512 });
        var maskTensor = new DenseTensor<float>(new[] { 1, 1, 512, 512 });

        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("image", imgTensor),
            NamedOnnxValue.CreateFromTensor("mask", maskTensor)
        };

        // Inference Cold
        var swInferCold = Stopwatch.StartNew();
        using var outCold = session.Run(inputs);
        swInferCold.Stop();

        // 3x Warm
        var warmTimes = new List<long>();
        for (int i = 1; i <= 3; i++)
        {
            var swWarm = Stopwatch.StartNew();
            using var outWarm = session.Run(inputs);
            swWarm.Stop();
            warmTimes.Add(swWarm.ElapsedMilliseconds);
        }

        _output.WriteLine($"  Session Creation (COLD): {swCold.ElapsedMilliseconds} ms");
        _output.WriteLine($"  Inference (COLD)       : {swInferCold.ElapsedMilliseconds} ms");
        _output.WriteLine($"  Inference (WARM Avg)   : {warmTimes.Average():F1} ms (Runs: {string.Join(", ", warmTimes)} ms)");
    }

    private static DenseTensor<float> CreateNormalizedNchw(Bitmap input, int width, int height)
    {
        using var resized = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(resized))
        {
            g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBilinear;
            g.DrawImage(input, 0, 0, width, height);
        }

        var tensor = new DenseTensor<float>(new[] { 1, 3, height, width });
        var data = resized.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
        try
        {
            int stride = Math.Abs(data.Stride);
            byte[] pixels = new byte[stride * height];
            Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);

            float[] mean = [0.485f, 0.456f, 0.406f];
            float[] std = [0.229f, 0.224f, 0.225f];

            for (int y = 0; y < height; y++)
            {
                int row = y * stride;
                for (int x = 0; x < width; x++)
                {
                    int px = row + (x * 3);
                    tensor[0, 0, y, x] = ((pixels[px + 2] / 255f) - mean[0]) / std[0]; // R
                    tensor[0, 1, y, x] = ((pixels[px + 1] / 255f) - mean[1]) / std[1]; // G
                    tensor[0, 2, y, x] = ((pixels[px + 0] / 255f) - mean[2]) / std[2]; // B
                }
            }
        }
        finally
        {
            resized.UnlockBits(data);
        }

        return tensor;
    }

    private static Bitmap ApplyAlphaMask(Bitmap input, Tensor<float> alphaTensor)
    {
        int maskH = alphaTensor.Dimensions[2];
        int maskW = alphaTensor.Dimensions[3];

        var result = new Bitmap(input.Width, input.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(result))
        {
            g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
            g.DrawImage(input, 0, 0, input.Width, input.Height);
        }

        var data = result.LockBits(new Rectangle(0, 0, result.Width, result.Height), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        try
        {
            int stride = Math.Abs(data.Stride);
            byte[] pixels = new byte[stride * result.Height];
            Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);

            float scaleY = (maskH - 1f) / Math.Max(1, result.Height - 1f);
            float scaleX = (maskW - 1f) / Math.Max(1, result.Width - 1f);

            for (int y = 0; y < result.Height; y++)
            {
                int row = y * stride;
                int my = (int)(y * scaleY);
                for (int x = 0; x < result.Width; x++)
                {
                    int mx = (int)(x * scaleX);
                    float val = alphaTensor[0, 0, my, mx];
                    // Sigmoid if needed
                    float prob = val < 0f || val > 1f ? 1f / (1f + MathF.Exp(-val)) : val;
                    pixels[row + (x * 4) + 3] = (byte)Math.Clamp(prob * 255f, 0f, 255f);
                }
            }
            Marshal.Copy(pixels, 0, data.Scan0, pixels.Length);
        }
        finally
        {
            result.UnlockBits(data);
        }

        return result;
    }
}
