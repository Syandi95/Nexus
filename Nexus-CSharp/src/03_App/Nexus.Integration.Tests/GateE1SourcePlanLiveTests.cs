using System.Collections.Immutable;
using Nexus.AI.EditingLite.Services;
using Xunit.Abstractions;

namespace Nexus.Integration.Tests;

public sealed class GateE1LiveFactAttribute : FactAttribute
{
    public GateE1LiveFactAttribute()
    { if(Environment.GetEnvironmentVariable("NEXUS_GATE_E1_LIVE")!="1") Skip="Explicit Gate E.1 reviewed source fixture/shared-model proof required."; }
}
public sealed class GateE1SourcePlanLiveTests(ITestOutputHelper output)
{
    [GateE1LiveFact]
    public Task OneApprovedSourcePlan_UsesSharedRuntimeAndPreservesChat()=>new GateCToolLiveTests(output).ExecuteProof(true,gateE1:true);
}
internal sealed class GateE1ProofApproval : IWriteApprovalProvider
{
    public int Requests { get; private set; }
    public Task<WriteApprovalDecision> RequestAsync(SafeWriteProposal proposal,CancellationToken token)=>throw new InvalidOperationException("Only plan approval expected.");
    public Task<SourcePlanApproval> RequestPlanAsync(SourceEditPlan plan,CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); Requests++;
        Assert.Equal("certified-csharp-fixture-v1",plan.VerificationProfileId); Assert.Equal(2,plan.Entries.Length);
        Assert.All(plan.Entries,e=>{ Assert.Equal("return 1;",e.OldText); Assert.Equal("return 2;",e.NewText); });
        return Task.FromResult(SourcePlanApproval.For(plan,true));
    }
}
