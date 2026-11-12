using System.Drawing;
using System.Drawing.Imaging;
using Nexus.Contracts;
using Nexus.Engine.ImageSubsystem.Pipelines;
using Nexus.Foundation.Registry;

namespace Nexus.Engine.ImageSubsystem.Tests;

public sealed class RemoveBackgroundPipelineTests
{
    [Fact]
    public async Task ExecuteAsync_UsesCapabilityAndReturnsArgbBitmapWithoutDisposingInput()
    {
        var descriptor = new ResolvedModelDescriptor
        {
            ModelId = "test-background-removal",
            ModelPath = "registry-owned-model.onnx",
            ModelType = ModelType.Onnx,
            InputTensorName = "input_image",
            OutputTensorName = "output_image",
            InputShape = [1, 3, 2, 2],
            OutputShape = [1, 1, 2, 2],
            TensorType = "float32"
        };
        var policy = new RecordingPolicy(descriptor);
        var alpha = new float[1, 1, 2, 2]
        {
            { { { 0f, 1f }, { 1f, 0f } } }
        };
        var engine = new RecordingImageEngine("output_image", alpha);
        var pipeline = new RemoveBackgroundPipeline(policy, engine);
        using var input = new Bitmap(2, 2, PixelFormat.Format24bppRgb);
        input.SetPixel(0, 0, Color.Red);
        input.SetPixel(1, 0, Color.Green);
        input.SetPixel(0, 1, Color.Blue);
        input.SetPixel(1, 1, Color.White);

        using var result = await pipeline.ExecuteAsync(input);

        Assert.Equal(AICapability.BackgroundRemovalGeneral, policy.Capability);
        Assert.Same(descriptor, engine.LoadedDescriptor);
        Assert.True(engine.Inputs!.TryGetValue("input_image", out var tensorValue));
        var tensor = Assert.IsType<float[,,,]>(tensorValue);
        Assert.Equal(2, tensor.GetLength(2));
        Assert.Equal(2, tensor.GetLength(3));
        Assert.Equal(2, result.Width);
        Assert.Equal(2, result.Height);
        Assert.Equal(0, result.GetPixel(0, 0).A);
        Assert.Equal(255, result.GetPixel(1, 0).A);
        Assert.Equal("session-id", engine.UnloadedSessionKey);

        // Caller masih dapat menggunakan dan membuang bitmap input.
        Assert.Equal(2, input.Width);
    }

    [Fact]
    public async Task ExecuteAsync_AppliesSigmoidOnlyWhenOutputContainsLogits()
    {
        var descriptor = new ResolvedModelDescriptor
        {
            ModelPath = "registry-owned-model.onnx",
            ModelType = ModelType.Onnx,
            InputTensorName = "input",
            OutputTensorName = "mask",
            InputShape = [1, 3, 1, 2],
            TensorType = "float32"
        };
        var logits = new float[1, 1, 1, 2]
        {
            { { { -10f, 10f } } }
        };
        var pipeline = new RemoveBackgroundPipeline(
            new RecordingPolicy(descriptor),
            new RecordingImageEngine("mask", logits));
        using var input = new Bitmap(2, 1, PixelFormat.Format24bppRgb);

        using var result = await pipeline.ExecuteAsync(input);

        Assert.InRange(result.GetPixel(0, 0).A, 0, 1);
        Assert.InRange(result.GetPixel(1, 0).A, 254, 255);
    }

    [Fact]
    public async Task ExecuteAsync_PhotosSeg_Handles3DOutputTensorAndBypassesStandardization()
    {
        var descriptor = new ResolvedModelDescriptor
        {
            ModelId = "PhotosSeg",
            ModelPath = "photos_seg.onnx",
            ModelType = ModelType.Onnx,
            InputTensorName = "input_image",
            OutputTensorName = "output",
            InputShape = [1, 3, 2, 2],
            OutputShape = [1, 2, 2],
            TensorType = "float32"
        };
        var policy = new RecordingPolicy(descriptor);
        var alpha3d = new float[1, 2, 2]
        {
            { { 0f, 1f }, { 1f, 0f } }
        };
        var engine = new RecordingImageEngine("output", alpha3d);
        var pipeline = new RemoveBackgroundPipeline(policy, engine);
        using var input = new Bitmap(2, 2, PixelFormat.Format24bppRgb);
        input.SetPixel(0, 0, Color.FromArgb(255, 0, 0)); // Red (R=255, G=0, B=0)
        input.SetPixel(1, 0, Color.FromArgb(0, 255, 0));
        input.SetPixel(0, 1, Color.FromArgb(0, 0, 255));
        input.SetPixel(1, 1, Color.FromArgb(255, 255, 255));

        using var result = await pipeline.ExecuteAsync(input);

        Assert.Equal(AICapability.BackgroundRemovalGeneral, policy.Capability);
        Assert.Same(descriptor, engine.LoadedDescriptor);
        Assert.True(engine.Inputs!.TryGetValue("input_image", out var tensorValue));
        var tensor = Assert.IsType<float[,,,]>(tensorValue);
        // PhotosSeg bypasses standardization: R channel for pixel (0,0) is R/255f = 1.0f (channel 0 is R in NCHW)
        Assert.Equal(1.0f, tensor[0, 0, 0, 0], precision: 4);
        Assert.Equal(0.0f, tensor[0, 1, 0, 0], precision: 4);
        Assert.Equal(0.0f, tensor[0, 2, 0, 0], precision: 4);

        // Alpha result verified from 3D tensor
        Assert.Equal(0, result.GetPixel(0, 0).A);
        Assert.Equal(255, result.GetPixel(1, 0).A);
    }

