using System.Collections.Immutable;
using System.Diagnostics;
using System.Text;

namespace Nexus.AI.EditingLite.Services;

public partial class EditingWorkflowService
{
    internal Action<string,SourceEditPlan,int>? SourcePlanProbe { get; set; }
    internal SourceVerification SourceVerifier { get; }=new();
    public async Task<SourceEditPlan> PrepareSourcePlanAsync(SourceVerificationProfile profile,IEnumerable<SourceFileEdit> requested,Guid runId,string callId,
        SourcePlanLimits limits,SourceRunControl run)
    {
        run.Check(); var edits=requested.ToArray();
        if(!limits.IsValid || edits.Length is <2 or >3 || edits.Length>limits.MaxFiles || runId==Guid.Empty ||
            edits.Select(e=>e.Target).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=edits.Length || edits.Any(e=>e.Edits.IsDefaultOrEmpty || e.Edits.Length>2 || !profile.Targets.Contains(e.Target,StringComparer.Ordinal)) ||
            edits.Sum(e=>e.Edits.Length)>3 || edits.Any(e=>e.Edits.Any(x=>!ExactSourceEdits.ValidLiteral(x))) || ExactSourceEdits.LiteralBytes(edits.SelectMany(e=>e.Edits))>1024 || !profile.IsIntact())
            throw new ArgumentException("Invalid source plan/catalog mapping.");
        var entries=new List<SafeWriteProposal>();
        foreach(var edit in edits)
        {
            run.Check(); var p=await PrepareSourceWriteAsync(profile.Workspace,edit.Target,edit.BeforeHash,edit.Edits,runId,callId,new(),run.Cancellation);
            if(p.NoOp) throw new ArgumentException("First proof requires a real change per source."); entries.Add(p);
        }
        var diffBytes=entries.Sum(p=>Encoding.UTF8.GetByteCount(p.Diff));
        var changedLines=entries.Sum(p=>p.ChangedLines);
        if(diffBytes>limits.MaxDiffBytes || changedLines>limits.MaxChangedLines || entries.Sum(p=>p.Before.Encoding.GetPreamble().Length+p.Before.Encoding.GetByteCount(p.DesiredText))>limits.MaxFiles*4096)
            throw new ArgumentException("Source diff/size budget exceeded.");
        return new(runId,entries,limits,profile);
    }
    public async Task<SourcePlanResult> ExecuteSourcePlanAsync(SourceEditPlan plan,IWriteApprovalProvider approval,SourceRunControl run,
        Action<string,WriteOperationRecord?>? observe=null)
    {
        var timer=Stopwatch.StartNew(); var records=new List<WriteOperationRecord>(); SourceProcessResult? build=null,tests=null;
        var decisions=new List<WriteApprovalDecision>();
        SourcePlanApproval? approvedPlan=null;
        bool InsufficientBudget()=>run.MinimumVerificationBudget>TimeSpan.Zero && run.Remaining()<=run.MinimumVerificationBudget;
        SourcePlanResult Result(SourcePlanOutcome outcome,bool verified=false)
        {
            var baseline=outcome!=SourcePlanOutcome.Success && records.All(r=>!r.CommitReached || r.Recovery==SafeWriteRecovery.Restored) && VerifySourcePlanBaseline(plan);
            return new(plan.PlanId,plan.PlanDigest,outcome,records.ToImmutableArray(),build,tests,verified,timer.Elapsed)
            { ApprovedPlan=approvedPlan,BaselineVerified=baseline,RecoveredBaseDigest=baseline ? SourcePlanBaseDigest(plan) : null };
        }
        void Event(string name,int index=-1,WriteOperationRecord? operation=null)
        { observe?.Invoke(name,operation); SourcePlanProbe?.Invoke(name,plan,index); }
        SourcePlanOutcome Failure(SourcePlanOutcome outcome)
        {
            var conflict=false;
            for(var i=records.Count-1;i>=0;i--)
            {
                if(!records[i].CommitReached || records[i].Recovery==SafeWriteRecovery.Restored) continue;
                Event("SourceRecoveryStarted",i,records[i]);
                var recovered=RecoverApprovedSafeWrite(plan.Entries[i],records[i],decisions[i],(_,r)=>observe?.Invoke("SourceRecoveryProgress",r));
                records[i]=recovered.Operation;
                conflict|=recovered.Outcome!=SafeWriteOutcome.RecoverySucceeded;
                Event(recovered.Outcome==SafeWriteOutcome.RecoverySucceeded ? "SourceRecoveryCompleted" : "SourceRecoveryConflict",i,records[i]);
            }
            return conflict ? SourcePlanOutcome.RecoveryConflict : outcome;
        }
        try
        {
            run.Check(); if(!plan.Profile.IsIntact()) return Result(SourcePlanOutcome.PlanConflict);
            Event("SourceApprovalRequested");
            var decision=await approval.RequestPlanAsync(plan,run.Cancellation).WaitAsync(run.Cancellation);
            if(!decision.Approved || !decision.Matches(plan)) return Result(SourcePlanOutcome.ApprovalDenied);
            approvedPlan=decision;
            Event("SourceApprovalGranted"); run.Check();
            // Hermes shared precheck pattern: every approved original rechecked
            // before first commit. Aider prepare/dry-run, without fuzzy fallback.
            foreach(var entry in plan.Entries)
            { run.Check(); if(ReadScopedSource(entry.Workspace,entry.Target).Hash!=entry.BeforeHash || !ProposalContentMatches(entry)) return Result(SourcePlanOutcome.PlanConflict); }
            Event("SourcePreflightPassed");
            for(var i=0;i<plan.Entries.Length;i++)
            {
                run.Check(); if(!plan.Profile.IsIntact()) return Result(Failure(SourcePlanOutcome.PlanConflict));
                // Reserve existing process caps only before the first mutation; recovery is never cut short.
                if(i==0 && InsufficientBudget())
                    return Result(SourcePlanOutcome.InsufficientRemainingBudget);
                Event("SourceApplyStarted",i);
                var entry=plan.Entries[i];
                // C# derives constituent grant only from the exact validated plan;
                // not a second approval prompt, and not model self-approval.
                var grant=WriteApprovalDecision.For(entry,true) with { ApprovalId=decision.ApprovalId };
                decisions.Add(grant);
                var budgetDenied=false;
                void AuthorizeCommit()
                {
                    run.Check();
                    if(i==0 && InsufficientBudget()){budgetDenied=true;throw new OperationCanceledException("InsufficientRemainingBudget",run.Cancellation);}
                }
                var applied=await ApplyApprovedSafeWriteAsync(entry,grant,run.Cancellation,(_,r)=>observe?.Invoke("SourceFileStage",r),AuthorizeCommit);
                records.Add(applied.Operation); Event("SourceApplyCompleted",i,applied.Operation);
                if(budgetDenied)return Result(Failure(SourcePlanOutcome.InsufficientRemainingBudget));
                if(applied.Outcome!=SafeWriteOutcome.Success)
                    return Result(Failure(applied.Outcome==SafeWriteOutcome.Conflict ? SourcePlanOutcome.PlanConflict : applied.Outcome==SafeWriteOutcome.Cancelled ? SourcePlanOutcome.Cancelled : SourcePlanOutcome.ApplyFailed));
            }
            run.Check(); if(!Desired(plan)) return Result(Failure(SourcePlanOutcome.VerificationConflict));
            var sourceHashes=plan.Profile.InitialSourceHashes.ToDictionary(p=>p.Key,p=>p.Value);
            foreach(var entry in plan.Entries) sourceHashes[entry.Target]=entry.DesiredHash;
            Event("SourceBuildStarted"); SourceVerification.BuildReceipt receipt;
            using(plan.Profile.LockSources(sourceHashes)) receipt=await SourceVerifier.BuildAsync(plan.Profile,run,plan);
            build=receipt.Result; Event("SourceBuildCompleted");
            if(!build.Passed) return Result(Failure(build.Category=="Cancelled" ? SourcePlanOutcome.Cancelled : build.Category=="ProcessDeadline" ? SourcePlanOutcome.DeadlineExceeded : SourcePlanOutcome.BuildFailed));
            run.Check(); if(!Desired(plan)) return Result(Failure(SourcePlanOutcome.VerificationConflict));
            Event("SourceTestStarted"); using(plan.Profile.LockSources(sourceHashes)) tests=await SourceVerifier.TestAsync(plan.Profile,run,receipt); Event("SourceTestCompleted");
            if(!tests.Passed) return Result(Failure(tests.Category=="Cancelled" ? SourcePlanOutcome.Cancelled : tests.Category=="ProcessDeadline" ? SourcePlanOutcome.DeadlineExceeded : SourcePlanOutcome.TestFailed));
            run.Check(); if(!Desired(plan) || !plan.Profile.IsIntact()) return Result(Failure(SourcePlanOutcome.VerificationConflict));
            Event("SourcePlanVerified"); return Result(SourcePlanOutcome.Success,true);
        }
        catch(OperationCanceledException) { return Result(Failure(run.Cancellation.IsCancellationRequested ? SourcePlanOutcome.Cancelled : SourcePlanOutcome.DeadlineExceeded)); }
        catch { return Result(Failure(records.Count==0 ? SourcePlanOutcome.PlanConflict : SourcePlanOutcome.ApplyFailed)); }
    }
    public bool VerifySourcePlanState(SourceEditPlan plan,SourcePlanResult result)=>result.PlanId==plan.PlanId && result.PlanDigest==plan.PlanDigest &&
        result.Outcome==SourcePlanOutcome.Success && result.FinalVerified && result.Build?.Passed==true && result.Tests?.Passed==true && result.Tests.TestsPassed==3 &&
        result.Operations.Length==plan.Entries.Length && result.Operations.All(r=>r.CommitReached && r.Verified && r.Recovery==SafeWriteRecovery.NotNeeded) &&
        plan.Profile.IsIntact() && Desired(plan);
    public SourcePlanResult RecoverSourcePlanAfterFinalFailure(SourceEditPlan plan,SourcePlanResult result)
    {
        if(result.PlanId!=plan.PlanId || result.PlanDigest!=plan.PlanDigest || result.ApprovedPlan is not { Approved:true } approval || !approval.Matches(plan) ||
            result.Operations.Length!=plan.Entries.Length)
            throw new ArgumentException("Approved source plan recovery facts required.");
        var records=result.Operations.ToArray(); var conflict=false;
        for(var i=records.Length-1;i>=0;i--)
        {
            if(!records[i].CommitReached || records[i].Recovery==SafeWriteRecovery.Restored) continue;
            var grant=WriteApprovalDecision.For(plan.Entries[i],true) with { ApprovalId=approval.ApprovalId };
            var recovered=RecoverApprovedSafeWrite(plan.Entries[i],records[i],grant);
            records[i]=recovered.Operation;
            conflict|=recovered.Outcome!=SafeWriteOutcome.RecoverySucceeded;
        }
        var baseline=!conflict && VerifySourcePlanBaseline(plan);
        return result with { Outcome=conflict || !baseline ? SourcePlanOutcome.RecoveryConflict : SourcePlanOutcome.VerificationConflict,
            Operations=records.ToImmutableArray(),FinalVerified=false,BaselineVerified=baseline,RecoveredBaseDigest=baseline ? SourcePlanBaseDigest(plan) : null };
    }
    public bool VerifySourcePlanBaseline(SourceEditPlan plan)
    {
        try
        {
            if(!plan.Profile.IsIntact()) return false;
            foreach(var source in plan.Profile.InitialSourceHashes)
            {
                using var lease=new SafePathLease(plan.Profile.Root,source.Key); using var stream=lease.OpenTarget();
                var expected=plan.Entries.FirstOrDefault(p=>p.Target==source.Key)?.BeforeHash ?? source.Value;
                if(FileWriterService.Hash(stream)!=expected) return false;
            }
            return true;
        }
        catch { return false; }
    }
    // Approval and original proposal facts are checked without creating authority.
    public bool VerifyApprovedRecoveredSourcePlan(SourceEditPlan plan,SourcePlanResult result)
    {
        try
        {
            return result.HasMatchingApproval(plan) && result.BaselineVerified && result.RecoveredBaseDigest==SourcePlanBaseDigest(plan) &&
                result.Operations.Length==plan.Entries.Length && result.Operations.All(r=>!r.CommitReached || r.Recovery==SafeWriteRecovery.Restored) &&
                plan.Entries.All(p=>Hash(Encode(p.Before.Encoding,p.Before.Content))==p.BeforeHash &&
                    Hash(Encode(p.Before.Encoding,p.DesiredText))==p.DesiredHash && ProposalContentMatches(p)) && VerifySourcePlanBaseline(plan);
        }
        catch { return false; }
    }
    public static string SourcePlanBaseDigest(SourceEditPlan plan)=>StateDigest(plan,false);
    public static string SourcePlanFailedDigest(SourceEditPlan plan)=>StateDigest(plan,true);
    private static string StateDigest(SourceEditPlan plan,bool desired)=>Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(
        System.Text.Json.JsonSerializer.Serialize(new { plan.Profile.Digest,files=plan.Profile.InitialSourceHashes.OrderBy(p=>p.Key,StringComparer.Ordinal).Select(p=>new
        { target=p.Key,hash=plan.Entries.FirstOrDefault(e=>e.Target==p.Key) is { } entry ? desired ? entry.DesiredHash : entry.BeforeHash : p.Value }) })))).ToLowerInvariant();
    private bool Desired(SourceEditPlan plan)
    { try { return plan.Entries.All(p=>ReadScopedSource(p.Workspace,p.Target).Hash==p.DesiredHash) && plan.Profile.InitialSourceHashes.Where(p=>plan.Entries.All(e=>e.Target!=p.Key)).All(p=>SourceVerificationProfile.HashFile(SafePath.ResolveRelative(plan.Profile.Root,p.Key))==p.Value); } catch { return false; } }
}
