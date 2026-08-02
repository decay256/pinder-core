using System;
using System.Collections.Generic;
using Pi.AI;

namespace Pinder.LlmAdapters.Pi
{
    /// <summary>
    /// Host-neutral configuration for Pinder's Pi provider composition root.
    /// The host supplies networking through <see cref="FetchFunction"/>.
    /// </summary>
    public sealed class PiProviderTransportOptions
    {
        public string Provider { get; set; } = string.Empty;
        public string Model { get; set; } = string.Empty;
        public string ApiKey { get; set; } = string.Empty;
        public string? BaseUrl { get; set; }
        public FetchFunction? Fetch { get; set; }
        public int MaxRetries { get; set; } = 3;
        public int MaxRetryDelayMilliseconds { get; set; } = 60_000;
        public ThinkingLevel? Reasoning { get; set; }
        public Action<AssistantMessage, string?>? ResponseObserver { get; set; }
    }

    /// <summary>
    /// Builds every supported Pinder provider through Pi.AI's common model and
    /// provider abstractions. Networking and host policy remain injected.
    /// </summary>
    public static class PiProviderTransportFactory
    {
        public static PiLlmTransport Create(PiProviderTransportOptions options)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            string providerName = NormalizeProvider(options.Provider);
            if (string.IsNullOrWhiteSpace(options.Model))
                throw new ArgumentException("A model id is required.", nameof(options));
            if (string.IsNullOrWhiteSpace(options.ApiKey))
                throw new ArgumentException("An API key is required.", nameof(options));
            if (options.Fetch == null)
                throw new ArgumentException("A fetch transport is required.", nameof(options));
            if (options.MaxRetries < 0)
                throw new ArgumentOutOfRangeException(nameof(options), "MaxRetries must be non-negative.");
            if (options.MaxRetryDelayMilliseconds < 0)
                throw new ArgumentOutOfRangeException(nameof(options), "MaxRetryDelayMilliseconds must be non-negative.");

            bool anthropic = providerName == "anthropic";
            var providerId = new ProviderId(providerName);
            var model = new Model
            {
                Id = anthropic
                    ? Pinder.LlmAdapters.Anthropic.AnthropicModelIds.ToApiId(options.Model)
                    : options.Model,
                Name = options.Model,
                Api = anthropic ? KnownApi.AnthropicMessages : KnownApi.OpenAICompletions,
                Provider = providerId,
                BaseUrl = ResolveBaseUrl(providerName, options.BaseUrl),
                Reasoning = providerName == "google",
                Input = new[] { ModelInput.Text },
                ContextWindow = 200_000,
                MaxTokens = 16_384,
                Compatibility = CreateCompatibility(providerName, options.Model),
            };

            IProviderStreams api = anthropic
                ? (IProviderStreams)new AnthropicMessagesApi()
                : new OpenAICompletionsApi();
            IProvider provider = ProviderFactory.CreateProvider(new CreateProviderOptions
            {
                Id = providerId.Value,
                Name = providerId.Value,
                BaseUrl = model.BaseUrl,
                Auth = new ProviderAuth
                {
                    ApiKey = AuthHelpers.EnvironmentApiKeyAuth(
                        providerId.Value + " API key",
                        Array.Empty<string>()),
                },
                Models = new[] { model },
                Api = api,
            });
            var models = new ModelsCollection();
            models.SetProvider(provider);

            return new PiLlmTransport(
                models,
                model,
                _ => new ModelsSimpleStreamOptions
                {
                    ApiKey = options.ApiKey,
                    Fetch = options.Fetch,
                    MaxRetries = options.MaxRetries,
                    MaxRetryDelayMs = options.MaxRetryDelayMilliseconds,
                    Reasoning = options.Reasoning
                        ?? (providerName == "google" ? ThinkingLevel.Minimal : null),
                },
                options.ResponseObserver);
        }

        private static string NormalizeProvider(string provider)
        {
            if (string.IsNullOrWhiteSpace(provider))
                throw new ArgumentException("A provider id is required.", nameof(provider));
            return provider.Trim().ToLowerInvariant();
        }

        private static string ResolveBaseUrl(string provider, string? configured)
        {
            if (!string.IsNullOrWhiteSpace(configured)) return configured!.TrimEnd('/');
            switch (provider)
            {
                case "anthropic": return "https://api.anthropic.com";
                case "groq": return "https://api.groq.com/openai/v1";
                case "openai": return "https://api.openai.com/v1";
                case "openrouter": return "https://openrouter.ai/api/v1";
                case "google": return "https://generativelanguage.googleapis.com/v1beta/openai";
                case "together": return "https://api.together.xyz/v1";
                case "ollama": return "http://localhost:11434/v1";
                default:
                    throw new ArgumentException(
                        "A base URL is required for custom provider '" + provider + "'.",
                        nameof(configured));
            }
        }

        private static ModelCompatibility CreateCompatibility(string provider, string model)
        {
            if (provider == "anthropic")
            {
                return new AnthropicMessagesCompat
                {
                    SupportsTemperature = !Pinder.LlmAdapters.Anthropic.AnthropicModelIds
                        .RejectsTemperature(model),
                };
            }
            return new OpenAICompletionsCompat
            {
                SupportsStrictMode = provider == "openai",
                SupportsReasoningEffort = provider == "google",
                SupportsUsageInStreaming = true,
            };
        }
    }
}
