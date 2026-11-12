using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Nexus.App.Services;
using Nexus.App.ViewModels;
using Nexus.Contracts;
using Nexus.Core.HomeSettings.Services;
using Nexus.Engine.ColorManagement;
using Nexus.Engine.ImageSubsystem.Pipelines;
using Nexus.Engine.ImageSubsystem.Services;
using Nexus.Foundation.Registry;
using Xunit;

namespace Nexus.Integration.Tests;

public sealed class InpaintPortableModelManagementTests
{
    private static readonly byte[] MinimalPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

    [Fact]
    public void CapabilityIsolation_SettingInpaintModel_DoesNotAffectRemoveBackgroundOrUpscale()
    {
        var registry = new ModelRegistry();
        var parser = new ModelMetadataParser();

        var removeBgDescriptor = new ResolvedModelDescriptor
        {
            ModelId = "photos-seg",
            ModelPath = "C:\\dummy\\removebg.onnx",
            ModelType = ModelType.Onnx,
            InputTensorName = "input",
            OutputTensorName = "output",
            InputShape = [1, 3, 512, 512],
            OutputShape = [1, 1, 512, 512],
            TensorType = "float32"
        };
        registry.RegisterModel(AICapability.BackgroundRemovalGeneral, removeBgDescriptor);
        registry.SetDefaultModel(AICapability.BackgroundRemovalGeneral, removeBgDescriptor.ModelId);

        var inpaintDescriptor = new ResolvedModelDescriptor
        {
            ModelId = "photos-mager",
            ModelPath = "C:\\dummy\\mager.onnx",
            ModelType = ModelType.Onnx,
            InputTensorName = "img_hr",
            OutputTensorName = "output",
            InputShape = [1, 3, 512, 512],
            OutputShape = [1, 3, 512, 512],
            TensorType = "float32",
            AdditionalInputs = new Dictionary<string, long[]>
            {
                ["mask_hr"] = [1, 1, 512, 512],
                ["img_lr"] = [1, 3, 256, 256],
                ["mask_lr"] = [1, 1, 256, 256]
            }
        };
        registry.RegisterModel(AICapability.InpaintingGeneral, inpaintDescriptor);
        registry.SetDefaultModel(AICapability.InpaintingGeneral, inpaintDescriptor.ModelId);

        var defaultPolicy = new DefaultModelSelectionPolicy(registry);
        var resolvedRemoveBg = defaultPolicy.SelectModel(AICapability.BackgroundRemovalGeneral, new EngineSessionOptions());
        var resolvedInpaint = defaultPolicy.SelectModel(AICapability.InpaintingGeneral, new EngineSessionOptions());

        Assert.Equal("photos-seg", resolvedRemoveBg.ModelId);
        Assert.Equal("photos-mager", resolvedInpaint.ModelId);
    }

