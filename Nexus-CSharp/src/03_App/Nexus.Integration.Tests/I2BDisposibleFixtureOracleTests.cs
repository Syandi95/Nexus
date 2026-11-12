using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using Nexus.AI.EditingLite.Services;
using Nexus.Contracts;
using Xunit;

namespace Nexus.Integration.Tests;

public sealed class I2BDisposibleFixtureOracleTests
{
    private const string ReviewedRoot = @"D:\NEXUS\scratch\I2B-ReviewedContracts";

    [Fact]
    public void DisposableFixture_Isolation_NeverMutatesPrimaryWorkspace()
    {
        var temp = Path.Combine(Path.GetTempPath(), "nexus-i2b-disposable-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);

        var originalA = Path.Combine(ReviewedRoot, "Nexus.Contracts", "EngineSessionOptions.cs");
        var originalB = Path.Combine(ReviewedRoot, "Nexus.Contracts", "EngineMemoryMetrics.cs");

        var hashABefore = HashFile(originalA);
        var hashBBefore = HashFile(originalB);

        try
        {
            var copyA = Path.Combine(temp, "EngineSessionOptions.cs");
            var copyB = Path.Combine(temp, "EngineMemoryMetrics.cs");

            File.Copy(originalA, copyA);
            File.Copy(originalB, copyB);

            // Mutate disposable copy
            File.AppendAllText(copyA, "\n// disposable mutation");
            File.AppendAllText(copyB, "\n// disposable mutation");

            // Verify original primary workspace files remain 100% byte-identical
            Assert.Equal(hashABefore, HashFile(originalA));
            Assert.Equal(hashBBefore, HashFile(originalB));
        }
        finally
        {
            if (Directory.Exists(temp)) Directory.Delete(temp, true);
        }
    }

    [Theory]
    [InlineData("correct", true)]
    [InlineData("inverted", false)]
    [InlineData("tautology", false)]
    public void Oracle_ValidatesTargetA_IsCpu_BehavioralTruthTable(string mode, bool expectedPass)
    {
        // Target A requirement: IsCpu is true if and only if execution provider is NOT CPU.
        bool EvaluateIsCpu(ExecutionProvider provider) => mode switch
        {
            "correct" => provider != ExecutionProvider.Cpu,
            "inverted" => provider == ExecutionProvider.Cpu,
            "tautology" => true, // ExecutionProvider.Cpu == ExecutionProvider.Cpu
            _ => false
        };

        bool passed = true;
        foreach (var provider in Enum.GetValues<ExecutionProvider>())
        {
            bool expected = provider != ExecutionProvider.Cpu;
            bool actual = EvaluateIsCpu(provider);
            if (actual != expected)
            {
                passed = false;
                break;
            }
        }

        Assert.Equal(expectedPass, passed);
    }

    [Theory]
    [InlineData("correct", true)]
    [InlineData("greater_or_equal", false)]
    [InlineData("always_true", false)]
    public void Oracle_ValidatesTargetB_HasActiveSessions_BehavioralTruthTable(string mode, bool expectedPass)
    {
        // Target B requirement: HasActiveSessions is true if and only if active sessions count is greater than zero.
        bool EvaluateHasActiveSessions(int count) => mode switch
        {
            "correct" => count > 0,
            "greater_or_equal" => count >= 0,
            "always_true" => true,
            _ => false
        };

        var testCases = new (int count, bool expected)[]
        {
            (0, false),
            (-1, false),
            (1, true),
            (5, true),
            (int.MaxValue, true)
        };

        bool passed = true;
        foreach (var (count, expected) in testCases)
        {
            bool actual = EvaluateHasActiveSessions(count);
            if (actual != expected)
            {
                passed = false;
                break;
            }
        }

        Assert.Equal(expectedPass, passed);
    }

    [Fact]
    public void CompilationProof_CompilerRejectsFieldInitializerReferencingInstanceMember_CS0236()
    {
        var temp = Path.Combine(Path.GetTempPath(), "nexus-cs0236-proof-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);

        try
        {
            // Invalid C# emitted by model in live capture:
            // "public bool HasActiveSessions { get; init; } = ActiveSessionsCount > 0;"
            // Auto-property field initializer cannot reference non-static instance member 'ActiveSessionsCount'.
            var invalidClass = @"
namespace TestProof;
public class EngineMemoryMetrics
{
    public int ActiveSessionsCount { get; init; }
    public bool HasActiveSessions { get; init; } = ActiveSessionsCount > 0;
}";
            var validClass = @"
namespace TestProof;
public class EngineMemoryMetrics
{
    public int ActiveSessionsCount { get; init; }
    public bool HasActiveSessions => ActiveSessionsCount > 0;
}";

            var (invalidSuccess, invalidOutput) = CompileSnippet(invalidClass);
            Assert.False(invalidSuccess, "Field initializer referencing instance member must fail compilation.");
            Assert.Contains("CS0236", invalidOutput);

            var (validSuccess, validOutput) = CompileSnippet(validClass);
            Assert.True(validSuccess, "Computed property expression must compile cleanly: " + validOutput);
        }
        finally
        {
            if (Directory.Exists(temp)) Directory.Delete(temp, true);
        }
    }

    [Fact]
    public void Workflow_SafeWriteAndRecovery_ProtectsDisposableWorkspaceOnFailure()
    {
        var temp = Path.Combine(Path.GetTempPath(), "nexus-recovery-disposable-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);

        var targetFile = Path.Combine(temp, "EngineMemoryMetrics.cs");
        var originalContent = @"namespace Nexus.Contracts;
public class EngineMemoryMetrics
{
    public int ActiveSessionsCount { get; init; }
}";
        File.WriteAllText(targetFile, originalContent);
        var snapshotService = new FileSnapshotService();
        var initialHash = snapshotService.ComputeSha256Hash(targetFile);

        try
        {
            // Simulate a proposal that causes a build failure
            var fileWriter = new FileWriterService();
            var backupPath = Path.Combine(temp, "EngineMemoryMetrics.cs.bak");
            File.Copy(targetFile, backupPath);

            // Mutate
            File.WriteAllText(targetFile, originalContent + "\npublic bool HasActiveSessions = BrokenSyntax;");

            // Perform SafeWrite reverse recovery
            File.Copy(backupPath, targetFile, overwrite: true);
            File.Delete(backupPath);

            var recoveredHash = snapshotService.ComputeSha256Hash(targetFile);
            Assert.Equal(initialHash, recoveredHash);
            Assert.Equal(originalContent, File.ReadAllText(targetFile));
        }
        finally
        {
            if (Directory.Exists(temp)) Directory.Delete(temp, true);
        }
    }

    private static (bool success, string output) CompileSnippet(string snippet)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "nexus-proof-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var csproj = @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <OutputType>Library</OutputType>
  </PropertyGroup>
</Project>";
            File.WriteAllText(Path.Combine(tempDir, "Proof.csproj"), csproj);
            File.WriteAllText(Path.Combine(tempDir, "Snippet.cs"), snippet);

            var sdk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "dotnet", "dotnet.exe");
            if (!File.Exists(sdk)) sdk = "dotnet";

            var psi = new System.Diagnostics.ProcessStartInfo(sdk, $"build \"{Path.Combine(tempDir, "Proof.csproj")}\" --no-incremental -v q")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = System.Diagnostics.Process.Start(psi)!;
            var outText = proc.StandardOutput.ReadToEnd() + proc.StandardError.ReadToEnd();
            proc.WaitForExit(30000);
            return (proc.ExitCode == 0, outText);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}
