using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Nexus.Engine.ImageSubsystem.Diagnostics;

namespace Nexus.Engine.ImageSubsystem.Services
{
    public class InferenceTiming
    {
        public long ModelLoadMs { get; set; }
        public long InferenceMs { get; set; }
        public long TotalMs { get; set; }
        public bool ModelWasCached { get; set; }
    }

    public class InferenceResult
    {
        public string OutputPath { get; set; } = string.Empty;
        public InferenceTiming Timing { get; set; } = new();
    }

    public class ModelInferenceConfig
    {
        public string InputName { get; set; } = "pixel_values";
        public string OutputName { get; set; } = "alphas";
        public int InputHeight { get; set; } = 1024;
        public int InputWidth { get; set; } = 1024;
        public float[] Mean { get; set; } = { 0.485f, 0.456f, 0.406f };
        public float[] Std { get; set; } = { 0.229f, 0.224f, 0.225f };
        public bool UseSigmoid { get; set; } = false;
        public string ModelFilePath { get; set; } = string.Empty;
        public string ModelId { get; set; } = string.Empty;
    }

    public interface IRemoveBackgroundService : IDisposable
    {
        Task<InferenceResult> ExecuteInferenceAsync(
            IImagePipelineService pipeline,
            IOnnxModelManager modelManager,
            string inputImagePath,
            string modelDir,
            string outputMaskPath,
            CancellationToken cancellationToken = default);
    }

    public class RemoveBackgroundService : IRemoveBackgroundService
    {
        private bool _isDisposed;
        private readonly InferenceLogger _logger;

        public RemoveBackgroundService() : this(null) { }

        public RemoveBackgroundService(InferenceLogger? logger)
        {
            try
            {
                var logDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
                _logger = logger ?? new InferenceLogger(logDir);
            }
            catch
            {
                _logger = new InferenceLogger(Path.GetTempPath());
            }
        }

