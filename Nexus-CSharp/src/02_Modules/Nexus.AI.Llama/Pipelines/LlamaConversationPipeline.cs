using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Nexus.AI.Llama.Models;
using Nexus.AI.Llama.Transport;
using Nexus.Contracts;
using Nexus.Foundation.Registry;

namespace Nexus.AI.Llama.Pipelines;

/// <summary>
/// Pipeline headless untuk serialisasi payload percakapan dan parsing SSE.
/// Lifecycle server tetap dikendalikan consumer melalui IGenericLlamaEngine.
/// </summary>
public sealed class LlamaConversationPipeline
{
    private readonly HttpClient _httpClient;
    private readonly IModelSelectionPolicy _modelSelectionPolicy;
    private readonly IGenericLlamaEngine _llamaEngine;

    public LlamaConversationPipeline(
        HttpClient httpClient,
        IModelSelectionPolicy modelSelectionPolicy,
        IGenericLlamaEngine llamaEngine)
    {
        _httpClient = httpClient
            ?? throw new ArgumentNullException(nameof(httpClient));
        _modelSelectionPolicy = modelSelectionPolicy
            ?? throw new ArgumentNullException(nameof(modelSelectionPolicy));
        _llamaEngine = llamaEngine
            ?? throw new ArgumentNullException(nameof(llamaEngine));
    }

    public async IAsyncEnumerable<LlamaStreamChunk> StreamAsync(
        IEnumerable<ChatMessagePayload> messages,
        bool enableThinking = false,
        EngineSessionOptions? sessionOptions = null,
        LlamaGenerationOptions? generationOptions = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);
        var materializedMessages = messages.ToArray();
        if (materializedMessages.Length == 0)
        {
            throw new ArgumentException(
                "At least one conversation message is required.",
                nameof(messages));
        }

        var options = sessionOptions ?? new EngineSessionOptions();
        var descriptor = _modelSelectionPolicy.SelectModel(
            AICapability.ChatCompletion,
            options);
        if (descriptor.ModelType != ModelType.Gguf)
        {
            throw new InvalidDataException(
                "Chat completion requires a GGUF model descriptor.");
        }

        var payload = LlamaTransportHelper.CreateChatPayload(
            descriptor.ModelId,
            materializedMessages,
            enableThinking,
            generationOptions?.MaxTokens,
            generationOptions?.Temperature,
            generationOptions?.TopP,
            generationOptions?.TopK,
            generationOptions?.MinP);
        var endpoint = new Uri(
            $"http://127.0.0.1:{_llamaEngine.Port}/v1/chat/completions");
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = JsonContent.Create(payload)
        };
        using var response = await _httpClient
            .SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content
            .ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        using var reader = new StreamReader(stream);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = await reader
                .ReadLineAsync(cancellationToken)
                .ConfigureAwait(false);
            if (line is null)
            {
                yield break;
            }

            if (!LlamaTransportHelper.TryExtractSseData(line, out var data))
            {
                continue;
            }

            if (LlamaTransportHelper.IsDone(data))
            {
                yield break;
            }

            using var document = JsonDocument.Parse(data);
            if (LlamaTransportHelper.TryParseChunk(document.RootElement, out var chunk, sanitizeSpecialTokens: false))
            {
                yield return chunk;
            }
        }
    }
}
