using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Nexus.Contracts.Models
{
    public record ConversationIndexRecord(
        [property: JsonPropertyName("schemaVersion")] int SchemaVersion,
        [property: JsonPropertyName("appVersion")] string AppVersion,
        [property: JsonPropertyName("updatedAt")] string UpdatedAt,
        [property: JsonPropertyName("conversations")] IReadOnlyList<ConversationIndexItem> Conversations
    );

    public record ConversationIndexItem(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("title")] string Title,
        [property: JsonPropertyName("createdAt")] string CreatedAt,
        [property: JsonPropertyName("updatedAt")] string UpdatedAt,
        [property: JsonPropertyName("provider")] string Provider,
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("modelId")] string ModelId,
        [property: JsonPropertyName("messageCount")] int MessageCount
    );
}
