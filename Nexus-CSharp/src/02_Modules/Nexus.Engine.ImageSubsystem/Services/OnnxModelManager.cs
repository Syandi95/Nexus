using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Nexus.Contracts;
using Nexus.Foundation.Registry;

namespace Nexus.Engine.ImageSubsystem.Services
{
    public class ModelMetadata
    {
        public int SchemaVersion { get; set; } = 1;
        public string? Status { get; set; } = "experimental";
        public string? ModelFile { get; set; }
        public string? DisplayName { get; set; }
        public string? InputName { get; set; }
        public string? OutputName { get; set; }
        public long[]? InputShape { get; set; }
        public long[]? OutputShape { get; set; }
        public string? TensorType { get; set; } = "float32";
        public long Opset { get; set; }
        public string? Sha256 { get; set; }
    }

    public interface IOnnxModelManager : IDisposable
    {
        long DetectOpsetVersion(string path);
        Task<Dictionary<string, string>> LoadModelSlotsAsync();
        Task SaveModelSlotsAsync(Dictionary<string, string> slots);
        InferenceSession GetOrCreateSession(string modelPath, SessionOptions? options = null);
        bool HasSession(string modelPath);
        Task<ModelMetadata?> ReadMetadataAsync(string metadataPath);
        Task<string> GenerateMetadataAsync(string onnxPath, string outputDir);
        Task<string> GenerateMetadataAsync(string onnxPath, string outputDir, AICapability? capability);
        bool ValidateModelPackage(string modelDir);
        string ResolveModelDirectory(string slotValue, string modelsRoot);
    }

    public class OnnxModelManager : IOnnxModelManager
    {
        private readonly string _slotsFilePath;
        private readonly ConcurrentDictionary<string, InferenceSession> _sessions = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _lock = new();
        private readonly ModelMetadataParser _metadataParser;
        private bool _isDisposed;

        public OnnxModelManager(
            string slotsFilePath = "model_slots.json",
            ModelMetadataParser? metadataParser = null)
        {
            _slotsFilePath = slotsFilePath;
            _metadataParser = metadataParser ?? new ModelMetadataParser();
        }

        public long DetectOpsetVersion(string path)
        {
            if (!File.Exists(path)) return 14;

            try
            {
                using var stream = File.OpenRead(path);
                using var reader = new BinaryReader(stream);
                while (stream.Position < stream.Length)
                {
                    long tag = 0;
                    int shift = 0;
                    while (true)
                    {
                        byte b = reader.ReadByte();
                        tag |= (long)(b & 0x7F) << shift;
                        if ((b & 0x80) == 0) break;
                        shift += 7;
                    }

                    int fieldNum = (int)(tag >> 3);
                    int wireType = (int)(tag & 7);

                    if (wireType == 0)
                    {
                        while ((reader.ReadByte() & 0x80) != 0) { }
                    }
                    else if (wireType == 2)
                    {
                        long len = 0;
                        shift = 0;
                        while (true)
                        {
                            byte b = reader.ReadByte();
                            len |= (long)(b & 0x7F) << shift;
                            if ((b & 0x80) == 0) break;
                            shift += 7;
                        }

                        if (fieldNum == 8)
                        {
                            long end = stream.Position + len;
                            long version = 17;
                            while (stream.Position < end)
                            {
                                long innerTag = 0;
                                shift = 0;
                                while (true)
                                {
                                    byte b = reader.ReadByte();
                                    innerTag |= (long)(b & 0x7F) << shift;
                                    if ((b & 0x80) == 0) break;
                                    shift += 7;
                                }
                                int innerField = (int)(innerTag >> 3);
                                int innerWire = (int)(innerTag & 7);

                                if (innerWire == 0 && innerField == 2)
                                {
                                    version = 0;
                                    shift = 0;
                                    while (true)
                                    {
                                        byte b = reader.ReadByte();
                                        version |= (long)(b & 0x7F) << shift;
                                        if ((b & 0x80) == 0) break;
                                        shift += 7;
                                    }
                                }
                                else if (innerWire == 2)
                                {
                                    long innerLen = 0;
                                    shift = 0;
                                    while (true)
                                    {
                                        byte b = reader.ReadByte();
                                        innerLen |= (long)(b & 0x7F) << shift;
                                        if ((b & 0x80) == 0) break;
                                        shift += 7;
                                    }
                                    stream.Seek(innerLen, SeekOrigin.Current);
                                }
                                else if (innerWire == 0)
                                {
                                    while ((reader.ReadByte() & 0x80) != 0) { }
                                }
                                else break;
                            }
                            return version;
                        }
                        else
                        {
                            stream.Seek(len, SeekOrigin.Current);
                        }
                    }
                    else if (wireType == 5)
                    {
                        stream.Seek(4, SeekOrigin.Current);
                    }
                    else if (wireType == 1)
                    {
                        stream.Seek(8, SeekOrigin.Current);
                    }
                    else break;
                }
            }
            catch
            {
            }

            return 14;
        }

