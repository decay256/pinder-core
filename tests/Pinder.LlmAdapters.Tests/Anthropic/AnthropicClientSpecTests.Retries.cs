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

        // Mutation: would catch if 429 response body is not captured internally in exception
        [Fact]
        public async Task AC3_429_ExhaustedRetries_KeepsLastResponseBodyInternalOnly()
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
                Assert.Contains("final_429", ex.RawResponseBody);
                Assert.DoesNotContain("final_429", ex.ResponseBody);
                Assert.DoesNotContain("final_429", ex.Message);
                Assert.DoesNotContain("final_429", ex.ToString());
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
                Assert.Contains("invalid_request", ex.RawResponseBody);
                Assert.DoesNotContain("invalid_request", ex.ResponseBody);
                Assert.DoesNotContain("invalid_request", ex.Message);
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
    }
}
