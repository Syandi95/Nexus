using System.Text.Json.Serialization;

namespace Nexus.AI.EditingLite.Services;

public sealed class ApprovedTextWorkspace
{
    private readonly HashSet<string>? sourceTargets;
    public string? Root { get; }
    public Guid PolicyId { get; }=Guid.NewGuid();
    public bool IsSourceScope=>sourceTargets is not null;
    public ApprovedTextWorkspace(string? root=null)
    {
        if(root is null) return;
        if(!Path.IsPathFullyQualified(root) || !Directory.Exists(root)) throw new ArgumentException("Existing explicit workspace root required.");
        Root=Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        if(Root.Length>512) throw new ArgumentException("Workspace path limit exceeded.");
        SafePath.ResolveRelative(Root,"scope-check.txt");
    }
    private ApprovedTextWorkspace(string root,IEnumerable<string> targets) : this(root)
    {
        var names=targets.ToArray(); sourceTargets=new(names,StringComparer.OrdinalIgnoreCase);
        if(names.Length is <2 or >3 || sourceTargets.Count!=names.Length || names.Any(t=>Path.GetExtension(t)!=".cs")) throw new ArgumentException("Exact 2-3 C# targets required.");
        foreach(var target in names) SafePath.ResolveRelative(Root!,target);
    }
    public static ApprovedTextWorkspace ForSourceFiles(string root,IEnumerable<string> exactTargets)=>new(root,exactTargets);
    public string ResolveTarget(string relative,int maxBytes=4096)
    {
        if(Root is null || (sourceTargets is null ? !string.Equals(Path.GetExtension(relative),".txt",StringComparison.OrdinalIgnoreCase) : !sourceTargets.Contains(relative))) throw new IOException("Target not authorized.");
        using var lease=new SafePathLease(Root,relative);
        using var target=lease.OpenTarget();
        if(target.Length>maxBytes) throw new IOException("Target size denied.");
        return lease.TargetPath;
    }
}

public sealed record SafeWriteLimits(int MaxWriteInputBytes=1024,int MaxTargetFileBytes=4096)
{
    public bool IsValid=>MaxWriteInputBytes is >=1 and <=1024 && MaxTargetFileBytes is >=1 and <=4096;
}
public enum SafeWriteStage { Prepared, Approved, PreconditionChecked, BackupCreated, TempWritten, CommitStarted, Committed, Verified, Recovering, Recovered }
public enum SafeWriteOutcome { Success, NoOp, ApprovalDenied, Conflict, PathDenied, InvalidArguments, BackupFailed, TempWriteFailed, CommitFailed, VerificationFailed, RecoverySucceeded, RecoveryConflict, Cancelled }
public enum SafeWriteRecovery { NotNeeded, Restored, Conflict, Failed }

public sealed record SafeWriteProposal(Guid OperationId,Guid RunId,string ToolCallId,string Target,string BeforeHash,string DesiredHash,
    string DiffHash,string Diff,DateTimeOffset CreatedAt,
    [property: JsonIgnore] ApprovedTextWorkspace Workspace,
    [property: JsonIgnore] FileSnapshot Before,
    [property: JsonIgnore] string DesiredText,
    [property: JsonIgnore] SafeWriteLimits Limits,
    [property: JsonIgnore] string OldText,
    [property: JsonIgnore] string NewText)
{
    [JsonIgnore] public System.Collections.Immutable.ImmutableArray<ResolvedSourceTextEdit> ResolvedEdits { get; init; }=[];
    public int ChangedLines=>ResolvedEdits.IsDefaultOrEmpty ? OldText.Split('\n').Length+NewText.Split('\n').Length : ExactSourceEdits.ChangedLines(ResolvedEdits);
    public int LiteralBytes=>ResolvedEdits.IsDefaultOrEmpty ? System.Text.Encoding.UTF8.GetByteCount(OldText)+System.Text.Encoding.UTF8.GetByteCount(NewText) : ExactSourceEdits.LiteralBytes(ResolvedEdits.Select(e=>new SourceTextEdit(e.OldText,e.NewText)));
    public Guid PolicyId=>Workspace.PolicyId;
    public string Risk=>"SafeWrite";
    public bool NoOp=>BeforeHash==DesiredHash;
}
public sealed record WriteApprovalDecision(bool Approved,Guid ApprovalId,Guid OperationId,Guid PolicyId,string Target,
    string BeforeHash,string DesiredHash,string DiffHash,Guid RunId,string ToolCallId,int MaxWriteInputBytes,int MaxTargetFileBytes)
{
    public static WriteApprovalDecision For(SafeWriteProposal p,bool approve)=>new(approve,Guid.NewGuid(),p.OperationId,p.PolicyId,p.Target,p.BeforeHash,p.DesiredHash,p.DiffHash,p.RunId,p.ToolCallId,p.Limits.MaxWriteInputBytes,p.Limits.MaxTargetFileBytes);
    public bool Matches(SafeWriteProposal p)=>ApprovalId!=Guid.Empty && OperationId==p.OperationId && PolicyId==p.PolicyId && Target==p.Target &&
        BeforeHash==p.BeforeHash && DesiredHash==p.DesiredHash && DiffHash==p.DiffHash && RunId==p.RunId && ToolCallId==p.ToolCallId &&
        MaxWriteInputBytes==p.Limits.MaxWriteInputBytes && MaxTargetFileBytes==p.Limits.MaxTargetFileBytes;
}
public interface IWriteApprovalProvider
{
    Task<WriteApprovalDecision> RequestAsync(SafeWriteProposal exactProposal,CancellationToken token);
    Task<SourcePlanApproval> RequestPlanAsync(SourceEditPlan exactPlan,CancellationToken token)=>Task.FromResult(SourcePlanApproval.For(exactPlan,false));
}
public sealed class DenyWriteApprovalProvider : IWriteApprovalProvider
{
    public Task<WriteApprovalDecision> RequestAsync(SafeWriteProposal p,CancellationToken token)
    { token.ThrowIfCancellationRequested(); return Task.FromResult(WriteApprovalDecision.For(p,false)); }
}
public sealed record WriteOperationRecord(Guid OperationId,Guid RunId,string ToolCallId,string Target,string BeforeHash,string DesiredHash,
    Guid? ApprovalId=null,string? BackupRef=null,bool CommitReached=false,bool Verified=false,string? ActualHash=null,
    SafeWriteRecovery Recovery=SafeWriteRecovery.NotNeeded,SafeWriteStage Stage=SafeWriteStage.Prepared,DateTimeOffset? Timestamp=null,Guid? PolicyId=null);
public sealed record SafeWriteResult(SafeWriteOutcome Outcome,WriteOperationRecord Operation);
