using System;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace Pinder.LlmAdapters.OpenAi
{
    /// <summary>
    /// Raw HTTP client for OpenAI-compatible chat completions API.
    /// Supports any provider that implements POST /v1/chat/completions
    /// (OpenAI, Groq, Together, OpenRouter, Ollama, etc.).
    /// </summary>
    public sealed class OpenAiClient : IDisposable
    {
        private const int MaxRetries429 = 3;
        private const int MaxRetries5xx = 2;
        private const int DefaultRetryAfterSeconds = 5;

        private readonly string _baseUrl;
        private readonly HttpClient _httpClient;
        private readonly bool _ownsHttpClient;
        private bool _disposed;

        /// <summary>Creates client with internally-owned HttpClient.</summary>
        public OpenAiClient(string apiKey, string baseUrl)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new ArgumentException("API key must not be null, empty, or whitespace.", nameof(apiKey));
            _baseUrl = (baseUrl ?? "https://api.openai.com").TrimEnd('/');
            _httpClient = new HttpClient();
            ConfigureHeaders(_httpClient, apiKey);
            _ownsHttpClient = true;
        }

        /// <summary>Creates client with externally-provided HttpClient (for testing).</summary>
        public OpenAiClient(string apiKey, string baseUrl, HttpClient httpClient)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new ArgumentException("API key must not be null, empty, or whitespace.", nameof(apiKey));
            _baseUrl = (baseUrl ?? "https://api.openai.com").TrimEnd('/');
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            ConfigureHeaders(_httpClient, apiKey);
            _ownsHttpClient = false;
        }

        /// <summary>
        /// Sends a chat completions request and returns the assistant message content.
        /// </summary>
        /// <param name="requestJson">Serialized JSON request body.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>The text content of choices[0].message.content.</returns>
        public async Task<string> SendChatCompletionAsync(string requestJson, CancellationToken ct = default)
        {
            if (requestJson == null) throw new ArgumentNullException(nameof(requestJson));

            string url = _baseUrl + "/v1/chat/completions";
            int retries429 = 0;
            int retries5xx = 0;

            while (true)
            {
                ct.ThrowIfCancellationRequested();

                using (var content = new StringContent(requestJson, Encoding.UTF8, "application/json"))
                {
                    var response = await _httpClient.PostAsync(url, content, ct).ConfigureAwait(false);
                    int statusCode = (int)response.StatusCode;

                    if (response.IsSuccessStatusCode)
                    {
                        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        try
                        {
                            var json = JObject.Parse(body);
                            return ExtractAssistantText(json);
                        }
                        catch (InvalidOperationException)
                        {
                            // ExtractAssistantText raises a typed error for malformed shapes;
                            // re-throw without wrapping to preserve the message.
                            throw;
                        }
                        catch (Exception)
                        {
                            var truncated = body.Length > 200 ? body.Substring(0, 200) : body;
                            throw new InvalidOperationException(
                                $"OpenAI-compatible API returned {statusCode} but response is malformed: {truncated}");
                        }
                    }

                    var errorBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                    if (statusCode == 429)
                    {
                        if (retries429 >= MaxRetries429)
                            throw new HttpRequestException($"OpenAI-compatible API rate limited after {MaxRetries429} retries. Status: {statusCode}. Body: {errorBody}");
                        retries429++;
                        var delay = GetRetryAfterDelay(response);
                        await Task.Delay(delay, ct).ConfigureAwait(false);
                        continue;
                    }

                    if (statusCode >= 500 && statusCode < 600)
                    {
                        if (retries5xx >= MaxRetries5xx)
                            throw new HttpRequestException($"OpenAI-compatible API server error after {MaxRetries5xx} retries. Status: {statusCode}. Body: {errorBody}");
                        retries5xx++;
                        await Task.Delay(TimeSpan.FromSeconds(1), ct).ConfigureAwait(false);
                        continue;
                    }

                    throw new HttpRequestException($"OpenAI-compatible API error. Status: {statusCode}. Body: {errorBody}");
                }
            }
        }

        /// <summary>
        /// Extracts the user-visible assistant text from a parsed chat-completions
        /// response body. Issue #320: reasoning models on OpenAI/OpenRouter
        /// (Anthropic-via-OpenRouter with extended thinking, OpenAI o-series,
        /// gpt-5* with reasoning enabled, etc.) sometimes return
        /// <c>choices[0].message.content == ""</c> (or null) and surface the
        /// real answer alongside reasoning in <c>message.reasoning</c> and/or
        /// <c>message.reasoning_details[i].summary</c>.
        /// </summary>
        /// <remarks>
        /// Resolution order (mirrors <c>OpenAiStreamingTransport.ExtractContentFragmentsOrThrow</c>):
        /// <list type="number">
        ///   <item><description><c>message.content</c> when non-empty / non-whitespace.</description></item>
        ///   <item><description><c>message.reasoning</c> when non-empty / non-whitespace.</description></item>
        ///   <item><description>concatenated <c>message.reasoning_details[i].summary</c> when any are non-empty.</description></item>
        /// </list>
        /// Returns the empty string when none of the channels carry text.
        /// </remarks>
        internal static string ExtractAssistantText(JObject json)
        {
            if (json == null) throw new ArgumentNullException(nameof(json));

            // Note: JArray's int indexer throws on empty arrays (it does not support
            // null-conditional chain semantics for out-of-range indices). Guard
            // explicitly so a no-choices payload returns the empty string.
            var choices = json["choices"] as JArray;
            if (choices == null || choices.Count == 0) return "";
            var message = choices[0]?["message"];
            if (message == null || message.Type != JTokenType.Object) return "";

            // 1) message.content
            var content = message["content"]?.Value<string?>();
            if (!string.IsNullOrWhiteSpace(content))
                return content!;

            // 2) message.reasoning (OpenRouter / OpenAI surface internal reasoning here
            //    for models like Anthropic-via-OpenRouter with extended thinking).
            var reasoning = message["reasoning"]?.Value<string?>();
            if (!string.IsNullOrWhiteSpace(reasoning))
                return reasoning!;

            // 3) message.reasoning_details[i].summary — concatenated.
            if (message["reasoning_details"] is Newtonsoft.Json.Linq.JArray details && details.Count > 0)
            {
                System.Text.StringBuilder? sb = null;
                foreach (var d in details)
                {
                    if (d == null || d.Type != JTokenType.Object) continue;
                    var summary = d["summary"]?.Value<string?>();
                    if (string.IsNullOrEmpty(summary)) continue;
                    if (sb == null) sb = new System.Text.StringBuilder();
                    sb.Append(summary);
                }
                if (sb != null)
                {
                    var joined = sb.ToString();
                    if (!string.IsNullOrWhiteSpace(joined))
                        return joined;
                }
            }

            return "";
        }

        public void Dispose()
        {
            if (!_disposed && _ownsHttpClient)
            {
                _httpClient.Dispose();
            }
            _disposed = true;
        }

        private static void ConfigureHeaders(HttpClient client, string apiKey)
        {
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
        }

        private static TimeSpan GetRetryAfterDelay(HttpResponseMessage response)
        {
            if (response.Headers.TryGetValues("Retry-After", out var values))
            {
                foreach (var value in values)
                {
                    if (int.TryParse(value, out var seconds) && seconds > 0)
                        return TimeSpan.FromSeconds(seconds);
                }
            }
            return TimeSpan.FromSeconds(DefaultRetryAfterSeconds);
        }
    }
}
