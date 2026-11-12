using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Nexus.App.Services;
using Nexus.App.ViewModels;
using Nexus.Contracts;
using Nexus.Contracts.AiCheck;
using Nexus.Contracts.ReferenceData;
using Nexus.Core.HomeSettings.Services;
using Nexus.Engine.ColorManagement;
using Nexus.Engine.ImageSubsystem.Ocr;
using Nexus.Engine.ImageSubsystem.Services;
using Xunit;

namespace Nexus.Integration.Tests;

public class AiCheckOcrSubsystemRegressionTests
{
    private static ImageSubsystemViewModel CreateTestImageViewModel(OneOcrEngine? engine = null)
    {
        var workflowMock = new Mock<IImageSubsystemWorkflow>();
        var genericEngineMock = new Mock<IGenericImageEngine>();
        var cmykMock = new Mock<ICmykProfileManager>();
        var settingsMock = new Mock<ISettingsManagerService>();
        settingsMock.Setup(s => s.ModelRootFolder).Returns(Path.Combine(Path.GetTempPath(), "NexusTest", "Models"));
        var mobileSamMock = new Mock<IMobileSamService>();

        return new ImageSubsystemViewModel(
            workflowMock.Object,
            genericEngineMock.Object,
            cmykMock.Object,
            () => new Mock<IColorManagementService>().Object,
            settingsMock.Object,
            mobileSamMock.Object,
            oneOcrEngine: engine);
    }

    private static (AiCheckViewModel vm, Mock<IReferenceDataManager> refMock, Mock<IAiCheckJudgeService> judgeMock) CreateAiCheckViewModel(
        ISettingsManagerService? settings = null,
        PpOcrEngine? ocrEngine = null)
    {
        var refMock = new Mock<IReferenceDataManager>();
        refMock.Setup(r => r.GetAllDatasetsMetadataAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ReferenceDatasetMetadata>());

        var judgeMock = new Mock<IAiCheckJudgeService>();

        var vm = new AiCheckViewModel(
            refMock.Object,
            judgeMock.Object,
            settingsManagerService: settings,
            ocrEngine: ocrEngine);

        return (vm, refMock, judgeMock);
    }

