using Pinder.LlmAdapters.Anthropic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace Pinder.LlmAdapters.Tests.Anthropic
{
    public class AnthropicOptionsTests
    {
        [Fact]
        public void Defaults_AreCorrect()
        {
            var options = new AnthropicOptions();

            Assert.Equal("", options.ApiKey);
            Assert.Equal("claude-sonnet-4-20250514", options.Model);
            Assert.Equal(1024, options.MaxTokens);
            Assert.Equal(0.9, options.Temperature);
            Assert.Null(options.DialogueOptionsTemperature);
            Assert.Null(options.DeliveryTemperature);
            Assert.Null(options.DateeResponseTemperature);
            Assert.Null(options.InterestChangeBeatTemperature);
        }

        [Fact]
        public async Task Options_AreConsumedByAnthropicTransportRequest()
        {
            var handler = new CapturingHandler();
            using var http = new HttpClient(handler);
            var options = new AnthropicOptions
            {
                ApiKey = "sk-test",
                Model = "claude-opus-4-20250514",
                MaxTokens = 2048,
                Temperature = 0.7
            };
            using var transport = new AnthropicTransport(options, http);

            await transport.SendAsync("system", "user", options.Temperature, options.MaxTokens);

            Assert.Contains("\"model\":\"claude-opus-4-20250514\"", handler.LastRequestBody);
            Assert.Contains("\"max_tokens\":2048", handler.LastRequestBody);
            Assert.Contains("\"temperature\":0.7", handler.LastRequestBody);
        }

        private sealed class CapturingHandler : HttpMessageHandler
        {
            public string LastRequestBody { get; private set; } = "";

            protected override async Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                System.Threading.CancellationToken cancellationToken)
            {
                LastRequestBody = request.Content == null
                    ? ""
                    : await request.Content.ReadAsStringAsync().ConfigureAwait(false);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        "{\"id\":\"msg_01\",\"type\":\"message\",\"role\":\"assistant\",\"content\":[{\"type\":\"text\",\"text\":\"ok\"}]}",
                        Encoding.UTF8,
                        "application/json")
                };
            }
        }
    }
}
