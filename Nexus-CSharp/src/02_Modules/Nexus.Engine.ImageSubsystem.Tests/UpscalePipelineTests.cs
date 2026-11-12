using System.Drawing;
using System.Drawing.Imaging;
using Nexus.Contracts;
using Nexus.Engine.ImageSubsystem.Pipelines;
using Nexus.Foundation.Registry;

namespace Nexus.Engine.ImageSubsystem.Tests;

public sealed class UpscalePipelineTests
{
    [Fact]
    public async Task ExecuteAsync_UsesUpscaleCapabilityAndReturnsNativeOutputBitmap()
    {
        var descriptor = new ResolvedModelDescriptor
        {
            ModelPath = "registry-owned-upscale.onnx",
            ModelType = ModelType.Onnx,
            InputTensorName = "input",
            OutputTensorName = "output",
            InputShape = [-1, 3, -1, -1],
            OutputShape = [-1, 3, -1, -1],
            TensorType = "float32"
        };
        var policy = new RecordingPolicy(descriptor);
        var output = new float[1, 3, 4, 4];
        for (var y = 0; y < 4; y++)
        {
            for (var x = 0; x < 4; x++)
            {
                output[0, 0, y, x] = 1f;
                output[0, 1, y, x] = 0.5f;
                output[0, 2, y, x] = 0f;
            }
        }

        var engine = new RecordingImageEngine("output", output);
        var pipeline = new UpscalePipeline(policy, engine);
        using var input = new Bitmap(1, 1, PixelFormat.Format24bppRgb);
        input.SetPixel(0, 0, Color.Red);

        using var result = await pipeline.ExecuteAsync(input);

        Assert.Equal(AICapability.UpscaleGeneral, policy.Capability);
        Assert.Same(descriptor, engine.LoadedDescriptor);
        var tensor = Assert.IsType<float[,,,]>(engine.Inputs!["input"]);
        Assert.Equal(1f, tensor[0, 0, 0, 0]);
        Assert.Equal(4, result.Width);
        Assert.Equal(4, result.Height);
        var pixel = result.GetPixel(0, 0);
        Assert.Equal(255, pixel.R);
        Assert.InRange(pixel.G, 127, 128);
        Assert.Equal(0, pixel.B);
        Assert.Equal(255, pixel.A);
        Assert.Equal(1, input.Width);
        Assert.Equal("session-id", engine.UnloadedSessionKey);
    }

    [Fact]
    public async Task ExecuteAsync_WhenInferenceFails_StillUnloadsSession()
    {
        var descriptor = new ResolvedModelDescriptor
        {
            ModelPath = "registry-owned-upscale.onnx",
            ModelType = ModelType.Onnx,
            InputTensorName = "input",
            OutputTensorName = "output",
            InputShape = [-1, 3, -1, -1],
            TensorType = "float32"
        };
        var engine = new RecordingImageEngine(
            "output",
            new float[1, 3, 1, 1],
            new InvalidOperationException("Inference failed."));
        var pipeline = new UpscalePipeline(
            new RecordingPolicy(descriptor),
            engine);
        using var input = new Bitmap(1, 1);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => pipeline.ExecuteAsync(input));

        Assert.Equal("session-id", engine.UnloadedSessionKey);
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
        float[,,,] output,
        Exception? failure = null) : IGenericImageEngine
    {
        public string EngineName => "Test";

        public bool IsLoaded => true;

        public ResolvedModelDescriptor? LoadedDescriptor { get; private set; }

        public IReadOnlyDictionary<string, object>? Inputs { get; private set; }

        public string? UnloadedSessionKey { get; private set; }

        public Task LoadEngineAsync() => Task.CompletedTask;

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
            if (failure is not null)
            {
                return Task.FromException<IReadOnlyDictionary<string, object>>(
                    failure);
            }

            IReadOnlyDictionary<string, object> result =
                new Dictionary<string, object> { [outputName] = output };
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
}
