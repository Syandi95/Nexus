using System.Text.Json;
using Nexus.Foundation.Logging;

namespace Nexus.App.Services.Agent;

internal static class AgentRunEvents
{
    internal static void Emit(ILoggerService logger,AgentRunContext c,string kind,string? callId=null,string? name=null,
        string? outcome=null,string? failure=null)
    {
        var p=c.Progress!;
        var l=c.Limits!;
        logger.LogInfo("AgentRun",JsonSerializer.Serialize(new { eventName=kind,c.RunId,c.SessionId,state=p.State.ToString(),
            turnNumber=p.ModelTurnCount,toolCallNumber=p.ToolCallCount,toolCallId=callId,toolName=name,
            outcome,failureCategory=failure,elapsedMs=p.Elapsed.TotalMilliseconds,modelId=c.Model?.ModelId,
            remainingBudget=new { modelTurns=l.MaxModelTurns-p.ModelTurnCount,toolCalls=l.MaxToolCalls-p.ToolCallCount,
                failures=l.MaxToolFailures-p.FailureCount,retries=l.MaxRetries-p.RetryCount,
                writes=l.MaxWriteOperations-p.WriteOperationCount,
                sourcePlans=l.MaxSourcePlans-p.SourcePlanCount,repairs=l.MaxRepairPlans-p.RepairPlanCount,
                buildStarts=l.MaxBuildStarts-p.BuildStartCount,testStarts=l.MaxTestStarts-p.TestStartCount,
                toolResultBytes=l.MaxToolResultBytes-p.TotalToolResultBytes,contextBytes=l.MaxInputContext-p.ContextBytes } }));
    }
    internal static void State(ILoggerService logger,AgentRunContext c,AgentRunState state)
    { c.Progress!.State=state; Emit(logger,c,"StateChanged"); }
}
