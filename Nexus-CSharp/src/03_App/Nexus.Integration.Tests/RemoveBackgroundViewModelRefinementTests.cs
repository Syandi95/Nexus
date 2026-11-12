using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Nexus.App.Services;
using Nexus.App.ViewModels;
using Nexus.Contracts;
using Nexus.Core.HomeSettings.Services;
using Nexus.Engine.ColorManagement;
using Nexus.Engine.ImageSubsystem.Services;
using Xunit;

namespace Nexus.Integration.Tests;

public sealed class RemoveBackgroundViewModelRefinementTests
{
    private static string CreateTestPng(string dir, int width = 4, int height = 4)
    {
        var path = Path.Combine(dir, $"{Guid.NewGuid():N}.png");
        using var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                bmp.SetPixel(x, y, Color.FromArgb(128, 50, 100, 150));
            }
        }
        bmp.Save(path, ImageFormat.Png);
        return path;
    }

    private static (ImageSubsystemViewModel ViewModel, Mock<IImageSubsystemWorkflow> WorkflowMock) CreateViewModelWithMock(string resultPath)
    {
        var workflowMock = new Mock<IImageSubsystemWorkflow>();
        workflowMock.Setup(w => w.RemoveBackgroundAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string input, string slot, string tempOut, CancellationToken ct) =>
            {
                File.Copy(resultPath, tempOut, overwrite: true);
                return tempOut;
            });

        var settingsMock = new Mock<ISettingsManagerService>();
        settingsMock.Setup(s => s.ModelRootFolder).Returns(Path.Combine(Path.GetTempPath(), "NexusTestModels"));

        var vm = new ImageSubsystemViewModel(
            workflowMock.Object,
            Mock.Of<IGenericImageEngine>(),
            Mock.Of<ICmykProfileManager>(),
            () => Mock.Of<IColorManagementService>(),
            settingsMock.Object);

        return (vm, workflowMock);
    }

    [Fact]
    public async Task RemoveBackground_InferenceSetsBaselineAndActiveMask()
    {
        using var tempDir = new TempDirectory();
        var inputPath = CreateTestPng(tempDir.Path, 4, 4);
        var resultPath = CreateTestPng(tempDir.Path, 4, 4);

        var (vm, _) = CreateViewModelWithMock(resultPath);
        vm.LoadImage(inputPath);

        Assert.False(vm.IsRemoveBgRefining);
        Assert.False(vm.CanUndoRemoveBgRefinement);
        Assert.False(vm.CanRedoRemoveBgRefinement);

        await vm.ExecutePrimaryActionAsync();

        Assert.True(vm.HasResult);
        Assert.NotNull(vm.GetActiveRemoveBgAlpha());
        Assert.NotNull(vm.GetBaselineRemoveBgAlpha());
        Assert.Equal(16, vm.GetActiveRemoveBgAlpha()!.Length);
    }

    [Fact]
    public async Task RemoveBackground_ApplyStroke_UpdatesMaskAndUndoRedoState()
    {
        using var tempDir = new TempDirectory();
        var inputPath = CreateTestPng(tempDir.Path, 4, 4);
        var resultPath = CreateTestPng(tempDir.Path, 4, 4);

        var (vm, _) = CreateViewModelWithMock(resultPath);
        vm.LoadImage(inputPath);
        await vm.ExecutePrimaryActionAsync();

        vm.IsRemoveBgRefining = true;
        Assert.True(vm.IsRemoveBgCanvasVisible);

        // Apply Add Mask on 2x2 ROI
        var roi = new Rectangle(1, 1, 2, 2);
        byte[] strokeMask = [100, 100, 100, 100];
        vm.ApplyRemoveBgStroke(roi, strokeMask, isAdd: true);

        Assert.True(vm.CanUndoRemoveBgRefinement);
        Assert.False(vm.CanRedoRemoveBgRefinement);
        Assert.True(vm.CanResetRemoveBgRefinement);

        // Verify active mask was modified (initial was 128, + 100 = 228)
        var mask = vm.GetActiveRemoveBgAlpha()!;
        Assert.Equal(228, mask[1 * 4 + 1]);
    }

    [Fact]
    public async Task RemoveBackground_UndoRedoAndReset_OperateZeroAiInference()
    {
        using var tempDir = new TempDirectory();
        var inputPath = CreateTestPng(tempDir.Path, 4, 4);
        var resultPath = CreateTestPng(tempDir.Path, 4, 4);

        var (vm, workflowMock) = CreateViewModelWithMock(resultPath);
        vm.LoadImage(inputPath);
        await vm.ExecutePrimaryActionAsync();

        // Verified AI was called exactly once for initial inference
        workflowMock.Verify(w => w.RemoveBackgroundAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);

        vm.IsRemoveBgRefining = true;
        var roi = new Rectangle(1, 1, 2, 2);
        byte[] strokeMask = [100, 100, 100, 100];
        vm.ApplyRemoveBgStroke(roi, strokeMask, isAdd: true);

        // Undo
        vm.UndoRemoveBgRefinementCommand.Execute(null);
        Assert.False(vm.CanUndoRemoveBgRefinement);
        Assert.True(vm.CanRedoRemoveBgRefinement);
        Assert.Equal(128, vm.GetActiveRemoveBgAlpha()![1 * 4 + 1]);

        // Redo
        vm.RedoRemoveBgRefinementCommand.Execute(null);
        Assert.True(vm.CanUndoRemoveBgRefinement);
        Assert.False(vm.CanRedoRemoveBgRefinement);
        Assert.Equal(228, vm.GetActiveRemoveBgAlpha()![1 * 4 + 1]);

        // Reset
        vm.ResetRemoveBgRefinementCommand.Execute(null);
        Assert.False(vm.CanUndoRemoveBgRefinement);
        Assert.False(vm.CanRedoRemoveBgRefinement);
        Assert.Equal(128, vm.GetActiveRemoveBgAlpha()![1 * 4 + 1]);
        Assert.Equal(vm.GetBaselineRemoveBgAlpha(), vm.GetActiveRemoveBgAlpha());

        // CRITICAL CHECK: Still exactly 1 AI call throughout stroke, undo, redo, reset!
        workflowMock.Verify(w => w.RemoveBackgroundAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RefiningMode_HidesBeforeAfterComparison_AndShowsMaskVisualization()
    {
        using var tempDir = new TempDirectory();
        var inputPath = CreateTestPng(tempDir.Path, 4, 4);
        var resultPath = CreateTestPng(tempDir.Path, 4, 4);

        var (vm, _) = CreateViewModelWithMock(resultPath);
        vm.LoadImage(inputPath);
        await vm.ExecutePrimaryActionAsync();

        // 1. Setelah hasil selesai tapi belum refine: Before/After compare aktif
        Assert.True(vm.HasResult);
        Assert.True(vm.IsCompareActive);
        Assert.Null(vm.RemoveBgRefineOverlaySource);

        // 2. Masuk mode refine: Before/After compare harus otomatis nonaktif dan overlay visualisasi aktif
        vm.IsRemoveBgRefining = true;
        Assert.False(vm.IsCompareActive);
        Assert.NotNull(vm.RemoveBgRefineOverlaySource);

        // 3. Keluar mode refine: Before/After compare kembali aktif dan overlay visualisasi dibersihkan
        vm.IsRemoveBgRefining = false;
        Assert.True(vm.IsCompareActive);
        Assert.Null(vm.RemoveBgRefineOverlaySource);
    }

    [Fact]
    public void GenericProcessingOverlay_SilencedAcrossAllSingleImageTools()
    {
        using var tempDir = new TempDirectory();
        var inputPath = CreateTestPng(tempDir.Path, 4, 4);
        var resultPath = CreateTestPng(tempDir.Path, 4, 4);

        var (vm, _) = CreateViewModelWithMock(resultPath);

        // Saat di RemoveBG, Inpaint, atau Upscale: generic black overlay harus SILENCED (false)
        // karena digantikan secara terpadu oleh Photo-Bounded Animated Border
        vm.IsProcessing = true;
        vm.SelectedMode = "RemoveBG";
        Assert.False(vm.IsGenericProcessingOverlayVisible);

        vm.SelectedMode = "Inpaint";
        Assert.False(vm.IsGenericProcessingOverlayVisible);

        vm.SelectedMode = "Upscale";
        Assert.False(vm.IsGenericProcessingOverlayVisible);
    }

    [Fact]
    public void BrushSoftness_DefaultsAndUpdatesCorrectly()
    {
        using var tempDir = new TempDirectory();
        var (vm, _) = CreateViewModelWithMock(CreateTestPng(tempDir.Path, 4, 4));

        // Default 25% softness
        Assert.Equal(0.25, vm.BrushSoftness);

        // Update
        vm.BrushSoftness = 0.65;
        Assert.Equal(0.65, vm.BrushSoftness);
    }

    [Fact]
    public async Task AddMask_RestoresOriginalPhotoRgb_NeverBlackCoretan()
    {
        using var tempDir = new TempDirectory();

        // Create an original photo with known bright colors (R=210, G=140, B=70)
        var inputPath = Path.Combine(tempDir.Path, "photo_input.png");
        using (var bmp = new Bitmap(4, 4, PixelFormat.Format32bppArgb))
        {
            for (int y = 0; y < 4; y++)
                for (int x = 0; x < 4; x++)
                    bmp.SetPixel(x, y, Color.FromArgb(255, 210, 140, 70));
            bmp.Save(inputPath, ImageFormat.Png);
        }

        // Create a model result where background is removed (alpha = 0)
        // Note: GDI+ PNG encoder writes RGB as (0, 0, 0) for alpha = 0 pixels
        var resultPath = Path.Combine(tempDir.Path, "model_output.png");
        using (var bmp = new Bitmap(4, 4, PixelFormat.Format32bppArgb))
        {
            for (int y = 0; y < 4; y++)
                for (int x = 0; x < 4; x++)
                    bmp.SetPixel(x, y, Color.FromArgb(0, 0, 0, 0)); // transparent
            bmp.Save(resultPath, ImageFormat.Png);
        }

        var (vm, _) = CreateViewModelWithMock(resultPath);
        vm.LoadImage(inputPath);
        await vm.ExecutePrimaryActionAsync();

        // Enter refine mode
        vm.IsRemoveBgRefining = true;

        // Apply Add Mask on pixel (2, 2)
        var roi = new Rectangle(2, 2, 1, 1);
        byte[] strokeMask = [255]; // full restore
        vm.ApplyRemoveBgStroke(roi, strokeMask, isAdd: true);

        // Exit refine mode (must persist without Apply button)
        vm.IsRemoveBgRefining = false;

        // Verify the saved temp PNG
        Assert.NotNull(vm.ResultTempPath);
        Assert.True(File.Exists(vm.ResultTempPath));

        using (var savedBmp = new Bitmap(vm.ResultTempPath))
        {
            var pixel = savedBmp.GetPixel(2, 2);
            // Alpha must be restored
            Assert.Equal(255, pixel.A);
            // CRITICAL CHECK (Bagian C): RGB must match original photo (210, 140, 70), NOT black (0, 0, 0)!
            Assert.Equal(210, pixel.R);
            Assert.Equal(140, pixel.G);
            Assert.Equal(70, pixel.B);
        }
    }

    [Fact]
    public async Task ExitRefine_PersistsActiveAlphaWithoutApplyAndWithoutAiReInference()
    {
        using var tempDir = new TempDirectory();
        var inputPath = CreateTestPng(tempDir.Path, 4, 4);
        var resultPath = CreateTestPng(tempDir.Path, 4, 4);

        var (vm, workflowMock) = CreateViewModelWithMock(resultPath);
        vm.LoadImage(inputPath);
        await vm.ExecutePrimaryActionAsync();

        // Initial AI call count = 1
        workflowMock.Verify(w => w.RemoveBackgroundAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);

        // Enter refine and edit
        vm.IsRemoveBgRefining = true;
        var roi = new Rectangle(1, 1, 2, 2);
        byte[] strokeMask = [50, 50, 50, 50];
        vm.ApplyRemoveBgStroke(roi, strokeMask, isAdd: true);

        var alphaDuringRefine = vm.GetActiveRemoveBgAlpha()![1 * 4 + 1];
        Assert.Equal(178, alphaDuringRefine); // 128 + 50 = 178

        // Exit refine
        vm.IsRemoveBgRefining = false;

        // Verify active alpha is NOT reverted to baseline
        var alphaAfterExit = vm.GetActiveRemoveBgAlpha()![1 * 4 + 1];
        Assert.Equal(178, alphaAfterExit);
        Assert.NotEqual(vm.GetBaselineRemoveBgAlpha()![1 * 4 + 1], alphaAfterExit);

        // Verify ZERO AI re-inference when exiting refine!
        workflowMock.Verify(w => w.RemoveBackgroundAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessingBorderState_TriggersOnlyDuringAiInference_AndIsDismissedOnCancelOrError()
    {
        using var tempDir = new TempDirectory();
        var inputPath = CreateTestPng(tempDir.Path, 4, 4);
        var resultPath = CreateTestPng(tempDir.Path, 4, 4);

        var (vm, workflowMock) = CreateViewModelWithMock(resultPath);
        vm.LoadImage(inputPath);

        // 1. Initially false
        Assert.False(vm.IsRemoveBgProcessing);

        // 2. Toggling Refine, Brush size/softness, Add/Remove mode does NOT trigger processing
        vm.IsRemoveBgRefining = true;
        Assert.False(vm.IsRemoveBgProcessing);
        vm.BrushSize = 50;
        Assert.False(vm.IsRemoveBgProcessing);
        vm.BrushSoftness = 0.8;
        Assert.False(vm.IsRemoveBgProcessing);
        vm.IsRemoveBgAddMode = false;
        Assert.False(vm.IsRemoveBgProcessing);
        vm.IsRemoveBgRefining = false;
        Assert.False(vm.IsRemoveBgProcessing);

        // 3. During ExecutePrimaryActionAsync, verify processing is false before and after
        var tcs = new TaskCompletionSource<string>();
        workflowMock.Setup(w => w.RemoveBackgroundAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns(async (string input, string slot, string tempOut, CancellationToken ct) =>
            {
                // In the middle of execution, IsRemoveBgProcessing is TRUE
                Assert.True(vm.IsRemoveBgProcessing);
                File.Copy(resultPath, tempOut, overwrite: true);
                await Task.Yield();
                return tempOut;
            });

        await vm.ExecutePrimaryActionAsync();

        // 4. After completion, IsRemoveBgProcessing MUST be FALSE
        Assert.False(vm.IsRemoveBgProcessing);

        // 5. Test error / cancellation dismissal
        workflowMock.Setup(w => w.RemoveBackgroundAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        await vm.ExecutePrimaryActionAsync();
        Assert.False(vm.IsRemoveBgProcessing);
    }

    [Fact]
    public async Task Upscale_ProcessingBorderState_TriggersOnlyDuringAiInference_AndIsDismissedOnCancelOrError()
    {
        using var tempDir = new TempDirectory();
        var inputPath = CreateTestPng(tempDir.Path, 4, 4);
        var resultPath = CreateTestPng(tempDir.Path, 4, 4);

        var (vm, workflowMock) = CreateViewModelWithMock(resultPath);
        vm.SelectedMode = "Upscale";
        vm.LoadImage(inputPath);

        // 1. Initially false
        Assert.False(vm.IsUpscaleProcessing);

        // 2. Adjusting settings does not trigger processing
        vm.UpscaleScale = 2.0;
        Assert.False(vm.IsUpscaleProcessing);

        // 3. During ExecutePrimaryActionAsync, verify processing is true in the middle
        workflowMock.Setup(w => w.UpscaleAsync(
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns(async (string input, int scale, string tempOut, CancellationToken ct) =>
            {
                Assert.True(vm.IsUpscaleProcessing);
                File.Copy(resultPath, tempOut, overwrite: true);
                await Task.Yield();
                return tempOut;
            });

        await vm.ExecutePrimaryActionAsync();

        // 4. After completion, IsUpscaleProcessing MUST be false
        Assert.False(vm.IsUpscaleProcessing);

        // 5. Test error / cancellation dismissal
        workflowMock.Setup(w => w.UpscaleAsync(
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        await vm.ExecutePrimaryActionAsync();
        Assert.False(vm.IsUpscaleProcessing);
    }

    [Fact]
    public async Task Inpaint_ProcessingBorderState_TriggersOnlyDuringAiInference_AndIsDismissedOnCancelOrError()
    {
        using var tempDir = new TempDirectory();
        var inputPath = CreateTestPng(tempDir.Path, 4, 4);
        var resultPath = CreateTestPng(tempDir.Path, 4, 4);

        var (vm, workflowMock) = CreateViewModelWithMock(resultPath);
        vm.SelectedMode = "Inpaint";
        vm.LoadImage(inputPath);
        vm.MaskProvider = () => new Bitmap(4, 4);

        // 1. Initially false
        Assert.False(vm.IsInpaintProcessing);

        // 2. Changing brush size/softness does not trigger processing
        vm.BrushSize = 30;
        Assert.False(vm.IsInpaintProcessing);

        // 3. During ExecutePrimaryActionAsync, verify processing is true during InpaintAsync
        workflowMock.Setup(w => w.InpaintAsync(
                It.IsAny<string>(),
                It.IsAny<Bitmap>(),
                It.IsAny<Nexus.Engine.ImageSubsystem.Pipelines.InpaintExecutionTier>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns(async (string input, Bitmap mask, Nexus.Engine.ImageSubsystem.Pipelines.InpaintExecutionTier tier, string tempOut, CancellationToken ct) =>
            {
                Assert.True(vm.IsInpaintProcessing);
                File.Copy(resultPath, tempOut, overwrite: true);
                await Task.Yield();
                return tempOut;
            });

        await vm.ExecutePrimaryActionAsync();

        // 4. After completion, IsInpaintProcessing MUST be false
        Assert.False(vm.IsInpaintProcessing);

        // 5. Test error / cancellation dismissal
        workflowMock.Setup(w => w.InpaintAsync(
                It.IsAny<string>(),
                It.IsAny<Bitmap>(),
                It.IsAny<Nexus.Engine.ImageSubsystem.Pipelines.InpaintExecutionTier>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        await vm.ExecutePrimaryActionAsync();
        Assert.False(vm.IsInpaintProcessing);
    }

    private static (ImageSubsystemViewModel ViewModel, Mock<IImageSubsystemWorkflow> WorkflowMock, Mock<IGenericImageEngine> EngineMock) CreateViewModelWithEngineMock(string resultPath)
    {
        var engineMock = new Mock<IGenericImageEngine>();
        var workflowMock = new Mock<IImageSubsystemWorkflow>();
        workflowMock.Setup(w => w.RemoveBackgroundAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string input, string slot, string tempOut, CancellationToken ct) =>
            {
                File.Copy(resultPath, tempOut, overwrite: true);
                return tempOut;
            });

        var settingsMock = new Mock<ISettingsManagerService>();
        settingsMock.Setup(s => s.ModelRootFolder).Returns(Path.Combine(Path.GetTempPath(), "NexusTestModels"));

        var vm = new ImageSubsystemViewModel(
            workflowMock.Object,
            engineMock.Object,
            Mock.Of<ICmykProfileManager>(),
            () => Mock.Of<IColorManagementService>(),
            settingsMock.Object);

        return (vm, workflowMock, engineMock);
    }

    [Fact]
    public async Task ProcessAsync_OnSuccess_PreservesWarmEngineSession()
    {
        using var tempDir = new TempDirectory();
        var inputPath = CreateTestPng(tempDir.Path, 4, 4);
        var resultPath = CreateTestPng(tempDir.Path, 4, 4);

        var (vm, _, engineMock) = CreateViewModelWithEngineMock(resultPath);
        vm.LoadImage(inputPath);

        await vm.ExecutePrimaryActionAsync();

        engineMock.Verify(e => e.UnloadAllModelsAsync(), Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_OnError_PreservesEngineLifecyclePolicy()
    {
        using var tempDir = new TempDirectory();
        var inputPath = CreateTestPng(tempDir.Path, 4, 4);
        var resultPath = CreateTestPng(tempDir.Path, 4, 4);

        var (vm, workflowMock, engineMock) = CreateViewModelWithEngineMock(resultPath);
        workflowMock.Setup(w => w.RemoveBackgroundAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Inference failed"));

        vm.LoadImage(inputPath);
        await vm.ExecutePrimaryActionAsync();

        engineMock.Verify(e => e.UnloadAllModelsAsync(), Times.Never);
        Assert.False(vm.HasResult);
        Assert.Contains("Gagal", vm.StatusText);
    }

    [Fact]
    public async Task ExecuteClearImageAsync_PreservesWarmEngineSession()
    {
        using var tempDir = new TempDirectory();
        var inputPath = CreateTestPng(tempDir.Path, 4, 4);
        var resultPath = CreateTestPng(tempDir.Path, 4, 4);

        var (vm, _, engineMock) = CreateViewModelWithEngineMock(resultPath);
        vm.LoadImage(inputPath);

        await vm.ExecuteClearImageAsync();

        engineMock.Verify(e => e.UnloadAllModelsAsync(), Times.Never);
        Assert.False(vm.IsImageLoaded);
    }

    [Fact]
    public async Task ProcessBatch_PreservesWarmEngineSession()
    {
        using var tempDir = new TempDirectory();
        var input1 = CreateTestPng(tempDir.Path, 4, 4);
        var input2 = CreateTestPng(tempDir.Path, 4, 4);
        var resultPath = CreateTestPng(tempDir.Path, 4, 4);

        var (vm, _, engineMock) = CreateViewModelWithEngineMock(resultPath);
        vm.AddFiles(new[] { input1, input2 });

        await vm.ExecutePrimaryActionAsync();

        engineMock.Verify(e => e.UnloadAllModelsAsync(), Times.Never);
    }

    [Fact]
    public void LoadImage_HighResolution_PreservesFullNativeResolutionInBeforeImageSource()
    {
        using var tempDir = new TempDirectory();
        // 2560 x 1440 is greater than 1920, verifying no 1920px downsampling occurs
        var inputPath = CreateTestPng(tempDir.Path, 2560, 1440);
        var resultPath = CreateTestPng(tempDir.Path, 2560, 1440);

        var (vm, _) = CreateViewModelWithMock(resultPath);
        vm.LoadImage(inputPath);

        Assert.NotNull(vm.BeforeImageSource);
        Assert.Equal(2560, vm.BeforeImageSource.PixelWidth);
        Assert.Equal(1440, vm.BeforeImageSource.PixelHeight);
        Assert.Equal(2560, vm.OriginalImageWidth);
        Assert.Equal(1440, vm.OriginalImageHeight);
    }

    [Fact]
    public void OutputImagePath_HighResolution_PreservesFullNativeResolutionInResultImageSource()
    {
        using var tempDir = new TempDirectory();
        var resultPath = CreateTestPng(tempDir.Path, 3840, 2160);

        var (vm, _) = CreateViewModelWithMock(resultPath);
        vm.OutputImagePath = resultPath;

        Assert.NotNull(vm.ResultImageSource);
        Assert.Equal(3840, vm.ResultImageSource.PixelWidth);
        Assert.Equal(2160, vm.ResultImageSource.PixelHeight);
    }

    [Fact]
    public void UpscaleComparison_HighResolutionBeforeAndAfter_DifferentResolutionsPreserved()
    {
        using var tempDir = new TempDirectory();
        var inputPath = CreateTestPng(tempDir.Path, 2000, 1500);
        var upscaledResultPath = CreateTestPng(tempDir.Path, 4000, 3000);

        var (vm, _) = CreateViewModelWithMock(upscaledResultPath);
        vm.LoadImage(inputPath);
        vm.OutputImagePath = upscaledResultPath;

        Assert.NotNull(vm.BeforeImageSource);
        Assert.NotNull(vm.ResultImageSource);
        Assert.Equal(2000, vm.BeforeImageSource.PixelWidth);
        Assert.Equal(1500, vm.BeforeImageSource.PixelHeight);
        Assert.Equal(4000, vm.ResultImageSource.PixelWidth);
        Assert.Equal(3000, vm.ResultImageSource.PixelHeight);
    }
}