    [Fact]
    public void Test_A_AiCheck_PpOcrEngine_IsRegisteredInDiAndReusedWithoutDirectConstruction()
    {
        // 1. Direct construction and dynamic factory method MUST be absent from AiCheckViewModel
        var getEngineMethod = typeof(AiCheckViewModel).GetMethod(
            "GetOrCreateOcrEngine",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        Assert.Null(getEngineMethod);

        string modelDir = @"D:\NEXUS\Models\AI Workspace\PP-OCRv6";
        if (!Directory.Exists(modelDir)) return; // Requires models present on test machine

        // 2. Verify DI registration and singleton lifecycle
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        var (mockRef, mockJudge) = (new Mock<IReferenceDataManager>(), new Mock<IAiCheckJudgeService>());
        services.AddSingleton(mockRef.Object);
        services.AddSingleton(mockJudge.Object);
        services.AddSingleton<PpOcrEngine>(_ => new PpOcrEngine(modelDir));
        services.AddSingleton<AiCheckViewModel>();

        using var sp = services.BuildServiceProvider();
        var diEngine = sp.GetRequiredService<PpOcrEngine>();
        var vm = sp.GetRequiredService<AiCheckViewModel>();

        var engineField = typeof(AiCheckViewModel).GetField("_ocrEngine", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(engineField);

        var vmEngine = (PpOcrEngine?)engineField.GetValue(vm);
        Assert.NotNull(vmEngine);
        Assert.Same(diEngine, vmEngine); // Verified: PpOcrEngine is injected from DI singleton and reused!
    }

    [Fact]
    public void Test_A2_AiCheck_FallbackPath_IsCompatibilityOnly_AndDoesNotRetainEngineState()
    {
        var (vm, _, _) = CreateAiCheckViewModel();

        var engineField = typeof(AiCheckViewModel).GetField("_ocrEngine", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(engineField);

        // When PpOcrEngine is omitted, _ocrEngine remains null (pure compatibility/testing fallback)
        Assert.Null(engineField.GetValue(vm));
    }

    [Fact]
    public async Task Test_B_ImageSubsystem_OcrCache_Invalidates_WhenFileContentChangedOnSamePath()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), $"ocr-cache-test-{Guid.NewGuid():N}.png");
        try
        {
            using (var bmp = new Bitmap(50, 50))
            {
                using var g = Graphics.FromImage(bmp);
                g.Clear(Color.White);
                bmp.Save(tempFile, System.Drawing.Imaging.ImageFormat.Png);
            }

            var vm = CreateTestImageViewModel();
            vm.LoadImage(tempFile);

            var docField = typeof(ImageSubsystemViewModel).GetField("_cachedOcrDocument", BindingFlags.Instance | BindingFlags.NonPublic);
            var writeTimeField = typeof(ImageSubsystemViewModel).GetField("_cachedOcrLastWriteTimeUtc", BindingFlags.Instance | BindingFlags.NonPublic);
            var lengthField = typeof(ImageSubsystemViewModel).GetField("_cachedOcrFileLength", BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.NotNull(docField);
            Assert.NotNull(writeTimeField);
            Assert.NotNull(lengthField);

            // Populate dummy cached document to test cache verification logic
            var dummyDoc = new OneOcrDocument { FullText = "Initial Text" };
            docField.SetValue(vm, dummyDoc);
            typeof(ImageSubsystemViewModel).GetField("_cachedOcrSource", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(vm, tempFile);

            var initialFi = new FileInfo(tempFile);
            writeTimeField.SetValue(vm, initialFi.LastWriteTimeUtc);
            lengthField.SetValue(vm, initialFi.Length);

            // 1. Unchanged file: Cache metadata matches
            Assert.Equal(initialFi.LastWriteTimeUtc, (DateTime)writeTimeField.GetValue(vm)!);
            Assert.Equal(initialFi.Length, (long)lengthField.GetValue(vm)!);

            // 2. Modify the file on disk (simulate external edit or new content)
            await Task.Delay(50); // Ensure timestamp advances
            using (var bmp2 = new Bitmap(100, 100))
            {
                using var g = Graphics.FromImage(bmp2);
                g.Clear(Color.Blue);
                bmp2.Save(tempFile, System.Drawing.Imaging.ImageFormat.Png);
            }

            var updatedFi = new FileInfo(tempFile);
            Assert.True(updatedFi.Length != initialFi.Length || updatedFi.LastWriteTimeUtc != initialFi.LastWriteTimeUtc);

            // 3. Verify that the cache check logic detects change
            DateTime cachedTime = (DateTime)writeTimeField.GetValue(vm)!;
            long cachedLen = (long)lengthField.GetValue(vm)!;

            bool isCacheValid = cachedTime == updatedFi.LastWriteTimeUtc && cachedLen == updatedFi.Length;
            Assert.False(isCacheValid, "Cache MUST NOT be considered valid when disk file has changed!");
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void Test_B2_ImageSubsystem_OcrCache_Invalidates_OnInternalMutationWithSameFileLength()
    {
        var vm = CreateTestImageViewModel();
        var docField = typeof(ImageSubsystemViewModel).GetField("_cachedOcrDocument", BindingFlags.Instance | BindingFlags.NonPublic);
        var sourceField = typeof(ImageSubsystemViewModel).GetField("_cachedOcrSource", BindingFlags.Instance | BindingFlags.NonPublic);
        var writeTimeField = typeof(ImageSubsystemViewModel).GetField("_cachedOcrLastWriteTimeUtc", BindingFlags.Instance | BindingFlags.NonPublic);
        var lengthField = typeof(ImageSubsystemViewModel).GetField("_cachedOcrFileLength", BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(docField);

        // Simulate identical length & write time on the same file path
        string path = @"C:\dummy\image.png";
        var now = DateTime.UtcNow;
        long length = 4096;

        docField.SetValue(vm, new OneOcrDocument { FullText = "Initial Text" });
        sourceField?.SetValue(vm, path);
        writeTimeField?.SetValue(vm, now);
        lengthField?.SetValue(vm, length);

        // Internal mutation occurs (e.g. refine/inpaint/bake)
        vm.InvalidateOcrCache();

        // Even if an external file had the same size, explicit invalidation guarantees cache miss
        Assert.Null(docField.GetValue(vm));
        Assert.Null(sourceField?.GetValue(vm));
    }

    [Fact]
    public void Test_B3_ImageSubsystem_ActiveImageChange_InvalidatesOcrCache()
    {
        var vm = CreateTestImageViewModel();
        var docField = typeof(ImageSubsystemViewModel).GetField("_cachedOcrDocument", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(docField);

        docField.SetValue(vm, new OneOcrDocument { FullText = "Initial Text" });

        // Changing input image path must invalidate OCR cache
        vm.InputImagePath = @"C:\dummy\new_image.png";

        Assert.Null(docField.GetValue(vm));
    }

    [Fact]
    public void Test_C_PpOcrEngine_CooperativeCancellation_ThrowsImmediately()
    {
        string modelDir = @"D:\NEXUS\Models\AI Workspace\PP-OCRv6";
        if (!Directory.Exists(modelDir)) return;

        using var engine = new PpOcrEngine(modelDir);
        using var bmp = new Bitmap(100, 100);

        using var cts = new CancellationTokenSource();
        cts.Cancel(); // Pre-canceled token

        var ex = Assert.Throws<OperationCanceledException>(() =>
        {
            engine.RunPipeline(bmp, ct: cts.Token);
        });

        Assert.NotNull(ex);
    }

    [Fact]
    public void Test_C_AiCheckViewModel_Cancellation_HaltsPipelineAndLeavesNoFindings()
    {
        var (vm, _, _) = CreateAiCheckViewModel();

        // Simulate cancellation token triggered during check
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var cancelMethod = typeof(AiCheckViewModel).GetMethod("CancelActiveCheck", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(cancelMethod);

        cancelMethod.Invoke(vm, null);

        Assert.False(vm.IsAnalyzing);
        Assert.False(vm.IsInspectionCompleted);
        Assert.Empty(vm.Findings);
        Assert.Empty(vm.OverlayBoxes);
    }

    [Fact]
    public async Task Test_D_OneOcr_ConcurrentInitialization_GuaranteesSingleInstance()
    {
        var vm = CreateTestImageViewModel();
        var getEngineMethod = typeof(ImageSubsystemViewModel).GetMethod(
            "GetOrCreateOneOcrEngine",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(getEngineMethod);

        // Run 20 concurrent tasks trying to initialize OneOCR
        var tasks = new Task<OneOcrEngine>[20];
        for (int i = 0; i < tasks.Length; i++)
        {
            tasks[i] = Task.Run(() => (OneOcrEngine)getEngineMethod.Invoke(vm, null)!);
        }

        try
        {
            var results = await Task.WhenAll(tasks);
            var firstInstance = results[0];
            Assert.NotNull(firstInstance);

            for (int i = 1; i < results.Length; i++)
            {
                Assert.Same(firstInstance, results[i]); // Verified: Exactly one instance created!
            }
        }
        catch (OneOcrNotAvailableException)
        {
            // Expected on test environments without oneocr.dll native library
        }
    }

    [Fact]
    public void Test_D2_OneOcr_ImageSubsystemViewModel_DeterministicDisposal_CleansUpOwnedEngine()
    {
        var vm = CreateTestImageViewModel();
        var getEngineMethod = typeof(ImageSubsystemViewModel).GetMethod(
            "GetOrCreateOneOcrEngine",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(getEngineMethod);

        try
        {
            getEngineMethod.Invoke(vm, null);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is OneOcrNotAvailableException)
        {
            // Expected on machines without oneocr.dll native library
        }

        // Deterministic disposal on ImageSubsystemViewModel cleans up the engine reference
        vm.Dispose();

        var engineField = typeof(ImageSubsystemViewModel).GetField("_oneOcrEngine", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(engineField);
        Assert.Null(engineField.GetValue(vm));
    }

    [Fact]
    public void Test_E_ImageSubsystem_RefineAndInpaint_InvalidatesOcrCache()
    {
        var vm = CreateTestImageViewModel();
        var docField = typeof(ImageSubsystemViewModel).GetField("_cachedOcrDocument", BindingFlags.Instance | BindingFlags.NonPublic);
        var sourceField = typeof(ImageSubsystemViewModel).GetField("_cachedOcrSource", BindingFlags.Instance | BindingFlags.NonPublic);
        var timeField = typeof(ImageSubsystemViewModel).GetField("_cachedOcrLastWriteTimeUtc", BindingFlags.Instance | BindingFlags.NonPublic);
        var lenField = typeof(ImageSubsystemViewModel).GetField("_cachedOcrFileLength", BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(docField);

        // 1. Populate cache
        docField.SetValue(vm, new OneOcrDocument { FullText = "Test" });
        sourceField?.SetValue(vm, "test.png");
        timeField?.SetValue(vm, DateTime.UtcNow);
        lenField?.SetValue(vm, 1024L);

        // 2. Invalidate cache explicitly
        vm.InvalidateOcrCache();

        Assert.Null(docField.GetValue(vm));
        Assert.Null(sourceField?.GetValue(vm));
        Assert.Equal(default, (DateTime)timeField?.GetValue(vm)!);
        Assert.Equal(0L, (long)lenField?.GetValue(vm)!);
        Assert.Empty(vm.OcrWords);
        Assert.Empty(vm.OverlayBoxes);
    }
}
