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
    /// Default <see cref="IDramaticArcGenerator"/> built on
    /// <see cref="ILlmTransport"/>. One LLM call per session.
    /// Issue #821.
    /// </summary>
    /// <remarks>
    /// Uses the canonical <see cref="LlmPhase.Synthesis"/> phase
    /// label so snapshot recording and audit decorators tag the exchange
    /// without re-deriving the phase from prompt text.
    /// </remarks>
    public sealed class LlmDramaticArcGenerator : IDramaticArcGenerator
    {
        private readonly ILlmTransport _transport;
        private readonly Options _options;
        private readonly PromptCatalog _catalog;

        public LlmDramaticArcGenerator(ILlmTransport transport, Options? options = null, PromptCatalog? catalog = null)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _options = options ?? new Options();
            _catalog = catalog ?? PromptTemplates.Catalog
                ?? throw new InvalidOperationException("PromptTemplates.Catalog is not wired. Call PromptWiring.Wire() at startup.");

            // Enforce that the catalog contains the required key and parameters
            var entry = _catalog.TryGet("dramatic_arc")
                ?? throw new InvalidOperationException("prompt-catalog: missing required key 'dramatic_arc'.");
            if (string.IsNullOrWhiteSpace(entry.SystemPrompt))
                throw new InvalidOperationException("prompt-catalog: key 'dramatic_arc' has no system_prompt. Check the yaml file.");
            if (string.IsNullOrWhiteSpace(entry.UserTemplate))
                throw new InvalidOperationException("prompt-catalog: key 'dramatic_arc' has no user_template. Check the yaml file.");

            if (!entry.Temperature.HasValue)
                throw new InvalidOperationException("prompt-catalog: key 'dramatic_arc' has no temperature. Check the yaml file.");
            if (!entry.MaxTokens.HasValue)
                throw new InvalidOperationException("prompt-catalog: key 'dramatic_arc' has no max_tokens. Check the yaml file.");
        }

        /// <summary>
        /// Generates a light dramatic arc asynchronously.
        /// This generation is OPTIONAL/degradable; if a transport failure or empty output occurs,
        /// it will trigger the <see cref="Options.OnDegraded"/> callback and return <see cref="string.Empty"/>.
        /// </summary>
        public async Task<string> GenerateAsync(
            string playerName,
            string playerStake,
            string playerBio,
            string dateeName,
            string dateeStake,
            string dateeBio,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(playerName))
                throw new ArgumentException("playerName must not be null or whitespace.", nameof(playerName));
            if (string.IsNullOrWhiteSpace(dateeName))
                throw new ArgumentException("dateeName must not be null or whitespace.", nameof(dateeName));
            // Stakes and bios are allowed to be empty/whitespace (character might not have them yet)

            var entry = _catalog.Get("dramatic_arc");
            string systemPrompt = entry.SystemPrompt!;
            string userTemplate = entry.UserTemplate!;

            string pStake = string.IsNullOrWhiteSpace(playerStake) ? "(none)" : playerStake;
            string pBio = string.IsNullOrWhiteSpace(playerBio) ? "(none)" : playerBio;
            string dStake = string.IsNullOrWhiteSpace(dateeStake) ? "(none)" : dateeStake;
            string dBio = string.IsNullOrWhiteSpace(dateeBio) ? "(none)" : dateeBio;

            var values = new Dictionary<string, string>
            {
                { "playerName", playerName },
                { "playerStake", pStake },
                { "playerBio", pBio },
                { "dateeName", dateeName },
                { "dateeStake", dStake },
                { "dateeBio", dBio }
            };

            systemPrompt = PromptCatalog.Substitute(systemPrompt, values);
            string userMessage = PromptCatalog.Substitute(userTemplate, values);

            try
            {
                double temp = _options.Temperature != GeneratorDefaultConfigs.DramaticArc.Temperature
                    ? _options.Temperature
                    : entry.Temperature!.Value;
                int maxTok = _options.MaxTokens != GeneratorDefaultConfigs.DramaticArc.MaxTokens
                    ? _options.MaxTokens
                    : entry.MaxTokens!.Value;

                string response = await _transport
                    .SendAsync(systemPrompt, userMessage, temp, maxTok, phase: LlmPhase.Synthesis, ct: cancellationToken)
                    .ConfigureAwait(false);
                string trimmed = (response ?? string.Empty).Trim();
                if (string.IsNullOrEmpty(trimmed))
                {
                    _options.OnDegraded?.Invoke(SetupGenerationResult.DegradedFailure("dramatic_arc", "empty_output"));
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
                    _options.OnDegraded.Invoke(SetupGenerationResult.DegradedFailure("dramatic_arc", "transport_error"));
                    return string.Empty;
                }
                throw;
            }
        }

        /// <summary>Tunable knobs for <see cref="LlmDramaticArcGenerator"/>.</summary>
        public sealed class Options
        {
            /// <summary>Temperature. Default 0.85 — creative but grounded.</summary>
            public double Temperature { get; set; } = GeneratorDefaultConfigs.DramaticArc.Temperature;

            /// <summary>Max tokens for dramatic arc generation.</summary>
            public int MaxTokens { get; set; } = GeneratorDefaultConfigs.DramaticArc.MaxTokens;

            /// <summary>
            /// Opt-in callback triggered when generation is degraded (e.g. transport failure or empty output).
            /// </summary>
            public Action<SetupGenerationResult>? OnDegraded { get; set; }
        }
    }
}
