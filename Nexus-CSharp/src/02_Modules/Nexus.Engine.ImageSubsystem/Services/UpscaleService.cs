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
    public class UpscaleConfig
    {
        public string InputName { get; set; } = "input";
        public string OutputName { get; set; } = "output";
        public int NativeScale { get; set; } = 4;
        public string ModelFilePath { get; set; } = string.Empty;
        public string ModelId { get; set; } = string.Empty;
    }

    public interface IUpscaleService : IDisposable
    {
        Task<InferenceResult> ExecuteUpscaleAsync(
            IImagePipelineService pipeline,
            IOnnxModelManager modelManager,
            string inputImagePath,
            string modelDir,
            int targetScale,
            string outputImagePath,
            CancellationToken cancellationToken = default);
    }

    public class UpscaleService : IUpscaleService
    {
        private bool _isDisposed;
        private readonly InferenceLogger _logger;

        public UpscaleService() : this(null) { }

        public UpscaleService(InferenceLogger? logger)
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

        public async Task<InferenceResult> ExecuteUpscaleAsync(
            IImagePipelineService pipeline,
            IOnnxModelManager modelManager,
            string inputImagePath,
            string modelDir,
            int targetScale,
            string outputImagePath,
            CancellationToken cancellationToken = default)
        {
            if (_isDisposed) throw new ObjectDisposedException(nameof(UpscaleService));
            if (pipeline == null) throw new ArgumentNullException(nameof(pipeline));
            if (modelManager == null) throw new ArgumentNullException(nameof(modelManager));

            var modelLoadSw = new Stopwatch();
            var inferenceSw = new Stopwatch();
            var totalSw = Stopwatch.StartNew();

            UpscaleConfig? config = null;
            string? configModelFile = null;
            string? configInputName = null;
            string? configOutputName = null;
            var result = new InferenceResult();
            bool modelWasCached = false;

            var requestId = $"INF-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";
            var callerThreadId = Environment.CurrentManagedThreadId;
            string modelId = "", sessionId = "", execProvider = "";

            _logger.LogLifecycle(requestId, "INFERENCE_REQUEST_CREATED", "Upscale",
                elapsedMs: 0, threadId: callerThreadId);

            if (!await pipeline.PipelineLock.WaitAsync(0, cancellationToken))
            {
                throw new InferenceException(
                    InferenceErrorCode.BUSY,
                    "Proses AI Upscale lain sedang berjalan.",
                    "Single inference contract enforced: concurrent execution rejected.",
                    requestId, modelId, sessionId);
            }

            try
            {
                if (pipeline.CurrentState == ImagePipelineLifecycleState.Failed ||
                    pipeline.CurrentState == ImagePipelineLifecycleState.Result)
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

                cancellationToken.ThrowIfCancellationRequested();

                config = LoadConfig(modelDir);
                configModelFile = config.ModelFilePath;
                configInputName = config.InputName;
                configOutputName = config.OutputName;
                modelId = config.ModelId;

                _logger.LogLifecycle(requestId, "MODEL_CONFIG_LOADED",
                    $"Model={modelId}, Input={configInputName}, Output={configOutputName}",
                    modelId, elapsedMs: totalSw.ElapsedMilliseconds, threadId: callerThreadId);

                pipeline.TransitionState(ImagePipelineLifecycleState.ModelLoaded);

                var dir = Path.GetDirectoryName(outputImagePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                pipeline.TransitionState(ImagePipelineLifecycleState.Processing);

                // If target scale is 1, skip AI inference — just copy
                if (targetScale == 1)
                {
                    modelWasCached = false;
                    inferenceSw.Start();
                    await Task.Run(() =>
                    {
                        using var original = new Bitmap(inputImagePath);
                        SaveBitmapAtScale(original, null, null, 1, config.NativeScale, outputImagePath);
                    }, cancellationToken);
                    inferenceSw.Stop();
                    totalSw.Stop();

                    pipeline.TransitionState(ImagePipelineLifecycleState.Result);
                    result.OutputPath = outputImagePath;
                    result.Timing = new InferenceTiming
                    {
                        ModelLoadMs = 0,
                        InferenceMs = inferenceSw.ElapsedMilliseconds,
                        TotalMs = totalSw.ElapsedMilliseconds,
                        ModelWasCached = false
                    };

                    _logger.LogLifecycle(requestId, "INFERENCE_SKIP_COPY", $"Scale=1 copy only", modelId, sessionId, execProvider, totalSw.ElapsedMilliseconds, callerThreadId);
                    return result;
                }

                modelWasCached = modelManager.HasSession(config.ModelFilePath);
                modelLoadSw.Start();

                _logger.LogLifecycle(requestId, "SESSION_ACQUIRE_START",
                    $"ModelPath={configModelFile}, Cached={modelWasCached}",
                    modelId, elapsedMs: totalSw.ElapsedMilliseconds, threadId: callerThreadId);

                var session = await Task.Run(() =>
                {
                    var opts = CreateSessionOptions();
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
                    cancellationToken.ThrowIfCancellationRequested();
                    using var original = new Bitmap(inputImagePath);
                    var tensor = CreateDynamicRgbTensor(original);

                    var expectedShape = new[] { 1, 3, original.Height, original.Width };
                    var validation = TensorValidator.Validate(tensor, config.InputName,
                        expectedShape, "none;rgb;div255",
                        original.Width, original.Height, original.Width, original.Height);

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

                    cancellationToken.ThrowIfCancellationRequested();

                    using var runOptions = new RunOptions();
                    using var reg = cancellationToken.Register(() =>
                    {
                        try { runOptions.Terminate = true; } catch { }
                    });
                    using var results = session.Run(inputs, new[] { config.OutputName }, runOptions);

                    _logger.LogLifecycle(requestId, "INFERENCE_RUN_PASS", "",
                        modelId: modelId, sessionId: sessionId, executionProvider: execProvider,
                        elapsedMs: totalSw.ElapsedMilliseconds, threadId: callerThreadId);

                    var outputTensor = results.First(r => r.Name == config.OutputName).AsTensor<float>();
                    var pixels = outputTensor.ToArray();
                    var dimensions = outputTensor.Dimensions.ToArray();

                    SaveBitmapAtScale(original, pixels, dimensions, targetScale, config.NativeScale, outputImagePath);
                }, cancellationToken);

                inferenceSw.Stop();
                totalSw.Stop();

                pipeline.TransitionState(ImagePipelineLifecycleState.Result);
                result.OutputPath = outputImagePath;
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
                if (pipeline.CurrentState != ImagePipelineLifecycleState.Disposed)
                {
                    pipeline.TransitionState(ImagePipelineLifecycleState.Idle);
                }
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
                var diag = BuildUpscaleDiagnostic(infEx, onnxEx, inputImagePath, modelDir, outputImagePath, config, configModelFile, configInputName, configOutputName, targetScale);
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
                var diag = BuildUpscaleDiagnostic(infEx, oomEx, inputImagePath, modelDir, outputImagePath, config, configModelFile, configInputName, configOutputName, targetScale);
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
                var diag = BuildUpscaleDiagnostic(infEx, ex, inputImagePath, modelDir, outputImagePath, config, configModelFile, configInputName, configOutputName, targetScale);
                _logger.LogFailure(infEx, diag);
                throw infEx;
            }
            finally
            {
                pipeline.PipelineLock.Release();
            }
        }

        private static string BuildUpscaleDiagnostic(
            InferenceException infEx, Exception originalEx,
            string inputImagePath, string modelDir, string outputImagePath,
            UpscaleConfig? config, string? configModelFile,
            string? configInputName, string? configOutputName, int targetScale)
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
            sb.AppendLine($"Output Path: {outputImagePath}");
            sb.AppendLine($"Model File: {configModelFile ?? "(not loaded)"}");
            sb.AppendLine($"Input Name: {configInputName ?? "(not loaded)"}");
            sb.AppendLine($"Output Name: {configOutputName ?? "(not loaded)"}");
            sb.AppendLine($"Target Scale: {targetScale}");
            if (config != null)
            {
                sb.AppendLine($"Native Scale: {config.NativeScale}");
            }
            return sb.ToString().TrimEnd();
        }

        internal static UpscaleConfig LoadConfig(string modelDir)
        {
            var config = new UpscaleConfig();
            var metaPath = Path.Combine(modelDir, "metadata.json");

            var onnxFiles = Directory.GetFiles(modelDir, "*.onnx");
            if (onnxFiles.Length == 0)
                throw new FileNotFoundException($"No .onnx file found in model directory '{modelDir}'.");
            config.ModelFilePath = onnxFiles[0];

            if (!File.Exists(metaPath))
                return config;

            var json = File.ReadAllText(metaPath);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("modelId", out var mid))
                config.ModelId = mid.GetString() ?? string.Empty;

            if (root.TryGetProperty("modelFile", out var mf))
            {
                var file = mf.GetString();
                if (!string.IsNullOrEmpty(file))
                {
                    var p = Path.Combine(modelDir, file);
                    if (File.Exists(p))
                        config.ModelFilePath = p;
                }
            }

            if (root.TryGetProperty("input", out var input))
            {
                if (input.TryGetProperty("name", out var n))
                    config.InputName = n.GetString() ?? "input";
            }

            if (root.TryGetProperty("output", out var output))
            {
                if (output.TryGetProperty("name", out var no))
                    config.OutputName = no.GetString() ?? "output";
                if (output.TryGetProperty("nativeScale", out var ns))
                    config.NativeScale = ns.GetInt32();
            }

            return config;
        }

        private static DenseTensor<float> CreateDynamicRgbTensor(Bitmap original)
        {
            int width = original.Width;
            int height = original.Height;
            using (var rgb = new Bitmap(width, height, PixelFormat.Format24bppRgb))
            {
                using (var graphics = Graphics.FromImage(rgb))
                {
                    graphics.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                    graphics.DrawImageUnscaled(original, 0, 0);
                }
                var tensor = new DenseTensor<float>(new[] { 1, 3, height, width });
                var rectangle = new Rectangle(0, 0, width, height);
                var data = rgb.LockBits(rectangle, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
                try
                {
                    int stride = Math.Abs(data.Stride);
                    byte[] sourcePixels = new byte[stride * height];
                    Marshal.Copy(data.Scan0, sourcePixels, 0, sourcePixels.Length);
                    int plane = checked(width * height);
                    for (int y = 0; y < height; y++)
                    {
                        int row = data.Stride >= 0 ? y * stride : (height - 1 - y) * stride;
                        for (int x = 0; x < width; x++)
                        {
                            int source = row + (x * 3);
                            int index = y * width + x;
                            tensor.Buffer.Span[index] = sourcePixels[source + 2] / 255f;
                            tensor.Buffer.Span[plane + index] = sourcePixels[source + 1] / 255f;
                            tensor.Buffer.Span[(plane * 2) + index] = sourcePixels[source] / 255f;
                        }
                    }
                }
                finally
                {
                    rgb.UnlockBits(data);
                }
                return tensor;
            }
        }

        private static void SaveBitmapAtScale(Bitmap original, float[]? output, int[]? dimensions, int targetScale, int nativeScale, string outputPath)
        {
            int targetWidth = checked(original.Width * targetScale);
            int targetHeight = checked(original.Height * targetScale);

            if (output == null)
            {
                using (var copy = new Bitmap(targetWidth, targetHeight, PixelFormat.Format32bppArgb))
                {
                    using (var graphics = Graphics.FromImage(copy))
                    {
                        graphics.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                        graphics.DrawImageUnscaled(original, 0, 0);
                    }
                    copy.Save(outputPath, ImageFormat.Png);
                }
                return;
            }

            if (dimensions == null || dimensions.Length != 4 || dimensions[0] != 1 || dimensions[1] != 3)
                throw new InvalidDataException("Expected a three-channel NCHW image output.");
            int nativeHeight = dimensions[2];
            int nativeWidth = dimensions[3];

            if (nativeWidth != checked(original.Width * nativeScale) ||
                nativeHeight != checked(original.Height * nativeScale))
                throw new InvalidDataException("Native output does not match the declared model scale.");

            int plane = checked(nativeWidth * nativeHeight);
            if (output.Length != checked(plane * 3))
                throw new InvalidDataException("Image output length is invalid.");

            using (var native = new Bitmap(nativeWidth, nativeHeight, PixelFormat.Format32bppArgb))
            {
                var rectangle = new Rectangle(0, 0, nativeWidth, nativeHeight);
                var data = native.LockBits(rectangle, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                try
                {
                    int stride = Math.Abs(data.Stride);
                    byte[] destination = new byte[stride * nativeHeight];
                    for (int y = 0; y < nativeHeight; y++)
                    {
                        int row = data.Stride >= 0 ? y * stride : (nativeHeight - 1 - y) * stride;
                        for (int x = 0; x < nativeWidth; x++)
                        {
                            int index = y * nativeWidth + x;
                            int pixel = row + x * 4;
                            destination[pixel] = ToByte(output[(plane * 2) + index]);
                            destination[pixel + 1] = ToByte(output[plane + index]);
                            destination[pixel + 2] = ToByte(output[index]);
                            destination[pixel + 3] = 255;
                        }
                    }
                    Marshal.Copy(destination, 0, data.Scan0, destination.Length);
                }
                finally
                {
                    native.UnlockBits(data);
                }

                if (targetScale == nativeScale)
                {
                    native.Save(outputPath, ImageFormat.Png);
                }
                else
                {
                    using (var resized = new Bitmap(targetWidth, targetHeight, PixelFormat.Format32bppArgb))
                    {
                        using (var graphics = Graphics.FromImage(resized))
                        {
                            graphics.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                            graphics.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
                            graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                            graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                            graphics.DrawImage(native, new Rectangle(0, 0, targetWidth, targetHeight),
                                0, 0, nativeWidth, nativeHeight, GraphicsUnit.Pixel);
                        }
                        resized.Save(outputPath, ImageFormat.Png);
                    }
                }
            }
        }

        private static byte ToByte(float value)
        {
            if (float.IsNaN(value)) return 0;
            float clamped = Math.Max(0f, Math.Min(1f, value));
            return (byte)Math.Max(0, Math.Min(255, (int)Math.Round(clamped * 255f)));
        }

        public static SessionOptions CreateSessionOptions()
        {
            var opts = new SessionOptions();
            opts.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;
            opts.ExecutionMode = ExecutionMode.ORT_SEQUENTIAL;
            opts.InterOpNumThreads = 1;
            opts.IntraOpNumThreads = 4;
            return opts;
        }

        public void Dispose()
        {
            _isDisposed = true;
        }
    }
}