        public async Task<Dictionary<string, string>> LoadModelSlotsAsync()
        {
            if (!File.Exists(_slotsFilePath))
            {
                return new Dictionary<string, string>
                {
                    { "Lite", "PhotosSeg" },
                    { "Balanced", "BiRefNet_lite" },
                    { "High Quality", "RMBG-2.0" }
                };
            }

            var json = await File.ReadAllTextAsync(_slotsFilePath);
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new Dictionary<string, string>();
        }

        public async Task SaveModelSlotsAsync(Dictionary<string, string> slots)
        {
            var json = JsonSerializer.Serialize(slots, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(_slotsFilePath, json);
        }

        public bool HasSession(string modelPath)
        {
            return _sessions.ContainsKey(modelPath);
        }

        public InferenceSession GetOrCreateSession(string modelPath, SessionOptions? options = null)
        {
            if (_isDisposed) throw new ObjectDisposedException(nameof(OnnxModelManager));

            return _sessions.GetOrAdd(modelPath, path =>
            {
                lock (_lock)
                {
                    if (!File.Exists(path))
                    {
                        throw new FileNotFoundException($"ONNX Model not found at '{path}'.");
                    }

                    if (options == null)
                    {
                        options = new SessionOptions();
                        options.LogSeverityLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_ERROR;
                        if (path.Contains("q4", StringComparison.OrdinalIgnoreCase) ||
                            path.Contains("mvanet", StringComparison.OrdinalIgnoreCase))
                        {
                            options.GraphOptimizationLevel =
                                Microsoft.ML.OnnxRuntime.GraphOptimizationLevel.ORT_ENABLE_EXTENDED;
                        }
                        else
                        {
                            options.GraphOptimizationLevel =
                                Microsoft.ML.OnnxRuntime.GraphOptimizationLevel.ORT_ENABLE_ALL;
                        }
                        options.ExecutionMode = ExecutionMode.ORT_SEQUENTIAL;
                        options.InterOpNumThreads = 1;
                        options.IntraOpNumThreads = 4;
                    }
                    else
                    {
                        options.LogSeverityLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_ERROR;
                    }

                    try
                    {
                        return new InferenceSession(path, options);
                    }
                    catch (OnnxRuntimeException) when (options.GraphOptimizationLevel == Microsoft.ML.OnnxRuntime.GraphOptimizationLevel.ORT_ENABLE_ALL)
                    {
                        try
                        {
                            options.GraphOptimizationLevel = Microsoft.ML.OnnxRuntime.GraphOptimizationLevel.ORT_ENABLE_EXTENDED;
                            return new InferenceSession(path, options);
                        }
                        catch (OnnxRuntimeException)
                        {
                            options.GraphOptimizationLevel = Microsoft.ML.OnnxRuntime.GraphOptimizationLevel.ORT_ENABLE_BASIC;
                            return new InferenceSession(path, options);
                        }
                    }
                }
            });
        }

        public async Task<ModelMetadata?> ReadMetadataAsync(string metadataPath)
        {
            if (!File.Exists(metadataPath)) return null;

            try
            {
                var json = await File.ReadAllTextAsync(metadataPath);
                return JsonSerializer.Deserialize<ModelMetadata>(json);
            }
            catch
            {
                return null;
            }
        }

        public Task<string> GenerateMetadataAsync(string onnxPath, string outputDir) =>
            GenerateMetadataAsync(onnxPath, outputDir, null);

        public async Task<string> GenerateMetadataAsync(string onnxPath, string outputDir, AICapability? capability)
        {
            var displayName = Path.GetFileNameWithoutExtension(onnxPath);
            string sha256;

            using (var stream = File.OpenRead(onnxPath))
            using (var sha = SHA256.Create())
            {
                var hash = await sha.ComputeHashAsync(stream);
                sha256 = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }

            InferenceSession session;
            try
            {
                var options = new SessionOptions();
                options.GraphOptimizationLevel =
                    Microsoft.ML.OnnxRuntime.GraphOptimizationLevel.ORT_DISABLE_ALL;
                session = new InferenceSession(onnxPath, options);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Model ONNX tidak valid: {ex.Message}", ex);
            }

            string? inputName = null;
            long[]? inputShape = null;
            Dictionary<string, long[]>? additionalInputs = null;
            string? outputName = null;
            long[]? outputShape = null;
            string tensorType = "float32";

            using (session)
            {
                if (capability == AICapability.InpaintingGeneral)
                {
                    var inputs = session.InputMetadata.ToList();
                    if (inputs.Count < 2)
                    {
                        throw new InvalidOperationException("Model inpainting membutuhkan minimal 2 input (image dan mask).");
                    }

                    var imageInputs = new List<(string Name, long[] Shape)>();
                    var maskInputs = new List<(string Name, long[] Shape)>();

                    foreach (var kvp in inputs)
                    {
                        var shape = kvp.Value.Dimensions.Select(d => (long)d).ToArray();
                        if (shape.Length != 4 || shape[0] != 1)
                        {
                            throw new InvalidOperationException($"Input '{kvp.Key}' bukan NCHW 4D tensor valid dengan batch 1.");
                        }

                        var channels = shape[1];
                        var name = kvp.Key;

                        if (channels == 3)
                        {
                            imageInputs.Add((name, shape));
                        }
                        else if (channels == 1)
                        {
                            maskInputs.Add((name, shape));
                        }
                        else
                        {
                            throw new InvalidOperationException($"Input '{name}' memiliki channel count {channels} yang tidak didukung untuk inpainting.");
                        }
                    }

                    if (imageInputs.Count == 0 || maskInputs.Count == 0)
                    {
                        throw new InvalidOperationException("Model inpainting harus memiliki setidaknya satu input citra (3 channel) dan satu input mask (1 channel).");
                    }

                    if (imageInputs.Count != maskInputs.Count)
                    {
                        throw new InvalidOperationException($"Jumlah input citra ({imageInputs.Count}) dan mask ({maskInputs.Count}) tidak seimbang.");
                    }

                    // Sort image inputs by resolution descending (largest first is primary)
                    var sortedImages = imageInputs.OrderByDescending(i => i.Shape[2] * i.Shape[3]).ToList();
                    var primaryImage = sortedImages[0];

                    // Check for ambiguous equal resolution images with ambiguous names
                    if (sortedImages.Count > 1 && sortedImages[0].Shape[2] == sortedImages[1].Shape[2] &&
                        !sortedImages[0].Name.Contains("hr", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException("Input citra ambigu dengan resolusi sama.");
                    }

                    inputName = primaryImage.Name;
                    inputShape = primaryImage.Shape;

                    additionalInputs = new Dictionary<string, long[]>(StringComparer.Ordinal);
                    for (int i = 1; i < sortedImages.Count; i++)
                    {
                        additionalInputs[sortedImages[i].Name] = sortedImages[i].Shape;
                    }
                    foreach (var maskIn in maskInputs)
                    {
                        additionalInputs[maskIn.Name] = maskIn.Shape;
                    }

                    var primaryMeta = session.InputMetadata[inputName];
                    tensorType = primaryMeta.ElementType switch
                    {
                        Type t when t == typeof(float) => "float32",
                        Type t when t == typeof(double) => "float64",
                        Type t when t == typeof(long) => "int64",
                        Type t when t == typeof(int) => "int32",
                        Type t when t == typeof(byte) => "uint8",
                        _ => "float32"
                    };
                }
                else
                {
                    foreach (var kvp in session.InputMetadata)
                    {
                        inputName = kvp.Key;
                        inputShape = kvp.Value.Dimensions.Select(d => (long)d).ToArray();
                        tensorType = kvp.Value.ElementType switch
                        {
                            Type t when t == typeof(float) => "float32",
                            Type t when t == typeof(double) => "float64",
                            Type t when t == typeof(long) => "int64",
                            Type t when t == typeof(int) => "int32",
                            Type t when t == typeof(byte) => "uint8",
                            _ => "float32"
                        };
                        break;
                    }
                }

                foreach (var kvp in session.OutputMetadata)
                {
                    outputName = kvp.Key;
                    outputShape = kvp.Value.Dimensions.Select(d => (long)d).ToArray();
                    break;
                }
            }

            if (string.IsNullOrEmpty(inputName) || string.IsNullOrEmpty(outputName))
            {
                throw new InvalidOperationException("Metadata input/output ONNX kosong.");
            }

            var metadata = new ResolvedModelDescriptor
            {
                ModelId = Path.GetFileName(
                    Path.TrimEndingDirectorySeparator(
                        Path.GetFullPath(outputDir))),
                ModelPath = Path.GetFullPath(onnxPath),
                ModelType = ModelType.Onnx,
                DisplayName = displayName,
                InputTensorName = inputName,
                OutputTensorName = outputName,
                InputShape = inputShape,
                OutputShape = outputShape,
                TensorType = tensorType,
                Sha256 = sha256,
                AdditionalInputs = additionalInputs
            };

            var metaPath = Path.Combine(outputDir, "metadata.json");
            _metadataParser.WriteOnnxMetadata(metaPath, metadata);
            var roundTripped = _metadataParser.ParseOnnxMetadata(metaPath);
            if (!MetadataMatches(metadata, roundTripped))
            {
                throw new InvalidDataException(
                    "Generated ONNX metadata failed canonical schema round-trip validation.");
            }

            return metaPath;
        }

        private static bool MetadataMatches(
            ResolvedModelDescriptor expected,
            ResolvedModelDescriptor actual) =>
            string.Equals(expected.ModelId, actual.ModelId, StringComparison.Ordinal) &&
            string.Equals(expected.ModelPath, actual.ModelPath, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(expected.DisplayName, actual.DisplayName, StringComparison.Ordinal) &&
            string.Equals(expected.InputTensorName, actual.InputTensorName, StringComparison.Ordinal) &&
            string.Equals(expected.OutputTensorName, actual.OutputTensorName, StringComparison.Ordinal) &&
            EqualsShape(expected.InputShape, actual.InputShape) &&
            EqualsShape(expected.OutputShape, actual.OutputShape) &&
            string.Equals(expected.TensorType, actual.TensorType, StringComparison.Ordinal) &&
            string.Equals(expected.Sha256, actual.Sha256, StringComparison.Ordinal) &&
            EqualsAdditionalInputs(expected.AdditionalInputs, actual.AdditionalInputs);

        private static bool EqualsAdditionalInputs(
            IReadOnlyDictionary<string, long[]>? expected,
            IReadOnlyDictionary<string, long[]>? actual)
        {
            if (expected == null && actual == null) return true;
            if (expected == null || actual == null) return false;
            if (expected.Count != actual.Count) return false;
            foreach (var kvp in expected)
            {
                if (!actual.TryGetValue(kvp.Key, out var actualShape) || !EqualsShape(kvp.Value, actualShape))
                    return false;
            }
            return true;
        }

        private static bool EqualsShape(long[]? expected, long[]? actual) =>
            expected is null
                ? actual is null
                : actual is not null && expected.SequenceEqual(actual);

        public bool ValidateModelPackage(string modelDir)
        {
            if (string.IsNullOrEmpty(modelDir) || !Directory.Exists(modelDir))
                return false;

            var onnxFiles = Directory.GetFiles(modelDir, "*.onnx");
            if (onnxFiles.Length == 0) return false;

            var metaPath = Path.Combine(modelDir, "metadata.json");
            if (!File.Exists(metaPath)) return false;

            return true;
        }

        public string ResolveModelDirectory(string slotValue, string modelsRoot)
        {
            if (string.IsNullOrWhiteSpace(slotValue))
                return modelsRoot;

            if (File.Exists(slotValue))
                return Path.GetDirectoryName(slotValue) ?? modelsRoot;

            if (Path.IsPathRooted(slotValue))
                return slotValue;

            return Path.Combine(modelsRoot, slotValue);
        }

        public void Dispose()
        {
            if (!_isDisposed)
            {
                lock (_lock)
                {
                    foreach (var kvp in _sessions)
                    {
                        kvp.Value.Dispose();
                    }
                    _sessions.Clear();
                    _isDisposed = true;
                }
            }
        }
    }
}
