using System.IO;
using System.Security.Cryptography;
using Nexus.AI.Llama.Services;

namespace Nexus.App.Services.Agent;

public enum NativeToolSupport { Supported, Unsupported, Unknown }

// Explicit known fixture, not an assumption based on endpoint compatibility.
// File identity checks happen inside Agent admission/deadline, never as a tool.
public sealed class NativeToolCapability(ILlamaServerManager server)
{
    private sealed record ModelProfile(string FileName, string ModelHash);

    private const string EngineHash = "C72F76994F8D680D7B423FF68662DE33857D2A2B001465DE55952873D4EF3DF3";

    private static readonly Dictionary<string, ModelProfile> SupportedProfiles = new(StringComparer.OrdinalIgnoreCase)
    {
        ["gemma-4-e4b"] = new("gemma-4-E4B-it-Q4_K_M.gguf", "519B9793ED6CE0FF530F1B7C96E848E08E49E7AF4D57BB97F76215963A54146D"),
        ["qwen3-4b"] = new("Qwen3-4B-Q4_K_M.gguf", "7485FE6F11AF29433BC51CAB58009521F205840F5B4AE3A32FA7F92E8534FDF5")
    };

    private readonly Dictionary<string, (string Identity, NativeToolSupport Support)> _cache = new(StringComparer.OrdinalIgnoreCase);

    internal int FingerprintComputationCount { get; private set; }
    internal int CacheHitCount { get; private set; }

    internal async Task<NativeToolSupport> GetSupportAsync(AgentModelSnapshot model, CancellationToken token)
    {
        if (model is null || string.IsNullOrWhiteSpace(model.ModelPath)) return NativeToolSupport.Unknown;
        if (!SupportedProfiles.TryGetValue(model.ModelId, out var profile)) return NativeToolSupport.Unknown;
        if (!string.Equals(Path.GetFileName(model.ModelPath), profile.FileName, StringComparison.OrdinalIgnoreCase)) return NativeToolSupport.Unknown;

        var engineDirectory = server.ResolveEngineDir();
        if (string.IsNullOrWhiteSpace(engineDirectory) || new DirectoryInfo(engineDirectory).Name != "b11057") return NativeToolSupport.Unknown;
        var executable = Path.Combine(engineDirectory, "llama-server.exe");

        var modelInfo = new FileInfo(model.ModelPath);
        var engineInfo = new FileInfo(executable);
        if (!modelInfo.Exists || !engineInfo.Exists) return NativeToolSupport.Unknown;

        var identity = $"{modelInfo.FullName}|{modelInfo.Length}|{modelInfo.LastWriteTimeUtc.Ticks}|{engineInfo.FullName}|{engineInfo.Length}|{engineInfo.LastWriteTimeUtc.Ticks}";
        if (_cache.TryGetValue(model.ModelId, out var cached) && cached.Identity == identity)
        {
            CacheHitCount++;
            return cached.Support;
        }

        FingerprintComputationCount++;
        await using var modelFile = File.OpenRead(model.ModelPath);
        var modelHash = Convert.ToHexString(await SHA256.HashDataAsync(modelFile, token).ConfigureAwait(false));
        await using var engineFile = File.OpenRead(executable);
        var engineHash = Convert.ToHexString(await SHA256.HashDataAsync(engineFile, token).ConfigureAwait(false));
        token.ThrowIfCancellationRequested();

        var isSupported = string.Equals(modelHash, profile.ModelHash, StringComparison.OrdinalIgnoreCase) &&
                          string.Equals(engineHash, EngineHash, StringComparison.OrdinalIgnoreCase);
        var support = isSupported ? NativeToolSupport.Supported : NativeToolSupport.Unknown;
        _cache[model.ModelId] = (identity, support);
        return support;
    }
}
