namespace Nexus.AI.EditingLite.Tests.Services;

using Nexus.AI.EditingLite.Services;

public sealed class SourcePlanRemainingBudgetTests
{
    [Theory]
    [InlineData("Build",120)]
    [InlineData("Test",120)]
    [InlineData("Build",180)]
    [InlineData("Test",180)]
    public async Task ExhaustedRunBudget_DoesNotRefillAtVerification_AndReverseRecoveryStillCompletes(string stage,int seconds)
    {
        using var f=await SourcePlanFixture.Create();var remaining=TimeSpan.FromSeconds(seconds);
        var run=new SourceRunControl(()=>remaining,CancellationToken.None,()=>{});var plan=await f.Plan(run);
        var started=new List<string>();var restored=new List<int>();
        f.Editing.SourceVerifier.Probe=(kind,_)=>started.Add(kind);
        f.Editing.SourcePlanProbe=(kind,_,ordinal)=>
        {
            if(kind=="Source"+stage+"Started")remaining=TimeSpan.Zero;
            if(kind=="SourceRecoveryStarted")restored.Add(ordinal);
        };
        var result=await f.Editing.ExecuteSourcePlanAsync(plan,new ApproveExact(),run);
        Assert.Equal(SourcePlanOutcome.DeadlineExceeded,result.Outcome);Assert.False(result.FinalVerified);
        Assert.Equal(stage=="Build"?Array.Empty<string>():["Build"],started);
        Assert.Equal(new[] {1,0},restored);Assert.True(result.BaselineVerified);
        Assert.All(result.Operations,r=>Assert.Equal(SafeWriteRecovery.Restored,r.Recovery));
        Assert.All(plan.Entries,e=>Assert.Equal(e.BeforeHash,f.Hash(e.Target)));
    }
    private sealed class ApproveExact : IWriteApprovalProvider
    {
        public Task<WriteApprovalDecision> RequestAsync(SafeWriteProposal proposal,CancellationToken token)=>throw new InvalidOperationException();
        public Task<SourcePlanApproval> RequestPlanAsync(SourceEditPlan plan,CancellationToken token)=>Task.FromResult(SourcePlanApproval.For(plan,true));
    }
}
