using System.Text.Json;
using Nexus.App.Services.Agent.Tools;

namespace Nexus.App.Services.Agent;

public sealed record AgentVerificationResult(bool Passed,string ReasonCode);

public static class AgentResultVerifier
{
    public static AgentVerificationResult Verify(AgentRunContext context,string content,string finish,
        AgentVerificationRequirement? requirement,CancellationToken token,
        Func<Nexus.AI.EditingLite.Services.WriteOperationRecord,bool>? confirmWrite=null,Func<AgentRunContext,bool>? confirmSource=null)
    {
        token.ThrowIfCancellationRequested();
        if(finish!="stop" || string.IsNullOrWhiteSpace(content) || content.Length>(context.Limits?.MaxFinalOutput ?? 16384))
            return new(false,"FinalStructureInvalid");
        if(requirement is null) return new(true,"NonToolStructureVerified");
        var facts=context.Progress!.ToolDispatches;
        if(facts.Any(d=>d.Result.Outcome!=ToolOutcome.Success && !(requirement==AgentVerificationRequirement.SourcePlan && IsRecoveredAttemptFailure(context,d)))) return new(false,"UnresolvedToolFailure");
        if(requirement==AgentVerificationRequirement.SourcePlan)
        {
            var result=context.Progress.SourceResult;
            var progress=context.Progress;
            if(progress.Attempts.Count!=progress.SourcePlanCount || progress.SourcePlanCount>context.Limits!.MaxSourcePlans || progress.RepairPlanCount>context.Limits.MaxRepairPlans ||
                progress.BuildStartCount>context.Limits.MaxBuildStarts || progress.TestStartCount>context.Limits.MaxTestStarts || progress.TotalDiffBytes>4096 || progress.TotalChangedLines>12 ||
                progress.SourcePlanCount==2 && (progress.RepairPlanCount!=1 || progress.Attempts[1].ParentPlanId!=progress.Attempts[0].PlanId ||
                    !progress.Attempts[1].Plan.Entries.Select(e=>e.Target).SequenceEqual(progress.Attempts[0].Plan.Entries.Select(e=>e.Target)))) return new(false,"SourceAttemptBudgetUnverified");
            if(context.SourceIntent is null || result is null || confirmSource is null || !confirmSource(context)) return new(false,"SourcePlanExecutionUnverified");
            try
            {
                using var doc=JsonDocument.Parse(content,new JsonDocumentOptions { MaxDepth=3 }); var final=doc.RootElement; var fields=final.EnumerateObject().ToArray();
                if(fields.Length!=3 || fields.Select(p=>p.Name).Distinct().Count()!=3 || fields.Any(p=>p.Name is not ("planId" or "planDigest" or "verified")) ||
                    final.GetProperty("planId").GetString()!=result.PlanId.ToString("D") || final.GetProperty("planDigest").GetString()!=result.PlanDigest || !final.GetProperty("verified").GetBoolean()) return new(false,"SourcePlanFactMismatch");
                token.ThrowIfCancellationRequested(); return new(true,"SourcePlanBuildTestsAndHashesVerified");
            }
            catch(Exception e) when(e is JsonException or InvalidOperationException or KeyNotFoundException) { return new(false,"FinalStructureInvalid"); }
        }
        if(requirement==AgentVerificationRequirement.SafeTextChange)
        {
            var op=context.Progress.WriteOperations.SingleOrDefault();
            if(op is null || !op.Verified || op.Recovery!=Nexus.AI.EditingLite.Services.SafeWriteRecovery.NotNeeded || op.ApprovalId is null ||
                op.ActualHash!=op.DesiredHash || (op.CommitReached && op.BackupRef is null) || (!op.CommitReached && op.BeforeHash!=op.DesiredHash))
                return new(false,"WriteExecutionUnverified");
            if(context.WriteIntent is null || context.WriteIntent.Target!=op.Target || context.WriteIntent.ExpectedSha256.ToLowerInvariant()!=op.BeforeHash ||
                confirmWrite is null || !confirmWrite(op)) return new(false,"CurrentWriteStateMismatch");
            try
            {
                using var doc=JsonDocument.Parse(content,new JsonDocumentOptions { MaxDepth=3 }); var final=doc.RootElement;
                var fields=final.EnumerateObject().ToArray(); var keys=new[] { "operationId","target","desiredSha256","verified" };
                if(fields.Length!=4 || fields.Select(p=>p.Name).Distinct(StringComparer.Ordinal).Count()!=4 || fields.Any(p=>!keys.Contains(p.Name,StringComparer.Ordinal)) ||
                    final.GetProperty("operationId").GetString()!=op.OperationId.ToString("D") || final.GetProperty("target").GetString()!=op.Target ||
                    final.GetProperty("desiredSha256").GetString()!=op.DesiredHash || !final.GetProperty("verified").GetBoolean()) return new(false,"WriteFactMismatch");
                token.ThrowIfCancellationRequested(); return new(true,"ExactWriteFactsVerified");
            }
            catch(Exception e) when(e is JsonException or InvalidOperationException or KeyNotFoundException) { return new(false,"FinalStructureInvalid"); }
        }
        var model=facts.LastOrDefault(d=>d.ToolName==GetModelSnapshotTool.ToolName)?.Result.Data;
        if(model is null) return new(false,"RequiredModelToolMissing");
        try
        {
            using var doc=JsonDocument.Parse(content,new JsonDocumentOptions { MaxDepth=3 });
            var final=doc.RootElement;
            var keys=requirement==AgentVerificationRequirement.ModelAndRuntime ? new[] { "modelId","runtimeState","ready" } : new[] { "modelId","runtimeState" };
            if(final.ValueKind!=JsonValueKind.Object) return new(false,"FinalStructureInvalid");
            var properties=final.EnumerateObject().ToArray();
            if(properties.Length!=keys.Length || properties.Select(p=>p.Name).Distinct(StringComparer.Ordinal).Count()!=keys.Length ||
                properties.Any(p=>!keys.Contains(p.Name,StringComparer.Ordinal))) return new(false,"FinalStructureInvalid");
            if(final.GetProperty("modelId").GetString()!=model.Value.GetProperty("modelId").GetString() ||
                final.GetProperty("modelId").GetString()!=context.Model!.ModelId ||
                final.GetProperty("runtimeState").GetString()!=model.Value.GetProperty("runtimeState").GetString())
                return new(false,"DeterministicFactMismatch");
            if(requirement==AgentVerificationRequirement.ModelAndRuntime)
            {
                var runtime=facts.LastOrDefault(d=>d.ToolName==GetRuntimeSnapshotTool.ToolName)?.Result.Data;
                if(runtime is null) return new(false,"RequiredRuntimeToolMissing");
                if(final.GetProperty("ready").GetBoolean()!=runtime.Value.GetProperty("ready").GetBoolean() ||
                    runtime.Value.GetProperty("runtimeState").GetString()!=final.GetProperty("runtimeState").GetString())
                    return new(false,"DeterministicFactMismatch");
            }
            token.ThrowIfCancellationRequested();
            return new(true,"DeterministicFactsVerified");
        }
        catch(Exception e) when(e is JsonException or InvalidOperationException or KeyNotFoundException)
        { return new(false,"FinalStructureInvalid"); }
    }
    internal static bool IsRecoveredAttemptFailure(AgentRunContext context,ToolDispatchResult dispatch)
    {
        var p=context.Progress!;
        if(p.Attempts.Count!=2 || p.RepairPlanCount!=1 || dispatch!=p.ToolDispatches.FirstOrDefault() || dispatch.ToolName!=ApplySourcePlanTool.ToolName ||
            dispatch.Result.Outcome!=ToolOutcome.VerificationFailed) return false;
        var first=p.Attempts[0]; var second=p.Attempts[1];
        var known=first.Result.Outcome==Nexus.AI.EditingLite.Services.SourcePlanOutcome.TestFailed && first.Result.Build?.Passed==true && first.Result.Tests?.Category=="KnownRequiredTestAssertionFailure" ||
            first.Result.Outcome==Nexus.AI.EditingLite.Services.SourcePlanOutcome.BuildFailed && first.Result.Build?.Category=="RecognizedBoundCompilerFailure" &&
            first.Result.Build.CompilerFailure is { } compiler && compiler.Matches(first.Plan,1) && first.Result.Tests is null;
        return known && first.Result.BaselineVerified && first.Result.RecoveredBaseDigest==first.BaselineDigest &&
            second.ParentPlanId==first.PlanId && second.BaselineDigest==first.BaselineDigest && second.Result.Outcome==Nexus.AI.EditingLite.Services.SourcePlanOutcome.Success;
    }
}
