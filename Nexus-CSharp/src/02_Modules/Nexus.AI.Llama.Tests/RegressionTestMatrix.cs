using System.Net;
using System.Text.Json;
using Nexus.AI.Llama.Services;
using Xunit;

namespace Nexus.AI.Llama.Tests
{
    public class RegressionTestMatrix
    {
        [Theory]
        [InlineData("Halo")]
        [InlineData("Siapa kamu?")]
        [InlineData("Selamat malam")]
        [InlineData("Bagi")]
        public async Task ServerOnline_EveryPrompt_IsPostedAndOnlyServerResponseIsStreamed(string prompt)
        {
            string? requestJson = null;
            var serverText = $"gemma-response:{prompt}";
            var sseJson = JsonSerializer.Serialize(new
            {
                choices = new[] { new { delta = new { content = serverText } } }
            });

            var handler = new MockHttpMessageHandler(async (request, token) =>
            {
                requestJson = await request.Content!.ReadAsStringAsync(token);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent($"data: {sseJson}\n\ndata: [DONE]\n\n")
                };
            });
            using var httpClient = new HttpClient(handler);
            using var service = new LlamaClientService(httpClient);
            var result = new List<string>();

            await foreach (var chunk in service.StreamChatCompletionsAsync(prompt))
            {
                result.Add(chunk.Content);
            }

            Assert.NotNull(requestJson);
            using var requestDocument = JsonDocument.Parse(requestJson);
            var postedPrompt = requestDocument.RootElement
                .GetProperty("messages")[0]
                .GetProperty("content")
                .GetString();
            Assert.Equal(prompt, postedPrompt);
            Assert.Equal(serverText, string.Concat(result));
        }

        [Fact]
        public void AssistantFallbackComponents_DoNotExist()
        {
            var assembly = typeof(LlamaClientService).Assembly;

            Assert.Null(assembly.GetType("Nexus.AI.Llama.Services.IntentDetector"));
            Assert.Null(assembly.GetType("Nexus.AI.Llama.Services.FallbackResponseProvider"));
        }
    }
}
