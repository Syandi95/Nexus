using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Nexus.Contracts;

namespace Nexus.Engine.ImageSubsystem.Services;

/// <summary>
/// Engine ONNX generik yang menerima dan mengembalikan tensor sebagai array CLR.
/// </summary>
public sealed class GenericImageEngine : IGenericImageEngine
{
    private readonly ConcurrentDictionary<string, SessionEntry> _sessions =
        new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _pathToSessionKey =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly TimeSpan _idleTimeout;
    private readonly int _maxConcurrency;
    private long _peakPrivateBytes;
    private int _disposeState;

    public string EngineName => "ONNX Runtime";

    public bool IsLoaded { get; private set; }

    public int ActiveSessionCount => _sessions.Count;

    public bool HasCachedSession(string modelPath)
    {
        if (string.IsNullOrWhiteSpace(modelPath)) return false;
        var fullPath = Path.GetFullPath(modelPath);
        return _pathToSessionKey.TryGetValue(fullPath, out var key) &&
               _sessions.TryGetValue(key, out var entry) &&
               !entry.IsDisposed;
    }

    public GenericImageEngine(TimeSpan? idleTimeout = null, int? maxConcurrency = null)
    {
        _idleTimeout = idleTimeout ?? TimeSpan.FromMinutes(5);
        _maxConcurrency = maxConcurrency ?? 2;
    }

    public Task LoadEngineAsync()
    {
        ThrowIfDisposed();
        IsLoaded = true;
        return Task.CompletedTask;
    }

    public async Task<string> LoadModelAsync(
        ResolvedModelDescriptor descriptor,
        EngineSessionOptions? options = null)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(descriptor);
        if (descriptor.ModelType != ModelType.Onnx)
        {
            throw new NotSupportedException(
                $"Image engine cannot load model type '{descriptor.ModelType}'.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.ModelPath);
        var resolvedPath = Path.GetFullPath(descriptor.ModelPath);
        if (!File.Exists(resolvedPath))
        {
            throw new FileNotFoundException(
                $"ONNX model was not found at '{resolvedPath}'.",
                resolvedPath);
        }

        if (!IsLoaded)
        {
            await LoadEngineAsync().ConfigureAwait(false);
        }

        // Cek apakah sesi hangat untuk model yang sama sudah ada di cache dan masih aktif
        if (_pathToSessionKey.TryGetValue(resolvedPath, out var existingKey) &&
            _sessions.TryGetValue(existingKey, out var existingEntry) &&
            !existingEntry.IsDisposed)
        {
            existingEntry.IncrementRef();
            return existingKey;
        }

        var session = CreateInferenceSession(resolvedPath, options ?? new EngineSessionOptions(), _maxConcurrency);
        var sessionKey = Guid.NewGuid().ToString("N");
        var entry = new SessionEntry(session, sessionKey, resolvedPath, _maxConcurrency);

        if (Volatile.Read(ref _disposeState) != 0 ||
            !_sessions.TryAdd(sessionKey, entry))
        {
            session.Dispose();
            entry.Dispose();
            throw new InvalidOperationException("Unable to register the ONNX session.");
        }

        _pathToSessionKey[resolvedPath] = sessionKey;
        return sessionKey;
    }

    public async Task<IReadOnlyDictionary<string, object>> ExecuteInferenceAsync(
        string sessionKey,
        IReadOnlyDictionary<string, object> inputs)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionKey);
        ArgumentNullException.ThrowIfNull(inputs);

        if (!_sessions.TryGetValue(sessionKey, out var entry))
        {
            throw new KeyNotFoundException($"ONNX session '{sessionKey}' was not found.");
        }

        if (inputs.Count == 0)
        {
            throw new ArgumentException("At least one tensor input is required.", nameof(inputs));
        }

        await entry.Gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (entry.IsDisposed)
            {
                throw new ObjectDisposedException(
                    nameof(GenericImageEngine),
                    $"ONNX session '{sessionKey}' has been unloaded.");
            }

            var namedInputs = inputs
                .Select(input => CreateNamedInput(input.Key, input.Value))
                .ToArray();

