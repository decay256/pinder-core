using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
    ///   <item><c>message_start</c> and <c>message_delta</c> frames are not
    ///   yielded as text, but their <c>usage</c> blocks are captured for
    ///   token/cost telemetry (issue #1115): <c>message_start</c> carries
    ///   <c>usage.input_tokens</c>, <c>usage.cache_creation_input_tokens</c>
    ///   and <c>usage.cache_read_input_tokens</c>; <c>message_delta</c>
    ///   carries the final cumulative <c>usage.output_tokens</c>. The
    ///   accumulated usage is exposed via
    ///   <see cref="ITokenUsageProvider.GetSessionUsage"/> and
    ///   <see cref="GetCallStats"/>, mirroring the non-streaming
    ///   <c>AnthropicLlmAdapter</c>.</item>
    ///   <item>All other event types (<c>content_block_start</c>, <c>ping</c>,
    ///   <c>content_block_stop</c>, <c>message_stop</c>, unknown) are silently
    ///   ignored. The stream terminates when the underlying response stream
    ///   ends; no sentinel is required (Anthropic does not send <c>[DONE]</c>).</item>
    ///   <item>An open-time non-2xx HTTP response throws
    ///   <see cref="LlmTransportException"/> with status code and safe body
    ///   length/hash diagnostics.</item>
    ///   <item>A mid-stream <c>event: error</c> frame throws
    ///   <see cref="LlmTransportException"/> with safe error-frame diagnostics.</item>
    ///   <item>Mid-stream network/IO failures are wrapped in
    ///   <see cref="LlmTransportException"/> preserving the inner exception.</item>
    ///   <item>The supplied <see cref="CancellationToken"/> is propagated to
    ///   the HTTP call and observed between SSE frames; cancellation closes
    ///   the response cleanly and surfaces as
    ///   <see cref="OperationCanceledException"/> (not a transport failure).</item>
    /// </list>
    /// </remarks>
    public sealed class AnthropicStreamingTransport : IStreamingLlmTransport, ITokenUsageProvider, IDisposable
    {
        private const string ApiUrl = "https://api.anthropic.com/v1/messages";
        private const string AnthropicVersion = "2023-06-01";

        private readonly HttpClient _httpClient;
        private readonly bool _ownsHttpClient;
        private readonly string _model;
        private bool _disposed;

        // Issue #1115: token usage captured from message_start / message_delta
        // SSE frames. One CallSummaryStat is committed per completed stream,
        // mirroring how the non-streaming AnthropicDebugLogger records usage
        // per call. Exposed via ITokenUsageProvider / GetCallStats so the same
        // downstream telemetry path that consumes the non-streaming adapter
        // sees real (non-zero) numbers for the streaming path.
        private readonly List<CallSummaryStat> _callStats = new List<CallSummaryStat>();
        private readonly object _statsLock = new object();

        /// <summary>
        /// Creates a transport with an internally-owned <see cref="HttpClient"/>.
        /// </summary>
        /// <param name="apiKey">Anthropic API key. Must not be null/empty/whitespace.</param>
        /// <param name="model">Model identifier (e.g. <c>claude-sonnet-4-20250514</c>).</param>
        public AnthropicStreamingTransport(string apiKey, string model = AnthropicModelIds.DefaultModel)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new ArgumentException("API key must not be null, empty, or whitespace.", nameof(apiKey));
            _model = AnthropicModelIds.ToApiId(model ?? throw new ArgumentNullException(nameof(model)));
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
            _model = AnthropicModelIds.ToApiId(model ?? throw new ArgumentNullException(nameof(model)));
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
            CancellationToken cancellationToken = default,
            string? phase = null)
        {
            if (systemPrompt == null) throw new ArgumentNullException(nameof(systemPrompt));
            if (userMessage == null) throw new ArgumentNullException(nameof(userMessage));

            var systemBlocks = new[]
            {
                new ContentBlock
                {
                    Type = "text",
                    Text = systemPrompt,
                    CacheControl = new CacheControl { Type = "ephemeral" }
                }
            };
            var request = AnthropicRequestBuilders.BuildMessagesRequest(
                _model, maxTokens, systemBlocks, userMessage, temperature);

            return StreamCoreAsync(request, phase, cancellationToken);
        }

        private async IAsyncEnumerable<string> StreamCoreAsync(
            MessagesRequest request,
            string? phase,
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
                    "Anthropic streaming request failed before headers: " + ex.Message, LlmFailureKind.Network, ex);
            }

            // Issue #1115: accumulate usage across this stream's SSE frames so
            // we can commit one per-call telemetry row once the stream ends.
            var usage = new StreamUsageAccumulator();
            bool streamCompleted = false;
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
                    LlmFailureKind failureKind = LlmFailureKind.Unknown;
                    string errorType = null;
                    try
                    {
                        if (!string.IsNullOrWhiteSpace(body))
                        {
                            var jsonObj = Newtonsoft.Json.Linq.JObject.Parse(body);
                            errorType = jsonObj["error"]?["type"]?.ToString();
                        }
                    }
                    catch
                    {
                        // ignore parsing failures
                    }

                    if (response.StatusCode == System.Net.HttpStatusCode.NotFound || errorType == "not_found_error")
                    {
                        failureKind = LlmFailureKind.ModelNotFound;
                    }
                    else if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized || errorType == "authentication_error")
                    {
                        failureKind = LlmFailureKind.Unauthorized;
                    }
                    else if ((int)response.StatusCode == 429 || errorType == "rate_limit_error")
                    {
                        failureKind = LlmFailureKind.RateLimited;
                    }

                    string message;
                    if (failureKind == LlmFailureKind.ModelNotFound)
                    {
                        message = LlmDiagnosticFormatter.ProviderFailure(
                            "anthropic-streaming",
                            "Anthropic streaming request failed (not_found_error). Operator hint: Ensure that the model ID is typed correctly and is active on your Anthropic account/plan.",
                            statusCode: (int)response.StatusCode,
                            model: _model,
                            phase: phase,
                            body: body);
                    }
                    else if (failureKind == LlmFailureKind.Unauthorized)
                    {
                        message = LlmDiagnosticFormatter.ProviderFailure(
                            "anthropic-streaming",
                            "Anthropic streaming request failed (authentication_error). Operator hint: Check your API key.",
                            statusCode: (int)response.StatusCode,
                            model: _model,
                            phase: phase,
                            body: body);
                    }
                    else if (failureKind == LlmFailureKind.RateLimited)
                    {
                        message = LlmDiagnosticFormatter.ProviderFailure(
                            "anthropic-streaming",
                            "Anthropic streaming request failed (rate_limit_error). Operator hint: Retry after the provider rate limit resets.",
                            statusCode: (int)response.StatusCode,
                            model: _model,
                            phase: phase,
                            body: body);
                    }
                    else
                    {
                        message = LlmDiagnosticFormatter.ProviderFailure(
                            "anthropic-streaming",
                            "Anthropic streaming request failed.",
                            statusCode: (int)response.StatusCode,
                            model: _model,
                            phase: phase,
                            body: body);
                    }

                    throw new LlmTransportException(message, failureKind);
                }

                await foreach (var fragment in ReadSseAsync(response, usage, cancellationToken).ConfigureAwait(false))
                {
                    yield return fragment;
                }
                streamCompleted = true;
            }
            finally
            {
                // Commit usage telemetry only when the stream ran to a clean
                // completion (mirrors the non-streaming path, which records
                // usage only on a successful response). On exceptions /
                // cancellation we skip so we never record a partial/zero row.
                if (streamCompleted && usage.HasUsage)
                {
                    CommitUsage(usage);
                }
                response.Dispose();
            }
        }

        private static async IAsyncEnumerable<string> ReadSseAsync(
            HttpResponseMessage response,
            StreamUsageAccumulator usage,
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
                    "Anthropic streaming request failed reading response stream: " + ex.Message, LlmFailureKind.Network, ex);
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
                            "Anthropic streaming I/O failure: " + ioex.Message, LlmFailureKind.Network, ioex);
                    }
                    catch (Exception ex)
                    {
                        throw new LlmTransportException(
                            "Anthropic streaming I/O failure: " + ex.Message, LlmFailureKind.Network, ex);
                    }

                    if (line == null)
                    {
                        // End of stream. If we have a buffered data line, dispatch it.
                        if (dataBuffer.Length > 0)
                        {
                            var emitFinal = TryHandleDataFrame(dataBuffer.ToString(), usage, out var frag, out var errMsg);
                            dataBuffer.Clear();
                            if (errMsg != null)
                                throw new LlmTransportException(errMsg);
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
                            var emit = TryHandleDataFrame(dataBuffer.ToString(), usage, out var frag, out var errMsg);
                            dataBuffer.Clear();
                            if (errMsg != null)
                                throw new LlmTransportException(errMsg);
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
        /// Anthropic <c>error</c> event; the caller must throw. Side-effect:
        /// folds any <c>usage</c> block on <c>message_start</c> /
        /// <c>message_delta</c> frames into <paramref name="usage"/> (issue
        /// #1115).
        /// </summary>
        private static bool TryHandleDataFrame(string data, StreamUsageAccumulator usage, out string? fragment, out string? errorMessage)
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
                case "message_start":
                {
                    // message_start carries the prompt-side usage:
                    // input_tokens + cache_creation_input_tokens +
                    // cache_read_input_tokens (and an initial output_tokens
                    // value that message_delta later supersedes).
                    var messageUsage = obj["message"]?["usage"] as JObject;
                    usage.ApplyMessageStart(messageUsage);
                    return false;
                }
                case "message_delta":
                {
                    // message_delta carries the final cumulative output_tokens.
                    var deltaUsage = obj["usage"] as JObject;
                    usage.ApplyMessageDelta(deltaUsage);
                    return false;
                }
                case "error":
                {
                    var err = obj["error"] as JObject;
                    var errorBody = err == null
                        ? obj.ToString(Formatting.None)
                        : err.ToString(Formatting.None);
                    errorMessage = LlmDiagnosticFormatter.ProviderFailure(
                        "anthropic-streaming",
                        "Anthropic streaming endpoint returned an error frame.",
                        body: errorBody,
                        bodyLabel: "error");
                    return false;
                }
                default:
                    // content_block_start, ping, content_block_stop,
                    // message_stop, and any future event types — ignore.
                    return false;
            }
        }

        /// <summary>
        /// Returns a read-only snapshot of the per-stream token stats captured
        /// during this transport's lifetime (issue #1115). Mirrors
        /// <c>AnthropicLlmAdapter.GetCallStats</c> for the streaming path.
        /// </summary>
        public IReadOnlyList<CallSummaryStat> GetCallStats()
        {
            lock (_statsLock)
            {
                return _callStats.ToArray();
            }
        }

        /// <inheritdoc />
        public SessionTokenUsage GetSessionUsage()
        {
            lock (_statsLock)
            {
                return new SessionTokenUsage
                {
                    InputTokens              = _callStats.Sum(s => s.InputTokens),
                    OutputTokens             = _callStats.Sum(s => s.OutputTokens),
                    CacheReadInputTokens     = _callStats.Sum(s => s.CacheReadInputTokens),
                    CacheCreationInputTokens = _callStats.Sum(s => s.CacheCreationInputTokens),
                    CallCount                = _callStats.Count
                };
            }
        }

        private void CommitUsage(StreamUsageAccumulator usage)
        {
            var stat = new CallSummaryStat
            {
                Turn = 0,
                Type = "stream",
                InputTokens = usage.InputTokens,
                OutputTokens = usage.OutputTokens,
                CacheReadInputTokens = usage.CacheReadInputTokens,
                CacheCreationInputTokens = usage.CacheCreationInputTokens
            };
            lock (_statsLock)
            {
                _callStats.Add(stat);
            }
        }

        /// <summary>
        /// Mutable accumulator for the usage fields scattered across an
        /// Anthropic streaming response. Anthropic splits usage between the
        /// <c>message_start</c> frame (prompt + cache tokens) and the final
        /// <c>message_delta</c> frame (output tokens), so we fold both into a
        /// single record (issue #1115).
        /// </summary>
        private sealed class StreamUsageAccumulator
        {
            public int InputTokens { get; private set; }
            public int OutputTokens { get; private set; }
            public int CacheReadInputTokens { get; private set; }
            public int CacheCreationInputTokens { get; private set; }

            /// <summary>True once any usage frame has contributed a value.</summary>
            public bool HasUsage { get; private set; }

            public void ApplyMessageStart(JObject? usage)
            {
                if (usage == null) return;
                InputTokens = (int?)usage["input_tokens"] ?? InputTokens;
                CacheCreationInputTokens = (int?)usage["cache_creation_input_tokens"] ?? CacheCreationInputTokens;
                CacheReadInputTokens = (int?)usage["cache_read_input_tokens"] ?? CacheReadInputTokens;
                // message_start also reports an initial output_tokens (usually 1);
                // message_delta supersedes it with the final cumulative count.
                OutputTokens = (int?)usage["output_tokens"] ?? OutputTokens;
                HasUsage = true;
            }

            public void ApplyMessageDelta(JObject? usage)
            {
                if (usage == null) return;
                // Anthropic emits the cumulative output_tokens on message_delta;
                // take it as authoritative when present. Input/cache fields may
                // also appear on later frames; fold them if present.
                OutputTokens = (int?)usage["output_tokens"] ?? OutputTokens;
                var input = (int?)usage["input_tokens"];
                if (input.HasValue) InputTokens = input.Value;
                var cacheCreate = (int?)usage["cache_creation_input_tokens"];
                if (cacheCreate.HasValue) CacheCreationInputTokens = cacheCreate.Value;
                var cacheRead = (int?)usage["cache_read_input_tokens"];
                if (cacheRead.HasValue) CacheReadInputTokens = cacheRead.Value;
                HasUsage = true;
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
