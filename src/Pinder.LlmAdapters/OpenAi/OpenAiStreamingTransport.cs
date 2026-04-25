using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Pinder.Core.Interfaces;

namespace Pinder.LlmAdapters.OpenAi
{
    /// <summary>
    /// <see cref="IStreamingLlmTransport"/> implementation for any OpenAI-compatible
    /// chat completions endpoint (OpenAI, OpenRouter, Groq, Together, Ollama, ...).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Posts to <c>{baseUrl}/v1/chat/completions</c> with <c>"stream": true</c>.
    /// The response is an SSE stream where each event payload is
    /// <c>data: {ChatCompletionChunk JSON}</c> followed by a blank line, and the
    /// stream terminates with <c>data: [DONE]</c>.
    /// </para>
    /// <para>
    /// For each chunk, only <c>choices[0].delta.content</c> strings are yielded
    /// (when present and non-empty). The role-only initial delta, tool/function
    /// call deltas, and the <c>[DONE]</c> sentinel are ignored. <c>finish_reason</c>
    /// is not surfaced — the stream simply ends when the response body is exhausted.
    /// </para>
    /// <para>
    /// <b>Error mapping.</b> Any non-2xx HTTP response, malformed SSE frame, or
    /// mid-stream <c>{"error": {...}}</c> frame is translated to
    /// <see cref="LlmTransportException"/>. Cancellation is propagated as
    /// <see cref="OperationCanceledException"/>.
    /// </para>
    /// </remarks>
    public sealed class OpenAiStreamingTransport : IStreamingLlmTransport, IDisposable
    {
        private readonly string _apiKey;
        private readonly string _baseUrl;
        private readonly string _model;
        private readonly HttpClient _httpClient;
        private readonly bool _ownsHttpClient;
        private bool _disposed;

        /// <summary>Creates transport with internally-owned HttpClient.</summary>
        public OpenAiStreamingTransport(string apiKey, string baseUrl, string model)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new ArgumentException("API key must not be null, empty, or whitespace.", nameof(apiKey));
            _apiKey = apiKey;
            _model = model ?? throw new ArgumentNullException(nameof(model));
            _baseUrl = (baseUrl ?? "https://api.openai.com").TrimEnd('/');
            _httpClient = new HttpClient
            {
                // Streaming: never time out the response read. Cancellation is via CancellationToken.
                Timeout = Timeout.InfiniteTimeSpan,
            };
            _ownsHttpClient = true;
        }

