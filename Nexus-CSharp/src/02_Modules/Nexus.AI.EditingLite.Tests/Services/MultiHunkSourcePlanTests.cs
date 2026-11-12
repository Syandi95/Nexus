using System.Collections.Immutable;
using Nexus.AI.EditingLite.Services;

namespace Nexus.AI.EditingLite.Tests.Services;

public sealed class MultiHunkSourcePlanTests
{
    private static SourceRunControl Control()=>new(()=>TimeSpan.FromSeconds(120),CancellationToken.None,()=>{});
    private static SourceFileEdit[] Edits(SourcePlanFixture f,bool fail=false)=>[
        new("Product/Alpha.cs",f.Hash("Product/Alpha.cs"),new[] { new SourceTextEdit("int Value()","int Value() => Core(); private static int Core()"),new SourceTextEdit("return 1;",fail ? "return \"two\";" : "return 2;") }),
        new("Product/Beta.cs",f.Hash("Product/Beta.cs"),"return 1;","return 2;")];
    [Fact]
    public async Task WholeFileTwoPlusOne_OneApproval_BuildTestsAndHashes()
    {
        using var f=await SourcePlanFixture.Create();var requested=Edits(f);var plan=await f.Editing.PrepareSourcePlanAsync(f.Profile,requested,Guid.NewGuid(),"A",new(),Control());
        Assert.Equal(new[] { 2,1 },plan.Entries.Select(e=>e.ResolvedEdits.Length));Assert.Equal(6,plan.Entries.Sum(e=>e.ChangedLines));
        var approval=new SourceApproval(p=>SourcePlanApproval.For(p,true));
        var result=await f.Editing.ExecuteSourcePlanAsync(plan,approval,Control());
        Assert.Equal(SourcePlanOutcome.Success,result.Outcome);Assert.Equal(1,approval.Calls);Assert.Equal(3,result.Tests!.TestsPassed);
        Assert.Equal(2,result.Operations.Length);Assert.All(plan.Entries,e=>Assert.Equal(e.DesiredHash,f.Hash(e.Target)));
    }
    [Fact]
    public async Task CanonicalFactsAndDigestTamper_RejectedBeforeWrite()
    {
        using var f=await SourcePlanFixture.Create();var plan=await f.Editing.PrepareSourcePlanAsync(f.Profile,Edits(f),Guid.NewGuid(),"A",new(),Control());
        var p=plan.Entries[0];var reverse=await f.Editing.PrepareSourceWriteAsync(f.Profile.Workspace,p.Target,p.BeforeHash,p.ResolvedEdits.Reverse().Select(e=>new SourceTextEdit(e.OldText,e.NewText)),p.RunId,p.ToolCallId,p.Limits,CancellationToken.None);
        Assert.Equal(p.ResolvedEdits.ToArray(),reverse.ResolvedEdits.ToArray());Assert.Equal(p.Diff,reverse.Diff);Assert.Equal(p.DesiredHash,reverse.DesiredHash);
        var approval=SourcePlanApproval.For(plan,true);var original=p.ResolvedEdits;
        typeof(SafeWriteProposal).GetProperty(nameof(SafeWriteProposal.ResolvedEdits))!.SetValue(p,reverse.ResolvedEdits);
        Assert.True(approval.Matches(plan));Assert.Equal(plan.PlanDigest,plan.ComputeDigest());
        foreach(var changed in new[] { original.SetItem(0,original[0] with { OldText="int Core()" }),original.SetItem(0,original[0] with { NewText="int Another()" }),original.SetItem(0,original[0] with { Start=original[0].Start+1 }),original.SetItem(0,original[0] with { Length=original[0].Length+1 }),original.SetItem(0,original[0] with { Ordinal=1 }),original.RemoveAt(1),original.Reverse().ToImmutableArray() })
        {
            typeof(SafeWriteProposal).GetProperty(nameof(SafeWriteProposal.ResolvedEdits))!.SetValue(p,changed);
            Assert.False(approval.Matches(plan));Assert.NotEqual(plan.PlanDigest,plan.ComputeDigest());
        }
        typeof(SafeWriteProposal).GetProperty(nameof(SafeWriteProposal.ResolvedEdits))!.SetValue(p,original);
        Assert.True(approval.Matches(plan));
        var desiredHash=p.DesiredHash;typeof(SafeWriteProposal).GetProperty(nameof(SafeWriteProposal.DesiredHash))!.SetValue(p,new string('0',64));Assert.False(approval.Matches(plan));
        typeof(SafeWriteProposal).GetProperty(nameof(SafeWriteProposal.DesiredHash))!.SetValue(p,desiredHash);
        typeof(SafeWriteProposal).GetProperty(nameof(SafeWriteProposal.Diff))!.SetValue(p,p.Diff+"tamper");Assert.False(approval.Matches(plan));
        Assert.All(Edits(f),e=>Assert.Equal(e.BeforeHash,f.Hash(e.Target)));
    }
    [Fact]
    public async Task FailedA_ReverseRecovery_FreshB_PreservesAllCollections()
    {
        using var f=await SourcePlanFixture.Create();var baseline=Edits(f,true);var a=await f.Editing.PrepareSourcePlanAsync(f.Profile,baseline,Guid.NewGuid(),"A",new(),Control());
        var old=SourcePlanApproval.For(a,true);var failed=await f.Editing.ExecuteSourcePlanAsync(a,new SourceApproval(_=>old),Control());
        Assert.Equal(SourcePlanOutcome.BuildFailed,failed.Outcome);Assert.True(failed.BaselineVerified);Assert.All(baseline,e=>Assert.Equal(e.BeforeHash,f.Hash(e.Target)));
        var b=await f.Editing.PrepareSourcePlanAsync(f.Profile,Edits(f),a.RunId,"B",new(),Control());Assert.False(old.Matches(b));
        Assert.Equal(b.Entries[1].ResolvedEdits.ToArray(),a.Entries[1].ResolvedEdits.ToArray());
        var rejected=await f.Editing.ExecuteSourcePlanAsync(b,new SourceApproval(_=>old),Control());Assert.Equal(SourcePlanOutcome.ApprovalDenied,rejected.Outcome);Assert.Empty(rejected.Operations);
        var result=await f.Editing.ExecuteSourcePlanAsync(b,new SourceApproval(p=>SourcePlanApproval.For(p,true)),Control());Assert.True(result.FinalVerified);Assert.Equal(3,result.Tests!.TestsPassed);
    }
    [Fact]
    public async Task PreparationBoundsAndStaleSecondFile_ZeroWrites()
    {
        using var f=await SourcePlanFixture.Create();var edits=Edits(f);
        foreach(var limits in new[] { new SourcePlanLimits(MaxChangedLines:5),new SourcePlanLimits(MaxDiffBytes:32) })
            await Assert.ThrowsAsync<ArgumentException>(()=>f.Editing.PrepareSourcePlanAsync(f.Profile,edits,Guid.NewGuid(),"bad",limits,Control()));
        var tooMany=new[] { edits[0],edits[1] with { Edits=ImmutableArray.Create(new SourceTextEdit("int Value()","int Core()"),new SourceTextEdit("return 1;","return 2;")) } };
        await Assert.ThrowsAsync<ArgumentException>(()=>f.Editing.PrepareSourcePlanAsync(f.Profile,tooMany,Guid.NewGuid(),"bad",new(),Control()));
        var tooManyBytes=new[] { new SourceFileEdit(edits[0].Target,edits[0].BeforeHash,new string('α',256),new string('β',256)),edits[1] };
        await Assert.ThrowsAsync<ArgumentException>(()=>f.Editing.PrepareSourcePlanAsync(f.Profile,tooManyBytes,Guid.NewGuid(),"bad",new(),Control()));
        var plan=await f.Editing.PrepareSourcePlanAsync(f.Profile,edits,Guid.NewGuid(),"A",new(),Control());
        var approval=new SourceApproval(p=>{ File.AppendAllText(Path.Combine(f.Root,"Product/Beta.cs"),"// external");return SourcePlanApproval.For(p,true); });
        var result=await f.Editing.ExecuteSourcePlanAsync(plan,approval,Control());Assert.Equal(SourcePlanOutcome.PlanConflict,result.Outcome);Assert.Empty(result.Operations);Assert.Equal(edits[0].BeforeHash,f.Hash(edits[0].Target));
    }
}
