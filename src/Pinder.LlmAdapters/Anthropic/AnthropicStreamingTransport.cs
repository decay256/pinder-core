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
using Pinder.LlmAdapters.Anthropic.Dto;

namespace Pinder.LlmAdapters.Anthropic
{
    /// <summary>
    /// Streaming counterpart to <see cref="AnthropicTransport"/>. Implements
    /// <see cref="IStreamingLlmTransport"/> on top of the Anthropic Messages
    /// API with <c>stream: true</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Wire format.</b> The Anthropic Messages API streams responses as
    /// Server-Sent Events. A typical successful stream looks like:
    /// </para>
    /// <code>
    /// event: message_start
    /// data: {"type":"message_start","message":{"id":"msg_01","model":"claude-sonnet-4-...","role":"assistant","content":[],"usage":{"input_tokens":25,"output_tokens":1}}}
    ///
    /// event: content_block_start
    /// data: {"type":"content_block_start","index":0,"content_block":{"type":"text","text":""}}
    ///
    /// event: ping
    /// data: {"type":"ping"}
    ///
    /// event: content_block_delta
    /// data: {"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"Hello"}}
    ///
    /// event: content_block_delta
    /// data: {"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":" world"}}
    ///
    /// event: content_block_stop
    /// data: {"type":"content_block_stop","index":0}
    ///
    /// event: message_delta
    /// data: {"type":"message_delta","delta":{"stop_reason":"end_turn","stop_sequence":null},"usage":{"output_tokens":15}}
    ///
    /// event: message_stop
    /// data: {"type":"message_stop"}
    /// </code>
    /// <para>
    /// On failure the server may emit:
    /// </para>
    /// <code>
    /// event: error
    /// data: {"type":"error","error":{"type":"overloaded_error","message":"Overloaded"}}
    /// </code>
    /// <para>
    /// <b>Contract.</b>
    /// </para>
    /// <list type="bullet">
    ///   <item>Yields only <c>content_block_delta</c> frames whose
    ///   <c>delta.type == "text_delta"</c> — the <c>delta.text</c> field, in
    ///   arrival order. Multi-chunk text reassembles by concatenation at the
    ///   call-site.</item>
    ///   <item>All other event types (<c>message_start</c>,
    ///   <c>content_block_start</c>, <c>ping</c>, <c>content_block_stop</c>,
    ///   <c>message_delta</c>, <c>message_stop</c>, unknown) are silently
    ///   ignored. The stream terminates when the underlying response stream
    ///   ends; no sentinel is required (Anthropic does not send <c>[DONE]</c>).</item>
    ///   <item>An open-time non-2xx HTTP response throws
    ///   <see cref="LlmTransportException"/> with the status code and a
    ///   truncated body excerpt (≤1KB).</item>
    ///   <item>A mid-stream <c>event: error</c> frame throws
    ///   <see cref="LlmTransportException"/> with the provider error message.</item>
    ///   <item>Mid-stream network/IO failures are wrapped in
    ///   <see cref="LlmTransportException"/> preserving the inner exception.</item>
    ///   <item>The supplied <see cref="CancellationToken"/> is propagated to
    ///   the HTTP call and observed between SSE frames; cancellation closes
    ///   the response cleanly and surfaces as
    ///   <see cref="OperationCanceledException"/> (not a transport failure).</item>
    /// </list>
    /// </remarks>
    public sealed class AnthropicStreamingTransport : IStreamingLlmTransport, IDisposable
    {
        private const string ApiUrl = "https://api.anthropic.com/v1/messages";
        private const string AnthropicVersion = "2023-06-01";
        private const int MaxBodyExcerptBytes = 1024;

        private readonly HttpClient _httpClient;
        private readonly bool _ownsHttpClient;
        private readonly string _model;
        private bool _disposed;

        /// <summary>
        /// Creates a transport with an internally-owned <see cref="HttpClient"/>.
        /// </summary>
        /// <param name="apiKey">Anthropic API key. Must not be null/empty/whitespace.</param>
        /// <param name="model">Model identifier (e.g. <c>claude-sonnet-4-20250514</c>).</param>
        public AnthropicStreamingTransport(string apiKey, string model = "claude-sonnet-4-20250514")
        {
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new ArgumentException("API key must not be null, empty, or whitespace.", nameof(apiKey));
            _model = model ?? throw new ArgumentNullException(nameof(model));
            _httpClient = new HttpClient();
            ConfigureHeaders(_httpClient, apiKey);
            _ownsHttpClient = true;
        }

