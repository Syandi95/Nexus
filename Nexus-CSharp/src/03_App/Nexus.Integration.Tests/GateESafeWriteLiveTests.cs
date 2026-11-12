using System.Security.Cryptography;
using System.Text;
using Nexus.AI.EditingLite.Services;
using Xunit.Abstractions;

namespace Nexus.Integration.Tests;

public sealed class GateELiveFactAttribute : FactAttribute
{
    public GateELiveFactAttribute()
    { if(Environment.GetEnvironmentVariable("NEXUS_GATE_E_LIVE")!="1") Skip="Explicit Gate E isolated approved workspace/shared-model fixtures required."; }
}
public sealed class GateESafeWriteLiveTests(ITestOutputHelper output)
{
    [GateELiveFact]
    public Task OneApprovedTextPatch_UsesSharedRuntimeAndVerifiesExactState()=>new GateCToolLiveTests(output).ExecuteProof(false,true);
}
internal sealed class GateEProofApproval : IWriteApprovalProvider
{
    public int Requests { get; private set; }
    public Task<WriteApprovalDecision> RequestAsync(SafeWriteProposal exact,CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); Requests++;
        Assert.Equal("sample.txt",exact.Target); Assert.Equal("SafeWrite",exact.Risk);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("hello\r\n"))).ToLowerInvariant(),exact.BeforeHash);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("hello nexus\r\n"))).ToLowerInvariant(),exact.DesiredHash);
        Assert.Contains("- hello",exact.Diff); Assert.Contains("+ hello nexus",exact.Diff);
        return Task.FromResult(WriteApprovalDecision.For(exact,true)); // Explicit synthetic user decision, never model data.
    }
}
