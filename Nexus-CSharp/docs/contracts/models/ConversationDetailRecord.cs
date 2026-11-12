using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Nexus.Contracts.Models
{
    public record ConversationDetailRecord(
        [property: JsonPropertyName("schemaVersion")] int SchemaVersion,
        [property: JsonPropertyName("appVersion")] string AppVersion,
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("title")] string Title,
        [property: JsonPropertyName("createdAt")] string CreatedAt,
        [property: JsonPropertyName("updatedAt")] string UpdatedAt,
        [property: JsonPropertyName("provider")] string Provider,
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("modelId")] string ModelId,
        [property: JsonPropertyName("messages")] IReadOnlyList<ConversationMessage> Messages
    );

    public record ConversationMessage(
        [property: JsonPropertyName("id")] int Id,
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] string Content,
        [property: JsonPropertyName("timestamp")] string Timestamp,
        
        // candidate_eval: EditProposal
        [property: JsonPropertyName("EditProposal")] object? EditProposal = null
    );
}