        /// <summary>
        /// Creates a transport with an externally-provided <see cref="HttpClient"/>
        /// (typically for tests with a mock <see cref="HttpMessageHandler"/>).
        /// The supplied client is NOT disposed by <see cref="Dispose"/>.
        /// </summary>
        public AnthropicStreamingTransport(string apiKey, string model, HttpClient httpClient)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new ArgumentException("API key must not be null, empty, or whitespace.", nameof(apiKey));
            _model = model ?? throw new ArgumentNullException(nameof(model));
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            ConfigureHeaders(_httpClient, apiKey);
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

            var systemBlocks = new[]
            {
                new ContentBlock { Type = "text", Text = systemPrompt }
            };
            var request = AnthropicRequestBuilders.BuildMessagesRequest(
                _model, maxTokens, systemBlocks, userMessage, temperature);

            return StreamCoreAsync(request, cancellationToken);
        }

        private async IAsyncEnumerable<string> StreamCoreAsync(
            MessagesRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            // Inject "stream":true into the serialized payload. We don't have
            // a Stream property on MessagesRequest, so do it via JObject.
            var payload = JObject.FromObject(request);
            payload["stream"] = true;
            var json = payload.ToString(Formatting.None);

            HttpResponseMessage response;
            try
            {
                using (var content = new StringContent(json, Encoding.UTF8, "application/json"))
                using (var req = new HttpRequestMessage(HttpMethod.Post, ApiUrl) { Content = content })
                {
                    response = await _httpClient
                        .SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                        .ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new LlmTransportException(
                    "Anthropic streaming request failed before headers: " + ex.Message, ex);
            }

            try
            {
                if (!response.IsSuccessStatusCode)
                {
                    string body;
                    try
                    {
                        body = await response.Content.ReadAsStringAsync().ConfigureAwait(false) ?? "";
                    }
                    catch
                    {
                        body = "";
                    }
                    var excerpt = body.Length > MaxBodyExcerptBytes
                        ? body.Substring(0, MaxBodyExcerptBytes) + "…[truncated]"
                        : body;
                    throw new LlmTransportException(
                        $"Anthropic streaming request failed: HTTP {(int)response.StatusCode} {response.StatusCode}. Body: {excerpt}");
                }

                await foreach (var fragment in ReadSseAsync(response, cancellationToken).ConfigureAwait(false))
                {
                    yield return fragment;
                }
            }
            finally
            {
                response.Dispose();
            }
        }

        private static async IAsyncEnumerable<string> ReadSseAsync(
            HttpResponseMessage response,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            // Honour pre-cancellation deterministically before we touch the
            // response stream — otherwise a cancellation registration that
            // fires before StreamReader is constructed throws
            // ArgumentException("Stream was not readable") instead of OCE.
            cancellationToken.ThrowIfCancellationRequested();

            Stream stream;
            try
            {
                stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                throw new LlmTransportException(
                    "Anthropic streaming request failed reading response stream: " + ex.Message, ex);
            }

            cancellationToken.ThrowIfCancellationRequested();

            // Register cancellation to forcibly close the underlying stream so
            // a blocking ReadLineAsync wakes up. ReadLineAsync on
            // netstandard2.0 does not accept a CancellationToken.
            using (stream)
            using (var ctRegistration = cancellationToken.Register(s =>
            {
                try { ((Stream)s!).Dispose(); } catch { /* best effort */ }
            }, stream))
            using (var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 4096, leaveOpen: false))
            {
                var dataBuffer = new StringBuilder();

                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    string? line;
                    try
                    {
                        line = await reader.ReadLineAsync().ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
                    {
                        // Stream was disposed by the cancellation callback above.
                        cancellationToken.ThrowIfCancellationRequested();
                        throw; // unreachable
                    }
                    catch (IOException ioex) when (cancellationToken.IsCancellationRequested)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        throw new LlmTransportException(
                            "Anthropic streaming I/O failure: " + ioex.Message, ioex);
                    }
                    catch (Exception ex)
                    {
                        throw new LlmTransportException(
                            "Anthropic streaming I/O failure: " + ex.Message, ex);
                    }

                    if (line == null)
                    {
                        // End of stream. If we have a buffered data line, dispatch it.
                        if (dataBuffer.Length > 0)
                        {
                            var emitFinal = TryHandleDataFrame(dataBuffer.ToString(), out var frag, out var errMsg);
                            dataBuffer.Clear();
                            if (errMsg != null)
                                throw new LlmTransportException("Anthropic streaming error: " + errMsg);
                            if (emitFinal && frag != null)
                                yield return frag;
                        }
                        yield break;
                    }

                    if (line.Length == 0)
                    {
                        // Frame boundary. Process whatever we accumulated.
                        if (dataBuffer.Length > 0)
                        {
                            var emit = TryHandleDataFrame(dataBuffer.ToString(), out var frag, out var errMsg);
                            dataBuffer.Clear();
                            if (errMsg != null)
                                throw new LlmTransportException("Anthropic streaming error: " + errMsg);
                            if (emit && frag != null)
                                yield return frag;
                        }
                        continue;
                    }

                    if (line[0] == ':')
                    {
                        // SSE comment — ignore.
                        continue;
                    }

                    // We only care about data: lines. The `event:` line is
                    // informational and matches the type embedded in the
                    // data JSON, which we use as the source of truth.
                    if (line.StartsWith("data:", StringComparison.Ordinal))
                    {
                        // Per SSE spec: strip a single optional leading space.
                        var payload = line.Length > 5 && line[5] == ' '
                            ? line.Substring(6)
                            : line.Substring(5);
                        if (dataBuffer.Length > 0) dataBuffer.Append('\n');
                        dataBuffer.Append(payload);
                    }
                    // Other SSE fields (event:, id:, retry:) are ignored —
                    // the data payload's "type" field is authoritative.
                }
            }
        }

        /// <summary>
        /// Parse one SSE data frame. Returns true when a text fragment should
        /// be yielded (with <paramref name="fragment"/> set). Sets
        /// <paramref name="errorMessage"/> when the frame represents an
        /// Anthropic <c>error</c> event; the caller must throw.
        /// </summary>
        private static bool TryHandleDataFrame(string data, out string? fragment, out string? errorMessage)
        {
            fragment = null;
            errorMessage = null;

            if (string.IsNullOrWhiteSpace(data))
                return false;

            JObject obj;
            try
            {
                obj = JObject.Parse(data);
            }
            catch (JsonException)
            {
                // Malformed frame — ignore. Anthropic does not send raw
                // strings on data: lines; if we see one, it's safer to
                // skip than to abort the whole stream.
                return false;
            }

            var type = (string?)obj["type"];
            if (string.IsNullOrEmpty(type))
                return false;

            switch (type)
            {
                case "content_block_delta":
                {
                    var delta = obj["delta"] as JObject;
                    var deltaType = (string?)delta?["type"];
                    if (deltaType == "text_delta")
                    {
                        var text = (string?)delta!["text"];
                        if (!string.IsNullOrEmpty(text))
                        {
                            fragment = text;
                            return true;
                        }
                    }
                    return false;
                }
                case "error":
                {
                    var err = obj["error"] as JObject;
                    var msg = (string?)err?["message"] ?? "unknown error";
                    var errType = (string?)err?["type"];
                    errorMessage = string.IsNullOrEmpty(errType)
                        ? msg
                        : $"{errType}: {msg}";
                    return false;
                }
                default:
                    // message_start, content_block_start, ping,
                    // content_block_stop, message_delta, message_stop,
                    // and any future event types — ignore.
                    return false;
            }
        }

        /// <summary>
        /// Disposes the internally-owned <see cref="HttpClient"/> if this
        /// instance created it. Safe to call multiple times.
        /// </summary>
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
            // Idempotent: only add headers that aren't already present (a
            // shared HttpClient passed across multiple constructions would
            // otherwise throw on re-add).
            if (!client.DefaultRequestHeaders.Contains("x-api-key"))
                client.DefaultRequestHeaders.Add("x-api-key", apiKey);
            if (!client.DefaultRequestHeaders.Contains("anthropic-version"))
                client.DefaultRequestHeaders.Add("anthropic-version", AnthropicVersion);
        }
    }
}
