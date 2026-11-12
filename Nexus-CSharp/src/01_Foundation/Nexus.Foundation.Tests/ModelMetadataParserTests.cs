using System.Text.Json;
using Nexus.Contracts;
using Nexus.Foundation.Registry;

namespace Nexus.Foundation.Tests;

public sealed class ModelMetadataParserTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"NexusModelMetadataParser-{Guid.NewGuid():N}");

    public ModelMetadataParserTests()
    {
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public void ParseOnnxMetadata_uses_declared_model_file_or_model_onnx_fallback()
    {
        var declaredMetadata = WriteFile(
            "Declared/metadata.json",
            """{"modelId":"upscale","displayName":"Upscale","modelFile":"upscale.onnx","input":{"name":"input","dataType":"float32","shape":[1,3,-1,-1]},"output":{"name":"output","shape":[1,3,-1,-1]}}""");
        var fallbackMetadata = WriteFile(
            "Fallback/metadata.json",
            """{"modelId":"remove-bg","input":{"name":"input_image"},"output":{"name":"output_image"}}""");
        var parser = new ModelMetadataParser();

        var declared = parser.ParseOnnxMetadata(declaredMetadata);
        var fallback = parser.ParseOnnxMetadata(fallbackMetadata);

        Assert.Equal(ModelType.Onnx, declared.ModelType);
        Assert.Equal(Path.Combine(_root, "Declared", "upscale.onnx"), declared.ModelPath);
        Assert.Equal("input", declared.InputTensorName);
        Assert.Equal(new long[] { 1, 3, -1, -1 }, declared.InputShape);
        Assert.Equal(Path.Combine(_root, "Fallback", "model.onnx"), fallback.ModelPath);
    }

    [Fact]
    public void ParseGgufModelIndex_resolves_relative_model_json_references()
    {
        var indexPath = WriteFile(
            "AI Workspace/model-index.json",
            """{"schemaVersion":1,"activeModelId":"gemma","models":[{"id":"gemma","metadataFile":"Gemma/model.json"}]}""");
        WriteFile(
            "AI Workspace/Gemma/model.json",
            """{"schemaVersion":1,"id":"gemma","displayName":"Gemma","ggufFile":"gemma.gguf"}""");
        var parser = new ModelMetadataParser();

        var descriptors = parser.ParseGgufModelIndex(indexPath);

        var descriptor = Assert.Single(descriptors);
        Assert.Equal(ModelType.Gguf, descriptor.ModelType);
        Assert.Equal("gemma", descriptor.ModelId);
        Assert.Equal(Path.Combine(_root, "AI Workspace", "Gemma", "gemma.gguf"), descriptor.ModelPath);
    }

    [Fact]
    public void ParseSlotsMapping_returns_raw_slot_values_and_empty_mapping_when_optional_file_is_missing()
    {
        var mappingPath = WriteFile("RemoveBackground/slots_mapping.json", """{"Lite":"BiRefNet_lite","High Quality":"RMBG-2.0"}""");
        var parser = new ModelMetadataParser();

        var mappings = parser.ParseSlotsMapping(mappingPath);
        var missingMappings = parser.ParseSlotsMapping(Path.Combine(_root, "RemoveBackground", "missing.json"));

        Assert.Equal("BiRefNet_lite", mappings["Lite"]);
        Assert.Equal("RMBG-2.0", mappings["High Quality"]);
        Assert.Empty(missingMappings);
    }

    [Fact]
    public void WriteOnnxMetadata_round_trips_the_canonical_m2_2_schema()
    {
        var modelDirectory = Path.Combine(_root, "RMBG-2.0");
        Directory.CreateDirectory(modelDirectory);
        var modelPath = Path.Combine(modelDirectory, "rmbg.onnx");
        File.WriteAllBytes(modelPath, [1, 2, 3]);
        var metadataPath = Path.Combine(modelDirectory, "metadata.json");
        var descriptor = new ResolvedModelDescriptor
        {
            ModelId = "RMBG-2.0",
            ModelPath = modelPath,
            ModelType = ModelType.Onnx,
            DisplayName = "RMBG 2.0",
            InputTensorName = "input",
            OutputTensorName = "output",
            InputShape = [1, 3, 1024, 1024],
            OutputShape = [1, 1, 1024, 1024],
            TensorType = "float32",
            Sha256 = "abc123"
        };
        var parser = new ModelMetadataParser();

        parser.WriteOnnxMetadata(metadataPath, descriptor);

        using var document = JsonDocument.Parse(File.ReadAllText(metadataPath));
        var root = document.RootElement;
        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("RMBG-2.0", root.GetProperty("modelId").GetString());
        Assert.Equal("rmbg.onnx", root.GetProperty("modelFile").GetString());
        Assert.Equal("input", root.GetProperty("input").GetProperty("name").GetString());
        Assert.Equal(
            "float32",
            root.GetProperty("input").GetProperty("dataType").GetString());
        Assert.Equal("output", root.GetProperty("output").GetProperty("name").GetString());
        Assert.False(root.TryGetProperty("ModelFile", out _));
        Assert.False(root.TryGetProperty("InputName", out _));

        var parsed = parser.ParseOnnxMetadata(metadataPath);

        Assert.Equal(descriptor.ModelId, parsed.ModelId);
        Assert.Equal(descriptor.ModelPath, parsed.ModelPath);
        Assert.Equal(descriptor.DisplayName, parsed.DisplayName);
        Assert.Equal(descriptor.InputTensorName, parsed.InputTensorName);
        Assert.Equal(descriptor.OutputTensorName, parsed.OutputTensorName);
        Assert.Equal(descriptor.InputShape, parsed.InputShape);
        Assert.Equal(descriptor.OutputShape, parsed.OutputShape);
        Assert.Equal(descriptor.TensorType, parsed.TensorType);
        Assert.Equal(descriptor.Sha256, parsed.Sha256);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private string WriteFile(string relativePath, string content)
    {
        var path = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }
}
