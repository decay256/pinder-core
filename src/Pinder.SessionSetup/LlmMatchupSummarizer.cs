using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Pinder.Core.Characters;
using Pinder.Core.Interfaces;
using Pinder.Core.Stats;

namespace Pinder.SessionSetup
{
    /// <summary>
    /// Default <see cref="IMatchupSummarizer"/> built on
    /// <see cref="ILlmTransport"/>. One LLM call per session.
    /// Issue #332.
    /// </summary>
    /// <remarks>
    /// Uses the canonical <see cref="LlmPhase.MatchupSummary"/> phase label
    /// so snapshot recording and audit decorators tag the exchange without
    /// re-deriving the phase from prompt text.
    ///
    /// The summary is intentionally produced by a SECOND LLM call rather
    /// than extending the existing matchup-analysis prompt. The streaming
    /// matchup-analysis path (see <see cref="LlmMatchupAnalyzer"/>) feeds
    /// raw token deltas directly into the UI; appending a summary section
    /// to that stream would leak the new affordance into the main matchup
    /// display. A separate, short, non-streaming call keeps the two
    /// concerns cleanly separated and runs in parallel with stake
    /// generation in the web tier so it adds minimal end-to-end latency.
    /// </remarks>
    public sealed class LlmMatchupSummarizer : IMatchupSummarizer
    {
        private const string SystemPrompt =
            "You are an expert game designer summarising a matchup in a dating RPG. " +
            "Write a SHORT 1-2 paragraph summary (roughly 60-120 words total) of the " +
            "matchup between the two characters. Capture the essential dynamic — who is " +
            "trying what, and what is at stake — in plain prose. Do NOT use markdown " +
            "formatting of any kind: no headings (#, ##), no bold or italics (**, __, *, _), " +
            "no bullet or numbered lists (-, *, +, 1., 2.), no blockquotes (>), and no " +
            "inline or fenced code (`, ```). Use paragraph breaks for structure if you " +
            "use two paragraphs.";

        private readonly ILlmTransport _transport;
        private readonly Options _options;

        public LlmMatchupSummarizer(ILlmTransport transport, Options? options = null)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _options = options ?? new Options();
        }

        public async Task<string> SummarizeAsync(
            CharacterProfile player,
            CharacterProfile opponent,
            CancellationToken cancellationToken = default)
        {
            if (player == null) throw new ArgumentNullException(nameof(player));
            if (opponent == null) throw new ArgumentNullException(nameof(opponent));

            string userMessage = BuildPrompt(player, opponent);

            try
            {
                string response = await _transport
                    .SendAsync(SystemPrompt, userMessage, _options.Temperature, _options.MaxTokens, phase: LlmPhase.MatchupSummary)
                    .ConfigureAwait(false);
                return (response ?? string.Empty).Trim();
            }
            catch
            {
                // Parity with IOutfitDescriber: transport failure → empty string.
                // The caller (web tier) tolerates an empty string by simply
                // not surfacing the summary section in the UI.
                return string.Empty;
            }
        }

        private static string BuildPrompt(CharacterProfile player, CharacterProfile opponent)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Summarise the following matchup between two characters in a dating RPG.");
            sb.AppendLine("Produce 1 to 2 short paragraphs in plain prose (no markdown).");
            sb.AppendLine("Mention each character by name. Capture the essential dynamic and the");
            sb.AppendLine("strongest lane of attack vs. defence — but keep it tight (60-120 words total).");
            sb.AppendLine();
            sb.AppendLine("Here is the data:");
            sb.AppendLine();
            AppendCharacterData(sb, "Player", player);
            sb.AppendLine();
            AppendCharacterData(sb, "Opponent", opponent);
            return sb.ToString();
        }

        private static void AppendCharacterData(StringBuilder sb, string label, CharacterProfile character)
        {
            sb.AppendLine($"--- {label}: {character.DisplayName} ---");
            sb.AppendLine($"Level: {character.Level}");
            sb.AppendLine($"Bio: {character.Bio}");

            sb.AppendLine("Stats:");
            foreach (var stat in new[] { StatType.Charm, StatType.Rizz, StatType.Honesty, StatType.Chaos, StatType.Wit, StatType.SelfAwareness })
            {
                sb.AppendLine($"- {stat}: {character.Stats.GetEffective(stat):+#;-#;0}");
            }

            sb.AppendLine("Shadows:");
            foreach (var shadow in new[] { ShadowStatType.Dread, ShadowStatType.Fixation, ShadowStatType.Denial, ShadowStatType.Madness })
            {
                sb.AppendLine($"- {shadow}: {character.Stats.GetShadow(shadow)}");
            }
        }

        /// <summary>Tunable knobs for <see cref="LlmMatchupSummarizer"/>.</summary>
        public sealed class Options
        {
            /// <summary>Temperature. Default 0.7 — same as matchup analysis.</summary>
            public double Temperature { get; set; } = 0.7;

            /// <summary>
            /// Max output tokens. Default 200 — enough for 1-2 short paragraphs
            /// with headroom; the prompt aims for 60-120 words (~80-160 tokens).
            /// </summary>
            public int MaxTokens { get; set; } = 200;
        }
    }
}
