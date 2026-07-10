using System;
using System.Collections.Generic;
using System.Linq;
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
    /// Spec-driven tests for AnthropicClient (issue #206).
    /// Tests verify behavior from docs/specs/issue-206-spec.md acceptance criteria and edge cases.
    /// Context-isolated: no implementation source files were read.
    /// </summary>
    public partial class AnthropicClientSpecTests
    {
        private const string TestApiKey = "sk-ant-spec-test-key";

        #region Test helpers (test-only utilities, not copied from implementation)

        /// <summary>
        /// Records each request's details for assertion, returns configured responses in sequence.
        /// </summary>
        private sealed class SequenceHandler : HttpMessageHandler
        {
            private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responses;
            private int _callCount;
            private readonly List<HttpRequestMessage> _requests = new List<HttpRequestMessage>();
            private readonly List<string> _requestBodies = new List<string>();

            public int CallCount => _callCount;
            public IReadOnlyList<HttpRequestMessage> Requests => _requests;
            public IReadOnlyList<string> RequestBodies => _requestBodies;

            public SequenceHandler(params Func<HttpRequestMessage, HttpResponseMessage>[] responses)
            {
                _responses = new Queue<Func<HttpRequestMessage, HttpResponseMessage>>(responses);
            }

            protected override async Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Interlocked.Increment(ref _callCount);

                // Capture request body before it's consumed
                string body = "";
                if (request.Content != null)
                {
                    body = await request.Content.ReadAsStringAsync();
                }
                lock (_requests)
                {
                    _requests.Add(request);
                    _requestBodies.Add(body);
                }

                if (_responses.Count == 0)
                    return MakeSuccess();

                return _responses.Dequeue()(request);
            }
        }

        /// <summary>
        /// Handler that introduces a delay before responding, to test cancellation during waits.
        /// </summary>
        private sealed class DelayThenRespondHandler : HttpMessageHandler
        {
            private readonly Func<int, HttpResponseMessage> _responseFactory;
            private int _callCount;

            public int CallCount => _callCount;

            public DelayThenRespondHandler(Func<int, HttpResponseMessage> responseFactory)
            {
                _responseFactory = responseFactory;
            }

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var count = Interlocked.Increment(ref _callCount);
                return Task.FromResult(_responseFactory(count));
            }
        }

        private static HttpResponseMessage MakeSuccess(string text = "Test response")
        {
            var json = JsonConvert.SerializeObject(new MessagesResponse
            {
                Content = new[] { new ResponseContent { Type = "text", Text = text } },
                Usage = new UsageStats { InputTokens = 50, OutputTokens = 20 }
            });
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
            };
        }

        private static HttpResponseMessage MakeError(int statusCode, string body = "{\"error\":\"test error\"}")
        {
            return new HttpResponseMessage((HttpStatusCode)statusCode)
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
            };
        }

        private static HttpResponseMessage Make429(string? retryAfter = "0")
        {
            var resp = MakeError(429, "{\"error\":\"rate_limited\"}");
            if (retryAfter != null)
                resp.Headers.TryAddWithoutValidation("Retry-After", retryAfter);
            return resp;
        }

        private static MessagesRequest MakeRequest()
        {
            return new MessagesRequest
            {
                Model = "claude-sonnet-4-20250514",
                MaxTokens = 100,
                Messages = new[] { new Message { Role = "user", Content = "Hello" } }
            };
        }

        #endregion

        // ===== AC1: Constructor Validation =====

        // Mutation: would catch if constructor accepts null apiKey without validation
        [Fact]
        public void AC1_Constructor1_NullApiKey_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => new AnthropicClient(null!));
        }

        // Mutation: would catch if constructor only checks for null but not empty string
        [Fact]
        public void AC1_Constructor1_EmptyApiKey_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => new AnthropicClient(""));
        }

        // Mutation: would catch if whitespace check is missing (only null/empty checked)
        [Fact]
        public void AC1_Constructor1_WhitespaceApiKey_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => new AnthropicClient("   \t\n"));
        }

        // Mutation: would catch if constructor2 doesn't validate apiKey
        [Fact]
        public void AC1_Constructor2_NullApiKey_ThrowsArgumentException()
        {
            var handler = new SequenceHandler(_ => MakeSuccess());
            using var http = new HttpClient(handler);
            Assert.Throws<ArgumentException>(() => new AnthropicClient(null!, http));
        }

        // Mutation: would catch if constructor2 doesn't validate httpClient parameter
        [Fact]
        public void AC1_Constructor2_NullHttpClient_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => new AnthropicClient(TestApiKey, null!));
        }

        // Mutation: would catch if x-api-key header value is wrong or missing
        [Fact]
        public void AC1_HeadersSetCorrectly_ApiKey()
        {
            var handler = new SequenceHandler(_ => MakeSuccess());
            using var httpClient = new HttpClient(handler);
            using (var client = new AnthropicClient(TestApiKey, httpClient))
            {
                var values = httpClient.DefaultRequestHeaders.GetValues("x-api-key").ToList();
                Assert.Single(values);
                Assert.Equal(TestApiKey, values[0]);
            }
        }

        // Mutation: would catch if anthropic-version header uses wrong version string
        [Fact]
        public void AC1_HeadersSetCorrectly_AnthropicVersion()
        {
            var handler = new SequenceHandler(_ => MakeSuccess());
            using var httpClient = new HttpClient(handler);
            using (var client = new AnthropicClient(TestApiKey, httpClient))
            {
                var values = httpClient.DefaultRequestHeaders.GetValues("anthropic-version").ToList();
                Assert.Single(values);
                Assert.Equal("2023-06-01", values[0]);
            }
        }

        // ===== AC2: No anthropic-beta header =====

        // Mutation: would catch if beta header is accidentally added
        [Fact]
        public void AC2_NoBetaHeader_OnConstruction()
        {
            var handler = new SequenceHandler(_ => MakeSuccess());
            using var httpClient = new HttpClient(handler);
            using (var client = new AnthropicClient(TestApiKey, httpClient))
            {
                Assert.False(httpClient.DefaultRequestHeaders.Contains("anthropic-beta"),
                    "anthropic-beta header must NOT be present — prompt caching is GA");
            }
        }

        // Mutation: would catch if beta header is added per-request instead of default headers
        [Fact]
        public async Task AC2_NoBetaHeader_InRequest()
        {
            var handler = new SequenceHandler(req =>
            {
                // Verify request-level headers don't include beta
                Assert.False(req.Headers.Contains("anthropic-beta"),
                    "Request must not include anthropic-beta header");
                return MakeSuccess();
            });
            using var httpClient = new HttpClient(handler);
            using (var client = new AnthropicClient(TestApiKey, httpClient))
            {
                await client.SendMessagesAsync(MakeRequest());
            }
        }
    }
}
