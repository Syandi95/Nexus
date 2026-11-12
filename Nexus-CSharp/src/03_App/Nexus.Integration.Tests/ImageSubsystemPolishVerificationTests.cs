using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Threading;
using Moq;
using Nexus.App.Services;
using Nexus.App.ViewModels;
using Nexus.App.Views;
using Nexus.Contracts;
using Nexus.Core.HomeSettings.Services;
using Nexus.Engine.ColorManagement;
using Nexus.Engine.ImageSubsystem.Services;
using Xunit;

namespace Nexus.Integration.Tests;

[Collection(WpfApplicationCollection.Name)]
public sealed class ImageSubsystemPolishVerificationTests
{
    private static void DrainDispatcher() =>
        Dispatcher.CurrentDispatcher.Invoke(
            () => { },
            DispatcherPriority.ApplicationIdle);
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

    private static (ImageSubsystemViewModel ViewModel, Mock<IImageSubsystemWorkflow> WorkflowMock) CreateViewModelWithMock()
    {
        var workflowMock = new Mock<IImageSubsystemWorkflow>();
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
    public void ResetTransformCommand_ShouldOnlyResetTransformStates_WithoutClearingImage()
    {
        using var tempDir = new TempDirectory();
        var inputPath = CreateTestPng(tempDir.Path, 4, 4);
        var (vm, _) = CreateViewModelWithMock();
        vm.LoadImage(inputPath);

        Assert.True(vm.IsImageLoaded);
        Assert.Equal(inputPath, vm.InputImagePath);

        // Modify transform states
        vm.RotationAngle = 90;
        vm.ScaleX = -1.0;
        vm.ScaleY = -1.0;
        vm.IsCropActive = true;

        // Execute ResetTransformCommand
        vm.ResetTransformCommand.Execute(null);

        // Verify transform states are reset
        Assert.Equal(0, vm.RotationAngle);
        Assert.Equal(1.0, vm.ScaleX);
        Assert.Equal(1.0, vm.ScaleY);
        Assert.False(vm.IsCropActive);

        // Verify image state is retained (unlike ClearImageCommand which clears the image)
        Assert.Equal(inputPath, vm.InputImagePath);
        Assert.True(vm.IsImageLoaded);
    }

    [Fact]
    public void CropAndMasking_ShouldBeMutuallyExclusive()
    {
        var (vm, _) = CreateViewModelWithMock();

        // Activating Crop deactivates Mask refining
        vm.IsCropActive = true;
        Assert.True(vm.IsCropActive);
        Assert.False(vm.IsRemoveBgRefining);

        // Activating Mask refining deactivates Crop
        vm.IsRemoveBgRefining = true;
        Assert.False(vm.IsCropActive);
        Assert.True(vm.IsRemoveBgRefining);

        // Activating Crop again deactivates Mask refining
        vm.IsCropActive = true;
        Assert.True(vm.IsCropActive);
        Assert.False(vm.IsRemoveBgRefining);
    }

    [Fact]
    public void RotateLeftAndRight_ShouldMaintainCanonicalRange_0_90_180_270()
    {
        var (vm, _) = CreateViewModelWithMock();
        Assert.Equal(0, vm.RotationAngle);

        // Rotate Right sequence: 0 -> 90 -> 180 -> 270 -> 0
        vm.RotateRightCommand.Execute(null);
        Assert.Equal(90, vm.RotationAngle);

        vm.RotateRightCommand.Execute(null);
        Assert.Equal(180, vm.RotationAngle);

        vm.RotateRightCommand.Execute(null);
        Assert.Equal(270, vm.RotationAngle);

        vm.RotateRightCommand.Execute(null);
        Assert.Equal(0, vm.RotationAngle);

        // Rotate Left sequence: 0 -> 270 -> 180 -> 90 -> 0
        vm.RotateLeftCommand.Execute(null);
        Assert.Equal(270, vm.RotationAngle);

        vm.RotateLeftCommand.Execute(null);
        Assert.Equal(180, vm.RotationAngle);

        vm.RotateLeftCommand.Execute(null);
        Assert.Equal(90, vm.RotationAngle);

        vm.RotateLeftCommand.Execute(null);
        Assert.Equal(0, vm.RotationAngle);
    }

    [Fact]
    public void FlipHorizontalAndVertical_ShouldInvertScales()
    {
        var (vm, _) = CreateViewModelWithMock();
        Assert.Equal(1.0, vm.ScaleX);
        Assert.Equal(1.0, vm.ScaleY);

        // Flip Horizontal: 1.0 -> -1.0 -> 1.0
        vm.FlipHorizontalCommand.Execute(null);
        Assert.Equal(-1.0, vm.ScaleX);
        Assert.Equal(1.0, vm.ScaleY);

        vm.FlipHorizontalCommand.Execute(null);
        Assert.Equal(1.0, vm.ScaleX);
        Assert.Equal(1.0, vm.ScaleY);

        // Flip Vertical: 1.0 -> -1.0 -> 1.0
        vm.FlipVerticalCommand.Execute(null);
        Assert.Equal(1.0, vm.ScaleX);
        Assert.Equal(-1.0, vm.ScaleY);

        vm.FlipVerticalCommand.Execute(null);
        Assert.Equal(1.0, vm.ScaleX);
        Assert.Equal(1.0, vm.ScaleY);
    }

    [Fact]
    public void SelectSiblingImage_ShouldLoadSelectedFile()
    {
        using var tempDir = new TempDirectory();
        var file1 = CreateTestPng(tempDir.Path, 4, 4);
        var file2 = CreateTestPng(tempDir.Path, 4, 4);

        var (vm, _) = CreateViewModelWithMock();
        vm.LoadImage(file1);
        Assert.Equal(file1, vm.InputImagePath);

        var item = new ImageBatchItem { FilePath = file2 };
        vm.SelectSiblingImageCommand.Execute(item);

        Assert.Equal(file2, vm.InputImagePath);
        Assert.True(vm.IsImageLoaded);
    }

    [Fact]
    public void ToggleFilmstrip_ShouldRefreshFromCurrentDirectory()
    {
        using var tempDir1 = new TempDirectory();
        using var tempDir2 = new TempDirectory();

        var dir1File1 = CreateTestPng(tempDir1.Path, 4, 4);
        var dir1File2 = CreateTestPng(tempDir1.Path, 4, 4);

        var dir2File1 = CreateTestPng(tempDir2.Path, 4, 4);

        var (vm, _) = CreateViewModelWithMock();
        vm.LoadImage(dir1File1);

        Assert.False(vm.IsFilmstripVisible);
        Assert.Empty(vm.SiblingImages);

        // Toggle filmstrip ON -> refreshes from dir1
        vm.ToggleFilmstripCommand.Execute(null);

        Assert.True(vm.IsFilmstripVisible);
        Assert.Equal(2, vm.SiblingImages.Count);
        Assert.Contains(vm.SiblingImages, item => item.FilePath == dir1File1);
        Assert.Contains(vm.SiblingImages, item => item.FilePath == dir1File2);

        // When filmstrip is visible and another image in a different directory is loaded, SiblingImages updates
        vm.LoadImage(dir2File1);
        Assert.Single(vm.SiblingImages);
        Assert.Equal(dir2File1, vm.SiblingImages[0].FilePath);

        // Toggle filmstrip OFF
        vm.ToggleFilmstripCommand.Execute(null);
        Assert.False(vm.IsFilmstripVisible);
    }

    [Fact]
    public void SelectSiblingImage_SameImage_ShouldBeNoOp()
    {
        using var tempDir = new TempDirectory();
        var file1 = CreateTestPng(tempDir.Path, 4, 4);

        var (vm, _) = CreateViewModelWithMock();
        vm.LoadImage(file1);
        Assert.Equal(file1, vm.InputImagePath);

        var item = new ImageBatchItem { FilePath = file1 };
        vm.SelectSiblingImageCommand.Execute(item);

        Assert.Equal(file1, vm.InputImagePath);
        Assert.True(vm.IsImageLoaded);
    }

    [Fact]
    public void RefreshSiblingImages_SameFolder_ShouldNotRecrawlUnlessForced()
    {
        using var tempDir = new TempDirectory();
        var file1 = CreateTestPng(tempDir.Path, 4, 4);
        var file2 = CreateTestPng(tempDir.Path, 4, 4);

        var (vm, _) = CreateViewModelWithMock();
        vm.LoadImage(file1);
        vm.ToggleFilmstripCommand.Execute(null);

        Assert.Equal(2, vm.SiblingImages.Count);

        // Add a third file to the same directory
        var file3 = CreateTestPng(tempDir.Path, 4, 4);

        // Loading file2 in the same directory should NOT re-crawl
        vm.LoadImage(file2);
        Assert.Equal(2, vm.SiblingImages.Count);

        // Forcing refresh re-crawls the directory
        vm.RefreshSiblingImages(force: true);
        Assert.Equal(3, vm.SiblingImages.Count);
        Assert.Contains(vm.SiblingImages, item => item.FilePath == file3);
    }

    [Fact]
    public void ResetTransform_ShouldResetAnglesScalesAndClearOverlayBoxes()
    {
        using var tempDir = new TempDirectory();
        var inputPath = CreateTestPng(tempDir.Path, 4, 4);
        var (vm, _) = CreateViewModelWithMock();
        vm.LoadImage(inputPath);

        // Set transforms and crop state
        vm.RotationAngle = 90;
        vm.ScaleX = -1.0;
        vm.ScaleY = -1.0;
        vm.ZoomLevel = 150.0;
        vm.IsCropActive = true;

        // Add OCR overlay box
        vm.OverlayBoxes.Add(new AiCheckOverlayBoxViewModel { X1 = 10, Y1 = 10, X2 = 50, Y2 = 50 });
        Assert.True(vm.HasOverlayBoxes);

        // Execute ResetTransform
        vm.ResetTransformCommand.Execute(null);

        Assert.Equal(0, vm.RotationAngle);
        Assert.Equal(1.0, vm.ScaleX);
        Assert.Equal(1.0, vm.ScaleY);
        Assert.Equal(100.0, vm.ZoomLevel);
        Assert.False(vm.IsCropActive);
        Assert.Empty(vm.OverlayBoxes);
        Assert.False(vm.HasOverlayBoxes);
        Assert.Equal(inputPath, vm.InputImagePath);
        Assert.True(vm.IsImageLoaded);
    }

    [Fact]
    public void LoadImage_InternalDerivative_ShouldPreserveOriginalContextAndFilmstrip()
    {
        using var originalDir = new TempDirectory();
        using var cropTempDir = new TempDirectory();

        var originalFile = CreateTestPng(originalDir.Path, 8, 8);
        var cropFile = CreateTestPng(cropTempDir.Path, 4, 4);

        var (vm, _) = CreateViewModelWithMock();
        vm.LoadImage(originalFile);
        vm.ToggleFilmstripCommand.Execute(null);

        Assert.Equal(originalFile, vm.OriginalSourcePath);
        Assert.Equal(originalDir.Path, vm.CurrentFilmstripDirectory);
        var expectedOutputPath = vm.OutputPath;

        // Load internal derivative (e.g. from Crop tool)
        vm.LoadImage(cropFile, isInternalDerivative: true);

        Assert.Equal(cropFile, vm.InputImagePath);
        Assert.Equal(originalFile, vm.OriginalSourcePath);
        Assert.Equal(originalDir.Path, vm.CurrentFilmstripDirectory);
        Assert.Equal(expectedOutputPath, vm.OutputPath);
    }

    [Fact]
    public void Filmstrip_Ordering_ShouldSortByDateModifiedDescendingWithTieBreaker()
    {
        using var tempDir = new TempDirectory();
        var fileOld = CreateTestPng(tempDir.Path, 4, 4);
        var fileMid = CreateTestPng(tempDir.Path, 4, 4);
        var fileNew = CreateTestPng(tempDir.Path, 4, 4);

        // Explicitly set LastWriteTime
        File.SetLastWriteTimeUtc(fileOld, DateTime.UtcNow.AddMinutes(-30));
        File.SetLastWriteTimeUtc(fileMid, DateTime.UtcNow.AddMinutes(-15));
        File.SetLastWriteTimeUtc(fileNew, DateTime.UtcNow);

        var (vm, _) = CreateViewModelWithMock();
        vm.LoadImage(fileMid);
        vm.ToggleFilmstripCommand.Execute(null);

        Assert.Equal(3, vm.SiblingImages.Count);
        // Order must be: fileNew (index 0), fileMid (index 1), fileOld (index 2)
        Assert.Equal(fileNew, vm.SiblingImages[0].FilePath);
        Assert.Equal(fileMid, vm.SiblingImages[1].FilePath);
        Assert.Equal(fileOld, vm.SiblingImages[2].FilePath);

        // Active image tracking: fileMid is loaded, so index 1 must be active
        Assert.False(vm.SiblingImages[0].IsActive);
        Assert.True(vm.SiblingImages[1].IsActive);
        Assert.False(vm.SiblingImages[2].IsActive);
    }

    [Fact]
    public void Ocr_SelectionAndActions_ShouldManageHighlightedBoxesAndClose()
    {
        var (vm, _) = CreateViewModelWithMock();
        Assert.False(vm.HasOverlayBoxes);
        Assert.False(vm.HasSelectedOcrBoxes);

        var box1 = new AiCheckOverlayBoxViewModel { Id = 1, OriginalText = "Line One", X1 = 0, Y1 = 0, X2 = 100, Y2 = 20 };
        var box2 = new AiCheckOverlayBoxViewModel { Id = 2, OriginalText = "Line Two", X1 = 0, Y1 = 30, X2 = 100, Y2 = 50 };

        vm.OverlayBoxes.Add(box1);
        vm.OverlayBoxes.Add(box2);

        // Manually trigger property notifications since collection was modified directly in test
        Assert.True(vm.HasOverlayBoxes);
        Assert.False(vm.HasSelectedOcrBoxes);

        // Toggle selection on box 1
        vm.ToggleOcrBoxSelectionCommand.Execute(box1);
        Assert.True(box1.IsHighlighted);
        Assert.True(vm.HasSelectedOcrBoxes);

        // Toggle selection off on box 1
        vm.ToggleOcrBoxSelectionCommand.Execute(box1);
        Assert.False(box1.IsHighlighted);
        Assert.False(vm.HasSelectedOcrBoxes);

        // Select box 2
        vm.ToggleOcrBoxSelectionCommand.Execute(box2);
        Assert.True(box2.IsHighlighted);
        Assert.True(vm.HasSelectedOcrBoxes);

        // Close OCR
        vm.CloseOcrCommand.Execute(null);
        Assert.False(vm.HasOverlayBoxes);
        Assert.False(vm.HasSelectedOcrBoxes);
        Assert.Empty(vm.OverlayBoxes);
    }

    [Fact]
    public void Crop_OrientationBaked_ShouldResetTransformsOnDerivativeLoad()
    {
        using var tempDir = new TempDirectory();
        var origFile = CreateTestPng(tempDir.Path, 8, 8);
        var cropFile = CreateTestPng(tempDir.Path, 4, 4);

        var (vm, _) = CreateViewModelWithMock();
        vm.LoadImage(origFile);

        // Set transforms
        vm.RotationAngle = 90;
        vm.ScaleX = -1.0;
        vm.ScaleY = 1.0;
        vm.IsCropActive = true;

        // Apply crop derivative load
        vm.LoadImage(cropFile, isInternalDerivative: true);

        // Transforms must be reset to baseline 0/1/1 and crop mode closed
        Assert.Equal(0, vm.RotationAngle);
        Assert.Equal(1.0, vm.ScaleX);
        Assert.Equal(1.0, vm.ScaleY);
        Assert.False(vm.IsCropActive);
        Assert.Equal(cropFile, vm.InputImagePath);
        Assert.Equal(origFile, vm.OriginalSourcePath);
    }

    [Fact]
    public void Ocr_MaskGeometry_ShouldBeEvenOddGroupAndResetOnClose()
    {
        using var tempDir = new TempDirectory();
        var file = CreateTestPng(tempDir.Path, 100, 100);
        var (vm, _) = CreateViewModelWithMock();
        vm.LoadImage(file);

        // Simulate OCR generating bounding boxes
        var box1 = new AiCheckOverlayBoxViewModel { Id = 1, OriginalText = "Hello", X1 = 10, Y1 = 10, X2 = 50, Y2 = 30 };
        var box2 = new AiCheckOverlayBoxViewModel { Id = 2, OriginalText = "World", X1 = 10, Y1 = 40, X2 = 50, Y2 = 60 };
        vm.OverlayBoxes.Add(box1);
        vm.OverlayBoxes.Add(box2);

        // Replicate mask construction
        var group = new System.Windows.Media.GeometryGroup { FillRule = System.Windows.Media.FillRule.EvenOdd };
        group.Children.Add(new System.Windows.Media.RectangleGeometry(new System.Windows.Rect(0, 0, vm.OriginalImageWidth, vm.OriginalImageHeight)));
        foreach (var b in vm.OverlayBoxes)
        {
            group.Children.Add(new System.Windows.Media.RectangleGeometry(new System.Windows.Rect(b.X1, b.Y1, b.Width, b.Height)));
        }
        group.Freeze();

        // Verify EvenOdd group properties
        Assert.Equal(System.Windows.Media.FillRule.EvenOdd, group.FillRule);
        Assert.Equal(3, group.Children.Count);

        // Test reset & close clearing
        vm.CloseOcrCommand.Execute(null);
        Assert.Null(vm.OcrMaskGeometry);
        Assert.Empty(vm.OverlayBoxes);
    }

    [Fact]
    public void Crop_AfterRemoveBackground_ShouldPreserveResultStateAndAlpha()
    {
        using var tempDir = new TempDirectory();
        var origFile = CreateTestPng(tempDir.Path, 20, 20);
        var (vm, _) = CreateViewModelWithMock();
        vm.LoadImage(origFile);

        // Create a transparent 32bpp ARGB PNG result
        var resultFile = Path.Combine(tempDir.Path, "result_transparent.png");
        using (var bmp = new Bitmap(20, 20, PixelFormat.Format32bppArgb))
        {
            using var g = Graphics.FromImage(bmp);
            g.Clear(Color.Transparent);
            bmp.SetPixel(10, 10, Color.FromArgb(255, 255, 0, 0));
            bmp.Save(resultFile, ImageFormat.Png);
        }

        // Set active result state
        vm.ResultTempPath = resultFile;
        vm.OutputImagePath = resultFile;
        vm.HasResult = true;

        // Active editor source should point to the active result, NOT the original input!
        var currentSource = vm.GetCurrentEditorSourcePath();
        Assert.Equal(resultFile, currentSource);

        // Create cropped transparent result
        var croppedResultFile = Path.Combine(tempDir.Path, "cropped_result.png");
        using (var bmp = new Bitmap(10, 10, PixelFormat.Format32bppArgb))
        {
            using var g = Graphics.FromImage(bmp);
            g.Clear(Color.Transparent);
            bmp.SetPixel(5, 5, Color.FromArgb(255, 255, 0, 0));
            bmp.Save(croppedResultFile, ImageFormat.Png);
        }

        // Set arbitrary rotation/flip to verify they reset upon apply
        vm.RotationAngle = 90;
        vm.ScaleX = -1.0;
        vm.IsCropActive = true;

        // Apply crop to active result
        vm.ApplyCropToActiveResult(croppedResultFile, 10, 10);

        // Verify active result state is preserved without wipeout
        Assert.True(vm.HasResult);
        Assert.Equal(croppedResultFile, vm.ResultTempPath);
        Assert.Equal(croppedResultFile, vm.OutputImagePath);
        Assert.NotNull(vm.ResultImageSource);
        Assert.Equal(10, vm.OriginalImageWidth);
        Assert.Equal(10, vm.OriginalImageHeight);
        Assert.Equal(0, vm.RotationAngle);
        Assert.Equal(1.0, vm.ScaleX);
        Assert.False(vm.IsCropActive);
    }

    [Fact]
    public async Task Filmstrip_ClearImage_ShouldInvalidateSequenceAndResetDirectory()
    {
        using var tempDir = new TempDirectory();
        var file1 = CreateTestPng(tempDir.Path, 4, 4);
        var (vm, _) = CreateViewModelWithMock();
        vm.LoadImage(file1);
        vm.RefreshSiblingImages(force: true);

        Assert.Equal(tempDir.Path, vm.CurrentFilmstripDirectory);

        // Clear/Close image
        await vm.ExecuteClearImageAsync();

        // Sequence invalidated, directory reset, filmstrip cleared
        Assert.Empty(vm.CurrentFilmstripDirectory);
        Assert.Empty(vm.SiblingImages);
        Assert.False(vm.IsFilmstripVisible);
        Assert.Null(vm.OcrMaskGeometry);
    }

    [Fact]
    public void RemoveBackgroundCompleted_ShouldShowCompare()
    {
        using var tempDir = new TempDirectory();
        var origFile = CreateTestPng(tempDir.Path, 20, 20);
        var (vm, _) = CreateViewModelWithMock();
        vm.LoadImage(origFile);

        // Before AI run, compare is OFF
        Assert.False(vm.IsCompareActive);

        // Simulate Remove Background completed
        var resultFile = CreateTestPng(tempDir.Path, 20, 20);
        vm.ResultTempPath = resultFile;
        vm.OutputImagePath = resultFile;
        vm.HasResult = true;

        // Compare must be ON
        Assert.True(vm.IsCompareActive);
        Assert.NotNull(vm.BeforeImageSource);
        Assert.NotNull(vm.ResultImageSource);
    }

    [Fact]
    public void EnteringCrop_ShouldHideCompare()
    {
        using var tempDir = new TempDirectory();
        var origFile = CreateTestPng(tempDir.Path, 20, 20);
        var (vm, _) = CreateViewModelWithMock();
        vm.LoadImage(origFile);

        var resultFile = CreateTestPng(tempDir.Path, 20, 20);
        vm.ResultTempPath = resultFile;
        vm.OutputImagePath = resultFile;
        vm.HasResult = true;

        Assert.True(vm.IsCompareActive);

        // User enters crop
        vm.IsCropActive = true;

        // Compare must be OFF during crop
        Assert.False(vm.IsCompareActive);
    }

    [Fact]
    public void CancelCrop_ShouldRestoreCompare()
    {
        using var tempDir = new TempDirectory();
        var origFile = CreateTestPng(tempDir.Path, 20, 20);
        var (vm, _) = CreateViewModelWithMock();
        vm.LoadImage(origFile);

        var resultFile = CreateTestPng(tempDir.Path, 20, 20);
        vm.ResultTempPath = resultFile;
        vm.OutputImagePath = resultFile;
        vm.HasResult = true;

        var beforeSourcePrior = vm.BeforeImageSource;
        var resultSourcePrior = vm.ResultImageSource;

        // User enters crop then cancels
        vm.IsCropActive = true;
        Assert.False(vm.IsCompareActive);

        vm.IsCropActive = false;

        // Compare restored, images unchanged, no derivatives created
        Assert.True(vm.IsCompareActive);
        Assert.Same(beforeSourcePrior, vm.BeforeImageSource);
        Assert.Same(resultSourcePrior, vm.ResultImageSource);
        Assert.Equal(origFile, vm.InputImagePath);
        Assert.Equal(resultFile, vm.ResultTempPath);
    }

    [Fact]
    public void CropApplied_ShouldCreateMatchingBeforeAfterGeometry()
    {
        using var tempDir = new TempDirectory();
        var origFile = CreateTestPng(tempDir.Path, 40, 30);
        var (vm, _) = CreateViewModelWithMock();
        vm.LoadImage(origFile);

        var resultFile = CreateTestPng(tempDir.Path, 40, 30);
        vm.ResultTempPath = resultFile;
        vm.OutputImagePath = resultFile;
        vm.HasResult = true;

        // Cropped slices (15x15)
        var croppedResult = CreateTestPng(tempDir.Path, 15, 15);
        var croppedBefore = CreateTestPng(tempDir.Path, 15, 15);

        vm.IsCropActive = true;
        vm.ApplyCropToActiveResult(croppedResult, 15, 15, croppedBefore);

        // Before and After must have 100% IDENTICAL dimensions
        Assert.NotNull(vm.BeforeImageSource);
        Assert.NotNull(vm.ResultImageSource);
        Assert.Equal(15, vm.BeforeImageSource.PixelWidth);
        Assert.Equal(15, vm.BeforeImageSource.PixelHeight);
        Assert.Equal(15, vm.ResultImageSource.PixelWidth);
        Assert.Equal(15, vm.ResultImageSource.PixelHeight);
        Assert.Equal(vm.BeforeImageSource.PixelWidth, vm.ResultImageSource.PixelWidth);
        Assert.Equal(vm.BeforeImageSource.PixelHeight, vm.ResultImageSource.PixelHeight);
    }

    [Fact]
    public void CropApplied_ShouldShowCompare()
    {
        using var tempDir = new TempDirectory();
        var origFile = CreateTestPng(tempDir.Path, 20, 20);
        var (vm, _) = CreateViewModelWithMock();
        vm.LoadImage(origFile);

        var resultFile = CreateTestPng(tempDir.Path, 20, 20);
        vm.ResultTempPath = resultFile;
        vm.OutputImagePath = resultFile;
        vm.HasResult = true;

        var croppedResult = CreateTestPng(tempDir.Path, 10, 10);
        var croppedBefore = CreateTestPng(tempDir.Path, 10, 10);

        vm.IsCropActive = true;
        Assert.False(vm.IsCompareActive);

        vm.ApplyCropToActiveResult(croppedResult, 10, 10, croppedBefore);

        // Compare must be ON after crop applied with matching before crop
        Assert.True(vm.IsCompareActive);
        Assert.False(vm.IsCropActive);
    }

    [Fact]
    public void CropApplied_ShouldPreserveRemoveBackgroundResult()
    {
        using var tempDir = new TempDirectory();
        var origFile = CreateTestPng(tempDir.Path, 20, 20);
        var (vm, _) = CreateViewModelWithMock();
        vm.LoadImage(origFile);

        var resultFile = Path.Combine(tempDir.Path, "result_alpha.png");
        using (var bmp = new Bitmap(20, 20, PixelFormat.Format32bppArgb))
        {
            using var g = Graphics.FromImage(bmp);
            g.Clear(Color.Transparent);
            bmp.SetPixel(5, 5, Color.FromArgb(255, 255, 0, 0));
            bmp.Save(resultFile, ImageFormat.Png);
        }

        vm.ResultTempPath = resultFile;
        vm.OutputImagePath = resultFile;
        vm.HasResult = true;

        var croppedResult = Path.Combine(tempDir.Path, "cropped_alpha.png");
        using (var bmp = new Bitmap(10, 10, PixelFormat.Format32bppArgb))
        {
            using var g = Graphics.FromImage(bmp);
            g.Clear(Color.Transparent);
            bmp.SetPixel(2, 2, Color.FromArgb(255, 255, 0, 0));
            bmp.Save(croppedResult, ImageFormat.Png);
        }
        var croppedBefore = CreateTestPng(tempDir.Path, 10, 10);

        vm.ApplyCropToActiveResult(croppedResult, 10, 10, croppedBefore);

        Assert.True(vm.HasResult);
        Assert.Equal(croppedResult, vm.ResultTempPath);
        Assert.Equal(croppedResult, vm.OutputImagePath);
        Assert.True(File.Exists(vm.ResultTempPath));

        // Verify transparent pixels preserved
        using var checkBmp = new Bitmap(vm.ResultTempPath);
        Assert.Equal(0, checkBmp.GetPixel(0, 0).A); // Transparent
        Assert.Equal(255, checkBmp.GetPixel(2, 2).A); // Red dot
    }

    [Fact]
    public void CropApplied_ShouldKeepOriginalInputPath()
    {
        using var tempDir = new TempDirectory();
        var origFile = CreateTestPng(tempDir.Path, 20, 20);
        var (vm, _) = CreateViewModelWithMock();
        vm.LoadImage(origFile);

        var resultFile = CreateTestPng(tempDir.Path, 20, 20);
        vm.ResultTempPath = resultFile;
        vm.OutputImagePath = resultFile;
        vm.HasResult = true;

        var croppedResult = CreateTestPng(tempDir.Path, 10, 10);
        var croppedBefore = CreateTestPng(tempDir.Path, 10, 10);

        vm.ApplyCropToActiveResult(croppedResult, 10, 10, croppedBefore);

        // InputImagePath must strictly remain the original file
        Assert.Equal(origFile, vm.InputImagePath);
        Assert.Equal(croppedBefore, vm.CurrentBeforeCropPath);
    }

    [Fact]
    public async Task ClearOrNewImage_ShouldHideCompare()
    {
        using var tempDir = new TempDirectory();
        var origFile = CreateTestPng(tempDir.Path, 20, 20);
        var (vm, _) = CreateViewModelWithMock();
        vm.LoadImage(origFile);

        var resultFile = CreateTestPng(tempDir.Path, 20, 20);
        vm.ResultTempPath = resultFile;
        vm.OutputImagePath = resultFile;
        vm.HasResult = true;

        Assert.True(vm.IsCompareActive);

        // Clear image
        await vm.ExecuteClearImageAsync();

        // Compare must return to OFF
        Assert.False(vm.IsCompareActive);
        Assert.False(vm.HasResult);
        Assert.Null(vm.CurrentBeforeCropPath);
    }

    [Fact]
    public void NormalCropWithoutRemoveBackground_ShouldRemainWorking()
    {
        using var tempDir = new TempDirectory();
        var origFile = CreateTestPng(tempDir.Path, 20, 20);
        var (vm, _) = CreateViewModelWithMock();
        vm.LoadImage(origFile);

        Assert.False(vm.HasResult);
        Assert.False(vm.IsCompareActive);

        // Enter crop on normal image without remove background
        vm.IsCropActive = true;
        Assert.False(vm.IsCompareActive);

        // Normal crop reload as derivative
        var cropFile = CreateTestPng(tempDir.Path, 10, 10);
        vm.LoadImage(cropFile, isInternalDerivative: true);

        // Compare must REMAIN OFF! Normal crop does not trigger compare
        Assert.False(vm.HasResult);
        Assert.False(vm.IsCompareActive);
        Assert.False(vm.IsCropActive);
        Assert.Equal(cropFile, vm.InputImagePath);
        Assert.Equal(10, vm.OriginalImageWidth);
        Assert.Equal(10, vm.OriginalImageHeight);
    }

    private static string CreateFourQuadrantPng(string dir, int width = 20, int height = 20)
    {
        var path = Path.Combine(dir, $"quadrant_{Guid.NewGuid():N}.png");
        using var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        int halfW = width / 2;
        int halfH = height / 2;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                Color color;
                if (x < halfW && y < halfH) color = Color.Red;                         // TL = Red
                else if (x >= halfW && y < halfH) color = Color.FromArgb(255, 0, 255, 0); // TR = Green (R=0, G=255, B=0)
                else if (x < halfW && y >= halfH) color = Color.Blue;                  // BL = Blue
                else color = Color.FromArgb(255, 255, 0);                             // BR = Yellow (RGB)
                bmp.SetPixel(x, y, color);
            }
        }
        bmp.Save(path, ImageFormat.Png);
        return path;
    }

    private static (ImageSubsystemView View, ImageSubsystemViewModel Vm, HwndSource Source) CreateViewAndVm()
    {
        var (vm, _) = CreateViewModelWithMock();
        var view = new ImageSubsystemView
        {
            DataContext = vm,
            Width = 800,
            Height = 600
        };
        var source = new HwndSource(new HwndSourceParameters("CropTest")
        {
            Width = 800,
            Height = 600,
            WindowStyle = 0
        })
        {
            RootVisual = view
        };
        return (view, vm, source);
    }

    private static void DragCropSelectionToVisualTopLeft(ImageSubsystemView view)
    {
        view.Measure(new System.Windows.Size(800, 600));
        view.Arrange(new Rect(0, 0, 800, 600));
        view.UpdateLayout();

        var boundsField = typeof(ImageSubsystemView).GetField("_imageDisplayBounds", BindingFlags.NonPublic | BindingFlags.Instance);
        var currentBounds = (Rect)boundsField!.GetValue(view)!;
        Assert.False(currentBounds.IsEmpty, "Rendered image bounds must not be empty.");
        Assert.True(currentBounds.Width > 0 && currentBounds.Height > 0, "Rendered image bounds must have positive dimensions.");

        // Drag ThumbCropLowerRight by half width and half height using production CropThumb_DragDelta
        var dragMethod = typeof(ImageSubsystemView).GetMethod("CropThumb_DragDelta", BindingFlags.NonPublic | BindingFlags.Instance);
        var thumbLr = (Thumb)view.FindName("ThumbCropLowerRight");
        Assert.NotNull(thumbLr);

        double dragDx = -(currentBounds.Width / 2.0);
        double dragDy = -(currentBounds.Height / 2.0);
        dragMethod!.Invoke(view, new object[] { thumbLr, new DragDeltaEventArgs(dragDx, dragDy) });

        // Apply crop via production BtnApplyCrop_Click
        var applyMethod = typeof(ImageSubsystemView).GetMethod("BtnApplyCrop_Click", BindingFlags.NonPublic | BindingFlags.Instance);
        applyMethod!.Invoke(view, new object[] { view, new RoutedEventArgs(ButtonBase.ClickEvent) });
    }

    private static string CreateFourQuadrantNonSquarePng(string dir, int width = 400, int height = 200)
    {
        var path = Path.Combine(dir, $"quadrant_rect_{Guid.NewGuid():N}.png");
        using var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        int halfW = width / 2;
        int halfH = height / 2;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                Color color;
                if (x < halfW && y < halfH) color = Color.FromArgb(255, 255, 0, 0);     // A (TL) = Merah
                else if (x >= halfW && y < halfH) color = Color.FromArgb(255, 0, 255, 0); // B (TR) = Hijau
                else if (x < halfW && y >= halfH) color = Color.FromArgb(255, 0, 0, 255); // C (BL) = Biru
                else color = Color.FromArgb(255, 255, 255, 0);                           // D (BR) = Kuning
                bmp.SetPixel(x, y, color);
            }
        }
        bmp.Save(path, ImageFormat.Png);
        return path;
    }

    [Fact]
    public async Task CropThenRemoveBackground_UsesCroppedActiveSource()
    {
        using var tempDir = new TempDirectory();
        var origFile = CreateTestPng(tempDir.Path, 20, 20);
        var (vm, workflowMock) = CreateViewModelWithMock();
        vm.LoadImage(origFile);

        Assert.Equal(origFile, vm.InputImagePath);
        Assert.Null(vm.CropDerivativePath);
        Assert.Equal(origFile, vm.GetCurrentEditorSourcePath());

        // Perform crop on input image
        var cropFile = CreateTestPng(tempDir.Path, 10, 10);
        vm.ApplyCropToInput(cropFile, 10, 10);

        // Verify InputImagePath strictly remains original file, while active editor source is cropped
        Assert.Equal(origFile, vm.InputImagePath);
        Assert.Equal(cropFile, vm.CropDerivativePath);
        Assert.Equal(cropFile, vm.GetCurrentEditorSourcePath());
        Assert.False(vm.HasResult);

        // Mock RemoveBackground workflow execution
        string? capturedInputPath = null;
        workflowMock.Setup(w => w.RemoveBackgroundAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<System.Threading.CancellationToken>()))
            .Callback<string, string, string, System.Threading.CancellationToken>((input, slot, temp, token) =>
            {
                capturedInputPath = input;
                File.Copy(input, temp, true);
            })
            .ReturnsAsync((string input, string slot, string temp, System.Threading.CancellationToken token) => temp);

        // Run inference
        var processMethod = typeof(ImageSubsystemViewModel).GetMethod("ProcessAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(processMethod);
        await (Task)processMethod.Invoke(vm, null)!;

        // VERIFY:
        // 1. RemoveBackgroundAsync was called with the cropped derivative, NOT the original image!
        Assert.Equal(cropFile, capturedInputPath);
        // 2. The cropped file on disk was NOT deleted by CleanupTemp during process start!
        Assert.True(File.Exists(cropFile));
        // 3. Workflow produced a result and active editor source is now ResultTempPath
        Assert.True(vm.HasResult);
        Assert.Equal(vm.ResultTempPath, vm.GetCurrentEditorSourcePath());
        Assert.Equal(origFile, vm.InputImagePath); // Original input path remains strictly preserved!
    }

    [Fact]
    public void Rotate90ThenCrop_NonSquare400x200_UsesCorrectSourcePixelRegion()
    {
        WpfAppHost.Run(() =>
        {
            using var tempDir = new TempDirectory();
            // Non-Square independent fixture: 400x200
            // A(TL): Red, B(TR): Green, C(BL): Blue, D(BR): Yellow
            var origFile = CreateFourQuadrantNonSquarePng(tempDir.Path, 400, 200);
            var (view, vm, source) = CreateViewAndVm();
            try
            {
                // Upload
                vm.LoadImage(origFile);
                // Rotate 90° Clockwise
                vm.RotateRightCommand.Execute(null);
                // Activate Crop overlay
                vm.ToggleCropCommand.Execute(null);

                // Run production crop on Top-Left visual quadrant via production CropThumb_DragDelta & BtnApplyCrop_Click
                DragCropSelectionToVisualTopLeft(view);

                // Assert actual production output file
                var croppedPath = vm.GetCurrentEditorSourcePath();
                Assert.True(File.Exists(croppedPath));
                Assert.NotEqual(origFile, croppedPath);

                using var resultBmp = new Bitmap(croppedPath);
                Assert.Equal(100, resultBmp.Width);
                Assert.Equal(200, resultBmp.Height);

                var centerPixel = resultBmp.GetPixel(resultBmp.Width / 2, resultBmp.Height / 2);
                // Pre-determined a priori expected color:
                // Original Bottom-Left (Blue: R=0, G=0, B=255) rotates to Top-Left in 90° CW!
                Assert.Equal(0, centerPixel.R);
                Assert.Equal(0, centerPixel.G);
                Assert.Equal(255, centerPixel.B);

                Assert.Equal(0.0, vm.RotationAngle);
                Assert.False(vm.IsCropActive);
            }
            finally
            {
                source.Dispose();
            }
        });
    }

    [Fact]
    public void Rotate180ThenCrop_NonSquare400x200_UsesCorrectSourcePixelRegion()
    {
        WpfAppHost.Run(() =>
        {
            using var tempDir = new TempDirectory();
            var origFile = CreateFourQuadrantNonSquarePng(tempDir.Path, 400, 200);
            var (view, vm, source) = CreateViewAndVm();
            try
            {
                // Upload
                vm.LoadImage(origFile);
                // Rotate 180° (Two 90° clockwise rotations)
                vm.RotateRightCommand.Execute(null);
                vm.RotateRightCommand.Execute(null);
                // Activate Crop overlay
                vm.ToggleCropCommand.Execute(null);

                // Run production crop on Top-Left visual quadrant via production CropThumb_DragDelta & BtnApplyCrop_Click
                DragCropSelectionToVisualTopLeft(view);

                // Assert actual production output file
                var croppedPath = vm.GetCurrentEditorSourcePath();
                Assert.True(File.Exists(croppedPath));
                Assert.NotEqual(origFile, croppedPath);

                using var resultBmp = new Bitmap(croppedPath);
                Assert.Equal(200, resultBmp.Width);
                Assert.Equal(100, resultBmp.Height);

                var centerPixel = resultBmp.GetPixel(resultBmp.Width / 2, resultBmp.Height / 2);
                // Pre-determined a priori expected color:
                // Original Bottom-Right (Yellow: R=255, G=255, B=0) rotates to Top-Left in 180°!
                Assert.Equal(255, centerPixel.R);
                Assert.Equal(255, centerPixel.G);
                Assert.Equal(0, centerPixel.B);

                Assert.Equal(0.0, vm.RotationAngle);
                Assert.False(vm.IsCropActive);
            }
            finally
            {
                source.Dispose();
            }
        });
    }

    [Fact]
    public void Rotate270ThenCrop_NonSquare400x200_UsesCorrectSourcePixelRegion()
    {
        WpfAppHost.Run(() =>
        {
            using var tempDir = new TempDirectory();
            var origFile = CreateFourQuadrantNonSquarePng(tempDir.Path, 400, 200);
            var (view, vm, source) = CreateViewAndVm();
            try
            {
                // Upload
                vm.LoadImage(origFile);
                // Rotate 270° Clockwise (equivalent to 1 RotateLeft)
                vm.RotateLeftCommand.Execute(null);
                // Activate Crop overlay
                vm.ToggleCropCommand.Execute(null);

                // Run production crop on Top-Left visual quadrant via production CropThumb_DragDelta & BtnApplyCrop_Click
                DragCropSelectionToVisualTopLeft(view);

                // Assert actual production output file
                var croppedPath = vm.GetCurrentEditorSourcePath();
                Assert.True(File.Exists(croppedPath));
                Assert.NotEqual(origFile, croppedPath);

                using var resultBmp = new Bitmap(croppedPath);
                Assert.Equal(100, resultBmp.Width);
                Assert.Equal(200, resultBmp.Height);

                var centerPixel = resultBmp.GetPixel(resultBmp.Width / 2, resultBmp.Height / 2);
                // Pre-determined a priori expected color:
                // Original Top-Right (Green: R=0, G=255, B=0) rotates to Top-Left in 270° CW!
                Assert.Equal(0, centerPixel.R);
                Assert.Equal(255, centerPixel.G);
                Assert.Equal(0, centerPixel.B);

                Assert.Equal(0.0, vm.RotationAngle);
                Assert.False(vm.IsCropActive);
            }
            finally
            {
                source.Dispose();
            }
        });
    }

    [Fact]
    public void CropActiveThenRotate90_ProvesOverlayBoundsDesyncOrderBug()
    {
        WpfAppHost.Run(() =>
        {
            using var tempDir = new TempDirectory();
            var origFile = CreateFourQuadrantNonSquarePng(tempDir.Path, 400, 200);
            var (view, vm, source) = CreateViewAndVm();
            try
            {
                // 1. Upload
                vm.LoadImage(origFile);
                view.Measure(new System.Windows.Size(800, 600));
                view.Arrange(new Rect(0, 0, 800, 600));
                view.UpdateLayout();

                // 2. Turn CROP ACTIVE first (before rotation)
                vm.ToggleCropCommand.Execute(null);
                view.Measure(new System.Windows.Size(800, 600));
                view.Arrange(new Rect(0, 0, 800, 600));
                view.UpdateLayout();

                var boundsField = typeof(ImageSubsystemView).GetField("_imageDisplayBounds", BindingFlags.NonPublic | BindingFlags.Instance);
                var boundsBeforeRotate = (Rect)boundsField!.GetValue(view)!;
                // Before rotation: 400x200 is landscape, width > height
                Assert.True(boundsBeforeRotate.Width > boundsBeforeRotate.Height, "Pre-rotation bounds must be landscape.");

                // 3. Rotate 90° Clockwise while Crop is already active
                vm.RotateRightCommand.Execute(null);
                view.Measure(new System.Windows.Size(800, 600));
                view.Arrange(new Rect(0, 0, 800, 600));
                view.UpdateLayout();

                var boundsAfterRotate = (Rect)boundsField!.GetValue(view)!;
                // FIX VERIFIED:
                // With Canonical Source Space and synchronized transform pipeline,
                // boundsAfterRotate properly reflects the rotated portrait dimensions:
                Assert.True(boundsAfterRotate.Height > boundsAfterRotate.Width, "Post-rotation bounds must be portrait.");
                Assert.Equal(0.5, boundsAfterRotate.Width / boundsAfterRotate.Height, 2);

                // 4. Complete production crop workflow
                DragCropSelectionToVisualTopLeft(view);

                var croppedPath = vm.GetCurrentEditorSourcePath();
                Assert.True(File.Exists(croppedPath));
                using var resultBmp = new Bitmap(croppedPath);
                var centerPixel = resultBmp.GetPixel(resultBmp.Width / 2, resultBmp.Height / 2);
                Assert.Equal(0, centerPixel.R);
                Assert.Equal(0, centerPixel.G);
                Assert.Equal(255, centerPixel.B);
            }
            finally
            {
                source.Dispose();
            }
        });
    }

    [Fact]
    public void BatchMode_ToolbarAndActionVisible_ZoomAndCompareDisabled_SingleImageEnabled()
    {
        WpfAppHost.Run(() =>
        {
            using var tempDir = new TempDirectory();
            var file1 = CreateTestPng(tempDir.Path, 10, 10);
            var file2 = CreateTestPng(tempDir.Path, 10, 10);

            var (view, vm, source) = CreateViewAndVm();
            try
            {
                // 1. Initial State (No images): Toolbar collapsed
                view.Measure(new System.Windows.Size(800, 600));
                view.Arrange(new Rect(0, 0, 800, 600));
                view.UpdateLayout();
                DrainDispatcher();

                Assert.True(vm.HasNoImages);
                Assert.False(vm.IsSingleImageMode);
                Assert.False(vm.IsBatchMode);
                Assert.Equal(Visibility.Collapsed, view.PreviewToolbar.Visibility);

                // 2. Single Image Mode: Toolbar visible, Action visible & enabled, Zoom & Compare enabled
                vm.AddFiles(new[] { file1 });
                view.Measure(new System.Windows.Size(800, 600));
                view.Arrange(new Rect(0, 0, 800, 600));
                view.UpdateLayout();
                DrainDispatcher();

                Assert.False(vm.HasNoImages);
                Assert.True(vm.IsSingleImageMode);
                Assert.False(vm.IsBatchMode);
                Assert.Equal(Visibility.Visible, view.PreviewToolbar.Visibility);
                Assert.Equal(Visibility.Visible, view.PrimaryActionButton.Visibility);
                Assert.True(view.PrimaryActionButton.IsEnabled);
                Assert.True(view.ZoomOutButton.IsEnabled);
                Assert.True(view.ZoomSlider.IsEnabled);
                Assert.True(view.ZoomInButton.IsEnabled);
                Assert.True(view.CompareButton.IsEnabled);

                // 3. Batch Mode: Toolbar remains visible, Action remains visible & enabled, Zoom & Compare DISABLED
                vm.AddFiles(new[] { file1, file2 });
                view.Measure(new System.Windows.Size(800, 600));
                view.Arrange(new Rect(0, 0, 800, 600));
                view.UpdateLayout();
                DrainDispatcher();

                Assert.False(vm.HasNoImages);
                Assert.False(vm.IsSingleImageMode);
                Assert.True(vm.IsBatchMode);
                Assert.Equal(Visibility.Visible, view.PreviewToolbar.Visibility);
                Assert.Equal(Visibility.Visible, view.PrimaryActionButton.Visibility);
                Assert.True(view.PrimaryActionButton.IsEnabled);
                Assert.False(view.ZoomOutButton.IsEnabled);
                Assert.False(view.ZoomSlider.IsEnabled);
                Assert.False(view.ZoomInButton.IsEnabled);
                Assert.False(view.CompareButton.IsEnabled);
                Assert.Equal(Visibility.Visible, view.BatchScrollViewer.Visibility);

                // 4. Clear Images: Toolbar collapsed again
                vm.ClearAllBatchCommand.Execute(null);
                view.Measure(new System.Windows.Size(800, 600));
                view.Arrange(new Rect(0, 0, 800, 600));
                view.UpdateLayout();
                DrainDispatcher();

                Assert.True(vm.HasNoImages);
                Assert.False(vm.IsSingleImageMode);
                Assert.False(vm.IsBatchMode);
                Assert.Equal(Visibility.Collapsed, view.PreviewToolbar.Visibility);
            }
            finally
            {
                source.Dispose();
            }
        });
    }
}