    [Fact]
    public async Task ExecuteAsync_PhotosSegFails_FallsBackToMvanet()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"nexus-fallback-test-{Guid.NewGuid():N}");
        var photosSegDir = Path.Combine(tempDir, "PhotosSeg");
        var mvanetDir = Path.Combine(tempDir, "MVANet");
        Directory.CreateDirectory(photosSegDir);
        Directory.CreateDirectory(mvanetDir);

        try
        {
            var mvanetMetaPath = Path.Combine(mvanetDir, "metadata.json");
            var mvanetOnnxPath = Path.Combine(mvanetDir, "model.onnx");
            File.WriteAllText(mvanetOnnxPath, "dummy");
            File.WriteAllText(mvanetMetaPath, """
            {
                "modelId": "mvanet-fallback",
                "modelFile": "model.onnx",
                "input": {
                    "name": "pixel_values",
                    "dataType": "float32",
                    "shape": [1, 3, 2, 2]
                },
                "output": {
                    "name": "alphas",
                    "dataType": "float32",
                    "shape": [1, 1, 2, 2]
                }
            }
            """);

            var photosDescriptor = new ResolvedModelDescriptor
            {
                ModelId = "PhotosSeg",
                ModelPath = Path.Combine(photosSegDir, "model.onnx"),
                ModelType = ModelType.Onnx,
                InputTensorName = "input_image",
                OutputTensorName = "output",
                InputShape = [1, 3, 2, 2],
                OutputShape = [1, 2, 2],
                TensorType = "float32"
            };

            var policy = new RecordingPolicy(photosDescriptor);
            var mvanetAlpha = new float[1, 1, 2, 2]
            {
                { { { 0.5f, 0.5f }, { 0.5f, 0.5f } } }
            };

            var engine = new FallbackRecordingImageEngine("alphas", mvanetAlpha);
            var pipeline = new RemoveBackgroundPipeline(policy, engine,
                () => new ModelMetadataParser().ParseOnnxMetadata(mvanetMetaPath));
            using var input = new Bitmap(2, 2, PixelFormat.Format24bppRgb);

            using var result = await pipeline.ExecuteAsync(input);

            Assert.Equal("mvanet-fallback", engine.LoadedDescriptor?.ModelId);
            Assert.NotNull(result);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    private sealed class RecordingPolicy(ResolvedModelDescriptor descriptor)
        : IModelSelectionPolicy
    {
        public AICapability Capability { get; private set; }

        public ResolvedModelDescriptor SelectModel(
            AICapability capability,
            EngineSessionOptions sessionOptions)
        {
            Capability = capability;
            return descriptor;
        }
    }

    private sealed class RecordingImageEngine(
        string outputName,
        object output) : IGenericImageEngine
    {
        public string EngineName => "Test";

        public bool IsLoaded { get; private set; }

        public ResolvedModelDescriptor? LoadedDescriptor { get; private set; }

        public IReadOnlyDictionary<string, object>? Inputs { get; private set; }

        public string? UnloadedSessionKey { get; private set; }

        public Task LoadEngineAsync()
        {
            IsLoaded = true;
            return Task.CompletedTask;
        }

        public Task<string> LoadModelAsync(
            ResolvedModelDescriptor descriptor,
            EngineSessionOptions? options = null)
        {
            LoadedDescriptor = descriptor;
            return Task.FromResult("session-id");
        }

        public Task<IReadOnlyDictionary<string, object>> ExecuteInferenceAsync(
            string sessionKey,
            IReadOnlyDictionary<string, object> inputs)
        {
            Inputs = inputs;
            IReadOnlyDictionary<string, object> result =
                new Dictionary<string, object>
                {
                    [outputName] = output
                };
            return Task.FromResult(result);
        }

        public Task UnloadModelAsync(string sessionKey)
        {
            UnloadedSessionKey = sessionKey;
            return Task.CompletedTask;
        }

        public Task UnloadAllModelsAsync() => Task.CompletedTask;

        public EngineMemoryMetrics GetMemoryMetrics() => new();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FallbackRecordingImageEngine(
        string mvanetOutputName,
        object mvanetOutput) : IGenericImageEngine
    {
        public string EngineName => "FallbackTest";
        public bool IsLoaded { get; private set; }
        public ResolvedModelDescriptor? LoadedDescriptor { get; private set; }

        public Task LoadEngineAsync() => Task.CompletedTask;

        public Task<string> LoadModelAsync(
            ResolvedModelDescriptor descriptor,
            EngineSessionOptions? options = null)
        {
            LoadedDescriptor = descriptor;
            if (descriptor.ModelId == "PhotosSeg")
            {
                throw new InvalidOperationException("Simulated PhotosSeg failure.");
            }
            return Task.FromResult("mvanet-session");
        }

        public Task<IReadOnlyDictionary<string, object>> ExecuteInferenceAsync(
            string sessionKey,
            IReadOnlyDictionary<string, object> inputs)
        {
            return Task.FromResult<IReadOnlyDictionary<string, object>>(
                new Dictionary<string, object>
                {
                    [mvanetOutputName] = mvanetOutput
                });
        }

        public Task UnloadModelAsync(string sessionKey) => Task.CompletedTask;
        public Task UnloadAllModelsAsync() => Task.CompletedTask;
        public EngineMemoryMetrics GetMemoryMetrics() => new();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
