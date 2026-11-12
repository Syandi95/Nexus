using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using Moq;
using Nexus.App.Services;
using Nexus.App.ViewModels;
using Nexus.App.Views;
using Nexus.Engine.ColorManagement;
using Nexus.Engine.ImageSubsystem.Services;
using Nexus.Contracts;
using Nexus.Core.HomeSettings.Services;
using Xunit;

namespace Nexus.Integration.Tests
{
    public class ImageSubsystemViewModelTests
    {
        private static readonly byte[] _minimalPng = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

        private static string CreateTempPng(string dir)
        {
            var path = Path.Combine(dir, Path.GetRandomFileName() + ".png");
            File.WriteAllBytes(path, _minimalPng);
            return path;
        }

        private static ImageSubsystemViewModel CreateViewModel(
            Mock<IImagePipelineService>? pipelineMock = null,
            Mock<IRemoveBackgroundService>? rmbgMock = null,
            Mock<IUpscaleService>? upscaleMock = null,
            Mock<IOnnxModelManager>? onnxMock = null,
            Mock<IGenericImageEngine>? genericEngineMock = null,
            Mock<ICmykProfileManager>? cmykMock = null,
            Mock<ISettingsManagerService>? settingsMock = null,
            Mock<IMobileSamService>? mobileSamMock = null)
        {
            pipelineMock ??= new Mock<IImagePipelineService>();
            rmbgMock ??= new Mock<IRemoveBackgroundService>();
            upscaleMock ??= new Mock<IUpscaleService>();
            onnxMock ??= new Mock<IOnnxModelManager>();
            var workflowMock = new Mock<IImageSubsystemWorkflow>();
            workflowMock
                .Setup(workflow => workflow.RemoveBackgroundAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
                .Returns(async (
                    string inputPath,
                    string modelReference,
                    string outputPath,
                    CancellationToken cancellationToken) =>
                {
                    var modelDirectory = onnxMock.Object.ResolveModelDirectory(
                        modelReference,
                        string.Empty);
                    var result = await rmbgMock.Object.ExecuteInferenceAsync(
                        pipelineMock.Object,
                        onnxMock.Object,
                        inputPath,
                        modelDirectory,
                        outputPath,
                        cancellationToken);
                    return string.IsNullOrEmpty(result.OutputPath)
                        ? outputPath
                        : result.OutputPath;
                });
            workflowMock
                .Setup(workflow => workflow.UpscaleAsync(
                    It.IsAny<string>(),
                    It.IsAny<int>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
                .Returns(async (
                    string inputPath,
                    int targetScale,
                    string outputPath,
                    CancellationToken cancellationToken) =>
                {
                    var result = await upscaleMock.Object.ExecuteUpscaleAsync(
                        pipelineMock.Object,
                        onnxMock.Object,
                        inputPath,
                        string.Empty,
                        targetScale,
                        outputPath,
                        cancellationToken);
                    return string.IsNullOrEmpty(result.OutputPath)
                        ? outputPath
                        : result.OutputPath;
                });
            workflowMock
                .Setup(workflow => workflow.ValidateModelPackage(It.IsAny<string>()))
                .Returns((string path) => onnxMock.Object.ValidateModelPackage(path));
            workflowMock
                .Setup(workflow => workflow.GenerateMetadataAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>()))
                .Returns((string modelPath, string modelDirectory) =>
                    onnxMock.Object.GenerateMetadataAsync(modelPath, modelDirectory));

            return new ImageSubsystemViewModel(
                workflowMock.Object,
                (genericEngineMock ?? new Mock<IGenericImageEngine>()).Object,
                (cmykMock ?? new Mock<ICmykProfileManager>()).Object,
                () => Mock.Of<IColorManagementService>(),
                CreateSettingsMock(settingsMock).Object,
                mobileSamMock?.Object);
        }

        private static Mock<ISettingsManagerService> CreateSettingsMock(
            Mock<ISettingsManagerService>? settingsMock)
        {
            settingsMock ??= new Mock<ISettingsManagerService>();
            settingsMock
                .Setup(settings => settings.ModelRootFolder)
                .Returns(Path.Combine(Path.GetTempPath(), "NexusTest", "Models"));
            return settingsMock;
        }

        // ============================================================
        // RC-1 & RC-2 & RC-3: LoadImage resets compare state
        // ============================================================

        [Fact]
        public void LoadImage_CompareIsInactive()
        {
            using var tempDir = new TempDirectory();
            var filePath = CreateTempPng(tempDir.Path);
            var vm = CreateViewModel();

            vm.LoadImage(filePath);

            Assert.False(vm.IsCompareActive,
                "IsCompareActive should be false after LoadImage (PowerShell: no comparison until Result state)");
        }

        [Fact]
        public void LoadImage_CompareRatioIsZero()
        {
            using var tempDir = new TempDirectory();
            var filePath = CreateTempPng(tempDir.Path);
            var vm = CreateViewModel();

            vm.LoadImage(filePath);

            Assert.Equal(0.0, vm.CompareRatio, 6);
        }

        [Fact]
        public void LoadImage_HasResultIsFalse()
        {
            using var tempDir = new TempDirectory();
            var filePath = CreateTempPng(tempDir.Path);
            var vm = CreateViewModel();

            vm.LoadImage(filePath);

            Assert.False(vm.HasResult,
                "HasResult should be false after LoadImage (no inference completed yet)");
        }

        [Fact]
        public void LoadImage_OutputPathIsEmpty()
        {
            using var tempDir = new TempDirectory();
            var filePath = CreateTempPng(tempDir.Path);
            var vm = CreateViewModel();

            vm.LoadImage(filePath);

            Assert.Empty(vm.OutputImagePath);
        }

        [Fact]
        public void LoadImage_InputPathIsSet()
        {
            using var tempDir = new TempDirectory();
            var filePath = CreateTempPng(tempDir.Path);
            var vm = CreateViewModel();

            vm.LoadImage(filePath);

            Assert.Equal(filePath, vm.InputImagePath);
        }

        [Fact]
        public void ClearImage_ResetsHasResult()
        {
            using var tempDir = new TempDirectory();
            var filePath = CreateTempPng(tempDir.Path);
            var vm = CreateViewModel();

            vm.LoadImage(filePath);
            vm.ClearImageCommand.Execute(null);

            Assert.False(vm.HasResult);
            Assert.False(vm.IsCompareActive);
            Assert.Empty(vm.InputImagePath);
            Assert.Empty(vm.OutputImagePath);
        }

        [Fact]
        public void ClearImage_RestoresToolToInitialEmptyState()
        {
            using var tempDir = new TempDirectory();
            var filePath = CreateTempPng(tempDir.Path);
            var vm = CreateViewModel();

            // ClearImageCommand should not be executable when no image is loaded
            Assert.False(vm.ClearImageCommand.CanExecute(null));

            vm.LoadImage(filePath);
            Assert.True(vm.ClearImageCommand.CanExecute(null));

            // Modify editing / transform states
            vm.RotationAngle = 90;
            vm.ScaleX = -1.0;
            vm.ScaleY = -1.0;
            vm.IsCropActive = true;

            // Execute Clear
            vm.ClearImageCommand.Execute(null);

            // Verify complete return to initial empty state
            Assert.False(vm.IsImageLoaded);
            Assert.True(vm.HasNoImages);
            Assert.False(vm.IsSaveEnabled);
            Assert.False(vm.ClearImageCommand.CanExecute(null));
            Assert.Empty(vm.InputImagePath);
            Assert.Empty(vm.OutputImagePath);
            Assert.False(vm.HasResult);
            Assert.Equal(0, vm.RotationAngle);
            Assert.Equal(1.0, vm.ScaleX);
            Assert.Equal(1.0, vm.ScaleY);
            Assert.False(vm.IsCropActive);
            Assert.False(vm.IsFilmstripVisible);
            Assert.Equal("Belum ada gambar", vm.FileInfoText);
            Assert.Equal(0, vm.ProgressValue);
            Assert.Equal(100.0, vm.ZoomLevel);
        }

        // ============================================================
        // RC-3: Successful inference activates result state
        // ============================================================

        [Fact]
        public async Task SuccessfulInference_SetsHasResult()
        {
            using var tempDir = new TempDirectory();
            var filePath = CreateTempPng(tempDir.Path);
            var outputPath = Path.Combine(tempDir.Path, "output.png");
            var modelDir = Path.Combine(tempDir.Path, "model");

            var pipelineMock = new Mock<IImagePipelineService>();
            var rmbgMock = new Mock<IRemoveBackgroundService>();
            var onnxMock = new Mock<IOnnxModelManager>();

            onnxMock.Setup(m => m.ResolveModelDirectory(It.IsAny<string>(), It.IsAny<string>()))
                .Returns(modelDir);

            rmbgMock.Setup(m => m.ExecuteInferenceAsync(
                    It.IsAny<IImagePipelineService>(),
                    It.IsAny<IOnnxModelManager>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new InferenceResult
                {
                    OutputPath = outputPath,
                    Timing = new InferenceTiming { TotalMs = 500, InferenceMs = 400, ModelWasCached = true }
                });

            var vm = CreateViewModel(pipelineMock, rmbgMock, upscaleMock: new Mock<IUpscaleService>(), onnxMock);
            vm.LoadImage(filePath);

            await vm.ExecutePrimaryActionAsync();

            Assert.True(vm.HasResult, "HasResult should be true after successful inference");
            Assert.True(vm.IsCompareActive, "IsCompareActive should be true after successful inference");
            Assert.Equal(0.5, vm.CompareRatio, 6);
        }

        [Fact]
        public async Task SuccessfulInference_SetsOutputPath()
        {
            using var tempDir = new TempDirectory();
            var filePath = CreateTempPng(tempDir.Path);
            var outputPath = Path.Combine(tempDir.Path, "output.png");
            var modelDir = Path.Combine(tempDir.Path, "model");

            var pipelineMock = new Mock<IImagePipelineService>();
            var rmbgMock = new Mock<IRemoveBackgroundService>();
            var onnxMock = new Mock<IOnnxModelManager>();

            onnxMock.Setup(m => m.ResolveModelDirectory(It.IsAny<string>(), It.IsAny<string>()))
                .Returns(modelDir);

            rmbgMock.Setup(m => m.ExecuteInferenceAsync(
                    It.IsAny<IImagePipelineService>(),
                    It.IsAny<IOnnxModelManager>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new InferenceResult
                {
                    OutputPath = outputPath,
                    Timing = new InferenceTiming { TotalMs = 500, InferenceMs = 400, ModelWasCached = true }
                });

            var vm = CreateViewModel(pipelineMock, rmbgMock, upscaleMock: new Mock<IUpscaleService>(), onnxMock);
            vm.LoadImage(filePath);

            await vm.ExecutePrimaryActionAsync();

            Assert.Equal(outputPath, vm.OutputImagePath);
            Assert.Contains("siap disimpan", vm.StatusText);
        }

        [Fact]
        public async Task FailedInference_DoesNotSetHasResult()
        {
            using var tempDir = new TempDirectory();
            var filePath = CreateTempPng(tempDir.Path);
            var modelDir = Path.Combine(tempDir.Path, "model");

            var pipelineMock = new Mock<IImagePipelineService>();
            var rmbgMock = new Mock<IRemoveBackgroundService>();
            var onnxMock = new Mock<IOnnxModelManager>();

            onnxMock.Setup(m => m.ResolveModelDirectory(It.IsAny<string>(), It.IsAny<string>()))
                .Returns(modelDir);

            rmbgMock.Setup(m => m.ExecuteInferenceAsync(
                    It.IsAny<IImagePipelineService>(),
                    It.IsAny<IOnnxModelManager>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("test failure"));

            var vm = CreateViewModel(pipelineMock, rmbgMock, upscaleMock: new Mock<IUpscaleService>(), onnxMock);
            vm.LoadImage(filePath);

            await vm.ExecutePrimaryActionAsync();

            Assert.False(vm.HasResult, "HasResult should remain false after failed inference");
            Assert.Contains("Gagal", vm.StatusText);
        }

        // ============================================================
        // RC-3: ToggleCompareCommand behavior
        // ============================================================

        [Fact]
        public void ToggleCompareCommand_InResultState_ResetsToMidpoint()
        {
            using var tempDir = new TempDirectory();
            var filePath = CreateTempPng(tempDir.Path);
            var vm = CreateViewModel();
            vm.LoadImage(filePath);
            vm.HasResult = true;
            vm.CompareRatio = 0.25;
            vm.ToggleCompareCommand.Execute(null);

            Assert.Equal(0.5, vm.CompareRatio, 6);
        }

        [Fact]
        public void ToggleCompareCommand_WithoutResult_DoesNotChangeRatio()
        {
            using var tempDir = new TempDirectory();
            var filePath = CreateTempPng(tempDir.Path);
            var vm = CreateViewModel();
            vm.LoadImage(filePath);

            vm.CompareRatio = 0.25;
            vm.ToggleCompareCommand.Execute(null);

            Assert.Equal(0.25, vm.CompareRatio, 6);
        }

        // ============================================================
        // RC-4: File lock reproduction
        // ============================================================

        [Fact]
        public void BitmapImage_DefaultBinding_LocksFile()
        {
            using var tempDir = new TempDirectory();
            var filePath = CreateTempPng(tempDir.Path);

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(filePath);
            bitmap.EndInit();
            bitmap.Freeze();

            Assert.ThrowsAny<IOException>(() =>
            {
                File.Delete(filePath);
            });
        }

        [Fact]
        public void BitmapImage_OnLoad_ReleasesFileHandle()
        {
            using var tempDir = new TempDirectory();
            var filePath = CreateTempPng(tempDir.Path);

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(filePath);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            bitmap.Freeze();

            File.Delete(filePath);
            Assert.False(File.Exists(filePath));
        }

        [Fact]
        public void OverwriteSameOutputFile_NoLock()
        {
            using var tempDir = new TempDirectory();
            var outputPath = Path.Combine(tempDir.Path, "output.png");
            File.WriteAllBytes(outputPath, _minimalPng);

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(outputPath);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            bitmap.Freeze();

            File.Delete(outputPath);
            File.WriteAllBytes(outputPath, _minimalPng);
            Assert.True(File.Exists(outputPath));
        }

        // ============================================================
        // Bug A regression: LoadImage must reset compare state when
        // called while already in Result state (replace scenario)
        // ============================================================

        [Fact]
        public void LoadImage_AfterResultState_ResetsAllCompareState()
        {
            using var tempDir = new TempDirectory();
            var filePath = CreateTempPng(tempDir.Path);
            var vm = CreateViewModel();

            vm.LoadImage(filePath);
            vm.HasResult = true;
            vm.CompareRatio = 0.5;

            vm.LoadImage(filePath); // replace

            Assert.False(vm.HasResult, "HasResult must be false after LoadImage in Result state");
            Assert.Equal(0.0, vm.CompareRatio, 6);
            Assert.False(vm.IsCompareActive);
            Assert.Empty(vm.OutputImagePath);
        }

        [Fact]
        public void LoadImage_AfterDefaultZoom_DoesNotFireZoomLevelChanged()
        {
            using var tempDir = new TempDirectory();
            var filePath = CreateTempPng(tempDir.Path);
            var vm = CreateViewModel();

            var zoomChanged = false;
            vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(ImageSubsystemViewModel.ZoomLevel))
                    zoomChanged = true;
            };

            vm.LoadImage(filePath);

            Assert.False(zoomChanged,
                "ZoomLevel defaults to 100.0, so setting 100.0 should not fire PropertyChanged. " +
                "Geometry sync is triggered by InputImagePath PropertyChanged.");
        }

        [Fact]
        public void LoadImage_FiresInputImagePathChanged()
        {
            using var tempDir = new TempDirectory();
            var filePath = CreateTempPng(tempDir.Path);
            var vm = CreateViewModel();

            var pathChanged = false;
            vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(ImageSubsystemViewModel.InputImagePath))
                    pathChanged = true;
            };

            vm.LoadImage(filePath);

            Assert.True(pathChanged,
                "LoadImage must fire InputImagePath PropertyChanged (code-behind depends on this for clip reset)");
        }

        // ============================================================
        // Bug B regression: DragDelta should use rendered width
        // ============================================================

        [Theory]
        [InlineData(1.0, 800, 800)]   // zoom=1x, ActualWidth=800 → rendered=800
        [InlineData(2.0, 800, 1600)]  // zoom=2x, ActualWidth=800 → rendered=1600
        [InlineData(0.5, 800, 400)]   // zoom=0.5x, ActualWidth=800 → rendered=400
        [InlineData(1.0, 100, 100)]   // zoom=1x, ActualWidth=100 → rendered=100
        public void CalculateRenderedWidth_AccountsForZoom(
            double zoom, double actualWidth, double expectedRenderedWidth)
        {
            var calculated = ImageSubsystemView.CalculateRenderedWidth(actualWidth, zoom);
            Assert.Equal(expectedRenderedWidth, calculated, 2);
        }

        [Fact]
        public void DragDelta_RenderedWidth_EdgeCases()
        {
            // When ActualWidth is 0, should return 0 (prevents division by zero)
            Assert.Equal(0, ImageSubsystemView.CalculateRenderedWidth(0, 2.0));

            // When zoom is 0 or negative, should return 0 (division guard)
            Assert.Equal(0, ImageSubsystemView.CalculateRenderedWidth(800, 0));
            Assert.Equal(0, ImageSubsystemView.CalculateRenderedWidth(800, -1));
        }

        [Fact]
        public async Task ExecutePrimaryActionAsync_DoubleClickProtection_RejectsSecondClickWhenProcessing()
        {
            using var tempDir = new TempDirectory();
            var filePath = CreateTempPng(tempDir.Path);

            var upscaleMock = new Mock<IUpscaleService>();
            var tcs = new TaskCompletionSource<InferenceResult>();
            upscaleMock.Setup(u => u.ExecuteUpscaleAsync(
                It.IsAny<IImagePipelineService>(),
                It.IsAny<IOnnxModelManager>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns(tcs.Task);

            var vm = CreateViewModel(upscaleMock: upscaleMock);
            vm.SelectedMode = "Upscale";
            vm.LoadImage(filePath);

            var task1 = vm.ExecutePrimaryActionAsync();
            Assert.True(vm.IsProcessing);

            // Second click while processing
            await vm.ExecutePrimaryActionAsync();

            // Verify upscale service was called ONLY ONCE
            upscaleMock.Verify(u => u.ExecuteUpscaleAsync(
                It.IsAny<IImagePipelineService>(),
                It.IsAny<IOnnxModelManager>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()), Times.Once);

            tcs.SetResult(new InferenceResult());
            await task1;
        }

        [Fact]
        public async Task ExecuteCancel_OngoingUpscale_CancelsCleanlyAndResetsState()
        {
            using var tempDir = new TempDirectory();
            var filePath = CreateTempPng(tempDir.Path);

            var upscaleMock = new Mock<IUpscaleService>();
            upscaleMock.Setup(u => u.ExecuteUpscaleAsync(
                It.IsAny<IImagePipelineService>(),
                It.IsAny<IOnnxModelManager>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns<IImagePipelineService, IOnnxModelManager, string, string, int, string, CancellationToken>(
                async (_, _, _, _, _, _, token) =>
                {
                    await Task.Delay(1000, token);
                    return new InferenceResult();
                });

            var vm = CreateViewModel(upscaleMock: upscaleMock);
            vm.SelectedMode = "Upscale";
            vm.LoadImage(filePath);

            var task = vm.ExecutePrimaryActionAsync();
            Assert.True(vm.IsProcessing);

            vm.ExecuteCancel();

            await task;

            Assert.False(vm.IsProcessing);
            Assert.Equal("Dibatalkan", vm.StatusText);
            Assert.Equal(0.0, vm.ProgressValue);
        }

        // ============================================================
        // Batch RemoveBackground Tests
        // ============================================================

        [Fact]
        public void AddFiles_SingleFile_LoadsSingleMode()
        {
            using var tempDir = new TempDirectory();
            var file1 = CreateTempPng(tempDir.Path);
            var vm = CreateViewModel();

            vm.AddFiles(new[] { file1 });

            Assert.True(vm.IsImageLoaded);
            Assert.False(vm.IsBatchMode);
            Assert.True(vm.IsSingleImageMode);
            Assert.Empty(vm.BatchItems);
            Assert.Equal(file1, vm.InputImagePath);
        }

        [Fact]
        public void AddFiles_MultipleFiles_EntersBatchMode()
        {
            using var tempDir = new TempDirectory();
            var file1 = CreateTempPng(tempDir.Path);
            var file2 = CreateTempPng(tempDir.Path);
            var file3 = CreateTempPng(tempDir.Path);
            var vm = CreateViewModel();

            vm.AddFiles(new[] { file1, file2, file3 });

            Assert.True(vm.IsImageLoaded);
            Assert.True(vm.IsBatchMode);
            Assert.False(vm.IsSingleImageMode);
            Assert.Equal(3, vm.BatchItems.Count);
            Assert.Equal("3 / 10 gambar", vm.BatchCountText);
            Assert.Equal("Hapus Background (3 Gambar)", vm.PrimaryActionButtonText);
        }

        [Fact]
        public void AddFiles_MoreThan10_Enforces10FileLimit()
        {
            using var tempDir = new TempDirectory();
            var files = new string[15];
            for (int i = 0; i < 15; i++)
            {
                files[i] = CreateTempPng(tempDir.Path);
            }
            var vm = CreateViewModel();

            vm.AddFiles(files);

            Assert.Equal(10, vm.BatchItems.Count);
            Assert.False(vm.CanAddBatchImage);
            Assert.Contains("Maksimal 10 gambar", vm.StatusText);
        }

        [Fact]
        public void RemoveBatchItem_DecrementsQueue()
        {
            using var tempDir = new TempDirectory();
            var file1 = CreateTempPng(tempDir.Path);
            var file2 = CreateTempPng(tempDir.Path);
            var vm = CreateViewModel();

            vm.AddFiles(new[] { file1, file2 });
            Assert.Equal(2, vm.BatchItems.Count);

            var firstItem = vm.BatchItems[0];
            vm.RemoveBatchItemCommand.Execute(firstItem);

            Assert.Single(vm.BatchItems);
            Assert.False(vm.IsBatchMode); // 1 item left -> single mode or 1 in batch
        }

        [Fact]
        public async Task ProcessBatch_SequentialExecution_ProcessesAllItems()
        {
            using var tempDir = new TempDirectory();
            var file1 = CreateTempPng(tempDir.Path);
            var file2 = CreateTempPng(tempDir.Path);

            var pipelineMock = new Mock<IImagePipelineService>();
            var rmbgMock = new Mock<IRemoveBackgroundService>();
            var onnxMock = new Mock<IOnnxModelManager>();

            onnxMock.Setup(m => m.ResolveModelDirectory(It.IsAny<string>(), It.IsAny<string>()))
                .Returns(tempDir.Path);

            rmbgMock.Setup(m => m.ExecuteInferenceAsync(
                    It.IsAny<IImagePipelineService>(),
                    It.IsAny<IOnnxModelManager>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new InferenceResult
                {
                    OutputPath = Path.Combine(tempDir.Path, "out.png"),
                    Timing = new InferenceTiming { TotalMs = 100, InferenceMs = 80 }
                });

            var vm = CreateViewModel(pipelineMock, rmbgMock, upscaleMock: new Mock<IUpscaleService>(), onnxMock);
            vm.AddFiles(new[] { file1, file2 });

            await vm.ExecutePrimaryActionAsync();

            Assert.False(vm.IsProcessing);
            Assert.All(vm.BatchItems, item => Assert.Equal(ImageBatchItemState.Completed, item.State));
            Assert.Contains("2/2 gambar berhasil diproses", vm.StatusText);
        }

        [Fact]
        public async Task ProcessBatch_UpscaleMode_ExecutesUpscaleAsync_NotRemoveBackground()
        {
            using var tempDir = new TempDirectory();
            var file1 = CreateTempPng(tempDir.Path);
            var file2 = CreateTempPng(tempDir.Path);

            var pipelineMock = new Mock<IImagePipelineService>();
            var rmbgMock = new Mock<IRemoveBackgroundService>();
            var upscaleMock = new Mock<IUpscaleService>();
            var onnxMock = new Mock<IOnnxModelManager>();

            upscaleMock.Setup(m => m.ExecuteUpscaleAsync(
                    It.IsAny<IImagePipelineService>(),
                    It.IsAny<IOnnxModelManager>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<int>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new InferenceResult
                {
                    OutputPath = Path.Combine(tempDir.Path, "out_upscale.png"),
                    Timing = new InferenceTiming { TotalMs = 120, InferenceMs = 100 }
                });

            var vm = CreateViewModel(pipelineMock, rmbgMock, upscaleMock, onnxMock);
            vm.SelectedMode = "Upscale";
            vm.AddFiles(new[] { file1, file2 });

            Assert.True(vm.IsBatchMode);
            Assert.Equal("AI Upscale (2 Gambar)", vm.PrimaryActionButtonText);
            Assert.Equal("Proses upscale untuk 2 gambar", vm.PrimaryActionToolTip);

            await vm.ExecutePrimaryActionAsync();

            Assert.False(vm.IsProcessing);
            Assert.All(vm.BatchItems, item => Assert.Equal(ImageBatchItemState.Completed, item.State));
            Assert.Contains("2/2 gambar berhasil diproses", vm.StatusText);

            // Verify: Upscale inference was executed for each file
            upscaleMock.Verify(m => m.ExecuteUpscaleAsync(
                It.IsAny<IImagePipelineService>(),
                It.IsAny<IOnnxModelManager>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()), Times.Exactly(2));

            // Verify: RemoveBackground inference was NEVER executed
            rmbgMock.Verify(m => m.ExecuteInferenceAsync(
                It.IsAny<IImagePipelineService>(),
                It.IsAny<IOnnxModelManager>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }

        // ============================================================
        // Inpaint Mode Single-Image Constraints & Batch Disabling Tests
        // ============================================================

        [Fact]
        public void InpaintMode_DropZoneTexts_ReflectSingleImageMode()
        {
            var vm = CreateViewModel();

            // Default RemoveBG
            Assert.Equal("Tarik dan lepas satu atau beberapa gambar di sini", vm.DropZoneTitle);
            Assert.Equal("Maksimal 10 gambar (PNG, JPG, JPEG, atau WebP)", vm.DropZoneSubtitle);

            // Switch to Inpaint
            vm.SelectedMode = "Inpaint";
            Assert.Equal("Tarik dan lepas satu gambar untuk dihapus objeknya", vm.DropZoneTitle);
            Assert.Equal("Mendukung format PNG, JPG, JPEG, atau WebP (1 gambar per proses)", vm.DropZoneSubtitle);
            Assert.False(vm.CanAddBatchImage);
            Assert.False(vm.IsBatchMode);

            // Switch back to Upscale
            vm.SelectedMode = "Upscale";
            Assert.Equal("Tarik dan lepas satu atau beberapa gambar di sini", vm.DropZoneTitle);
            Assert.Equal("Maksimal 10 gambar (PNG, JPG, JPEG, atau WebP)", vm.DropZoneSubtitle);
        }

        [Fact]
        public void InpaintMode_AddMultipleFiles_StrictlyLimitsToOneAndRejectsBatchQueue()
        {
            using var tempDir = new TempDirectory();
            var file1 = CreateTempPng(tempDir.Path);
            var file2 = CreateTempPng(tempDir.Path);
            var file3 = CreateTempPng(tempDir.Path);

            var vm = CreateViewModel();
            vm.SelectedMode = "Inpaint";

            // Attempt to drop/add 3 files simultaneously in Inpaint mode
            vm.AddFiles(new[] { file1, file2, file3 });

            // Must NOT populate batch queue
            Assert.Empty(vm.BatchItems);
            Assert.False(vm.IsBatchMode);
            Assert.True(vm.IsImageLoaded);
            Assert.True(vm.IsSingleImageMode);

            // Only the first file must be loaded
            Assert.Equal(file1, vm.InputImagePath);

            // Informative rejection status for the rest
            Assert.Contains("1 gambar", vm.StatusText);
            Assert.Contains("diabaikan", vm.DetailText);
        }

        [Fact]
        public void InpaintMode_SwitchModeFromBatch_ClearsExistingBatchQueue()
        {
            using var tempDir = new TempDirectory();
            var file1 = CreateTempPng(tempDir.Path);
            var file2 = CreateTempPng(tempDir.Path);

            var vm = CreateViewModel();
            vm.SelectedMode = "RemoveBG";
            vm.AddFiles(new[] { file1, file2 });

            Assert.Equal(2, vm.BatchItems.Count);
            Assert.True(vm.IsBatchMode);

            // Switch to Inpaint mode
            vm.SelectedMode = "Inpaint";

            Assert.Empty(vm.BatchItems);
            Assert.False(vm.IsBatchMode);
            Assert.False(vm.CanAddBatchImage);
        }

        [Fact]
        public void AutoSelect_Mode_MutualExclusivity()
        {
            var mobileSamMock = new Mock<IMobileSamService>();
            mobileSamMock.SetupGet(m => m.IsModelAvailable).Returns(true);

            var vm = CreateViewModel(mobileSamMock: mobileSamMock);
            vm.SelectedMode = "Inpaint";

            // Default state: Brush mode active
            Assert.True(vm.IsBrushMode);
            Assert.False(vm.IsEraserMode);
            Assert.False(vm.IsAutoSelectMode);

            // Switch to AutoSelect mode
            vm.SetAutoSelectModeCommand.Execute(null);
            Assert.True(vm.IsAutoSelectMode);
            Assert.False(vm.IsBrushMode);
            Assert.False(vm.IsEraserMode);

            // Switch to Eraser mode
            vm.SetEraserModeCommand.Execute(null);
            Assert.True(vm.IsEraserMode);
            Assert.False(vm.IsBrushMode);
            Assert.False(vm.IsAutoSelectMode);

            // Switch to Brush mode
            vm.SetBrushModeCommand.Execute(null);
            Assert.True(vm.IsBrushMode);
            Assert.False(vm.IsEraserMode);
            Assert.False(vm.IsAutoSelectMode);
        }

        [Fact]
        public async Task AutoSelect_AddPoint_ProgressiveUnion_AndUndoRedo()
        {
            using var tempDir = new TempDirectory();
            var imgPath = CreateTempPng(tempDir.Path);

            var mobileSamMock = new Mock<IMobileSamService>();
            mobileSamMock.SetupGet(m => m.IsModelAvailable).Returns(true);

            var maskBmp1 = new System.Drawing.Bitmap(10, 10, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
            maskBmp1.SetPixel(2, 2, System.Drawing.Color.White);

            var maskBmp2 = new System.Drawing.Bitmap(10, 10, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
            maskBmp2.SetPixel(8, 8, System.Drawing.Color.White);

            mobileSamMock.Setup(m => m.EncodeImageAsync(It.IsAny<System.Drawing.Bitmap>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new MobileSamEmbeddingSession(new Microsoft.ML.OnnxRuntime.Tensors.DenseTensor<float>(new[] { 1, 256, 64, 64 }), 10, 10, 1f));

            mobileSamMock.SetupSequence(m => m.SegmentPointAsync(It.IsAny<MobileSamEmbeddingSession>(), It.IsAny<System.Drawing.PointF>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(maskBmp1)
                .ReturnsAsync(maskBmp2);

            var vm = CreateViewModel(mobileSamMock: mobileSamMock);
            vm.SelectedMode = "Inpaint";
            vm.LoadImage(imgPath);

            Assert.False(vm.HasAutoMask);
            Assert.False(vm.HasMaskStrokes);

            // 1. Add first point (2, 2)
            var success1 = await vm.AddAutoSelectPointAsync(new System.Drawing.PointF(2f, 2f));
            Assert.True(success1);
            Assert.True(vm.HasAutoMask);
            Assert.True(vm.HasMaskStrokes);
            Assert.NotNull(vm.AutoMaskOverlaySource);

            var autoMask1 = vm.GetAccumulatedAutoMask();
            Assert.NotNull(autoMask1);
            Assert.Equal(255, autoMask1[2 * 10 + 2]); // (2, 2) set
            Assert.Equal(0, autoMask1[8 * 10 + 8]);   // (8, 8) not yet set

            // 2. Add second point (8, 8) -> Progressive union
            var success2 = await vm.AddAutoSelectPointAsync(new System.Drawing.PointF(8f, 8f));
            Assert.True(success2);
            var autoMask2 = vm.GetAccumulatedAutoMask();
            Assert.NotNull(autoMask2);
            Assert.Equal(255, autoMask2[2 * 10 + 2]); // (2, 2) still preserved
            Assert.Equal(255, autoMask2[8 * 10 + 8]); // (8, 8) added to union

            // 3. Undo second point
            var undoSuccess = await vm.UndoAutoMaskAsync();
            Assert.True(undoSuccess);
            var autoMaskAfterUndo = vm.GetAccumulatedAutoMask();
            Assert.NotNull(autoMaskAfterUndo);
            Assert.Equal(255, autoMaskAfterUndo[2 * 10 + 2]); // (2, 2) preserved
            Assert.Equal(0, autoMaskAfterUndo[8 * 10 + 8]);   // (8, 8) reverted

            // 4. Redo second point
            var redoSuccess = await vm.RedoAutoMaskAsync();
            Assert.True(redoSuccess);
            var autoMaskAfterRedo = vm.GetAccumulatedAutoMask();
            Assert.NotNull(autoMaskAfterRedo);
            Assert.Equal(255, autoMaskAfterRedo[2 * 10 + 2]);
            Assert.Equal(255, autoMaskAfterRedo[8 * 10 + 8]);

            // 5. Clear mask
            vm.ClearAutoMask();
            Assert.False(vm.HasAutoMask);
            Assert.Null(vm.GetAccumulatedAutoMask());
            Assert.Null(vm.AutoMaskOverlaySource);
        }

        [Fact]
        public async Task SaveResultAsync_OnSuccessfulSave_ShowsSuccessSnackbar()
        {
            using var tempDir = new TempDirectory();
            var inputPath = CreateTempPng(tempDir.Path);
            var resultPath = CreateTempPng(tempDir.Path);

            var workflowMock = new Mock<IImageSubsystemWorkflow>();
            workflowMock.Setup(w => w.UpscaleAsync(
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(resultPath);

            var notificationMock = new Mock<INotificationService>();
            var vm = new ImageSubsystemViewModel(
                workflowMock.Object,
                Mock.Of<IGenericImageEngine>(),
                Mock.Of<ICmykProfileManager>(),
                () => Mock.Of<IColorManagementService>(),
                CreateSettingsMock(null).Object,
                notificationService: notificationMock.Object);

            vm.SelectedMode = "Upscale";
            vm.LoadImage(inputPath);

            // Step 1: Process inference
            await vm.ExecutePrimaryActionAsync();

            // Step 2: Save result
            await vm.ExecutePrimaryActionAsync();

            notificationMock.Verify(n => n.ShowSuccess("Sukses", "Gambar berhasil disimpan.", null), Times.Once);
        }

        // ============================================================
        // Zoom Ladder & Proportional Scaling Tests
        // ============================================================

        [Fact]
        public void InitialZoomLevel_DefaultsTo100Percent_FitBehaviorPreserved()
        {
            var vm = CreateViewModel();
            Assert.Equal(100.0, vm.ZoomLevel);
            Assert.Equal("100%", vm.ZoomText);
        }

        [Fact]
        public void ZoomInCommand_TraversesDiscreteLadderToMax1000()
        {
            var vm = CreateViewModel();
            Assert.Equal(100.0, vm.ZoomLevel);

            double[] expectedLadder = { 125, 150, 200, 250, 300, 400, 500, 600, 800, 1000 };
            foreach (var expected in expectedLadder)
            {
                vm.ZoomInCommand.Execute(null);
                Assert.Equal(expected, vm.ZoomLevel);
            }

            // Cap test: execute at 1000 should remain 1000
            vm.ZoomInCommand.Execute(null);
            Assert.Equal(1000.0, vm.ZoomLevel);
        }

        [Fact]
        public void ZoomOutCommand_TraversesDiscreteLadderToMin50()
        {
            var vm = CreateViewModel();
            vm.ZoomLevel = 1000.0;

            double[] expectedLadder = { 800, 600, 500, 400, 300, 250, 200, 150, 125, 100, 75, 50 };
            foreach (var expected in expectedLadder)
            {
                vm.ZoomOutCommand.Execute(null);
                Assert.Equal(expected, vm.ZoomLevel);
            }

            // Floor test: execute at 50 should remain 50
            vm.ZoomOutCommand.Execute(null);
            Assert.Equal(50.0, vm.ZoomLevel);
        }

        [Fact]
        public void ZoomAbove400_HasNoticeableProportionalIncrease()
        {
            var vm = CreateViewModel();
            vm.ZoomLevel = 400.0;

            // From 400%, Zoom In must advance to 500% (+25% relative, not old +6.25% at 425%)
            vm.ZoomInCommand.Execute(null);
            Assert.Equal(500.0, vm.ZoomLevel);

            // From 500%, Zoom In must advance to 600% (+20% relative, not old +5% at 525%)
            vm.ZoomInCommand.Execute(null);
            Assert.Equal(600.0, vm.ZoomLevel);

            // From 600%, Zoom In must advance to 800% (+33.3% relative)
            vm.ZoomInCommand.Execute(null);
            Assert.Equal(800.0, vm.ZoomLevel);

            // From 800%, Zoom In must advance to 1000% (+25% relative, not old +3.125% at 825%)
            vm.ZoomInCommand.Execute(null);
            Assert.Equal(1000.0, vm.ZoomLevel);
        }

        [Theory]
        [InlineData(430.0, 500.0, 400.0)]
        [InlineData(720.0, 800.0, 600.0)]
        [InlineData(85.0, 100.0, 75.0)]
        public void IntermediateSliderValues_SnapToCorrectLadderTiers(
            double intermediateZoom, double expectedNextIn, double expectedNextOut)
        {
            var nextIn = ImageSubsystemViewModel.GetNextZoomIn(intermediateZoom);
            var nextOut = ImageSubsystemViewModel.GetNextZoomOut(intermediateZoom);

            Assert.Equal(expectedNextIn, nextIn);
            Assert.Equal(expectedNextOut, nextOut);
        }
    }

    public class TempDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "NexusTest_" + System.IO.Path.GetRandomFileName());

        public TempDirectory()
        {
            Directory.CreateDirectory(Path);
        }

        public void Dispose()
        {
            try { Directory.Delete(Path, true); }
            catch { }
        }
    }
}
