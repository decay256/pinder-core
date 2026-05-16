using System;
using System.Collections.Generic;
using System.Linq;
using Pinder.Core.Characters;

namespace Pinder.Core.Prompts
{
    /// <summary>
    /// Records one fragment dropped from the aggregated texting-style
    /// profile due to a cross-axis conflict detected by
    /// <see cref="TextingStyleConflicts"/>.
    ///
    /// See #907.
    /// </summary>
    public sealed class AggregationAuditEntry
    {
        /// <summary>The axis whose value was dropped.</summary>
        public string Axis { get; }

        /// <summary>The value that was dropped.</summary>
        public string DroppedValue { get; }

        /// <summary>The axis of the value that caused the drop.</summary>
        public string KeptAxis { get; }

        /// <summary>The kept value that caused the drop.</summary>
        public string KeptValue { get; }

        /// <summary>Human-readable reason from the conflict matrix.</summary>
        public string Reason { get; }

        public AggregationAuditEntry(
            string axis,
            string droppedValue,
            string keptAxis,
            string keptValue,
            string reason)
        {
            Axis = axis ?? throw new ArgumentNullException(nameof(axis));
            DroppedValue = droppedValue ?? string.Empty;
            KeptAxis = keptAxis ?? throw new ArgumentNullException(nameof(keptAxis));
            KeptValue = keptValue ?? string.Empty;
            Reason = reason ?? string.Empty;
        }

        public override string ToString()
            => $"[{Axis}] \"{DroppedValue}\" dropped: conflicts with [{KeptAxis}] \"{KeptValue}\" — {Reason}";
    }

    // ====================================================================
    // Aggregator
    // ====================================================================

    /// <summary>
    /// Aggregation for the texting-style channel that flows into the LLM
    /// system prompt and runtime <c>PlayerTextingStyle</c>.
    ///
    /// As of issue #836, this class implements the discoverable v1 rule
    /// documented in <c>docs/persona/texting-style-aggregation.md</c>:
    ///
    ///   - The 6 item slots own the 6 syntax axes (1:1 mapping):
    ///         shoes → emoji, hat → shorthand, shirt → grammar,
    ///         trousers → structure, frame → length, accessory → tics.
    ///     Each slot reads the ASSIGNED axis line from its equipped item's
    ///     SYNTAX block; the other 5 lines on that item are ignored.
    ///   - The 9 anatomy parameters partition into 3 groups of 3, one
    ///     group per tone axis (stance, register, pacing). Each group
    ///     decides its axis by majority vote across the equipped tiers'
    ///     TONE block, ties broken by group order. Empty contributions
    ///     are dropped.
    ///   - Output is exactly 9 axes. Unfilled slots / silent groups drop
    ///     their axis from the final list rather than back-filling.
    ///
    /// As of issue #907, cross-axis conflict resolution is applied when a
    /// <see cref="TextingStyleConflicts"/> is provided. Conflicting
    /// axis-value pairs are resolved by keeping the value whose axis
    /// appears earlier in the canonical order; dropped fragments are
    /// recorded in the audit log. Tone axes attempt re-pick from
    /// remaining candidates before falling back to silent-dropping.
    ///
    /// Replaces the placeholder random-pick-2 aggregation. Personality /
    /// backstory channels are unaffected — they remain a flat join across
    /// items + anatomy and travel through different prompt sections.
    ///
    /// Determinism: the rule is fully deterministic for a given
    /// (character_id, equipped items, anatomy tiers). The
    /// <paramref name="seedKey"/> parameter is retained on the public
    /// surface for backward compatibility with callers that pass the
    /// character UUID, but the rule itself no longer consults RNG —
    /// the seed is unused in v1. It may return as a tie-breaker in a
    /// future revision.
    /// </summary>
    public static class TextingStyleAggregator
    {
        // ------------------------------------------------------------------
        // Slot → syntax axis (1:1 fixed mapping, see
        // docs/persona/texting-style-aggregation.md). Lookups are
        // ordinal-case-insensitive so future content can use either
        // "shoes" or "Shoes" without the aggregator silently dropping it.
        // ------------------------------------------------------------------

