using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Nexus.App.Services.Agent;

/// <summary>
/// Diagnostic-only classification of raw model responses.
/// Provides observability into Gemma local model text dialect vs native envelopes.
/// Never confers tool execution authority or mutates workspace state.
/// </summary>
public enum ModelResponseClassification
{
    /// <summary>
    /// Response finished prematurely (finish_reason=length) or payload was cut off mid-token/JSON.
    /// Strictly rejected under OutputLimit / fail-closed rules.
    /// </summary>
    IncompleteGeneration,

    /// <summary>
    /// OpenAI-compatible native tool_calls envelope was observed in the response JSON.
    /// Observation only; execution authority requires full schema validation and approval.
    /// </summary>
    NativeEnvelopeObserved,

    /// <summary>
    /// Gemma text markup tool call syntax was observed in message content (<|tool_call>...<tool_call|>).
    /// Diagnostic only; never promoted to an executable native tool call.
    /// </summary>
    TextMarkupGemmaCandidate,

    /// <summary>
    /// Regular conversational text without tool-calling envelope or tool-calling markup.
    /// </summary>
    PlainProse
}

/// <summary>
/// Diagnostic report for a model response.
/// </summary>
public sealed record ModelResponseDiagnostic(
    ModelResponseClassification Classification,
    string? FinishReason,
    string? DetectedToolName,
    string? CandidateArguments,
    string Detail
);

/// <summary>
/// Diagnostic-only response classifier and Gemma dialect normalizer.
/// Ports lightweight token/quote normalisation from verified Unsloth OSS source.
/// Pure C#, zero side-effects, zero runtime Python dependency.
/// </summary>
public static class ModelResponseClassifier
{
    public const string GemmaQuoteToken = "<|\"|>";
    private static readonly Regex GemmaStartRegex = new(@"<\|tool_call>\s*(?:call\s*:\s*)?([A-Za-z0-9_.-]+)\s*\{", RegexOptions.Compiled);
    private static readonly Regex GemmaClosedRegex = new(@"(?:<\|tool_call>\s*)?(?:call\s*:\s*)?([A-Za-z0-9_.-]+)\s*(\{[\s\S]*?\})\s*<tool_call\|>", RegexOptions.Compiled);
    private static readonly Regex BareGemmaRegex = new(@"\bcall\s*:\s*([A-Za-z0-9_.-]+)\s*(\{[\s\S]*?\})", RegexOptions.Compiled);

    /// <summary>
    /// Classifies an OpenAI-compatible JSON completion response string.
    /// </summary>
    public static ModelResponseDiagnostic Classify(string rawResponseJson)
    {
        if (string.IsNullOrWhiteSpace(rawResponseJson))
        {
            return new ModelResponseDiagnostic(
                ModelResponseClassification.IncompleteGeneration,
                null, null, null,
                "Empty or whitespace response payload.");
        }

        string? finishReason = null;
        string content = "";
        bool hasNativeCalls = false;

        try
        {
            using var doc = JsonDocument.Parse(rawResponseJson);
            var root = doc.RootElement;
            if (root.TryGetProperty("choices", out var choices) && choices.ValueKind == JsonValueKind.Array && choices.GetArrayLength() > 0)
            {
                var choice = choices[0];
                if (choice.TryGetProperty("finish_reason", out var finishProp) && finishProp.ValueKind == JsonValueKind.String)
                {
                    finishReason = finishProp.GetString();
                }

                if (choice.TryGetProperty("message", out var msg) && msg.ValueKind == JsonValueKind.Object)
                {
                    if (msg.TryGetProperty("content", out var contentProp) && contentProp.ValueKind == JsonValueKind.String)
                    {
                        content = contentProp.GetString() ?? "";
                    }

                    if (msg.TryGetProperty("tool_calls", out var callsProp) &&
                        callsProp.ValueKind == JsonValueKind.Array &&
                        callsProp.GetArrayLength() > 0)
                    {
                        hasNativeCalls = true;
                    }
                }
            }
        }
        catch (JsonException)
        {
            return new ModelResponseDiagnostic(
                ModelResponseClassification.IncompleteGeneration,
                finishReason, null, null,
                "Malformed JSON response envelope.");
        }

        return ClassifyContent(content, finishReason, hasNativeCalls);
    }

