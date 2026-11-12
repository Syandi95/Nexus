using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Nexus.AI.EditingLite.Services;

public sealed record SourceFileEdit(string Target,string BeforeHash,ImmutableArray<SourceTextEdit> Edits)
{
    public SourceFileEdit(string target,string beforeHash,IEnumerable<SourceTextEdit> edits) : this(target,beforeHash,edits.ToImmutableArray()) { }
    // Deterministic internal one-item construction compatibility, not a second tool dialect.
    public SourceFileEdit(string target,string beforeHash,string oldText,string newText) : this(target,beforeHash,ImmutableArray.Create(new SourceTextEdit(oldText,newText))) { }
    [System.Text.Json.Serialization.JsonIgnore] public string OldText=>Edits.Single().OldText;
    [System.Text.Json.Serialization.JsonIgnore] public string NewText=>Edits.Single().NewText;
}
public sealed record SourcePlanLimits(int MaxFiles=3,int MaxDiffBytes=2048,int MaxChangedLines=6)
{
    public bool IsValid=>MaxFiles is >=2 and <=3 && MaxDiffBytes is >=1 and <=2048 && MaxChangedLines is >=1 and <=6;
}
public sealed class SourceEditPlan
{
    public Guid PlanId { get; }=Guid.NewGuid();
    public Guid RunId { get; }
    public Guid WorkspaceIdentity=>Entries[0].PolicyId;
    public string VerificationProfileId=>Profile.Id;
    public ImmutableArray<SafeWriteProposal> Entries { get; }
    public SourcePlanLimits Limits { get; }
    public SourceVerificationProfile Profile { get; }
    public string PlanDigest { get; }
    public int EditCount=>Entries.Sum(e=>e.ResolvedEdits.Length);
    public int LiteralBytes=>Entries.Sum(e=>e.LiteralBytes);
    public int DesiredBytes=>Entries.Sum(e=>e.Before.Encoding.GetPreamble().Length+e.Before.Encoding.GetByteCount(e.DesiredText));
    public int DiffBytes=>Entries.Sum(e=>Encoding.UTF8.GetByteCount(e.Diff));
    public int ChangedLines=>Entries.Sum(e=>e.ChangedLines);
    internal SourceEditPlan(Guid runId,IEnumerable<SafeWriteProposal> entries,SourcePlanLimits limits,SourceVerificationProfile profile)
    {
        RunId=runId; Entries=entries.ToImmutableArray(); Limits=limits; Profile=profile;
        PlanDigest=ComputeDigest();
    }
    internal string ComputeDigest()=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
    {
        PlanId,RunId,WorkspaceIdentity,VerificationProfileId,Profile.Digest,Limits,EditCount,LiteralBytes,DesiredBytes,DiffBytes,ChangedLines,
        files=Entries.Select(p=>new { p.OperationId,p.RunId,p.ToolCallId,p.Target,p.BeforeHash,p.DesiredHash,p.DiffHash,p.Diff,p.Limits,
            editCount=p.ResolvedEdits.Length,edits=p.ResolvedEdits,p.LiteralBytes,p.ChangedLines,desiredBytes=p.Before.Encoding.GetPreamble().Length+p.Before.Encoding.GetByteCount(p.DesiredText) })
    })))).ToLowerInvariant();
}
public sealed record SourcePlanApproval(bool Approved,Guid ApprovalId,Guid PlanId,Guid RunId,Guid WorkspaceIdentity,string VerificationProfileId,string PlanDigest)
{
    public static SourcePlanApproval For(SourceEditPlan p,bool approve)=>new(approve,Guid.NewGuid(),p.PlanId,p.RunId,p.WorkspaceIdentity,p.VerificationProfileId,p.PlanDigest);
    public bool Matches(SourceEditPlan p)=>ApprovalId!=Guid.Empty && PlanId==p.PlanId && RunId==p.RunId && WorkspaceIdentity==p.WorkspaceIdentity &&
        VerificationProfileId==p.VerificationProfileId && PlanDigest==p.PlanDigest && p.ComputeDigest()==PlanDigest;
}
public enum SourcePlanOutcome { Success, ApprovalDenied, PlanConflict, ApplyFailed, BuildFailed, TestFailed, VerificationConflict, RecoveryConflict, Cancelled, DeadlineExceeded, InsufficientRemainingBudget }
public sealed record RequiredAssertionFailure(string RequiredTestName,string LogicalSourcePath,int Expected,int Actual)
{
    public string BoundedMessage=>$"Assert.Equal failed: expected {Expected}, actual {Actual}.";
}
public sealed record SourceProcessResult(bool Passed,string Category,int? ExitCode,int OwnedPid,TimeSpan Elapsed,string Output,bool Truncated,int TestsPassed=0,
    ImmutableArray<RequiredAssertionFailure> AssertionFailures=default)
{
    public ImmutableArray<RequiredAssertionFailure> AssertionFailures { get; init; }=AssertionFailures.IsDefault ? [] : AssertionFailures;
    public CompilerFailureReceipt? CompilerFailure { get; internal init; }
    [System.Text.Json.Serialization.JsonIgnore] internal string? CompilerArtifactError { get; init; }
}
public sealed record SourcePlanResult(Guid PlanId,string PlanDigest,SourcePlanOutcome Outcome,ImmutableArray<WriteOperationRecord> Operations,
    SourceProcessResult? Build,SourceProcessResult? Tests,bool FinalVerified,TimeSpan Elapsed)
{
    [System.Text.Json.Serialization.JsonIgnore]
    internal SourcePlanApproval? ApprovedPlan { get; init; }
    // Read-only approval integrity query; callers cannot mint the stored grant.
    public bool HasMatchingApproval(SourceEditPlan plan)=>PlanId==plan.PlanId && PlanDigest==plan.PlanDigest &&
        ApprovedPlan is { Approved:true } approval && approval.Matches(plan);
    public bool BaselineVerified { get; init; }
    public string? RecoveredBaseDigest { get; init; }
}

// Same run-local monotonic authority passed by Agent; no fresh deadline per stage.
public sealed record SourceRunControl(Func<TimeSpan> Remaining,CancellationToken Cancellation,Action CheckIdentity,Action<string>? BeforeProcessStart=null,int AttemptNumber=1)
{
    public TimeSpan MinimumVerificationBudget { get; init; }
    internal void Check()
    { Cancellation.ThrowIfCancellationRequested(); if(Remaining()<=TimeSpan.Zero) throw new OperationCanceledException(Cancellation); CheckIdentity(); }
}
