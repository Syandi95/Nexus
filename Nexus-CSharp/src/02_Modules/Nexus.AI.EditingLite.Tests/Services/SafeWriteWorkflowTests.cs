using System.Text;
using Nexus.AI.EditingLite.Services;

namespace Nexus.AI.EditingLite.Tests.Services;

public sealed class SafeWriteWorkflowTests : IDisposable
{
    private readonly string root=Path.Combine(Path.GetTempPath(),"nexus-safe-write-"+Guid.NewGuid().ToString("N"));
    private readonly EditingWorkflowService workflow;
    private readonly FileWriterService writer=new();
    private readonly FileSnapshotService snapshots=new();
    private readonly ApprovedTextWorkspace workspace;
    private string Target=>Path.Combine(root,"sample.txt");
    public SafeWriteWorkflowTests()
    {
        Directory.CreateDirectory(root); File.WriteAllText(Target,"hello\r\n",new UTF8Encoding(false));
        workspace=new(root); workflow=new(snapshots,new ProposalService(),new SyntaxValidatorService(),writer);
    }
    private Task<SafeWriteProposal> Prepare(string old="hello",string next="hello nexus",string? hash=null)=>workflow.PrepareSafeWriteAsync(workspace,"sample.txt",hash ?? snapshots.ComputeSha256Hash(Target),old,next,Guid.NewGuid(),"call-1",new(),CancellationToken.None);
    private Task<SafeWriteResult> Apply(SafeWriteProposal p,bool approve=true,CancellationToken token=default)=>workflow.ApplyApprovedSafeWriteAsync(p,WriteApprovalDecision.For(p,approve),token);