        public async Task<InferenceResult> ExecuteInferenceAsync(
            IImagePipelineService pipeline,
            IOnnxModelManager modelManager,
            string inputImagePath,
            string modelDir,
            string outputMaskPath,
            CancellationToken cancellationToken = default)
        {
            if (_isDisposed) throw new ObjectDisposedException(nameof(RemoveBackgroundService));
            if (pipeline == null) throw new ArgumentNullException(nameof(pipeline));
            if (modelManager == null) throw new ArgumentNullException(nameof(modelManager));

            var modelLoadSw = new Stopwatch();
            var inferenceSw = new Stopwatch();
            var totalSw = Stopwatch.StartNew();

            ModelInferenceConfig? config = null;
            string? configModelFile = null;
            string? configInputName = null;
            string? configOutputName = null;
            var result = new InferenceResult();
            bool modelWasCached = false;

            var requestId = $"INF-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";
            var callerThreadId = Environment.CurrentManagedThreadId;
            string modelId = "", sessionId = "", execProvider = "";

            _logger.LogLifecycle(requestId, "INFERENCE_REQUEST_CREATED", "RemoveBackground",
                elapsedMs: 0, threadId: callerThreadId);

            await pipeline.PipelineLock.WaitAsync(cancellationToken);

            try
            {
                if (pipeline.CurrentState == ImagePipelineLifecycleState.Failed)
                {
                    pipeline.TransitionState(ImagePipelineLifecycleState.Idle);
                    pipeline.TransitionState(ImagePipelineLifecycleState.ImageLoaded);
                }
                else if (pipeline.CurrentState == ImagePipelineLifecycleState.Idle)
                {
                    pipeline.TransitionState(ImagePipelineLifecycleState.ImageLoaded);
                }

                pipeline.TransitionState(ImagePipelineLifecycleState.ModelLoading);

                _logger.LogLifecycle(requestId, "MODEL_LOADING", $"Dir={modelDir}", elapsedMs: totalSw.ElapsedMilliseconds, threadId: callerThreadId);

                if (!File.Exists(inputImagePath))
                    throw new FileNotFoundException($"Input image file not found at '{inputImagePath}'.");

                if (string.IsNullOrEmpty(modelDir) || !Directory.Exists(modelDir))
                    throw new DirectoryNotFoundException($"Model directory not found: '{modelDir}'.");

                config = LoadModelConfig(modelDir);
                configModelFile = config.ModelFilePath;
                configInputName = config.InputName;
                configOutputName = config.OutputName;
                modelId = config.ModelId;
                cancellationToken.ThrowIfCancellationRequested();

                _logger.LogLifecycle(requestId, "MODEL_CONFIG_LOADED",
                    $"Model={modelId}, Input={configInputName}, Output={configOutputName}, Shape=1,3,{config.InputHeight},{config.InputWidth}",
                    modelId, elapsedMs: totalSw.ElapsedMilliseconds, threadId: callerThreadId);

                pipeline.TransitionState(ImagePipelineLifecycleState.ModelLoaded);

                var dir = Path.GetDirectoryName(outputMaskPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                pipeline.TransitionState(ImagePipelineLifecycleState.Processing);

                modelWasCached = modelManager.HasSession(config.ModelFilePath);
                modelLoadSw.Start();

                _logger.LogLifecycle(requestId, "SESSION_ACQUIRE_START",
                    $"ModelPath={configModelFile}, Cached={modelWasCached}",
                    modelId, elapsedMs: totalSw.ElapsedMilliseconds, threadId: callerThreadId);

                var session = await Task.Run(() =>
                {
                    var opts = new SessionOptions();
                    if (string.Equals(config.ModelId, "mvanet-q4f16", StringComparison.Ordinal))
                        opts.GraphOptimizationLevel = GraphOptimizationLevel.ORT_DISABLE_ALL;
                    else
                        opts.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;
                    opts.InterOpNumThreads = 1;
                    opts.IntraOpNumThreads = Math.Max(1, Environment.ProcessorCount - 1);
                    return modelManager.GetOrCreateSession(config.ModelFilePath, opts);
                }, cancellationToken);

                sessionId = $"SES-{config.ModelFilePath.GetHashCode():X8}";
                execProvider = "cpu";
                modelLoadSw.Stop();

                _logger.LogLifecycle(requestId, "SESSION_ACQUIRED",
                    $"Session={sessionId}, LoadMs={modelLoadSw.ElapsedMilliseconds}",
                    modelId, sessionId, execProvider, totalSw.ElapsedMilliseconds, callerThreadId);

                inferenceSw.Start();

                await Task.Run(() =>
                {
                    using var original = LoadOrientedBitmap(inputImagePath);

                    var tensor = CreateInputTensor(original, config);

                    var expectedShape = new[] { 1, 3, config.InputHeight, config.InputWidth };
                    var validation = TensorValidator.Validate(tensor, config.InputName,
                        expectedShape, $"mean={string.Join(",", config.Mean)};std={string.Join(",", config.Std)}",
                        original.Width, original.Height, config.InputWidth, config.InputHeight);

                    _logger.LogLifecycle(requestId, "TENSOR_VALIDATION",
                        validation.Snapshot.ToDiagnosticString(),
                        modelId, sessionId, execProvider,
                        totalSw.ElapsedMilliseconds, callerThreadId);

                    if (!validation.IsValid)
                    {
                        var firstError = validation.Errors[0];
                        var firstMsg = validation.Messages[0];
                        var diag = $"Tensor validation failed: {string.Join("; ", validation.Messages)}";
                        _logger.LogRawFailure(
                            $"[{requestId}] TENSOR_VALIDATION_FAILED: {diag}\n  Snapshot: {validation.Snapshot.ToDiagnosticString()}");
                        throw TensorValidator.CreateTensorException(firstError, firstMsg,
                            requestId, modelId, sessionId);
                    }

                    _logger.LogLifecycle(requestId, "INFERENCE_RUN_START",
                        $"Tensor={validation.Snapshot.ToDiagnosticString()}",
                        modelId, sessionId, execProvider,
                        totalSw.ElapsedMilliseconds, callerThreadId);

                    var inputs = new List<NamedOnnxValue>
                    {
                        NamedOnnxValue.CreateFromTensor(config.InputName, tensor)
                    };

                    using var results = session.Run(inputs);

                    _logger.LogLifecycle(requestId, "INFERENCE_RUN_PASS", "",
                        elapsedMs: totalSw.ElapsedMilliseconds,
                        modelId: modelId, sessionId: sessionId, executionProvider: execProvider,
                        threadId: callerThreadId);

                    var outputTensor = results.First(r => r.Name == config.OutputName).AsTensor<float>();
                    var logits = outputTensor.ToArray();
                    var dimensions = outputTensor.Dimensions.ToArray();

                    SaveTransparentPng(original, logits, dimensions, config, outputMaskPath);
                }, cancellationToken);

                inferenceSw.Stop();
                totalSw.Stop();

                pipeline.TransitionState(ImagePipelineLifecycleState.Result);
                result.OutputPath = outputMaskPath;
                result.Timing = new InferenceTiming
                {
                    ModelLoadMs = modelLoadSw.ElapsedMilliseconds,
                    InferenceMs = inferenceSw.ElapsedMilliseconds,
                    TotalMs = totalSw.ElapsedMilliseconds,
                    ModelWasCached = modelWasCached
                };

                _logger.LogLifecycle(requestId, "INFERENCE_COMPLETE",
                    $"TotalMs={totalSw.ElapsedMilliseconds}",
                    modelId, sessionId, execProvider, totalSw.ElapsedMilliseconds, callerThreadId);

                return result;
            }
            catch (OperationCanceledException)
            {
                totalSw.Stop();
                pipeline.TransitionState(ImagePipelineLifecycleState.Failed);
                _logger.LogLifecycle(requestId, "OPERATION_CANCELLED",
                    $"ElapsedMs={totalSw.ElapsedMilliseconds}",
                    modelId, sessionId, execProvider, totalSw.ElapsedMilliseconds, callerThreadId);
                throw;
            }
            catch (InferenceException)
            {
                totalSw.Stop();
                pipeline.TransitionState(ImagePipelineLifecycleState.Failed);
                throw;
            }
            catch (OnnxRuntimeException onnxEx)
            {
                totalSw.Stop();
                pipeline.TransitionState(ImagePipelineLifecycleState.Failed);
                var infEx = InferenceException.FromOnnxRuntime(onnxEx, requestId, modelId, sessionId);
                var diag = BuildDiagnosticString(infEx, onnxEx, inputImagePath, modelDir, outputMaskPath, config, configModelFile, configInputName, configOutputName);
                _logger.LogFailure(infEx, diag);
                throw infEx;
            }
            catch (OutOfMemoryException oomEx)
            {
                totalSw.Stop();
                pipeline.TransitionState(ImagePipelineLifecycleState.Failed);
                var infEx = new InferenceException(
                    InferenceErrorCode.OUT_OF_MEMORY,
                    "Memori tidak cukup untuk memproses gambar. Coba gambar yang lebih kecil.",
                    $"OOM: {oomEx.Message}", requestId, modelId, sessionId,
                    isRecoverable: false, innerException: oomEx);
                var diag = BuildDiagnosticString(infEx, oomEx, inputImagePath, modelDir, outputMaskPath, config, configModelFile, configInputName, configOutputName);
                _logger.LogFailure(infEx, diag);
                throw infEx;
            }
            catch (FileNotFoundException fnfEx)
            {
                totalSw.Stop();
                pipeline.TransitionState(ImagePipelineLifecycleState.Failed);
                var infEx = new InferenceException(
                    InferenceErrorCode.MODEL_OR_FILE_ACCESS_FAILURE,
                    "File yang diperlukan tidak ditemukan. Periksa direktori model dan file input.",
                    $"{fnfEx.GetType().Name}: {fnfEx.Message}", requestId, modelId, sessionId,
                    isRecoverable: false, innerException: fnfEx);
                var diag = BuildDiagnosticString(infEx, fnfEx, inputImagePath, modelDir, outputMaskPath, config, configModelFile, configInputName, configOutputName);
                _logger.LogFailure(infEx, diag);
                throw infEx;
            }
            catch (DirectoryNotFoundException dnfEx)
            {
                totalSw.Stop();
                pipeline.TransitionState(ImagePipelineLifecycleState.Failed);
                var infEx = new InferenceException(
                    InferenceErrorCode.MODEL_OR_FILE_ACCESS_FAILURE,
                    "Direktori model tidak ditemukan. Periksa konfigurasi model.",
                    $"{dnfEx.GetType().Name}: {dnfEx.Message}", requestId, modelId, sessionId,
                    isRecoverable: false, innerException: dnfEx);
                var diag = BuildDiagnosticString(infEx, dnfEx, inputImagePath, modelDir, outputMaskPath, config, configModelFile, configInputName, configOutputName);
                _logger.LogFailure(infEx, diag);
                throw infEx;
            }
            catch (Exception ex)
            {
                totalSw.Stop();
                pipeline.TransitionState(ImagePipelineLifecycleState.Failed);
                var infEx = new InferenceException(
                    InferenceErrorCode.UNKNOWN_RUNTIME_FAILURE,
                    "Terjadi kesalahan saat memproses gambar. Silakan coba ulang.",
                    $"{ex.GetType().Name}: {ex.Message}", requestId, modelId, sessionId,
                    isRecoverable: false, innerException: ex);
                var diag = BuildDiagnosticString(infEx, ex, inputImagePath, modelDir, outputMaskPath, config, configModelFile, configInputName, configOutputName);
                _logger.LogFailure(infEx, diag);
                throw infEx;
            }
            finally
            {
                pipeline.PipelineLock.Release();
            }
        }

        private static string BuildDiagnosticString(
            InferenceException infEx, Exception originalEx,
            string inputImagePath, string modelDir, string outputMaskPath,
            ModelInferenceConfig? config, string? configModelFile,
            string? configInputName, string? configOutputName)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"ErrorCode: {infEx.ErrorCode}");
            sb.AppendLine($"RequestId: {infEx.RequestId}");
            sb.AppendLine($"ModelId: {infEx.ModelId}");
            sb.AppendLine($"SessionId: {infEx.SessionId}");
            sb.AppendLine($"IsRecoverable: {infEx.IsRecoverable}");
            sb.AppendLine($"Exception Type: {originalEx.GetType().FullName}");
            sb.AppendLine($"Exception Message: {originalEx.Message}");
            sb.AppendLine($"Stack Trace: {originalEx.StackTrace}");
            var inner = originalEx.InnerException;
            int level = 0;
            while (inner != null)
            {
                sb.AppendLine($"Inner[{level}] Type: {inner.GetType().FullName}");
                sb.AppendLine($"Inner[{level}] Message: {inner.Message}");
                inner = inner.InnerException;
                level++;
            }
            sb.AppendLine($"Input Image: {inputImagePath}");
            sb.AppendLine($"Model Dir: {modelDir}");
            sb.AppendLine($"Output Path: {outputMaskPath}");
            sb.AppendLine($"Model File: {configModelFile ?? "(not loaded)"}");
            sb.AppendLine($"Input Name: {configInputName ?? "(not loaded)"}");
            sb.AppendLine($"Output Name: {configOutputName ?? "(not loaded)"}");
            if (config != null)
            {
                sb.AppendLine($"Input Shape: 1,3,{config.InputHeight},{config.InputWidth}");
                sb.AppendLine($"Use Sigmoid: {config.UseSigmoid}");
                sb.AppendLine($"Mean: {string.Join(",", config.Mean)}");
                sb.AppendLine($"Std: {string.Join(",", config.Std)}");
            }
            return sb.ToString().TrimEnd();
        }

