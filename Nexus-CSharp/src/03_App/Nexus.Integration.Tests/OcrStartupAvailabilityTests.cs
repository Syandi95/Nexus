using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nexus.App.ViewModels;
using Nexus.Contracts.ReferenceData;
using Nexus.Engine.ImageSubsystem.Ocr;
using Nexus.Foundation.Config;
using Nexus.Foundation.ReferenceData;

namespace Nexus.Integration.Tests;

public sealed class OcrStartupAvailabilityTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ShellAndChat_ResolveWithoutOcr_InspectionReportsMissingOrCorruptModel(bool corrupt)
    {
        var root = Path.Combine(Path.GetTempPath(), "nexus-startup-ocr-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var imagePath = Path.Combine(root, "input.png");
        using (var image = new Bitmap(128, 128))
        {
            using var graphics = Graphics.FromImage(image);
            graphics.Clear(Color.White);
            image.Save(imagePath, ImageFormat.Png);
        }
        using (var imageFile = File.OpenWrite(imagePath)) imageFile.SetLength(2048);
        var ocrRoot = Path.Combine(root, "models", "AI Workspace", "PP-OCRv6");
        if (corrupt)
        {
            Directory.CreateDirectory(ocrRoot);
            File.WriteAllText(Path.Combine(ocrRoot, PpOcrEngine.DefaultDetModelName), "not an ONNX model");
            File.WriteAllText(Path.Combine(ocrRoot, PpOcrEngine.DefaultRecModelName), "not an ONNX model");
        }
        var services = ArchitectureGovernanceTests.CreateProductionServices();
        services.RemoveAll<IConfigService>();
        var config = new ConfigService(Path.Combine(root, "config.json"));
        config.SaveConfig(new AppConfigRecord { ModelRootFolder = Path.Combine(root, "models"), EngineRootFolder = Path.Combine(root, "engines") });
        services.AddSingleton<IConfigService>(config);
        services.RemoveAll<IReferenceDataManager>();
        services.AddSingleton<IReferenceDataManager>(new ReferenceDataManager(Path.Combine(root, "references")));
        await using var provider = services.BuildServiceProvider();
        try
        {
            await WpfAppHost.RunAsync(async () =>
            {
                var shell = provider.GetRequiredService<Nexus.App.MainWindow>();
                Assert.NotNull(shell.DataContext);
                Assert.NotNull(provider.GetRequiredService<ConversationViewModel>());
                var check = provider.GetRequiredService<AiCheckViewModel>();
                await check.LoadFileDirectlyAsync(imagePath);
                Assert.True(check.CanRunCheck);
                var execute = typeof(AiCheckViewModel).GetMethod("ExecuteRunCheckAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
                await ((Task)execute.Invoke(check, null)!).WaitAsync(TimeSpan.FromSeconds(20));
                Assert.False(check.IsAnalyzing);
                Assert.False(check.IsInspectionCompleted);
                Assert.Contains("Pemeriksaan gagal", check.SummaryText);
                Assert.NotEmpty(check.SummaryText);
                Assert.Empty(check.Findings);
            });
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