    [Fact]
    public async Task ApprovedPatch_AtomicVerified_WithRetainedExactBackupAndDiff()
    {
        var before=File.ReadAllBytes(Target); var p=await Prepare();
        Assert.Contains("- hello",p.Diff); Assert.Contains("+ hello nexus",p.Diff);
        var result=await Apply(p);
        Assert.Equal(SafeWriteOutcome.Success,result.Outcome); Assert.True(result.Operation.CommitReached); Assert.True(result.Operation.Verified);
        Assert.Equal("hello nexus\r\n",File.ReadAllText(Target)); Assert.Equal(p.DesiredHash,snapshots.ComputeSha256Hash(Target));
        Assert.Equal(before,File.ReadAllBytes(Path.Combine(root,result.Operation.BackupRef!))); Assert.Empty(Directory.GetFiles(root,"*.tmp"));
    }
    [Fact]
    public async Task StaleHashAfterApproval_Conflict_ExternalBytesIntact()
    {
        var p=await Prepare(); File.WriteAllText(Target,"external newer");
        var result=await Apply(p);
        Assert.Equal(SafeWriteOutcome.Conflict,result.Outcome); Assert.False(result.Operation.CommitReached);
        Assert.Equal("external newer",File.ReadAllText(Target)); Assert.Single(Directory.GetFiles(root));
    }
    [Fact]
    public async Task StaleHashAtPrepare_Conflict_NoArtifacts()
    {
        var hash=snapshots.ComputeSha256Hash(Target); File.WriteAllText(Target,"external");
        await Assert.ThrowsAsync<SafeWriteConflictException>(()=>Prepare(hash:hash)); Assert.Single(Directory.GetFiles(root));
    }
    [Fact]
    public async Task RejectApproval_ZeroMutationOrTemp()
    {
        var p=await Prepare(); var result=await Apply(p,false);
        Assert.Equal(SafeWriteOutcome.ApprovalDenied,result.Outcome); Assert.Equal(p.BeforeHash,snapshots.ComputeSha256Hash(Target)); Assert.Single(Directory.GetFiles(root));
    }
    [Fact]
    public async Task ChangedProposalCannotReuseApproval()
    {
        var p=await Prepare(); var approval=WriteApprovalDecision.For(p,true);
        foreach(var changed in new[] { p with { DesiredText="different" },p with { Target="different.txt" },p with { DesiredHash=new string('a',64) },p with { Diff="fake" },p with { OperationId=Guid.NewGuid() },p with { Workspace=new ApprovedTextWorkspace(root) },p with { RunId=Guid.NewGuid() },p with { ToolCallId="other-call" },p with { Limits=new SafeWriteLimits(512,2048) } })
            Assert.Equal(SafeWriteOutcome.ApprovalDenied,(await workflow.ApplyApprovedSafeWriteAsync(changed,approval,CancellationToken.None)).Outcome);
        Assert.Equal(p.BeforeHash,snapshots.ComputeSha256Hash(Target));
    }
    [Theory]
    [InlineData(SafeWriteStage.Approved)]
    [InlineData(SafeWriteStage.BackupCreated)]
    [InlineData(SafeWriteStage.TempWritten)]
    [InlineData(SafeWriteStage.CommitStarted)]
    public async Task CancelBeforeCommit_ZeroTargetMutation(SafeWriteStage boundary)
    {
        var p=await Prepare(); using var cancel=new CancellationTokenSource();
        workflow.SafeWriteProbe=(stage,_)=>{ if(stage==boundary) cancel.Cancel(); };
        var result=await Apply(p,token:cancel.Token);
        Assert.Equal(SafeWriteOutcome.Cancelled,result.Outcome); Assert.False(result.Operation.CommitReached);
        Assert.Equal(p.BeforeHash,snapshots.ComputeSha256Hash(Target)); Assert.Empty(Directory.GetFiles(root,"*.tmp"));
    }
    [Fact]
    public async Task CancelDuringTransactionalCommit_RollsBackPrivateChanges()
    {
        var p=await Prepare(); using var cancel=new CancellationTokenSource();
        writer.SafeWriteProbe=stage=>{ if(stage=="BeforeCommit") cancel.Cancel(); };
        var result=await Apply(p,token:cancel.Token);
        Assert.Equal(SafeWriteOutcome.Cancelled,result.Outcome); Assert.Equal(p.BeforeHash,snapshots.ComputeSha256Hash(Target)); Assert.Empty(Directory.GetFiles(root,"*.tmp"));
    }
    [Fact]
    public async Task CancelAfterCommit_VerifiesCommittedStateRatherThanPretendingNoChange()
    {
        var p=await Prepare(); using var cancel=new CancellationTokenSource();
        workflow.SafeWriteProbe=(stage,_)=>{ if(stage==SafeWriteStage.Committed) cancel.Cancel(); };
        var result=await Apply(p,token:cancel.Token);
        Assert.Equal(SafeWriteOutcome.Success,result.Outcome); Assert.True(result.Operation.CommitReached); Assert.True(result.Operation.Verified);
        Assert.Equal(p.DesiredHash,snapshots.ComputeSha256Hash(Target));
    }
    [Fact]
    public async Task FaultAfterTemp_TargetIntact_BackupRetained_TempCleaned()
    {
        var p=await Prepare(); workflow.SafeWriteProbe=(stage,_)=>{ if(stage==SafeWriteStage.TempWritten) throw new IOException("synthetic"); };
        var result=await Apply(p);
        Assert.Equal(SafeWriteOutcome.CommitFailed,result.Outcome); Assert.False(result.Operation.CommitReached);
        Assert.Equal(p.BeforeHash,snapshots.ComputeSha256Hash(Target)); Assert.NotNull(result.Operation.BackupRef); Assert.Empty(Directory.GetFiles(root,"*.tmp"));
    }
    [Fact]
    public async Task VerificationFault_WithCommittedBytesIntact_RestoresAndVerifiesOriginal()
    {
        var p=await Prepare(); workflow.FailSafeVerification=true;
        var result=await Apply(p);
        Assert.Equal(SafeWriteOutcome.RecoverySucceeded,result.Outcome); Assert.True(result.Operation.CommitReached);
        Assert.Equal(SafeWriteRecovery.Restored,result.Operation.Recovery); Assert.False(result.Operation.Verified);
        Assert.Equal(p.BeforeHash,snapshots.ComputeSha256Hash(Target)); Assert.True(File.Exists(Path.Combine(root,result.Operation.BackupRef!)));
    }
    [Theory]
    [InlineData(SafeWriteStage.Committed)]
    [InlineData(SafeWriteStage.Recovering)]
    public async Task RecoveryConflict_NeverRestoresOverNewerExternalWork(SafeWriteStage boundary)
    {
        var p=await Prepare(); workflow.FailSafeVerification=true;
        workflow.SafeWriteProbe=(stage,path)=>{ if(stage==boundary) File.WriteAllText(path,"external newer C"); };
        var result=await Apply(p);
        Assert.Equal(SafeWriteOutcome.RecoveryConflict,result.Outcome); Assert.Equal("external newer C",File.ReadAllText(Target));
        Assert.Equal(p.BeforeHash,snapshots.ComputeSha256Hash(Path.Combine(root,result.Operation.BackupRef!)));
    }
    [Fact]
    public async Task TransactionEnlistment_BlocksExternalWriteAndReplacementInCriticalInterval()
    {
        var p=await Prepare(); var attempts=0;
        writer.SafeWriteProbe=stage=>
        {
            if(stage!="Enlisted") return;
            attempts++;
            Assert.Throws<IOException>(()=>File.WriteAllText(Target,"external"));
            var other=Path.Combine(root,"external.tmp"); File.WriteAllText(other,"external");
            Assert.Throws<IOException>(()=>File.Replace(other,Target,null)); File.Delete(other);
        };
        var result=await Apply(p); Assert.Equal(SafeWriteOutcome.Success,result.Outcome); Assert.Equal(1,attempts);
    }
    [Fact]
    public async Task Utf8BomAndLineEndings_ArePreservedIntentionally()
    {
        File.WriteAllText(Target,"hello\r\n",new UTF8Encoding(true)); var p=await Prepare(); var result=await Apply(p);
        Assert.Equal(SafeWriteOutcome.Success,result.Outcome); Assert.True(File.ReadAllBytes(Target).AsSpan().StartsWith(new byte[] { 0xef,0xbb,0xbf }));
        Assert.Equal("hello nexus\r\n",File.ReadAllText(Target));
    }
    [Fact]
    public async Task NoOp_DoesNotCommitOrRewrite()
    {
        var p=await Prepare("hello","hello"); var time=File.GetLastWriteTimeUtc(Target); var result=await Apply(p);
        Assert.Equal(SafeWriteOutcome.NoOp,result.Outcome); Assert.True(result.Operation.Verified); Assert.False(result.Operation.CommitReached);
        Assert.Equal(time,File.GetLastWriteTimeUtc(Target)); Assert.Single(Directory.GetFiles(root));
    }
    [Fact]
    public async Task AlreadyDesiredLiteral_IsIdempotent_WithFreshExpectedHash()
    {
        var first=await Prepare(); Assert.Equal(SafeWriteOutcome.Success,(await Apply(first)).Outcome);
        var second=await Prepare(); var time=File.GetLastWriteTimeUtc(Target);
        Assert.Equal(SafeWriteOutcome.NoOp,(await Apply(second)).Outcome);
        Assert.Equal("hello nexus\r\n",File.ReadAllText(Target)); Assert.Equal(time,File.GetLastWriteTimeUtc(Target));
    }
    [Fact]
    public async Task CancelHalfwayThroughTempWrite_TargetUnchangedAndTempRemoved()
    {
        var p=await Prepare(); using var cancel=new CancellationTokenSource();
        writer.SafeWriteProbe=stage=>{ if(stage=="TempHalfWritten") cancel.Cancel(); };
        var result=await Apply(p,token:cancel.Token);
        Assert.Equal(SafeWriteOutcome.Cancelled,result.Outcome); Assert.False(result.Operation.CommitReached);
        Assert.Equal(p.BeforeHash,snapshots.ComputeSha256Hash(Target)); Assert.Empty(Directory.GetFiles(root,"*.tmp"));
    }
    [Fact]
    public async Task CancelDuringRecovery_StillRestoresSafeState()
    {
        var p=await Prepare(); workflow.FailSafeVerification=true; using var cancel=new CancellationTokenSource();
        workflow.SafeWriteProbe=(stage,_)=>{ if(stage==SafeWriteStage.Recovering) cancel.Cancel(); };
        var result=await Apply(p,token:cancel.Token);
        Assert.Equal(SafeWriteOutcome.RecoverySucceeded,result.Outcome); Assert.True(result.Operation.CommitReached);
        Assert.Equal(p.BeforeHash,snapshots.ComputeSha256Hash(Target));
    }
    [Fact]
    public async Task ExactDiff_ShowsInvisibleLineEndingChange()
    {
        var p=await Prepare("\r\n","\n"); Assert.Contains("\\r\\n",p.Diff); Assert.Contains("\\n",p.Diff);
        Assert.Equal(SafeWriteOutcome.Success,(await Apply(p)).Outcome); Assert.Equal("hello\n",File.ReadAllText(Target));
    }
    [Fact]
    public async Task TemporaryFileTamperBeforeTransaction_IsNeverCommitted()
    {
        var p=await Prepare(); workflow.SafeWriteProbe=(stage,_)=>
        { if(stage==SafeWriteStage.CommitStarted) File.WriteAllText(Directory.GetFiles(root,"*.tmp").Single(),"tampered"); };
        var result=await Apply(p); Assert.Equal(SafeWriteOutcome.CommitFailed,result.Outcome);
        Assert.False(result.Operation.CommitReached); Assert.Equal(p.BeforeHash,snapshots.ComputeSha256Hash(Target));
    }
    [Fact]
    public async Task ExistingTemporaryArtifact_IsNeverOverwrittenOrDeleted()
    {
        var p=await Prepare(); var foreign=Path.Combine(root,".nexus-edit-"+p.OperationId.ToString("N")+".tmp");
        File.WriteAllText(foreign,"foreign work"); var result=await Apply(p);
        Assert.Equal(SafeWriteOutcome.TempWriteFailed,result.Outcome); Assert.Equal("foreign work",File.ReadAllText(foreign));
        Assert.Equal(p.BeforeHash,snapshots.ComputeSha256Hash(Target));
    }
    [Fact]
    public async Task CommitAuthorityRecheck_CanAbortPrivateTransactionWithoutDurableMutation()
    {
        var p=await Prepare(); var checks=0;
        var result=await workflow.ApplyApprovedSafeWriteAsync(p,WriteApprovalDecision.For(p,true),CancellationToken.None,
            authorizeCommit:()=>{ if(++checks==2) throw new OperationCanceledException(); });
        Assert.Equal(SafeWriteOutcome.Cancelled,result.Outcome); Assert.False(result.Operation.CommitReached);
        Assert.Equal(p.BeforeHash,snapshots.ComputeSha256Hash(Target));
    }
    [Fact]
    public async Task BinaryOrInvalidUtf8AndUnapprovedTypes_AreRejected()
    {
        foreach(var bytes in new[] { new byte[] { 0,1,2 },new byte[] { 0xff,0xfe,0x41 } })
        { File.WriteAllBytes(Target,bytes); await Assert.ThrowsAnyAsync<ArgumentException>(()=>Prepare()); }
        File.WriteAllText(Path.Combine(root,"source.cs"),"source");
        Assert.ThrowsAny<IOException>(()=>workspace.ResolveTarget("source.cs"));
    }
    [Fact]
    public async Task SizeAndMissingTarget_AreDenied()
    {
        File.WriteAllText(Target,new string('x',4097)); await Assert.ThrowsAnyAsync<IOException>(()=>Prepare());
        File.Delete(Target); await Assert.ThrowsAnyAsync<IOException>(()=>Prepare(hash:new string('a',64)));
    }
    public void Dispose()=>Directory.Delete(root,true);
}
