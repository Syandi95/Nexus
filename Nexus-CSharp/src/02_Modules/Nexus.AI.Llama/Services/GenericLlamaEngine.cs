using System.Diagnostics;
using Nexus.Contracts;

namespace Nexus.AI.Llama.Services;

/// <summary>
/// Adapter engine LLM generik yang mempertahankan lazy process spawn.
/// </summary>
public sealed class GenericLlamaEngine : IGenericLlamaEngine
{
    private readonly ILlamaServerManager _serverManager;
    private long _peakPrivateBytes;
    private int _disposeState;

    public GenericLlamaEngine(
        ILlamaServerManager serverManager,
        int port = 8080)
    {
        _serverManager = serverManager
            ?? throw new ArgumentNullException(nameof(serverManager));
        if (port is < 1 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(port));
        }

        Port = port;
    }

    public string EngineName => "llama.cpp";

    public bool IsLoaded { get; private set; }

    public string? ActiveModelPath => _serverManager.RuntimeModelPath;

    public int Port { get; }

    public Task LoadEngineAsync()
    {
        ThrowIfDisposed();

        var engineDirectory = _serverManager.ResolveEngineDir();
        var executablePath = Path.Combine(engineDirectory, "llama-server.exe");
        if (!File.Exists(executablePath))
        {
            throw new FileNotFoundException(
                $"llama-server.exe was not found at '{executablePath}'.",
                executablePath);
        }

        _serverManager.Initialize(engineDirectory, Port);
        IsLoaded = true;
        return Task.CompletedTask;
    }

    public async Task<bool> StartServerAsync(
        string modelPath,
        int contextSize = 8192)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(modelPath);
        if (contextSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(contextSize));
        }

        if (!IsLoaded)
        {
            await LoadEngineAsync().ConfigureAwait(false);
        }

        await _serverManager
            .StartServerAsync(modelPath, "auto", contextSize)
            .ConfigureAwait(false);
        return await _serverManager
            .IsServerReadyForModelAsync(modelPath)
            .ConfigureAwait(false);
    }

    public Task StopServerAsync()
    {
        ThrowIfDisposed();
        return _serverManager.StopServerAsync();
    }

    public async Task UnloadModelAsync(string sessionKey)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionKey);

        if (PathsEqual(sessionKey, ActiveModelPath))
        {
            await _serverManager.StopServerAsync().ConfigureAwait(false);
        }
    }

    public Task UnloadAllModelsAsync()
    {
        ThrowIfDisposed();
        return _serverManager.StopServerAsync();
    }

    /// <summary>
    /// Menghentikan runtime LLM dan melepaskan proses native yang dimiliki engine.
    /// </summary>
    public async Task UnloadEngineAsync()
    {
        ThrowIfDisposed();
        await _serverManager.StopServerAsync().ConfigureAwait(false);
        IsLoaded = false;
    }

    public EngineMemoryMetrics GetMemoryMetrics()
    {
        ThrowIfDisposed();

        var processId = _serverManager.OwnedPid;
        if (!processId.HasValue)
        {
            return new EngineMemoryMetrics();
        }

        try
        {
            using var process = Process.GetProcessById(processId.Value);
            var privateBytes = process.PrivateMemorySize64;
            UpdatePeakPrivateBytes(privateBytes);
            return new EngineMemoryMetrics
            {
                WorkingSetMB = BytesToMegabytes(process.WorkingSet64),
                PrivateBytesMB = BytesToMegabytes(privateBytes),
                PeakPrivateBytesMB = BytesToMegabytes(
                    Volatile.Read(ref _peakPrivateBytes)),
                CommitSizeMB = BytesToMegabytes(privateBytes),
                ActiveSessionsCount = string.IsNullOrWhiteSpace(ActiveModelPath) ? 0 : 1
            };
        }
        catch (ArgumentException)
        {
            return new EngineMemoryMetrics();
        }
        catch (InvalidOperationException)
        {
            return new EngineMemoryMetrics();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) == 0)
        {
            await _serverManager.StopServerAsync().ConfigureAwait(false);
            IsLoaded = false;
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref _disposeState) != 0,
            this);
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

    private static bool PathsEqual(string left, string? right)
    {
        if (string.IsNullOrWhiteSpace(right))
        {
            return false;
        }

        try
        {
            return string.Equals(
                Path.GetFullPath(left),
                Path.GetFullPath(right),
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static double BytesToMegabytes(long bytes) =>
        bytes / (1024d * 1024d);
}
