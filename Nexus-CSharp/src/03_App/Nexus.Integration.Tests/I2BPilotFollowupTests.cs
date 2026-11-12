using System.IO;
using Nexus.AI.EditingLite.Services;

namespace Nexus.Integration.Tests;

public sealed class I2BPilotFollowupTests
{
    [Fact]
    public async Task VerificationReserve_ConsumedDuringStaging_RejectsAtCommitWithoutChangingSource()
    {
        using var workspace=await SourcePlanTestWorkspace.Create();var remaining=TimeSpan.FromSeconds(120);
        var control=new SourceRunControl(()=>remaining,CancellationToken.None,()=>{})
        {MinimumVerificationBudget=workspace.Profile.BuildTimeout+workspace.Profile.TestTimeout};
        var plan=await workspace.Editing.PrepareSourcePlanAsync(workspace.Profile,
            [new("Product/Alpha.cs",workspace.Hash("Product/Alpha.cs"),"return 1;","return 2;"),new("Product/Beta.cs",workspace.Hash("Product/Beta.cs"),"return 1;","return 2;")],
            Guid.NewGuid(),"offline-staging-budget",new(),control);
        remaining=TimeSpan.FromSeconds(31);
        workspace.Editing.SafeWriteProbe=(stage,_)=>
        {
            if(stage==SafeWriteStage.TempWritten)remaining=TimeSpan.FromSeconds(29);
            // Independent recovery guard keeps the RED reproduction disposable and preserved.
            if(stage==SafeWriteStage.Committed)throw new IOException("Offline postcommit recovery guard");
        };
        var result=await workspace.Editing.ExecuteSourcePlanAsync(plan,new ExactApproval(),control);
        Assert.Equal("InsufficientRemainingBudget",result.Outcome.ToString());
        Assert.All(result.Operations,operation=>Assert.False(operation.CommitReached));
        Assert.Null(result.Build);Assert.Null(result.Tests);Assert.True(result.BaselineVerified);
        Assert.All(plan.Entries,e=>Assert.Equal(e.BeforeHash,workspace.Hash(e.Target)));
    }
    [Theory]
    [InlineData(30,true,false)]
    [InlineData(29,true,false)]
    [InlineData(31,true,true)]
    [InlineData(10,false,true)]
    public async Task VerificationReserve_IsCheckedAfterRevalidation_BeforeFirstApply(double seconds,bool reserve,bool mayReachApply)
    {
        using var workspace=await SourcePlanTestWorkspace.Create();
        var remaining=TimeSpan.FromSeconds(120);var reached=false;
        var control=new SourceRunControl(()=>remaining,CancellationToken.None,()=>{});
        if(reserve)typeof(SourceRunControl).GetProperty("MinimumVerificationBudget")?.SetValue(control,workspace.Profile.BuildTimeout+workspace.Profile.TestTimeout);
        var plan=await workspace.Editing.PrepareSourcePlanAsync(workspace.Profile,
            [new("Product/Alpha.cs",workspace.Hash("Product/Alpha.cs"),"return 1;","return 2;"),new("Product/Beta.cs",workspace.Hash("Product/Beta.cs"),"return 1;","return 2;")],
            Guid.NewGuid(),"offline-budget",new(),control);
        workspace.Editing.SourcePlanProbe=(stage,_,_)=>
        {
            if(stage=="SourcePreflightPassed")remaining=TimeSpan.FromSeconds(seconds);
            if(stage=="SourceApplyStarted"){reached=true;throw new InvalidOperationException("Independent prewrite stop");}
        };
        var result=await workspace.Editing.ExecuteSourcePlanAsync(plan,new ExactApproval(),control);
        Assert.Equal(mayReachApply,reached);
        if(!mayReachApply)Assert.Equal("InsufficientRemainingBudget",result.Outcome.ToString());
        Assert.Empty(result.Operations);Assert.Null(result.Build);Assert.Null(result.Tests);Assert.True(result.BaselineVerified);
        Assert.All(plan.Entries,e=>Assert.Equal(e.BeforeHash,workspace.Hash(e.Target)));
    }
    private sealed class ExactApproval:IWriteApprovalProvider
    {
        public Task<WriteApprovalDecision> RequestAsync(SafeWriteProposal proposal,CancellationToken token)=>Task.FromResult(WriteApprovalDecision.For(proposal,false));
        public Task<SourcePlanApproval> RequestPlanAsync(SourceEditPlan plan,CancellationToken token)=>Task.FromResult(SourcePlanApproval.For(plan,true));
    }
}
