using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using System.Collections.Generic;
using Nexus.AI.Llama.Models;
using Nexus.AI.Llama.Services;
using Nexus.Contracts.AiCheck;
using Xunit;

namespace Nexus.AI.Llama.Tests
{
    public class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _sendAsync;

        public MockHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> sendAsync)
        {
            _sendAsync = sendAsync;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return _sendAsync(request, cancellationToken);
        }
    }

    public class LlamaClientServiceTests
    {
        [Fact]
        public async Task StreamChatCompletionsAsync_DeepThinkOff_SendsEnableThinkingFalse()
        {
            string? payload = null;
            using var service = CreateRecordingService(body => payload = body);

            await foreach (var _ in service.StreamChatCompletionsAsync(
                new[] { new ChatMessagePayload { Role = "user", Content = "Halo" } }))
            {
            }

            using var document = JsonDocument.Parse(Assert.IsType<string>(payload));
            var templateArguments = document.RootElement.GetProperty("chat_template_kwargs");
            Assert.False(templateArguments.GetProperty("enable_thinking").GetBoolean());
        }

        [Fact]
        public async Task StreamChatCompletionsAsync_DeepThinkOn_SendsEnableThinkingTrue()
        {
            string? payload = null;
            using var service = CreateRecordingService(body => payload = body);

            await foreach (var _ in service.StreamChatCompletionsAsync(
                new[] { new ChatMessagePayload { Role = "user", Content = "Jelaskan algoritma." } },
                enableThinking: true))
            {
            }

            using var document = JsonDocument.Parse(Assert.IsType<string>(payload));
            var templateArguments = document.RootElement.GetProperty("chat_template_kwargs");
            Assert.True(templateArguments.GetProperty("enable_thinking").GetBoolean());
        }

        [Fact]
        public async Task StreamChatCompletionsAsync_WhenSuccessful_ShouldStreamChunks()
        {
            // Arrange
            var sseContent = "data: {\"choices\":[{\"delta\":{\"content\":\"Hello\"}}]}\n\n" +
                             "data: {\"choices\":[{\"delta\":{\"content\":\" world\"}}]}\n\n" +
                             "data: [DONE]\n\n";

            var handler = new MockHttpMessageHandler((req, token) =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(sseContent)
                };
                return Task.FromResult(response);
            });

            var httpClient = new HttpClient(handler)
            {
                BaseAddress = new Uri("http://127.0.0.1:8080")
            };

            var service = new LlamaClientService(httpClient);

            // Act
            var chunks = service.StreamChatCompletionsAsync("Hello");
            var resultList = new System.Collections.Generic.List<string>();

            await foreach (var chunk in chunks)
            {
                resultList.Add(chunk.Content);
            }

            // Assert
            Assert.Equal(2, resultList.Count);
            Assert.Equal("Hello", resultList[0]);
            Assert.Equal(" world", resultList[1]);
        }

        [Fact]
        public async Task StreamChatCompletionsAsync_WhenHttpRequestExceptionOccurs_ShouldPropagateFailure()
        {
            // Arrange
            var handler = new MockHttpMessageHandler((req, token) =>
            {
                throw new HttpRequestException("Connection refused");
            });

            var httpClient = new HttpClient(handler)
            {
                BaseAddress = new Uri("http://127.0.0.1:8080")
            };

            var service = new LlamaClientService(httpClient);

            // Act + Assert
            await Assert.ThrowsAsync<HttpRequestException>(async () =>
            {
                await foreach (var _ in service.StreamChatCompletionsAsync("Test prompt"))
                {
                }
            });
        }

        [Fact]
        public async Task StreamChatCompletionsAsync_WhenCancelled_ShouldPropagateCancellation()
        {
            var handler = new MockHttpMessageHandler((req, token) =>
                Task.FromCanceled<HttpResponseMessage>(token));
            using var httpClient = new HttpClient(handler);
            using var service = new LlamaClientService(httpClient);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            {
                await foreach (var _ in service.StreamChatCompletionsAsync("Halo", cancellation.Token))
                {
                }
            });
        }

        [Fact]
        public async Task StreamChatCompletionsAsync_WhenServerSendsMalformedJson_ShouldReportFailure()
        {
            var handler = new MockHttpMessageHandler((req, token) =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("data: {not-json}\n\n")
                }));
            using var httpClient = new HttpClient(handler);
            using var service = new LlamaClientService(httpClient);

            await Assert.ThrowsAnyAsync<JsonException>(async () =>
            {
                await foreach (var _ in service.StreamChatCompletionsAsync("Halo"))
                {
                }
            });
        }

        [Fact]
        public async Task StreamChatCompletionsAsync_WhenCancelledMidStream_ShouldPropagateCancellation()
        {
            var sseContent = "data: {\"choices\":[{\"delta\":{\"content\":\"partial\"}}]}\n\n";
            var handler = new MockHttpMessageHandler((req, token) =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(sseContent)
                }));
            using var httpClient = new HttpClient(handler);
            using var service = new LlamaClientService(httpClient);
            using var cancellation = new CancellationTokenSource();
            await using var enumerator = service
                .StreamChatCompletionsAsync("Halo", cancellation.Token)
                .GetAsyncEnumerator();

            Assert.True(await enumerator.MoveNextAsync());
            Assert.Equal("partial", enumerator.Current.Content);
            cancellation.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                await enumerator.MoveNextAsync().AsTask());
        }

        [Fact]
        public async Task StreamChatCompletionsAsync_ConcurrentRequests_ExecuteIndependentlyWithoutInterference()
        {
            var handler = new MockHttpMessageHandler(async (req, token) =>
            {
                var content = await req.Content!.ReadAsStringAsync();
                string responseText;
                if (content.Contains("Request1"))
                {
                    responseText = "data: {\"choices\":[{\"delta\":{\"content\":\"Chunk1A\"}}]}\n\n" +
                                   "data: {\"choices\":[{\"delta\":{\"content\":\"Chunk1B\"}}]}\n\n" +
                                   "data: [DONE]\n\n";
                }
                else
                {
                    responseText = "data: {\"choices\":[{\"delta\":{\"content\":\"Chunk2A\"}}]}\n\n" +
                                   "data: {\"choices\":[{\"delta\":{\"content\":\"Chunk2B\"}}]}\n\n" +
                                   "data: [DONE]\n\n";
                }

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(responseText)
                };
            });

            using var httpClient = new HttpClient(handler);
            using var service = new LlamaClientService(httpClient);

            var task1 = Task.Run(async () =>
            {
                var list = new List<string>();
                await foreach (var chunk in service.StreamChatCompletionsAsync("Request1"))
                {
                    list.Add(chunk.Content);
                }
                return list;
            });

            var task2 = Task.Run(async () =>
            {
                var list = new List<string>();
                await foreach (var chunk in service.StreamChatCompletionsAsync("Request2"))
                {
                    list.Add(chunk.Content);
                }
                return list;
            });

            var results = await Task.WhenAll(task1, task2);
            Assert.Equal(new[] { "Chunk1A", "Chunk1B" }, results[0]);
            Assert.Equal(new[] { "Chunk2A", "Chunk2B" }, results[1]);
        }

        [Fact]
        public async Task StreamChatCompletionsAsync_CancelOneRequest_DoesNotCancelConcurrentRequest()
        {
            var req1Started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var req1CanProceed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            var handler = new MockHttpMessageHandler(async (req, token) =>
            {
                var content = await req.Content!.ReadAsStringAsync();
                if (content.Contains("Request1"))
                {
                    req1Started.TrySetResult(true);
                    using var reg = token.Register(() => req1CanProceed.TrySetCanceled(token));
                    await req1CanProceed.Task;
                    token.ThrowIfCancellationRequested();
                }

                var responseText = "data: {\"choices\":[{\"delta\":{\"content\":\"Response2\"}}]}\n\n" +
                                   "data: [DONE]\n\n";
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(responseText)
                };
            });

            using var httpClient = new HttpClient(handler);
            using var service = new LlamaClientService(httpClient);

            using var cts1 = new CancellationTokenSource();
            using var cts2 = new CancellationTokenSource();

            var task1 = Task.Run(async () =>
            {
                var list = new List<string>();
                await foreach (var chunk in service.StreamChatCompletionsAsync("Request1", cts1.Token))
                {
                    list.Add(chunk.Content);
                }
                return list;
            });

            await req1Started.Task;

            var task2 = Task.Run(async () =>
            {
                var list = new List<string>();
                await foreach (var chunk in service.StreamChatCompletionsAsync("Request2", cts2.Token))
                {
                    list.Add(chunk.Content);
                }
                return list;
            });

            cts1.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task1);

            var result2 = await task2;
            Assert.Single(result2);
            Assert.Equal("Response2", result2[0]);
        }

        [Fact]
        public async Task CancelStreaming_CancelsAllActiveConcurrentRequests()
        {
            var req1Started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var req2Started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            var handler = new MockHttpMessageHandler(async (req, token) =>
            {
                var content = await req.Content!.ReadAsStringAsync();
                if (content.Contains("Request1"))
                {
                    req1Started.TrySetResult(true);
                }
                else
                {
                    req2Started.TrySetResult(true);
                }

                var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                using var reg = token.Register(() => tcs.TrySetCanceled(token));
                await tcs.Task;
                token.ThrowIfCancellationRequested();

                return new HttpResponseMessage(HttpStatusCode.OK);
            });

            using var httpClient = new HttpClient(handler);
            using var service = new LlamaClientService(httpClient);

            var task1 = Task.Run(async () =>
            {
                await foreach (var _ in service.StreamChatCompletionsAsync("Request1"))
                {
                }
            });

            var task2 = Task.Run(async () =>
            {
                await foreach (var _ in service.StreamChatCompletionsAsync("Request2"))
                {
                }
            });

            await Task.WhenAll(req1Started.Task, req2Started.Task);

            service.CancelStreaming();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task1);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task2);
        }

        [Fact]
        public async Task AiCheckJudgeService_WithRealLlamaClientService_EvaluatesSuccessfully()
        {
            var handler = new MockHttpMessageHandler((req, token) =>
            {
                var responseText = "data: {\"choices\":[{\"delta\":{\"content\":\"[32_0]: TYPO | Surat Izin Iklan\\n\"}}]}\n\n" +
                                   "data: [DONE]\n\n";
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(responseText)
                });
            });

            using var httpClient = new HttpClient(handler);
            using var llamaService = new LlamaClientService(httpClient);
            var judgeService = new AiCheckJudgeService(llamaService);

            var candidate = new TriageCandidateDto
            {
                CandidateId = "32_0",
                BoxId = 32,
                OriginalText = "Surat ljin Iklan",
                PossibleCorrections = new List<string> { "Surat Izin Iklan" },
                SpatialContext = "BANNER Surat ljin Iklan RESMI",
                RecConfidence = 0.95f,
                G3LowMargin = 0.50,
                PrimaryScore = 0.50
            };

            var judgment = await judgeService.EvaluateSingleCandidateAsync("Banner", candidate);

            Assert.NotNull(judgment);
            Assert.Equal("32_0", judgment.CandidateId);
            Assert.Equal(AiCheckJudgmentStatus.Koreksi, judgment.Status);
            Assert.Equal("Surat Izin Iklan", judgment.RecommendedText);
        }

        [Fact]
        public async Task StreamChatCompletionsAsync_SanitizesSpecialTokens_AndSkipsEmptyChunks()
        {
            var sseContent = "data: {\"choices\":[{\"delta\":{\"content\":\"<unused24>\"}}]}\n\n" +
                             "data: {\"choices\":[{\"delta\":{\"content\":\"_model\"}}]}\n\n" +
                             "data: {\"choices\":[{\"delta\":{\"content\":\"Real text\"}}]}\n\n" +
                             "data: [DONE]\n\n";

            var handler = new MockHttpMessageHandler((req, token) =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(sseContent)
                }));

            using var httpClient = new HttpClient(handler);
            using var service = new LlamaClientService(httpClient);

            var chunks = new List<string>();
            await foreach (var chunk in service.StreamChatCompletionsAsync("Test"))
            {
                chunks.Add(chunk.Content);
            }

            Assert.Single(chunks);
            Assert.Equal("Real text", chunks[0]);
        }

        private static LlamaClientService CreateRecordingService(Action<string> capture)
        {
            var handler = new MockHttpMessageHandler(async (request, _) =>
            {
                capture(await request.Content!.ReadAsStringAsync());
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("data: [DONE]\n\n")
                };
            });
            return new LlamaClientService(new HttpClient(handler));
        }
    }
}
