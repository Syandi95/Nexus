using System.IO;
using Moq;
using Nexus.AI.Llama.Services;
using Nexus.App.Services.Agent;

namespace Nexus.Integration.Tests;

public sealed class AgentCapabilityCacheTests
{
    [Fact]
    public async Task CapabilityCache_ReusesHashes_AndInvalidatesPathLengthAndMtime()
    {
        var root=Path.Combine(Path.GetTempPath(),"nexus-capability-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root,"b11057"));
        var model=Path.Combine(root,"gemma-4-E4B-it-Q4_K_M.gguf");
        var engine=Path.Combine(root,"b11057","llama-server.exe");
        File.WriteAllBytes(model,[1]); File.WriteAllBytes(engine,[2]);
        var server=new Mock<ILlamaServerManager>(); server.Setup(s=>s.ResolveEngineDir()).Returns(Path.GetDirectoryName(engine)!);
        var capability=new NativeToolCapability(server.Object);
        var snapshot=new AgentModelSnapshot("gemma-4-e4b",model,123,"private-endpoint",1);
        try
        {
            Assert.Equal(NativeToolSupport.Unknown,await capability.GetSupportAsync(snapshot,CancellationToken.None));
            Assert.Equal(NativeToolSupport.Unknown,await capability.GetSupportAsync(snapshot,CancellationToken.None));
            Assert.Equal(1,capability.FingerprintComputationCount); Assert.Equal(1,capability.CacheHitCount);
            File.WriteAllBytes(model,[1,3]); await capability.GetSupportAsync(snapshot,CancellationToken.None);
            Assert.Equal(2,capability.FingerprintComputationCount);
            File.SetLastWriteTimeUtc(model,File.GetLastWriteTimeUtc(model).AddSeconds(10));
            await capability.GetSupportAsync(snapshot,CancellationToken.None); Assert.Equal(3,capability.FingerprintComputationCount);
            Directory.CreateDirectory(Path.Combine(root,"other"));
            var other=Path.Combine(root,"other",Path.GetFileName(model)); File.Copy(model,other);
            await capability.GetSupportAsync(snapshot with { ModelPath=other },CancellationToken.None);
            Assert.Equal(4,capability.FingerprintComputationCount);
            File.WriteAllBytes(engine,[2,4]); await capability.GetSupportAsync(snapshot with { ModelPath=other },CancellationToken.None);
            Assert.Equal(5,capability.FingerprintComputationCount);
        }
        finally { Directory.Delete(root,true); }
    }

    [Fact]
    public async Task Capability_RejectsUnknownModel_FailClosed()
    {
        var root = Path.Combine(Path.GetTempPath(), "nexus-capability-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "b11057"));
        var model = Path.Combine(root, "gemma-4-E4B-it-Q4_K_M.gguf");
        var engine = Path.Combine(root, "b11057", "llama-server.exe");
        File.WriteAllBytes(model, [1]);
        File.WriteAllBytes(engine, [2]);
        var server = new Mock<ILlamaServerManager>();
        server.Setup(s => s.ResolveEngineDir()).Returns(Path.GetDirectoryName(engine)!);
        var capability = new NativeToolCapability(server.Object);

        try
        {
            var unknownSnapshot = new AgentModelSnapshot("unknown-model", model, 123, "private-endpoint", 1);
            Assert.Equal(NativeToolSupport.Unknown, await capability.GetSupportAsync(unknownSnapshot, CancellationToken.None));
            Assert.Equal(0, capability.FingerprintComputationCount);

            var llamaSnapshot = new AgentModelSnapshot("llama-3-8b", model, 123, "private-endpoint", 1);
            Assert.Equal(NativeToolSupport.Unknown, await capability.GetSupportAsync(llamaSnapshot, CancellationToken.None));
            Assert.Equal(0, capability.FingerprintComputationCount);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Capability_RejectsMismatchedFilename_EvenIfModelIdMatches()
    {
        var root = Path.Combine(Path.GetTempPath(), "nexus-capability-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "b11057"));
        var wrongQwen = Path.Combine(root, "wrong-qwen.gguf");
        var wrongGemma = Path.Combine(root, "wrong-gemma.gguf");
        var engine = Path.Combine(root, "b11057", "llama-server.exe");
        File.WriteAllBytes(wrongQwen, [1]);
        File.WriteAllBytes(wrongGemma, [2]);
        File.WriteAllBytes(engine, [3]);
        var server = new Mock<ILlamaServerManager>();
        server.Setup(s => s.ResolveEngineDir()).Returns(Path.GetDirectoryName(engine)!);
        var capability = new NativeToolCapability(server.Object);

        try
        {
            var qwenSnapshot = new AgentModelSnapshot("qwen3-4b", wrongQwen, 123, "private-endpoint", 1);
            Assert.Equal(NativeToolSupport.Unknown, await capability.GetSupportAsync(qwenSnapshot, CancellationToken.None));
            Assert.Equal(0, capability.FingerprintComputationCount);

            var gemmaSnapshot = new AgentModelSnapshot("gemma-4-e4b", wrongGemma, 123, "private-endpoint", 1);
            Assert.Equal(NativeToolSupport.Unknown, await capability.GetSupportAsync(gemmaSnapshot, CancellationToken.None));
            Assert.Equal(0, capability.FingerprintComputationCount);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Capability_RejectsMismatchedEngineDirectory_OrMismatchedEngineHash()
    {
        var root = Path.Combine(Path.GetTempPath(), "nexus-capability-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "b11160"));
        Directory.CreateDirectory(Path.Combine(root, "b11057"));
        var model = Path.Combine(root, "Qwen3-4B-Q4_K_M.gguf");
        var wrongEngine = Path.Combine(root, "b11160", "llama-server.exe");
        var mismatchedHashEngine = Path.Combine(root, "b11057", "llama-server.exe");
        File.WriteAllBytes(model, [1]);
        File.WriteAllBytes(wrongEngine, [2]);
        File.WriteAllBytes(mismatchedHashEngine, [3]);

        var server = new Mock<ILlamaServerManager>();
        server.Setup(s => s.ResolveEngineDir()).Returns(Path.GetDirectoryName(wrongEngine)!);
        var capability = new NativeToolCapability(server.Object);

        try
        {
            var snapshot = new AgentModelSnapshot("qwen3-4b", model, 123, "private-endpoint", 1);
            // 1. Directory mismatch b11160 rejected without hashing
            Assert.Equal(NativeToolSupport.Unknown, await capability.GetSupportAsync(snapshot, CancellationToken.None));
            Assert.Equal(0, capability.FingerprintComputationCount);

            // 2. Directory matches b11057, but hash does not match pinned engine hash
            server.Setup(s => s.ResolveEngineDir()).Returns(Path.GetDirectoryName(mismatchedHashEngine)!);
            Assert.Equal(NativeToolSupport.Unknown, await capability.GetSupportAsync(snapshot, CancellationToken.None));
            Assert.Equal(1, capability.FingerprintComputationCount);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Capability_ModelSwitching_MaintainsIndependentCacheEntries_WithoutStaleLeakage()
    {
        var root = Path.Combine(Path.GetTempPath(), "nexus-capability-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "b11057"));
        var gemma = Path.Combine(root, "gemma-4-E4B-it-Q4_K_M.gguf");
        var qwen = Path.Combine(root, "Qwen3-4B-Q4_K_M.gguf");
        var engine = Path.Combine(root, "b11057", "llama-server.exe");
        File.WriteAllBytes(gemma, [10]);
        File.WriteAllBytes(qwen, [20]);
        File.WriteAllBytes(engine, [30]);

        var server = new Mock<ILlamaServerManager>();
        server.Setup(s => s.ResolveEngineDir()).Returns(Path.GetDirectoryName(engine)!);
        var capability = new NativeToolCapability(server.Object);

        var gemmaSnapshot = new AgentModelSnapshot("gemma-4-e4b", gemma, 123, "private-endpoint", 1);
        var qwenSnapshot = new AgentModelSnapshot("qwen3-4b", qwen, 123, "private-endpoint", 1);

        try
        {
            // Initial evaluations: each model computes its own fingerprint
            Assert.Equal(NativeToolSupport.Unknown, await capability.GetSupportAsync(gemmaSnapshot, CancellationToken.None));
            Assert.Equal(1, capability.FingerprintComputationCount);
            Assert.Equal(0, capability.CacheHitCount);

            Assert.Equal(NativeToolSupport.Unknown, await capability.GetSupportAsync(qwenSnapshot, CancellationToken.None));
            Assert.Equal(2, capability.FingerprintComputationCount);
            Assert.Equal(0, capability.CacheHitCount);

            // Interleaved switches: each model hits its independent cached entry
            Assert.Equal(NativeToolSupport.Unknown, await capability.GetSupportAsync(gemmaSnapshot, CancellationToken.None));
            Assert.Equal(2, capability.FingerprintComputationCount);
            Assert.Equal(1, capability.CacheHitCount);

            Assert.Equal(NativeToolSupport.Unknown, await capability.GetSupportAsync(qwenSnapshot, CancellationToken.None));
            Assert.Equal(2, capability.FingerprintComputationCount);
            Assert.Equal(2, capability.CacheHitCount);

            // Invalidate Gemma only by modifying length: Qwen cache must remain intact and independent
            File.WriteAllBytes(gemma, [10, 11]);
            Assert.Equal(NativeToolSupport.Unknown, await capability.GetSupportAsync(gemmaSnapshot, CancellationToken.None));
            Assert.Equal(3, capability.FingerprintComputationCount);
            Assert.Equal(2, capability.CacheHitCount);

            Assert.Equal(NativeToolSupport.Unknown, await capability.GetSupportAsync(qwenSnapshot, CancellationToken.None));
            Assert.Equal(3, capability.FingerprintComputationCount);
            Assert.Equal(3, capability.CacheHitCount);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Capability_RecognizesVerifiedOnDiskProfiles_GemmaAndQwen()
    {
        var engineDir = @"D:\NEXUS\Engine\LlamaServer\b11057";
        var gemmaFile = @"D:\NEXUS\Models\AI Workspace\Gemma-4-E4B\gemma-4-E4B-it-Q4_K_M.gguf";
        var qwenFile = @"D:\NEXUS\Models\AI Workspace\Qwen3-4B\Qwen3-4B-Q4_K_M.gguf";

        if (!Directory.Exists(engineDir) || !File.Exists(gemmaFile) || !File.Exists(qwenFile))
        {
            return;
        }

        var server = new Mock<ILlamaServerManager>();
        server.Setup(s => s.ResolveEngineDir()).Returns(engineDir);
        var capability = new NativeToolCapability(server.Object);

        var gemmaSnapshot = new AgentModelSnapshot("gemma-4-e4b", gemmaFile, 123, "http://127.0.0.1:8080/v1/chat/completions", 1);
        var qwenSnapshot = new AgentModelSnapshot("qwen3-4b", qwenFile, 123, "http://127.0.0.1:8080/v1/chat/completions", 1);

        // First verification: computes hashes
        Assert.Equal(NativeToolSupport.Supported, await capability.GetSupportAsync(gemmaSnapshot, CancellationToken.None));
        Assert.Equal(NativeToolSupport.Supported, await capability.GetSupportAsync(qwenSnapshot, CancellationToken.None));
        Assert.Equal(2, capability.FingerprintComputationCount);
        Assert.Equal(0, capability.CacheHitCount);

        // Second verification: hits per-model cache
        Assert.Equal(NativeToolSupport.Supported, await capability.GetSupportAsync(gemmaSnapshot, CancellationToken.None));
        Assert.Equal(NativeToolSupport.Supported, await capability.GetSupportAsync(qwenSnapshot, CancellationToken.None));
        Assert.Equal(2, capability.FingerprintComputationCount);
        Assert.Equal(2, capability.CacheHitCount);
    }
}
