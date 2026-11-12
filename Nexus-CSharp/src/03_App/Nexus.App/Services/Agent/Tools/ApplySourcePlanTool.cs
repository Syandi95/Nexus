using System.Text.Json;
using System.Collections.Immutable;
using System.Diagnostics;
using Nexus.AI.EditingLite.Services;
using Nexus.Foundation.Logging;

namespace Nexus.App.Services.Agent.Tools;

// Disabled production catalog by default. Only a host-reviewed fixture profile
// can enable source authority; no paths/commands/profile configuration from model.
public sealed class SourcePlanCatalog(SourceVerificationProfile? profile=null)
{
    public SourceVerificationProfile? Profile { get; }=profile;
    private SourceRepairProbeReceipt? repair;
    internal void EnableRepair(SourceRepairProbeReceipt proof)
    {
        if(Profile is null || proof.ProfileDigest!=Profile.Digest || !proof.Adequate || proof.ElapsedSeconds is <=0 or >=120 || proof.PromptTokens<=0 || proof.CompletionTokens is <=0 or >256)
            throw new ArgumentException("Measured read-only repair prerequisite required.");
        repair=proof;
    }
    internal bool RepairAvailable(AgentRunContext run)=>repair is not null && Profile?.Digest==repair.ProfileDigest && run.Model?.ModelId==repair.ModelId &&
        run.Model.ModelPath==repair.ModelPath && run.Limits?.MaxSourcePlans==2 && run.Limits.MaxRepairPlans==1 && run.Limits.MaxRunDuration==TimeSpan.FromSeconds(120);
    internal bool CompilerRepairAvailable(AgentRunContext run)=>RepairAvailable(run) && Profile!.CompilerObservationEnabled && repair!.CompilerAdequate;
}
public sealed class ApplySourcePlanTool(SourcePlanCatalog catalog,EditingWorkflowService editing,IWriteApprovalProvider approval,ILoggerService logger) : INexusTool
{
    public const string ToolName="nexus_apply_source_plan";
    public ToolDescriptor Descriptor { get; }=new(ToolName,"Propose original-snapshot exact edit arrays for the host's two or three existing C# files. One or two edits per file, three total. One independent plan approval; C# applies, builds, tests and verifies. No commands, paths, approvals or budgets from model.",
        ToolRisk.SourcePlan,false,TimeSpan.FromSeconds(120),4096,SourcePlanArgumentValidator.Schema);
    internal bool UsesPilotDeadline(AgentRunContext run)=>run.ProposalScope is {} scope && scope.ProfileDigest==catalog.Profile?.Digest &&
        run.Limits is {} limits && scope.AllowsExtendedPilot(limits);
    public Task<ToolExecutionResult> ExecuteAsync(ToolExecutionContext ctx,JsonElement args,CancellationToken token)
    {
        var run=ctx.Run; var progress=run.Progress!; var intent=run.SourceIntent!; var profile=catalog.Profile;
        // Raw JSON is never the executable repair entry point.
        if(profile is null || profile.Id!=intent.ProfileId || progress.SourcePlanCount!=1 || progress.SourceResult is not null || progress.RepairPlanning)
            return Task.FromResult(new ToolExecutionResult(ToolOutcome.Denied,ReasonCode:"SourcePlanAuthorityDenied"));
        var edits=new List<SourceFileEdit>();
        for(var i=0;i<intent.Files.Length;i++)
        {
            var prefix=((char)('a'+i)).ToString();
            if(!args.TryGetProperty(prefix,out var slot))
                return Task.FromResult(new ToolExecutionResult(ToolOutcome.InvalidArguments,ReasonCode:"SourceIntentMismatch"));
            var proposed=SourcePlanArgumentValidator.Read(slot);
            if(!ExactSourceEdits.SameIntent(proposed,intent.Files[i].Edits)) return Task.FromResult(new ToolExecutionResult(ToolOutcome.InvalidArguments,ReasonCode:"SourceIntentMismatch"));
            edits.Add(intent.Files[i]);
        }
        if(intent.Files.Length==2 && args.TryGetProperty("c",out _)) return Task.FromResult(new ToolExecutionResult(ToolOutcome.InvalidArguments,ReasonCode:"UnapprovedThirdTarget"));
        return ExecuteAuthoritativePlanAsync(ctx,edits,false,token);
    }

