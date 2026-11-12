using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.RegularExpressions;
using Nexus.AI.Llama.Models;

namespace Nexus.AI.Llama.Transport;

/// <summary>
/// Shared low-level transport helper for LLM chat completion payload generation,
/// SSE line extraction, and chunk parsing.
/// </summary>
internal static class LlamaTransportHelper
{
    private static readonly Regex SpecialTokenRegex = new(
        @"<\|?[a-zA-Z0-9_\-]+(?:[ \=][^>]+)?\|?>",
        RegexOptions.Compiled);

    public static object CreateChatPayload(
        string? modelId,
        IEnumerable<ChatMessagePayload> messages,
        bool enableThinking,
        int? maxTokens = null,
        double? temperature = null,
        double? topP = null,
        int? topK = null,
        double? minP = null)
    {
        var resolvedModelId = string.IsNullOrWhiteSpace(modelId) ? "local" : modelId;
        var payload = new Dictionary<string, object?>
        {
            ["model"] = resolvedModelId,
            ["messages"] = messages,
            ["stream"] = true,
            ["stream_options"] = new { include_usage = true },
            ["chat_template_kwargs"] = new { enable_thinking = enableThinking }
        };

        if (maxTokens.HasValue)
        {
            payload["max_tokens"] = maxTokens.Value;
        }
        if (temperature.HasValue)
        {
            payload["temperature"] = temperature.Value;
        }
        if (topP.HasValue)
        {
            payload["top_p"] = topP.Value;
        }
        if (topK.HasValue)
        {
            payload["top_k"] = topK.Value;
        }
        if (minP.HasValue)
        {
            payload["min_p"] = minP.Value;
        }

        return payload;
    }

    public static bool TryExtractSseData(string line, out string data)
    {
        if (line.StartsWith("data:", StringComparison.Ordinal))
        {
            data = line["data:".Length..].Trim();
            return data.Length > 0;
        }

        data = string.Empty;
        return false;
    }

    public static bool IsDone(string data) =>
        string.Equals(data, "[DONE]", StringComparison.Ordinal);

    public static bool TryParseChunk(
        JsonElement root,
        out LlamaStreamChunk chunk,
        bool sanitizeSpecialTokens = false)
    {
        chunk = new LlamaStreamChunk();
        if (!root.TryGetProperty("choices", out var choices) ||
            choices.ValueKind != JsonValueKind.Array ||
            choices.GetArrayLength() == 0)
        {
            return false;
        }

        var choice = choices[0];
        if (choice.TryGetProperty("finish_reason", out var finishReason) &&
            finishReason.ValueKind == JsonValueKind.String)
        {
            chunk.FinishReason = finishReason.GetString();
        }

        if (choice.TryGetProperty("delta", out var delta))
        {
            if (delta.TryGetProperty("reasoning_content", out var reasoning) &&
                reasoning.ValueKind == JsonValueKind.String)
            {
                chunk.ReasoningContent = reasoning.GetString() ?? string.Empty;
            }

            if (delta.TryGetProperty("content", out var content) &&
                content.ValueKind == JsonValueKind.String)
            {
                var text = content.GetString() ?? string.Empty;
                if (sanitizeSpecialTokens && !string.IsNullOrEmpty(text))
                {
                    text = SanitizeSpecialTokens(text);
                }
                chunk.Content = text;
                chunk.Delta = text;
            }
        }

        return chunk.ReasoningContent.Length > 0 ||
               chunk.Content.Length > 0 ||
               !string.IsNullOrWhiteSpace(chunk.FinishReason);
    }

    public static string SanitizeSpecialTokens(string content)
    {
        if (string.IsNullOrEmpty(content)) return string.Empty;
        var result = SpecialTokenRegex.Replace(content, string.Empty);
        var trimmed = result.Trim();
        if (trimmed.Equals("_model", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("_4", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("_4)", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals(")", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("_4", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }
        return result;
    }
}
