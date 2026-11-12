using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ML.OnnxRuntime;
using Nexus.Engine.ImageSubsystem.Diagnostics;
using Nexus.Engine.ImageSubsystem.Services;
using Xunit;

namespace Nexus.Engine.ImageSubsystem.Tests
{
    public class UpscaleServiceTests
    {
        private const string RealModelDir = @"C:\Users\Syand\3D Objects\Nexus\Models\Upscale\RealESR General WDN x4 v3";
        private const string SampleImagePath = @"C:\Users\Syand\3D Objects\Proyek Upscaler\Foto.jpg";

        private static string CreateTempPng(string dir, int width = 32, int height = 32)
        {
            var path = Path.Combine(dir, Path.GetRandomFileName() + ".png");
            using var bmp = new Bitmap(width, height, PixelFormat.Format24bppRgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Blue);
            }
            bmp.Save(path, ImageFormat.Png);
            return path;
        }

        [Fact]
        public void CreateSessionOptions_ConfiguresCorrectThreadsAndOptions()
        {
            var opts = UpscaleService.CreateSessionOptions();
            Assert.Equal(4, opts.IntraOpNumThreads);
            Assert.Equal(1, opts.InterOpNumThreads);
            Assert.Equal(ExecutionMode.ORT_SEQUENTIAL, opts.ExecutionMode);
            Assert.Equal(GraphOptimizationLevel.ORT_ENABLE_ALL, opts.GraphOptimizationLevel);
        }

        [Fact]
        public async Task ExecuteUpscaleAsync_SingleInference_SucceedsWithFilter()
        {
            if (!Directory.Exists(RealModelDir) || !File.Exists(SampleImagePath))
                return; // Skip integration test if model/image files are not present in test env

            using var pipeline = new ImagePipelineService();
            using var modelManager = new OnnxModelManager();
            using var service = new UpscaleService();

            var tempDir = Path.Combine(Path.GetTempPath(), "UpscaleTest_" + Path.GetRandomFileName());
            Directory.CreateDirectory(tempDir);
            try
            {
                var inputPath = CreateTempPng(tempDir, 16, 16);
                var outputPath = Path.Combine(tempDir, "out_upscaled.png");

                var result = await service.ExecuteUpscaleAsync(
                    pipeline, modelManager, inputPath, RealModelDir, 4, outputPath);

                Assert.NotNull(result);
                Assert.True(File.Exists(outputPath));
                Assert.True(new FileInfo(outputPath).Length > 0);
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public async Task ExecuteUpscaleAsync_DoubleInference_RejectsConcurrentRequestSafely()
        {
            if (!Directory.Exists(RealModelDir) || !File.Exists(SampleImagePath))
                return;

            using var pipeline = new ImagePipelineService();
            using var modelManager = new OnnxModelManager();
            using var service = new UpscaleService();

            var tempDir = Path.Combine(Path.GetTempPath(), "UpscaleTest_" + Path.GetRandomFileName());
            Directory.CreateDirectory(tempDir);
            try
            {
                var inputPath = CreateTempPng(tempDir, 64, 64);
                var outputPath1 = Path.Combine(tempDir, "out1.png");
                var outputPath2 = Path.Combine(tempDir, "out2.png");

                var task1 = service.ExecuteUpscaleAsync(pipeline, modelManager, inputPath, RealModelDir, 4, outputPath1);
                
                // Concurrent second call while task1 is running
                var ex = await Assert.ThrowsAsync<InferenceException>(() =>
                    service.ExecuteUpscaleAsync(pipeline, modelManager, inputPath, RealModelDir, 4, outputPath2));

                Assert.Equal(InferenceErrorCode.BUSY, ex.ErrorCode);

                await task1;
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public async Task ExecuteUpscaleAsync_Cancellation_CleanlyCancelsAndIsNotFailure()
        {
            if (!Directory.Exists(RealModelDir) || !File.Exists(SampleImagePath))
                return;

            using var pipeline = new ImagePipelineService();
            using var modelManager = new OnnxModelManager();
            using var service = new UpscaleService();

            var tempDir = Path.Combine(Path.GetTempPath(), "UpscaleTest_" + Path.GetRandomFileName());
            Directory.CreateDirectory(tempDir);
            try
            {
                var inputPath = CreateTempPng(tempDir, 128, 128);
                var outputPath = Path.Combine(tempDir, "out_cancelled.png");

                using var cts = new CancellationTokenSource();
                cts.Cancel(); // Cancel pre-emptively or immediately

                await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                    service.ExecuteUpscaleAsync(pipeline, modelManager, inputPath, RealModelDir, 4, outputPath, cts.Token));

                // Verify pipeline state is reset to Idle or ImageLoaded, NOT Failed
                Assert.NotEqual(ImagePipelineLifecycleState.Failed, pipeline.CurrentState);
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public async Task ExecuteUpscaleAsync_SessionRemainsReusableAfterCancellation()
        {
            if (!Directory.Exists(RealModelDir) || !File.Exists(SampleImagePath))
                return;

            using var pipeline = new ImagePipelineService();
            using var modelManager = new OnnxModelManager();
            using var service = new UpscaleService();

            var tempDir = Path.Combine(Path.GetTempPath(), "UpscaleTest_" + Path.GetRandomFileName());
            Directory.CreateDirectory(tempDir);
            try
            {
                var inputPath = CreateTempPng(tempDir, 16, 16);
                var outputPath1 = Path.Combine(tempDir, "out_cancel.png");
                var outputPath2 = Path.Combine(tempDir, "out_success.png");

                // 1. Cancelled run
                using (var cts = new CancellationTokenSource())
                {
                    cts.Cancel();
                    await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                        service.ExecuteUpscaleAsync(pipeline, modelManager, inputPath, RealModelDir, 4, outputPath1, cts.Token));
                }

                // 2. Normal run immediately after cancellation
                var result = await service.ExecuteUpscaleAsync(pipeline, modelManager, inputPath, RealModelDir, 4, outputPath2);
                Assert.NotNull(result);
                Assert.True(File.Exists(outputPath2));
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public async Task ExecuteUpscaleAsync_OutputChecksumIsIdentical()
        {
            if (!Directory.Exists(RealModelDir) || !File.Exists(SampleImagePath))
                return;

            using var pipeline = new ImagePipelineService();
            using var modelManager = new OnnxModelManager();
            using var service = new UpscaleService();

            var tempDir = Path.Combine(Path.GetTempPath(), "UpscaleTest_" + Path.GetRandomFileName());
            Directory.CreateDirectory(tempDir);
            try
            {
                var inputPath = CreateTempPng(tempDir, 16, 16);
                var outPath1 = Path.Combine(tempDir, "out_run1.png");
                var outPath2 = Path.Combine(tempDir, "out_run2.png");

                await service.ExecuteUpscaleAsync(pipeline, modelManager, inputPath, RealModelDir, 4, outPath1);
                await service.ExecuteUpscaleAsync(pipeline, modelManager, inputPath, RealModelDir, 4, outPath2);

                using var sha = SHA256.Create();
                using var fs1 = File.OpenRead(outPath1);
                using var fs2 = File.OpenRead(outPath2);
                var hash1 = BitConverter.ToString(sha.ComputeHash(fs1));
                var hash2 = BitConverter.ToString(sha.ComputeHash(fs2));

                Assert.Equal(hash1, hash2);
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }
    }
}
