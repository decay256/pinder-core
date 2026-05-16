using System;
using System.Collections.Generic;
using System.Linq;
using Pinder.Core.Characters;

namespace Pinder.Core.Prompts
{
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
    /// As of issue #907, the aggregator also applies a <em>conflict
    /// matrix</em> loaded from <c>data/persona/texting-style-conflicts.yaml</c>
    /// via <see cref="TextingStyleConflicts"/>. When two picked
    /// <c>(axis, value)</c> pairs are mutually exclusive, the later-picked
    /// value is dropped and the drop is recorded in the audit log that is
    /// returned via <see cref="AggregateWithAudit"/>. Callers that only
    /// need the string output can use <see cref="Aggregate"/> as before.
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

        public static readonly IReadOnlyDictionary<string, string> SlotToSyntaxAxis =
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

        // ------------------------------------------------------------------
        // Audit log entry — one per dropped (axis, value) pair.
        // #907: surfaced so callers can log which fragments were rejected
        // at session-creation time.
        // ------------------------------------------------------------------

        /// <summary>
        /// Records a single fragment that was rejected by the conflict resolver.
        /// </summary>
        public sealed class ConflictDropEntry
        {
            /// <summary>Character id (the <c>seedKey</c> passed by the caller).</summary>
            public string? CharacterId   { get; }
            /// <summary>The axis whose value was dropped.</summary>
            public string  Axis          { get; }
            /// <summary>The value that was dropped.</summary>
            public string  DroppedValue  { get; }
            /// <summary>The axis whose kept value triggered the drop.</summary>
            public string  ConflictAxis  { get; }
            /// <summary>The value that was already kept when the conflict fired.</summary>
            public string  KeptValue     { get; }
            /// <summary>Human-readable reason from the conflict matrix.</summary>
            public string  Reason        { get; }

            public ConflictDropEntry(
                string? characterId,
                string  axis,
                string  droppedValue,
                string  conflictAxis,
                string  keptValue,
                string  reason)
            {
                CharacterId  = characterId;
                Axis         = axis;
                DroppedValue = droppedValue;
                ConflictAxis = conflictAxis;
                KeptValue    = keptValue;
                Reason       = reason;
            }

            public override string ToString() =>
                $"[ConflictDrop] char={CharacterId ?? "(unknown)"} " +
                $"dropped={Axis}:{DroppedValue} " +
                $"conflict_with={ConflictAxis}:{KeptValue} " +
                $"reason=\"{Reason}\"";
        }

        /// <summary>
        /// Result of conflict-aware aggregation: the resolved axis lines
        /// plus the audit log of dropped fragments.
        /// </summary>
        public sealed class AggregationResult
        {
            public IReadOnlyList<string>           Lines   { get; }
            public IReadOnlyList<ConflictDropEntry> Drops  { get; }

            public AggregationResult(
                IReadOnlyList<string>           lines,
                IReadOnlyList<ConflictDropEntry> drops)
            {
                Lines = lines;
                Drops = drops;
            }
        }

        // ------------------------------------------------------------------
        // #907: Production conflict catalog. Loaded once at startup by
        // PromptWiring.Wire() from data/persona/texting-style-conflicts.yaml.
        // The 2-arg overloads below use this catalog automatically so all
        // existing callsites get conflict resolution without signature changes.
        // Defaults to Empty (no-op) until Wire() assigns the loaded catalog.
        // ------------------------------------------------------------------

        /// <summary>
        /// The globally-loaded conflict catalog. Assigned by
        /// <c>PromptWiring.Wire()</c> at startup. Falls back to
        /// <see cref="TextingStyleConflicts.Empty"/> if not yet assigned.
        /// Tests that need an isolated catalog should use the 3-arg overloads
        /// directly.
        /// </summary>
        public static TextingStyleConflicts? ConflictCatalog { get; set; }

        // ------------------------------------------------------------------
        // Public surface (unchanged signatures from the placeholder). The
        // seedKey parameter is retained for callers but unused by the v1
        // rule — deterministic by construction.
        // ------------------------------------------------------------------

