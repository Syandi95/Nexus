using System;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ML.OnnxRuntime.Tensors;
using Moq;
using Nexus.App.Services;
using Nexus.App.ViewModels;
using Nexus.Contracts;
using Nexus.Core.HomeSettings.Services;
using Nexus.Engine.ColorManagement;
using Nexus.Engine.ImageSubsystem.Services;
using Xunit;

namespace Nexus.Integration.Tests
{
    public class MobileSamConcurrencyTests
    {
        private static readonly byte[] _minimalPng = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

        private static string CreateTempPng(string dir)
        {
            var path = Path.Combine(dir, Path.GetRandomFileName() + ".png");
            File.WriteAllBytes(path, _minimalPng);
            return path;
        }

        private static ImageSubsystemViewModel CreateViewModel(Mock<IMobileSamService> mobileSamMock)
        {
            var workflowMock = new Mock<IImageSubsystemWorkflow>();
            var settingsMock = new Mock<ISettingsManagerService>();
            settingsMock.Setup(s => s.ModelRootFolder).Returns(Path.Combine(Path.GetTempPath(), "NexusTest", "Models"));

            return new ImageSubsystemViewModel(
                workflowMock.Object,
                Mock.Of<IGenericImageEngine>(),
                Mock.Of<ICmykProfileManager>(),
                () => Mock.Of<IColorManagementService>(),
                settingsMock.Object,
                mobileSamMock.Object);
        }

        [Fact]
        public async Task SingleEncodeActivePerImage_WhenPrecomputeCalledMultipleTimes()
        {
            using var tempDir = new TempDirectory();
            var imgPath = CreateTempPng(tempDir.Path);

            var mobileSamMock = new Mock<IMobileSamService>();
            mobileSamMock.SetupGet(m => m.IsModelAvailable).Returns(true);

            var tcs = new TaskCompletionSource<MobileSamEmbeddingSession>();
            var encodeStarted = new TaskCompletionSource<bool>();
            int encodeCallCount = 0;

            mobileSamMock.Setup(m => m.EncodeImageAsync(It.IsAny<Bitmap>(), It.IsAny<CancellationToken>()))
                .Returns((Bitmap bmp, CancellationToken ct) =>
                {
                    Interlocked.Increment(ref encodeCallCount);
                    encodeStarted.TrySetResult(true);
                    return tcs.Task;
                });

            var vm = CreateViewModel(mobileSamMock);
            vm.SelectedMode = "Inpaint";
            vm.LoadImage(imgPath); // Triggers TriggerMobileSamPrecompute

            // Wait until encode work item is picked up on thread pool
            await encodeStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

            // Call precompute multiple times rapidly on the same image
            vm.TriggerMobileSamPrecompute();
            vm.TriggerMobileSamPrecompute();

            // Verify only 1 encode was initiated despite multiple precomputes
            Assert.Equal(1, encodeCallCount);
            Assert.True(vm.IsAutoSelectEncoding);

            // Complete the encode
            tcs.SetResult(new MobileSamEmbeddingSession(new DenseTensor<float>(new[] { 1, 256, 64, 64 }), 10, 10, 1f));

            // Wait until encoding flag resets
            for (int i = 0; i < 50 && vm.IsAutoSelectEncoding; i++)
            {
                await Task.Delay(20);
            }

            Assert.False(vm.IsAutoSelectEncoding);

            // Calling precompute now that session is computed should still not re-encode
            vm.TriggerMobileSamPrecompute();
            Assert.Equal(1, encodeCallCount);
        }

