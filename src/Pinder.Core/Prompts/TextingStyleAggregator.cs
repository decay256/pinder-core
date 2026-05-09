using System;
using System.Collections.Generic;
using System.Linq;
using Pinder.Core.Characters;

namespace Pinder.Core.Prompts
{
    /// <summary>
    /// Placeholder aggregation for the texting-style channel that flows into
    /// the LLM system prompt and runtime <c>PlayerTextingStyle</c>.
    ///
    /// As of issue #836 the texting-style fragments on items + anatomy were
    /// reworked into 9-axis blocks. Stacking 6 items + several anatomy tiers
    /// and joining the raw fragments verbatim with " | " produces a long
    /// list of contradictory rules ("never asks questions" + "only asks
    /// questions"). The real aggregation rule design is tracked in #836.
    ///
    /// This helper is a placeholder that ships immediately and does the
    /// minimum to bound the noise:
    ///
    ///   1. Anatomy contributions are excluded entirely from the
    ///      texting-style channel. Anatomy still contributes to the
    ///      personality / backstory channels — those are untouched.
    ///   2. Of the remaining (item-only) fragments, exactly 2 are picked
    ///      when 2 or more items are equipped. With fewer items, all
    ///      available item fragments are used.
    ///   3. The pick is deterministic per character: a stable seed (the
    ///      character id when available, otherwise a stable hash of the
    ///      fragment content itself) feeds <see cref="System.Random"/>.
    ///      Same character configuration → same two items, every call.
    ///
    /// The full <see cref="FragmentCollection.TextingStyleFragments"/> and
    /// <see cref="FragmentCollection.TextingStyleSources"/> lists are NOT
    /// modified — the Character Sheet 'Texting Style' tab (#404) keeps the
    /// full per-source breakdown. Only the joined LLM-facing string is
    /// trimmed.
    /// </summary>
    public static class TextingStyleAggregator
    {
        /// <summary>How many item fragments the placeholder picks at most.</summary>
        public const int PlaceholderItemPickCount = 2;

        /// <summary>
        /// Aggregate the texting-style sources into the joined string that
        /// gets injected into the LLM system prompt / runtime player style.
        ///
        /// Anatomy entries are dropped. Up to <see cref="PlaceholderItemPickCount"/>
        /// item entries are kept, picked deterministically from the seed.
        /// Picked entries preserve their original order.
        /// </summary>
        /// <param name="sources">
        /// Per-source breakdown from
        /// <see cref="FragmentCollection.TextingStyleSources"/>. May be null
        /// or empty.
        /// </param>
        /// <param name="seedKey">
        /// Stable per-character seed (e.g. the character UUID). When null
        /// or empty, a stable hash of the fragment content is used so the
        /// pick is still deterministic for a given configuration.
        /// </param>
        /// <returns>The " | "-joined fragment string, or empty if no item fragments are available.</returns>
        public static string Aggregate(
            IReadOnlyList<TextingStyleFragmentSource> sources,
            string? seedKey)
        {
            if (sources == null || sources.Count == 0)
                return string.Empty;

            // 1. Anatomy is silenced for now in the texting-style channel.
            //    Personality / backstory fragments from anatomy are unaffected
            //    because they enter the prompt through different lists.
            var itemSources = new List<(int OriginalIndex, TextingStyleFragmentSource Src)>();
            for (int i = 0; i < sources.Count; i++)
            {
                var src = sources[i];
                if (src == null) continue;
                if (string.Equals(src.Kind, "item", StringComparison.Ordinal))
                    itemSources.Add((i, src));
            }

            if (itemSources.Count == 0)
                return string.Empty;

            // 2. With 0..PlaceholderItemPickCount items, no sampling is
            //    needed — just keep them all. The picked items preserve
            //    their original assembly order.
            List<(int OriginalIndex, TextingStyleFragmentSource Src)> picked;
            if (itemSources.Count <= PlaceholderItemPickCount)
            {
                picked = itemSources;
            }
            else
            {
                picked = PickDeterministic(itemSources, PlaceholderItemPickCount, seedKey);
            }

            // 3. Join with the existing " | " separator.
            return string.Join(" | ", picked.Select(p => p.Src.Fragment));
        }

        // ------------------------------------------------------------------

        /// <summary>
        /// Pick exactly <paramref name="count"/> entries from
        /// <paramref name="candidates"/> using a seeded RNG. Original order
        /// of the picks is preserved.
        /// </summary>
        private static List<(int OriginalIndex, TextingStyleFragmentSource Src)> PickDeterministic(
            List<(int OriginalIndex, TextingStyleFragmentSource Src)> candidates,
            int count,
            string? seedKey)
        {
            int seed = ResolveSeed(seedKey, candidates);
            var rng = new Random(seed);

            // Fisher-Yates partial shuffle: build an index permutation, then
            // take the first <count> indices and re-sort them so the picked
            // entries stay in original assembly order. This keeps the prompt
            // section ordering legible even though the *which two* decision
            // is randomized.
            int n = candidates.Count;
            var indices = new int[n];
            for (int i = 0; i < n; i++) indices[i] = i;
            for (int i = 0; i < count; i++)
            {
                int j = i + rng.Next(n - i);
                (indices[i], indices[j]) = (indices[j], indices[i]);
            }

            var pickedIndices = indices.Take(count).OrderBy(x => x).ToList();
            var result = new List<(int, TextingStyleFragmentSource)>(count);
            foreach (var idx in pickedIndices)
                result.Add(candidates[idx]);
            return result;
        }

        /// <summary>
        /// Resolve a 32-bit RNG seed from the caller-supplied key (preferred)
        /// or, when absent, a stable hash of the candidate fragments. Both
        /// paths yield deterministic-per-configuration picks. The character
        /// UUID is the canonical seed source.
        /// </summary>
        internal static int ResolveSeed(
            string? seedKey,
            IReadOnlyList<(int OriginalIndex, TextingStyleFragmentSource Src)> candidates)
        {
            if (!string.IsNullOrWhiteSpace(seedKey))
                return StableStringHash(seedKey!);

            // Fallback: stable hash of the candidate fragment content.
            // Same configuration → same seed, even without a character id.
            var sb = new System.Text.StringBuilder();
            foreach (var c in candidates)
            {
                sb.Append(c.Src.Source ?? string.Empty);
                sb.Append('\u001f');
                sb.Append(c.Src.Fragment ?? string.Empty);
                sb.Append('\u001e');
            }
            return StableStringHash(sb.ToString());
        }

        /// <summary>
        /// Deterministic 32-bit string hash. .NET's <see cref="string.GetHashCode()"/>
        /// is randomised per process, so we cannot use it as a stable seed.
        /// FNV-1a 32-bit is small, stable, and good enough for picking 2
        /// items out of N.
        /// </summary>
        private static int StableStringHash(string s)
        {
            unchecked
            {
                const uint FnvOffset = 2166136261u;
                const uint FnvPrime  = 16777619u;
                uint hash = FnvOffset;
                for (int i = 0; i < s.Length; i++)
                {
                    hash ^= s[i];
                    hash *= FnvPrime;
                }
                // Cast to int; Random(int) accepts the full int range.
                return (int)hash;
            }
        }
    }
}