    internal bool TryCaptureApprovedA(AgentRunContext run,out SourceAttemptFact? first,out int failed,out string reason)
    {
        first=null;failed=-1;reason="ApprovedALineageMismatch";
        var p=run.Progress;var profile=catalog.Profile;var intent=run.SourceIntent;
        if(p is null || p.Attempts.Count!=1 || profile is null || intent is null) return false;
        var a=p.Attempts[0];
        if(a.RunId!=run.RunId || a.AttemptNumber!=1 || a.ParentPlanId is not null || a.Plan.RunId!=run.RunId || a.PlanId!=a.Plan.PlanId ||
            a.PlanDigest!=a.Plan.PlanDigest || a.Plan.Profile.Digest!=profile.Digest || a.Plan.VerificationProfileId!=intent.ProfileId ||
            !ReferenceEquals(p.SourcePlan,a.Plan) || !ReferenceEquals(p.SourceResult,a.Result) ||
            !a.Plan.Entries.Select(e=>e.Target).SequenceEqual(intent.Files.Select(e=>e.Target),StringComparer.Ordinal) ||
            a.Plan.Entries.Where((e,i)=>e.BeforeHash!=intent.Files[i].BeforeHash || !ExactSourceEdits.SameIntent(e.ResolvedEdits.Select(x=>new SourceTextEdit(x.OldText,x.NewText)),intent.Files[i].Edits)).Any() ||
            a.BaselineDigest!=EditingWorkflowService.SourcePlanBaseDigest(a.Plan) || a.VerificationOutcome!=a.Result.Outcome.ToString() ||
            !a.Result.HasMatchingApproval(a.Plan)) return false;
        if(!SourceRepairFeedback.EligibleFailure(run)) { reason="IneligibleRepairFailure";return false; }
        var targets=a.Result.Build?.CompilerFailure is { } compiler ? new[] { compiler.LogicalSourcePath } : a.Result.Tests!.AssertionFailures.Select(f=>f.LogicalSourcePath).Distinct(StringComparer.Ordinal).ToArray();
        if(targets.Length!=1) { reason="AmbiguousFailedSource";return false; }
        failed=Array.FindIndex(a.Plan.Entries.ToArray(),e=>e.Target==targets[0]);if(failed<0) { reason="UnapprovedFailedSource";return false; }
        if(!editing.VerifyApprovedRecoveredSourcePlan(a.Plan,a.Result)) { reason="ApprovedAOrBaselineMismatch";return false; }
        first=a;reason="ApprovedRecoveredA";return true;
    }

    internal bool TryValidateRepair(AgentRunContext run,string raw,out ValidatedSourceRepairInput? input,out string reason)
    {
        input=null;
        if(!TryCaptureApprovedA(run,out var first,out var failed,out reason)) return false;
        return SourceRepairInputValidator.TryValidate(raw,first!,failed,out input,out reason);
    }

    internal bool TryComposeRepair(AgentRunContext run,ValidatedSourceRepairInput input,out ImmutableArray<SourceFileEdit> edits,out string reason)
    {
        edits=[];var watch=Stopwatch.StartNew();
        if(!TryCaptureApprovedA(run,out var first,out var failed,out reason)) return false;
        if(input.RunId!=run.RunId || input.APlanId!=first!.PlanId || input.APlanDigest!=first.PlanDigest || input.BaselineDigest!=first.BaselineDigest ||
            input.FailedOrdinal!=failed || input.FailedTarget!=first.Plan.Entries[failed].Target) { reason="RepairProjectionIdentityMismatch";return false; }
        var candidate=ImmutableArray.CreateBuilder<SourceFileEdit>();
        for(var i=0;i<first.Plan.Entries.Length;i++)
        {
            var entry=first.Plan.Entries[i];var intended=entry.ResolvedEdits.Select(e=>new SourceTextEdit(e.OldText,e.NewText)).ToImmutableArray();
            candidate.Add(new(entry.Target,entry.BeforeHash,i==failed ? input.FailedEdits : intended));
            if(i!=failed && !ExactSourceEdits.SameIntent(candidate[i].Edits,intended)) { reason="PreservedAIntentMismatch";return false; }
        }
        edits=candidate.ToImmutable();reason="HostComposedRepair";
        logger.LogInfo("AgentSourceRepairProjection",JsonSerializer.Serialize(new { run.RunId,input.APlanId,input.APlanDigest,input.FailedTarget,
            failedEditCount=input.FailedEdits.Length,advisory=input.AdvisoryExclusions,compositionMs=watch.Elapsed.TotalMilliseconds }));
        return true;
    }

