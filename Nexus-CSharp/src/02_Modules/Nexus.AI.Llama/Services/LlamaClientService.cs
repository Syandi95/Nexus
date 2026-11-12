using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Nexus.AI.Llama.Models;
using Nexus.AI.Llama.Transport;

namespace Nexus.AI.Llama.Services
{
    public class LlamaClientService : ILlamaClientService, IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly ILlamaServerManager? _serverManager;
        private readonly string _endpointUrl;
        private readonly object _cancellationLock = new();
        private readonly HashSet<CancellationTokenSource> _activeCancellationSources = new();
        private bool _isDisposed;

        public LlamaClientService(HttpClient httpClient, ILlamaServerManager? serverManager = null, string endpointUrl = "http://127.0.0.1:8080/v1/chat/completions")
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _serverManager = serverManager;
            _endpointUrl = !string.IsNullOrWhiteSpace(endpointUrl) ? endpointUrl : "http://127.0.0.1:8080/v1/chat/completions";
        }

        public async IAsyncEnumerable<LlamaStreamChunk> StreamChatCompletionsAsync(
            string prompt, 
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var messages = new[]
            {
                new ChatMessagePayload { Role = "user", Content = prompt }
            };
            await foreach (var chunk in StreamChatCompletionsAsync(messages, cancellationToken))
            {
                yield return chunk;
            }
        }

        public async IAsyncEnumerable<LlamaStreamChunk> StreamChatCompletionsAsync(
            IEnumerable<ChatMessagePayload> messages,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await foreach (var chunk in StreamChatCompletionsAsync(messages, false, cancellationToken))
            {
                yield return chunk;
            }
        }

        public async IAsyncEnumerable<LlamaStreamChunk> StreamChatCompletionsAsync(
            IEnumerable<ChatMessagePayload> messages,
            bool enableThinking,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await foreach (var chunk in StreamChatCompletionsAsync(messages, enableThinking, (int?)null, cancellationToken))
            {
                yield return chunk;
            }
        }

        public IAsyncEnumerable<LlamaStreamChunk> StreamChatCompletionsAsync(
            IEnumerable<ChatMessagePayload> messages,
            bool enableThinking,
            int? maxTokens,
            CancellationToken cancellationToken = default)
            => StreamChatCompletionsAsync(messages, enableThinking,
                new LlamaGenerationOptions { MaxTokens = maxTokens }, cancellationToken);

        public async IAsyncEnumerable<LlamaStreamChunk> StreamChatCompletionsAsync(
            IEnumerable<ChatMessagePayload> messages,
            bool enableThinking,
            LlamaGenerationOptions options,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            CancellationTokenSource localCts;
            lock (_cancellationLock)
            {
                if (_isDisposed)
                {
                    throw new ObjectDisposedException(nameof(LlamaClientService));
                }

                localCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                _activeCancellationSources.Add(localCts);
            }

            try
            {
                var token = localCts.Token;

                System.Diagnostics.Debug.WriteLine($"[LLM] Request Started -> {_endpointUrl}");
                Console.WriteLine($"[LLM] Request Started -> {_endpointUrl}");

                var requestBody = LlamaTransportHelper.CreateChatPayload("local", messages, enableThinking,
                    options.MaxTokens, options.Temperature, options.TopP, options.TopK, options.MinP);
                var jsonContent = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
                using var request = new HttpRequestMessage(HttpMethod.Post, _endpointUrl);
                request.Content = jsonContent;

                using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
                System.Diagnostics.Debug.WriteLine($"[LLM] Connected (Status: {response.StatusCode})");
                Console.WriteLine($"[LLM] Connected (Status: {response.StatusCode})");

                response.EnsureSuccessStatusCode();

                using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
                using var reader = new StreamReader(stream);

                long startTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                bool isFirstToken = true;
                long timeToFirstToken = 0;
                bool serverCompleted = false;

                string? line;
                while ((line = await reader.ReadLineAsync(token).ConfigureAwait(false)) != null)
                {
                    token.ThrowIfCancellationRequested();

                    if (string.IsNullOrWhiteSpace(line)) continue;

                    if (LlamaTransportHelper.TryExtractSseData(line, out var data))
                    {
                        if (LlamaTransportHelper.IsDone(data))
                        {
                            serverCompleted = true;
                            break;
                        }

                        using var jsonDoc = JsonDocument.Parse(data);
                        if (!LlamaTransportHelper.TryParseChunk(jsonDoc.RootElement, out var chunk, sanitizeSpecialTokens: true))
                        {
                            continue;
                        }

                        if (string.IsNullOrEmpty(chunk.Content) && string.IsNullOrEmpty(chunk.ReasoningContent))
                        {
                            continue;
                        }

                        if (isFirstToken)
                        {
                            timeToFirstToken = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - startTime;
                            isFirstToken = false;
                            System.Diagnostics.Debug.WriteLine($"[LLM] First Token Received (TTFT: {timeToFirstToken}ms)");
                            Console.WriteLine($"[LLM] First Token Received (TTFT: {timeToFirstToken}ms)");
                        }

                        chunk.TimeToFirstTokenMs = timeToFirstToken;
                        yield return chunk;
                    }
                }

                if (!serverCompleted)
                {
                    token.ThrowIfCancellationRequested();
                }
            }
            finally
            {
                lock (_cancellationLock)
                {
                    _activeCancellationSources.Remove(localCts);
                }
                try
                {
                    localCts.Dispose();
                }
                catch (ObjectDisposedException)
                {
                }
            }
        }

        public void CancelStreaming()
        {
            List<CancellationTokenSource> activeSources;
            lock (_cancellationLock)
            {
                activeSources = new List<CancellationTokenSource>(_activeCancellationSources);
            }

            foreach (var cts in activeSources)
            {
                try
                {
                    cts.Cancel();
                }
                catch (ObjectDisposedException)
                {
                }
            }
        }

        public void Dispose()
        {
            List<CancellationTokenSource> activeSources;
            lock (_cancellationLock)
            {
                if (_isDisposed)
                {
                    return;
                }

                _isDisposed = true;
                activeSources = new List<CancellationTokenSource>(_activeCancellationSources);
                _activeCancellationSources.Clear();
            }

            foreach (var cts in activeSources)
            {
                try
                {
                    cts.Cancel();
                }
                catch (ObjectDisposedException)
                {
                }
                try
                {
                    cts.Dispose();
                }
                catch (ObjectDisposedException)
                {
                }
            }
        }
    }
}
