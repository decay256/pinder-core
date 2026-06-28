using System;
using System.Text;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;

namespace Pinder.Core.Conversation
{
    /// <summary>
    /// #1125 — Deterministic, NON-LLM transformation of a picked dialogue option
    /// into the final committed line, keyed off the roll outcome + DC margin.
    ///
    /// <para>
    /// This is the engine-side replacement for the old <c>delivery</c> LLM call.
    /// Previously the picked option carried only a <em>gist</em> (intended text)
    /// and a second creative LLM call ("delivery") expanded it into the sent
    /// words AND degraded/corrupted it according to the roll. Under #1125 the
    /// avatar GM already returns full, sendable candidate lines, so "delivery"
    /// collapses into this pure, deterministic commit step:
    /// </para>
    /// <list type="bullet">
    ///   <item><description>
    ///     On <see cref="FailureTier.Success"/> the picked line commits verbatim
    ///     — a success delivers what the player chose.
    ///   </description></item>
    ///   <item><description>
    ///     On any failure tier the line is degraded/corrupted in a deterministic
    ///     way whose severity scales with the tier and the miss margin. This
    ///     preserves parity with the prior delivery-LLM degradation behaviour
    ///     (a worse miss produces a more broken message) without an LLM call.
    ///   </description></item>
    /// </list>
    ///
    /// <para>
    /// Determinism is a hard contract: the same (line, tier, margin) input always
    /// produces the same output, with no RNG and no I/O. The transform is total —
    /// null/empty input returns empty, never throws.
    /// </para>
    /// </summary>
    public static class DeliveryOverlay
    {
        /// <summary>
        /// Applies the deterministic delivery overlay to a picked option's full
        /// text. Returns the committed line.
        /// </summary>
        /// <param name="pickedLine">The full sendable line the avatar GM produced and the player picked (may already carry an appended steering question).</param>
        /// <param name="tier">The roll outcome tier. <see cref="FailureTier.Success"/> commits verbatim.</param>
        /// <param name="missMargin">
        /// How far the roll missed the DC by, as a NON-NEGATIVE magnitude
        /// (i.e. <c>DC - total</c> on a failure). On success this is ignored.
        /// Negative inputs are clamped to 0.
        /// </param>
        public static string Apply(string pickedLine, FailureTier tier, int missMargin, StatType stat = StatType.Rizz)
        {
            if (string.IsNullOrEmpty(pickedLine))
                return pickedLine ?? string.Empty;

            // Success: a clean delivery commits exactly what the player picked.
            if (tier == FailureTier.Success)
                return pickedLine;

            int margin = missMargin < 0 ? 0 : missMargin;

            switch (tier)
            {
                case FailureTier.Fumble:
                    // Missed by 1–2: minor awkwardness — confidence wobble.
                    return Hesitate(pickedLine);

                case FailureTier.Misfire:
                    // Missed by 3–5: message goes sideways — stumble + wobble.
                    return Stumble(Hesitate(pickedLine));

                case FailureTier.TropeTrap:
                    // Missed by 6–9: it gets away from you — the tail is lost.
                    // Larger miss drops more of the line.
                    return TrimTail(pickedLine, keepFraction: margin >= 8 ? 0.6 : 0.75);

                case FailureTier.Catastrophe:
                    // Missed by 10+: spectacular disaster — over-hedged, stumbling full line.
                    return GetStatTaintPrefix(stat) + Flail(pickedLine);

                case FailureTier.Legendary:
                    // Nat 1: maximum humiliation — extremely flustered nervous opener, full body trailing off.
                    return GetStatTaintPrefix(stat) + Panic(pickedLine);

                default:
                    return pickedLine;
            }
        }

        /// <summary>Confidence wobble: lowercase the opening and trail off.</summary>
        private static string Hesitate(string line)
        {
            string trimmed = line.TrimEnd();
            string body = LowerFirst(trimmed.TrimEnd('.', '!', '?', '…'));
            return body + "...";
        }

        /// <summary>Stumble: inject a verbal stumble after the first word.</summary>
        private static string Stumble(string line)
        {
            int firstSpace = line.IndexOf(' ');
            if (firstSpace < 0)
                return "uh, " + line;
            return line.Substring(0, firstSpace + 1) + "uh, " + line.Substring(firstSpace + 1);
        }

        /// <summary>Drop the tail of the message, keeping a leading fraction of words.</summary>
        private static string TrimTail(string line, double keepFraction)
        {
            var words = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length <= 1)
                return LowerFirst(line.TrimEnd('.', '!', '?', '…')) + "...";

            int keep = (int)Math.Floor(words.Length * keepFraction);
            if (keep < 1) keep = 1;
            if (keep >= words.Length) keep = words.Length - 1;

            var sb = new StringBuilder();
            for (int i = 0; i < keep; i++)
            {
                if (i > 0) sb.Append(' ');
                sb.Append(words[i]);
            }
            // Strip any closing punctuation off the surviving fragment, then trail off.
            string kept = sb.ToString().TrimEnd('.', '!', '?', '…', ',');
            return kept + "...";
        }

        /// <summary>Over-hedged, stumbling version of the full line.</summary>
        private static string Flail(string line)
        {
            return "well, i mean, " + Stumble(Hesitate(line));
        }

        /// <summary>Extremely flustered nervous opener with a stumble.</summary>
        private static string Panic(string line)
        {
            return "oh god, um, " + Stumble(Hesitate(line));
        }

        private static string LowerFirst(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            if (!char.IsUpper(s[0])) return s;
            return char.ToLowerInvariant(s[0]) + s.Substring(1);
        }

        private static string GetStatTaintPrefix(StatType stat)
        {
            switch (stat)
            {
                case StatType.Rizz:
                    return "*I'm ruining this...* ";
                case StatType.SelfAwareness:
                case StatType.Wit:
                    return "(I know how this sounds...) ";
                case StatType.Charm:
                    return "*It has to be perfect...* ";
                case StatType.Chaos:
                    return "*Let it burn...* ";
                case StatType.Honesty:
                    return "*I'm filled with dread...* ";
                default:
                    return "";
            }
        }
    }
}
