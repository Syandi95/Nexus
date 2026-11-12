using System;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Nexus.AI.Llama.Services;
using Xunit;

namespace Nexus.AI.Llama.Tests
{
    public class LlamaServerManagerTests
    {
        [Fact]
        public void Interface_ExposesResolvedEngineAndRuntimeModelIdentity()
        {
            Assert.NotNull(typeof(ILlamaServerManager).GetMethod("ResolveEngineDir"));
            Assert.NotNull(typeof(ILlamaServerManager).GetProperty("RuntimeModelPath"));
            Assert.NotNull(typeof(ILlamaServerManager).GetMethod("IsServerReadyForModelAsync"));
        }

        [Fact]
        public async Task Health_RequiresHealthyStatusInResponseBody()
        {
            var port = GetFreePort();
            using var listener = StartJsonServer(
                port,
                _ => """{"status":"loading"}""",
                expectedRequests: 1);
            var manager = new LlamaServerManager();
            manager.Initialize(Path.GetTempPath(), port);

            var healthy = await manager.IsServerHealthyAsync();

            Assert.False(healthy);
        }

        [Fact]
        public async Task RuntimeVerification_RejectsPropsModelPathMismatch()
        {
            var port = GetFreePort();
            using var listener = StartJsonServer(
                port,
                path => path.Equals("/health", StringComparison.OrdinalIgnoreCase)
                    ? """{"status":"ok"}"""
                    : """{"model_path":"C:\\models\\other.gguf"}""",
                expectedRequests: 2);
            var manager = new LlamaServerManager();
            manager.Initialize(Path.GetTempPath(), port);
            var method = typeof(LlamaServerManager).GetMethod(
                "IsServerReadyForModelAsync",
                BindingFlags.Instance | BindingFlags.Public);
            Assert.NotNull(method);

            var verification = Assert.IsAssignableFrom<Task<bool>>(
                method!.Invoke(
                    manager,
                    new object[] { @"C:\models\target.gguf", CancellationToken.None }));

            Assert.False(await verification);
        }

        [Fact]
        public void BuildStartArguments_ShouldFormatCorrectly()
        {
            // Arrange
            var manager = new TestableLlamaServerManager();
            manager.Initialize("C:\\llama", 8080);
            
            // Act
            var args = manager.BuildStartArguments("model.gguf", "chatml", 2048);
            
            // Assert
            Assert.Contains("-m \"model.gguf\"", args);
            Assert.Contains("--port 8080", args);
            Assert.Contains("-c 2048", args);
            Assert.Contains("--chat-template chatml", args);
            Assert.Contains("-t 4 -tb 8 -b 128 -ub 64 -fa on -np 1 --reasoning auto --reasoning-format deepseek", args);
            Assert.DoesNotContain("-tb 4", args);
            Assert.DoesNotContain("--reasoning-budget", args);
        }

        [Fact]
        public void DetectBackend_ReturnsValidString()
        {
            // Arrange
            var manager = new LlamaServerManager();
            
            // Act
            var backend = manager.DetectBackend();
            
            // Assert
            Assert.True(backend == "cpu" || backend == "cuda");
        }

        [Fact]
        public void ResolveEngineDir_ReturnsEngineDirSetViaInitialize()
        {
            var engineDir = CreateTempEngineDir();
            var manager = new LlamaServerManager();
            manager.Initialize(engineDir, 8080);

            var resolved = manager.ResolveEngineDir();

            Assert.Equal(engineDir, resolved);
            Directory.Delete(engineDir, recursive: true);
        }

        [Fact]
        public void ResolveEngineDir_ReturnsCtorEngineDirectory_WhenLlamaServerPresent()
        {
            var engineDir = CreateTempEngineDir();
            var manager = new LlamaServerManager(engineDir);

            var resolved = manager.ResolveEngineDir();

            Assert.Equal(engineDir, resolved);
            Directory.Delete(engineDir, recursive: true);
        }