    /// <summary>
    /// Classifies response message content and finish reason.
    /// Priority order strictly maintains safety fences: finish=length always dominates.
    /// </summary>
    public static ModelResponseDiagnostic ClassifyContent(string content, string? finishReason, bool hasNativeEnvelope = false)
    {
        // Fence 1: finish=length is ALWAYS IncompleteGeneration, regardless of content markup.
        if (string.Equals(finishReason, "length", StringComparison.OrdinalIgnoreCase))
        {
            return new ModelResponseDiagnostic(
                ModelResponseClassification.IncompleteGeneration,
                finishReason,
                null,
                null,
                "Output limit reached (finish_reason is 'length'). Generation was cut off and rejected.");
        }

        // Fence 2: Native envelope observed.
        if (hasNativeEnvelope)
        {
            return new ModelResponseDiagnostic(
                ModelResponseClassification.NativeEnvelopeObserved,
                finishReason,
                null,
                null,
                "Native tool_calls envelope observed in response payload (observation only, not execution authority).");
        }

        // Fence 3: Gemma tool-call syntax candidate.
        if (TryExtractGemmaCandidate(content, out var toolName, out var candidateArgs))
        {
            if (LooksLikeBrokenJson(candidateArgs))
            {
                return new ModelResponseDiagnostic(
                    ModelResponseClassification.IncompleteGeneration,
                    finishReason,
                    toolName,
                    candidateArgs,
                    "Gemma markup detected but candidate arguments are broken or truncated JSON.");
            }

            return new ModelResponseDiagnostic(
                ModelResponseClassification.TextMarkupGemmaCandidate,
                finishReason,
                toolName,
                candidateArgs,
                "Diagnostic-only Gemma text markup candidate detected. No execution authority granted.");
        }

        // Fence 4: Incomplete/unclosed tool-call opener without closing tag.
        if (content.Contains("<|tool_call>", StringComparison.Ordinal) ||
            content.Contains("<tool_call>", StringComparison.Ordinal) ||
            content.Contains("<function=", StringComparison.Ordinal) ||
            GemmaStartRegex.IsMatch(content))
        {
            return new ModelResponseDiagnostic(
                ModelResponseClassification.IncompleteGeneration,
                finishReason,
                null,
                null,
                "Unclosed tool call opener detected without matching closer tag.");
        }

        // Fence 5: Plain prose.
        return new ModelResponseDiagnostic(
            ModelResponseClassification.PlainProse,
            finishReason,
            null,
            null,
            "Plain conversational prose without tool calling markup.");
    }

    /// <summary>
    /// Attempts to extract and normalize a Gemma dialect tool call from message content.
    /// </summary>
    public static bool TryExtractGemmaCandidate(string content, out string? toolName, out string? normalizedArguments)
    {
        toolName = null;
        normalizedArguments = null;
        if (string.IsNullOrWhiteSpace(content)) return false;

        // 1. Try closed Gemma markup: <|tool_call>call:name{...}<tool_call|> or name{...}<tool_call|>
        var match = GemmaClosedRegex.Match(content);
        if (!match.Success)
        {
            // 2. Try bare call pattern if Gemma quote token is present
            if (content.Contains(GemmaQuoteToken, StringComparison.Ordinal))
            {
                match = BareGemmaRegex.Match(content);
            }
        }

        if (!match.Success) return false;

        toolName = match.Groups[1].Value.Trim();
        var rawArgs = match.Groups[2].Value.Trim();

        var normalized = TryNormalizeGemmaArguments(rawArgs);
        if (normalized is null) return false;

        normalizedArguments = normalized;
        return true;
    }

    /// <summary>
    /// Normalizes Gemma dialect arguments:
    /// 1. Converts <|"|> quoted strings to standard JSON quoted strings.
    /// 2. Quotes unquoted object keys outside string literals.
    /// </summary>
    public static string? TryNormalizeGemmaArguments(string argsText)
    {
        if (string.IsNullOrWhiteSpace(argsText)) return "{}";
        var trimmed = argsText.Trim();
        if (trimmed.StartsWith('{') && trimmed.EndsWith('}'))
        {
            trimmed = trimmed[1..^1].Trim();
        }

        // 1. Normalize Gemma quotes
        var quotesNormalized = NormalizeGemmaQuotedStrings(trimmed);
        if (quotesNormalized is null) return null;

        // 2. Wrap in braces and quote object keys
        var wrapped = "{" + quotesNormalized + "}";
        var keysQuoted = QuoteGemmaObjectKeys(wrapped);

        // 3. Verify that the result is valid JSON
        try
        {
            using var doc = JsonDocument.Parse(keysQuoted);
            return keysQuoted;
        }
        catch (JsonException)
        {
            // Return string anyway so caller can inspect or detect broken json
            return keysQuoted;
        }
    }