    internal Task<ToolExecutionResult> ExecuteValidatedRepairAsync(ToolExecutionContext ctx,ValidatedSourceRepairInput input,CancellationToken token)
    {
        var p=ctx.Run.Progress!;
        if(p.SourcePlanCount!=2 || p.RepairPlanCount!=1 || !p.RepairPlanning || !catalog.RepairAvailable(ctx.Run))
            return Task.FromResult(new ToolExecutionResult(ToolOutcome.Denied,ReasonCode:"RepairAuthorityDenied"));
        if(!TryComposeRepair(ctx.Run,input,out var edits,out var reason)) return Task.FromResult(new ToolExecutionResult(ToolOutcome.Conflict,ReasonCode:reason));
        return ExecuteAuthoritativePlanAsync(ctx,edits,true,token);
    }

    private async Task<ToolExecutionResult> ExecuteAuthoritativePlanAsync(ToolExecutionContext ctx,IEnumerable<SourceFileEdit> edits,bool repairing,CancellationToken token)
    {
        var run=ctx.Run;var progress=run.Progress!;var profile=catalog.Profile!;
        var control=new SourceRunControl(()=>run.RemainingDuration,token,()=>
        {
            run.Cancellation.ThrowIfCancellationRequested();
            run.CheckIdentity?.Invoke();
        },stage=>
        {
            if(stage=="Build") { if(progress.BuildStartCount>=run.Limits!.MaxBuildStarts) throw new ToolProtocolException("BudgetFailure"); progress.BuildStartCount++; }
            else { if(progress.TestStartCount>=run.Limits!.MaxTestStarts) throw new ToolProtocolException("BudgetFailure"); progress.TestStartCount++; }
        },AttemptNumber:progress.SourcePlanCount)
        { MinimumVerificationBudget=UsesPilotDeadline(run)?profile.BuildTimeout+profile.TestTimeout:TimeSpan.Zero };
        try
        {
            var plan=await editing.PrepareSourcePlanAsync(profile,edits,run.RunId,run.ActiveToolCallId!,new(),control);
            var bytes=plan.Entries.Sum(e=>System.Text.Encoding.UTF8.GetByteCount(e.Diff));
            var lines=plan.Entries.Sum(e=>e.ChangedLines);
            if(progress.TotalDiffBytes+bytes>4096 || progress.TotalChangedLines+lines>12) return new(ToolOutcome.Denied,ReasonCode:"BudgetFailure");
            progress.TotalDiffBytes+=bytes; progress.TotalChangedLines+=lines;
            progress.SourcePlan=plan; progress.RepairPlanning=false;
            AgentRunEvents.Emit(logger,run,repairing ? "RepairPlanPrepared" : "AttemptStarted",outcome:plan.PlanId.ToString("D"));
            progress.SourceResult=await editing.ExecuteSourcePlanAsync(plan,approval,control,(kind,operation)=>
            {
                if(kind=="SourceApprovalRequested") AgentRunEvents.State(logger,run,AgentRunState.AwaitingApproval);
                if(kind=="SourceApprovalRequested" || kind=="SourceApprovalGranted") AgentRunEvents.Emit(logger,run,
                    repairing ? kind=="SourceApprovalRequested" ? "RepairApprovalRequested" : "RepairApprovalGranted" : kind);
                if(kind.Contains("Recovery")) AgentRunEvents.State(logger,run,AgentRunState.Recovering);
                if(kind.Contains("Build") || kind.Contains("Test") || kind=="SourcePlanVerified") AgentRunEvents.State(logger,run,AgentRunState.Verifying);
                logger.LogInfo("AgentSourcePlan",JsonSerializer.Serialize(new { eventName=kind,run.RunId,toolCallId=run.ActiveToolCallId,plan.PlanId,plan.PlanDigest,
                    operation?.OperationId,operation?.Target,operation?.BeforeHash,operation?.DesiredHash,operation?.CommitReached,operation?.Recovery,elapsedMs=progress.Elapsed.TotalMilliseconds }));
            });
            var result=progress.SourceResult;
            progress.RecordSourceAttempt(SourceAttemptFact.For(run,plan,result));
            if(result.Outcome!=SourcePlanOutcome.Success) AgentRunEvents.Emit(logger,run,"AttemptVerificationFailed",outcome:result.Outcome.ToString());
            if(result.BaselineVerified) AgentRunEvents.Emit(logger,run,"RecoveryBaselineVerified",outcome:result.RecoveredBaseDigest);
            var outcome=result.Outcome switch
            {
                SourcePlanOutcome.Success=>ToolOutcome.Success,
                SourcePlanOutcome.ApprovalDenied=>ToolOutcome.ApprovalDenied,
                SourcePlanOutcome.InsufficientRemainingBudget=>ToolOutcome.Denied,
                SourcePlanOutcome.PlanConflict or SourcePlanOutcome.VerificationConflict=>ToolOutcome.Conflict,
                SourcePlanOutcome.RecoveryConflict=>ToolOutcome.RecoveryConflict,
                SourcePlanOutcome.Cancelled=>ToolOutcome.Cancelled,
                SourcePlanOutcome.DeadlineExceeded=>ToolOutcome.Timeout,
                _=>ToolOutcome.VerificationFailed
            };
            var data=System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(new { planId=plan.PlanId.ToString("D"),planDigest=plan.PlanDigest,verified=result.FinalVerified,
                outcome=result.Outcome.ToString(),files=result.Operations.Select(o=>new { o.Target,o.BeforeHash,o.DesiredHash,o.CommitReached,o.Recovery }),
                build=result.Build?.Category,tests=result.Tests?.Category,testsPassed=result.Tests?.TestsPassed }))!;
            if(CanRepair(run)) data["repair"]=JsonSerializer.SerializeToNode(SourceRepairFeedback.Create(run,editing));
            else if(result.Outcome!=SourcePlanOutcome.Success) AgentRunEvents.Emit(logger,run,"RepairDenied",outcome:progress.SourcePlanCount==2 ? "NoPlanC" : result.Outcome.ToString());
            var response=new ToolExecutionResult(outcome,JsonSerializer.SerializeToElement(data),outcome==ToolOutcome.Success ? null : result.Outcome.ToString());
            if(run.Limits!.MaxRepairPlans==1 && System.Text.Encoding.UTF8.GetByteCount(ToolExecutor.SerializeResult(response))>3072)
                throw new ToolProtocolException("ToolResultTooLarge");
            return response;
        }
        catch(ArgumentException) { return new(ToolOutcome.InvalidArguments,ReasonCode:"InvalidSourcePlan"); }
        catch(SafeWriteConflictException) { return new(ToolOutcome.Conflict,ReasonCode:"SourceSnapshotConflict"); }
    }
    internal bool Verify(AgentRunContext run)=>run.Progress?.SourcePlan is SourceEditPlan plan && run.Progress.SourceResult is SourcePlanResult result && editing.VerifySourcePlanState(plan,result);
    internal bool CanRepair(AgentRunContext run)=>catalog.RepairAvailable(run) && run.Progress is { SourcePlanCount:1,RepairPlanCount:0,SourcePlan:not null,SourceResult:not null } p &&
        TryCaptureApprovedA(run,out _,out _,out _) && (p.SourceResult.Build?.CompilerFailure is null || catalog.CompilerRepairAvailable(run)) && p.FailureCount<run.Limits!.MaxToolFailures &&
        run.RemainingDuration>TimeSpan.Zero && !run.Cancellation.IsCancellationRequested;
    internal void RecoverFinalFailure(AgentRunContext run)
    {
        if(run.Progress?.FinalSuccessAccepted==true || run.Progress?.SourcePlan is not SourceEditPlan plan || run.Progress.SourceResult is not { Outcome:SourcePlanOutcome.Success } result) return;
        AgentRunEvents.State(logger,run,AgentRunState.Recovering);
        run.Progress.SourceResult=editing.RecoverSourcePlanAfterFinalFailure(plan,result);
        run.Progress.Verification=new(false,run.Progress.SourceResult.BaselineVerified ? "SourceFinalizationRecovered" : "SourceFinalizationRecoveryConflict");
        run.Progress.RecordSourceAttempt(SourceAttemptFact.For(run,plan,run.Progress.SourceResult));
        AgentRunEvents.Emit(logger,run,"SourceFinalFailureRecoveryCompleted",outcome:run.Progress.SourceResult.Outcome.ToString());
    }
}
