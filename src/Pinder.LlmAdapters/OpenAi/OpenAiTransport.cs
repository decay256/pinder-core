using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Pinder.Core.Interfaces;

namespace Pinder.LlmAdapters.OpenAi
{
    /// <summary>
    /// ILlmTransport implementation wrapping OpenAiClient.
    /// Thin wrapper — no game logic. Converts (systemPrompt, userMessage) into
    /// an OpenAI-compatible chat completions request and returns the response text.
    /// Supports any provider with an OpenAI-compatible API (OpenAI, Groq, Together,
    /// OpenRouter, Ollama, etc.).
    /// </summary>
    public sealed class OpenAiTransport : ILlmTransport, IStructuredLlmTransport, ITokenUsageProvider, IDisposable
    {
        private readonly OpenAiClient _client;
        private readonly string _model;
        private readonly bool _useAnthropicCacheControl;
        private readonly LlmCallTelemetryOptions? _telemetry;
        private readonly bool _supportsNativeStructuredOutput;
        private readonly OpenAiUsageCollector _usageCollector = new OpenAiUsageCollector();
        private bool _disposed;

        /// <summary>Creates transport with internally-owned OpenAiClient.</summary>
        public OpenAiTransport(
            string apiKey,
            string baseUrl,
            string model,
            bool useAnthropicCacheControl = true,
            LlmCallTelemetryOptions? telemetry = null,
            bool supportsNativeStructuredOutput = false)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new ArgumentException("API key must not be null, empty, or whitespace.", nameof(apiKey));
            _model = model ?? throw new ArgumentNullException(nameof(model));
            _client = new OpenAiClient(apiKey, baseUrl);
            _useAnthropicCacheControl = useAnthropicCacheControl;
            _telemetry = telemetry;
            _supportsNativeStructuredOutput = supportsNativeStructuredOutput;
        }

