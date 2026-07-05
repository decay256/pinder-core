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
        private const string DefaultSystemPrompt =
            "You are a dramaturge for a comedy dating-RPG. Given two characters (their names, " +
            "psychological stakes, and bios), sketch a light dramatic arc for their conversation " +
            "as soft direction — setup, escalation, a turning point, and a possible resolution — " +
            "in 3-5 sentences of plain prose. This is flavour and direction, NOT a script: the " +
            "actual conversation is driven by a simulation and the characters' own choices, so " +
            "never dictate specific lines or outcomes, and never contradict where the interaction " +
            "actually goes. Plain prose only: no markdown, no headings, no lists, no bold/italics.";

        private const string DefaultUserTemplate =
            "Player: {playerName}\n" +
            "Psychological stake: {playerStake}\n" +
            "Bio: {playerBio}\n\n" +
            "Datee: {dateeName}\n" +
            "Psychological stake: {dateeStake}\n" +
            "Bio: {dateeBio}\n\n" +
            "Sketch a light dramatic arc for their conversation in 3-5 sentences of plain prose: " +
            "setup, escalation, a turning point, and a possible resolution. Remember: this is " +
            "soft direction only — the actual conversation will unfold based on the simulation " +
            "and the characters' choices, so do not prescribe specific dialogue or fixed outcomes.";

        private readonly ILlmTransport _transport;
        private readonly Options _options;
        private readonly PromptCatalog? _catalog;

        public LlmDramaticArcGenerator(ILlmTransport transport, Options? options = null, PromptCatalog? catalog = null)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _options = options ?? new Options();
            _catalog = catalog;
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

            string systemPrompt = DefaultSystemPrompt;
            string userTemplate = DefaultUserTemplate;

            var entry = _catalog?.TryGet("dramatic_arc");
            if (entry != null)
            {
                if (entry.SystemPrompt != null) systemPrompt = entry.SystemPrompt;
                if (entry.UserTemplate != null) userTemplate = entry.UserTemplate;
            }

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

            if (_catalog != null && entry != null && entry.SystemPrompt != null)
            {
                systemPrompt = PromptCatalog.Substitute(systemPrompt, values);
            }
            string userMessage = _catalog != null && entry != null
                ? PromptCatalog.Substitute(userTemplate, values)
                : BuildUserMessage(playerName, pStake, pBio, dateeName, dStake, dBio);

            try
            {
                string response = await _transport
                    .SendAsync(systemPrompt, userMessage, _options.Temperature, _options.MaxTokens, phase: LlmPhase.Synthesis, ct: cancellationToken)
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

        private static string BuildUserMessage(
            string playerName, string playerStake, string playerBio,
            string dateeName, string dateeStake, string dateeBio)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Player: {playerName}");
            sb.AppendLine($"Psychological stake: {(string.IsNullOrWhiteSpace(playerStake) ? "(none)" : playerStake)}");
            sb.AppendLine($"Bio: {(string.IsNullOrWhiteSpace(playerBio) ? "(none)" : playerBio)}");
            sb.AppendLine();
            sb.AppendLine($"Datee: {dateeName}");
            sb.AppendLine($"Psychological stake: {(string.IsNullOrWhiteSpace(dateeStake) ? "(none)" : dateeStake)}");
            sb.AppendLine($"Bio: {(string.IsNullOrWhiteSpace(dateeBio) ? "(none)" : dateeBio)}");
            sb.AppendLine();
            sb.AppendLine(
                "Sketch a light dramatic arc for their conversation in 3-5 sentences of plain prose: " +
                "setup, escalation, a turning point, and a possible resolution. Remember: this is " +
                "soft direction only — the actual conversation will unfold based on the simulation " +
                "and the characters' choices, so do not prescribe specific dialogue or fixed outcomes.");
            return sb.ToString();
        }

        /// <summary>Tunable knobs for <see cref="LlmDramaticArcGenerator"/>.</summary>
        public sealed class Options
        {
            /// <summary>Temperature. Default 0.85 — creative but grounded.</summary>
            public double Temperature { get; set; } = 0.85;

            /// <summary>Max output tokens. Default 300 (paragraph-sized).</summary>
            public int MaxTokens { get; set; } = 300;

            /// <summary>
            /// Opt-in callback triggered when generation is degraded (e.g. transport failure or empty output).
            /// </summary>
            public Action<SetupGenerationResult>? OnDegraded { get; set; }
        }
    }
}
