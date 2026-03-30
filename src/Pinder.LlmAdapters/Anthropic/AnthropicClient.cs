using System;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Pinder.LlmAdapters.Anthropic.Dto;

namespace Pinder.LlmAdapters.Anthropic
{
    /// <summary>
    /// Raw HTTP client for Anthropic Messages API.
    /// Owns: serialization, HTTP transport, retry logic.
    /// Does NOT own: prompt construction, response parsing beyond DTO deserialization.
    /// </summary>
    public sealed class AnthropicClient : IDisposable
    {
        private const string ApiUrl = "https://api.anthropic.com/v1/messages";
        private const string AnthropicVersion = "2023-06-01";
        // Prompt caching is GA — no beta header required. cache_control in body is sufficient.

        private const int MaxRetries429 = 3;
        private const int MaxRetries529 = 3;
        private const int MaxRetries5xx = 1;
        private const int DefaultRetryAfterSeconds = 5;

        private readonly HttpClient _httpClient;
        private readonly bool _ownsHttpClient;
        private bool _disposed;

        /// <summary>Creates client with internally-owned HttpClient.</summary>
        /// <param name="apiKey">Anthropic API key. Must not be null/empty.</param>
        /// <exception cref="ArgumentException">If apiKey is null, empty, or whitespace.</exception>
        public AnthropicClient(string apiKey)
        {
            ValidateApiKey(apiKey);
            _httpClient = new HttpClient();
            ConfigureHeaders(_httpClient, apiKey);
            _ownsHttpClient = true;
        }

        /// <summary>Creates client with externally-provided HttpClient (for testing).</summary>
        /// <param name="apiKey">Anthropic API key. Must not be null/empty.</param>
        /// <param name="httpClient">Caller-owned HttpClient. Client does NOT dispose it.</param>
        /// <exception cref="ArgumentException">If apiKey is null, empty, or whitespace.</exception>
        /// <exception cref="ArgumentNullException">If httpClient is null.</exception>
        public AnthropicClient(string apiKey, HttpClient httpClient)
        {
            ValidateApiKey(apiKey);
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            ConfigureHeaders(_httpClient, apiKey);
            _ownsHttpClient = false;
        }

        /// <summary>
        /// Sends a Messages API request with automatic retry for transient failures.
        /// </summary>
        /// <param name="request">The MessagesRequest to send. Must not be null.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>Deserialized MessagesResponse from the API.</returns>
        /// <exception cref="ArgumentNullException">If request is null.</exception>
        /// <exception cref="AnthropicApiException">
        /// On non-retryable 4xx errors (immediate), or after all retry attempts are exhausted.
        /// </exception>
        /// <exception cref="OperationCanceledException">If ct is cancelled.</exception>
        public async Task<MessagesResponse> SendMessagesAsync(
            MessagesRequest request,
            CancellationToken ct = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));

            var json = JsonConvert.SerializeObject(request);

            int retries429 = 0;
            int retries529 = 0;
            int retries5xx = 0;

            while (true)
            {
                ct.ThrowIfCancellationRequested();

                using (var content = new StringContent(json, Encoding.UTF8, "application/json"))
                {
                    var response = await _httpClient.PostAsync(ApiUrl, content, ct).ConfigureAwait(false);
                    var statusCode = (int)response.StatusCode;

                    if (response.IsSuccessStatusCode)
                    {
                        var responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        MessagesResponse? result;
                        try
                        {
                            result = JsonConvert.DeserializeObject<MessagesResponse>(responseBody);
                        }
                        catch (JsonException)
                        {
                            var truncated = responseBody?.Length > 200 ? responseBody.Substring(0, 200) : responseBody;
                            throw new AnthropicApiException(
                                statusCode,
                                responseBody,
                                $"Anthropic API returned {statusCode} but response body is malformed JSON: {truncated}") { };
                        }
                        if (result == null)
                        {
                            var truncated = responseBody?.Length > 200 ? responseBody.Substring(0, 200) : responseBody;
                            throw new AnthropicApiException(
                                statusCode,
                                responseBody,
                                $"Anthropic API returned {statusCode} but response body deserialized to null: {truncated}");
                        }
                        return result;
                    }

                    var errorBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                    // 429: rate-limited — retry with Retry-After
                    if (statusCode == 429)
                    {
                        if (retries429 >= MaxRetries429)
                        {
                            throw new AnthropicApiException(statusCode, errorBody);
                        }
                        retries429++;
                        var delay = GetRetryAfterDelay(response);
                        await Task.Delay(delay, ct).ConfigureAwait(false);
                        continue;
                    }

                    // 529: overloaded — exponential backoff
                    if (statusCode == 529)
                    {
                        if (retries529 >= MaxRetries529)
                        {
                            throw new AnthropicApiException(statusCode, errorBody);
                        }
                        retries529++;
                        var delaySeconds = 1 << (retries529 - 1); // 1s, 2s, 4s
                        await Task.Delay(TimeSpan.FromSeconds(delaySeconds), ct).ConfigureAwait(false);
                        continue;
                    }

                    // Other 5xx: retry once with 1s delay
                    if (statusCode >= 500 && statusCode < 600)
                    {
                        if (retries5xx >= MaxRetries5xx)
                        {
                            throw new AnthropicApiException(statusCode, errorBody);
                        }
                        retries5xx++;
                        await Task.Delay(TimeSpan.FromSeconds(1), ct).ConfigureAwait(false);
                        continue;
                    }

                    // 4xx (not 429): throw immediately
                    throw new AnthropicApiException(statusCode, errorBody);
                }
            }
        }

        /// <summary>
        /// Disposes the internally-owned HttpClient if this instance created it.
        /// Does nothing if the HttpClient was provided externally. Safe to call multiple times.
        /// </summary>
        public void Dispose()
        {
            if (!_disposed && _ownsHttpClient)
            {
                _httpClient.Dispose();
            }
            _disposed = true;
        }

        private static void ValidateApiKey(string apiKey)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new ArgumentException("API key must not be null, empty, or whitespace.", nameof(apiKey));
        }

        private static void ConfigureHeaders(HttpClient client, string apiKey)
        {
            client.DefaultRequestHeaders.Add("x-api-key", apiKey);
            client.DefaultRequestHeaders.Add("anthropic-version", AnthropicVersion);
        }

        private static TimeSpan GetRetryAfterDelay(HttpResponseMessage response)
        {
            var retryAfterHeader = response.Headers.RetryAfter;
            if (retryAfterHeader?.Delta != null)
            {
                return retryAfterHeader.Delta.Value.TotalSeconds <= 0
                    ? TimeSpan.Zero
                    : retryAfterHeader.Delta.Value;
            }

            // Try raw header parsing as fallback
            if (response.Headers.TryGetValues("Retry-After", out var values))
            {
                foreach (var value in values)
                {
                    if (int.TryParse(value, out var seconds))
                    {
                        return seconds <= 0 ? TimeSpan.Zero : TimeSpan.FromSeconds(seconds);
                    }
                }
            }

            return TimeSpan.FromSeconds(DefaultRetryAfterSeconds);
        }
    }
}
