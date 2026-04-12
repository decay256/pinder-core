using System;
using System.Collections.Generic;

namespace Pinder.Core.Text
{
    public enum DiffSpanType { Keep, Remove, Add }

    public sealed class DiffSpan
    {
        public DiffSpanType Type { get; }
        public string Text { get; }
        public DiffSpan(DiffSpanType type, string text)
        {
            Type = type;
            Text = text;
        }
    }

    public sealed class TextDiff
    {
        /// <summary>Layer name, e.g. "Misfire", "Horniness", "Steering".</summary>
        public string LayerName { get; }
        public IReadOnlyList<DiffSpan> Spans { get; }
        public string Before { get; }
        public string After { get; }

        public TextDiff(string layerName, IReadOnlyList<DiffSpan> spans, string before, string after)
        {
            LayerName = layerName ?? throw new ArgumentNullException(nameof(layerName));
            Spans     = spans     ?? throw new ArgumentNullException(nameof(spans));
            Before    = before    ?? throw new ArgumentNullException(nameof(before));
            After     = after     ?? throw new ArgumentNullException(nameof(after));
        }
    }

    /// <summary>
    /// Myers word-level diff between two strings.
    /// Tokenizes on whitespace boundaries (punctuation attached to words).
    /// </summary>
    public static class WordDiff
    {
        /// <summary>
        /// Compute a word-level diff between <paramref name="before"/> and <paramref name="after"/>.
        /// Tokens are split by spaces; each token includes its trailing space if any,
        /// so rejoining spans gives the original string.
        /// </summary>
        public static IReadOnlyList<DiffSpan> Compute(string before, string after)
        {
            if (before == null) throw new ArgumentNullException(nameof(before));
            if (after  == null) throw new ArgumentNullException(nameof(after));

            var aTokens = Tokenize(before);
            var bTokens = Tokenize(after);

            // LCS via Myers / standard DP
            var lcs = ComputeLcs(aTokens, bTokens);

            // Walk the LCS to produce edit spans
            var raw = new List<DiffSpan>();
            int ai = 0, bi = 0, li = 0;
            while (ai < aTokens.Count || bi < bTokens.Count)
            {
                if (li < lcs.Count
                    && ai < aTokens.Count
                    && bi < bTokens.Count
                    && NormalizeToken(aTokens[ai]) == NormalizeToken(lcs[li])
                    && NormalizeToken(bTokens[bi]) == NormalizeToken(lcs[li]))
                {
                    raw.Add(new DiffSpan(DiffSpanType.Keep, bTokens[bi]));
                    ai++; bi++; li++;
                }
                else
                {
                    // Emit removes first, then adds (standard diff convention)
                    bool emittedAny = false;
                    while (ai < aTokens.Count && (li >= lcs.Count || NormalizeToken(aTokens[ai]) != NormalizeToken(lcs[li])))
                    {
                        raw.Add(new DiffSpan(DiffSpanType.Remove, aTokens[ai]));
                        ai++;
                        emittedAny = true;
                    }
                    while (bi < bTokens.Count && (li >= lcs.Count || NormalizeToken(bTokens[bi]) != NormalizeToken(lcs[li])))
                    {
                        raw.Add(new DiffSpan(DiffSpanType.Add, bTokens[bi]));
                        bi++;
                        emittedAny = true;
                    }
                    if (!emittedAny) break; // safety
                }
            }

            // Merge consecutive spans of the same type
            return Merge(raw);
        }

        // ── Tokenizer ────────────────────────────────────────────────────────

        /// <summary>
        /// Split text into tokens, each token = word + its trailing space (if any).
        /// This ensures tokens can be concatenated to reconstruct the original string.
        /// </summary>
        private static List<string> Tokenize(string text)
        {
            var tokens = new List<string>();
            if (string.IsNullOrEmpty(text)) return tokens;

            int i = 0;
            while (i < text.Length)
            {
                // Consume non-space characters (the word)
                int start = i;
                while (i < text.Length && text[i] != ' ') i++;
                // Consume any trailing spaces
                while (i < text.Length && text[i] == ' ') i++;
                tokens.Add(text.Substring(start, i - start));
            }
            return tokens;
        }

        // ── LCS via standard DP ───────────────────────────────────────────────

        // Compare tokens by their word content (stripped of trailing spaces)
        private static string NormalizeToken(string token) => token.TrimEnd();

        private static List<string> ComputeLcs(List<string> a, List<string> b)
        {
            int m = a.Count, n = b.Count;
            // dp[i][j] = length of LCS of a[0..i-1] and b[0..j-1]
            // Compare by normalized (trimmed) form so "text" and "text " match.
            var dp = new int[m + 1, n + 1];
            for (int i = 1; i <= m; i++)
                for (int j = 1; j <= n; j++)
                    dp[i, j] = NormalizeToken(a[i - 1]) == NormalizeToken(b[j - 1])
                        ? dp[i - 1, j - 1] + 1
                        : Math.Max(dp[i - 1, j], dp[i, j - 1]);

            // Backtrack — return the b-side tokens (they carry correct spacing for "after")
            var lcs = new List<string>();
            int r = m, c = n;
            while (r > 0 && c > 0)
            {
                if (NormalizeToken(a[r - 1]) == NormalizeToken(b[c - 1]))
                {
                    lcs.Add(b[c - 1]); // use b token (has correct trailing space for "after")
                    r--; c--;
                }
                else if (dp[r - 1, c] > dp[r, c - 1])
                    r--;
                else
                    c--;
            }
            lcs.Reverse();
            return lcs;
        }

        // ── Merge consecutive same-type spans ────────────────────────────────

        private static IReadOnlyList<DiffSpan> Merge(List<DiffSpan> raw)
        {
            if (raw.Count == 0) return Array.Empty<DiffSpan>();
            var result = new List<DiffSpan>();
            var current = raw[0];
            for (int i = 1; i < raw.Count; i++)
            {
                if (raw[i].Type == current.Type)
                    current = new DiffSpan(current.Type, current.Text + raw[i].Text);
                else
                {
                    result.Add(current);
                    current = raw[i];
                }
            }
            result.Add(current);
            return result;
        }
    }
}
