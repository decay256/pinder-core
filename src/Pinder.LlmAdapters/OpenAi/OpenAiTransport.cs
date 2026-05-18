using System;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
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
    public sealed class OpenAiTransport : ILlmTransport, IDisposable
    {
        private readonly OpenAiClient _client;
        private readonly string _model;
        private readonly bool _useAnthropicCacheControl;
        private bool _disposed;

        /// <summary>Creates transport with internally-owned OpenAiClient.</summary>
        public OpenAiTransport(string apiKey, string baseUrl, string model, bool useAnthropicCacheControl = true)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new ArgumentException("API key must not be null, empty, or whitespace.", nameof(apiKey));
            _model = model ?? throw new ArgumentNullException(nameof(model));
            _client = new OpenAiClient(apiKey, baseUrl);
            _useAnthropicCacheControl = useAnthropicCacheControl;
        }

        /// <summary>Creates transport with externally-provided HttpClient (for testing).</summary>
        public OpenAiTransport(string apiKey, string baseUrl, string model, System.Net.Http.HttpClient httpClient, bool useAnthropicCacheControl = true)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new ArgumentException("API key must not be null, empty, or whitespace.", nameof(apiKey));
            _model = model ?? throw new ArgumentNullException(nameof(model));
            _client = new OpenAiClient(apiKey, baseUrl, httpClient);
            _useAnthropicCacheControl = useAnthropicCacheControl;
        }

        /// <inheritdoc />
        public async Task<string> SendAsync(string systemPrompt, string userMessage, double temperature = 0.9, int maxTokens = 1024, string? phase = null, CancellationToken ct = default)
        {
            // phase is metadata for decorators; the underlying provider has no use for it.
            _ = phase;
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
            return await _client.SendChatCompletionAsync(requestJson, ct).ConfigureAwait(false);
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
