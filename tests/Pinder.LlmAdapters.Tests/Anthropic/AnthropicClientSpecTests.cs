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
    public class AnthropicClientSpecTests
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
            var httpClient = new HttpClient(handler);
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
            var httpClient = new HttpClient(handler);
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
            var httpClient = new HttpClient(handler);
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
            var httpClient = new HttpClient(handler);
            using (var client = new AnthropicClient(TestApiKey, httpClient))
            {
                await client.SendMessagesAsync(MakeRequest());
            }
        }

        // ===== AC3: Retry logic for 429 =====

        // Mutation: would catch if 429 is treated as non-retryable
        [Fact]
        public async Task AC3_429_RetryAfterHeader_RetriesAndSucceeds()
        {
            var handler = new SequenceHandler(
                _ => Make429("0"),
                _ => MakeSuccess("recovered")
            );
            var httpClient = new HttpClient(handler);
            using (var client = new AnthropicClient(TestApiKey, httpClient))
            {
                var response = await client.SendMessagesAsync(MakeRequest());
                Assert.Equal("recovered", response.GetText());
                Assert.Equal(2, handler.CallCount);
            }
        }

        // Mutation: would catch if retry count is < 3 (e.g., only 2 retries allowed)
        [Fact]
        public async Task AC3_429_ThreeRetries_ExhaustsAt4TotalAttempts()
        {
            int callCount = 0;
            var handler = new SequenceHandler(
                _ => { callCount++; return Make429("0"); },
                _ => { callCount++; return Make429("0"); },
                _ => { callCount++; return Make429("0"); },
                _ => { callCount++; return Make429("0"); }
            );
            var httpClient = new HttpClient(handler);
            using (var client = new AnthropicClient(TestApiKey, httpClient))
            {
                var ex = await Assert.ThrowsAsync<AnthropicApiException>(
                    () => client.SendMessagesAsync(MakeRequest()));
                Assert.Equal(429, ex.StatusCode);
                // 1 original + 3 retries = 4 total
                Assert.Equal(4, handler.CallCount);
            }
        }

        // Mutation: would catch if 429 response body is not captured in exception
        [Fact]
        public async Task AC3_429_ExhaustedRetries_IncludesLastResponseBody()
        {
            var handler = new SequenceHandler(
                _ => Make429("0"),
                _ => Make429("0"),
                _ => Make429("0"),
                _ => { var r = MakeError(429, "{\"error\":\"final_429\"}"); r.Headers.TryAddWithoutValidation("Retry-After", "0"); return r; }
            );
            var httpClient = new HttpClient(handler);
            using (var client = new AnthropicClient(TestApiKey, httpClient))
            {
                var ex = await Assert.ThrowsAsync<AnthropicApiException>(
                    () => client.SendMessagesAsync(MakeRequest()));
                Assert.Equal(429, ex.StatusCode);
                Assert.Contains("final_429", ex.ResponseBody);
            }
        }

        // Mutation: would catch if 3rd retry succeeds but is not returned
        [Fact]
        public async Task AC3_429_SucceedsOnThirdRetry()
        {
            var handler = new SequenceHandler(
                _ => Make429("0"),
                _ => Make429("0"),
                _ => Make429("0"),
                _ => MakeSuccess("third retry success")
            );
            var httpClient = new HttpClient(handler);
            using (var client = new AnthropicClient(TestApiKey, httpClient))
            {
                var response = await client.SendMessagesAsync(MakeRequest());
                Assert.Equal("third retry success", response.GetText());
                Assert.Equal(4, handler.CallCount);
            }
        }

        // ===== AC4: Retry logic for 529 =====

        // Mutation: would catch if 529 is treated like regular 5xx (only 1 retry)
        [Fact]
        public async Task AC4_529_Retries3Times()
        {
            var handler = new SequenceHandler(
                _ => MakeError(529, "{\"error\":\"overloaded\"}"),
                _ => MakeError(529, "{\"error\":\"overloaded\"}"),
                _ => MakeError(529, "{\"error\":\"overloaded\"}"),
                _ => MakeError(529, "{\"error\":\"overloaded\"}")
            );
            var httpClient = new HttpClient(handler);
            using (var client = new AnthropicClient(TestApiKey, httpClient))
            {
                var ex = await Assert.ThrowsAsync<AnthropicApiException>(
                    () => client.SendMessagesAsync(MakeRequest()));
                Assert.Equal(529, ex.StatusCode);
                Assert.Equal(4, handler.CallCount); // 1 + 3 retries
            }
        }

        // Mutation: would catch if 529 recovery doesn't return the successful response
        [Fact]
        public async Task AC4_529_SucceedsOnSecondRetry()
        {
            var handler = new SequenceHandler(
                _ => MakeError(529),
                _ => MakeError(529),
                _ => MakeSuccess("529 recovered")
            );
            var httpClient = new HttpClient(handler);
            using (var client = new AnthropicClient(TestApiKey, httpClient))
            {
                var response = await client.SendMessagesAsync(MakeRequest());
                Assert.Equal("529 recovered", response.GetText());
                Assert.Equal(3, handler.CallCount);
            }
        }

        // ===== AC5: Retry logic for 5xx (not 529) =====

        // Mutation: would catch if 500 is given more than 1 retry
        [Fact]
        public async Task AC5_500_RetriesExactlyOnce_ThenThrows()
        {
            var handler = new SequenceHandler(
                _ => MakeError(500, "{\"error\":\"first\"}"),
                _ => MakeError(500, "{\"error\":\"second\"}")
            );
            var httpClient = new HttpClient(handler);
            using (var client = new AnthropicClient(TestApiKey, httpClient))
            {
                var ex = await Assert.ThrowsAsync<AnthropicApiException>(
                    () => client.SendMessagesAsync(MakeRequest()));
                Assert.Equal(500, ex.StatusCode);
                Assert.Equal(2, handler.CallCount); // 1 original + 1 retry
            }
        }

        // Mutation: would catch if 502 is treated differently than 500
        [Fact]
        public async Task AC5_502_RetriesOnce_Succeeds()
        {
            var handler = new SequenceHandler(
                _ => MakeError(502),
                _ => MakeSuccess("502 recovered")
            );
            var httpClient = new HttpClient(handler);
            using (var client = new AnthropicClient(TestApiKey, httpClient))
            {
                var response = await client.SendMessagesAsync(MakeRequest());
                Assert.Equal("502 recovered", response.GetText());
                Assert.Equal(2, handler.CallCount);
            }
        }

        // Mutation: would catch if 503 is not retried
        [Fact]
        public async Task AC5_503_RetriesOnce_ThenThrows()
        {
            var handler = new SequenceHandler(
                _ => MakeError(503),
                _ => MakeError(503)
            );
            var httpClient = new HttpClient(handler);
            using (var client = new AnthropicClient(TestApiKey, httpClient))
            {
                var ex = await Assert.ThrowsAsync<AnthropicApiException>(
                    () => client.SendMessagesAsync(MakeRequest()));
                Assert.Equal(503, ex.StatusCode);
                Assert.Equal(2, handler.CallCount);
            }
        }

        // ===== AC6: Non-retryable 4xx throws immediately =====

        // Mutation: would catch if 400 is retried
        [Fact]
        public async Task AC6_400_NoRetry()
        {
            var handler = new SequenceHandler(
                _ => MakeError(400, "{\"error\":\"invalid_request\"}")
            );
            var httpClient = new HttpClient(handler);
            using (var client = new AnthropicClient(TestApiKey, httpClient))
            {
                var ex = await Assert.ThrowsAsync<AnthropicApiException>(
                    () => client.SendMessagesAsync(MakeRequest()));
                Assert.Equal(400, ex.StatusCode);
                Assert.Contains("invalid_request", ex.ResponseBody);
                Assert.Equal(1, handler.CallCount);
            }
        }

        // Mutation: would catch if 403 is retried
        [Fact]
        public async Task AC6_403_NoRetry()
        {
            var handler = new SequenceHandler(_ => MakeError(403, "{\"error\":\"forbidden\"}"));
            var httpClient = new HttpClient(handler);
            using (var client = new AnthropicClient(TestApiKey, httpClient))
            {
                var ex = await Assert.ThrowsAsync<AnthropicApiException>(
                    () => client.SendMessagesAsync(MakeRequest()));
                Assert.Equal(403, ex.StatusCode);
                Assert.Equal(1, handler.CallCount);
            }
        }

        // Mutation: would catch if 404 is retried
        [Fact]
        public async Task AC6_404_NoRetry()
        {
            var handler = new SequenceHandler(_ => MakeError(404));
            var httpClient = new HttpClient(handler);
            using (var client = new AnthropicClient(TestApiKey, httpClient))
            {
                var ex = await Assert.ThrowsAsync<AnthropicApiException>(
                    () => client.SendMessagesAsync(MakeRequest()));
                Assert.Equal(404, ex.StatusCode);
                Assert.Equal(1, handler.CallCount);
            }
        }

        // Mutation: would catch if 422 is retried
        [Fact]
        public async Task AC6_422_NoRetry()
        {
            var handler = new SequenceHandler(_ => MakeError(422));
            var httpClient = new HttpClient(handler);
            using (var client = new AnthropicClient(TestApiKey, httpClient))
            {
                var ex = await Assert.ThrowsAsync<AnthropicApiException>(
                    () => client.SendMessagesAsync(MakeRequest()));
                Assert.Equal(422, ex.StatusCode);
                Assert.Equal(1, handler.CallCount);
            }
        }

        // ===== Edge Cases =====

        // Mutation: would catch if null request doesn't throw or throws wrong exception
        [Fact]
        public async Task Edge_NullRequest_ThrowsArgumentNullException()
        {
            var handler = new SequenceHandler(_ => MakeSuccess());
            var httpClient = new HttpClient(handler);
            using (var client = new AnthropicClient(TestApiKey, httpClient))
            {
                await Assert.ThrowsAsync<ArgumentNullException>(
                    () => client.SendMessagesAsync(null!));
                // No HTTP call should be made
                Assert.Equal(0, handler.CallCount);
            }
        }

        // Mutation: would catch if non-integer Retry-After causes exception instead of defaulting
        [Fact]
        public async Task Edge_RetryAfter_NonIntegerValue_UsesDefault()
        {
            // Non-integer Retry-After should be treated as missing → 5s default
            // We can't easily test the delay duration, but we can verify it retries
            var handler = new SequenceHandler(
                _ =>
                {
                    var r = MakeError(429);
                    r.Headers.TryAddWithoutValidation("Retry-After", "Sat, 29 Mar 2025 12:00:00 GMT");
                    return r;
                },
                _ => MakeSuccess("recovered from date retry-after")
            );
            var httpClient = new HttpClient(handler);
            using (var client = new AnthropicClient(TestApiKey, httpClient))
            {
                // Use a long timeout — if default is 5s, we need to wait
                var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                var response = await client.SendMessagesAsync(MakeRequest(), cts.Token);
                Assert.Equal("recovered from date retry-after", response.GetText());
                Assert.Equal(2, handler.CallCount);
            }
        }

        // Mutation: would catch if Retry-After=0 waits 5s instead of retrying immediately
        [Fact]
        public async Task Edge_RetryAfter_Zero_RetriesImmediately()
        {
            var handler = new SequenceHandler(
                _ =>
                {
                    var r = MakeError(429);
                    r.Headers.TryAddWithoutValidation("Retry-After", "0");
                    return r;
                },
                _ => MakeSuccess("immediate retry")
            );
            var httpClient = new HttpClient(handler);
            using (var client = new AnthropicClient(TestApiKey, httpClient))
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var response = await client.SendMessagesAsync(MakeRequest());
                sw.Stop();
                Assert.Equal("immediate retry", response.GetText());
                // Should be nearly instant (< 2 seconds, definitely not 5s)
                Assert.True(sw.ElapsedMilliseconds < 2000,
                    $"Retry-After=0 should retry immediately, took {sw.ElapsedMilliseconds}ms");
            }
        }

        // Mutation: would catch if missing Retry-After header crashes instead of using default
        [Fact]
        public async Task Edge_RetryAfter_Missing_UsesDefault()
        {
            var handler = new SequenceHandler(
                _ => MakeError(429), // No Retry-After header
                _ => MakeSuccess("recovered no header")
            );
            var httpClient = new HttpClient(handler);
            using (var client = new AnthropicClient(TestApiKey, httpClient))
            {
                var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                var response = await client.SendMessagesAsync(MakeRequest(), cts.Token);
                Assert.Equal("recovered no header", response.GetText());
                Assert.Equal(2, handler.CallCount);
            }
        }

        // Mutation: would catch if error response with empty body crashes exception construction
        [Fact]
        public async Task Edge_ErrorResponse_EmptyBody()
        {
            var handler = new SequenceHandler(
                _ => new HttpResponseMessage((HttpStatusCode)400)
                {
                    Content = new StringContent("", System.Text.Encoding.UTF8, "application/json")
                }
            );
            var httpClient = new HttpClient(handler);
            using (var client = new AnthropicClient(TestApiKey, httpClient))
            {
                var ex = await Assert.ThrowsAsync<AnthropicApiException>(
                    () => client.SendMessagesAsync(MakeRequest()));
                Assert.Equal(400, ex.StatusCode);
                // ResponseBody should be empty or null, but not throw
                Assert.True(ex.ResponseBody == null || ex.ResponseBody == "" || ex.ResponseBody.Length == 0,
                    "ResponseBody should be null or empty for empty response");
            }
        }

        // Mutation: would catch if Dispose() can't be called multiple times safely
        [Fact]
        public void Edge_Dispose_MultipleCalls_NoException()
        {
            var handler = new SequenceHandler(_ => MakeSuccess());
            var httpClient = new HttpClient(handler);
            var client = new AnthropicClient(TestApiKey, httpClient);
            client.Dispose();
            client.Dispose();
            client.Dispose(); // Three times, no exception
        }

        // Mutation: would catch if Dispose with external HttpClient disposes it
        [Fact]
        public async Task Edge_Dispose_ExternalHttpClient_StillUsable()
        {
            var handler = new SequenceHandler(_ => MakeSuccess(), _ => MakeSuccess());
            var httpClient = new HttpClient(handler);

            var client1 = new AnthropicClient(TestApiKey, httpClient);
            client1.Dispose();

            // External HttpClient should still be usable after client disposal
            // Creating another client with same HttpClient should work
            using (var client2 = new AnthropicClient(TestApiKey, httpClient))
            {
                var response = await client2.SendMessagesAsync(MakeRequest());
                Assert.NotNull(response);
            }
        }

        // Mutation: would catch if cancellation is not checked before HTTP call
        [Fact]
        public async Task Edge_Cancellation_BeforeFirstCall()
        {
            var handler = new SequenceHandler(_ => MakeSuccess());
            var httpClient = new HttpClient(handler);
            using (var client = new AnthropicClient(TestApiKey, httpClient))
            {
                var cts = new CancellationTokenSource();
                cts.Cancel();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(
                    () => client.SendMessagesAsync(MakeRequest(), cts.Token));
                Assert.Equal(0, handler.CallCount);
            }
        }

        // Mutation: would catch if request is sent to wrong endpoint
        [Fact]
        public async Task Edge_RequestSentToCorrectEndpoint()
        {
            var handler = new SequenceHandler(req =>
            {
                Assert.Equal("https://api.anthropic.com/v1/messages", req.RequestUri!.ToString());
                return MakeSuccess();
            });
            var httpClient = new HttpClient(handler);
            using (var client = new AnthropicClient(TestApiKey, httpClient))
            {
                await client.SendMessagesAsync(MakeRequest());
            }
        }

        // Mutation: would catch if request body is not JSON serialized properly
        [Fact]
        public async Task Edge_RequestBody_IsValidJson()
        {
            var handler = new SequenceHandler(req =>
            {
                // Content-Type should be application/json
                Assert.NotNull(req.Content);
                var contentType = req.Content!.Headers.ContentType?.MediaType;
                Assert.Equal("application/json", contentType);
                return MakeSuccess();
            });
            var httpClient = new HttpClient(handler);
            using (var client = new AnthropicClient(TestApiKey, httpClient))
            {
                await client.SendMessagesAsync(MakeRequest());
            }
        }

        // Mutation: would catch if successful 200 response doesn't deserialize Usage
        [Fact]
        public async Task Edge_Success_DeserializesUsageStats()
        {
            var responseJson = JsonConvert.SerializeObject(new MessagesResponse
            {
                Content = new[] { new ResponseContent { Type = "text", Text = "test" } },
                Usage = new UsageStats
                {
                    InputTokens = 100,
                    OutputTokens = 50,
                    CacheCreationInputTokens = 75,
                    CacheReadInputTokens = 25
                }
            });
            var handler = new SequenceHandler(_ =>
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(responseJson, System.Text.Encoding.UTF8, "application/json")
                });
            var httpClient = new HttpClient(handler);
            using (var client = new AnthropicClient(TestApiKey, httpClient))
            {
                var response = await client.SendMessagesAsync(MakeRequest());
                Assert.NotNull(response.Usage);
                Assert.Equal(100, response.Usage!.InputTokens);
                Assert.Equal(50, response.Usage.OutputTokens);
                Assert.Equal(75, response.Usage.CacheCreationInputTokens);
                Assert.Equal(25, response.Usage.CacheReadInputTokens);
            }
        }

        // Mutation: would catch if cache_control in request body is stripped during serialization
        [Fact]
        public async Task Edge_CacheControl_SerializedInRequestBody()
        {
            string? capturedBody = null;
            var handler = new SequenceHandler(req =>
            {
                capturedBody = req.Content!.ReadAsStringAsync().Result;
                return MakeSuccess();
            });
            var httpClient = new HttpClient(handler);
            using (var client = new AnthropicClient(TestApiKey, httpClient))
            {
                var request = new MessagesRequest
                {
                    Model = "claude-sonnet-4-20250514",
                    MaxTokens = 1024,
                    System = new[]
                    {
                        new ContentBlock
                        {
                            Type = "text",
                            Text = "System prompt with caching",
                            CacheControl = new CacheControl { Type = "ephemeral" }
                        }
                    },
                    Messages = new[] { new Message { Role = "user", Content = "test" } }
                };
                await client.SendMessagesAsync(request);
            }

            Assert.NotNull(capturedBody);
            // The request body should contain cache_control somewhere
            // (exact key depends on serializer config - snake_case or camelCase)
            Assert.True(
                capturedBody!.Contains("cache_control") || capturedBody.Contains("cacheControl") || capturedBody.Contains("CacheControl"),
                $"Request body should contain cache_control field. Body: {capturedBody}");
            Assert.Contains("ephemeral", capturedBody);
        }

        // Mutation: would catch if AnthropicApiException doesn't preserve status code
        [Fact]
        public void Edge_AnthropicApiException_PreservesFields()
        {
            var ex = new AnthropicApiException(418, "{\"error\":\"teapot\"}");
            Assert.Equal(418, ex.StatusCode);
            Assert.Equal("{\"error\":\"teapot\"}", ex.ResponseBody);
        }

        // Mutation: would catch if AnthropicApiException with null body crashes
        [Fact]
        public void Edge_AnthropicApiException_NullBody_DoesNotThrow()
        {
            var ex = new AnthropicApiException(500, null);
            Assert.Equal(500, ex.StatusCode);
            Assert.Null(ex.ResponseBody);
        }

        // Mutation: would catch if AnthropicApiException message constructor is broken
        [Fact]
        public void Edge_AnthropicApiException_WithMessage()
        {
            var ex = new AnthropicApiException(400, "body", "custom message");
            Assert.Equal(400, ex.StatusCode);
            Assert.Equal("body", ex.ResponseBody);
            Assert.Equal("custom message", ex.Message);
        }

        // Mutation: would catch if 200 with null deserialization result doesn't throw
        [Fact]
        public async Task Edge_200_NullDeserialization_Throws()
        {
            var handler = new SequenceHandler(_ =>
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("null", System.Text.Encoding.UTF8, "application/json")
                });
            var httpClient = new HttpClient(handler);
            using (var client = new AnthropicClient(TestApiKey, httpClient))
            {
                // Should throw AnthropicApiException on null deserialization
                var ex = await Assert.ThrowsAsync<AnthropicApiException>(
                    () => client.SendMessagesAsync(MakeRequest()));
                Assert.Equal(200, ex.StatusCode);
            }
        }

        // Mutation: would catch if POST is used instead of another HTTP method (or vice versa)
        [Fact]
        public async Task Edge_UsesPostMethod()
        {
            var handler = new SequenceHandler(req =>
            {
                Assert.Equal(HttpMethod.Post, req.Method);
                return MakeSuccess();
            });
            var httpClient = new HttpClient(handler);
            using (var client = new AnthropicClient(TestApiKey, httpClient))
            {
                await client.SendMessagesAsync(MakeRequest());
            }
        }

        // Mutation: would catch if IDisposable is not implemented
        [Fact]
        public void Edge_ImplementsIDisposable()
        {
            var handler = new SequenceHandler(_ => MakeSuccess());
            var httpClient = new HttpClient(handler);
            IDisposable disposable = new AnthropicClient(TestApiKey, httpClient);
            disposable.Dispose(); // Should not throw
        }
    }
}