    /// <summary>
    /// Replaces <|"|>...<|"|> with standard JSON string representations.
    /// </summary>
    private static string? NormalizeGemmaQuotedStrings(string src)
    {
        var sb = new StringBuilder();
        int i = 0;
        int qLen = GemmaQuoteToken.Length;

        while (i < src.Length)
        {
            int idx = src.IndexOf(GemmaQuoteToken, i, StringComparison.Ordinal);
            if (idx == -1)
            {
                sb.Append(src, i, src.Length - i);
                break;
            }

            sb.Append(src, i, idx - i);
            int end = src.IndexOf(GemmaQuoteToken, idx + qLen, StringComparison.Ordinal);
            if (end == -1)
            {
                // Unterminated Gemma quote token
                return null;
            }

            string rawVal = src.Substring(idx + qLen, end - (idx + qLen));
            sb.Append(JsonSerializer.Serialize(rawVal));
            i = end + qLen;
        }

        return sb.ToString();
    }

    /// <summary>
    /// Quotes unquoted object keys in Gemma JSON-like objects (e.g. {a:[...], b:[...]}).
    /// Uses stateful character scanning matching Unsloth's _quote_gemma_object_keys.
    /// </summary>
    private static string QuoteGemmaObjectKeys(string src)
    {
        var sb = new StringBuilder();
        int i = 0;
        bool inString = false;

        while (i < src.Length)
        {
            char ch = src[i];
            if (inString)
            {
                sb.Append(ch);
                if (ch == '\\' && i + 1 < src.Length)
                {
                    sb.Append(src[i + 1]);
                    i += 2;
                    continue;
                }
                if (ch == '"')
                {
                    inString = false;
                }
                i++;
                continue;
            }

            if (ch == '"')
            {
                inString = true;
                sb.Append(ch);
                i++;
                continue;
            }

            if (ch != '{' && ch != ',')
            {
                sb.Append(ch);
                i++;
                continue;
            }

            sb.Append(ch);
            i++;
            int keyStart = i;
            while (i < src.Length && char.IsWhiteSpace(src[i])) i++;
            int keyNameStart = i;
            while (i < src.Length && (char.IsLetterOrDigit(src[i]) || src[i] == '_' || src[i] == '-' || src[i] == '.')) i++;
            string keyName = src[keyNameStart..i];
            int colonPos = i;
            while (colonPos < src.Length && char.IsWhiteSpace(src[colonPos])) colonPos++;

            if (keyName.Length > 0 && colonPos < src.Length && src[colonPos] == ':')
            {
                sb.Append(src, keyStart, keyNameStart - keyStart);
                sb.Append(JsonSerializer.Serialize(keyName));
                sb.Append(src, i, colonPos - i);
                sb.Append(':');
                i = colonPos + 1;
            }
            else
            {
                sb.Append(src, keyStart, i - keyStart);
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Detects whether raw string was meant to be JSON and stopped mid-stream.
    /// Adapted from Unsloth tool_loop_controller._looks_like_broken_json.
    /// </summary>
    public static bool LooksLikeBrokenJson(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return false;
        var text = raw.Trim();
        if (!text.StartsWith('{') && !text.StartsWith('[')) return false;

        try
        {
            using var doc = JsonDocument.Parse(text);
            return false;
        }
        catch (JsonException ex)
        {
            var msg = ex.Message;
            if (msg.Contains("open string", StringComparison.OrdinalIgnoreCase) ||
                msg.Contains("not closed", StringComparison.OrdinalIgnoreCase) ||
                msg.Contains("end of the JSON payload", StringComparison.OrdinalIgnoreCase) ||
                msg.Contains("does not contain any complete JSON tokens", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (ex.BytePositionInLine.HasValue && ex.BytePositionInLine.Value >= text.Length - 10)
            {
                return true;
            }

            return true;
        }
    }
}
