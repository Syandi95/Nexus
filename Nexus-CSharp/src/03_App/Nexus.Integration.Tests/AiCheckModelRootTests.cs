using System.Drawing;
using System.IO;
using Moq;
using Nexus.App.ViewModels;
using Nexus.Contracts.AiCheck;
using Nexus.Contracts.ReferenceData;
using Nexus.Core.HomeSettings.Services;
using Nexus.Engine.ImageSubsystem.Ocr;

namespace Nexus.Integration.Tests;

public class AiCheckModelRootTests
{
    [Fact]
    public async Task ReadOcrBoxesAsync_UsesConfiguredNexusModelRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), $"nexus-ocr-root-{Guid.NewGuid():N}");
        string imagePath = Path.Combine(Path.GetTempPath(), $"nexus-ocr-input-{Guid.NewGuid():N}.png");
        string expectedModelDirectory = Path.Combine(root, "AI Workspace", "PP-OCRv6");

        try
        {
            using (var image = new Bitmap(2, 2))
            {
                image.Save(imagePath);
            }
            using (var imageFile = File.OpenWrite(imagePath))
            {
                imageFile.SetLength(2048);
            }

            var references = new Mock<IReferenceDataManager>();
            references.Setup(r => r.GetAllDatasetsMetadataAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync([]);
            var settings = new Mock<ISettingsManagerService>();
            settings.SetupGet(s => s.ModelRootFolder).Returns(root);

            var vm = new TestAiCheckViewModel(
                references.Object,
                Mock.Of<IAiCheckJudgeService>(),
                settings.Object);

            var exception = await Assert.ThrowsAsync<DirectoryNotFoundException>(
                () => vm.ReadFromConfiguredRootAsync(imagePath));

            Assert.Contains(expectedModelDirectory, exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (File.Exists(imagePath)) File.Delete(imagePath);
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private sealed class TestAiCheckViewModel(
        IReferenceDataManager references,
        IAiCheckJudgeService judge,
        ISettingsManagerService settings)
        : AiCheckViewModel(references, judge, settingsManagerService: settings)
    {
        public Task<List<OcrBoxModel>> ReadFromConfiguredRootAsync(string imagePath)
            => base.ReadOcrBoxesAsync(imagePath);
    }
}