        private static ModelInferenceConfig LoadModelConfig(string modelDir)
        {
            var config = new ModelInferenceConfig();
            var metaPath = Path.Combine(modelDir, "metadata.json");

            var dirName = new DirectoryInfo(modelDir).Name;
            string modelFile;
            if (string.Equals(dirName, "RMBG-2.0", StringComparison.OrdinalIgnoreCase))
                modelFile = "model_int8.onnx";
            else if (string.Equals(dirName, "MVANet", StringComparison.OrdinalIgnoreCase))
                modelFile = "model_q4f16.onnx";
            else
                modelFile = "model.onnx";
            config.ModelFilePath = Path.Combine(modelDir, modelFile);
            if (!File.Exists(config.ModelFilePath))
            {
                var onnxFiles = Directory.GetFiles(modelDir, "*.onnx");
                if (onnxFiles.Length == 0)
                    throw new FileNotFoundException($"No .onnx file found in model directory '{modelDir}'.");
                config.ModelFilePath = onnxFiles[0];
            }

            if (!File.Exists(metaPath))
                return config;

            var json = File.ReadAllText(metaPath);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("modelFile", out var mf) && !string.IsNullOrWhiteSpace(mf.GetString()))
            {
                var customPath = Path.Combine(modelDir, mf.GetString()!);
                if (File.Exists(customPath))
                    config.ModelFilePath = customPath;
            }

