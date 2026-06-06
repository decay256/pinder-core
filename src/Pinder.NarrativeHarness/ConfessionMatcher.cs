using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Pinder.Tools.NarrativeHarness
{
    /// <summary>One heuristic detection of a confession surfacing in model output.</summary>
    public sealed class ConfessionHit
    {
        public ConfessionEntry Entry { get; }
        public int Overlap { get; }
        public IReadOnlyList<string> MatchedTokens { get; }

        public ConfessionHit(ConfessionEntry entry, int overlap, IReadOnlyList<string> matchedTokens)
        {
            Entry = entry;
            Overlap = overlap;
            MatchedTokens = matchedTokens;
        }
    }

    /// <summary>
    /// BEST-EFFORT, HEURISTIC post-hoc matcher. Given a model utterance, guesses
    /// which confession(s) it appears to have drawn on by keyword / named-anchor
    /// overlap. This is explicitly NOT ground truth — it is labelled as
    /// heuristic detection in the transcript so reviewers don't over-trust it.
    /// </summary>
    public static class ConfessionMatcher
    {
        // Common words that should not count as evidence of a specific confession.
        private static readonly HashSet<string> Stop = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "the","a","an","and","or","but","is","was","were","are","i","you","my","me","to",
            "of","in","on","at","it","that","this","with","for","when","what","most","thing",
            "about","im","i'm","have","had","would","find","last","next","do","be","not","no",
            "from","got","get","one","like","just","so","if","then","really","actually","said",
        };

        /// <summary>
        /// Return confession hits sorted by descending overlap. A hit requires
        /// either a named-anchor match or at least <paramref name="minTokens"/>
        /// distinctive token overlaps.
        /// </summary>
        public static IReadOnlyList<ConfessionHit> Detect(
            string utterance, IReadOnlyList<ConfessionEntry> entries, int minTokens = 2)
        {
            var uttTokens = Tokenize(utterance);
            var uttSet = new HashSet<string>(uttTokens, StringComparer.OrdinalIgnoreCase);
            var hits = new List<ConfessionHit>();

            foreach (var e in entries)
            {
                var matched = new List<string>();

                // Named/dated anchors are strong signals.
                foreach (var anchor in e.Anchors)
                {
                    foreach (var part in Tokenize(anchor))
                    {
                        if (part.Length >= 3 && uttSet.Contains(part) && !matched.Contains(part))
                            matched.Add(part);
                    }
                }

                // Distinctive content tokens.
                foreach (var tok in Tokenize(e.Text))
                {
                    if (tok.Length < 4) continue;
                    if (Stop.Contains(tok)) continue;
                    if (uttSet.Contains(tok) && !matched.Contains(tok))
                        matched.Add(tok);
                }

                bool anchorHit = e.Anchors.Any(a =>
                    Tokenize(a).Any(p => p.Length >= 3 && uttSet.Contains(p)));

                if ((anchorHit && matched.Count >= 1) || matched.Count >= minTokens)
                    hits.Add(new ConfessionHit(e, matched.Count, matched));
            }

            return hits.OrderByDescending(h => h.Overlap).ToList();
        }

        private static IEnumerable<string> Tokenize(string s)
        {
            foreach (Match m in Regex.Matches(s ?? "", @"[A-Za-z']+"))
            {
                string t = m.Value.Trim('\'');
                if (t.Length > 0) yield return t;
            }
        }
    }
}