        /// <summary>Creates transport with externally-provided HttpClient (for testing).</summary>
        public OpenAiTransport(
            string apiKey,
            string baseUrl,
            string model,
            System.Net.Http.HttpClient httpClient,
            bool useAnthropicCacheControl = true,
            LlmCallTelemetryOptions? telemetry = null,
            bool supportsNativeStructuredOutput = false)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new ArgumentException("API key must not be null, empty, or whitespace.", nameof(apiKey));
            _model = model ?? throw new ArgumentNullException(nameof(model));
            _client = new OpenAiClient(apiKey, baseUrl, httpClient);
            _useAnthropicCacheControl = useAnthropicCacheControl;
            _telemetry = telemetry;
            _supportsNativeStructuredOutput = supportsNativeStructuredOutput;
        }

        /// <inheritdoc />
        public async Task<string> SendAsync(string systemPrompt, string userMessage, double temperature = 0.9, int maxTokens = 1024, string? phase = null, CancellationToken ct = default)
        {
            if (systemPrompt == null) throw new ArgumentNullException(nameof(systemPrompt));
            if (userMessage == null) throw new ArgumentNullException(nameof(userMessage));

            // #947: emit the system prompt as a content-block with
            // cache_control: ephemeral so OpenRouter→Anthropic / direct
            // Anthropic-compatible routes can hit the prompt cache. See
            // OpenAiCacheControl for the rationale.
            var systemContent = OpenAiCacheControl.BuildSystemContent(
                systemPrompt, _useAnthropicCacheControl);

            var request = new
            {
                model = _model,
                max_tokens = maxTokens,
                temperature = temperature,
                messages = new object[]
                {
                    new { role = "system", content = systemContent },
                    new { role = "user", content = userMessage }
                }
            };

            string requestJson = JsonConvert.SerializeObject(request);
            // #794: forward the engine-level cancellation token to the underlying
            // HTTP call so a mid-turn Cancel() halts the in-flight request.
            try
            {
                var (text, rawJson) = await _client.SendChatCompletionWithUsageAsync(
                    requestJson,
                    ct,
                    _telemetry,
                    provider: "openai-compatible",
                    phase: phase).ConfigureAwait(false);
                _usageCollector.Collect(rawJson);
                return text;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (HttpRequestException ex)
            {
                throw NormalizeHttpFailure(ex);
            }
        }

        public async Task<StructuredLlmResponse> SendStructuredAsync(
            StructuredLlmRequest structuredRequest,
            CancellationToken ct = default)
        {
            if (structuredRequest == null) throw new ArgumentNullException(nameof(structuredRequest));
            if (!_supportsNativeStructuredOutput)
            {
                var fallbackRequest = new
                {
                    model = _model,
                    max_tokens = structuredRequest.MaxTokens,
                    temperature = structuredRequest.Temperature,
                    messages = new object[]
                    {
                        new { role = "system", content = OpenAiCacheControl.BuildSystemContent(structuredRequest.SystemPrompt, _useAnthropicCacheControl) },
                        new { role = "user", content = structuredRequest.UserMessage }
                    }
                };
                string fallbackRequestJson = JsonConvert.SerializeObject(fallbackRequest);
                try
                {
                    var (text, fallbackRawJson) = await _client.SendChatCompletionWithUsageAsync(
                        fallbackRequestJson,
                        ct,
                        _telemetry,
                        provider: "openai-compatible",
                        phase: structuredRequest.Phase).ConfigureAwait(false);
                    _usageCollector.Collect(fallbackRawJson);
                    return new StructuredLlmResponse(
                        text,
                        provider: "openai-compatible",
                        model: _model,
                        usedNativeStructuredOutput: false,
                        providerRequestJson: fallbackRequestJson,
                        validationMode: "local_validation");
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (HttpRequestException ex)
                {
                    throw NormalizeHttpFailure(ex);
                }
            }

            var systemContent = OpenAiCacheControl.BuildSystemContent(
                structuredRequest.SystemPrompt, _useAnthropicCacheControl);

            var request = new
            {
                model = _model,
                max_tokens = structuredRequest.MaxTokens,
                temperature = structuredRequest.Temperature,
                response_format = new
                {
                    type = "json_schema",
                    json_schema = new
                    {
                        name = structuredRequest.SchemaName,
                        strict = true,
                        schema = JObject.Parse(structuredRequest.JsonSchema)
                    }
                },
                messages = new object[]
                {
                    new { role = "system", content = systemContent },
                    new { role = "user", content = structuredRequest.UserMessage }
                }
            };

            string requestJson = JsonConvert.SerializeObject(request);
            try
            {
                var (_, rawJson) = await _client.SendChatCompletionWithUsageAsync(
                    requestJson,
                    ct,
                    _telemetry,
                    provider: "openai-compatible",
                    phase: structuredRequest.Phase).ConfigureAwait(false);
                _usageCollector.Collect(rawJson);

                string jsonText = OpenAiClient.ExtractAssistantContentOnly(rawJson);
                return new StructuredLlmResponse(
                    jsonText,
                    provider: "openai-compatible",
                    model: _model,
                    usedNativeStructuredOutput: true,
                    providerRequestJson: requestJson,
                    validationMode: "openai_json_schema");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (HttpRequestException ex)
            {
                throw NormalizeHttpFailure(ex);
            }
        }

        /// <inheritdoc />
        public SessionTokenUsage GetSessionUsage() => _usageCollector.GetSessionUsage();

        private static LlmTransportException NormalizeHttpFailure(HttpRequestException ex)
        {
            var kind = LlmFailureKind.Network;
            if (ex.Data.Contains("StatusCode") && ex.Data["StatusCode"] is int statusCode)
            {
                kind = statusCode == 429
                    ? LlmFailureKind.RateLimited
                    : statusCode >= 500 ? LlmFailureKind.Network : LlmFailureKind.Unknown;
            }

            return new LlmTransportException(ex.Message, kind, ex);
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _client.Dispose();
                _disposed = true;
            }
        }
    }
}
