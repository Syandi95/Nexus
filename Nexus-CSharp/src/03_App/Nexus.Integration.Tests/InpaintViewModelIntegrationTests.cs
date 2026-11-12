using System;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Nexus.App.Services;
using Nexus.App.ViewModels;
using Nexus.Contracts;
using Nexus.Core.HomeSettings.Services;
using Nexus.Engine.ColorManagement;
using Nexus.Engine.ImageSubsystem.Pipelines;
using Nexus.Engine.ImageSubsystem.Services;
using Xunit;

namespace Nexus.Integration.Tests;

public sealed class InpaintViewModelIntegrationTests
{
    private static readonly byte[] _minimalPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

    private static string CreateTempPng(string dir)
    {
        var path = Path.Combine(dir, Path.GetRandomFileName() + ".png");
        File.WriteAllBytes(path, _minimalPng);
        return path;
    }

    private static (ImageSubsystemViewModel vm, Mock<IImageSubsystemWorkflow> workflowMock) CreateInpaintTestFixture()
    {
        var workflowMock = new Mock<IImageSubsystemWorkflow>();
        var genericEngineMock = new Mock<IGenericImageEngine>();
        var cmykMock = new Mock<ICmykProfileManager>();
        var settingsMock = new Mock<ISettingsManagerService>();
        settingsMock
            .Setup(s => s.ModelRootFolder)
            .Returns(Path.Combine(Path.GetTempPath(), "NexusTest", "Models"));

        var vm = new ImageSubsystemViewModel(
            workflowMock.Object,
            genericEngineMock.Object,
            cmykMock.Object,
            () => Mock.Of<IColorManagementService>(),
            settingsMock.Object);

        return (vm, workflowMock);
    }

    [Fact]
    public void SelectMode_Inpaint_UpdatesPropertiesAndActionLabelsCorrectly()
    {
        var (vm, _) = CreateInpaintTestFixture();

        vm.SelectedMode = "Inpaint";

        Assert.True(vm.IsInpaintMode);
        Assert.False(vm.IsRemoveBgMode);
        Assert.False(vm.IsUpscaleMode);
        Assert.Equal("Hapus Objek", vm.PrimaryActionButtonText);
        Assert.Equal("Clean", vm.PrimaryActionIcon);
        Assert.Equal("Hapus objek yang dicoret dari gambar", vm.PrimaryActionToolTip);
    }

    [Fact]
    public void BrushControls_ToggleAndResize_UpdatesState()
    {
        var (vm, _) = CreateInpaintTestFixture();

        Assert.True(vm.IsBrushMode);
        Assert.False(vm.IsEraserMode);

        vm.SetEraserModeCommand.Execute(null);
        Assert.True(vm.IsEraserMode);
        Assert.False(vm.IsBrushMode);

        vm.SetBrushModeCommand.Execute(null);
        Assert.False(vm.IsEraserMode);
        Assert.True(vm.IsBrushMode);

        vm.BrushSize = 65.0;
        Assert.Equal(65.0, vm.BrushSize);
    }

    [Fact]
    public async Task ProcessInpaint_ExecutesWorkflowWithCorrectTier()
    {
        using var tempDir = new TempDirectory();
        var imagePath = CreateTempPng(tempDir.Path);

        var (vm, workflowMock) = CreateInpaintTestFixture();
        vm.SelectedMode = "Inpaint";
        vm.LoadImage(imagePath);

        var dummyMask = new Bitmap(10, 10);
        vm.MaskProvider = () => new Bitmap(dummyMask);

        workflowMock
            .Setup(w => w.InpaintAsync(
                It.IsAny<string>(),
                It.IsAny<Bitmap>(),
                InpaintExecutionTier.Auto,
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, Bitmap _, InpaintExecutionTier _, string outPath, CancellationToken _) =>
            {
                File.WriteAllBytes(outPath, _minimalPng);
                return outPath;
            });

        // 1. Normal mode (Auto tier)
        vm.IsHighDetailMode = false;
        await vm.ExecutePrimaryActionAsync();

        workflowMock.Verify(w => w.InpaintAsync(
            imagePath,
            It.IsAny<Bitmap>(),
            InpaintExecutionTier.Auto,
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Once);

        Assert.True(vm.HasResult);
        Assert.Equal("Objek berhasil dihapus.", vm.StatusText);

        // 2. High detail mode (DualPass tier)
        workflowMock
            .Setup(w => w.InpaintAsync(
                It.IsAny<string>(),
                It.IsAny<Bitmap>(),
                InpaintExecutionTier.HighDetailDualPass,
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, Bitmap _, InpaintExecutionTier _, string outPath, CancellationToken _) =>
            {
                File.WriteAllBytes(outPath, _minimalPng);
                return outPath;
            });

        vm.IsHighDetailMode = true;
        vm.LoadImage(imagePath); // Reload to reset state to ImageLoaded
        await vm.ExecutePrimaryActionAsync();

        workflowMock.Verify(w => w.InpaintAsync(
            imagePath,
            It.IsAny<Bitmap>(),
            InpaintExecutionTier.HighDetailDualPass,
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Once);

        Assert.True(vm.HasResult);

        dummyMask.Dispose();
    }
}
