using System.Net.Http;
using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using Nexus.AI.Llama.Models;
using Nexus.App.Services.Agent.Tools;

namespace Nexus.App.Services.Agent;

public sealed class NativeToolModelAdapter(HttpClient sharedHttpClient)
{
    internal async Task<NativeAssistantResponse> GenerateAsync(AgentRunContext context, JsonArray messages,
        IReadOnlyList<ToolDescriptor> descriptors, CancellationToken token,
        bool requireTool=false,AgentVerificationRequirement? finalRequirement=null)
    {
        if(context.Progress?.IsWaitingForApproval==true)throw new ToolProtocolException("ApprovalPending");
        var tools = new JsonArray();
        var repairProjection=context.Progress?.RepairPlanning==true && context.SourceIntent is not null && SourceRepairFeedback.EligibleFailure(context);
        var exactRepairTool=repairProjection && descriptors.Count>0;
        IReadOnlyList<ToolDescriptor> visibleDescriptors=exactRepairTool
            ? descriptors.Where(d=>d.Name==ApplySourcePlanTool.ToolName).ToArray() : descriptors;
        if(exactRepairTool && visibleDescriptors.Count!=1) throw new ToolProtocolException("InvalidExactToolSelection");
        var requireNativeTool=visibleDescriptors.Count>0 && (requireTool || exactRepairTool);
        var exactToolName=requireNativeTool && visibleDescriptors.Count==1 ? visibleDescriptors[0].Name : null;
        foreach (var descriptor in visibleDescriptors)
        {
            var visible=repairProjection ? SourceRepairFeedback.CompactRepairDescriptor(context,descriptor) : context.ProposalScope is { } proposalScope && descriptor.Name==ApplySourcePlanTool.ToolName ? proposalScope.CompactProposalDescriptor(descriptor) : descriptor;
            tools.Add(new JsonObject { ["type"] = "function", ["function"] = new JsonObject
            {
                ["name"] = visible.Name, ["description"] = visible.Description,
                ["parameters"] = JsonNode.Parse(visible.ArgumentSchema.GetRawText())
            } });
        }
        var payload = new JsonObject
        {
            ["model"] = context.Model!.ModelId, ["stream"] = false, ["max_tokens"] = context.MaxOutputTokens, ["temperature"] = 0,
            // Certified llama.cpp b11057 accepts string choices, not named objects.
            // Exact selection comes from one host-selected descriptor, never model text.
            ["chat_template_kwargs"] = new JsonObject { ["enable_thinking"] = false }, ["tool_choice"] = visibleDescriptors.Count==0 ? "none" : requireNativeTool ? "required" : "auto",
            ["messages"] = messages.DeepClone(), ["tools"] = tools
        };
        if(finalRequirement is not null)
        {
            var properties=new JsonObject { ["modelId"]=new JsonObject { ["type"]="string" },["runtimeState"]=new JsonObject { ["type"]="string" } };
            var required=new JsonArray("modelId","runtimeState");
            if(finalRequirement==AgentVerificationRequirement.SourcePlan)
            {
                properties=new JsonObject { ["planId"]=new JsonObject { ["type"]="string" },["planDigest"]=new JsonObject { ["type"]="string" },["verified"]=new JsonObject { ["type"]="boolean" } };
                required=new JsonArray("planId","planDigest","verified");
            }
            if(finalRequirement==AgentVerificationRequirement.SafeTextChange)
            {
                properties=new JsonObject { ["operationId"]=new JsonObject { ["type"]="string" },["target"]=new JsonObject { ["type"]="string" },
                    ["desiredSha256"]=new JsonObject { ["type"]="string" },["verified"]=new JsonObject { ["type"]="boolean" } };
                required=new JsonArray("operationId","target","desiredSha256","verified");
            }
            if(finalRequirement==AgentVerificationRequirement.ModelAndRuntime)
            { properties["ready"]=new JsonObject { ["type"]="boolean" }; required.Add("ready"); }
            payload["response_format"]=new JsonObject { ["type"]="json_schema",["json_schema"]=new JsonObject
            {
                ["name"]="nexus_read_only_facts",["strict"]=true,["schema"]=new JsonObject
                { ["type"]="object",["properties"]=properties,["required"]=required,["additionalProperties"]=false }
            } };
        }
        var raw = payload.ToJsonString();
        if (Encoding.UTF8.GetByteCount(raw) > 65536) throw new ToolProtocolException("RequestLimit");
        using var request = new HttpRequestMessage(HttpMethod.Post, context.Model.Endpoint) { Content = new StringContent(raw, Encoding.UTF8, "application/json") };
        using var response = await sharedHttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        using var bytes = new MemoryStream();
        var buffer = new byte[4096];
        int count;
        while ((count = await stream.ReadAsync(buffer, token).ConfigureAwait(false)) != 0)
        {
            if (bytes.Length + count > 65536) throw new ToolProtocolException("ResponseLimit");
            bytes.Write(buffer, 0, count);
        }
        var parsed=NativeToolProtocol.Parse(Encoding.UTF8.GetString(bytes.ToArray()));
        if(requireNativeTool && parsed.ToolCall is null) throw new ToolProtocolException("MissingNativeToolCall");
        if(exactToolName is not null && parsed.ToolCall!.Name!=exactToolName) throw new ToolProtocolException("UnexpectedToolName");
        return parsed;
    }

    internal static JsonArray Messages(IEnumerable<ChatMessagePayload> messages)
    {
        var result = new JsonArray();
        foreach (var message in messages) result.Add(new JsonObject { ["role"] = message.Role, ["content"] = message.Content });
        return result;
    }
}
