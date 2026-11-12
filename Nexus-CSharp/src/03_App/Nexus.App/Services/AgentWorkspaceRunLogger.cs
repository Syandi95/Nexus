using System.Text.Json;
using Nexus.Foundation.Logging;

namespace Nexus.App.Services;

// Metadata only. It observes the existing Agent events, never executes tools.
internal sealed class AgentWorkspaceRunLogger(ILoggerService logger, string session,
    AgentWorkspaceApprovalProvider approval, Action<string> state) : ILoggerService
{
    private Guid runId;
    public void LogInfo(string category,string message)
    {
        logger.LogInfo(category,message);
        if(category != "AgentRun") return;
        using var json = JsonDocument.Parse(message);
        var data=json.RootElement;
        if(data.GetProperty("SessionId").GetString()!=session) return;
        var run=data.GetProperty("RunId").GetGuid();
        if(runId==Guid.Empty && data.GetProperty("eventName").GetString()=="RunStarted")
        { runId=run; approval.BindRun(run); }
        if(run==runId && data.GetProperty("eventName").GetString()=="StateChanged")
            state(data.GetProperty("state").GetString()!);
    }
    public void LogWarning(string category,string message)=>logger.LogWarning(category,message);
    public void LogError(string category,string message)=>logger.LogError(category,message);
}