        [Fact]
        public void ResolveEngineDir_PrefersInitializeOverCtor()
        {
            var ctorDir = CreateTempEngineDir();
            var initDir = CreateTempEngineDir();
            var manager = new LlamaServerManager(ctorDir);
            manager.Initialize(initDir, 8080);

            var resolved = manager.ResolveEngineDir();

            Assert.Equal(initDir, resolved);
            Directory.Delete(ctorDir, recursive: true);
            Directory.Delete(initDir, recursive: true);
        }

        [Fact]
        public void ResolveEngineDir_ReturnsCtorPath_WhenExeMissing_ForMeaningfulError()
        {
            var tempRoot = Path.Combine(
                Path.GetTempPath(),
                "NexusEngineMissing_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);
            var manager = new LlamaServerManager(tempRoot);

            var resolved = manager.ResolveEngineDir();

            Assert.Equal(tempRoot, resolved);
            Directory.Delete(tempRoot, recursive: true);
        }

        [Fact]
        public async Task GenericLlamaEngine_LoadEngine_ThrowsWithFullEnginePath()
        {
            var tempRoot = Path.Combine(
                Path.GetTempPath(),
                "NexusEngineMissing_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);
            var manager = new LlamaServerManager(tempRoot);
            var engine = new GenericLlamaEngine(manager, 8080);

            var ex = await Assert.ThrowsAsync<FileNotFoundException>(
                () => engine.LoadEngineAsync());

            Assert.Contains(Path.Combine(tempRoot, "llama-server.exe"), ex.Message);
            Directory.Delete(tempRoot, recursive: true);
        }

        private static string CreateTempEngineDir()
        {
            var dir = Path.Combine(
                Path.GetTempPath(),
                "NexusLlamaEngine_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            var exe = Path.Combine(dir, "llama-server.exe");
            File.WriteAllText(exe, string.Empty);
            return dir;
        }

        private class TestableLlamaServerManager : LlamaServerManager
        {
            // Override detect backend to ensure consistent test results on CI
            public override string DetectBackend()
            {
                return "cpu"; // Force CPU for deterministic arguments string
            }
        }

        private static int GetFreePort()
        {
            using var socket = new System.Net.Sockets.Socket(
                System.Net.Sockets.AddressFamily.InterNetwork,
                System.Net.Sockets.SocketType.Stream,
                System.Net.Sockets.ProtocolType.Tcp);
            socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            return ((IPEndPoint)socket.LocalEndPoint!).Port;
        }

        private static IDisposable StartJsonServer(
            int port,
            Func<string, string> responseFactory,
            int expectedRequests)
        {
            return new TcpJsonServer(port, responseFactory, expectedRequests);
        }

        private sealed class TcpJsonServer : IDisposable
        {
            private readonly System.Net.Sockets.TcpListener _listener;

            public TcpJsonServer(
                int port,
                Func<string, string> responseFactory,
                int expectedRequests)
            {
                _listener = new System.Net.Sockets.TcpListener(
                    IPAddress.Loopback,
                    port);
                _listener.Start();
                _ = Task.Run(async () =>
                {
                    for (var index = 0; index < expectedRequests; index++)
                    {
                        using var client = await _listener.AcceptTcpClientAsync();
                        await using var stream = client.GetStream();
                        using var reader = new StreamReader(
                            stream,
                            Encoding.ASCII,
                            leaveOpen: true);
                        var requestLine = await reader.ReadLineAsync() ?? "GET / HTTP/1.1";
                        string? line;
                        do
                        {
                            line = await reader.ReadLineAsync();
                        } while (!string.IsNullOrEmpty(line));
                        var path = requestLine.Split(' ')[1];
                        var payload = Encoding.UTF8.GetBytes(responseFactory(path));
                        var headers = Encoding.ASCII.GetBytes(
                            $"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {payload.Length}\r\nConnection: close\r\n\r\n");
                        await stream.WriteAsync(headers);
                        await stream.WriteAsync(payload);
                    }
                });
            }

            public void Dispose()
            {
                _listener.Stop();
            }
        }
    }
}
