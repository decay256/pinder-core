using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Pinder.Core.Interfaces;

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
        private const string SystemPrompt =
            "You are setting the visual scene for a comedy dating-RPG conversation. " +
            "Given the items both characters are wearing, write a single brief paragraph " +
            "describing what each is wearing and the visual / aesthetic vibe of the encounter. " +
            "Keep it grounded and concrete. " +
            "Respond in plain prose only. Do NOT use markdown formatting of any kind: no " +
            "headings, no bold or italics, no bullet or numbered lists, no blockquotes, no " +
            "inline or fenced code. Two to four sentences total \u2014 not a list, not a fashion review.";

        private readonly ILlmTransport _transport;
        private readonly Options _options;

        public LlmOutfitDescriber(ILlmTransport transport, Options? options = null)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _options = options ?? new Options();
        }

        public async Task<string> GenerateAsync(
            string playerName,
            IReadOnlyList<string> playerItems,
            string opponentName,
            IReadOnlyList<string> opponentItems,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(playerName))
                throw new ArgumentException("playerName must not be null or whitespace.", nameof(playerName));
            if (string.IsNullOrWhiteSpace(opponentName))
                throw new ArgumentException("opponentName must not be null or whitespace.", nameof(opponentName));
            if (playerItems == null) throw new ArgumentNullException(nameof(playerItems));
            if (opponentItems == null) throw new ArgumentNullException(nameof(opponentItems));

            string userMessage = BuildUserMessage(playerName, playerItems, opponentName, opponentItems);

            try
            {
                string response = await _transport
                    .SendAsync(SystemPrompt, userMessage, _options.Temperature, _options.MaxTokens, phase: LlmPhase.OutfitDescription)
                    .ConfigureAwait(false);
                return (response ?? string.Empty).Trim();
            }
            catch
            {
                // Parity with IStakeGenerator: transport failure \u2192 empty string.
                // The caller decides what to do (skip scene entry, or fail setup).
                return string.Empty;
            }
        }

        private static string BuildUserMessage(
            string playerName, IReadOnlyList<string> playerItems,
            string opponentName, IReadOnlyList<string> opponentItems)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Player ({playerName}) is wearing:");
            if (playerItems.Count == 0) sb.AppendLine("- (no items recorded)");
            else
                foreach (var i in playerItems)
                    sb.AppendLine($"- {i}");
            sb.AppendLine();
            sb.AppendLine($"Opponent ({opponentName}) is wearing:");
            if (opponentItems.Count == 0) sb.AppendLine("- (no items recorded)");
            else
                foreach (var i in opponentItems)
                    sb.AppendLine($"- {i}");
            sb.AppendLine();
            sb.AppendLine(
                "Write 2\u20134 sentences in plain prose: what each is wearing and the overall vibe of the encounter. " +
                "Do not list items \u2014 weave them into the description. Mention both characters by name.");
            return sb.ToString();
        }

        /// <summary>Tunable knobs for <see cref="LlmOutfitDescriber"/>.</summary>
        public sealed class Options
        {
            /// <summary>Temperature. Default 0.8 \u2014 a touch warmer than matchup analysis.</summary>
            public double Temperature { get; set; } = 0.8;

            /// <summary>Max output tokens. Default 250 (paragraph-sized).</summary>
            public int MaxTokens { get; set; } = 250;
        }
    }
}
