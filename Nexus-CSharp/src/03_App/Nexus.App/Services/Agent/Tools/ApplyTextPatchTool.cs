using System.Text.Json;
using System.IO;
using Nexus.AI.EditingLite.Services;
using Nexus.Foundation.Logging;

namespace Nexus.App.Services.Agent.Tools;

public sealed class ApplyTextPatchTool(ApprovedTextWorkspace workspace,EditingWorkflowService editing,IWriteApprovalProvider approval,ILoggerService logger) : INexusTool
{
    public const string ToolName="nexus_apply_text_patch";
    public ToolDescriptor Descriptor { get; }=new(ToolName,"Propose one literal replacement in an existing approved .txt file. Exact expected SHA-256 and independent approval required.",
        ToolRisk.SafeWrite,false,TimeSpan.FromSeconds(120),2048,JsonSerializer.SerializeToElement(new
        {
            type="object",properties=new
            {
                target=new { type="string",maxLength=64 },expected_sha256=new { type="string",maxLength=64 },
                old_text=new { type="string",maxLength=256 },new_text=new { type="string",maxLength=256 }
            },required=new[] { "target","expected_sha256","old_text","new_text" },additionalProperties=false
        }));
    public async Task<ToolExecutionResult> ExecuteAsync(ToolExecutionContext context,JsonElement arguments,CancellationToken token)
    {
        var run=context.Run;
        var progress=run.Progress!;
        SafeWriteProposal? proposal=null;
        void Event(string kind,WriteOperationRecord? record=null)
        {
            logger.LogInfo("AgentMutation",JsonSerializer.Serialize(new { eventName=kind,run.RunId,toolCallId=run.ActiveToolCallId,
                operationId=record?.OperationId ?? proposal?.OperationId,logicalTarget=record?.Target ?? proposal?.Target,
                beforeHash=record?.BeforeHash ?? proposal?.BeforeHash,desiredHash=record?.DesiredHash ?? proposal?.DesiredHash,
                record?.ApprovalId,record?.PolicyId,record?.CommitReached,record?.Verified,record?.ActualHash,recovery=record?.Recovery.ToString(),outcome=kind,elapsedMs=progress.Elapsed.TotalMilliseconds }));
        }
        try
        {
            if(run.WriteIntent is null || progress.WriteOperationCount!=1 || progress.WriteOperations.Count!=0 || workspace.Root is null)
                return new(ToolOutcome.Denied,ReasonCode:"SafeWriteAuthorityDenied");
            var callId=run.ActiveToolCallId ?? throw new InvalidOperationException("Dispatch correlation required.");
            proposal=await editing.PrepareSafeWriteAsync(workspace,arguments.GetProperty("target").GetString()!,arguments.GetProperty("expected_sha256").GetString()!,
                arguments.GetProperty("old_text").GetString()!,arguments.GetProperty("new_text").GetString()!,run.RunId,callId,
                new(run.Limits!.MaxWriteInputBytes,run.Limits.MaxTargetFileBytes),token);
            Event("MutationProposed"); AgentRunEvents.State(logger,run,AgentRunState.AwaitingApproval); Event("ApprovalRequested");
            WriteApprovalDecision decision;
            try { decision=await approval.RequestAsync(proposal,token).WaitAsync(token).ConfigureAwait(false); }
            catch(OperationCanceledException)
            {
                progress.AddWrite(new(proposal.OperationId,run.RunId,callId,proposal.Target,proposal.BeforeHash,proposal.DesiredHash,Timestamp:proposal.CreatedAt,PolicyId:proposal.PolicyId));
                return new(ToolOutcome.Cancelled,ReasonCode:"ApprovalCancelled");
            }
            Event(decision.Approved && decision.Matches(proposal) ? "ApprovalGranted" : "ApprovalDenied");
            AgentRunEvents.State(logger,run,AgentRunState.ExecutingTool);
            var result=await editing.ApplyApprovedSafeWriteAsync(proposal,decision,token,(stage,record)=>
            {
                if(stage==SafeWriteStage.Recovering) AgentRunEvents.State(logger,run,AgentRunState.Recovering);
                if(stage is SafeWriteStage.Verified or SafeWriteStage.Committed) AgentRunEvents.State(logger,run,AgentRunState.Verifying);
                Event(stage switch { SafeWriteStage.PreconditionChecked=>"PreconditionChecked",SafeWriteStage.BackupCreated=>"BackupCreated",SafeWriteStage.TempWritten=>"TempWritten",
                    SafeWriteStage.CommitStarted=>"CommitStarted",SafeWriteStage.Committed=>"Committed",SafeWriteStage.Verified=>"WriteVerified",SafeWriteStage.Recovering=>"RecoveryStarted",SafeWriteStage.Recovered=>"RecoveryCompleted",_=>stage.ToString() },record);
            },authorizeCommit:()=>
            {
                token.ThrowIfCancellationRequested(); run.Cancellation.ThrowIfCancellationRequested();
                if(run.RemainingDuration<=TimeSpan.Zero) throw new OperationCanceledException(token);
            });
            progress.AddWrite(result.Operation);
            if(result.Outcome==SafeWriteOutcome.RecoveryConflict) Event("RecoveryConflict",result.Operation);
            var outcome=result.Outcome switch
            {
                SafeWriteOutcome.Success or SafeWriteOutcome.NoOp=>ToolOutcome.Success,
                SafeWriteOutcome.ApprovalDenied=>ToolOutcome.ApprovalDenied,
                SafeWriteOutcome.Conflict=>ToolOutcome.Conflict,
                SafeWriteOutcome.PathDenied=>ToolOutcome.PathDenied,
                SafeWriteOutcome.InvalidArguments=>ToolOutcome.InvalidArguments,
                SafeWriteOutcome.BackupFailed=>ToolOutcome.BackupFailed,
                SafeWriteOutcome.TempWriteFailed=>ToolOutcome.TempWriteFailed,
                SafeWriteOutcome.CommitFailed=>ToolOutcome.CommitFailed,
                SafeWriteOutcome.RecoverySucceeded=>ToolOutcome.RecoverySucceeded,
                SafeWriteOutcome.RecoveryConflict=>ToolOutcome.RecoveryConflict,
                SafeWriteOutcome.Cancelled=>ToolOutcome.Cancelled,
                _=>ToolOutcome.VerificationFailed
            };
            return new(outcome,JsonSerializer.SerializeToElement(new
            {
                operationId=result.Operation.OperationId.ToString("D"),target=result.Operation.Target,beforeSha256=result.Operation.BeforeHash,desiredSha256=result.Operation.DesiredHash,
                actualSha256=result.Operation.ActualHash,approvalId=result.Operation.ApprovalId,backupRef=result.Operation.BackupRef,
                committed=result.Operation.CommitReached,verified=result.Operation.Verified,noOp=result.Outcome==SafeWriteOutcome.NoOp,recovery=result.Operation.Recovery.ToString()
            }),outcome==ToolOutcome.Success ? null : result.Outcome.ToString());
        }
        catch(SafeWriteConflictException) { return new(ToolOutcome.Conflict,ReasonCode:"ExpectedHashConflict"); }
        catch(Nexus.AI.EditingLite.Exceptions.EditingException e)
        { return new(e.FailureCode==Nexus.AI.EditingLite.Exceptions.EditingFailureCode.PathOutsideProjectRoot ? ToolOutcome.PathDenied : ToolOutcome.InvalidArguments,ReasonCode:"InvalidLiteralProposal"); }
        catch(ArgumentException) { return new(ToolOutcome.InvalidArguments,ReasonCode:"InvalidBoundedPatch"); }
        catch(IOException) { return new(ToolOutcome.PathDenied,ReasonCode:"TargetDenied"); }
    }
}
