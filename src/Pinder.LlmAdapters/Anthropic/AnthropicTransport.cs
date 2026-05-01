using System;
using System.Threading;
using System.Threading.Tasks;
using Pinder.Core.Interfaces;
using Pinder.LlmAdapters.Anthropic.Dto;

namespace Pinder.LlmAdapters.Anthropic
{
    /// <summary>
    /// ILlmTransport implementation wrapping AnthropicClient.
    /// Thin wrapper — no game logic. Converts (systemPrompt, userMessage) into
    /// an Anthropic MessagesRequest and returns the response text.
    /// </summary>
    public sealed class AnthropicTransport : ILlmTransport, IDisposable
    {
        private readonly AnthropicClient _client;
        private readonly string _model;
        private bool _disposed;

        /// <summary>Creates transport with internally-owned AnthropicClient.</summary>
        public AnthropicTransport(string apiKey, string model = "claude-sonnet-4-20250514")
        {
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new ArgumentException("API key must not be null, empty, or whitespace.", nameof(apiKey));
            _model = model ?? throw new ArgumentNullException(nameof(model));
            _client = new AnthropicClient(apiKey);
        }

        /// <summary>Creates transport with externally-provided HttpClient (for testing).</summary>
        public AnthropicTransport(string apiKey, string model, System.Net.Http.HttpClient httpClient)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new ArgumentException("API key must not be null, empty, or whitespace.", nameof(apiKey));
            _model = model ?? throw new ArgumentNullException(nameof(model));
            _client = new AnthropicClient(apiKey, httpClient);
        }

        /// <inheritdoc />
        public async Task<string> SendAsync(string systemPrompt, string userMessage, double temperature = 0.9, int maxTokens = 1024, string? phase = null, CancellationToken ct = default)
        {
            // phase is metadata for decorators; the underlying provider has no use for it.
            _ = phase;
            if (systemPrompt == null) throw new ArgumentNullException(nameof(systemPrompt));
            if (userMessage == null) throw new ArgumentNullException(nameof(userMessage));

            var systemBlocks = new[]
            {
                new ContentBlock { Type = "text", Text = systemPrompt }
            };

            var request = AnthropicRequestBuilders.BuildMessagesRequest(
                _model, maxTokens, systemBlocks, userMessage, temperature);

            // #794: forward the engine-level cancellation token to the underlying
            // HTTP call so a mid-turn Cancel() halts the in-flight request.
            var response = await _client.SendMessagesAsync(request, ct).ConfigureAwait(false);
            return response.GetText() ?? "";
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