    [Fact]
    public void ModelSelectionPolicy_UseInpaintModel_SwitchesBetweenLaMaAndPhotosMager()
    {
        using var tempDir = new TempDirectory();
        var modelsRoot = tempDir.Path;
        var inpaintRoot = Path.Combine(modelsRoot, "Inpaint");
        var lamaDir = Path.Combine(inpaintRoot, "LaMa");
        var magerDir = Path.Combine(inpaintRoot, "PhotosMager");
        Directory.CreateDirectory(lamaDir);
        Directory.CreateDirectory(magerDir);

        File.WriteAllBytes(Path.Combine(lamaDir, "model.onnx"), [0x08, 0x01]);
        File.WriteAllText(Path.Combine(lamaDir, "metadata.json"), JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            modelId = "big-lama-fp32",
            displayName = "LaMa",
            modelFile = "model.onnx",
            input = new { name = "image", dataType = "float32", shape = new long[] { 1, 3, 512, 512 } },
            output = new { name = "output", dataType = "float32", shape = new long[] { 1, 3, 512, 512 } },
            additionalInputs = new Dictionary<string, long[]> { ["mask"] = [1, 1, 512, 512] }
        }));

        File.WriteAllBytes(Path.Combine(magerDir, "model.onnx"), [0x08, 0x01]);
        File.WriteAllText(Path.Combine(magerDir, "metadata.json"), JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            modelId = "photos-mager",
            displayName = "Photos Mager",
            modelFile = "model.onnx",
            input = new { name = "img_hr", dataType = "float32", shape = new long[] { 1, 3, 512, 512 } },
            output = new { name = "output", dataType = "float32", shape = new long[] { 1, 3, 512, 512 } },
            additionalInputs = new Dictionary<string, long[]>
            {
                ["mask_hr"] = [1, 1, 512, 512],
                ["img_lr"] = [1, 3, 256, 256],
                ["mask_lr"] = [1, 1, 256, 256]
            }
        }));

        var registry = new ModelRegistry();
        var parser = new ModelMetadataParser();
        var policy = new ImageApplicationModelSelectionPolicy(
            registry,
            parser,
            removeBackgroundModelsRoot: Path.Combine(modelsRoot, "RemoveBackground"),
            upscaleMetadataPath: Path.Combine(modelsRoot, "dummy_upscale.json"),
            inpaintModelsRoot: inpaintRoot);

        // 1. Switch to PhotosMager
        policy.UseInpaintModel("PhotosMager");
        var selected1 = policy.SelectModel(AICapability.InpaintingGeneral, new EngineSessionOptions());
        Assert.Equal("photos-mager", selected1.ModelId);
        Assert.Equal("img_hr", selected1.InputTensorName);
        Assert.NotNull(selected1.AdditionalInputs);
        Assert.Equal(3, selected1.AdditionalInputs.Count);

        // 2. Switch to LaMa
        policy.UseInpaintModel("LaMa");
        var selected2 = policy.SelectModel(AICapability.InpaintingGeneral, new EngineSessionOptions());
        Assert.Equal("big-lama-fp32", selected2.ModelId);
        Assert.Equal("image", selected2.InputTensorName);
    }

    [Fact]
    public void FallbackMechanism_WhenRequestedModelFails_RevertsToLaMaAndHealsSlots()
    {
        using var tempDir = new TempDirectory();
        var modelsRoot = tempDir.Path;
        var inpaintRoot = Path.Combine(modelsRoot, "Inpaint");
        var lamaDir = Path.Combine(inpaintRoot, "LaMa");
        Directory.CreateDirectory(lamaDir);

        File.WriteAllBytes(Path.Combine(lamaDir, "model.onnx"), [0x08, 0x01]);
        File.WriteAllText(Path.Combine(lamaDir, "metadata.json"), JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            modelId = "big-lama-fp32",
            displayName = "LaMa",
            modelFile = "model.onnx",
            input = new { name = "image", dataType = "float32", shape = new long[] { 1, 3, 512, 512 } },
            output = new { name = "output", dataType = "float32", shape = new long[] { 1, 3, 512, 512 } },
            additionalInputs = new Dictionary<string, long[]> { ["mask"] = [1, 1, 512, 512] }
        }));

        var slotsPath = Path.Combine(inpaintRoot, "slots_mapping.json");
        File.WriteAllText(slotsPath, JsonSerializer.Serialize(new { Selected = "NonExistentModel" }));

        var registry = new ModelRegistry();
        var parser = new ModelMetadataParser();
        var policy = new ImageApplicationModelSelectionPolicy(
            registry,
            parser,
            removeBackgroundModelsRoot: Path.Combine(modelsRoot, "RemoveBackground"),
            upscaleMetadataPath: Path.Combine(modelsRoot, "dummy_upscale.json"),
            inpaintModelsRoot: inpaintRoot);

        // SelectModel triggers EnsureInpaintRegistered which should fall back to LaMa
        var selected = policy.SelectModel(AICapability.InpaintingGeneral, new EngineSessionOptions());
        Assert.Equal("big-lama-fp32", selected.ModelId);

        // slots_mapping.json must be healed
        Assert.True(File.Exists(slotsPath));
        var healedJson = File.ReadAllText(slotsPath);
        Assert.Contains("LaMa", healedJson);
    }

    [Fact]
    public async Task InpaintPipeline_ExecutesWithGenericMultiInputBuilder_ForPhotosMager()
    {
        var descriptor = new ResolvedModelDescriptor
        {
            ModelId = "photos-mager",
            ModelPath = "C:\\dummy\\model.onnx",
            ModelType = ModelType.Onnx,
            InputTensorName = "img_hr",
            OutputTensorName = "output",
            InputShape = [1, 3, 512, 512],
            OutputShape = [1, 3, 512, 512],
            TensorType = "float32",
            AdditionalInputs = new Dictionary<string, long[]>
            {
                ["mask_hr"] = [1, 1, 512, 512],
                ["img_lr"] = [1, 3, 256, 256],
                ["mask_lr"] = [1, 1, 256, 256]
            }
        };

        var policyMock = new Mock<IModelSelectionPolicy>();
        policyMock
            .Setup(p => p.SelectModel(AICapability.InpaintingGeneral, It.IsAny<EngineSessionOptions>()))
            .Returns(descriptor);

        Dictionary<string, object>? capturedInputs = null;
        var engineMock = new Mock<IGenericImageEngine>();
        engineMock
            .Setup(e => e.LoadModelAsync(descriptor, It.IsAny<EngineSessionOptions>()))
            .ReturnsAsync("session_mager_1");

        engineMock
            .Setup(e => e.ExecuteInferenceAsync("session_mager_1", It.IsAny<IReadOnlyDictionary<string, object>>()))
            .Callback<string, IReadOnlyDictionary<string, object>>((_, inputs) =>
            {
                capturedInputs = new Dictionary<string, object>(inputs);
            })
            .ReturnsAsync(new Dictionary<string, object>
            {
                ["output"] = new float[1, 3, 512, 512]
            });

        var pipeline = new InpaintPipeline(policyMock.Object, engineMock.Object);

        using var input = new Bitmap(512, 512, PixelFormat.Format24bppRgb);
        using var mask = new Bitmap(512, 512, PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(mask))
        {
            g.Clear(Color.Black);
            g.FillRectangle(Brushes.White, 100, 100, 100, 100);
        }

        using var result = await pipeline.ExecuteAsync(input, mask, InpaintExecutionTier.FastSinglePass);

        Assert.NotNull(result);
        Assert.NotNull(capturedInputs);

        // Verify that all 4 tensors are present with correct dimensions
        Assert.True(capturedInputs.ContainsKey("img_hr"));
        Assert.True(capturedInputs.ContainsKey("mask_hr"));
        Assert.True(capturedInputs.ContainsKey("img_lr"));
        Assert.True(capturedInputs.ContainsKey("mask_lr"));

        var imgHr = Assert.IsType<float[,,,]>(capturedInputs["img_hr"]);
        Assert.Equal(1, imgHr.GetLength(0));
        Assert.Equal(3, imgHr.GetLength(1));
        Assert.Equal(512, imgHr.GetLength(2));
        Assert.Equal(512, imgHr.GetLength(3));

        var maskHr = Assert.IsType<float[,,,]>(capturedInputs["mask_hr"]);
        Assert.Equal(1, maskHr.GetLength(0));
        Assert.Equal(1, maskHr.GetLength(1));
        Assert.Equal(512, maskHr.GetLength(2));
        Assert.Equal(512, maskHr.GetLength(3));

        var imgLr = Assert.IsType<float[,,,]>(capturedInputs["img_lr"]);
        Assert.Equal(1, imgLr.GetLength(0));
        Assert.Equal(3, imgLr.GetLength(1));
        Assert.Equal(256, imgLr.GetLength(2));
        Assert.Equal(256, imgLr.GetLength(3));

        var maskLr = Assert.IsType<float[,,,]>(capturedInputs["mask_lr"]);
        Assert.Equal(1, maskLr.GetLength(0));
        Assert.Equal(1, maskLr.GetLength(1));
        Assert.Equal(256, maskLr.GetLength(2));
        Assert.Equal(256, maskLr.GetLength(3));
    }

    [Fact]
    public void ViewModel_InpaintModelDiscoveryAndSelection_SyncsWithSlots()
    {
        using var tempDir = new TempDirectory();
        var modelsRoot = tempDir.Path;
        var inpaintRoot = Path.Combine(modelsRoot, "Inpaint");
        var lamaDir = Path.Combine(inpaintRoot, "LaMa");
        var magerDir = Path.Combine(inpaintRoot, "PhotosMager");
        Directory.CreateDirectory(lamaDir);
        Directory.CreateDirectory(magerDir);

        File.WriteAllBytes(Path.Combine(lamaDir, "model.onnx"), [0x08, 0x01]);
        File.WriteAllText(Path.Combine(lamaDir, "metadata.json"), "{}");
        File.WriteAllBytes(Path.Combine(magerDir, "model.onnx"), [0x08, 0x01]);
        File.WriteAllText(Path.Combine(magerDir, "metadata.json"), "{}");

        var workflowMock = new Mock<IImageSubsystemWorkflow>();
        workflowMock
            .Setup(w => w.ValidateModelPackage(It.IsAny<string>()))
            .Returns(true);

        var genericEngineMock = new Mock<IGenericImageEngine>();
        var cmykMock = new Mock<ICmykProfileManager>();
        var settingsMock = new Mock<ISettingsManagerService>();
        settingsMock.Setup(s => s.ModelRootFolder).Returns(modelsRoot);

        var registry = new ModelRegistry();
        var parser = new ModelMetadataParser();

        File.WriteAllText(Path.Combine(lamaDir, "metadata.json"), JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            modelId = "big-lama-fp32",
            displayName = "LaMa",
            modelFile = "model.onnx",
            input = new { name = "image", dataType = "float32", shape = new long[] { 1, 3, 512, 512 } },
            output = new { name = "output", dataType = "float32", shape = new long[] { 1, 3, 512, 512 } }
        }));
        File.WriteAllText(Path.Combine(magerDir, "metadata.json"), JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            modelId = "photos-mager",
            displayName = "Photos Mager",
            modelFile = "model.onnx",
            input = new { name = "img_hr", dataType = "float32", shape = new long[] { 1, 3, 512, 512 } },
            output = new { name = "output", dataType = "float32", shape = new long[] { 1, 3, 512, 512 } }
        }));

        var policy = new ImageApplicationModelSelectionPolicy(
            registry,
            parser,
            removeBackgroundModelsRoot: Path.Combine(modelsRoot, "RemoveBackground"),
            upscaleMetadataPath: Path.Combine(modelsRoot, "dummy_upscale.json"),
            inpaintModelsRoot: inpaintRoot);

        var vm = new ImageSubsystemViewModel(
            workflowMock.Object,
            genericEngineMock.Object,
            cmykMock.Object,
            () => Mock.Of<IColorManagementService>(),
            settingsMock.Object,
            modelSelectionPolicy: policy);

        // Verify discovery
        Assert.Contains("LaMa", vm.AvailableInpaintModels);
        Assert.Contains("PhotosMager", vm.AvailableInpaintModels);

        // Select PhotosMager
        vm.SelectedInpaintModel = "PhotosMager";
        Assert.Equal("PhotosMager", vm.SelectedInpaintModel);
        Assert.Equal("Microsoft Photos Magic Eraser", vm.SelectedInpaintModelDisplayName);

        // Check slots_mapping.json
        var slotsPath = Path.Combine(inpaintRoot, "slots_mapping.json");
        Assert.True(File.Exists(slotsPath));
        var slotsContent = File.ReadAllText(slotsPath);
        Assert.Contains("PhotosMager", slotsContent);

        // Fallback test
        vm.SelectedInpaintModel = "BrokenModelNotFound";
        Assert.Equal("LaMa", vm.SelectedInpaintModel);
        Assert.Contains("LaMa", File.ReadAllText(slotsPath));
    }

    [Fact]
    public async Task ExecuteAsync_RealModel_PhotosMager_Inference_Succeeds()
    {
        var modelDir = @"D:\NEXUS\Models\Inpaint\PhotosMager";
        var onnxPath = Path.Combine(modelDir, "model.onnx");
        var metaPath = Path.Combine(modelDir, "metadata.json");

        if (!File.Exists(onnxPath) || !File.Exists(metaPath))
        {
            return;
        }

        var parser = new ModelMetadataParser();
        var descriptor = parser.ParseOnnxMetadata(metaPath);
        Assert.Equal("photos-mager", descriptor.ModelId);
        Assert.Equal("img_hr", descriptor.InputTensorName);
        Assert.NotNull(descriptor.AdditionalInputs);

        var registry = new ModelRegistry();
        registry.RegisterModel(AICapability.InpaintingGeneral, descriptor);
        registry.SetDefaultModel(AICapability.InpaintingGeneral, descriptor.ModelId);

        var policy = new DefaultModelSelectionPolicy(registry);
        await using var engine = new GenericImageEngine();
        var pipeline = new InpaintPipeline(policy, engine);

        using var input = new Bitmap(512, 512, PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(input))
        {
            g.Clear(Color.SkyBlue);
            g.FillEllipse(Brushes.Red, 200, 200, 100, 100);
        }

        using var mask = new Bitmap(512, 512, PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(mask))
        {
            g.Clear(Color.Black);
            g.FillRectangle(Brushes.White, 190, 190, 120, 120);
        }

        using var output = await pipeline.ExecuteAsync(input, mask, InpaintExecutionTier.FastSinglePass);
        Assert.NotNull(output);
        Assert.Equal(512, output.Width);
        Assert.Equal(512, output.Height);

        var centerPixel = output.GetPixel(250, 250);
        Assert.False(centerPixel.R > 200 && centerPixel.G < 50 && centerPixel.B < 50, "Center should be inpainted");
    }
}