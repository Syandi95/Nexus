using System;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Nexus.AI.Llama.Services
{
    public enum ServerState
    {
        Stopped,
        Starting,
        WaitingForHealth,
        Ready,
        Failed,
        Stopping
    }

    public enum ProcessExitReason
    {
        UnexpectedTermination,
        UserShutdown,
        Crash,
        StartupFailure,
        HealthTimeout,
        KilledStaleProcess
    }

    public class ServerEvent
    {
        public DateTime Timestamp { get; } = DateTime.UtcNow;
        public string ServerInstanceId { get; internal set; } = string.Empty;
        public string EventType { get; }
        public string Message { get; }
        public long ElapsedMs { get; internal set; }
        public int? ThreadId { get; }
        public int? Pid { get; }
        public ServerState? State { get; }

        public ServerEvent(string eventType, string message = "", long elapsedMs = 0, int? threadId = null, int? pid = null, ServerState? state = null)
        {
            EventType = eventType;
            Message = message;
            ElapsedMs = elapsedMs;
            ThreadId = threadId ?? Environment.CurrentManagedThreadId;
            Pid = pid;
            State = state;
        }
    }

    public class LlamaServerManager : ILlamaServerManager, IDisposable
    {
        private string _engineDir = string.Empty;
        private readonly string _engineDirectory;
        private int _port = 8080;
        private Process? _serverProcess;
        private int? _ownedPid;
        private string? _runtimeModelPath;
        private readonly HttpClient _httpClient;
        private readonly SemaphoreSlim _lifecycleLock = new(1, 1);
        private ServerState _state = ServerState.Stopped;
        private string? _cachedBackend;
        private bool _isDisposed;
        private long _startTimestamp;
        private string _serverInstanceId = string.Empty;
        internal const string HealthSignature = "Nexus-Llama";

        public ServerState State => _state;
        public int? OwnedPid => _ownedPid;
        public string? RuntimeModelPath => _runtimeModelPath;
        public int ActivePort => _port;
        public static int ActiveConfiguredPort { get; private set; } = 8080;

        public event Action<ServerEvent>? OnEvent;

        public LlamaServerManager() : this(string.Empty)
        {
        }

        public LlamaServerManager(string engineDirectory)
        {
            _engineDirectory = engineDirectory;
            _httpClient = new HttpClient();
        }

        public void Initialize(string engineDir, int port = 8080)
        {
            _engineDir = engineDir;
            _port = port;
            ActiveConfiguredPort = port;
            Emit("INIT", $"EngineDir={engineDir}, Port={port}");
        }

        public string ResolveEngineDir()
        {
            if (!string.IsNullOrWhiteSpace(_engineDir))
            {
                var targetSubdir = Path.Combine(_engineDir, "b11057");
                if (IsValidEngineDir(targetSubdir))
                    return targetSubdir;

                if (IsValidEngineDir(_engineDir))
                    return _engineDir;
            }

            if (!string.IsNullOrWhiteSpace(_engineDirectory))
            {
                var targetSubdir = Path.Combine(_engineDirectory, "b11057");
                if (IsValidEngineDir(targetSubdir))
                    return targetSubdir;

                if (IsValidEngineDir(_engineDirectory))
                    return _engineDirectory;
            }

            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var candidates = new[]
            {
                Path.Combine(baseDir, @"Engine\LlamaServer\b11057"),
                Path.Combine(baseDir, @"Engine\LlamaServer"),
                Path.Combine(baseDir, @"..\..\..\..\Engine\LlamaServer\b11057"),
                Path.Combine(baseDir, @"..\..\..\..\Engine\LlamaServer")
            };

            foreach (var dir in candidates)
            {
                try
                {
                    var fullPath = Path.GetFullPath(dir);
                    if (IsValidEngineDir(fullPath))
                        return fullPath;
                }
                catch { }
            }

            Emit("ENGINE_NOT_FOUND", $"No engine dir found. Configured: '{_engineDirectory}'");
            return _engineDirectory;
        }

        private static bool IsValidEngineDir(string? dir) =>
            !string.IsNullOrWhiteSpace(dir) &&
            Directory.Exists(dir) &&
            File.Exists(Path.Combine(dir, "llama-server.exe"));

        public virtual string DetectBackend()
        {
            if (_cachedBackend != null)
                return _cachedBackend;

            var sw = Stopwatch.StartNew();
            string result = "cpu";
            if (OperatingSystem.IsWindows())
            {
                try
                {
                    using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_VideoController");
                    foreach (var obj in searcher.Get())
                    {
                        var name = obj["Name"]?.ToString()?.ToLowerInvariant();
                        if (name != null && name.Contains("nvidia"))
                        {
                            result = "cuda";
                            break;
                        }
                    }
                }
                catch { }
            }
            _cachedBackend = result;
            Emit("BACKEND_DETECTED", $"{result} ({sw.ElapsedMilliseconds}ms)");
            return result;
        }

        public string GetTuningArguments()
        {
            return "-t 4 -tb 8 -b 128 -ub 64 -fa on -np 1 --reasoning auto --reasoning-format deepseek";
        }

        public string BuildStartArguments(string modelPath, string chatTemplate, int contextSize)
        {
            var tuning = GetTuningArguments();
            var templateArg = string.IsNullOrWhiteSpace(chatTemplate) || chatTemplate.Equals("auto", StringComparison.OrdinalIgnoreCase)
                ? "--jinja"
                : $"--chat-template {chatTemplate}";

            var backend = DetectBackend();
            var nglValue = backend == "cpu" ? 0 : 99;
            var effectiveContext = Math.Min(contextSize, 32768);

            return $"-m \"{modelPath}\" --port {_port} -c {effectiveContext} -ngl {nglValue} {tuning} {templateArg}";
        }

        public async Task<bool> IsServerHealthyAsync()
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                var response = await _httpClient.GetAsync($"http://127.0.0.1:{_port}/health", cts.Token);
                if (!response.IsSuccessStatusCode)
                {
                    return false;
                }

                await using var content = await response.Content.ReadAsStreamAsync(cts.Token);
                using var document = await JsonDocument.ParseAsync(
                    content,
                    cancellationToken: cts.Token);
                return document.RootElement.TryGetProperty("status", out var status) &&
                       status.ValueKind == JsonValueKind.String &&
                       string.Equals(
                           status.GetString(),
                           "ok",
                           StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        public async Task<bool> IsServerReadyForModelAsync(
            string modelPath,
            CancellationToken cancellationToken = default)
        {
            if (!await IsServerHealthyAsync())
            {
                return false;
            }

            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(2));
                var response = await _httpClient.GetAsync(
                    $"http://127.0.0.1:{_port}/props",
                    timeout.Token);
                if (!response.IsSuccessStatusCode)
                {
                    return false;
                }

                await using var content = await response.Content.ReadAsStreamAsync(
                    timeout.Token);
                using var document = await JsonDocument.ParseAsync(
                    content,
                    cancellationToken: timeout.Token);
                if (!document.RootElement.TryGetProperty(
                        "model_path",
                        out var modelPathElement) ||
                    modelPathElement.ValueKind != JsonValueKind.String)
                {
                    return false;
                }

                return PathsEqual(modelPathElement.GetString(), modelPath);
            }
            catch
            {
                return false;
            }
        }

        public async Task StartServerAsync(string modelPath, string chatTemplate, int contextSize)
        {
            await StartServerAsync(modelPath, chatTemplate, contextSize, CancellationToken.None);
        }

        public async Task StartServerAsync(string modelPath, string chatTemplate, int contextSize, CancellationToken cancellationToken)
        {
            var tid = Environment.CurrentManagedThreadId;
            _serverInstanceId = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
            Emit("START_REQUESTED", $"Thread={tid}, Model={modelPath}, Instance={_serverInstanceId}", state: ServerState.Starting);

            if (!await _lifecycleLock.WaitAsync(0, cancellationToken))
            {
                throw new InvalidOperationException(
                    "A llama-server lifecycle operation is already in progress.");
            }
            try
            {
                if (_state == ServerState.Ready && IsProcessAlive())
                {
                    if (PathsEqual(_runtimeModelPath, modelPath) &&
                        await IsServerReadyForModelAsync(modelPath, cancellationToken))
                    {
                        Emit("START_SKIP_SAME_MODEL", $"Server already serves {modelPath}, PID={_ownedPid}", state: _state);
                        return;
                    }

                    throw new InvalidOperationException(
                        "A different or unhealthy model runtime is already active.");
                }

                if (_state == ServerState.Starting || _state == ServerState.WaitingForHealth)
                {
                    throw new InvalidOperationException(
                        "A llama-server start operation is already in progress.");
                }

                if (_state == ServerState.Stopping)
                {
                    throw new InvalidOperationException("Server is stopping. Wait for stop to complete before starting.");
                }

                _state = ServerState.Starting;
                _startTimestamp = Stopwatch.GetTimestamp();

                CleanupStaleProcess();

                if (IsPortInUseByForeignProcess())
                {
                    Emit("PORT_IN_USE_FOREIGN", $"Port {_port} in use by non-Nexus process", state: _state);
                    throw new InvalidOperationException($"Port {_port} is already in use by another application.");
                }

                var resolvedDir = ResolveEngineDir();
                var exePath = Path.Combine(resolvedDir, "llama-server.exe");
                if (!File.Exists(exePath))
                {
                    Emit("EXE_NOT_FOUND", exePath, state: ServerState.Failed);
                    _state = ServerState.Failed;
                    throw new FileNotFoundException($"llama-server.exe not found at {exePath}");
                }
                if (!File.Exists(modelPath))
                {
                    _state = ServerState.Failed;
                    throw new FileNotFoundException(
                        $"Model GGUF not found at {modelPath}");
                }

                var arguments = BuildStartArguments(modelPath, chatTemplate, contextSize);

                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = exePath,
                        Arguments = arguments,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = false,
                        RedirectStandardError = false
                    },
                    EnableRaisingEvents = true
                };

                process.Exited += (sender, args) =>
                {
                    if (_ownedPid.HasValue && sender is Process p && p.Id == _ownedPid.Value)
                    {
                        var exitCode = 0;
                        try { exitCode = p.ExitCode; } catch { }

                        var currentState = _state;
                        ProcessExitReason reason;
                        bool expected;

                        if (currentState == ServerState.Stopping)
                        {
                            reason = ProcessExitReason.UserShutdown;
                            expected = true;
                        }
                        else if (currentState == ServerState.Starting || currentState == ServerState.WaitingForHealth)
                        {
                            reason = ProcessExitReason.StartupFailure;
                            expected = false;
                            _state = ServerState.Failed;
                        }
                        else if (currentState == ServerState.Ready)
                        {
                            reason = ProcessExitReason.Crash;
                            expected = false;
                            _state = ServerState.Failed;
                        }
                        else
                        {
                            reason = ProcessExitReason.UnexpectedTermination;
                            expected = false;
                            _state = ServerState.Failed;
                        }

                        Emit("PROCESS_EXITED",
                            $"PID={_ownedPid}, ExitCode={exitCode}, Reason={reason}, Expected={expected}",
                            pid: _ownedPid, state: _state);
                    }
                };

                process.Start();
                _serverProcess = process;
                _ownedPid = process.Id;

                Emit("PROCESS_STARTED", $"PID={_ownedPid}, Exe={exePath}", pid: _ownedPid, state: _state);

                _state = ServerState.WaitingForHealth;

                var startupTimeout = TimeSpan.FromSeconds(60);
                var startupTimer = Stopwatch.StartNew();
                int healthAttempt = 0;
                bool healthy = false;

                while (startupTimer.Elapsed < startupTimeout)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (_serverProcess.HasExited)
                    {
                        Emit("PROCESS_DIED_DURING_START",
                            $"PID={_ownedPid}, ExitCode={_serverProcess.ExitCode}, Reason={ProcessExitReason.StartupFailure}, Expected=false",
                            pid: _ownedPid, state: ServerState.Failed);
                        _state = ServerState.Failed;
                        throw new InvalidOperationException(
                            $"Llama server exited before becoming healthy. Exit code: {_serverProcess.ExitCode}");
                    }

                    healthAttempt++;
                    var healthSw = Stopwatch.StartNew();
                    healthy = await IsServerReadyForModelAsync(
                        modelPath,
                        cancellationToken);
                    healthSw.Stop();

                    if (healthy)
                    {
                        Emit("HEALTH_CHECK_PASS", $"Attempt={healthAttempt}, Latency={healthSw.ElapsedMilliseconds}ms",
                            pid: _ownedPid, state: ServerState.Ready);
                        _state = ServerState.Ready;
                        _runtimeModelPath = Path.GetFullPath(modelPath);
                        var totalMs = (long)((double)(Stopwatch.GetTimestamp() - _startTimestamp) / Stopwatch.Frequency * 1000);
                        Emit("SERVER_READY", $"Total startup time: {totalMs}ms", pid: _ownedPid, state: _state);
                        return;
                    }

                    var remaining = startupTimeout - startupTimer.Elapsed;
                    if (remaining > TimeSpan.Zero)
                    {
                        var delay = remaining < TimeSpan.FromMilliseconds(500) ? remaining : TimeSpan.FromMilliseconds(500);
                        await Task.Delay(delay, cancellationToken);
                    }
                }

                Emit("HEALTH_CHECK_TIMEOUT",
                    $"Attempts={healthAttempt}, Duration={startupTimer.ElapsedMilliseconds}ms, Reason={ProcessExitReason.HealthTimeout}",
                    state: ServerState.Failed);
                _state = ServerState.Failed;
                throw new TimeoutException("Llama server failed to start within 60 seconds.");
            }
            catch (OperationCanceledException)
            {
                Emit("START_CANCELLED", $"Thread={tid}", state: ServerState.Failed);
                _state = ServerState.Failed;
                await CleanupFailedProcessAsync();
                throw;
            }
            catch
            {
                _state = ServerState.Failed;
                await CleanupFailedProcessAsync();
                throw;
            }
            finally
            {
                _lifecycleLock.Release();
            }
        }

        public async Task StopServerAsync()
        {
            Emit("STOP_REQUESTED", state: ServerState.Stopping);

            await _lifecycleLock.WaitAsync();
            try
            {
                if (_state == ServerState.Stopped)
                {
                    Emit("STOP_SKIP_ALREADY_STOPPED");
                    return;
                }

                _state = ServerState.Stopping;

                if (_serverProcess != null)
                {
                    try
                    {
                        if (!_serverProcess.HasExited)
                        {
                            Emit("KILLING_PROCESS", $"PID={_ownedPid}", pid: _ownedPid);
                            _serverProcess.Kill(entireProcessTree: true);
                            using var stopTimeout = new CancellationTokenSource(
                                TimeSpan.FromSeconds(5));
                            try
                            {
                                await _serverProcess.WaitForExitAsync(
                                    stopTimeout.Token);
                            }
                            catch (OperationCanceledException)
                            {
                                _state = ServerState.Failed;
                                Emit("KILL_TIMEOUT", $"PID={_ownedPid} did not exit within 5s", pid: _ownedPid, state: _state);
                                throw new TimeoutException(
                                    "llama-server did not exit within 5 seconds.");
                            }
                        }
                    }
                    finally
                    {
                        if (_serverProcess.HasExited)
                        {
                            _serverProcess.Dispose();
                            _serverProcess = null;
                            _ownedPid = null;
                            _runtimeModelPath = null;
                        }
                    }
                }

                if (_serverProcess != null)
                {
                    throw new InvalidOperationException(
                        "llama-server is still running.");
                }
                _state = ServerState.Stopped;
                Emit("STOP_COMPLETE", state: _state);
            }
            finally
            {
                _lifecycleLock.Release();
            }
        }

        private void CleanupStaleProcess()
        {
            if (_serverProcess != null)
            {
                try
                {
                    if (!_serverProcess.HasExited)
                    {
                        Emit("CLEANUP_STALE",
                            $"Killing stale process PID={_ownedPid}, Reason={ProcessExitReason.KilledStaleProcess}",
                            pid: _ownedPid);
                        _serverProcess.Kill();
                        _serverProcess.WaitForExit(3000);
                    }
                }
                catch { }
                finally
                {
                    _serverProcess.Dispose();
                    _serverProcess = null;
                    _ownedPid = null;
                    _runtimeModelPath = null;
                }
            }
        }

        private async Task CleanupFailedProcessAsync()
        {
            if (_serverProcess == null)
            {
                _ownedPid = null;
                _runtimeModelPath = null;
                return;
            }

            try
            {
                if (!_serverProcess.HasExited)
                {
                    _serverProcess.Kill(entireProcessTree: true);
                    using var timeout = new CancellationTokenSource(
                        TimeSpan.FromSeconds(5));
                    await _serverProcess.WaitForExitAsync(timeout.Token);
                }
            }
            catch (Exception error)
            {
                Emit("STARTUP_CLEANUP_FAILED", error.Message, pid: _ownedPid, state: ServerState.Failed);
            }
            finally
            {
                if (_serverProcess.HasExited)
                {
                    _serverProcess.Dispose();
                    _serverProcess = null;
                    _ownedPid = null;
                    _runtimeModelPath = null;
                }
            }
        }

        private bool IsProcessAlive()
        {
            if (_serverProcess == null) return false;
            try { return !_serverProcess.HasExited; }
            catch { return false; }
        }

        private bool IsPortInUseByForeignProcess()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    $"SELECT * FROM MSFT_NetTCPConnection WHERE LocalPort={_port}");
                foreach (var obj in searcher.Get())
                {
                    var owningPid = (uint)obj["OwningProcess"];
                    if (owningPid != (_ownedPid ?? 0))
                    {
                        try
                        {
                            using var proc = Process.GetProcessById((int)owningPid);
                            var procName = proc.ProcessName.ToLowerInvariant();
                            _ = procName;
                        }
                        catch { }
                        return true;
                    }
                }
            }
            catch { }
            return false;
        }

        private static bool PathsEqual(string? left, string? right)
        {
            if (string.IsNullOrWhiteSpace(left) ||
                string.IsNullOrWhiteSpace(right))
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

        private void Emit(string eventType, string message = "", long elapsedMs = 0, int? pid = null, ServerState? state = null)
        {
            var elapsed = elapsedMs > 0 ? elapsedMs
                : _startTimestamp > 0 ? (long)((Stopwatch.GetTimestamp() - _startTimestamp) * 1000.0 / Stopwatch.Frequency)
                : 0L;
            var evt = new ServerEvent(eventType, message, elapsed, pid: pid, state: state ?? _state)
            {
                ServerInstanceId = _serverInstanceId
            };
            OnEvent?.Invoke(evt);
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;
            _lifecycleLock.Dispose();
            _httpClient.Dispose();
            _serverProcess?.Dispose();
        }
    }
}
