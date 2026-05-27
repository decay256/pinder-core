using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Pinder.LlmAdapters.Anthropic;
using Xunit;

namespace Pinder.LlmAdapters.Tests.Anthropic
{
    public sealed class AnthropicTransportTests
    {
        private const string TestApiKey = "sk-ant-test-key";
        private const string TestModel = "claude-sonnet-4-20250514";

        private sealed class CapturingHandler : HttpMessageHandler
        {
            public HttpRequestMessage? LastRequest { get; private set; }
            public string? LastRequestBody { get; private set; }
            private readonly string _responseJson;

            public CapturingHandler(string responseJson)
            {
                _responseJson = responseJson;
            }

            protected override async Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, System.Threading.CancellationToken cancellationToken)
            {
                LastRequest = request;
                if (request.Content != null)
                {
                    LastRequestBody = await request.Content.ReadAsStringAsync().ConfigureAwait(false);
                }
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(_responseJson, Encoding.UTF8, "application/json")
                };
            }
        }

        [Fact]
        public async Task SendAsync_SystemBlocks_HaveCacheControlEphemeral()
        {
            string cannedResponse = "{\"id\":\"msg_01\",\"type\":\"message\",\"role\":\"assistant\",\"content\":[{\"type\":\"text\",\"text\":\"mocked response\"}]}";
            var handler = new CapturingHandler(cannedResponse);
            using var http = new HttpClient(handler);
            using var transport = new AnthropicTransport(TestApiKey, TestModel, http);

            var responseText = await transport.SendAsync("sysprompt-value", "usermsg-value");

            Assert.Equal("mocked response", responseText);
            Assert.NotNull(handler.LastRequestBody);
            Assert.Contains("\"cache_control\"", handler.LastRequestBody);
            Assert.Contains("\"type\":\"ephemeral\"", handler.LastRequestBody);
            Assert.Contains("sysprompt-value", handler.LastRequestBody);
        }
    }
}
