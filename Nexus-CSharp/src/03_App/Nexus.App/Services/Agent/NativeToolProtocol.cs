using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Nexus.App.Services.Agent.Tools;

namespace Nexus.App.Services.Agent;

public sealed class ToolProtocolException(string reasonCode) : Exception(reasonCode)
{
    public string ReasonCode { get; } = reasonCode;
}
public sealed record NativeToolCall(string Id, string Name, string Arguments);
public sealed record NativeAssistantResponse(string Content, string FinishReason, NativeToolCall? ToolCall);

// Gate C intentionally uses the verified non-stream native protocol. No text
// regex becomes execution; streamed parsing is deferred, not silently repaired.
public static class NativeToolProtocol
{
    public static NativeAssistantResponse Parse(string raw)
    {
        if (Encoding.UTF8.GetByteCount(raw) > 65536) throw new ToolProtocolException("ResponseLimit");
        try
        {
            using var document = JsonDocument.Parse(raw, new JsonDocumentOptions { MaxDepth = 12 });
            RejectDuplicateProperties(document.RootElement);
            var choices = document.RootElement.GetProperty("choices");
            if (choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() != 1) throw new ToolProtocolException("InvalidChoices");
            var choice = choices[0];
            var finish = choice.GetProperty("finish_reason").GetString()!;
            var message = choice.GetProperty("message");
            if (message.GetProperty("role").GetString() != "assistant") throw new ToolProtocolException("InvalidAssistantRole");
            var content = message.TryGetProperty("content", out var text) && text.ValueKind != JsonValueKind.Null ? text.GetString()! : "";
            if (content.Length > SharedRuntimeAgent.MaxResultCharacters) throw new ToolProtocolException("ResponseLimit");
            if (!message.TryGetProperty("tool_calls", out var calls) || calls.ValueKind == JsonValueKind.Null ||
                (calls.ValueKind == JsonValueKind.Array && calls.GetArrayLength() == 0))
            {
                if (finish != "stop") throw new ToolProtocolException(finish == "length" ? "OutputLimit" : "MissingNativeToolCall");
                return new(content, finish, null);
            }
            if (calls.ValueKind != JsonValueKind.Array) throw new ToolProtocolException("InvalidToolCalls");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            NativeToolCall? parsed = null;
            foreach (var call in calls.EnumerateArray())
            {
                CheckKeys(call, "id", "type", "function");
                var id = call.GetProperty("id").GetString()!;
                if (id is null || !Regex.IsMatch(id, "\\A[A-Za-z0-9_-]{1,128}\\z", RegexOptions.CultureInvariant)) throw new ToolProtocolException("InvalidToolCallId");
                if (!ids.Add(id)) throw new ToolProtocolException("DuplicateToolCallId");
                if (call.GetProperty("type").GetString() != "function") throw new ToolProtocolException("InvalidCallType");
                var function = call.GetProperty("function");
                CheckKeys(function, "name", "arguments");
                var name = function.GetProperty("name").GetString()!;
                if (!ToolRegistry.IsValidName(name)) throw new ToolProtocolException("InvalidToolName");
                var arguments = function.GetProperty("arguments").GetString()!;
                if (arguments is null || Encoding.UTF8.GetByteCount(arguments) > ToolArgumentValidator.MaxArgumentBytes) throw new ToolProtocolException("ArgumentLimit");
                parsed = new(id, name, arguments);
            }
            if (calls.GetArrayLength() != 1) throw new ToolProtocolException("ToolCallLimit");
            if (finish != "tool_calls") throw new ToolProtocolException("InvalidToolFinishReason");
            return new(content, finish, parsed);
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or KeyNotFoundException)
        {
            throw new ToolProtocolException("MalformedNativeResponse");
        }
    }

    public static JsonObject AssistantMessage(NativeAssistantResponse response)
    {
        var call = response.ToolCall ?? throw new ToolProtocolException("MissingNativeToolCall");
        return new JsonObject { ["role"] = "assistant", ["content"] = response.Content, ["tool_calls"] = new JsonArray(new JsonObject
        {
            ["id"] = call.Id, ["type"] = "function", ["function"] = new JsonObject { ["name"] = call.Name, ["arguments"] = call.Arguments }
        }) };
    }
    public static JsonObject ToolMessage(NativeToolCall call, ToolDispatchResult dispatch)
    {
        if (call.Id != dispatch.ToolCallId || call.Name != dispatch.ToolName) throw new ToolProtocolException("MismatchedToolCallId");
        return new JsonObject { ["role"] = "tool", ["tool_call_id"] = call.Id, ["content"] = ToolExecutor.SerializeResult(dispatch.Result) };
    }
    private static void CheckKeys(JsonElement element, params string[] allowed)
    {
        if (element.ValueKind != JsonValueKind.Object) throw new ToolProtocolException("InvalidCallShape");
        var properties = element.EnumerateObject().ToArray();
        if (properties.Select(p => p.Name).Distinct(StringComparer.Ordinal).Count() != properties.Length || properties.Any(p => !allowed.Contains(p.Name, StringComparer.Ordinal)))
            throw new ToolProtocolException("UnexpectedCallMetadata");
    }
    private static void RejectDuplicateProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!keys.Add(property.Name)) throw new ToolProtocolException("DuplicateNativeProperty");
                RejectDuplicateProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var value in element.EnumerateArray()) RejectDuplicateProperties(value);
    }
}
