using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Moq;
using Nexus.App.Services;
using Nexus.App.ViewModels;
using Nexus.Contracts;
using Nexus.Core.HomeSettings.Services;
using Nexus.Engine.ColorManagement;
using Nexus.Engine.ImageSubsystem.Ocr;
using Nexus.Engine.ImageSubsystem.Services;
using Xunit;

namespace Nexus.Integration.Tests;

public class ImageSubsystemOneOcrViewModelTests
{
    private const string BenchmarkDir = @"D:\NEXUS\test\OcrBenchmark";
    private static readonly string DocumentPath = Path.Combine(BenchmarkDir, "5_document_indonesian.png");
    private static readonly string BannerPath = Path.Combine(BenchmarkDir, "3_banner_poster.png");

    private static ImageSubsystemViewModel CreateTestViewModel(INotificationService? notifService = null)
    {
        var pipelineMock = new Mock<IImagePipelineService>();
        var rmbgMock = new Mock<IRemoveBackgroundService>();
        var upscaleMock = new Mock<IUpscaleService>();
        var onnxMock = new Mock<IOnnxModelManager>();
        var workflowMock = new Mock<IImageSubsystemWorkflow>();
        var genericEngineMock = new Mock<IGenericImageEngine>();
        var cmykMock = new Mock<ICmykProfileManager>();
        var settingsMock = new Mock<ISettingsManagerService>();
        settingsMock.Setup(s => s.ModelRootFolder).Returns(Path.Combine(Path.GetTempPath(), "NexusTest", "Models"));
        var mobileSamMock = new Mock<IMobileSamService>();

        var vm = new ImageSubsystemViewModel(
            workflowMock.Object,
            genericEngineMock.Object,
            cmykMock.Object,
            () => new Mock<IColorManagementService>().Object,
            settingsMock.Object,
            mobileSamMock.Object,
            notifService);

        return vm;
    }

    [Fact]
    public async Task Test_ViewModel_OneOcr_Scan_PopulatesWords_AndBuildsGeometryMask()
    {
        if (!File.Exists(DocumentPath)) return;

        var vm = CreateTestViewModel();
        vm.LoadImage(DocumentPath);

        Assert.True(vm.IsImageLoaded);
        Assert.False(vm.HasOcrWords);
        Assert.False(vm.HasOverlayBoxes);

        // Execute OCR Scan
        await vm.ExecuteCopyTextOcrAsync();

        // Assert words populated
        Assert.True(vm.HasOcrWords);
        Assert.True(vm.HasOverlayBoxes);
        Assert.True(vm.OcrWords.Count > 0);
        Assert.NotNull(vm.OcrMaskGeometry);

        // Assert status
        Assert.Contains("kata", vm.StatusText);

        // Assert words have valid geometry and reading order
        for (int i = 0; i < vm.OcrWords.Count; i++)
        {
            var w = vm.OcrWords[i];
            Assert.Equal(i, w.GlobalIndex);
            Assert.False(string.IsNullOrWhiteSpace(w.Text));
            Assert.True(w.Width > 0);
            Assert.True(w.Height > 0);
            Assert.NotNull(w.PolygonGeometry);
            Assert.False(w.PolygonGeometry.IsEmpty());
        }
    }

