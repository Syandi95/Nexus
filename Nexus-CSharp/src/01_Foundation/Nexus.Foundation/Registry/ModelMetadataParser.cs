using System.Text.Json;
using Nexus.Contracts;

namespace Nexus.Foundation.Registry;

/// <summary>
/// Membaca metadata model ONNX dan katalog GGUF menjadi descriptor model fisik.
/// </summary>
public sealed class ModelMetadataParser
{
    public void WriteOnnxMetadata(
        string metadataPath,
        ResolvedModelDescriptor descriptor)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(metadataPath);
        ArgumentNullException.ThrowIfNull(descriptor);
        if (descriptor.ModelType != ModelType.Onnx)
        {
            throw new InvalidDataException("Only ONNX descriptors use ONNX metadata.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.ModelId);
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.ModelPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.InputTensorName);
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.OutputTensorName);

        var fullMetadataPath = Path.GetFullPath(metadataPath);
        var metadataDirectory = Path.GetDirectoryName(fullMetadataPath)
            ?? throw new InvalidDataException("Metadata directory could not be resolved.");
        var fullModelPath = Path.GetFullPath(descriptor.ModelPath);
        if (!string.Equals(
                Path.GetDirectoryName(fullModelPath),
                metadataDirectory,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "ONNX model and metadata must be in the same package directory.");
        }

        var metadata = new Dictionary<string, object?>
        {
            ["schemaVersion"] = 1,
            ["modelId"] = descriptor.ModelId,
            ["displayName"] = descriptor.DisplayName,
            ["modelFile"] = Path.GetFileName(fullModelPath),
            ["input"] = new Dictionary<string, object?>
            {
                ["name"] = descriptor.InputTensorName,
                ["dataType"] = descriptor.TensorType,
                ["shape"] = descriptor.InputShape
            },
            ["output"] = new Dictionary<string, object?>
            {
                ["name"] = descriptor.OutputTensorName,
                ["shape"] = descriptor.OutputShape
            },
            ["sha256"] = descriptor.Sha256
        };

        if (descriptor.AdditionalInputs != null && descriptor.AdditionalInputs.Count > 0)
        {
            var addInputsDict = new Dictionary<string, object?>();
            foreach (var kvp in descriptor.AdditionalInputs)
            {
                addInputsDict[kvp.Key] = kvp.Value;
            }
            metadata["additionalInputs"] = addInputsDict;
        }

