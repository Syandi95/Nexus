using System.Drawing;
using System.Drawing.Imaging;
using Nexus.Contracts;
using Nexus.Engine.ImageSubsystem.Modules;
using Nexus.Engine.ImageSubsystem.Pipelines;
using Nexus.Foundation.Registry;
using Xunit;

namespace Nexus.Engine.ImageSubsystem.Tests;

public sealed class InpaintPipelineTests
{
    [Fact]
    public async Task ExecuteAsync_UsesInpaintingCapability_AndPassesBothImageAndMaskTensors()
    {
        var descriptor = new ResolvedModelDescriptor
        {
            ModelId = "lama-test",
            ModelPath = "registry-owned-model.onnx",
            ModelType = ModelType.Onnx,
            InputTensorName = "image",
            OutputTensorName = "output",
            InputShape = [1, 3, 2, 2],
            OutputShape = [1, 3, 2, 2],
            TensorType = "float32",
            AdditionalInputs = new Dictionary<string, long[]>
            {
                ["mask"] = [1, 1, 2, 2]
            }
        };

        var policy = new RecordingPolicy(descriptor);
        var inpaintOut = new float[1, 3, 2, 2]
        {
            {
                { { 255f, 0f }, { 0f, 255f } }, // R
                { { 0f, 255f }, { 255f, 0f } }, // G
                { { 0f, 0f }, { 255f, 255f } }   // B
            }
        };

        var engine = new RecordingImageEngine("output", inpaintOut);
        var pipeline = new InpaintPipeline(policy, engine);

        using var input = new Bitmap(2, 2, PixelFormat.Format24bppRgb);
        input.SetPixel(0, 0, Color.Black);
        input.SetPixel(1, 0, Color.Black);
        input.SetPixel(0, 1, Color.Black);
        input.SetPixel(1, 1, Color.Black);

        using var mask = new Bitmap(2, 2, PixelFormat.Format24bppRgb);
        mask.SetPixel(0, 0, Color.White); // inpaint this pixel
        mask.SetPixel(1, 0, Color.Black); // keep original
        mask.SetPixel(0, 1, Color.Black);
        mask.SetPixel(1, 1, Color.Black);

        using var result = await pipeline.ExecuteAsync(input, mask, InpaintExecutionTier.FastSinglePass);

        Assert.Equal(AICapability.InpaintingGeneral, policy.Capability);
        Assert.Same(descriptor, engine.LoadedDescriptor);
        Assert.NotNull(engine.Inputs);
        Assert.True(engine.Inputs.ContainsKey("image"));
        Assert.True(engine.Inputs.ContainsKey("mask"));

        var imageTensor = Assert.IsType<float[,,,]>(engine.Inputs["image"]);
        var maskTensor = Assert.IsType<float[,,,]>(engine.Inputs["mask"]);
        Assert.Equal(2, imageTensor.GetLength(2));
        Assert.Equal(2, imageTensor.GetLength(3));
        Assert.Equal(2, maskTensor.GetLength(2));
        Assert.Equal(2, maskTensor.GetLength(3));

        // Coordinate (0, 0) was masked, so it receives inpaint color (Red: 255, 0, 0)
        var pixel00 = result.GetPixel(0, 0);
        Assert.Equal(255, pixel00.R);
        Assert.Equal(0, pixel00.G);
        Assert.Equal(0, pixel00.B);

        // Coordinate (1, 0) was unmasked, so it keeps original input color (Black: 0, 0, 0)
        var pixel10 = result.GetPixel(1, 0);
        Assert.Equal(0, pixel10.R);
        Assert.Equal(0, pixel10.G);
        Assert.Equal(0, pixel10.B);

        Assert.Equal("session-id", engine.UnloadedSessionKey);
        // Ensure input and mask are intact
        Assert.Equal(2, input.Width);
        Assert.Equal(2, mask.Width);
    }