            using var results = entry.Session.Run(namedInputs);
            return results.ToDictionary(
                result => result.Name,
                result => ConvertOutput(result.Value),
                StringComparer.Ordinal);
        }
        finally
        {
            entry.Gate.Release();
        }
    }

    public async Task UnloadModelAsync(string sessionKey)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionKey);

        if (_sessions.TryGetValue(sessionKey, out var entry))
        {
            var immediate = entry.DecrementRefAndCheckIdle(_idleTimeout, async expiredEntry =>
            {
                if (expiredEntry.ReferenceCount > 0)
                {
                    return;
                }

                if (_sessions.TryRemove(expiredEntry.SessionKey, out var removedEntry))
                {
                    _pathToSessionKey.TryRemove(removedEntry.ModelPath, out _);
                    await DisposeEntryAsync(removedEntry).ConfigureAwait(false);
                }
            });

            if (immediate)
            {
                if (_sessions.TryRemove(sessionKey, out var removedEntry))
                {
                    _pathToSessionKey.TryRemove(removedEntry.ModelPath, out _);
                    await DisposeEntryAsync(removedEntry).ConfigureAwait(false);
                }
            }
        }
    }

    public async Task UnloadAllModelsAsync()
    {
        ThrowIfDisposed();
        await DisposeSessionsAsync().ConfigureAwait(false);
    }

    public EngineMemoryMetrics GetMemoryMetrics()
    {
        ThrowIfDisposed();
        using var process = Process.GetCurrentProcess();
        var privateBytes = process.PrivateMemorySize64;
        UpdatePeakPrivateBytes(privateBytes);
        return new EngineMemoryMetrics
        {
            WorkingSetMB = BytesToMegabytes(process.WorkingSet64),
            PrivateBytesMB = BytesToMegabytes(privateBytes),
            PeakPrivateBytesMB = BytesToMegabytes(Volatile.Read(ref _peakPrivateBytes)),
            CommitSizeMB = BytesToMegabytes(privateBytes),
            ActiveSessionsCount = _sessions.Count
        };
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) == 0)
        {
            await DisposeSessionsAsync().ConfigureAwait(false);
            IsLoaded = false;
        }
    }

    private static SessionOptions CreateSessionOptions(EngineSessionOptions options, int maxConcurrency)
    {
        if (options.ExecutionProvider != ExecutionProvider.Cpu)
        {
            throw new NotSupportedException(
                $"Execution provider '{options.ExecutionProvider}' is not available in the CPU ONNX Runtime package.");
        }

        int intraThreads = options.IntraOpNumThreads > 0
            ? options.IntraOpNumThreads
            : Math.Max(2, Environment.ProcessorCount / maxConcurrency);

        return new SessionOptions
        {
            LogSeverityLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_ERROR,
            InterOpNumThreads = options.InterOpNumThreads,
            IntraOpNumThreads = intraThreads,
            GraphOptimizationLevel = options.GraphOptimization switch
            {
                Nexus.Contracts.GraphOptimizationLevel.DisableAll =>
                    Microsoft.ML.OnnxRuntime.GraphOptimizationLevel.ORT_DISABLE_ALL,
                Nexus.Contracts.GraphOptimizationLevel.EnableBasic =>
                    Microsoft.ML.OnnxRuntime.GraphOptimizationLevel.ORT_ENABLE_BASIC,
                Nexus.Contracts.GraphOptimizationLevel.EnableExtended =>
                    Microsoft.ML.OnnxRuntime.GraphOptimizationLevel.ORT_ENABLE_EXTENDED,
                _ => Microsoft.ML.OnnxRuntime.GraphOptimizationLevel.ORT_ENABLE_ALL
            }
        };
    }

    private static InferenceSession CreateInferenceSession(string resolvedPath, EngineSessionOptions options, int maxConcurrency)
    {
        // Model terkuantisasi (seperti MVANet q4f16) memiliki kendala fusi internal saat ORT_ENABLE_ALL.
        // Langsung gunakan EnableExtended sejak awal agar ONNX Runtime tidak memicu log error C++ ke terminal.
        var initialOptions = options;
        if (options.GraphOptimization == Nexus.Contracts.GraphOptimizationLevel.EnableAll &&
            (resolvedPath.Contains("q4", StringComparison.OrdinalIgnoreCase) ||
             resolvedPath.Contains("mvanet", StringComparison.OrdinalIgnoreCase)))
        {
            initialOptions = new EngineSessionOptions
            {
                InterOpNumThreads = options.InterOpNumThreads,
                IntraOpNumThreads = options.IntraOpNumThreads,
                ExecutionProvider = options.ExecutionProvider,
                GraphOptimization = Nexus.Contracts.GraphOptimizationLevel.EnableExtended
            };
        }

        try
        {
            using var sessionOptions = CreateSessionOptions(initialOptions, maxConcurrency);
            return new InferenceSession(resolvedPath, sessionOptions);
        }
        catch (OnnxRuntimeException) when (initialOptions.GraphOptimization == Nexus.Contracts.GraphOptimizationLevel.EnableAll)
        {
            // Graceful Fallback 1: Jika level EnableAll gagal karena kendala fusi grafik pada model terkuantisasi lainnya, coba EnableExtended
            try
            {
                var fallbackExtended = new EngineSessionOptions
                {
                    InterOpNumThreads = options.InterOpNumThreads,
                    IntraOpNumThreads = options.IntraOpNumThreads,
                    ExecutionProvider = options.ExecutionProvider,
                    GraphOptimization = Nexus.Contracts.GraphOptimizationLevel.EnableExtended
                };
                using var sessionOptions = CreateSessionOptions(fallbackExtended, maxConcurrency);
                return new InferenceSession(resolvedPath, sessionOptions);
            }
            catch (OnnxRuntimeException)
            {
                // Graceful Fallback 2: Coba EnableBasic jika EnableExtended juga bermasalah
                var fallbackBasic = new EngineSessionOptions
                {
                    InterOpNumThreads = options.InterOpNumThreads,
                    IntraOpNumThreads = options.IntraOpNumThreads,
                    ExecutionProvider = options.ExecutionProvider,
                    GraphOptimization = Nexus.Contracts.GraphOptimizationLevel.EnableBasic
                };
                using var sessionOptions = CreateSessionOptions(fallbackBasic, maxConcurrency);
                return new InferenceSession(resolvedPath, sessionOptions);
            }
        }
    }

    private static NamedOnnxValue CreateNamedInput(string name, object value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(value);

        if (value is not Array array || array.Rank == 0)
        {
            throw new ArgumentException(
                $"Input '{name}' must be a CLR array with one or more dimensions.",
                nameof(value));
        }

        var dimensions = Enumerable.Range(0, array.Rank)
            .Select(array.GetLength)
            .ToArray();

        var elementType = array.GetType().GetElementType();
        if (elementType == typeof(float))
        {
            return CreateNamedInput(name, array.Cast<float>().ToArray(), dimensions);
        }

        if (elementType == typeof(double))
        {
            return CreateNamedInput(name, array.Cast<double>().ToArray(), dimensions);
        }

        if (elementType == typeof(int))
        {
            return CreateNamedInput(name, array.Cast<int>().ToArray(), dimensions);
        }

        if (elementType == typeof(long))
        {
            return CreateNamedInput(name, array.Cast<long>().ToArray(), dimensions);
        }

        if (elementType == typeof(byte))
        {
            return CreateNamedInput(name, array.Cast<byte>().ToArray(), dimensions);
        }

        throw new NotSupportedException(
            $"CLR array element type '{elementType}' is not supported.");
    }

    private static NamedOnnxValue CreateNamedInput<T>(
        string name,
        T[] values,
        int[] dimensions) =>
        NamedOnnxValue.CreateFromTensor(name, new DenseTensor<T>(values, dimensions));

    private static object ConvertOutput(object value) => value switch
    {
        Tensor<float> tensor => ToClrArray(tensor),
        Tensor<double> tensor => ToClrArray(tensor),
        Tensor<int> tensor => ToClrArray(tensor),
        Tensor<long> tensor => ToClrArray(tensor),
        Tensor<byte> tensor => ToClrArray(tensor),
        _ => throw new NotSupportedException(
            $"ONNX output type '{value.GetType()}' is not supported.")
    };

    private static Array ToClrArray<T>(Tensor<T> tensor)
    {
        var dimensions = tensor.Dimensions.ToArray();
        var output = Array.CreateInstance(typeof(T), dimensions);
        var indices = new int[dimensions.Length];

        foreach (var value in tensor)
        {
            output.SetValue(value, indices);
            IncrementIndices(indices, dimensions);
        }

        return output;
    }

    private static void IncrementIndices(int[] indices, int[] dimensions)
    {
        for (var position = indices.Length - 1; position >= 0; position--)
        {
            indices[position]++;
            if (indices[position] < dimensions[position])
            {
                return;
            }

            indices[position] = 0;
        }
    }

    private async Task DisposeSessionsAsync()
    {
        _pathToSessionKey.Clear();
        foreach (var sessionKey in _sessions.Keys)
        {
            if (_sessions.TryRemove(sessionKey, out var entry))
            {
                entry.CancelIdle();
                await DisposeEntryAsync(entry).ConfigureAwait(false);
            }
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref _disposeState) != 0,
            this);
    }

    private static async Task DisposeEntryAsync(SessionEntry entry)
    {
        for (var i = 0; i < entry.MaxConcurrency; i++)
        {
            await entry.Gate.WaitAsync().ConfigureAwait(false);
        }

        try
        {
            if (!entry.IsDisposed)
            {
                entry.Session.Dispose();
                entry.IsDisposed = true;
            }
        }
        finally
        {
            for (var i = 0; i < entry.MaxConcurrency; i++)
            {
                entry.Gate.Release();
            }
            entry.Dispose();
        }
    }

    private void UpdatePeakPrivateBytes(long privateBytes)
    {
        var currentPeak = Volatile.Read(ref _peakPrivateBytes);
        while (privateBytes > currentPeak)
        {
            var observed = Interlocked.CompareExchange(
                ref _peakPrivateBytes,
                privateBytes,
                currentPeak);
            if (observed == currentPeak)
            {
                return;
            }

            currentPeak = observed;
        }
    }

    private static double BytesToMegabytes(long bytes) =>
        bytes / (1024d * 1024d);

    private sealed class SessionEntry : IDisposable
    {
        private int _referenceCount;
        private CancellationTokenSource? _idleCts;
        private readonly object _syncRoot = new();

        public SessionEntry(InferenceSession session, string sessionKey, string modelPath, int maxConcurrency)
        {
            Session = session;
            SessionKey = sessionKey;
            ModelPath = modelPath;
            MaxConcurrency = maxConcurrency;
            Gate = new SemaphoreSlim(maxConcurrency, maxConcurrency);
            _referenceCount = 1;
        }

        public InferenceSession Session { get; }
        public string SessionKey { get; }
        public string ModelPath { get; }
        public int MaxConcurrency { get; }
        public SemaphoreSlim Gate { get; }
        public bool IsDisposed { get; set; }
        public object SyncRoot => _syncRoot;
        public int ReferenceCount
        {
            get
            {
                lock (_syncRoot)
                {
                    return _referenceCount;
                }
            }
        }

        public void IncrementRef()
        {
            lock (_syncRoot)
            {
                _referenceCount++;
                _idleCts?.Cancel();
                _idleCts?.Dispose();
                _idleCts = null;
            }
        }

        public bool DecrementRefAndCheckIdle(TimeSpan idleTimeout, Func<SessionEntry, Task> onIdleExpired)
        {
            lock (_syncRoot)
            {
                _referenceCount--;
                if (_referenceCount <= 0)
                {
                    _referenceCount = 0;
                    if (idleTimeout == TimeSpan.Zero)
                    {
                        return true; // Immediate unload
                    }

                    _idleCts?.Cancel();
                    _idleCts?.Dispose();
                    var cts = new CancellationTokenSource();
                    _idleCts = cts;

                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await Task.Delay(idleTimeout, cts.Token).ConfigureAwait(false);
                            await onIdleExpired(this).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException)
                        {
                            // Sesi digunakan kembali sebelum timeout berakhir
                        }
                    });
                }
                return false;
            }
        }

        public void CancelIdle()
        {
            lock (_syncRoot)
            {
                _idleCts?.Cancel();
                _idleCts?.Dispose();
                _idleCts = null;
            }
        }

        public void Dispose()
        {
            CancelIdle();
            Gate.Dispose();
        }
    }
}
