using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Pinder.Core.Characters;
using Pinder.Core.Interfaces;
using Pinder.Core.Stats;

namespace Pinder.SessionSetup
{
    /// <summary>
    /// Default <see cref="IMatchupAnalyzer"/> built on <see cref="ILlmTransport"/>.
    /// Provider-agnostic: pass any transport (Anthropic, OpenAi, Groq, etc.).
    /// </summary>
    /// <remarks>
    /// Behaviour mirrors the previous static <c>MatchupAnalyzer</c> in
    /// <c>session-runner</c>: builds the same prompt, same 500-token budget,
    /// same 0.7 temperature. Caching is opt-in via <see cref="Options.CacheDirectory"/>.
    ///
    /// Output contract (issue pinder-web #136): the returned analysis is
    /// plain prose with paragraph breaks only — no markdown headings, bold,
    /// italics, bullet or numbered lists, blockquotes, or code fences. The
    /// system + user prompts forbid markdown explicitly. <c>Pinder.GameApi</c>
    /// additionally runs a <c>MarkdownSanitizer</c> as defence-in-depth before
    /// storing the result.
    /// </remarks>
    public sealed class LlmMatchupAnalyzer : IMatchupAnalyzer
    {
        private readonly ILlmTransport _transport;
        private readonly Options _options;

        public LlmMatchupAnalyzer(ILlmTransport transport, Options? options = null)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _options = options ?? new Options();
        }

        public async Task<string?> AnalyzeMatchupAsync(
            CharacterProfile player,
            CharacterProfile opponent,
            CancellationToken cancellationToken = default)
        {
            if (player == null) throw new ArgumentNullException(nameof(player));
            if (opponent == null) throw new ArgumentNullException(nameof(opponent));

            // Optional file cache — keeps parity with the legacy session-runner helper.
            string? cacheFile = null;
            if (!string.IsNullOrWhiteSpace(_options.CacheDirectory))
            {
                Directory.CreateDirectory(_options.CacheDirectory!);
                string cacheKey =
                    $"{player.DisplayName}_vs_{opponent.DisplayName}_{GetStatsHash(player, opponent)}";
                cacheFile = Path.Combine(_options.CacheDirectory!, cacheKey + ".md");
                if (File.Exists(cacheFile))
                {
                    try
                    {
#if NETSTANDARD2_0
                        return File.ReadAllText(cacheFile);
#else
                        return await File.ReadAllTextAsync(cacheFile, cancellationToken).ConfigureAwait(false);
#endif
                    }
                    catch
                    {
                        // fall through to regeneration
                    }
                }
            }

            // Output is rendered as plain text by the frontend; markdown markers
            // would leak through as literal characters. Forbid them at the
            // system prompt level (pinder-web #136). MarkdownSanitizer in
            // Pinder.GameApi.Services is the backstop.
            string systemPrompt =
                "You are an expert game designer analyzing a matchup in a dating RPG. " +
                "Respond in plain prose only. Do NOT use markdown formatting of any " +
                "kind: no headings (#, ##), no bold or italics (**, __, *, _), no " +
                "bullet or numbered lists (-, *, +, 1., 2.), no blockquotes (>), and " +
                "no inline or fenced code (`, ```). Use paragraph breaks for structure.";
            string userPrompt = BuildPrompt(player, opponent);

            try
            {
                string analysis = await _transport
                    .SendAsync(systemPrompt, userPrompt, _options.Temperature, _options.MaxTokens)
                    .ConfigureAwait(false);

                analysis = (analysis ?? string.Empty).Trim();

                if (!string.IsNullOrEmpty(cacheFile))
                {
                    try
                    {
#if NETSTANDARD2_0
                        File.WriteAllText(cacheFile, analysis);
#else
                        await File.WriteAllTextAsync(cacheFile, analysis, cancellationToken).ConfigureAwait(false);
#endif
                    }
                    catch
                    {
                        // cache write failure is non-fatal
                    }
                }

                return analysis;
            }
            catch
            {
                // Parity with legacy helper: any transport error → null, not an exception.
                return null;
            }
        }

        // ── prompt building ──────────────────────────────────────────────

        private static string BuildPrompt(CharacterProfile player, CharacterProfile opponent)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Analyze the following matchup between two characters in a dating RPG.");
            sb.AppendLine("Produce a brief 3-paragraph plain-prose output. The format must be:");
            sb.AppendLine();
            sb.AppendLine("Paragraph 1 — begin with the player's name followed by their level and archetype list in parentheses, then 3-4 sentences on their strongest lane, percent chance, and shadow risks.");
            sb.AppendLine();
            sb.AppendLine("Paragraph 2 — begin with the opponent's name followed by their level and archetype list in parentheses, then 3-4 sentences on their best defence, shadow effects, and vulnerabilities.");
            sb.AppendLine();
            sb.AppendLine("Paragraph 3 — begin with 'Prediction:' (followed by a space) and then 2-3 sentences predicting how the match will play out based on stats and shadows.");
            sb.AppendLine();
            sb.AppendLine("IMPORTANT: write plain prose only. Do not use markdown. Do not bold names with **. Do not use headings, bullets, numbered lists, blockquotes, or code formatting.");
            sb.AppendLine();
            sb.AppendLine("Here is the data:");
            sb.AppendLine();
            AppendCharacterData(sb, "Player", player);
            sb.AppendLine();
            AppendCharacterData(sb, "Opponent", opponent);

            sb.AppendLine();
            sb.AppendLine("DC Reference (Player attacking, Opponent defending):");
            sb.AppendLine("Stat | Player Mod | Opponent Defends | DC | Success %");
            foreach (var stat in new[] { StatType.Charm, StatType.Rizz, StatType.Honesty, StatType.Chaos, StatType.Wit, StatType.SelfAwareness })
            {
                int atkMod = player.Stats.GetEffective(stat);
                int dc = opponent.Stats.GetDefenceDC(stat);
                int need = dc - atkMod;
                int pct = Math.Max(0, Math.Min(100, (21 - need) * 5));
                sb.AppendLine(
                    $"{stat} | {atkMod:+#;-#;0} | {StatBlock.DefenceTable[stat]} ({opponent.Stats.GetEffective(StatBlock.DefenceTable[stat]):+#;-#;0}) | {dc} | {pct}%");
            }

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

        private static string GetStatsHash(CharacterProfile player, CharacterProfile opponent)
        {
            var sb = new StringBuilder();
            AppendCharacterData(sb, "P", player);
            AppendCharacterData(sb, "O", opponent);

            using (var sha = SHA256.Create())
            {
                byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
                var hex = new StringBuilder(bytes.Length * 2);
                for (int i = 0; i < bytes.Length; i++)
                    hex.AppendFormat("{0:x2}", bytes[i]);
                return hex.ToString().Substring(0, 8);
            }
        }

        /// <summary>Tunable knobs for <see cref="LlmMatchupAnalyzer"/>.</summary>
        public sealed class Options
        {
            /// <summary>Temperature for the analysis generation. Default 0.7.</summary>
            public double Temperature { get; set; } = 0.7;

            /// <summary>Max output tokens. Default 500.</summary>
            public int MaxTokens { get; set; } = 500;

            /// <summary>
            /// Optional directory for caching analyses by (player+opponent+stats) hash.
            /// When null or empty, caching is disabled.
            /// </summary>
            public string? CacheDirectory { get; set; }
        }
    }
}
