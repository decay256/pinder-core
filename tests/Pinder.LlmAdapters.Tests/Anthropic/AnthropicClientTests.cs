using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Pinder.LlmAdapters.Anthropic;
using Pinder.LlmAdapters.Anthropic.Dto;
using Xunit;

namespace Pinder.LlmAdapters.Tests.Anthropic
{
    /// <summary>
    /// Mock HttpMessageHandler that returns a sequence of responses.
    /// </summary>
    internal sealed class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, int, HttpResponseMessage> _responseFactory;
        private int _callCount;

        public int CallCount => _callCount;

        public MockHttpMessageHandler(Func<HttpRequestMessage, int, HttpResponseMessage> responseFactory)
        {
            _responseFactory = responseFactory;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var count = Interlocked.Increment(ref _callCount);
            return Task.FromResult(_responseFactory(request, count));
        }
    }

    public class AnthropicClientTests
    {
        private const string TestApiKey = "sk-ant-test-key";

        private static HttpResponseMessage MakeSuccessResponse()
        {
            var responseJson = JsonConvert.SerializeObject(new MessagesResponse
            {
                Content = new[] { new ResponseContent { Type = "text", Text = "Hello world" } },
                Usage = new UsageStats { InputTokens = 10, OutputTokens = 5 }
            });
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, System.Text.Encoding.UTF8, "application/json")
            };
        }

        private static HttpResponseMessage MakeErrorResponse(int statusCode, string body = "{\"error\":\"test\"}")
        {
            return new HttpResponseMessage((HttpStatusCode)statusCode)
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
            };
        }

        private static MessagesRequest MakeTestRequest()
        {
            return new MessagesRequest
            {
                Model = "claude-sonnet-4-20250514",
                MaxTokens = 100,
                Messages = new[] { new Message { Role = "user", Content = "test" } }
            };
        }

        // --- Constructor tests ---

        [Fact]
        public void Constructor_NullApiKey_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => new AnthropicClient(null!));
        }

        [Fact]
        public void Constructor_EmptyApiKey_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => new AnthropicClient(""));
        }

        [Fact]
        public void Constructor_WhitespaceApiKey_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => new AnthropicClient("   "));
        }

        [Fact]
        public void Constructor_WithHttpClient_NullClient_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => new AnthropicClient(TestApiKey, null!));
        }

        [Fact]
        public void Constructor_SetsHeaders()
        {
            var handler = new MockHttpMessageHandler((_, __) => MakeSuccessResponse());
            var httpClient = new HttpClient(handler);
            using (var client = new AnthropicClient(TestApiKey, httpClient))
            {
                Assert.Contains(httpClient.DefaultRequestHeaders.GetValues("x-api-key"),
                    v => v == TestApiKey);
                Assert.Contains(httpClient.DefaultRequestHeaders.GetValues("anthropic-version"),
                    v => v == "2023-06-01");
            }
        }

        [Fact]
        public void Constructor_NoBetaHeader()
        {
            var handler = new MockHttpMessageHandler((_, __) => MakeSuccessResponse());
            var httpClient = new HttpClient(handler);
            using (var client = new AnthropicClient(TestApiKey, httpClient))
            {
                Assert.False(httpClient.DefaultRequestHeaders.Contains("anthropic-beta"),
                    "anthropic-beta header must NOT be set — prompt caching is GA");
            }
        }

        // --- SendMessagesAsync tests ---

        [Fact]
        public async Task SendMessagesAsync_NullRequest_ThrowsArgumentNullException()
        {
            var handler = new MockHttpMessageHandler((_, __) => MakeSuccessResponse());
            var httpClient = new HttpClient(handler);
            using (var client = new AnthropicClient(TestApiKey, httpClient))
            {
                await Assert.ThrowsAsync<ArgumentNullException>(
                    () => client.SendMessagesAsync(null!));
            }
        }

        [Fact]
        public async Task SendMessagesAsync_Success_ReturnsDeserializedResponse()
        {
            var handler = new MockHttpMessageHandler((_, __) => MakeSuccessResponse());
            var httpClient = new HttpClient(handler);
            using (var client = new AnthropicClient(TestApiKey, httpClient))
            {
                var response = await client.SendMessagesAsync(MakeTestRequest());

                Assert.NotNull(response);
                Assert.Equal("Hello world", response.GetText());
                Assert.Equal(10, response.Usage!.InputTokens);
                Assert.Equal(5, response.Usage.OutputTokens);
                Assert.Equal(1, handler.CallCount);
            }
        }

        [Fact]
        public async Task SendMessagesAsync_400_ThrowsImmediately()
        {
            var handler = new MockHttpMessageHandler((_, __) =>
                MakeErrorResponse(400, "{\"error\":\"bad request\"}"));
            var httpClient = new HttpClient(handler);
            using (var client = new AnthropicClient(TestApiKey, httpClient))
            {
                var ex = await Assert.ThrowsAsync<AnthropicApiException>(
                    () => client.SendMessagesAsync(MakeTestRequest()));

                Assert.Equal(400, ex.StatusCode);
                Assert.Contains("bad request", ex.ResponseBody);
                Assert.Equal(1, handler.CallCount); // no retry
            }
        }

        [Fact]
        public async Task SendMessagesAsync_401_ThrowsImmediately()
        {
            var handler = new MockHttpMessageHandler((_, __) =>
                MakeErrorResponse(401, "{\"error\":\"unauthorized\"}"));
            var httpClient = new HttpClient(handler);
            using (var client = new AnthropicClient(TestApiKey, httpClient))
            {
                var ex = await Assert.ThrowsAsync<AnthropicApiException>(
                    () => client.SendMessagesAsync(MakeTestRequest()));

                Assert.Equal(401, ex.StatusCode);
                Assert.Equal(1, handler.CallCount);
            }
        }

        [Fact]
        public async Task SendMessagesAsync_429_RetriesWithRetryAfter_ThenSucceeds()
        {
            var handler = new MockHttpMessageHandler((_, callNum) =>
            {
                if (callNum == 1)
                {
                    var resp = MakeErrorResponse(429);
                    // Use raw header — Retry-After: 0 for fast test
                    resp.Headers.TryAddWithoutValidation("Retry-After", "0");
                    return resp;
                }
                return MakeSuccessResponse();
            });
            var httpClient = new HttpClient(handler);
            using (var client = new AnthropicClient(TestApiKey, httpClient))
            {
                var response = await client.SendMessagesAsync(MakeTestRequest());

                Assert.NotNull(response);
                Assert.Equal("Hello world", response.GetText());
                Assert.Equal(2, handler.CallCount); // 1 failure + 1 success
            }
        }

        [Fact]
        public async Task SendMessagesAsync_429_ExhaustsRetries_Throws()
        {
            var handler = new MockHttpMessageHandler((_, __) =>
            {
                var resp = MakeErrorResponse(429, "{\"error\":\"rate limited\"}");
                resp.Headers.TryAddWithoutValidation("Retry-After", "0");
                return resp;
            });
            var httpClient = new HttpClient(handler);
            using (var client = new AnthropicClient(TestApiKey, httpClient))
            {
                var ex = await Assert.ThrowsAsync<AnthropicApiException>(
                    () => client.SendMessagesAsync(MakeTestRequest()));

                Assert.Equal(429, ex.StatusCode);
                Assert.Equal(4, handler.CallCount); // 1 original + 3 retries
            }
        }

        [Fact]
        public async Task SendMessagesAsync_5xx_RetriesOnce_ThenSucceeds()
        {
            var handler = new MockHttpMessageHandler((_, callNum) =>
            {
                if (callNum == 1) return MakeErrorResponse(500, "{\"error\":\"internal\"}");
                return MakeSuccessResponse();
            });
            var httpClient = new HttpClient(handler);
            using (var client = new AnthropicClient(TestApiKey, httpClient))
            {
                var response = await client.SendMessagesAsync(MakeTestRequest());

                Assert.NotNull(response);
                Assert.Equal("Hello world", response.GetText());
                Assert.Equal(2, handler.CallCount); // 1 failure + 1 success
            }
        }

        [Fact]
        public async Task SendMessagesAsync_5xx_ExhaustsRetries_Throws()
        {
            var handler = new MockHttpMessageHandler((_, __) =>
                MakeErrorResponse(500, "{\"error\":\"internal\"}"));
            var httpClient = new HttpClient(handler);
            using (var client = new AnthropicClient(TestApiKey, httpClient))
            {
                var ex = await Assert.ThrowsAsync<AnthropicApiException>(
                    () => client.SendMessagesAsync(MakeTestRequest()));

                Assert.Equal(500, ex.StatusCode);
                Assert.Equal(2, handler.CallCount); // 1 original + 1 retry
            }
        }

        [Fact]
        public async Task SendMessagesAsync_529_RetriesWithExponentialBackoff_ThenSucceeds()
        {
            var handler = new MockHttpMessageHandler((_, callNum) =>
            {
                if (callNum <= 2) return MakeErrorResponse(529, "{\"error\":\"overloaded\"}");
                return MakeSuccessResponse();
            });
            var httpClient = new HttpClient(handler);
            using (var client = new AnthropicClient(TestApiKey, httpClient))
            {
                var response = await client.SendMessagesAsync(MakeTestRequest());

                Assert.NotNull(response);
                Assert.Equal(3, handler.CallCount); // 2 failures + 1 success
            }
        }

        [Fact]
        public async Task SendMessagesAsync_529_ExhaustsRetries_Throws()
        {
            var handler = new MockHttpMessageHandler((_, __) =>
                MakeErrorResponse(529, "{\"error\":\"overloaded\"}"));
            var httpClient = new HttpClient(handler);
            using (var client = new AnthropicClient(TestApiKey, httpClient))
            {
                var ex = await Assert.ThrowsAsync<AnthropicApiException>(
                    () => client.SendMessagesAsync(MakeTestRequest()));

                Assert.Equal(529, ex.StatusCode);
                Assert.Equal(4, handler.CallCount); // 1 original + 3 retries
            }
        }

        [Fact]
        public async Task SendMessagesAsync_200_MalformedJson_ThrowsAnthropicApiException()
        {
            var handler = new MockHttpMessageHandler((_, __) =>
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("not valid json", System.Text.Encoding.UTF8, "application/json")
                });
            var httpClient = new HttpClient(handler);
            using (var client = new AnthropicClient(TestApiKey, httpClient))
            {
                var ex = await Assert.ThrowsAsync<AnthropicApiException>(
                    () => client.SendMessagesAsync(MakeTestRequest()));

                Assert.Equal(200, ex.StatusCode);
                Assert.Contains("malformed JSON", ex.Message);
                Assert.Equal("not valid json", ex.ResponseBody);
                Assert.Equal(1, handler.CallCount); // no retry on deserialization failure
            }
        }

        [Fact]
        public async Task SendMessagesAsync_200_EmptyBody_ThrowsAnthropicApiException()
        {
            var handler = new MockHttpMessageHandler((_, __) =>
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("", System.Text.Encoding.UTF8, "application/json")
                });
            var httpClient = new HttpClient(handler);
            using (var client = new AnthropicClient(TestApiKey, httpClient))
            {
                var ex = await Assert.ThrowsAsync<AnthropicApiException>(
                    () => client.SendMessagesAsync(MakeTestRequest()));

                Assert.Equal(200, ex.StatusCode);
                Assert.Contains("deserialized to null", ex.Message);
                Assert.Equal(1, handler.CallCount);
            }
        }

        // --- Dispose tests ---

        [Fact]
        public void Dispose_ExternalHttpClient_DoesNotDispose()
        {
            var handler = new MockHttpMessageHandler((_, __) => MakeSuccessResponse());
            var httpClient = new HttpClient(handler);
            var client = new AnthropicClient(TestApiKey, httpClient);
            client.Dispose();

            // External HttpClient should still be usable — no ObjectDisposedException
            Assert.NotNull(httpClient.DefaultRequestHeaders);
        }

        [Fact]
        public void Dispose_CalledMultipleTimes_NoException()
        {
            var handler = new MockHttpMessageHandler((_, __) => MakeSuccessResponse());
            var httpClient = new HttpClient(handler);
            var client = new AnthropicClient(TestApiKey, httpClient);
            client.Dispose();
            client.Dispose(); // should not throw
        }

        // --- Cancellation test ---

        [Fact]
        public async Task SendMessagesAsync_CancellationRequested_ThrowsOperationCancelled()
        {
            var handler = new MockHttpMessageHandler((_, __) => MakeSuccessResponse());
            var httpClient = new HttpClient(handler);
            using (var client = new AnthropicClient(TestApiKey, httpClient))
            {
                var cts = new CancellationTokenSource();
                cts.Cancel();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(
                    () => client.SendMessagesAsync(MakeTestRequest(), cts.Token));
            }
        }
    }
}
