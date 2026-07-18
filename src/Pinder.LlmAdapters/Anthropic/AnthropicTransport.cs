using System;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using Pinder.Core.Interfaces;
using Pinder.LlmAdapters.Anthropic.Dto;

namespace Pinder.LlmAdapters.Anthropic
{
    /// <summary>
    /// ILlmTransport implementation wrapping AnthropicClient.
    /// Thin wrapper — no game logic. Converts (systemPrompt, userMessage) into
    /// an Anthropic MessagesRequest and returns the response text.
    /// </summary>
    public sealed class AnthropicTransport : ILlmTransport, IStructuredLlmTransport, IDisposable
    {
        private readonly AnthropicClient _client;
        private readonly string _model;
        private readonly AnthropicOptions? _options;
        private readonly LlmCallTelemetryOptions? _telemetry;
        private bool _disposed;

        /// <summary>Creates transport with internally-owned AnthropicClient.</summary>
        public AnthropicTransport(
            string apiKey,
            string model = AnthropicModelIds.DefaultModel,
            LlmCallTelemetryOptions? telemetry = null)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new ArgumentException("API key must not be null, empty, or whitespace.", nameof(apiKey));
            _model = AnthropicModelIds.ToApiId(model ?? throw new ArgumentNullException(nameof(model)));
            _telemetry = telemetry;
            _client = new AnthropicClient(apiKey);
        }

        /// <summary>Creates transport from AnthropicOptions with internally-owned AnthropicClient.</summary>
        public AnthropicTransport(
            AnthropicOptions options,
            LlmCallTelemetryOptions? telemetry = null)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (string.IsNullOrWhiteSpace(options.ApiKey))
                throw new ArgumentException("API key must not be null, empty, or whitespace.", nameof(options));
            _model = AnthropicModelIds.ToApiId(options.Model);
            _options = options;
            _telemetry = telemetry;
            _client = new AnthropicClient(options.ApiKey);
        }

        /// <summary>Creates transport with externally-provided HttpClient (for testing).</summary>
        public AnthropicTransport(
            string apiKey,
            string model,
            System.Net.Http.HttpClient httpClient,
            LlmCallTelemetryOptions? telemetry = null)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new ArgumentException("API key must not be null, empty, or whitespace.", nameof(apiKey));
            _model = AnthropicModelIds.ToApiId(model ?? throw new ArgumentNullException(nameof(model)));
            _telemetry = telemetry;
            _client = new AnthropicClient(apiKey, httpClient);
        }

        /// <summary>Creates transport from AnthropicOptions with externally-provided HttpClient (for testing).</summary>
        public AnthropicTransport(
            AnthropicOptions options,
            System.Net.Http.HttpClient httpClient,
            LlmCallTelemetryOptions? telemetry = null)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (string.IsNullOrWhiteSpace(options.ApiKey))
                throw new ArgumentException("API key must not be null, empty, or whitespace.", nameof(options));
            _model = AnthropicModelIds.ToApiId(options.Model);
            _options = options;
            _telemetry = telemetry;
            _client = new AnthropicClient(options.ApiKey, httpClient);
        }

        /// <inheritdoc />
        public async Task<string> SendAsync(string systemPrompt, string userMessage, double temperature = 0.9, int maxTokens = 1024, string? phase = null, CancellationToken ct = default)
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

            // #794: forward the engine-level cancellation token to the underlying
            // HTTP call so a mid-turn Cancel() halts the in-flight request.
            var response = await _client.SendMessagesAsync(
                request,
                ct,
                _telemetry,
                provider: "anthropic",
                model: _model,
                phase: phase).ConfigureAwait(false);
            var draft = response.GetText() ?? "";
            if (_options == null)
            {
                return draft;
            }

            return await AnthropicResponseImprover.ApplyImprovementAsync(
                _client,
                _options,
                _model,
                maxTokens,
                systemBlocks,
                userMessage,
                draft,
                temperature,
                _telemetry,
                phase,
                ct).ConfigureAwait(false);
        }

        public async Task<StructuredLlmResponse> SendStructuredAsync(
            StructuredLlmRequest request,
            CancellationToken ct = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));

            var systemBlocks = new[]
            {
                new ContentBlock
                {
                    Type = "text",
                    Text = request.SystemPrompt,
                    CacheControl = new CacheControl { Type = "ephemeral" }
                }
            };

            var messagesRequest = AnthropicRequestBuilders.BuildMessagesRequest(
                _model,
                request.MaxTokens,
                systemBlocks,
                request.UserMessage,
                request.Temperature);

            messagesRequest.Tools = new[]
            {
                new ToolDefinition
                {
                    Name = request.SchemaName,
                    Description = $"Return {request.SchemaVersion} JSON for {request.Phase}.",
                    InputSchema = JObject.Parse(request.JsonSchema)
                }
            };
            messagesRequest.ToolChoice = new ToolChoiceOption
            {
                Type = "tool",
                Name = request.SchemaName
            };
            string providerRequestJson = JsonConvert.SerializeObject(messagesRequest);

            var response = await _client.SendMessagesAsync(
                messagesRequest,
                ct,
                _telemetry,
                provider: "anthropic",
                model: _model,
                phase: request.Phase).ConfigureAwait(false);

            var toolInput = response.GetToolInput();
            string jsonText = toolInput != null
                ? toolInput.ToString(Newtonsoft.Json.Formatting.None)
                : response.GetText() ?? string.Empty;

            return new StructuredLlmResponse(
                jsonText,
                provider: "anthropic",
                model: _model,
                usedNativeStructuredOutput: toolInput != null,
                providerRequestJson: providerRequestJson,
                validationMode: toolInput != null ? "anthropic_tool" : "local_validation");
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
