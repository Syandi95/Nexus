using System;
using System.Text.Json.Serialization;

namespace Nexus.AI.Llama.Models
{
    public class LlamaStreamChunk
    {
        [JsonPropertyName("content")]
        public string Content { get; set; } = string.Empty;

        [JsonPropertyName("reasoning_content")]
        public string ReasoningContent { get; set; } = string.Empty;

        [JsonPropertyName("delta")]
        public string Delta { get; set; } = string.Empty;

        [JsonPropertyName("finish_reason")]
        public string? FinishReason { get; set; }

        [JsonPropertyName("tokens_per_second")]
        public double TokensPerSecond { get; set; }

        [JsonPropertyName("time_to_first_token_ms")]
        public long TimeToFirstTokenMs { get; set; }
    }

    public sealed record LlamaGenerationOptions
    {
        public int? MaxTokens { get; init; }
        public double? Temperature { get; init; }
        public double? TopP { get; init; }
        public int? TopK { get; init; }
        public double? MinP { get; init; }
    }

    public class ChatMessagePayload
    {
        [JsonPropertyName("role")]
        public string Role { get; set; } = "user";

        [JsonPropertyName("content")]
        public string Content { get; set; } = string.Empty;
    }
}