        internal static readonly IReadOnlyDictionary<string, string> SlotToSyntaxAxis =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "shoes",     "emoji" },
                { "hat",       "shorthand" },
                { "shirt",     "grammar" },
                { "trousers",  "structure" },
                { "frame",     "length" },
                { "accessory", "tics" },
            };

        // ------------------------------------------------------------------
        // Anatomy parameter → tone axis groupings. The order of parameters
        // inside each group is load-bearing: it's the tie-breaker when
        // two distinct lines share the highest count. See the design doc
        // for the rationale (size/shape → stance, surface → register,
        // presentation → pacing).
        // ------------------------------------------------------------------

        internal static readonly IReadOnlyList<string> StanceGroup =
            new[] { "length", "girth", "circumcision" };

        internal static readonly IReadOnlyList<string> RegisterGroup =
            new[] { "vein_definition", "skin_texture", "skin_tone" };

        internal static readonly IReadOnlyList<string> PacingGroup =
            new[] { "ball_size", "tattoos", "eye_style" };

        // Canonical output order. Aggregate() emits axes in this order;
        // missing axes are dropped, not preserved as gaps.
        internal static readonly IReadOnlyList<string> CanonicalAxisOrder =
            new[]
            {
                "emoji", "shorthand", "grammar", "structure", "length", "tics",
                "stance", "register", "pacing",
            };

        // Dense index for canonical axis order (fast rank lookups).
        private static readonly Dictionary<string, int> CanonicalRank =
            CanonicalAxisOrder
                .Select((name, idx) => (name, idx))
                .ToDictionary(x => x.name, x => x.idx, StringComparer.OrdinalIgnoreCase);

        // ------------------------------------------------------------------
        // Public surface (unchanged signatures from the placeholder). The
        // seedKey parameter is retained for callers but unused by the v1
        // rule — deterministic by construction.
        // ------------------------------------------------------------------

        /// <summary>
        /// Aggregate the texting-style sources into the joined string that
        /// gets injected into the LLM system prompt / runtime player
        /// style. Implements the #836 v1 rule. When
        /// <paramref name="conflicts"/> is non-null, cross-axis
        /// conflict resolution (#907) is applied and dropped fragments
        /// are recorded in <paramref name="auditEntries"/>.
        /// </summary>
        public static string Aggregate(
            IReadOnlyList<TextingStyleFragmentSource> sources,
            string? seedKey,
            TextingStyleConflicts? conflicts,
            out IReadOnlyList<AggregationAuditEntry>? auditEntries)
        {
            var picked = AggregateAsList(sources, seedKey, conflicts, out auditEntries);
            return picked.Count == 0 ? string.Empty : string.Join(" | ", picked);
        }

        /// <summary>
        /// Aggregate without conflict resolution (backward-compatible
        /// overload). Equivalent to
        /// <c>Aggregate(sources, seedKey, null, out _)</c>.
        /// </summary>
        public static string Aggregate(
            IReadOnlyList<TextingStyleFragmentSource> sources,
            string? seedKey)
        {
            return Aggregate(sources, seedKey, null, out _);
        }

        /// <summary>
        /// Aggregate to an ordered list of axis-prefixed lines. Used by
        /// <see cref="PromptBuilder"/> to bullet-format the TEXTING STYLE
        /// section in the system prompt.
        ///
        /// Each emitted line has the shape <c>"&lt;axis&gt;: &lt;rule&gt;"</c>,
        /// e.g. <c>"emoji: ends every sentence with an emoji that conveys
        /// its emotion"</c>. Axes appear in the canonical order documented
        /// in <c>texting-style-aggregation.md</c>; missing axes are
        /// dropped.
        ///
        /// When <paramref name="conflicts"/> is non-null, cross-axis
        /// conflict resolution (#907) is applied and dropped fragments
        /// are recorded in <paramref name="auditEntries"/>.
        /// </summary>
        public static IReadOnlyList<string> AggregateAsList(
            IReadOnlyList<TextingStyleFragmentSource> sources,
            string? seedKey,
            TextingStyleConflicts? conflicts,
            out IReadOnlyList<AggregationAuditEntry>? auditEntries)
        {
            // seedKey is retained on the API surface for backward
            // compatibility but unused by the v1 deterministic rule.
            _ = seedKey;

            if (sources == null || sources.Count == 0)
            {
                auditEntries = null;
                return Array.Empty<string>();
            }

            // Index syntax inputs by slot, anatomy inputs by parameter id.
            var bySlot = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var anatomyByParam = new List<(string ParamId, string Fragment)>();

            foreach (var src in sources)
            {
                if (src == null) continue;
                if (string.IsNullOrEmpty(src.Fragment)) continue;
                if (string.IsNullOrEmpty(src.SlotOrParameter)) continue;

                if (string.Equals(src.Kind, "item", StringComparison.Ordinal))
                {
                    if (!bySlot.ContainsKey(src.SlotOrParameter))
                        bySlot[src.SlotOrParameter] = src.Fragment;
                }
                else if (string.Equals(src.Kind, "anatomy", StringComparison.Ordinal))
                {
                    anatomyByParam.Add((src.SlotOrParameter, src.Fragment));
                }
            }

            // Pre-parse each anatomy fragment into its TONE map.
            var toneByParam = new Dictionary<string, IReadOnlyDictionary<string, string>>(
                StringComparer.OrdinalIgnoreCase);
            foreach (var (paramId, frag) in anatomyByParam)
            {
                toneByParam[paramId] = ParseToneAxes(frag);
            }

            // ------------------------------------------------------------------
            // Phase 1: collect initial picks (same as v1 rule).
            // picked[axis] = value (axis-prefixed line body, e.g. "ends every sentence …")
            // ------------------------------------------------------------------
            var picked = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            // Syntax axes — read from the slot's item, if equipped.
            foreach (var kv in SlotToSyntaxAxis)
            {
                string slot = kv.Key;
                string axis = kv.Value;
                if (!bySlot.TryGetValue(slot, out var fragment)) continue;
                var syntax = ParseSyntaxAxes(fragment);
                if (syntax.TryGetValue(axis, out var line) && !string.IsNullOrWhiteSpace(line))
                {
                    picked[axis] = line;
                }
            }

            // Tone axes — majority vote per group.
            // Also collect ALL ranked candidates per axis for potential re-pick (#907).
            var toneCandidates = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            AppendToneVoteResult("stance", StanceGroup, toneByParam, picked, toneCandidates);
            AppendToneVoteResult("register", RegisterGroup, toneByParam, picked, toneCandidates);
            AppendToneVoteResult("pacing", PacingGroup, toneByParam, picked, toneCandidates);

            // ------------------------------------------------------------------
            // Phase 2: conflict resolution (#907).
            // Walk picked axes in canonical order. When axis A (earlier)
            // conflicts with axis B (later): keep A's value, drop B's
            // value. For dropped tone axes, try next-ranked candidate
            // from toneCandidates before giving up.
            // ------------------------------------------------------------------
            var audit = new List<AggregationAuditEntry>();

            if (conflicts != null && picked.Count > 1)
            {
                ResolveConflicts(picked, toneCandidates, conflicts, audit);
            }

            auditEntries = audit.Count > 0 ? audit : null;

            // ------------------------------------------------------------------
            // Phase 3: format result in canonical order.
            // ------------------------------------------------------------------
            var result = new List<string>(picked.Count);
            foreach (var axis in CanonicalAxisOrder)
            {
                if (picked.TryGetValue(axis, out var value))
                    result.Add($"{axis}: {value}");
            }

            return result;
        }

        /// <summary>
        /// Aggregate without conflict resolution (backward-compatible
        /// overload). Equivalent to calling
        /// <c>AggregateAsList(sources, seedKey, null, out _)</c>.
        /// </summary>
        public static IReadOnlyList<string> AggregateAsList(
            IReadOnlyList<TextingStyleFragmentSource> sources,
            string? seedKey)
        {
            return AggregateAsList(sources, seedKey, null, out _);
        }

        // ------------------------------------------------------------------
        // Conflict resolution (#907)
        // ------------------------------------------------------------------

        private static void ResolveConflicts(
            Dictionary<string, string> picked,
            Dictionary<string, List<string>> toneCandidates,
            TextingStyleConflicts conflicts,
            List<AggregationAuditEntry> audit)
        {
            // Walk axes in canonical order; each seen axis gets a chance
            // to keep its value. Later axes that conflict with an already-
            // kept value are dropped (or re-picked if tone candidates exist).
            var keptAxes = new List<string>(picked.Count);

            foreach (var axis in CanonicalAxisOrder)
            {
                if (!picked.TryGetValue(axis, out var value))
                    continue;

                // Check against all already-kept axes.
                string? conflictAxis = null;
                string? conflictValue = null;
                string? conflictReason = null;

                foreach (var keptAxis in keptAxes)
                {
                    var keptValue = picked[keptAxis];
                    var reason = conflicts.GetReason((axis, value), (keptAxis, keptValue));
                    if (reason != null)
                    {
                        conflictAxis = keptAxis;
                        conflictValue = keptValue;
                        conflictReason = reason;
                        break;
                    }
                }

                if (conflictAxis == null)
                {
                    // No conflict — keep.
                    keptAxes.Add(axis);
                }
                else
                {
                    // Conflict: the kept value (earlier axis) wins.
                    // Try to re-pick from toneCandidates for this axis.
                    string? replacement = TryRePick(axis, toneCandidates, picked, keptAxes, conflicts);
                    if (replacement != null)
                    {
                        // Re-pick succeeded — use the replacement value.
                        var oldValue = picked[axis];
                        picked[axis] = replacement;
                        audit.Add(new AggregationAuditEntry(
                            axis, oldValue, conflictAxis, conflictValue!, conflictReason!));
                        // Re-check the replacement against all kept axes.
                        // (TryRePick already validated; add to kept list.)
                        keptAxes.Add(axis);
                    }
                    else
                    {
                        // No alternative — drop the axis entirely.
                        audit.Add(new AggregationAuditEntry(
                            axis, value, conflictAxis, conflictValue!, conflictReason!));
                        picked.Remove(axis);
                        // Do NOT add to keptAxes.
                    }
                }
            }
        }

        /// <summary>
        /// Try to find an alternative value for <paramref name="axis"/>
        /// from <paramref name="toneCandidates"/> that doesn't conflict
        /// with any already-kept axis-value pair. Returns <c>null</c>
        /// if no compatible candidate exists.
        /// </summary>
        private static string? TryRePick(
            string axis,
            Dictionary<string, List<string>> toneCandidates,
            Dictionary<string, string> picked,
            List<string> keptAxes,
            TextingStyleConflicts conflicts)
        {
            if (!toneCandidates.TryGetValue(axis, out var candidates))
                return null;

            foreach (var candidate in candidates)
            {
                // Skip the already-picked value.
                if (string.Equals(candidate,
                        picked.TryGetValue(axis, out var existingValue) ? existingValue : string.Empty,
                        StringComparison.Ordinal))
                    continue;

                // Check against all kept values.
                bool hasConflict = false;
                foreach (var keptAxis in keptAxes)
                {
                    if (conflicts.AreConflicting((axis, candidate), (keptAxis, picked[keptAxis])))
                    {
                        hasConflict = true;
                        break;
                    }
                }

                if (!hasConflict)
                    return candidate;
            }

            return null;
        }

        /// <summary>
        /// Run majority vote for a tone axis group and populate both
        /// <paramref name="picked"/> (the winner) and
        /// <paramref name="toneCandidates"/> (all candidates ranked by
        /// vote count then group order, for re-pick fallback).
        /// Returns the winning line body or null.
        /// </summary>
        private static void AppendToneVoteResult(
            string axisName,
            IReadOnlyList<string> groupParams,
            IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> toneByParam,
            Dictionary<string, string> picked,
            Dictionary<string, List<string>> toneCandidates)
        {
            // Tally per text.
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            var firstRank = new Dictionary<string, int>(StringComparer.Ordinal);

            for (int rank = 0; rank < groupParams.Count; rank++)
            {
                var paramId = groupParams[rank];
                if (!toneByParam.TryGetValue(paramId, out var tone)) continue;
                if (!tone.TryGetValue(axisName, out var line)) continue;
                if (string.IsNullOrWhiteSpace(line)) continue;

                counts.TryGetValue(line, out int c);
                counts[line] = c + 1;
                if (!firstRank.ContainsKey(line))
                    firstRank[line] = rank;
            }

            if (counts.Count == 0) return;

            // Build ranked candidate list (most votes → earliest first-rank).
            var ranked = counts
                .OrderByDescending(kv => kv.Value)
                .ThenBy(kv => firstRank[kv.Key])
                .Select(kv => kv.Key)
                .ToList();

            toneCandidates[axisName] = ranked;

            string winner = ranked[0];
            picked[axisName] = winner;
        }

        // ------------------------------------------------------------------
        // Parsing helpers — extract axis maps from a single
        // texting_style_fragment block. The canonical block shape is:
        //
        //   SYNTAX:
        //   - emoji: <line>
        //   - shorthand: <line>
        //   - grammar: <line>
        //   - structure: <line>
        //   - length: <line>
        //   - tics: <line>
        //   TONE:
        //   - stance (<key>): <line>
        //   - register (<key>): <line>
        //   - pacing (<key>): <line>
        //
        // The parser is forgiving on whitespace and parenthesised
        // sub-keys (e.g. "stance (escalator):") and silently drops lines
        // it can't classify so future content additions don't crash the
        // pipeline.
        // ------------------------------------------------------------------

        private static readonly string[] SyntaxAxisNames =
        {
            "emoji", "shorthand", "grammar", "structure", "length", "tics",
        };

        private static readonly string[] ToneAxisNames =
        {
            "stance", "register", "pacing",
        };

        internal static IReadOnlyDictionary<string, string> ParseSyntaxAxes(string fragment)
            => ParseAxes(fragment, "SYNTAX:", "TONE:", SyntaxAxisNames);

        internal static IReadOnlyDictionary<string, string> ParseToneAxes(string fragment)
            => ParseAxes(fragment, "TONE:", null, ToneAxisNames);

        private static IReadOnlyDictionary<string, string> ParseAxes(
            string fragment,
            string sectionHeader,
            string? endHeader,
            string[] axisNames)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(fragment)) return result;

            int sectionStart = fragment.IndexOf(sectionHeader, StringComparison.Ordinal);
            if (sectionStart < 0) return result;
            int bodyStart = sectionStart + sectionHeader.Length;

            int bodyEnd = fragment.Length;
            if (endHeader != null)
            {
                int endIdx = fragment.IndexOf(endHeader, bodyStart, StringComparison.Ordinal);
                if (endIdx >= 0) bodyEnd = endIdx;
            }

            string body = fragment.Substring(bodyStart, bodyEnd - bodyStart);
            var lines = body.Split('\n');
            foreach (var rawLine in lines)
            {
                var line = rawLine.Trim();
                if (line.Length == 0) continue;
                if (!line.StartsWith("-", StringComparison.Ordinal)) continue;
                line = line.Substring(1).Trim();

                int colon = line.IndexOf(':');
                if (colon <= 0) continue;
                string axisToken = line.Substring(0, colon).Trim();
                string value = line.Substring(colon + 1).Trim();
                if (value.Length == 0) continue;

                // axis token may carry a parenthesised sub-key, e.g.
                // "stance (escalator)". Strip it.
                int paren = axisToken.IndexOf('(');
                if (paren > 0) axisToken = axisToken.Substring(0, paren).Trim();

                foreach (var axis in axisNames)
                {
                    if (string.Equals(axisToken, axis, StringComparison.OrdinalIgnoreCase))
                    {
                        if (!result.ContainsKey(axis))
                            result[axis] = value;
                        break;
                    }
                }
            }

            return result;
        }
    }
}
