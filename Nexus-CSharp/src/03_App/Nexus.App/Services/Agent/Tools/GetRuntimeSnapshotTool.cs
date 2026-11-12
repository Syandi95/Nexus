using System.Text.Json;
using Nexus.AI.Llama.Services;

namespace Nexus.App.Services.Agent.Tools;

// Independent current workflow/ownership readiness, not model catalog data.
public sealed class GetRuntimeSnapshotTool(ConversationRuntimeWorkflow workflow,ILlamaServerManager server) : INexusTool
{
    public const string ToolName="nexus_get_runtime_snapshot";
    public ToolDescriptor Descriptor { get; }=new(ToolName,"Read current workspace runtime readiness. No paths or process IDs.",
        ToolRisk.ReadOnly,true,TimeSpan.FromSeconds(2),2048,
        JsonSerializer.SerializeToElement(new { type="object",properties=new { },required=Array.Empty<string>(),additionalProperties=false }));
    public Task<ToolExecutionResult> ExecuteAsync(ToolExecutionContext context,JsonElement arguments,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var data=JsonSerializer.SerializeToElement(new { runtimeState=server.State.ToString(),
            ready=workflow.IsReady && server.State==ServerState.Ready && server.OwnedPid==context.Run.Model!.Pid });
        token.ThrowIfCancellationRequested();
        return Task.FromResult(new ToolExecutionResult(ToolOutcome.Success,data));
    }
}