        /// <summary>Creates transport with an externally-provided HttpClient (for testing).</summary>
        public OpenAiStreamingTransport(string apiKey, string baseUrl, string model, HttpClient httpClient)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new ArgumentException("API key must not be null, empty, or whitespace.", nameof(apiKey));
            _apiKey = apiKey;
            _model = model ?? throw new ArgumentNullException(nameof(model));
            _baseUrl = (baseUrl ?? "https://api.openai.com").TrimEnd('/');
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _ownsHttpClient = false;
        }

        /// <inheritdoc />
        public IAsyncEnumerable<string> SendStreamAsync(
            string systemPrompt,
            string userMessage,
            double temperature = 0.9,
            int maxTokens = 1024,
            CancellationToken cancellationToken = default)
        {
            if (systemPrompt == null) throw new ArgumentNullException(nameof(systemPrompt));
            if (userMessage == null) throw new ArgumentNullException(nameof(userMessage));

            var request = new
            {
                model = _model,
                max_tokens = maxTokens,
                temperature = temperature,
                stream = true,
                messages = new[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = userMessage }
                }
            };
            string requestJson = JsonConvert.SerializeObject(request);
            return StreamCore(requestJson, cancellationToken);
        }

        private async IAsyncEnumerable<string> StreamCore(
            string requestJson,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            string url = _baseUrl + "/v1/chat/completions";

            using (var requestMessage = new HttpRequestMessage(HttpMethod.Post, url))
            {
                requestMessage.Headers.TryAddWithoutValidation("Authorization", "Bearer " + _apiKey);
                requestMessage.Headers.TryAddWithoutValidation("Accept", "text/event-stream");
                requestMessage.Content = new StringContent(requestJson, Encoding.UTF8, "application/json");

                HttpResponseMessage response;
                try
                {
                    response = await _httpClient
                        .SendAsync(requestMessage, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    throw new LlmTransportException(
                        "OpenAI-compatible streaming request failed before response: " + ex.Message, ex);
                }

                using (response)
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        string errorBody;
                        try
                        {
                            errorBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        }
                        catch
                        {
                            errorBody = "<unavailable>";
                        }
                        var truncated = errorBody.Length > 500 ? errorBody.Substring(0, 500) : errorBody;
                        throw new LlmTransportException(
                            $"OpenAI-compatible streaming endpoint returned HTTP {(int)response.StatusCode}: {truncated}");
                    }

                    // Honour pre-cancellation deterministically before we touch the
                    // response stream — mirrors the Anthropic transport's guard so a
                    // pre-cancelled token surfaces as OCE rather than a stream-construction
                    // ArgumentException.
                    cancellationToken.ThrowIfCancellationRequested();

                    Stream responseStream;
                    try
                    {
                        responseStream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        throw new LlmTransportException(
                            "Failed to open OpenAI-compatible streaming response body: " + ex.Message, ex);
                    }

                    cancellationToken.ThrowIfCancellationRequested();

                    // Register cancellation to forcibly close the underlying stream so
                    // a blocking ReadLineAsync wakes up. ReadLineAsync on netstandard2.0
                    // does not accept a CancellationToken; without this dispose-on-cancel
                    // hook a token cancellation mid-read would not interrupt the read.
                    using (responseStream)
                    using (cancellationToken.Register(s =>
                    {
                        try { ((Stream)s!).Dispose(); } catch { /* best effort */ }
                    }, responseStream))
                    using (var reader = new StreamReader(responseStream, Encoding.UTF8))
                    {
                        await foreach (var fragment in ParseSseAsync(reader, cancellationToken).ConfigureAwait(false))
                        {
                            yield return fragment;
                        }
                    }
                }
            }
        }

        private static async IAsyncEnumerable<string> ParseSseAsync(
            StreamReader reader,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            // OpenAI-style SSE: each frame is a `data: <payload>` line followed by a
            // blank line. We ignore other SSE field names (event:, id:, retry:, :comment).
            // Multi-line `data:` continuations are not produced by these providers, but
            // we tolerate them by joining with newlines per the SSE spec.
            StringBuilder? dataBuilder = null;

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                string? line;
                try
                {
                    // StreamReader.ReadLineAsync on netstandard2.0 has no CancellationToken
                    // overload; we check the token between lines and rely on the
                    // dispose-on-cancel registration in StreamCore to wake any blocked
                    // read from inside ReadLineAsync.
                    line = await reader.ReadLineAsync().ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
                {
                    // Stream was disposed by the cancellation callback above.
                    cancellationToken.ThrowIfCancellationRequested();
                    throw; // unreachable
                }
                catch (IOException ioex) when (cancellationToken.IsCancellationRequested)
                {
                    // Some platforms surface the dispose-on-cancel as an IOException
                    // ("stream was closed") rather than ObjectDisposedException.
                    // Treat it as cancellation.
                    cancellationToken.ThrowIfCancellationRequested();
                    throw new LlmTransportException(
                        "OpenAI-compatible streaming response read failed: " + ioex.Message, ioex);
                }
                catch (Exception ex)
                {
                    throw new LlmTransportException(
                        "OpenAI-compatible streaming response read failed: " + ex.Message, ex);
                }

                if (line == null)
                {
                    // End of stream.
                    yield break;
                }

                if (line.Length == 0)
                {
                    // Blank line: dispatch the accumulated event, if any.
                    if (dataBuilder != null)
                    {
                        var data = dataBuilder.ToString();
                        dataBuilder = null;

                        if (data == "[DONE]")
                            yield break;

                        var fragment = ExtractContentFragmentOrThrow(data);
                        if (!string.IsNullOrEmpty(fragment))
                            yield return fragment!;
                    }
                    continue;
                }

                if (line[0] == ':')
                {
                    // SSE comment / keepalive — ignore.
                    continue;
                }

                if (line.StartsWith("data:", StringComparison.Ordinal))
                {
                    var payload = line.Substring(5);
                    if (payload.Length > 0 && payload[0] == ' ')
                        payload = payload.Substring(1);

                    if (dataBuilder == null)
                        dataBuilder = new StringBuilder(payload);
                    else
                        dataBuilder.Append('\n').Append(payload);
                    continue;
                }

                // Other SSE fields (event:, id:, retry:) — ignore.
            }
        }

        /// <summary>
        /// Parse a single SSE <c>data:</c> JSON payload. Returns the
        /// <c>choices[0].delta.content</c> string when present and non-empty,
        /// otherwise <c>null</c>. Throws <see cref="LlmTransportException"/> on
        /// a top-level <c>error</c> object or malformed JSON.
        /// </summary>
        private static string? ExtractContentFragmentOrThrow(string data)
        {
            JObject obj;
            try
            {
                obj = JObject.Parse(data);
            }
            catch (Exception ex)
            {
                var truncated = data.Length > 200 ? data.Substring(0, 200) : data;
                throw new LlmTransportException(
                    $"Malformed SSE chunk from OpenAI-compatible streaming endpoint: {truncated}", ex);
            }

            // Mid-stream provider error frame (some OpenAI-compat providers emit this).
            var errorToken = obj["error"];
            if (errorToken != null && errorToken.Type != JTokenType.Null)
            {
                string? message = null;
                if (errorToken.Type == JTokenType.Object)
                    message = errorToken["message"]?.ToString();
                if (string.IsNullOrEmpty(message))
                    message = errorToken.ToString(Formatting.None);
                throw new LlmTransportException(
                    "OpenAI-compatible streaming endpoint returned error frame: " + message);
            }

            var content = obj["choices"]?[0]?["delta"]?["content"]?.Value<string?>();
            return string.IsNullOrEmpty(content) ? null : content;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                if (_ownsHttpClient)
                    _httpClient.Dispose();
                _disposed = true;
            }
        }
    }
}
