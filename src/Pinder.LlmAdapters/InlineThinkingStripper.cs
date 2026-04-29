using System;
using System.Text.RegularExpressions;

namespace Pinder.LlmAdapters
{
    /// <summary>
    /// Issue #351: post-processor that strips inline <c>&lt;thinking&gt;...&lt;/thinking&gt;</c>
    /// and <c>&lt;reasoning&gt;...&lt;/reasoning&gt;</c> blocks from prose-only LLM
    /// surfaces.
    ///
    /// Applied to surfaces where the LLM response is consumed as raw player-visible
    /// text (steering question, horniness/shadow/trap overlays). NOT applied to
    /// structured surfaces (matchup analysis, dialogue option parsing) — those
    /// already parse fields out of a known JSON / structured shape, so a stray
    /// thinking block would either be ignored or break parsing on its own.
    ///
    /// Conservative behaviour:
    ///   * Strip the wrapped block when it spans the WHOLE response (model
    ///     emits "&lt;thinking&gt;X&lt;/thinking&gt;" only).
    ///   * Strip a SINGLE occurrence at the start (model emits
    ///     "&lt;thinking&gt;X&lt;/thinking&gt;Y" — keep "Y").
    ///   * Don't try to be clever about partial / nested / mid-text tags;
    ///     leaving them in place is safer than silently mangling player
    ///     dialogue that legitimately contains an angle-bracket fragment.
    ///
    /// Pattern is case-insensitive and dot-matches-newline so multi-line thinking
    /// blocks are caught.
    /// </summary>
    public static class InlineThinkingStripper
    {
        // Match <thinking>...</thinking> or <reasoning>...</reasoning>.
        // RegexOptions.Singleline → ``.`` matches newlines, so multi-line
        // thinking blocks are caught.
        // RegexOptions.IgnoreCase → some models capitalise the tag.
        // The lazy ``.*?`` ensures the FIRST closing tag terminates the
        // capture (avoid swallowing later content if the model emits two
        // separate thinking blocks).
        private static readonly Regex LeadingTagRegex = new Regex(
            @"^\s*<\s*(?:thinking|reasoning)\s*>.*?<\s*/\s*(?:thinking|reasoning)\s*>",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);

        /// <summary>
        /// Strips a single leading <c>&lt;thinking&gt;...&lt;/thinking&gt;</c> or
        /// <c>&lt;reasoning&gt;...&lt;/reasoning&gt;</c> block from <paramref name="response"/>.
        ///
        /// Returns the input unchanged when:
        ///   * <paramref name="response"/> is null or empty.
        ///   * No leading tag block is present.
        ///   * The text begins with anything else (any leading content is
        ///     considered "real" prose; we don't reach into the middle of
        ///     player text to remove tags).
        ///
        /// On a match, returns the text after the tag block, with leading
        /// whitespace trimmed.
        /// </summary>
        public static string Strip(string? response)
        {
            if (string.IsNullOrEmpty(response))
                return response ?? string.Empty;

            var match = LeadingTagRegex.Match(response);
            if (!match.Success)
                return response;

            // Slice off the matched leading block; trim leading whitespace
            // from what remains so the player-visible text doesn't start
            // with the blank line that often follows a thinking block.
            string remaining = response.Substring(match.Index + match.Length);
            return remaining.TrimStart();
        }
    }
}