        File.WriteAllText(
            fullMetadataPath,
            JsonSerializer.Serialize(
                metadata,
                new JsonSerializerOptions { WriteIndented = true }));
    }

    public ResolvedModelDescriptor ParseOnnxMetadata(string metadataPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(metadataPath);

        using var document = JsonDocument.Parse(File.ReadAllText(metadataPath));
        var root = RequireObject(document.RootElement, metadataPath);
        var metadataDirectory = Path.GetDirectoryName(Path.GetFullPath(metadataPath))
            ?? throw new InvalidDataException("Metadata directory could not be resolved.");
        var modelFile = GetOptionalString(root, "modelFile") ?? "model.onnx";

        Dictionary<string, long[]>? additionalInputs = null;
        if (root.TryGetProperty("additionalInputs", out var addElem) &&
            addElem.ValueKind == JsonValueKind.Object)
        {
            additionalInputs = new Dictionary<string, long[]>(StringComparer.Ordinal);
            foreach (var prop in addElem.EnumerateObject())
            {
                if (prop.Value.ValueKind == JsonValueKind.Array)
                {
                    var dims = new List<long>();
                    foreach (var dim in prop.Value.EnumerateArray())
                    {
                        if (dim.TryGetInt64(out var d)) dims.Add(d);
                    }
                    additionalInputs[prop.Name] = dims.ToArray();
                }
            }
        }

        return new ResolvedModelDescriptor
        {
            ModelId = RequireString(root, "modelId", metadataPath),
            ModelPath = ResolveFileUnderDirectory(metadataDirectory, modelFile),
            ModelType = ModelType.Onnx,
            DisplayName = GetOptionalString(root, "displayName"),
            InputTensorName = GetNestedString(root, "input", "name"),
            OutputTensorName = GetNestedString(root, "output", "name"),
            InputShape = GetNestedShape(root, "input"),
            OutputShape = GetNestedShape(root, "output"),
            TensorType = GetNestedString(root, "input", "dataType") ?? "float32",
            Sha256 = GetOptionalString(root, "sha256") ?? GetNestedString(root, "provenance", "sha256"),
            AdditionalInputs = additionalInputs
        };
    }

    public IReadOnlyList<ResolvedModelDescriptor> ParseGgufModelIndex(string indexPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(indexPath);

        using var document = JsonDocument.Parse(File.ReadAllText(indexPath));
        var root = RequireObject(document.RootElement, indexPath);
        var catalogDirectory = Path.GetDirectoryName(Path.GetFullPath(indexPath))
            ?? throw new InvalidDataException("Model index directory could not be resolved.");

        if (!root.TryGetProperty("models", out var modelsElement) ||
            modelsElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("Model index must contain a models array.");
        }

        var descriptors = new List<ResolvedModelDescriptor>();
        foreach (var indexEntry in modelsElement.EnumerateArray())
        {
            var entry = RequireObject(indexEntry, indexPath);
            var metadataPath = ResolveRelativePathUnderDirectory(
                catalogDirectory,
                RequireString(entry, "metadataFile", indexPath));
            var indexModelId = RequireString(entry, "id", indexPath);
            descriptors.Add(ParseGgufMetadata(metadataPath, indexModelId));
        }

        return descriptors;
    }

    public IReadOnlyDictionary<string, string> ParseSlotsMapping(string slotsMappingPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slotsMappingPath);

        if (!File.Exists(slotsMappingPath))
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        using var document = JsonDocument.Parse(File.ReadAllText(slotsMappingPath));
        var root = RequireObject(document.RootElement, slotsMappingPath);
        var slots = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var property in root.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(property.Value.GetString()))
            {
                throw new InvalidDataException("Slot mapping values must be non-empty strings.");
            }

            slots.Add(property.Name, property.Value.GetString()!);
        }

        return slots;
    }

    private static ResolvedModelDescriptor ParseGgufMetadata(string metadataPath, string indexModelId)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(metadataPath));
        var root = RequireObject(document.RootElement, metadataPath);
        var modelId = RequireString(root, "id", metadataPath);
        if (!string.Equals(modelId, indexModelId, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Model index ID does not match GGUF metadata ID.");
        }

        var metadataDirectory = Path.GetDirectoryName(Path.GetFullPath(metadataPath))
            ?? throw new InvalidDataException("GGUF metadata directory could not be resolved.");

        return new ResolvedModelDescriptor
        {
            ModelId = modelId,
            ModelPath = ResolveFileUnderDirectory(
                metadataDirectory,
                RequireString(root, "ggufFile", metadataPath)),
            ModelType = ModelType.Gguf,
            DisplayName = GetOptionalString(root, "displayName")
        };
    }

    private static JsonElement RequireObject(JsonElement element, string sourcePath)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException($"JSON root must be an object: {sourcePath}");
        }

        return element;
    }

    private static string RequireString(JsonElement element, string propertyName, string sourcePath) =>
        GetOptionalString(element, propertyName)
            ?? throw new InvalidDataException($"Required property '{propertyName}' is missing: {sourcePath}");

    private static string? GetOptionalString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) &&
        property.ValueKind == JsonValueKind.String &&
        !string.IsNullOrWhiteSpace(property.GetString())
            ? property.GetString()
            : null;

    private static string? GetNestedString(JsonElement element, string objectName, string propertyName) =>
        element.TryGetProperty(objectName, out var nestedObject) &&
        nestedObject.ValueKind == JsonValueKind.Object
            ? GetOptionalString(nestedObject, propertyName)
            : null;

    private static long[]? GetNestedShape(JsonElement element, string objectName)
    {
        if (!element.TryGetProperty(objectName, out var nestedObject) ||
            nestedObject.ValueKind != JsonValueKind.Object ||
            !nestedObject.TryGetProperty("shape", out var shape) ||
            shape.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var values = new List<long>();
        foreach (var dimension in shape.EnumerateArray())
        {
            if (!dimension.TryGetInt64(out var value))
            {
                throw new InvalidDataException("Tensor shape values must be int64 numbers.");
            }

            values.Add(value);
        }

        return values.ToArray();
    }

    private static string ResolveFileUnderDirectory(string directory, string relativeFileName)
    {
        if (Path.IsPathRooted(relativeFileName) ||
            !string.Equals(relativeFileName, Path.GetFileName(relativeFileName), StringComparison.Ordinal) ||
            relativeFileName.Contains("..", StringComparison.Ordinal))
        {
            throw new InvalidDataException("Model file reference must be a file name.");
        }

        return Path.Combine(directory, relativeFileName);
    }

    private static string ResolveRelativePathUnderDirectory(string directory, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
        {
            throw new InvalidDataException("Model metadata reference must be a relative path.");
        }

        var root = Path.GetFullPath(directory).TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var candidate = Path.GetFullPath(Path.Combine(root, relativePath));
        if (!candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Model metadata reference must remain under the index directory.");
        }

        return candidate;
    }
}
