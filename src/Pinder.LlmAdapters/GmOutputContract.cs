using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using Pinder.Core.Conversation;
using Pinder.Core.Stats;
using Pinder.LlmAdapters.Anthropic;

namespace Pinder.LlmAdapters
{
    /// <summary>
    /// #1124: THE single canonical text output-format contract the Game Master
    /// must emit, reused by BOTH sessions (player-avatar delivery and datee
    /// response). pinder-core parses this format deterministically.
    ///
    /// Canonical shape:
    ///
    ///   &lt;message text — one or more lines&gt;
    ///   [SIGNALS]
    ///   TELL: &lt;Stat&gt; (&lt;description&gt;)
    ///   WEAKNESS: &lt;Stat&gt; -&lt;n&gt; (&lt;description&gt;)
    ///
    /// Rules:
    ///   • The message text is everything before the optional <c>[SIGNALS]</c>
    ///     marker. It is always present (may be empty on malformed input).
    ///   • The <c>[SIGNALS]</c> block is OPTIONAL. When present it may carry a
    ///     <c>TELL:</c> line and/or a <c>WEAKNESS:</c> line, in any order.
    ///   • A <c>TELL:</c> names the revealed stat and a parenthetical description.
    ///   • A <c>WEAKNESS:</c> names the defending stat, a <c>-n</c> DC reduction,
    ///     and a parenthetical description.
    ///
    /// <see cref="Emit"/> renders a <see cref="GmTurnOutput"/> to this format and
    /// <see cref="Parse"/> reads it back. The pair round-trips:
    /// <c>Parse(Emit(x)).Equals(x)</c> for any well-formed value. Parsing
    /// degrades gracefully — malformed or partial input never throws; unknown
    /// stats / missing fields collapse to a message-only result.
    /// </summary>
    public static class GmOutputContract
    {
        /// <summary>Marker that opens the optional structured signals block.</summary>
        public const string SignalsMarker = "[SIGNALS]";

        private static readonly Regex TellSignalRegex = new Regex(
            @"TELL:\s*(\w+)\s*\(([^)]+)\)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex WeaknessSignalRegex = new Regex(
            @"WEAKNESS:\s*(\w+)\s*-(\d+)\s*\(([^)]+)\)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>
        /// Renders a GM turn output to the canonical text format. The result is
        /// parseable back into an equal value by <see cref="Parse"/>.
        /// </summary>
        public static string Emit(GmTurnOutput output)
        {
            if (output == null) throw new ArgumentNullException(nameof(output));

            var sb = new StringBuilder();
            sb.Append(output.Message ?? string.Empty);

            var tell = output.Tell;
            var weakness = output.Weakness;
            if (tell != null || weakness != null)
            {
                sb.Append('\n').Append(SignalsMarker);
                if (tell != null)
                {
                    sb.Append('\n')
                      .Append("TELL: ")
                      .Append(StatName(tell.Stat))
                      .Append(" (")
                      .Append(tell.Description)
                      .Append(')');
                }
                if (weakness != null)
                {
                    sb.Append('\n')
                      .Append("WEAKNESS: ")
                      .Append(StatName(weakness.DefendingStat))
                      .Append(" -")
                      .Append(weakness.DcReduction.ToString())
                      .Append(" (")
                      .Append(output.WeaknessDescription ?? string.Empty)
                      .Append(')');
                }
            }

            return sb.ToString();
        }

        /// <summary>
        /// Parses canonical GM output text into a <see cref="GmTurnOutput"/>.
        /// Never throws — malformed input degrades to a message-only result with
        /// null signals. The message is everything before <c>[SIGNALS]</c>.
        /// </summary>
        public static GmTurnOutput Parse(string? raw)
        {
            if (string.IsNullOrEmpty(raw))
                return new GmTurnOutput(string.Empty);

            string message;
            Tell? tell = null;
            WeaknessWindow? weakness = null;
            string? weaknessDescription = null;

            try
            {
                int signalsIdx = raw!.IndexOf(SignalsMarker, StringComparison.OrdinalIgnoreCase);
                message = signalsIdx >= 0 ? raw.Substring(0, signalsIdx) : raw;
                message = message.TrimEnd('\n', '\r', ' ', '\t');

                if (signalsIdx >= 0)
                {
                    string block = raw.Substring(signalsIdx);

                    var tellMatch = TellSignalRegex.Match(block);
                    if (tellMatch.Success)
                    {
                        if (TryParseStat(tellMatch.Groups[1].Value, out var stat))
                        {
                            tell = new Tell(stat, tellMatch.Groups[2].Value.Trim());
                        }
                    }

                    var weakMatch = WeaknessSignalRegex.Match(block);
                    if (weakMatch.Success)
                    {
                        if (TryParseStat(weakMatch.Groups[1].Value, out var stat) &&
                            int.TryParse(weakMatch.Groups[2].Value.Trim(), out int reduction) &&
                            reduction > 0)
                        {
                            weakness = new WeaknessWindow(stat, reduction);
                            weaknessDescription = weakMatch.Groups[3].Value.Trim();
                        }
                    }
                }
            }
            catch
            {
                // Defensive: any unexpected failure collapses to message-only.
                return new GmTurnOutput(raw!.Trim());
            }

            return new GmTurnOutput(message, tell, weakness, weaknessDescription);
        }

        private static bool TryParseStat(string raw, out StatType stat)
        {
            var normalized = StatNameNormalizer.NormalizeStatName(raw.Trim());
            try
            {
                stat = (StatType)Enum.Parse(typeof(StatType), normalized, true);
                return true;
            }
            catch (ArgumentException)
            {
                stat = default;
                return false;
            }
        }

        private static string StatName(StatType stat)
        {
            // Canonical wire spelling: enum name, with SelfAwareness expanded to
            // the SELF_AWARENESS token the model emits and the normalizer accepts.
            return stat == StatType.SelfAwareness ? "SELF_AWARENESS" : stat.ToString();
        }
    }
}
