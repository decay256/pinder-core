using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Pinder.Core.Interfaces;
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

        private sealed class SequenceCapturingHandler : HttpMessageHandler
        {
            private readonly Queue<string> _responses;

            public SequenceCapturingHandler(params string[] responseJson)
            {
                _responses = new Queue<string>(responseJson);
            }

            public List<string> RequestBodies { get; } = new List<string>();

            protected override async Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, System.Threading.CancellationToken cancellationToken)
            {
                RequestBodies.Add(request.Content == null
                    ? ""
                    : await request.Content.ReadAsStringAsync().ConfigureAwait(false));

                string responseJson = _responses.Count == 0
                    ? "{\"id\":\"msg_empty\",\"type\":\"message\",\"role\":\"assistant\",\"content\":[]}"
                    : _responses.Dequeue();

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
                };
            }
        }

        private sealed class ErrorHandler : HttpMessageHandler
        {
            private readonly HttpStatusCode _statusCode;

            public ErrorHandler(HttpStatusCode statusCode)
            {
                _statusCode = statusCode;
            }

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, System.Threading.CancellationToken cancellationToken)
            {
                var response = new HttpResponseMessage(_statusCode)
                {
                    Content = new StringContent("{\"error\":\"transient\"}", Encoding.UTF8, "application/json")
                };
                response.Headers.TryAddWithoutValidation("Retry-After", "0");
                return Task.FromResult(response);
            }
        }

        private sealed class ThrowingHandler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, System.Threading.CancellationToken cancellationToken)
            {
                throw new HttpRequestException("connection failed");
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

        [Fact]
        public async Task SendAsync_WithConfiguredImprovementPrompt_PerformsToolBackedImprovementPass()
        {
            string draftResponse = "{\"id\":\"msg_01\",\"type\":\"message\",\"role\":\"assistant\",\"content\":[{\"type\":\"text\",\"text\":\"draft line\"}]}";
            string improvementResponse = "{\"id\":\"msg_02\",\"type\":\"message\",\"role\":\"assistant\",\"content\":[{\"type\":\"tool_use\",\"id\":\"toolu_01\",\"name\":\"submit_improvement\",\"input\":{\"improved\":\"improved line\"}}]}";
            var handler = new SequenceCapturingHandler(draftResponse, improvementResponse);
            using var http = new HttpClient(handler);
            using var transport = new AnthropicTransport(new AnthropicOptions
            {
                ApiKey = TestApiKey,
                Model = TestModel,
                GameDefinition = CreateGameDefinition("Improve the draft and return the final text.")
            }, http);

            var responseText = await transport.SendAsync(
                "sysprompt-value",
                "usermsg-value",
                temperature: 0.42,
                maxTokens: 321);

            Assert.Equal("improved line", responseText);
            Assert.Equal(2, handler.RequestBodies.Count);

            var improveRequest = JObject.Parse(handler.RequestBodies[1]);
            Assert.Equal(TestModel, improveRequest.Value<string>("model"));
            Assert.Equal(321, improveRequest.Value<int>("max_tokens"));
            Assert.Equal(0.42, improveRequest.Value<double>("temperature"));
            Assert.Equal("submit_improvement", improveRequest["tools"]![0]!.Value<string>("name"));
            Assert.Equal("any", improveRequest["tool_choice"]!.Value<string>("type"));
            Assert.Equal("user", improveRequest["messages"]![0]!.Value<string>("role"));
            Assert.Equal("usermsg-value", improveRequest["messages"]![0]!["content"]!.Value<string>());
            Assert.Equal("assistant", improveRequest["messages"]![1]!.Value<string>("role"));
            Assert.Equal("draft line", improveRequest["messages"]![1]!["content"]!.Value<string>());
            Assert.Equal("Improve the draft and return the final text.", improveRequest["messages"]![2]!["content"]!.Value<string>());
        }

        [Fact]
        public async Task SendAsync_WithOptionsButNoImprovementPrompt_SendsOnlyDraftRequest()
        {
            string draftResponse = "{\"id\":\"msg_01\",\"type\":\"message\",\"role\":\"assistant\",\"content\":[{\"type\":\"text\",\"text\":\"draft line\"}]}";
            var handler = new SequenceCapturingHandler(draftResponse);
            using var http = new HttpClient(handler);
            using var transport = new AnthropicTransport(new AnthropicOptions
            {
                ApiKey = TestApiKey,
                Model = TestModel,
                GameDefinition = CreateGameDefinition("")
            }, http);

            var responseText = await transport.SendAsync("sysprompt-value", "usermsg-value");

            Assert.Equal("draft line", responseText);
            Assert.Single(handler.RequestBodies);
            Assert.DoesNotContain("\"tools\"", handler.RequestBodies[0]);
        }

        [Theory]
        [InlineData(LlmPhase.Delivery)]
        [InlineData(LlmPhase.HorninessOverlay)]
        [InlineData(LlmPhase.ShadowCorruption)]
        [InlineData(LlmPhase.TrapOverlay)]
        public async Task SendAsync_RewritePhase_DoesNotRunGenericImprovement(string phase)
        {
            string draftResponse = "{\"id\":\"msg_01\",\"type\":\"message\",\"role\":\"assistant\",\"content\":[{\"type\":\"text\",\"text\":\"rewritten line\"}]}";
            var handler = new SequenceCapturingHandler(draftResponse);
            using var http = new HttpClient(handler);
            using var transport = new AnthropicTransport(new AnthropicOptions
            {
                ApiKey = TestApiKey,
                Model = TestModel,
                GameDefinition = CreateGameDefinition("Improve the draft.")
            }, http);

            var responseText = await transport.SendAsync(
                "sysprompt-value",
                "usermsg-value",
                phase: phase);

            Assert.Equal("rewritten line", responseText);
            Assert.Single(handler.RequestBodies);
        }

        [Theory]
        [InlineData(429, LlmFailureKind.RateLimited)]
        [InlineData(503, LlmFailureKind.Network)]
        public async Task SendAsync_HttpProviderFailure_ThrowsTypedTransportException(
            int statusCode,
            LlmFailureKind expectedKind)
        {
            using var http = new HttpClient(new ErrorHandler((HttpStatusCode)statusCode));
            using var transport = new AnthropicTransport(TestApiKey, TestModel, http);

            var ex = await Assert.ThrowsAsync<LlmTransportException>(
                () => transport.SendAsync("sys", "user"));

            Assert.Equal(expectedKind, ex.FailureKind);
        }

        [Fact]
        public async Task SendAsync_NetworkFailure_ThrowsTypedTransportException()
        {
            using var http = new HttpClient(new ThrowingHandler());
            using var transport = new AnthropicTransport(TestApiKey, TestModel, http);

            var ex = await Assert.ThrowsAsync<LlmTransportException>(
                () => transport.SendAsync("sys", "user"));

            Assert.Equal(LlmFailureKind.Network, ex.FailureKind);
        }

        private static GameDefinition CreateGameDefinition(string improvementPrompt)
        {
            return new GameDefinition(
                name: "Pinder",
                gameMasterPrompt: "gm prompt",
                playerAvatarRoleDescription: "player avatar",
                dateeRoleDescription: "datee",
                improvementPrompt: improvementPrompt);
        }
    }
}
