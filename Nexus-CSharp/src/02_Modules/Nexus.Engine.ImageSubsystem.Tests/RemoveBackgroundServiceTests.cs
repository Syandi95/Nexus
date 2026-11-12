using System;
using System.IO;
using System.Threading.Tasks;
using Nexus.Engine.ImageSubsystem.Diagnostics;
using Nexus.Engine.ImageSubsystem.Services;
using Xunit;

namespace Nexus.Engine.ImageSubsystem.Tests
{
    public class RemoveBackgroundServiceTests
    {
        [Fact]
        public async Task ExecuteInferenceAsync_ValidDirNoOnnx_Throws()
        {
            using var pipeline = new ImagePipelineService();
            using var modelManager = new OnnxModelManager();
            using var service = new RemoveBackgroundService();

            var tempDir = Path.Combine(Path.GetTempPath(), "RmbgTest_" + Path.GetRandomFileName());
            Directory.CreateDirectory(tempDir);
            try
            {
                var inputPath = Path.Combine(tempDir, "test.png");
                var outPath = Path.Combine(tempDir, "out.png");
                await File.WriteAllTextAsync(inputPath, "not-a-real-image");

                var ex = await Assert.ThrowsAsync<InferenceException>(() =>
                    service.ExecuteInferenceAsync(pipeline, modelManager, inputPath, tempDir, outPath));
                Assert.Equal(InferenceErrorCode.MODEL_OR_FILE_ACCESS_FAILURE, ex.ErrorCode);
            }
            finally
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public async Task ExecuteInferenceAsync_MissingInput_Throws()
        {
            using var pipeline = new ImagePipelineService();
            using var modelManager = new OnnxModelManager();
            using var service = new RemoveBackgroundService();

            var tempDir = Path.Combine(Path.GetTempPath(), "RmbgTest_" + Path.GetRandomFileName());
            Directory.CreateDirectory(tempDir);
            try
            {
                var missingPath = Path.Combine(tempDir, "nonexistent.png");
                var outPath = Path.Combine(tempDir, "out.png");

                var ex = await Assert.ThrowsAsync<InferenceException>(() =>
                    service.ExecuteInferenceAsync(pipeline, modelManager, missingPath, tempDir, outPath));
                Assert.Equal(InferenceErrorCode.MODEL_OR_FILE_ACCESS_FAILURE, ex.ErrorCode);
            }
            finally
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
            }
        }
    }
}
