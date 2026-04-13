using System;
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
        private bool _disposed;

        /// <summary>Creates transport with internally-owned OpenAiClient.</summary>
        public OpenAiTransport(string apiKey, string baseUrl, string model)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new ArgumentException("API key must not be null, empty, or whitespace.", nameof(apiKey));
            _model = model ?? throw new ArgumentNullException(nameof(model));
            _client = new OpenAiClient(apiKey, baseUrl);
        }

        /// <summary>Creates transport with externally-provided HttpClient (for testing).</summary>
        public OpenAiTransport(string apiKey, string baseUrl, string model, System.Net.Http.HttpClient httpClient)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new ArgumentException("API key must not be null, empty, or whitespace.", nameof(apiKey));
            _model = model ?? throw new ArgumentNullException(nameof(model));
            _client = new OpenAiClient(apiKey, baseUrl, httpClient);
        }

        /// <inheritdoc />
        public async Task<string> SendAsync(string systemPrompt, string userMessage, double temperature = 0.9, int maxTokens = 1024)
        {
            if (systemPrompt == null) throw new ArgumentNullException(nameof(systemPrompt));
            if (userMessage == null) throw new ArgumentNullException(nameof(userMessage));

            var request = new
            {
                model = _model,
                max_tokens = maxTokens,
                temperature = temperature,
                messages = new[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = userMessage }
                }
            };

            string requestJson = JsonConvert.SerializeObject(request);
            return await _client.SendChatCompletionAsync(requestJson).ConfigureAwait(false);
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
