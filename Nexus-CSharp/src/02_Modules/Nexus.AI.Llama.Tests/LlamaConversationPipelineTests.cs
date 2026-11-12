using System.Net;
using System.Text.Json;
using Nexus.AI.Llama.Models;
using Nexus.AI.Llama.Pipelines;
using Nexus.Contracts;
using Nexus.Foundation.Registry;

namespace Nexus.AI.Llama.Tests;

public sealed class LlamaConversationPipelineTests
{
    [Fact]
    public async Task StreamAsync_SelectsChatCapabilitySerializesPayloadAndStreamsChunks()
    {
        string? payload = null;
        Uri? endpoint = null;
        var handler = new MockHttpMessageHandler(async (request, _) =>
        {
            endpoint = request.RequestUri;
            payload = await request.Content!.ReadAsStringAsync();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "data: {\"choices\":[{\"delta\":{\"content\":\"Halo\"}}]}\n\n" +
                    "data: {\"choices\":[{\"delta\":{\"content\":\" Nexus\"},\"finish_reason\":\"stop\"}]}\n\n" +
                    "data: [DONE]\n\n")
            };
        });
        using var httpClient = new HttpClient(handler);
        var policy = new RecordingPolicy(new ResolvedModelDescriptor
        {
            ModelId = "registered-chat-model",
            ModelType = ModelType.Gguf
        });
        await using var engine = new StubLlamaEngine(8123);
        var pipeline = new LlamaConversationPipeline(httpClient, policy, engine);
        var chunks = new List<LlamaStreamChunk>();

        await foreach (var chunk in pipeline.StreamAsync(
            [new ChatMessagePayload { Role = "user", Content = "Halo" }],
            enableThinking: true))
        {
            chunks.Add(chunk);
        }

        Assert.Equal(AICapability.ChatCompletion, policy.Capability);
        Assert.Equal(
            "http://127.0.0.1:8123/v1/chat/completions",
            endpoint!.AbsoluteUri);
        using var document = JsonDocument.Parse(payload!);
        Assert.Equal(
            "registered-chat-model",
            document.RootElement.GetProperty("model").GetString());
        Assert.True(document.RootElement.GetProperty("stream").GetBoolean());
        Assert.True(
            document.RootElement
                .GetProperty("chat_template_kwargs")
                .GetProperty("enable_thinking")
                .GetBoolean());
        Assert.Equal(["Halo", " Nexus"], chunks.Select(chunk => chunk.Content));
        Assert.Equal("stop", chunks[1].FinishReason);
    }

    [Fact]
    public async Task StreamAsync_ParsesReasoningContentAndSerializesSamplingOptions()
    {
        string? payload = null;
        var handler = new MockHttpMessageHandler(async (request, _) =>
        {
            payload = await request.Content!.ReadAsStringAsync();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "data: {\"choices\":[{\"delta\":{\"reasoning_content\":\"Analisis masalah\"}}]}\n\n" +
                    "data: {\"choices\":[{\"delta\":{\"reasoning_content\":\" langkah demi langkah.\"}}]}\n\n" +
                    "data: {\"choices\":[{\"delta\":{\"content\":\"Jawaban final.\"}}]}\n\n" +
                    "data: [DONE]\n\n")
            };
        });
        using var httpClient = new HttpClient(handler);
        var policy = new RecordingPolicy(new ResolvedModelDescriptor
        {
            ModelId = "gemma-4-e4b",
            ModelType = ModelType.Gguf
        });
        await using var engine = new StubLlamaEngine(8080);
        var pipeline = new LlamaConversationPipeline(httpClient, policy, engine);
        var chunks = new List<LlamaStreamChunk>();

        var options = new LlamaGenerationOptions
        {
            MaxTokens = 1024
        };

        await foreach (var chunk in pipeline.StreamAsync(
            [new ChatMessagePayload { Role = "user", Content = "Pertanyaan" }],
            enableThinking: true,
            generationOptions: options))
        {
            chunks.Add(chunk);
        }

        using var document = JsonDocument.Parse(payload!);
        var root = document.RootElement;
        Assert.True(root.GetProperty("chat_template_kwargs").GetProperty("enable_thinking").GetBoolean());
        Assert.Equal(1024, root.GetProperty("max_tokens").GetInt32());
        Assert.False(root.TryGetProperty("temperature", out _));
        Assert.False(root.TryGetProperty("top_p", out _));
        Assert.False(root.TryGetProperty("top_k", out _));
        Assert.False(root.TryGetProperty("repeat_penalty", out _));

        Assert.Equal(3, chunks.Count);
        Assert.Equal("Analisis masalah", chunks[0].ReasoningContent);
        Assert.Equal(string.Empty, chunks[0].Content);
        Assert.Equal(" langkah demi langkah.", chunks[1].ReasoningContent);
        Assert.Equal(string.Empty, chunks[1].Content);
        Assert.Equal(string.Empty, chunks[2].ReasoningContent);
        Assert.Equal("Jawaban final.", chunks[2].Content);
    }

    [Fact]
    public async Task StreamAsync_SendsExplicitSamplingParameters()
    {
        string? payload = null;
        var handler = new MockHttpMessageHandler(async (request, _) =>
        {
            payload = await request.Content!.ReadAsStringAsync();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("data: [DONE]\n\n")
            };
        });
        using var client = new HttpClient(handler);
        await using var engine = new StubLlamaEngine(8080);
        var pipeline = new LlamaConversationPipeline(client,
            new RecordingPolicy(new ResolvedModelDescriptor { ModelId = "local", ModelType = ModelType.Gguf }),
            engine);

        await foreach (var _ in pipeline.StreamAsync(
            [new ChatMessagePayload { Content = "Halo" }],
            generationOptions: new LlamaGenerationOptions { Temperature = 0.7, TopP = 0.9, TopK = 40, MinP = 0.05 }))
        {
        }

        using var json = JsonDocument.Parse(payload!);
        Assert.Equal(0.7, json.RootElement.GetProperty("temperature").GetDouble());
        Assert.Equal(0.9, json.RootElement.GetProperty("top_p").GetDouble());
        Assert.Equal(40, json.RootElement.GetProperty("top_k").GetInt32());
        Assert.Equal(0.05, json.RootElement.GetProperty("min_p").GetDouble());
    }

    [Fact]
    public async Task StreamAsync_WhenThinkingDisabled_SendsEnableThinkingFalse_AndNoSamplingParameters()
    {
        string? payload = null;
        var handler = new MockHttpMessageHandler(async (request, _) =>
        {
            payload = await request.Content!.ReadAsStringAsync();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("data: [DONE]\n\n")
            };
        });
        using var httpClient = new HttpClient(handler);
        var policy = new RecordingPolicy(new ResolvedModelDescriptor
        {
            ModelId = "gemma-4-e2b",
            ModelType = ModelType.Gguf
        });
        await using var engine = new StubLlamaEngine(8080);
        var pipeline = new LlamaConversationPipeline(httpClient, policy, engine);

        var options = new LlamaGenerationOptions
        {
            MaxTokens = 2048
        };

        await foreach (var _ in pipeline.StreamAsync(
            [new ChatMessagePayload { Role = "user", Content = "Halo" }],
            enableThinking: false,
            generationOptions: options))
        {
        }

        using var document = JsonDocument.Parse(payload!);
        var root = document.RootElement;
        Assert.False(root.GetProperty("chat_template_kwargs").GetProperty("enable_thinking").GetBoolean());
        Assert.Equal(2048, root.GetProperty("max_tokens").GetInt32());
        Assert.False(root.TryGetProperty("temperature", out _));
        Assert.False(root.TryGetProperty("top_p", out _));
        Assert.False(root.TryGetProperty("top_k", out _));
        Assert.False(root.TryGetProperty("repeat_penalty", out _));
    }

    [Fact]
    public async Task StreamAsync_MalformedSseJsonPropagatesJsonException()
    {
        var handler = new MockHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("data: {invalid}\n\n")
            }));
        using var httpClient = new HttpClient(handler);
        await using var engine = new StubLlamaEngine(8080);
        var pipeline = new LlamaConversationPipeline(
            httpClient,
            new RecordingPolicy(new ResolvedModelDescriptor
            {
                ModelType = ModelType.Gguf
            }),
            engine);

        await Assert.ThrowsAnyAsync<JsonException>(async () =>
        {
            await foreach (var _ in pipeline.StreamAsync(
                [new ChatMessagePayload { Content = "test" }]))
            {
            }
        });
    }

    [Fact]
    public async Task StreamAsync_DoesNotSanitizeSpecialTokens()
    {
        var sseContent = "data: {\"choices\":[{\"delta\":{\"content\":\"<unused24>\"}}]}\n\n" +
                         "data: [DONE]\n\n";
        var handler = new MockHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(sseContent)
            }));
        using var httpClient = new HttpClient(handler);
        await using var engine = new StubLlamaEngine(8080);
        var pipeline = new LlamaConversationPipeline(
            httpClient,
            new RecordingPolicy(new ResolvedModelDescriptor
            {
                ModelType = ModelType.Gguf
            }),
            engine);

        var chunks = new List<LlamaStreamChunk>();
        await foreach (var chunk in pipeline.StreamAsync(
            [new ChatMessagePayload { Content = "test" }]))
        {
            chunks.Add(chunk);
        }

        Assert.Single(chunks);
        Assert.Equal("<unused24>", chunks[0].Content);
    }

    private sealed class RecordingPolicy(ResolvedModelDescriptor descriptor)
        : IModelSelectionPolicy
    {
        public AICapability Capability { get; private set; }

        public ResolvedModelDescriptor SelectModel(
            AICapability capability,
            EngineSessionOptions sessionOptions)
        {
            Capability = capability;
            return descriptor;
        }
    }

    private sealed class StubLlamaEngine(int port) : IGenericLlamaEngine
    {
        public string EngineName => "Test";

        public bool IsLoaded => true;

        public string? ActiveModelPath => null;

        public int Port { get; } = port;

        public Task LoadEngineAsync() => Task.CompletedTask;

        public Task<bool> StartServerAsync(string modelPath, int contextSize = 8192) =>
            Task.FromResult(true);

        public Task StopServerAsync() => Task.CompletedTask;

        public Task UnloadModelAsync(string sessionKey) => Task.CompletedTask;

        public Task UnloadAllModelsAsync() => Task.CompletedTask;

        public EngineMemoryMetrics GetMemoryMetrics() => new();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