    [Fact]
    public async Task Test_ViewModel_OneOcr_Selection_Single_Range_All_AndCopy()
    {
        Assert.True(File.Exists(DocumentPath), $"OCR benchmark fixture missing: {DocumentPath}");

        var vm = CreateTestViewModel();
        vm.LoadImage(DocumentPath);
        await vm.ExecuteCopyTextOcrAsync();

        Assert.Equal(126, vm.OcrWords.Count);

        // 1. Single selection
        var firstWord = vm.OcrWords[0];
        vm.SelectWord(firstWord, multiSelect: false);
        Assert.True(firstWord.IsSelected);
        Assert.True(vm.HasSelectedOcrWords);
        Assert.True(vm.HasSelectedOcrBoxes);
        Assert.False(vm.OcrWords[1].IsSelected);

        // 2. Range selection (reading order)
        vm.SelectWordRange(0, 3);
        Assert.True(vm.OcrWords[0].IsSelected);
        Assert.True(vm.OcrWords[1].IsSelected);
        Assert.True(vm.OcrWords[2].IsSelected);
        Assert.True(vm.OcrWords[3].IsSelected);
        Assert.False(vm.OcrWords[4].IsSelected);

        // 3. Select All
        vm.SelectAllWords();
        Assert.True(vm.OcrWords.All(w => w.IsSelected));

        // 4. Clear Selection
        vm.ClearWordSelection();
        Assert.True(vm.OcrWords.All(w => !w.IsSelected));
        Assert.False(vm.HasSelectedOcrWords);
        Assert.False(vm.HasSelectedOcrBoxes);

        // 5. Select range and copy selected
        vm.SelectWordRange(0, 2);
        var expectedSelectedWords = vm.OcrWords.Take(3).Select(w => w.Text).ToArray();
        var expectedAllText = string.Join(Environment.NewLine, vm.OcrWords
            .GroupBy(w => w.LineIndex)
            .OrderBy(g => g.Key)
            .Select(g => string.Join(" ", g.OrderBy(w => w.WordIndex).Select(w => w.Text))));
        var copiedTexts = new List<string>();
        int legacyCopyCalls = 0;
        var previousNativeHook = NexusClipboard.CustomNativeSetTextAction;
        var previousLegacyHook = NexusClipboard.CustomSetTextAction;

        // Clipboard hooks are process-global; AssemblyInfo keeps integration tests serial.
        // Capture both paths so this test never writes to the desktop clipboard.
        try
        {
            NexusClipboard.CustomNativeSetTextAction = text => copiedTexts.Add(text);
            NexusClipboard.CustomSetTextAction = _ => legacyCopyCalls++;

            vm.CopySelectedOcrTextCommand.Execute(null);
            Assert.Single(copiedTexts);
            Assert.Equal(expectedSelectedWords, copiedTexts[0].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            Assert.Contains("3 kata disalin", vm.StatusText);
            Assert.Equal(0, legacyCopyCalls);

            // 6. Copy All: verify the complete payload separately from success status.
            vm.CopyAllOcrTextCommand.Execute(null);
            Assert.Equal(2, copiedTexts.Count);
            Assert.Equal(expectedAllText, copiedTexts[1]);
            Assert.Contains("Semua teks (126 kata) disalin", vm.StatusText);
            Assert.Equal(0, legacyCopyCalls);
        }
        finally
        {
            NexusClipboard.CustomNativeSetTextAction = previousNativeHook;
            NexusClipboard.CustomSetTextAction = previousLegacyHook;
        }
    }

    [Fact]
    public async Task Test_ViewModel_OneOcr_SessionCache_ReopenDoesNotReScan()
    {
        if (!File.Exists(DocumentPath)) return;

        var vm = CreateTestViewModel();
        vm.LoadImage(DocumentPath);

        // Scan 1
        await vm.ExecuteCopyTextOcrAsync();
        int initialCount = vm.OcrWords.Count;
        Assert.True(initialCount > 0);

        // Close OCR
        vm.CloseOcrCommand.Execute(null);
        Assert.False(vm.HasOverlayBoxes);
        Assert.Empty(vm.OcrWords);

        // Scan 2 (same image)
        await vm.ExecuteCopyTextOcrAsync();

        // Must restore immediately from cache
        Assert.True(vm.HasOverlayBoxes);
        Assert.Equal(initialCount, vm.OcrWords.Count);
        Assert.Contains("dari cache", vm.StatusText);
    }

    [Fact]
    public async Task Test_ViewModel_OneOcr_CacheInvalidation_WhenImageChanges()
    {
        if (!File.Exists(DocumentPath)) return;

        var vm = CreateTestViewModel();
        vm.LoadImage(DocumentPath);
        await vm.ExecuteCopyTextOcrAsync();
        Assert.True(vm.HasOcrWords);

        // Invalidate cache (simulating crop or image reset)
        vm.InvalidateOcrCache();

        Assert.False(vm.HasOcrWords);
        Assert.False(vm.HasOverlayBoxes);
        Assert.Null(vm.OcrMaskGeometry);
    }
}
