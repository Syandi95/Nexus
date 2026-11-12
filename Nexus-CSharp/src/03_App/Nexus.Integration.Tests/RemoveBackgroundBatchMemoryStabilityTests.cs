using System;
using System.Collections.Generic;
using System.Diagnostics;
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
using Xunit.Abstractions;

namespace Nexus.Integration.Tests;

[Collection(WpfApplicationCollection.Name)]
public sealed class RemoveBackgroundBatchMemoryStabilityTests
{
    private readonly ITestOutputHelper _output;

    public RemoveBackgroundBatchMemoryStabilityTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private static string CreateTestPng(string dir, int width, int height, Color color)
    {
        var path = Path.Combine(dir, $"{Guid.NewGuid():N}.png");
        using var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            using var brush = new SolidBrush(color);
            g.FillRectangle(brush, 0, 0, width, height);
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
    public async Task AcceptanceTest_TenSequentialImages_MemoryStaysBounded_NoLinearEscalation()
    {
        using var tempDir = new TempDirectory();
        int width = 1200;
        int height = 1200;

        _output.WriteLine("=== STARTING 10-IMAGE STABILITY ACCEPTANCE TEST (P1-P4) ===");

        // Create 10 distinct test images and 1 mock output PNG
        var imagePaths = new List<string>(10);
        for (int i = 0; i < 10; i++)
        {
            var color = Color.FromArgb(255, (i * 25) % 256, (100 + i * 15) % 256, (200 - i * 10) % 256);
            imagePaths.Add(CreateTestPng(tempDir.Path, width, height, color));
        }

        var resultPng = CreateTestPng(tempDir.Path, width, height, Color.FromArgb(128, 50, 100, 150));

        var (vm, _) = CreateViewModelWithMock(resultPng);

        var wsReadings = new List<double>(10);
        var heapReadings = new List<double>(10);

        for (int i = 0; i < 10; i++)
        {
            var path = imagePaths[i];
            vm.LoadImage(path);

            Assert.Equal(width, vm.OriginalImageWidth);
            Assert.Equal(height, vm.OriginalImageHeight);

            await vm.ExecutePrimaryActionAsync();

            Assert.True(vm.HasResult, $"Image {i + 1} did not produce a result.");
            Assert.NotNull(vm.GetActiveRemoveBgAlpha());
            Assert.Equal(width * height, vm.GetActiveRemoveBgAlpha()!.Length);

            var proc = Process.GetCurrentProcess();
            proc.Refresh();
            double wsMb = proc.WorkingSet64 / (1024.0 * 1024.0);
            double heapMb = GC.GetTotalMemory(false) / (1024.0 * 1024.0);

            wsReadings.Add(wsMb);
            heapReadings.Add(heapMb);

            _output.WriteLine($"[ACCEPTANCE_TEST] Image {i + 1:D2}/10: WorkingSet = {wsMb:F2} MB, ManagedHeap = {heapMb:F2} MB");
        }

        _output.WriteLine("=== MEMORY STABILITY EVALUATION ===");
        _output.WriteLine($"Image 01: WorkingSet={wsReadings[0]:F2} MB, ManagedHeap={heapReadings[0]:F2} MB");
        _output.WriteLine($"Image 02: WorkingSet={wsReadings[1]:F2} MB, ManagedHeap={heapReadings[1]:F2} MB");
        _output.WriteLine($"Image 05: WorkingSet={wsReadings[4]:F2} MB, ManagedHeap={heapReadings[4]:F2} MB");
        _output.WriteLine($"Image 10: WorkingSet={wsReadings[9]:F2} MB, ManagedHeap={heapReadings[9]:F2} MB");

        double wsGrowthAfterWarmup = wsReadings[9] - wsReadings[1];
        double heapGrowth = heapReadings[9] - heapReadings[1];
        _output.WriteLine($"WorkingSet delta (Image 10 - Image 02): {wsGrowthAfterWarmup:+0.00;-0.00;0.00} MB");
        _output.WriteLine($"ManagedHeap delta (Image 10 - Image 02): {heapGrowth:+0.00;-0.00;0.00} MB");

        // With P1-P4 in place, there must NOT be linear growth (e.g. +500 MB to +10 GB).
        // The ManagedHeap and WorkingSet growth after warm-up (from image 2 to 10) must remain comfortably bounded.
        Assert.True(heapGrowth < 50.0,
            $"Managed heap grew by {heapGrowth:F2} MB between image 2 and 10, indicating memory accumulation.");
        Assert.True(wsGrowthAfterWarmup < 150.0,
            $"Working set grew by {wsGrowthAfterWarmup:F2} MB between image 2 and 10, indicating potential memory leak or linear accumulation.");

        // Verification of Visual Parity & Refinement Precision on the 10th image
        vm.IsRemoveBgRefining = true;
        Assert.True(vm.IsRemoveBgCanvasVisible);

        var roi = new Rectangle(10, 10, 20, 20);
        byte[] strokeMask = new byte[20 * 20];
        Array.Fill(strokeMask, (byte)100);

        vm.ApplyRemoveBgStroke(roi, strokeMask, isAdd: true);

        Assert.True(vm.CanUndoRemoveBgRefinement);
        var activeAlpha = vm.GetActiveRemoveBgAlpha()!;
        Assert.Equal((byte)(128 + 100), activeAlpha[10 * width + 10]);

        vm.UndoRemoveBgRefinement();
        Assert.Equal((byte)128, activeAlpha[10 * width + 10]);

        _output.WriteLine("=== ACCEPTANCE TEST PASSED: Zero linear growth, 100% refinement precision preserved ===");
    }
}
