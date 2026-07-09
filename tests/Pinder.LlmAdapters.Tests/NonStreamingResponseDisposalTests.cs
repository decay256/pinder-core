using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Pinder.LlmAdapters.Anthropic;
using Pinder.LlmAdapters.Anthropic.Dto;
using Pinder.LlmAdapters.OpenAi;
using Xunit;

namespace Pinder.LlmAdapters.Tests
{
    public class NonStreamingResponseDisposalTests
    {
        [Fact]
        public async Task AnthropicClient_DisposesResponse_OnSuccess()
        {
            var content = JsonContent(AnthropicSuccessBody("ok"));
            var handler = new QueueHandler(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
            using var http = new HttpClient(handler);
            using var client = new AnthropicClient("sk-ant-test", http);

            var result = await client.SendMessagesAsync(AnthropicRequest());

            Assert.Equal("ok", result.Content[0].Text);
            Assert.True(content.Disposed);
        }

        [Fact]
        public async Task AnthropicClient_DisposesResponses_OnRetryThenSuccess()
        {
            var retryContent = JsonContent("{\"error\":\"rate_limited\"}");
            var retry = new HttpResponseMessage((HttpStatusCode)429) { Content = retryContent };
            retry.Headers.TryAddWithoutValidation("Retry-After", "0");
            var successContent = JsonContent(AnthropicSuccessBody("recovered"));
            var handler = new QueueHandler(
                retry,
                new HttpResponseMessage(HttpStatusCode.OK) { Content = successContent });
            using var http = new HttpClient(handler);
            using var client = new AnthropicClient("sk-ant-test", http);

            var result = await client.SendMessagesAsync(AnthropicRequest());

            Assert.Equal("recovered", result.Content[0].Text);
            Assert.True(retryContent.Disposed);
            Assert.True(successContent.Disposed);
        }

        [Fact]
        public async Task AnthropicClient_DisposesResponse_OnNonRetryableError()
        {
            var content = JsonContent("{\"error\":\"bad_request\"}");
            var handler = new QueueHandler(new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = content });
            using var http = new HttpClient(handler);
            using var client = new AnthropicClient("sk-ant-test", http);

            await Assert.ThrowsAsync<AnthropicApiException>(() => client.SendMessagesAsync(AnthropicRequest()));

            Assert.True(content.Disposed);
        }

        [Fact]
        public async Task OpenAiClient_DisposesResponse_OnSuccess()
        {
            var content = JsonContent(OpenAiSuccessBody("ok"));
            var handler = new QueueHandler(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
            using var http = new HttpClient(handler);
            using var client = new OpenAiClient("sk-test", "https://example.test", http);

            var result = await client.SendChatCompletionAsync("{\"model\":\"gpt-test\",\"messages\":[]}");

            Assert.Equal("ok", result);
            Assert.True(content.Disposed);
        }

        [Fact]
        public async Task OpenAiClient_DisposesResponse_OnMalformedSuccess()
        {
            var content = JsonContent("{");
            var handler = new QueueHandler(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
            using var http = new HttpClient(handler);
            using var client = new OpenAiClient("sk-test", "https://example.test", http);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                client.SendChatCompletionAsync("{\"model\":\"gpt-test\",\"messages\":[]}"));

            Assert.True(content.Disposed);
        }

        [Fact]
        public async Task OpenAiClient_DisposesResponse_OnNonRetryableError()
        {
            var content = JsonContent("{\"error\":\"bad_request\"}");
            var handler = new QueueHandler(new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = content });
            using var http = new HttpClient(handler);
            using var client = new OpenAiClient("sk-test", "https://example.test", http);

            await Assert.ThrowsAsync<HttpRequestException>(() =>
                client.SendChatCompletionAsync("{\"model\":\"gpt-test\",\"messages\":[]}"));

            Assert.True(content.Disposed);
        }

        private static MessagesRequest AnthropicRequest()
        {
            return new MessagesRequest
            {
                Model = "claude-test",
                MaxTokens = 64,
                Messages = new[] { new Message { Role = "user", Content = "hello" } }
            };
        }

        private static string AnthropicSuccessBody(string text)
        {
            return JsonConvert.SerializeObject(new MessagesResponse
            {
                Content = new[] { new ResponseContent { Type = "text", Text = text } },
                Usage = new UsageStats { InputTokens = 1, OutputTokens = 1 }
            });
        }

        private static string OpenAiSuccessBody(string text)
        {
            return "{\"choices\":[{\"message\":{\"content\":\"" + text + "\"}}]}";
        }

        private static DisposalTrackingContent JsonContent(string body)
        {
            return new DisposalTrackingContent(body);
        }

        private sealed class QueueHandler : HttpMessageHandler
        {
            private readonly Queue<HttpResponseMessage> _responses;

            public QueueHandler(params HttpResponseMessage[] responses)
            {
                _responses = new Queue<HttpResponseMessage>(responses);
            }

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Task.FromResult(_responses.Dequeue());
            }
        }

        private sealed class DisposalTrackingContent : StringContent
        {
            public DisposalTrackingContent(string content)
                : base(content, Encoding.UTF8, "application/json")
            {
            }

            public bool Disposed { get; private set; }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    Disposed = true;
                }
                base.Dispose(disposing);
            }
        }
    }
}
