using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Nexus.Foundation.Logging;

namespace Nexus.App.Services.Agent.Tools;

public sealed class ToolExecutor(ToolRegistry registry, ReadOnlyToolPolicy policy, ILoggerService logger,TimeProvider? timeProvider=null)
{
    public async Task<ToolDispatchResult> ExecuteAsync(ToolExecutionContext context, string callId, string name, string rawArguments, CancellationToken token)
    {
        var started = DateTimeOffset.UtcNow;
        var timer = Stopwatch.StartNew();
        ToolDispatchResult Finish(string validation, ToolPolicyDecision decision, ToolExecutionResult result)
        {
            var length = Encoding.UTF8.GetByteCount(SerializeResult(result));
            logger.LogInfo("AgentTool", JsonSerializer.Serialize(new { context.Run.RunId, toolCallId = callId.Length <= 128 ? callId : "<invalid>", toolName = ToolRegistry.IsValidName(name) ? name : "<invalid>",
                validationResult = validation, policyDecision = decision.Allowed ? "Allowed" : "Denied", decision.ReasonCode,
                started, durationMs = timer.ElapsedMilliseconds, outcome = result.Outcome.ToString(), errorCategory = result.ReasonCode, resultLength = length }));
            return new(callId, name, validation, decision, result, length);
        }
        if(context.Run.Progress?.IsWaitingForApproval==true)
            return Finish("NotEvaluated",new(false,"ApprovalPending"),new(ToolOutcome.Denied,ReasonCode:"ApprovalPending"));
        if (!registry.TryGet(name, out var entry))
        {
            if(context.Run.Progress is not null)
            {
                AgentRunEvents.Emit(logger,context.Run,"ToolValidated",callId,name,outcome:"NotEvaluated");
                AgentRunEvents.Emit(logger,context.Run,"ToolPolicyDecision",callId,name,outcome:"UnknownTool");
            }
            return Finish("NotEvaluated", new(false, "UnknownTool"), new(ToolOutcome.ToolNotFound, ReasonCode: "UnknownTool"));
        }
        var descriptor = entry!.Descriptor;
        if(descriptor.Risk==ToolRisk.SourcePlan)
        {
            var progress=context.Run.Progress;
            if(progress is null || context.Run.Limits is null || progress.SourcePlanCount>=context.Run.Limits.MaxSourcePlans)
                return Finish("NotEvaluated",new(false,"SourcePlanLimitReached"),new(ToolOutcome.WriteOperationLimitReached,ReasonCode:"SourcePlanLimitReached"));
            if(progress.SourcePlanCount==1)
            {
                if(!progress.RepairPlanning || progress.RepairPlanCount>=context.Run.Limits.MaxRepairPlans)
                    return Finish("NotEvaluated",new(false,"RepairPlanLimitReached"),new(ToolOutcome.Denied,ReasonCode:"RepairPlanLimitReached"));
                progress.RepairPlanCount++;
            }
            progress.SourcePlanCount++;
        }
        if(descriptor.Risk==ToolRisk.SafeWrite)
        {
            var progress=context.Run.Progress;
            if(progress is null || context.Run.Limits is null || progress.WriteOperationCount>=context.Run.Limits.MaxWriteOperations)
                return Finish("NotEvaluated",new(false,"WriteOperationLimitReached"),new(ToolOutcome.WriteOperationLimitReached,ReasonCode:"WriteOperationLimitReached"));
            progress.WriteOperationCount++;
        }
        JsonElement arguments;
        ValidatedSourceRepairInput? repairInput=null;
        var validationReason="InvalidArguments";
        bool valid;
        if(entry.Tool is ApplySourcePlanTool source && context.Run.Progress?.RepairPlanning==true)
        {
            valid=source.TryValidateRepair(context.Run,rawArguments,out repairInput,out validationReason);
            // Source policy admission is host-owned. Raw advisory JSON never reaches execution.
            arguments=JsonSerializer.SerializeToElement(new { });
        }
        else valid=entry.Tool is ApplySourcePlanTool ? SourcePlanArgumentValidator.TryValidate(rawArguments,out arguments)
            : ToolArgumentValidator.TryValidate(rawArguments,descriptor.ArgumentSchema,out arguments);
        if (!valid)
        {
            if(context.Run.Progress is not null)
            {
                AgentRunEvents.Emit(logger,context.Run,"ToolValidated",callId,name,outcome:"InvalidArguments");
                AgentRunEvents.Emit(logger,context.Run,"ToolPolicyDecision",callId,name,outcome:"InvalidArguments");
            }
            return Finish("InvalidArguments", new(false, "InvalidArguments"), new(ToolOutcome.InvalidArguments, ReasonCode:validationReason));
        }
        if(context.Run.Progress is not null) AgentRunEvents.Emit(logger,context.Run,"ToolValidated",callId,name,outcome:"Valid");
        var decision = policy.Evaluate(descriptor, context, arguments);
        if(context.Run.Progress is not null) AgentRunEvents.Emit(logger,context.Run,"ToolPolicyDecision",callId,name,decision.ReasonCode);
        if (!decision.Allowed) return Finish("Valid", decision, new(decision.ReasonCode=="PathDenied" ? ToolOutcome.PathDenied : ToolOutcome.Denied, ReasonCode: decision.ReasonCode));
        if (token.IsCancellationRequested) return Finish("Valid", decision, new(ToolOutcome.Cancelled, ReasonCode: "RunCancelled"));
        var remaining = context.Run.RemainingDuration;
        var maximum=entry.Tool is ApplySourcePlanTool sourceTool && sourceTool.UsesPilotDeadline(context.Run) ? remaining : descriptor.Timeout;
        var sharedBudget=entry.Tool is ApplySourcePlanTool pilot && pilot.UsesPilotDeadline(context.Run) && context.Run.Progress?.HasActiveBudget==true;
        using var elapsed=new CancellationTokenSource(sharedBudget?Timeout.InfiniteTimeSpan:remaining < maximum ? (remaining > TimeSpan.Zero ? remaining : TimeSpan.FromTicks(1)) : maximum>TimeSpan.Zero ? maximum : TimeSpan.FromTicks(1),timeProvider??TimeProvider.System);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token,elapsed.Token);
        try
        {
            if(context.Run.Progress is not null) AgentRunEvents.Emit(logger,context.Run,"ToolStarted",callId,name);
            // Trusted registered snapshot operations are synchronous and fast.
            // WaitAsync bounds asynchronous completion; it cannot preempt a blocking
            // synchronous body or forcibly terminate a non-cooperative API.
            var admitted=context with { Run=context.Run with { ActiveToolCallId=callId } };
            var execution = repairInput is not null && entry.Tool is ApplySourcePlanTool repairTool
                ? repairTool.ExecuteValidatedRepairAsync(admitted,repairInput,deadline.Token)
                : entry.Tool.ExecuteAsync(admitted,arguments,deadline.Token);
            _ = execution.ContinueWith(t => { _ = t.Exception; }, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            // Mutation never detaches on cancellation. Post-commit verify/recovery
            // must finish before lease drain; original commit facts are preserved.
            var result = descriptor.Risk is ToolRisk.SafeWrite or ToolRisk.SourcePlan ? await execution.ConfigureAwait(false) : await execution.WaitAsync(deadline.Token).ConfigureAwait(false);
            if(descriptor.Risk is not (ToolRisk.SafeWrite or ToolRisk.SourcePlan)) deadline.Token.ThrowIfCancellationRequested();
            if (Encoding.UTF8.GetByteCount(SerializeResult(result)) > descriptor.MaxResultSize)
                result = new(ToolOutcome.ResultTooLarge, ReasonCode: "ResultLimit");
            return Finish("Valid", decision, result);
        }
        catch (OperationCanceledException)
        {
            return Finish("Valid", decision, token.IsCancellationRequested
                ? new(ToolOutcome.Cancelled, ReasonCode: "RunCancelled") : new(ToolOutcome.Timeout, ReasonCode: "ToolDeadline"));
        }
        catch (Exception)
        {
            return Finish("Valid", decision, new(ToolOutcome.ExecutionFailure, ReasonCode: "ToolExecutionFailure"));
        }
    }

    public static string SerializeResult(ToolExecutionResult result) => JsonSerializer.Serialize(new
    {
        status = result.Outcome.ToString(), data = result.Data, reasonCode = result.ReasonCode
    });
}
