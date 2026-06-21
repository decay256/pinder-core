using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Pinder.Core.Interfaces;

namespace Pinder.SessionSetup
{
    /// <summary>
    /// Default <see cref="IDramaticArcGenerator"/> built on
    /// <see cref="ILlmTransport"/>. One LLM call per session.
    /// Issue #821.
    /// </summary>
    /// <remarks>
    /// Uses the canonical <see cref="LlmPhase.DramaticArc"/> phase
    /// label so snapshot recording and audit decorators tag the exchange
    /// without re-deriving the phase from prompt text.
    /// </remarks>
    public sealed class LlmDramaticArcGenerator : IDramaticArcGenerator
    {
        private const string SystemPrompt =
            "You are a dramaturge for a comedy dating-RPG. Given two characters (their names, " +
            "psychological stakes, and bios), sketch a light dramatic arc for their conversation " +
            "as soft direction — setup, escalation, a turning point, and a possible resolution — " +
            "in 3-5 sentences of plain prose. This is flavour and direction, NOT a script: the " +
            "actual conversation is driven by a simulation and the characters' own choices, so " +
            "never dictate specific lines or outcomes, and never contradict where the interaction " +
            "actually goes. Plain prose only: no markdown, no headings, no lists, no bold/italics.";

        private readonly ILlmTransport _transport;
        private readonly Options _options;

        public LlmDramaticArcGenerator(ILlmTransport transport, Options? options = null)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _options = options ?? new Options();
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

            string userMessage = BuildUserMessage(
                playerName, playerStake ?? string.Empty, playerBio ?? string.Empty,
                dateeName, dateeStake ?? string.Empty, dateeBio ?? string.Empty);

            try
            {
                string response = await _transport
                    .SendAsync(SystemPrompt, userMessage, _options.Temperature, _options.MaxTokens, phase: LlmPhase.DramaticArc)
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
                // Do not fire OnDegraded on cancellation; preserve existing behavior of returning empty string.
                return string.Empty;
            }
            catch
            {
                _options.OnDegraded?.Invoke(SetupGenerationResult.DegradedFailure("dramatic_arc", "transport_error"));
                // Parity with IStakeGenerator and IOutfitDescriber: transport failure → empty string.
                // The caller decides what to do (skip arc, or fail setup).
                return string.Empty;
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