        /// <summary>
        /// Aggregate the texting-style sources into the joined string that
        /// gets injected into the LLM system prompt / runtime player
        /// style. Implements the #836 v1 rule with #907 conflict resolution.
        ///
        /// Uses <see cref="ConflictCatalog"/> when set (assigned by
        /// <c>PromptWiring.Wire()</c>), otherwise falls back to
        /// <see cref="TextingStyleConflicts.Empty"/>. Pass an explicit
        /// catalog via
        /// <see cref="Aggregate(IReadOnlyList{TextingStyleFragmentSource}, string?, TextingStyleConflicts)"/>
        /// to override for a specific call.
        /// </summary>
        public static string Aggregate(
            IReadOnlyList<TextingStyleFragmentSource> sources,
            string? seedKey)
            => Aggregate(sources, seedKey, ConflictCatalog ?? TextingStyleConflicts.Empty);

        /// <summary>
        /// Aggregate with conflict resolution. Dropped fragments are silently
        /// discarded; use <see cref="AggregateWithAudit"/> to capture them.
        /// </summary>
        public static string Aggregate(
            IReadOnlyList<TextingStyleFragmentSource> sources,
            string? seedKey,
            TextingStyleConflicts conflicts)
        {
            var result = AggregateWithAudit(sources, seedKey, conflicts);
            return result.Lines.Count == 0
                ? string.Empty
                : string.Join(" | ", result.Lines);
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
        /// Uses <see cref="ConflictCatalog"/> when set (assigned by
        /// <c>PromptWiring.Wire()</c>), otherwise falls back to
        /// <see cref="TextingStyleConflicts.Empty"/>.
        /// </summary>
        public static IReadOnlyList<string> AggregateAsList(
            IReadOnlyList<TextingStyleFragmentSource> sources,
            string? seedKey)
            => AggregateAsList(sources, seedKey, ConflictCatalog ?? TextingStyleConflicts.Empty);

        /// <summary>
        /// Aggregate to a list with conflict resolution. Dropped fragments
        /// are silently discarded; use <see cref="AggregateWithAudit"/> for
        /// the full result including the audit log.
        /// </summary>
        public static IReadOnlyList<string> AggregateAsList(
            IReadOnlyList<TextingStyleFragmentSource> sources,
            string? seedKey,
            TextingStyleConflicts conflicts)
            => AggregateWithAudit(sources, seedKey, conflicts).Lines;

        /// <summary>
        /// Full conflict-aware aggregation with audit log. Returns both the
        /// resolved axis lines and the list of dropped fragments (one entry
        /// per conflict fired). Callers at session-creation time should log
        /// the <see cref="AggregationResult.Drops"/> so content authors can
        /// detect problematic item combinations.
        /// </summary>
        public static AggregationResult AggregateWithAudit(
            IReadOnlyList<TextingStyleFragmentSource> sources,
            string? seedKey,
            TextingStyleConflicts conflicts)
        {
            // seedKey is retained on the API surface for backward
            // compatibility but unused by the v1 deterministic rule.
            // It IS used as CharacterId in audit-log entries.
            _ = seedKey;

            if (sources == null || sources.Count == 0)
                return new AggregationResult(Array.Empty<string>(), Array.Empty<ConflictDropEntry>());

            // Index syntax inputs by slot, tone inputs by parameter id.
            // Multiple sources for the same slot would be a content bug
            // (two items in one slot is not supposed to happen); first
            // wins so the assembler's ordering decides.
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

            // Pre-parse each anatomy fragment into its TONE map so the
            // group-vote step doesn't re-parse on every lookup.
            var toneByParam = new Dictionary<string, IReadOnlyDictionary<string, string>>(
                StringComparer.OrdinalIgnoreCase);
            foreach (var (paramId, frag) in anatomyByParam)
            {
                toneByParam[paramId] = ParseToneAxes(frag);
            }

            // Resolve axis-by-axis in canonical order. Missing axes drop.
            // Collect as (axis, value) pairs first so we can run conflict
            // resolution across all picked values before emitting strings.
            var pickedPairs = new List<(string axis, string value)>();

            // Syntax axes — read from the slot's item, if equipped.
            foreach (var kv in SlotToSyntaxAxis)
            {
                string slot = kv.Key;
                string axis = kv.Value;
                if (!bySlot.TryGetValue(slot, out var fragment)) continue;
                var syntax = ParseSyntaxAxes(fragment);
                if (syntax.TryGetValue(axis, out var line) && !string.IsNullOrWhiteSpace(line))
                {
                    pickedPairs.Add((axis, line));
                }
            }

            // Tone axes — majority vote per group.
            string? stanceLine   = MajorityVote("stance",   StanceGroup,   toneByParam);
            string? registerLine = MajorityVote("register", RegisterGroup, toneByParam);
            string? pacingLine   = MajorityVote("pacing",   PacingGroup,   toneByParam);

            if (stanceLine   != null) pickedPairs.Add(AxisValuePairOf(stanceLine));
            if (registerLine != null) pickedPairs.Add(AxisValuePairOf(registerLine));
            if (pacingLine   != null) pickedPairs.Add(AxisValuePairOf(pacingLine));

            // ------------------------------------------------------------------
            // #907: Conflict resolution.
            //
            // Walk the picked set; on conflict, drop the LATER-picked value
            // (the one that conflicts with an already-kept earlier value).
            // The resolver is O(n²) over the picked set — fine for n ≤ 9.
            // ------------------------------------------------------------------
            var kept  = new List<(string axis, string value)>(pickedPairs.Count);
            var drops = new List<ConflictDropEntry>();

            foreach (var candidate in pickedPairs)
            {
                string? conflictReason = null;
                (string axis, string value) conflictKept = default;

                foreach (var alreadyKept in kept)
                {
                    var reason = conflicts.GetReason(alreadyKept, candidate);
                    if (reason != null)
                    {
                        conflictReason = reason;
                        conflictKept   = alreadyKept;
                        break;
                    }
                }

                if (conflictReason != null)
                {
                    drops.Add(new ConflictDropEntry(
                        characterId:  seedKey,
                        axis:         candidate.axis,
                        droppedValue: candidate.value,
                        conflictAxis: conflictKept.axis,
                        keptValue:    conflictKept.value,
                        reason:       conflictReason));
                    // Do NOT add to kept — this axis is silenced for this character.
                }
                else
                {
                    kept.Add(candidate);
                }
            }

            // Re-order to match the canonical sequence.
            var canonicalList = CanonicalAxisOrder.ToList();
            var result = kept
                .Select(p => $"{p.axis}: {p.value}")
                .OrderBy(line => canonicalList.IndexOf(AxisOf(line)))
                .ToList();

            return new AggregationResult(result, drops);
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

        // ------------------------------------------------------------------
        // Tone aggregation — majority vote across an anatomy group.
        // ------------------------------------------------------------------

        private static string? MajorityVote(
            string axisName,
            IReadOnlyList<string> groupParams,
            IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> toneByParam)
        {
            // Tally per text. Keep a parallel "first source rank" so the
            // tie-break (group order) is correct: if two lines tie at the
            // highest count, the one whose earliest source-param sits
            // earlier in groupParams wins.
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

            if (counts.Count == 0) return null;

            // Sort: most votes first, then earliest first-source rank.
            var winner = counts
                .OrderByDescending(kv => kv.Value)
                .ThenBy(kv => firstRank[kv.Key])
                .First()
                .Key;

            return $"{axisName}: {winner}";
        }

        // ------------------------------------------------------------------
        // Output ordering helpers.
        // ------------------------------------------------------------------

        private static string AxisOf(string axisPrefixedLine)
        {
            int colon = axisPrefixedLine.IndexOf(':');
            return colon > 0 ? axisPrefixedLine.Substring(0, colon) : axisPrefixedLine;
        }

        private static (string axis, string value) AxisValuePairOf(string axisPrefixedLine)
        {
            int colon = axisPrefixedLine.IndexOf(':');
            if (colon <= 0) return (axisPrefixedLine, string.Empty);
            return (
                axisPrefixedLine.Substring(0, colon).Trim(),
                axisPrefixedLine.Substring(colon + 1).Trim()
            );
        }
    }
}
