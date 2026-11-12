using System.Threading;
using System.Threading.Tasks;

namespace Nexus.AI.Llama.Services
{
    public interface ILlamaServerManager
    {
        ServerState State { get; }
        int? OwnedPid { get; }
        string? RuntimeModelPath { get; }
        void Initialize(string engineDir, int port = 8080);
        string ResolveEngineDir();
        string DetectBackend();
        string GetTuningArguments();
        string BuildStartArguments(string modelPath, string chatTemplate, int contextSize);
        Task<bool> IsServerHealthyAsync();
        Task<bool> IsServerReadyForModelAsync(
            string modelPath,
            CancellationToken cancellationToken = default);
        Task StartServerAsync(string modelPath, string chatTemplate, int contextSize);
        Task StartServerAsync(string modelPath, string chatTemplate, int contextSize, CancellationToken cancellationToken);
        Task StopServerAsync();
    }
}