        [Fact]
        public async Task RapidAutoSelectClicks_WhileEncodeInFlight_ReusesInFlightEncodeWithoutDuplicateCalls()
        {
            using var tempDir = new TempDirectory();
            var imgPath = CreateTempPng(tempDir.Path);

            var mobileSamMock = new Mock<IMobileSamService>();
            mobileSamMock.SetupGet(m => m.IsModelAvailable).Returns(true);

            var tcs = new TaskCompletionSource<MobileSamEmbeddingSession>();
            var encodeStarted = new TaskCompletionSource<bool>();
            int encodeCallCount = 0;
            int segmentCallCount = 0;

            mobileSamMock.Setup(m => m.EncodeImageAsync(It.IsAny<Bitmap>(), It.IsAny<CancellationToken>()))
                .Returns((Bitmap bmp, CancellationToken ct) =>
                {
                    Interlocked.Increment(ref encodeCallCount);
                    encodeStarted.TrySetResult(true);
                    return tcs.Task;
                });

            mobileSamMock.Setup(m => m.SegmentPointAsync(It.IsAny<MobileSamEmbeddingSession>(), It.IsAny<PointF>(), It.IsAny<CancellationToken>()))
                .Returns((MobileSamEmbeddingSession s, PointF p, CancellationToken ct) =>
                {
                    Interlocked.Increment(ref segmentCallCount);
                    var bmp = new Bitmap(10, 10, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
                    bmp.SetPixel(2, 2, Color.White);
                    return Task.FromResult(bmp);
                });

            var vm = CreateViewModel(mobileSamMock);
            vm.SelectedMode = "Inpaint";
            vm.LoadImage(imgPath); // Background precompute starts

            // Wait until encode is in flight
            await encodeStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal(1, encodeCallCount);
            Assert.True(vm.IsAutoSelectEncoding);

            // Rapid clicks before encode completes
            var click1 = vm.AddAutoSelectPointAsync(new PointF(2f, 2f));
            var click2 = vm.AddAutoSelectPointAsync(new PointF(4f, 4f));
            var click3 = vm.AddAutoSelectPointAsync(new PointF(6f, 6f));

            // Wait slightly for click tasks to evaluate EnsureSamSessionAsync
            await Task.Delay(50);

            // While awaiting encode, no new encode should be started!
            Assert.Equal(1, encodeCallCount);

            // Now resolve encode
            tcs.SetResult(new MobileSamEmbeddingSession(new DenseTensor<float>(new[] { 1, 256, 64, 64 }), 10, 10, 1f));

            var results = await Task.WhenAll(click1, click2, click3);

            // All clicks must succeed
            Assert.All(results, res => Assert.True(res));

            // Encode was only called ONCE across all clicks and precompute
            Assert.Equal(1, encodeCallCount);

            // All 3 segmentations executed
            Assert.Equal(3, segmentCallCount);

            // Auto-mask was accumulated
            Assert.True(vm.HasAutoMask);
        }

        [Fact]
        public async Task ImageChangeDuringEncode_CancelsInFlightEncode_DoesNotPublishSessionToNewImage()
        {
            using var tempDir = new TempDirectory();
            var imgPath1 = CreateTempPng(tempDir.Path);
            var imgPath2 = CreateTempPng(tempDir.Path);

            var mobileSamMock = new Mock<IMobileSamService>();
            mobileSamMock.SetupGet(m => m.IsModelAvailable).Returns(true);

            var tcs1 = new TaskCompletionSource<MobileSamEmbeddingSession>();
            var tcs2 = new TaskCompletionSource<MobileSamEmbeddingSession>();
            var encode1Started = new TaskCompletionSource<bool>();
            var encode2Started = new TaskCompletionSource<bool>();

            CancellationToken token1 = default;
            CancellationToken token2 = default;

            mobileSamMock.Setup(m => m.EncodeImageAsync(It.IsAny<Bitmap>(), It.IsAny<CancellationToken>()))
                .Returns((Bitmap bmp, CancellationToken ct) =>
                {
                    if (token1 == default)
                    {
                        token1 = ct;
                        encode1Started.TrySetResult(true);
                        return tcs1.Task;
                    }
                    token2 = ct;
                    encode2Started.TrySetResult(true);
                    return tcs2.Task;
                });

            var session1 = new MobileSamEmbeddingSession(new DenseTensor<float>(new[] { 1, 256, 64, 64 }), 10, 10, 1f);
            var session2 = new MobileSamEmbeddingSession(new DenseTensor<float>(new[] { 1, 256, 64, 64 }), 20, 20, 1f);

            MobileSamEmbeddingSession? usedSession = null;
            var maskBmp = new Bitmap(10, 10, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
            mobileSamMock.Setup(m => m.SegmentPointAsync(It.IsAny<MobileSamEmbeddingSession>(), It.IsAny<PointF>(), It.IsAny<CancellationToken>()))
                .Returns((MobileSamEmbeddingSession s, PointF p, CancellationToken ct) =>
                {
                    usedSession = s;
                    return Task.FromResult(maskBmp);
                });

            var vm = CreateViewModel(mobileSamMock);
            vm.SelectedMode = "Inpaint";

            // 1. Load image 1
            vm.LoadImage(imgPath1);
            await encode1Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.False(token1.IsCancellationRequested);

            // 2. Change image to image 2 while image 1 is still encoding
            vm.LoadImage(imgPath2);

            // Cancellation must be triggered on image 1's token
            Assert.True(token1.IsCancellationRequested);

            // Wait for image 2 encode to start
            await encode2Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

            // 3. Complete Image 1 late
            tcs1.SetResult(session1);

            // 4. Complete Image 2
            tcs2.SetResult(session2);

            // 5. User clicks point on Image 2
            var success = await vm.AddAutoSelectPointAsync(new PointF(5f, 5f));
            Assert.True(success);

            // The session used MUST be session2, NOT session1!
            Assert.Same(session2, usedSession);
        }

        [Fact]
        public async Task CancelOrResetDuringEncode_CancelsEncodeAndClearsEncodingState()
        {
            using var tempDir = new TempDirectory();
            var imgPath = CreateTempPng(tempDir.Path);

            var mobileSamMock = new Mock<IMobileSamService>();
            mobileSamMock.SetupGet(m => m.IsModelAvailable).Returns(true);

            var tcs = new TaskCompletionSource<MobileSamEmbeddingSession>();
            var encodeStarted = new TaskCompletionSource<bool>();
            CancellationToken capturedToken = default;

            mobileSamMock.Setup(m => m.EncodeImageAsync(It.IsAny<Bitmap>(), It.IsAny<CancellationToken>()))
                .Returns((Bitmap bmp, CancellationToken ct) =>
                {
                    capturedToken = ct;
                    encodeStarted.TrySetResult(true);
                    return tcs.Task;
                });

            var vm = CreateViewModel(mobileSamMock);
            vm.SelectedMode = "Inpaint";
            vm.LoadImage(imgPath);

            // Wait until encode is in flight
            await encodeStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

            Assert.True(vm.IsAutoSelectEncoding);
            Assert.False(capturedToken.IsCancellationRequested);

            // Trigger workspace reset/clear while encode is in flight
            await vm.ExecuteClearImageAsync();

            Assert.True(capturedToken.IsCancellationRequested);
            Assert.False(vm.IsAutoSelectEncoding);
            Assert.Null(vm.GetAccumulatedAutoMask());
            Assert.False(vm.HasAutoMask);

            // Complete late encode after reset
            var lateSession = new MobileSamEmbeddingSession(new DenseTensor<float>(new[] { 1, 256, 64, 64 }), 10, 10, 1f);
            tcs.SetResult(lateSession);

            // Delay slightly to let task finish
            await Task.Delay(50);

            // Verify state remains clean and uncorrupted
            Assert.False(vm.IsAutoSelectEncoding);
            Assert.Null(vm.GetAccumulatedAutoMask());
            Assert.False(vm.HasAutoMask);
        }

        [Fact]
        public async Task LateTaskACompletion_DoesNotClearOrCancelTaskB_AndDoesNotCauseSecondEncodeB()
        {
            using var tempDir = new TempDirectory();
            var imgPathA = CreateTempPng(tempDir.Path);
            var imgPathB = CreateTempPng(tempDir.Path);

            var mobileSamMock = new Mock<IMobileSamService>();
            mobileSamMock.SetupGet(m => m.IsModelAvailable).Returns(true);

            var tcsA = new TaskCompletionSource<MobileSamEmbeddingSession>();
            var tcsB = new TaskCompletionSource<MobileSamEmbeddingSession>();
            var encodeAStarted = new TaskCompletionSource<bool>();
            var encodeBStarted = new TaskCompletionSource<bool>();

            int encodeBCount = 0;
            int totalEncodeCount = 0;

            mobileSamMock.Setup(m => m.EncodeImageAsync(It.IsAny<Bitmap>(), It.IsAny<CancellationToken>()))
                .Returns((Bitmap bmp, CancellationToken ct) =>
                {
                    int current = Interlocked.Increment(ref totalEncodeCount);
                    if (current == 1)
                    {
                        encodeAStarted.TrySetResult(true);
                        return tcsA.Task;
                    }
                    Interlocked.Increment(ref encodeBCount);
                    encodeBStarted.TrySetResult(true);
                    return tcsB.Task;
                });

            var maskBmp = new Bitmap(10, 10, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
            mobileSamMock.Setup(m => m.SegmentPointAsync(It.IsAny<MobileSamEmbeddingSession>(), It.IsAny<PointF>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(maskBmp);

            var vm = CreateViewModel(mobileSamMock);
            vm.SelectedMode = "Inpaint";

            // 1. Image A starts Encode Task A
            vm.LoadImage(imgPathA);
            await encodeAStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

            // 2. Change image to Image B -> Image B starts Encode Task B
            vm.LoadImage(imgPathB);
            await encodeBStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

            Assert.Equal(1, encodeBCount);
            Assert.True(vm.IsAutoSelectEncoding);

            // 3. Task A completes LATE after Task B is already in-flight
            var sessionA = new MobileSamEmbeddingSession(new DenseTensor<float>(new[] { 1, 256, 64, 64 }), 10, 10, 1f);
            tcsA.SetResult(sessionA);

            // Wait briefly for Task A's finally to execute
            await Task.Delay(50);

            // 4. Verify Task A did NOT clear Task B state or cancel Task B
            Assert.True(vm.IsAutoSelectEncoding);

            // 5. Trigger precompute or click on Image B: should NOT start a second Encode B
            vm.TriggerMobileSamPrecompute();
            var clickTask = vm.AddAutoSelectPointAsync(new PointF(5f, 5f));

            // Acceptance: EncodeImageAsync for B must remain exactly 1
            Assert.Equal(1, encodeBCount);

            // 6. Complete Task B
            var sessionB = new MobileSamEmbeddingSession(new DenseTensor<float>(new[] { 1, 256, 64, 64 }), 20, 20, 1f);
            tcsB.SetResult(sessionB);

            var clickSuccess = await clickTask;
            Assert.True(clickSuccess);
            Assert.Equal(1, encodeBCount); // Still exactly 1!
        }

        [Fact]
        public async Task ConcurrentImageChangeOrReset_WhileAutoSelectRunning_DoesNotThrowObjectDisposedException_AndDoesNotPublishOldSession()
        {
            using var tempDir = new TempDirectory();
            var imgPathA = CreateTempPng(tempDir.Path);
            var imgPathB = CreateTempPng(tempDir.Path);

            var mobileSamMock = new Mock<IMobileSamService>();
            mobileSamMock.SetupGet(m => m.IsModelAvailable).Returns(true);

            var sessionA = new MobileSamEmbeddingSession(new DenseTensor<float>(new[] { 1, 256, 64, 64 }), 10, 10, 1f);
            var sessionB = new MobileSamEmbeddingSession(new DenseTensor<float>(new[] { 1, 256, 64, 64 }), 20, 20, 1f);

            mobileSamMock.Setup(m => m.EncodeImageAsync(It.IsAny<Bitmap>(), It.IsAny<CancellationToken>()))
                .Returns((Bitmap bmp, CancellationToken ct) =>
                {
                    return Task.FromResult(sessionA);
                });

            var segmentAStarted = new TaskCompletionSource<bool>();
            var segmentAResolve = new TaskCompletionSource<Bitmap>();

            mobileSamMock.Setup(m => m.SegmentPointAsync(It.IsAny<MobileSamEmbeddingSession>(), It.IsAny<PointF>(), It.IsAny<CancellationToken>()))
                .Returns((MobileSamEmbeddingSession s, PointF p, CancellationToken ct) =>
                {
                    segmentAStarted.TrySetResult(true);
                    return segmentAResolve.Task;
                });

            var vm = CreateViewModel(mobileSamMock);
            vm.SelectedMode = "Inpaint";

            // Load Image A
            vm.LoadImage(imgPathA);
            for (int i = 0; i < 50 && vm.IsAutoSelectEncoding; i++) await Task.Delay(20);

            // Start auto select on Image A (reaches in-flight SegmentPointAsync)
            var selectTaskA = vm.AddAutoSelectPointAsync(new PointF(2f, 2f));
            await segmentAStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

            // Concurrently change image to Image B while SegmentPointAsync is running
            vm.LoadImage(imgPathB);

            // Let Image A's segment resolve
            var maskA = new Bitmap(10, 10, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
            maskA.SetPixel(2, 2, Color.White);
            segmentAResolve.SetResult(maskA);

            // Await select task: must not throw ObjectDisposedException
            var selectResultA = await selectTaskA;

            // Must cleanly return false and NOT publish old mask to new image
            Assert.False(selectResultA);
            Assert.False(vm.HasAutoMask);
            Assert.Null(vm.GetAccumulatedAutoMask());
        }

        [Fact]
        public async Task ConcurrentReset_WhileAutoSelectRunning_DoesNotThrowObjectDisposedException_AndDoesNotPublishMask()
        {
            using var tempDir = new TempDirectory();
            var imgPathA = CreateTempPng(tempDir.Path);

            var mobileSamMock = new Mock<IMobileSamService>();
            mobileSamMock.SetupGet(m => m.IsModelAvailable).Returns(true);

            var sessionA = new MobileSamEmbeddingSession(new DenseTensor<float>(new[] { 1, 256, 64, 64 }), 10, 10, 1f);

            mobileSamMock.Setup(m => m.EncodeImageAsync(It.IsAny<Bitmap>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(sessionA);

            var segmentStarted = new TaskCompletionSource<bool>();
            var segmentResolve = new TaskCompletionSource<Bitmap>();

            mobileSamMock.Setup(m => m.SegmentPointAsync(It.IsAny<MobileSamEmbeddingSession>(), It.IsAny<PointF>(), It.IsAny<CancellationToken>()))
                .Returns((MobileSamEmbeddingSession s, PointF p, CancellationToken ct) =>
                {
                    segmentStarted.TrySetResult(true);
                    return segmentResolve.Task;
                });

            var vm = CreateViewModel(mobileSamMock);
            vm.SelectedMode = "Inpaint";
            vm.LoadImage(imgPathA);
            for (int i = 0; i < 50 && vm.IsAutoSelectEncoding; i++) await Task.Delay(20);

            // Start auto select
            var selectTask = vm.AddAutoSelectPointAsync(new PointF(3f, 3f));
            await segmentStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

            // Concurrently reset workspace
            await vm.ExecuteClearImageAsync();

            // Resolve segment
            var maskA = new Bitmap(10, 10, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
            maskA.SetPixel(3, 3, Color.White);
            segmentResolve.SetResult(maskA);

            // Await select task: must not throw ObjectDisposedException
            var selectResult = await selectTask;

            Assert.False(selectResult);
            Assert.False(vm.HasAutoMask);
            Assert.Null(vm.GetAccumulatedAutoMask());
        }

        [Fact]
        public async Task SessionValidDuringSegmentation_ResetRightBeforePublication_OldMaskDoesNotEnterCurrentWorkspace()
        {
            using var tempDir = new TempDirectory();
            var imgPathA = CreateTempPng(tempDir.Path);

            var mobileSamMock = new Mock<IMobileSamService>();
            mobileSamMock.SetupGet(m => m.IsModelAvailable).Returns(true);

            var sessionA = new MobileSamEmbeddingSession(new DenseTensor<float>(new[] { 1, 256, 64, 64 }), 10, 10, 1f);
            mobileSamMock.Setup(m => m.EncodeImageAsync(It.IsAny<Bitmap>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(sessionA);

            var segmentStarted = new TaskCompletionSource<bool>();

            mobileSamMock.Setup(m => m.SegmentPointAsync(It.IsAny<MobileSamEmbeddingSession>(), It.IsAny<PointF>(), It.IsAny<CancellationToken>()))
                .Returns((MobileSamEmbeddingSession s, PointF p, CancellationToken ct) =>
                {
                    segmentStarted.TrySetResult(true);
                    var bmp = new Bitmap(10, 10, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
                    bmp.SetPixel(3, 3, Color.White);
                    return Task.FromResult(bmp);
                });

            var vm = CreateViewModel(mobileSamMock);
            vm.SelectedMode = "Inpaint";
            vm.LoadImage(imgPathA);
            for (int i = 0; i < 50 && vm.IsAutoSelectEncoding; i++) await Task.Delay(20);

            // Start auto select: segment completes, then right before publication/during overlay, reset workspace
            var selectTask = vm.AddAutoSelectPointAsync(new PointF(3f, 3f));
            await segmentStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

            // Reset workspace right before/during publication
            await vm.ExecuteClearImageAsync();

            var result = await selectTask;

            // Result must be rejected and old mask must NOT enter current workspace
            Assert.False(result);
            Assert.False(vm.HasAutoMask);
            Assert.Null(vm.GetAccumulatedAutoMask());
            Assert.Null(vm.AutoMaskOverlaySource);
        }
    }
}
