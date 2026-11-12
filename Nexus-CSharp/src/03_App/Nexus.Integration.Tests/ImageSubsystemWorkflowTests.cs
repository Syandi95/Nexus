using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using Moq;
using Nexus.App.Services;
using Nexus.Contracts;
using Nexus.Engine.ImageSubsystem.Pipelines;
using Nexus.Engine.ImageSubsystem.Services;
using Nexus.Foundation.Registry;

namespace Nexus.Integration.Tests;

public sealed class ImageSubsystemWorkflowTests
{
    [Fact]
    public async Task UpscaleAsync_TransformsNativeFourTimesOutputToRequestedTwoTimes()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var upscaleDirectory = Path.Combine(temporaryDirectory.Path, "upscale");
        Directory.CreateDirectory(upscaleDirectory);
        var modelPath = Path.Combine(upscaleDirectory, "model.onnx");
        File.WriteAllBytes(modelPath, [0]);
        var metadataPath = Path.Combine(upscaleDirectory, "metadata.json");
        File.WriteAllText(
            metadataPath,
            """
            {
              "modelId": "upscale-test",
              "modelFile": "model.onnx",
              "input": { "name": "input", "dataType": "float32", "shape": [-1, 3, -1, -1] },
              "output": { "name": "output", "dataType": "float32", "shape": [-1, 3, -1, -1] }
            }
            """);

        var registry = new ModelRegistry();
        var policy = new ImageApplicationModelSelectionPolicy(
            registry,
            new ModelMetadataParser(),
            temporaryDirectory.Path,
            metadataPath);
        var engine = new Mock<IGenericImageEngine>();
        engine
            .Setup(item => item.LoadModelAsync(
                It.Is<ResolvedModelDescriptor>(
                    descriptor => descriptor.ModelPath == modelPath),
                It.IsAny<EngineSessionOptions>()))
            .ReturnsAsync("session-id");
        engine
            .Setup(item => item.ExecuteInferenceAsync(
                "session-id",
                It.IsAny<IReadOnlyDictionary<string, object>>()))
            .ReturnsAsync(new Dictionary<string, object>
            {
                ["output"] = CreateNativeOutput()
            });

        var workflow = new ImageSubsystemWorkflow(
            policy,
            new RemoveBackgroundPipeline(policy, engine.Object),
            new UpscalePipeline(policy, engine.Object),
            Mock.Of<IOnnxModelManager>());
        var inputPath = Path.Combine(temporaryDirectory.Path, "input.png");
        var outputPath = Path.Combine(temporaryDirectory.Path, "output.png");
        using (var input = new Bitmap(2, 2))
        {
            input.Save(inputPath, ImageFormat.Png);
        }

        await workflow.UpscaleAsync(
            inputPath,
            targetScale: 2,
            outputPath,
            CancellationToken.None);

        using var output = new Bitmap(outputPath);
        Assert.Equal(4, output.Width);
        Assert.Equal(4, output.Height);
        engine.Verify(item => item.UnloadModelAsync("session-id"), Times.Once);
    }

    private static float[,,,] CreateNativeOutput()
    {
        var output = new float[1, 3, 8, 8];
        for (var channel = 0; channel < 3; channel++)
        {
            for (var y = 0; y < 8; y++)
            {
                for (var x = 0; x < 8; x++)
                {
                    output[0, channel, y, x] = 0.5f;
                }
            }
        }

        return output;
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"NexusImageWorkflow-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
