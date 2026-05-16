using System;
using System.Text.RegularExpressions;

namespace Pinder.Core.Text
{
    /// <summary>
    /// Issue #902: post-process pass that strips meta-prefix labels the LLM
    /// prepends to text output — category labels like <c>"WOULD-YOU-RATHER:"</c>,
    /// <c>"CONTEXT:"</c>, <c>"RECOGNITION:"</c>, <c>"OPENER:"</c>,
    /// <c>"GENUINE QUESTION:"</c>, <c>"LABEL:"</c>. These are LLM artifacts, not
    /// player-visible content; the engine strips them so the player never sees
    /// them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The regex matches an all-caps label token followed by a colon and
    /// optional whitespace, anchored to the start of the string. Hyphens are
    /// allowed (<c>"WOULD-YOU-RATHER:"</c> was observed in staging session
    /// <c>ce5a6f82</c>, turn 1). The pattern is intentionally strict — only
    /// strings that start with a label in the exact "<c>ALL_CAPS:</c>" shape
    /// are stripped. Mid-string or sentence-cased tokens are left alone.
    /// </para>
    /// <para>
    /// Output contract: returns the stripped text. If the pattern matched,
    /// the returned text differs from the input; otherwise the input is
    /// returned unchanged. Leading whitespace between the label and the
    /// start of content is consumed by the regex.
    /// </para>
    /// </remarks>
    public static class MetaPrefixStripper
    {
        /// <summary>
        /// Layer name emitted on the <see cref="TextDiff"/> when the
        /// strip pass actually changed the message. Stable string —
        /// snapshot/replay tooling can match against it.
        /// </summary>
        public const string LayerName = "Meta-Prefix Strip";

        // Pattern: one or more tokens consisting of A-Z, spaces, and hyphens,
        // followed by a colon and optional whitespace, anchored to the start
        // of the string. The outer non-capturing group ensures the colon is
        // only matched when there's a label before it (avoiding a bare
        // colon match). Compiled once at class init — this is hot-path on
        // every delivered turn and every overlay call.
        //
        // Note: the hyphen is the addition vs #862's original parser-stage
        // regex. Staging session ce5a6f82 proved "WOULD-YOU-RATHER:" slips
        // past ^[A-Z][A-Z\s]+:\s*; hyphens in label tokens are real.
        private static readonly Regex LabelRegex = new Regex(
            @"^(?:[A-Z][A-Z\s\-]+):\s*",
            RegexOptions.Compiled);

        /// <summary>
        /// Run the strip pass on <paramref name="input"/>. Returns the
        /// stripped text (possibly equal to the input if no pattern
        /// matched). Never null.
        /// </summary>
        public static string Strip(string? input)
        {
            if (string.IsNullOrEmpty(input)) return input ?? string.Empty;

            string result = LabelRegex.Replace(input!, string.Empty, 1);
            if (ReferenceEquals(result, input)) return input!;

            // Leading whitespace between label and content is consumed by the
            // regex; no further clean-up needed beyond what the regex already
            // does. Unlike CallbackStripper we don't need sentence-start
            // re-capitalisation because we only match at the very start of
            // the string, and the remainder is already properly capitalised.

            return result;
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
            return LabelRegex.IsMatch(input!);
        }
    }
}
