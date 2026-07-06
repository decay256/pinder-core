using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Pinder.Core.Interfaces;
using Pinder.LlmAdapters;

namespace Pinder.SessionSetup
{
    /// <summary>
    /// Default <see cref="IOutfitDescriber"/> built on
    /// <see cref="ILlmTransport"/>. One LLM call per session.
    /// Issue #333.
    /// </summary>
    /// <remarks>
    /// Uses the canonical <see cref="LlmPhase.OutfitDescription"/> phase
    /// label so snapshot recording and audit decorators tag the exchange
    /// without re-deriving the phase from prompt text.
    /// </remarks>
    public sealed class LlmOutfitDescriber : IOutfitDescriber
    {
        private readonly ILlmTransport _transport;
        private readonly Options _options;
        private readonly PromptCatalog _catalog;

        public LlmOutfitDescriber(ILlmTransport transport, Options? options = null, PromptCatalog? catalog = null)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _options = options ?? new Options();
            _catalog = catalog ?? PromptTemplates.Catalog
                ?? throw new InvalidOperationException("PromptTemplates.Catalog is not wired. Call PromptWiring.Wire() at startup.");

            // Enforce that the catalog contains the required key
            var entry = _catalog.TryGet("outfit")
                ?? throw new InvalidOperationException("prompt-catalog: missing required key 'outfit'.");
            if (string.IsNullOrWhiteSpace(entry.SystemPrompt))
                throw new InvalidOperationException("prompt-catalog: key 'outfit' has no system_prompt. Check the yaml file.");
            if (string.IsNullOrWhiteSpace(entry.UserTemplate))
                throw new InvalidOperationException("prompt-catalog: key 'outfit' has no user_template. Check the yaml file.");

            if (!entry.Temperature.HasValue)
                throw new InvalidOperationException("prompt-catalog: key 'outfit' has no temperature. Check the yaml file.");
            if (!entry.MaxTokens.HasValue)
                throw new InvalidOperationException("prompt-catalog: key 'outfit' has no max_tokens. Check the yaml file.");
        }

        /// <summary>
        /// Generates the outfit scene setting asynchronously.
        /// This generation is OPTIONAL/degradable; if a transport failure or empty output occurs,
        /// it will trigger the <see cref="Options.OnDegraded"/> callback and return <see cref="string.Empty"/>.
        /// </summary>
        public async Task<string> GenerateAsync(
            string playerName,
            IReadOnlyList<string> playerItems,
            string dateeName,
            IReadOnlyList<string> dateeItems,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(playerName))
                throw new ArgumentException("playerName must not be null or whitespace.", nameof(playerName));
            if (string.IsNullOrWhiteSpace(dateeName))
                throw new ArgumentException("dateeName must not be null or whitespace.", nameof(dateeName));
            if (playerItems == null) throw new ArgumentNullException(nameof(playerItems));
            if (dateeItems == null) throw new ArgumentNullException(nameof(dateeItems));

            var entry = _catalog.Get("outfit");
            string systemPrompt = entry.SystemPrompt!;
            string userTemplate = entry.UserTemplate!;

            var playerItemsSb = new StringBuilder();
            if (playerItems.Count == 0) playerItemsSb.AppendLine("- (no items recorded)");
            else
                foreach (var i in playerItems)
                    playerItemsSb.AppendLine($"- {i}");

            var dateeItemsSb = new StringBuilder();
            if (dateeItems.Count == 0) dateeItemsSb.AppendLine("- (no items recorded)");
            else
                foreach (var i in dateeItems)
                    dateeItemsSb.AppendLine($"- {i}");

            var values = new Dictionary<string, string>
            {
                { "playerName", playerName },
                { "playerItems", playerItemsSb.ToString().TrimEnd() },
                { "dateeName", dateeName },
                { "dateeItems", dateeItemsSb.ToString().TrimEnd() }
            };

            systemPrompt = PromptCatalog.Substitute(systemPrompt, values);
            string userMessage = PromptCatalog.Substitute(userTemplate, values);

            try
            {
                double temp = _options.Temperature != GeneratorDefaultConfigs.Outfit.Temperature
                    ? _options.Temperature
                    : entry.Temperature!.Value;
                int maxTok = _options.MaxTokens != GeneratorDefaultConfigs.Outfit.MaxTokens
                    ? _options.MaxTokens
                    : entry.MaxTokens!.Value;

                string response = await _transport
                    .SendAsync(systemPrompt, userMessage, temp, maxTok, phase: LlmPhase.OutfitDescription)
                    .ConfigureAwait(false);
                string trimmed = (response ?? string.Empty).Trim();
                if (string.IsNullOrEmpty(trimmed))
                {
                    _options.OnDegraded?.Invoke(SetupGenerationResult.DegradedFailure("outfit", "empty_output"));
                }
                return trimmed;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                if (_options.OnDegraded != null)
                {
                    _options.OnDegraded.Invoke(SetupGenerationResult.DegradedFailure("outfit", "transport_error"));
                    return string.Empty;
                }
                throw;
            }
        }

        /// <summary>Tunable knobs for <see cref="LlmOutfitDescriber"/>.</summary>
        public sealed class Options
        {
            /// <summary>Temperature. Default 0.8 — a touch warmer than stake generation.</summary>
            public double Temperature { get; set; } = GeneratorDefaultConfigs.Outfit.Temperature;

            /// <summary>Max output tokens. Default 250 (paragraph-sized).</summary>
            public int MaxTokens { get; set; } = GeneratorDefaultConfigs.Outfit.MaxTokens;

            /// <summary>
            /// Opt-in callback triggered when generation is degraded (e.g. transport failure or empty output).
            /// </summary>
            public Action<SetupGenerationResult>? OnDegraded { get; set; }
        }
    }
}
