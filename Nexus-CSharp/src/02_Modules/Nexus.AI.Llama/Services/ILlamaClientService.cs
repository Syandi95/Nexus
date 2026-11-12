using System.Collections.Generic;
using System.Threading;
using Nexus.AI.Llama.Models;

namespace Nexus.AI.Llama.Services
{
    public interface ILlamaClientService
    {
        IAsyncEnumerable<LlamaStreamChunk> StreamChatCompletionsAsync(string prompt, CancellationToken cancellationToken = default);
        IAsyncEnumerable<LlamaStreamChunk> StreamChatCompletionsAsync(IEnumerable<ChatMessagePayload> messages, CancellationToken cancellationToken = default);
        IAsyncEnumerable<LlamaStreamChunk> StreamChatCompletionsAsync(IEnumerable<ChatMessagePayload> messages, bool enableThinking, CancellationToken cancellationToken = default);
        IAsyncEnumerable<LlamaStreamChunk> StreamChatCompletionsAsync(IEnumerable<ChatMessagePayload> messages, bool enableThinking, int? maxTokens, CancellationToken cancellationToken = default)
            => StreamChatCompletionsAsync(messages, enableThinking, cancellationToken);
        IAsyncEnumerable<LlamaStreamChunk> StreamChatCompletionsAsync(IEnumerable<ChatMessagePayload> messages, bool enableThinking, LlamaGenerationOptions options, CancellationToken cancellationToken = default)
            => StreamChatCompletionsAsync(messages, enableThinking, options.MaxTokens, cancellationToken);
        void CancelStreaming();
    }
}