            if (root.TryGetProperty("modelId", out var mid))
                config.ModelId = mid.GetString() ?? string.Empty;

            if (root.TryGetProperty("inputName", out var flatInput))
            {
                config.InputName = flatInput.GetString() ?? "pixel_values";
            }
            else if (root.TryGetProperty("input", out var nestedInput))
            {
                if (nestedInput.TryGetProperty("name", out var n))
                    config.InputName = n.GetString() ?? "pixel_values";
                if (nestedInput.TryGetProperty("shape", out var shape))
                {
                    var dims = shape.EnumerateArray().Select(s => s.GetInt32()).ToArray();
                    if (dims.Length == 4)
                    {
                        if (dims[1] == 3) { config.InputHeight = dims[2]; config.InputWidth = dims[3]; }
                        else if (dims[3] == 3) { config.InputHeight = dims[1]; config.InputWidth = dims[2]; }
                    }
                }
                if (nestedInput.TryGetProperty("mean", out var mean))
                    config.Mean = mean.EnumerateArray().Select(m => m.GetSingle()).ToArray();
                if (nestedInput.TryGetProperty("std", out var std))
                    config.Std = std.EnumerateArray().Select(m => m.GetSingle()).ToArray();
            }

            if (root.TryGetProperty("outputName", out var flatOut))
            {
                config.OutputName = flatOut.GetString() ?? "alphas";
            }
            else if (root.TryGetProperty("output", out var nestedOutput))
            {
                if (nestedOutput.TryGetProperty("name", out var no))
                    config.OutputName = no.GetString() ?? "alphas";
                if (nestedOutput.TryGetProperty("activation", out var act))
                    config.UseSigmoid = act.GetString() == "sigmoid";
            }

