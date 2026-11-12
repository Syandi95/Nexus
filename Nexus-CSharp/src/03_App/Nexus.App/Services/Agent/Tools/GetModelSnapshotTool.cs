using System.Text.Json;
using Nexus.AI.Llama.Services;

namespace Nexus.App.Services.Agent.Tools;

public sealed class GetModelSnapshotTool(ILlamaServerManager server) : INexusTool
{
    public const string ToolName = "nexus_get_model_snapshot";
    public ToolDescriptor Descriptor { get; } = new(ToolName, "Read the logical identity and state of the active Nexus model. No paths, configuration or credentials.",
        ToolRisk.ReadOnly, true, TimeSpan.FromSeconds(2), 2048,
        JsonSerializer.SerializeToElement(new { type = "object", properties = new { }, required = Array.Empty<string>(), additionalProperties = false }));
    public Task<ToolExecutionResult> ExecuteAsync(ToolExecutionContext context, JsonElement arguments, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var data = JsonSerializer.SerializeToElement(new { modelId = context.Run.Model!.ModelId, runtimeState = server.State.ToString() });
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new ToolExecutionResult(ToolOutcome.Success, data));
    }
}
