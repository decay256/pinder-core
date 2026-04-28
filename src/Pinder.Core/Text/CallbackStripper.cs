using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Pinder.Core.Text
{
    /// <summary>
    /// Issue #339: post-process pass that strips same-turn "callback"
    /// phrases — short references the LLM emits to a previous turn or
    /// thought within the same conversation, e.g. "As you just said,",
    /// "Like we mentioned,", "As we discussed,". These read as filler at
    /// the rendering layer; the engine strips them after the LLM call so
    /// the player never sees them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The pattern set is intentionally conservative — only phrases with
    /// strong same-turn-callback semantics, not legitimate cross-turn
    /// references. In particular, anything that names a turn explicitly
    /// (<c>"Earlier, on turn 2..."</c>) or references a specific named
    /// topic (<c>"As we said about the IKEA..."</c>) is left alone.
    /// </para>
    /// <para>
    /// Output contract: returns the stripped text. If at least one
    /// pattern matched, the returned text differs from the input;
    /// otherwise the input is returned unchanged. Whitespace adjacent to
    /// the stripped span is normalised so we don't leave double spaces
    /// or a leading lowercase letter after a stripped sentence opener.
    /// </para>
    /// </remarks>
    public static class CallbackStripper
    {
        /// <summary>
        /// Layer name emitted on the <see cref="TextDiff"/> when the
        /// strip pass actually changed the message. Stable string —
        /// snapshot/replay tooling can match against it.
        /// </summary>
        public const string LayerName = "Callback Strip";

        // Patterns target the typical opening-phrase shape: optional
        // "and"/"so" lead-in, the callback verb, optional "just", and a
        // trailing comma. Anchored to start-of-line OR start-of-sentence
        // (after `. `, `! `, `? `) so we only strip callback OPENERS, not
        // mid-sentence references.
        //
        // Each pattern matches the trailing comma + any following
        // whitespace so what's stripped is "<phrase>, " in one move; the
        // remaining text is then capitalised at its new opener if needed.
        //
        // RegexOptions.IgnoreCase covers the surface forms the LLM emits.
        // Compiled once at class init — these are hot-path on every
        // delivered turn.
        private static readonly Regex[] Patterns =
        {
            // "As you said,"  /  "As you just said,"
            new Regex(@"(?<=^|[\.\!\?]\s)As you (?:just )?said,?\s*",
                RegexOptions.IgnoreCase | RegexOptions.Compiled),

            // "Like we said," / "Like you just said," / "Like we mentioned," etc.
            new Regex(@"(?<=^|[\.\!\?]\s)Like (?:we|you) (?:just )?(?:said|mentioned),?\s*",
                RegexOptions.IgnoreCase | RegexOptions.Compiled),

            // "As we discussed," / "As I just discussed,"
            new Regex(@"(?<=^|[\.\!\?]\s)As (?:we|I) (?:just )?discussed,?\s*",
                RegexOptions.IgnoreCase | RegexOptions.Compiled),

            // "Like I said,"
            new Regex(@"(?<=^|[\.\!\?]\s)Like I (?:just )?said,?\s*",
                RegexOptions.IgnoreCase | RegexOptions.Compiled),

            // "As I mentioned,"
            new Regex(@"(?<=^|[\.\!\?]\s)As I (?:just )?mentioned,?\s*",
                RegexOptions.IgnoreCase | RegexOptions.Compiled),
        };

        // Patterns that look callback-shaped but contain an explicit
        // turn-number reference are kept as legitimate cross-turn
        // references and NEVER stripped. Checked before any pattern runs;
        // if any preserve-pattern matches we short-circuit out.
        private static readonly Regex[] PreservePatterns =
        {
            // "earlier, on turn 2..." / "back on turn 5..."
            new Regex(@"\bturn\s+\d+\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        };

        /// <summary>
        /// Run the strip pass on <paramref name="input"/>. Returns the
        /// stripped text (possibly equal to the input if no pattern
        /// matched). Never null.
        /// </summary>
        public static string Strip(string? input)
        {
            if (string.IsNullOrEmpty(input)) return input ?? string.Empty;

            // Preserve legitimate cross-turn references: if the message
            // contains an explicit "turn N" reference, treat the whole
            // message as legitimate and skip the strip pass.
            foreach (var preserve in PreservePatterns)
                if (preserve.IsMatch(input!)) return input!;

            string result = input!;
            foreach (var p in Patterns)
            {
                result = p.Replace(result, string.Empty);
            }

            if (ReferenceEquals(result, input)) return input!;

            // Cosmetic cleanup pass 1: collapse double spaces produced
            // when a stripped span sat between two existing spaces.
            while (result.Contains("  "))
                result = result.Replace("  ", " ");

            // Cosmetic cleanup pass 2: stripped openers leave a leading
            // lowercase letter (start-of-message OR after a sentence
            // boundary). Re-capitalise the first alpha after the start of
            // the message and after each `". "` / `"! "` / `"? "` pair.
            result = result.TrimStart();
            result = ReCapitaliseSentenceStarts(result);

            return result;
        }

        /// <summary>
        /// Walk the string, lower-casing the first alpha char of each
        /// sentence (start of string + immediately after a `". "`, `"! "`,
        /// or `"? "`). Used post-strip so a callback removed from the
        /// middle of a multi-sentence message doesn’t leave the next
        /// sentence starting in lower case.
        /// </summary>
        private static string ReCapitaliseSentenceStarts(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            var chars = s.ToCharArray();
            bool atSentenceStart = true;
            for (int i = 0; i < chars.Length; i++)
            {
                char c = chars[i];
                if (atSentenceStart && char.IsLetter(c))
                {
                    chars[i] = char.ToUpperInvariant(c);
                    atSentenceStart = false;
                    continue;
                }
                if ((c == '.' || c == '!' || c == '?')
                    && i + 1 < chars.Length && chars[i + 1] == ' ')
                {
                    atSentenceStart = true; // capitalise next alpha
                }
                else if (!char.IsWhiteSpace(c))
                {
                    atSentenceStart = false;
                }
            }
            return new string(chars);
        }

        /// <summary>
        /// Convenience: returns whether <see cref="Strip"/> would actually
        /// change <paramref name="input"/>. Same logic, exposed for
        /// callers that want to skip building a <see cref="TextDiff"/>
        /// when there's nothing to record.
        /// </summary>
        public static bool WouldStrip(string? input)
        {
            if (string.IsNullOrEmpty(input)) return false;
            string stripped = Strip(input);
            return !ReferenceEquals(stripped, input) && stripped != input;
        }
    }
}
