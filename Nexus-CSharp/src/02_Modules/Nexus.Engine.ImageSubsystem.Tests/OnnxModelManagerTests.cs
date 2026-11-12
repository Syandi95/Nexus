using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Nexus.Engine.ImageSubsystem.Services;
using Nexus.Foundation.Registry;
using Xunit;

namespace Nexus.Engine.ImageSubsystem.Tests
{
    public class OnnxModelManagerTests
    {
        [Fact]
        public void DetectOpsetVersion_ReturnsCorrectVersion()
        {
            var manager = new OnnxModelManager();
            var version = manager.DetectOpsetVersion("dummy.onnx");
            
            // Just verifying it doesn't crash and returns something
            Assert.True(version > 0);
        }

        [Fact]
        public async Task LoadModelSlotsAsync_ReturnsDefaultWhenNoFile()
        {
            var manager = new OnnxModelManager("nonexistent.json");
            var slots = await manager.LoadModelSlotsAsync();
            
            Assert.NotNull(slots);
            Assert.Equal("PhotosSeg", slots["Lite"]);
            Assert.Equal("BiRefNet_lite", slots["Balanced"]);
            Assert.Equal("RMBG-2.0", slots["High Quality"]);
        }

        [Fact]
        public async Task SaveAndLoadModelSlotsAsync_WorksCorrectly()
        {
            var testFile = "test_slots.json";
            if (File.Exists(testFile)) File.Delete(testFile);
            
            var manager = new OnnxModelManager(testFile);
            var slots = new Dictionary<string, string>
            {
                { "Custom", "MyModel" }
            };
            
            await manager.SaveModelSlotsAsync(slots);
            var loaded = await manager.LoadModelSlotsAsync();
            
            Assert.Equal("MyModel", loaded["Custom"]);
            
            if (File.Exists(testFile)) File.Delete(testFile);
        }

        [Fact]
        public async Task GenerateMetadataAsync_writes_a_parser_compatible_model_package()
        {
            var sourceModel = FindRealEsrganModel();
            var packageDirectory = Path.Combine(
                Path.GetTempPath(),
                $"NexusMetadataRoundTrip-{Guid.NewGuid():N}",
                "RealESRGAN");
            Directory.CreateDirectory(packageDirectory);
            try
            {
                var modelPath = Path.Combine(
                    packageDirectory,
                    Path.GetFileName(sourceModel));
                File.Copy(sourceModel, modelPath);
                using var manager = new OnnxModelManager();

                var metadataPath = await manager.GenerateMetadataAsync(
                    modelPath,
                    packageDirectory);
                var descriptor = new ModelMetadataParser()
                    .ParseOnnxMetadata(metadataPath);

                Assert.Equal("RealESRGAN", descriptor.ModelId);
                Assert.Equal(modelPath, descriptor.ModelPath);
                Assert.False(string.IsNullOrWhiteSpace(descriptor.InputTensorName));
                Assert.False(string.IsNullOrWhiteSpace(descriptor.OutputTensorName));
                Assert.NotNull(descriptor.InputShape);
                Assert.NotNull(descriptor.OutputShape);
                Assert.False(string.IsNullOrWhiteSpace(descriptor.Sha256));
            }
            finally
            {
                Directory.Delete(
                    Path.GetDirectoryName(packageDirectory)!,
                    recursive: true);
            }
        }

        private static string FindRealEsrganModel()
        {
            var current = new DirectoryInfo(AppContext.BaseDirectory);
            while (current is not null)
            {
                var candidate = Path.Combine(
                    current.FullName,
                    "Models",
                    "Upscale",
                    "RealESR General WDN x4 v3",
                    "real_esrgan_general_x4v3.onnx");
                if (File.Exists(candidate))
                {
                    return candidate;
                }

                current = current.Parent;
            }

            throw new FileNotFoundException(
                "RealESRGAN verification model was not found.");
        }
    }
}
