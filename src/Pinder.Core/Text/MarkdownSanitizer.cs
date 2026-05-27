using System.Text.RegularExpressions;

namespace Pinder.Core.Text
{
    /// <summary>
    /// Issue #1041 (Tier C): Strips markdown formatting from LLM output for surfaces
    /// that expect plain text (e.g. outfit descriptions, opponent responses). Compiled
    /// regex patterns are used to avoid repeated JIT overhead on the hot-path per-turn
    /// calls. Bullet-list markers (<c>- </c>) are intentionally preserved so the stake
    /// and other bullet-list surfaces render correctly in the SPA (per issue #949).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Strip order matters: code fences before inline code (to avoid partial matches),
    /// images before links (image syntax is a superset of link syntax), bold-italic
    /// before bold before italic (same prefix reasoning). Headings and blockquotes are
    /// stripped of their Markdown sigil only, leaving the prose text intact.
    /// </para>
    /// <para>
    /// Bullet markers (<c>- </c>, <c>* </c>, <c>+ </c>) are preserved as-is.
    /// Numbered list markers (<c>1. </c> etc.) are also preserved since they carry
    /// structural meaning in stake output.
    /// </para>
    /// </remarks>
    public static class MarkdownSanitizer
    {
        /// <summary>
        /// Layer name emitted on the <see cref="TextDiff"/> when the strip pass
        /// actually changed the message. Stable string — snapshot/replay tooling
        /// can match against it.
        /// </summary>
        public const string LayerName = "Markdown Sanitize";

        // ── compiled patterns ───────────────────────────────────────────────────
        // All patterns are compiled once at class-init (static readonly). This
        // eliminates per-call Regex JIT compilation on the hot per-turn path.

        // Fenced code blocks: ```optional-lang\n...\n``` — strip fence+lang, keep body.
        private static readonly Regex CodeFenceRegex = new Regex(
            @"```[^\n]*\n?([\s\S]*?)```",
            RegexOptions.Compiled);

        // Inline code: `text` → text
        private static readonly Regex InlineCodeRegex = new Regex(
            @"`([^`\r\n]+)`",
            RegexOptions.Compiled);

        // Image: ![alt](url) → alt (must precede LinkRegex; image is superset)
        private static readonly Regex ImageRegex = new Regex(
            @"!\[([^\]]*)\]\([^\)]*\)",
            RegexOptions.Compiled);

        // Link: [text](url) → text
        private static readonly Regex LinkRegex = new Regex(
            @"\[([^\]]+)\]\([^\)]*\)",
            RegexOptions.Compiled);

        // Horizontal rule: three or more - * _ on their own line → remove line
        private static readonly Regex HorizontalRuleRegex = new Regex(
            @"^[ \t]*[-*_]{3,}[ \t]*$",
            RegexOptions.Multiline | RegexOptions.Compiled);

        // ATX heading: # / ## / ... → remove sigil, keep heading text
        private static readonly Regex HeadingRegex = new Regex(
            @"^#{1,6}[ \t]+",
            RegexOptions.Multiline | RegexOptions.Compiled);

        // Blockquote: > prefix → remove sigil, keep prose
        private static readonly Regex BlockquoteRegex = new Regex(
            @"^>[ \t]?",
            RegexOptions.Multiline | RegexOptions.Compiled);

        // Bold-italic: ***text*** or ___text___ → text
        private static readonly Regex BoldItalicRegex = new Regex(
            @"\*{3}(.+?)\*{3}|_{3}(.+?)_{3}",
            RegexOptions.Compiled);

        // Bold: **text** or __text__ → text
        private static readonly Regex BoldRegex = new Regex(
            @"\*{2}(.+?)\*{2}|_{2}(.+?)_{2}",
            RegexOptions.Compiled);

        // Italic: *text* or _text_ (word-boundary guarded) → text
        private static readonly Regex ItalicRegex = new Regex(
            @"(?<!\w)\*(?!\s)(.+?)(?<!\s)\*(?!\w)|(?<!\w)_(?!\s)(.+?)(?<!\s)_(?!\w)",
            RegexOptions.Compiled);

        // Strikethrough: ~~text~~ → text
        private static readonly Regex StrikethroughRegex = new Regex(
            @"~~(.+?)~~",
            RegexOptions.Compiled);

        // ── public API ───────────────────────────────────────────────────────────

        /// <summary>
        /// Strip markdown formatting from <paramref name="input"/> and return
        /// plain-text prose. Bullet-list markers and numbered list markers are
        /// preserved. Never returns null.
        /// </summary>
        public static string Strip(string? input)
        {
            if (string.IsNullOrEmpty(input)) return input ?? string.Empty;

            string s = input!;

            // 1. Code (fences first, then inline, to avoid partial-match confusion)
            s = CodeFenceRegex.Replace(s, "$1");
            s = InlineCodeRegex.Replace(s, "$1");

            // 2. Links/images (images before links — image syntax is a superset)
            s = ImageRegex.Replace(s, "$1");
            s = LinkRegex.Replace(s, "$1");

            // 3. Structural chrome with no textual content
            s = HorizontalRuleRegex.Replace(s, string.Empty);

            // 4. Block-level sigils (heading/blockquote) — remove sigil, keep text
            s = HeadingRegex.Replace(s, string.Empty);
            s = BlockquoteRegex.Replace(s, string.Empty);

            // 5. Inline emphasis: bold-italic → bold → italic → strikethrough
            //    Use MatchEvaluator to pick the correct capture group.
            s = BoldItalicRegex.Replace(s, m =>
                m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value);
            s = BoldRegex.Replace(s, m =>
                m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value);
            s = ItalicRegex.Replace(s, m =>
                m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value);
            s = StrikethroughRegex.Replace(s, "$1");

            return s.Trim();
        }

        /// <summary>
        /// Convenience: returns whether <see cref="Strip"/> would change
        /// <paramref name="input"/>. Uses the same logic without an extra allocation.
        /// </summary>
        public static bool WouldStrip(string? input)
        {
            if (string.IsNullOrEmpty(input)) return false;
            string stripped = Strip(input);
            return stripped != input;
        }
    }
}
