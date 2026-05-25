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
    public partial class AnthropicClientSpecTests
    {
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