    [Fact]
    public async Task ExecuteAsync_DualPass_ExecutesTwoInferencePasses_WithSameSessionKey()
    {
        var descriptor = new ResolvedModelDescriptor
        {
            ModelId = "lama-test-dual",
            ModelPath = "registry-owned-model.onnx",
            ModelType = ModelType.Onnx,
            InputTensorName = "image",
            OutputTensorName = "output",
            InputShape = [1, 3, 2, 2],
            OutputShape = [1, 3, 2, 2],
            TensorType = "float32"
        };

        var policy = new RecordingPolicy(descriptor);
        var inpaintOut = new float[1, 3, 2, 2]
        {
            {
                { { 200f, 200f }, { 200f, 200f } },
                { { 100f, 100f }, { 100f, 100f } },
                { { 50f, 50f }, { 50f, 50f } }
            }
        };

        var engine = new RecordingImageEngine("output", inpaintOut);
        var pipeline = new InpaintPipeline(policy, engine);

        // Large image (2500x2500) with a center mask
        using var input = new Bitmap(2500, 2500, PixelFormat.Format24bppRgb);
        using var mask = new Bitmap(2500, 2500, PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(mask))
        {
            g.Clear(Color.Black);
            g.FillRectangle(Brushes.White, 500, 500, 300, 300);
        }

        using var result = await pipeline.ExecuteAsync(input, mask, InpaintExecutionTier.HighDetailDualPass);

        // Should have called inference TWICE (Pass 1 Global + Pass 2 Local Crop)
        Assert.Equal(2, engine.InferenceCount);
        // And session should be unloaded once at the end
        Assert.Equal("session-id", engine.UnloadedSessionKey);
        Assert.Equal(2500, result.Width);
        Assert.Equal(2500, result.Height);
    }

    [Fact]
    public async Task ExecuteAsync_DualPass_WhenMaskCoversAlmostEntireImage_FallsBackToSinglePass()
    {
        var descriptor = new ResolvedModelDescriptor
        {
            ModelId = "lama-test-fallback",
            ModelPath = "registry-owned-model.onnx",
            ModelType = ModelType.Onnx,
            InputTensorName = "image",
            OutputTensorName = "output",
            InputShape = [1, 3, 2, 2],
            OutputShape = [1, 3, 2, 2],
            TensorType = "float32"
        };

        var policy = new RecordingPolicy(descriptor);
        var inpaintOut = new float[1, 3, 2, 2]
        {
            {
                { { 200f, 200f }, { 200f, 200f } },
                { { 100f, 100f }, { 100f, 100f } },
                { { 50f, 50f }, { 50f, 50f } }
            }
        };

        var engine = new RecordingImageEngine("output", inpaintOut);
        var pipeline = new InpaintPipeline(policy, engine);

        // Large image (2500x2500) where the mask covers 95% of the canvas
        using var input = new Bitmap(2500, 2500, PixelFormat.Format24bppRgb);
        using var mask = new Bitmap(2500, 2500, PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(mask))
        {
            g.Clear(Color.Black);
            g.FillRectangle(Brushes.White, 50, 50, 2400, 2400); // 96% coverage
        }

        using var result = await pipeline.ExecuteAsync(input, mask, InpaintExecutionTier.HighDetailDualPass);

        // Optimasi: Karena area crop hampir sama dengan kanvas utuh, Pass 2 redundant tidak dijalankan!
        Assert.Equal(1, engine.InferenceCount);
        Assert.Equal("session-id", engine.UnloadedSessionKey);
        Assert.Equal(2500, result.Width);
        Assert.Equal(2500, result.Height);
    }

    [Fact]
    public void InpaintModule_ImplementsContractCorrectly()
    {
        var module = new InpaintModule();
        Assert.True(module.SupportsColorSpace(Nexus.Engine.ColorManagement.ColorSpaceKind.Rgb));
        Assert.Equal("_inpainted", module.GetFileSuffix(null));
        var formats = module.GetAllowedFormats(Nexus.Engine.ColorManagement.ColorSpaceKind.Rgb);
        Assert.Contains(Nexus.Engine.ColorManagement.ImageOutputFormat.Png, formats);
    }

    private sealed class RecordingPolicy(ResolvedModelDescriptor descriptor) : IModelSelectionPolicy
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

    private sealed class RecordingImageEngine(string outputName, float[,,,] output) : IGenericImageEngine
    {
        public ResolvedModelDescriptor? LoadedDescriptor { get; private set; }
        public IReadOnlyDictionary<string, object>? Inputs { get; private set; }
        public string? UnloadedSessionKey { get; private set; }
        public int InferenceCount { get; private set; }
        public string EngineName => "RecordingEngine";
        public bool IsLoaded => true;

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
            InferenceCount++;
            Inputs = inputs;
            return Task.FromResult<IReadOnlyDictionary<string, object>>(
                new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    [outputName] = output
                });
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
