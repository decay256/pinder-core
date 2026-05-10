using System;
using System.Text.RegularExpressions;

namespace Pinder.LlmAdapters
{
    /// <summary>
    /// Issue #351 (introduced) / #831 (DRY refactor): post-processor that
    /// strips a leading <c>&lt;thinking&gt;...&lt;/thinking&gt;</c> or
    /// <c>&lt;reasoning&gt;...&lt;/reasoning&gt;</c> block from LLM responses.
    ///
    /// As of #831 this is invoked at the <see cref="Pinder.Core.Interfaces.ILlmTransport"/>
    /// boundary by <see cref="ThinkingStrippingLlmTransport"/> (and its
    /// streaming counterpart in the same class), not at individual call
    /// sites. The transport decorator is registered as the outermost
    /// transformation layer in pinder-core's <c>session-runner/Program.cs</c>
    /// and pinder-web's <c>LlmProviderFactory</c>, so every prose-only and
    /// structured surface (delivery, opponent reply, steering, overlays,
    /// stake, outfit, interest beat, dialogue options, …) automatically
    /// gets strip-on-output. New call sites added later cannot silently
    /// leak thinking blocks into player-visible text or the persistent
    /// system prompt.
    ///
    /// Structured surfaces (dialogue option parsing, opponent response
    /// signals) parse fields out of a known shape after a marker line
    /// (e.g. <c>OPTION_N</c> or <c>[SIGNALS]</c>); a stripped leading
    /// thinking block leaves those parsers unchanged.
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
    /// Pattern is case-insensitive and dot-matches-newline so multi-line
    /// thinking blocks are caught. The static <see cref="Strip"/> method is
    /// retained as the canonical pure-function form so the decorator and
    /// any future direct caller (e.g. ad-hoc audit-log post-processing)
    /// share the same regex.
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