            return config;
        }

        private static Bitmap LoadOrientedBitmap(string path)
        {
            using (var source = new Bitmap(path))
            {
                var result = new Bitmap(source);
                const int orientationId = 0x0112;
                if (source.PropertyIdList.Contains(orientationId))
                {
                    var propItem = source.GetPropertyItem(orientationId);
                    int orientation = propItem?.Value != null && propItem.Value.Length > 0 ? propItem.Value[0] : 1;
                    var transform = RotateFlipType.RotateNoneFlipNone;
                    if (orientation == 2) transform = RotateFlipType.RotateNoneFlipX;
                    else if (orientation == 3) transform = RotateFlipType.Rotate180FlipNone;
                    else if (orientation == 4) transform = RotateFlipType.Rotate180FlipX;
                    else if (orientation == 5) transform = RotateFlipType.Rotate90FlipX;
                    else if (orientation == 6) transform = RotateFlipType.Rotate90FlipNone;
                    else if (orientation == 7) transform = RotateFlipType.Rotate270FlipX;
                    else if (orientation == 8) transform = RotateFlipType.Rotate270FlipNone;
                    result.RotateFlip(transform);
                }
                return result;
            }
        }

        private static DenseTensor<float> CreateInputTensor(Bitmap original, ModelInferenceConfig config)
        {
            int width = config.InputWidth;
            int height = config.InputHeight;
            using (var resized = new Bitmap(width, height, PixelFormat.Format24bppRgb))
            {
                using (var graphics = Graphics.FromImage(resized))
                {
                    graphics.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                    graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBilinear;
                    graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                    graphics.DrawImage(original, 0, 0, width, height);
                }
                var tensor = new DenseTensor<float>(new[] { 1, 3, height, width });
                var rectangle = new Rectangle(0, 0, width, height);
                var data = resized.LockBits(rectangle, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
                try
                {
                    int stride = Math.Abs(data.Stride);
                    byte[] pixels = new byte[stride * height];
                    Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
                    int plane = width * height;
                    for (int y = 0; y < height; y++)
                    {
                        int row = data.Stride >= 0 ? y * stride : (height - 1 - y) * stride;
                        for (int x = 0; x < width; x++)
                        {
                            int source = row + (x * 3);
                            int index = y * width + x;
                            tensor.Buffer.Span[index] = ((pixels[source + 2] / 255f) - config.Mean[0]) / config.Std[0];
                            tensor.Buffer.Span[plane + index] = ((pixels[source + 1] / 255f) - config.Mean[1]) / config.Std[1];
                            tensor.Buffer.Span[(plane * 2) + index] = ((pixels[source] / 255f) - config.Mean[2]) / config.Std[2];
                        }
                    }
                }
                finally
                {
                    resized.UnlockBits(data);
                }
                return tensor;
            }
        }

        private static void SaveTransparentPng(Bitmap original, float[] logits, int[] dimensions, ModelInferenceConfig config, string outputPath)
        {
            if (dimensions.Length != 4 && dimensions.Length != 3)
                throw new InvalidDataException($"Expected single-channel alpha output (3D [1,H,W] or 4D [1,1,H,W]), got {dimensions.Length}D.");
            if (dimensions[0] != 1)
                throw new InvalidDataException($"Expected batch size 1, got {dimensions[0]}.");
            if (dimensions.Length == 4 && dimensions[1] != 1)
                throw new InvalidDataException($"Expected single-channel alpha output, got {dimensions[1]} channels.");

            int maskHeight = dimensions.Length >= 4 ? dimensions[2] : dimensions[1];
            int maskWidth = dimensions.Length >= 4 ? dimensions[3] : dimensions[2];
            if (logits.Length != maskWidth * maskHeight)
                throw new InvalidDataException($"Alpha output length ({logits.Length}) does not match expected dimensions ({maskWidth}x{maskHeight}={maskWidth * maskHeight}).");

            using (var result = new Bitmap(original.Width, original.Height, PixelFormat.Format32bppArgb))
            {
                using (var graphics = Graphics.FromImage(result))
                {
                    graphics.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                    graphics.DrawImageUnscaled(original, 0, 0);
                }
                var rectangle = new Rectangle(0, 0, result.Width, result.Height);
                var data = result.LockBits(rectangle, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
                try
                {
                    int stride = Math.Abs(data.Stride);
                    byte[] pixels = new byte[stride * result.Height];
                    Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
                    for (int y = 0; y < result.Height; y++)
                    {
                        float sourceY = result.Height == 1 ? 0 : y * (maskHeight - 1f) / (result.Height - 1f);
                        int y0 = (int)Math.Floor(sourceY);
                        int y1 = Math.Min(maskHeight - 1, y0 + 1);
                        float fy = sourceY - y0;
                        int row = data.Stride >= 0 ? y * stride : (result.Height - 1 - y) * stride;
                        for (int x = 0; x < result.Width; x++)
                        {
                            float sourceX = result.Width == 1 ? 0 : x * (maskWidth - 1f) / (result.Width - 1f);
                            int x0 = (int)Math.Floor(sourceX);
                            int x1 = Math.Min(maskWidth - 1, x0 + 1);
                            float fx = sourceX - x0;
                            float top = logits[y0 * maskWidth + x0] * (1f - fx) + logits[y0 * maskWidth + x1] * fx;
                            float bottom = logits[y1 * maskWidth + x0] * (1f - fx) + logits[y1 * maskWidth + x1] * fx;
                            float logit = top * (1f - fy) + bottom * fy;
                            float probability = config.UseSigmoid
                                ? (logit >= 0f ? 1f / (1f + (float)Math.Exp(-logit)) : (float)Math.Exp(logit) / (1f + (float)Math.Exp(logit)))
                                : Math.Max(0f, Math.Min(1f, logit));
                            pixels[row + x * 4 + 3] = (byte)Math.Max(0, Math.Min(255, (int)Math.Round(probability * 255f)));
                        }
                    }
                    Marshal.Copy(pixels, 0, data.Scan0, pixels.Length);
                }
                finally
                {
                    result.UnlockBits(data);
                }
                result.Save(outputPath, ImageFormat.Png);
            }
        }

        public void Dispose()
        {
            _isDisposed = true;
        }
    }
}
