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
    public static partial class TextingStyleAggregator
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
        // for the rationale.
        //
        // Updated for #1175: parameter ids now mirror Unity CharacterData
        // field names. Old ids (length, girth, etc.) are replaced with the
        // Unity scalar ids. The 3 tone groups remain (stance, register, pacing)
        // but now cover the new ~24 Unity params.
        // ------------------------------------------------------------------

        internal static readonly IReadOnlyList<string> StanceGroup =
            new[] { "trunkLengthBase", "trunkLengthMid", "trunkLengthTip",
                    "trunkGirth", "trunkCurvature" };

        internal static readonly IReadOnlyList<string> RegisterGroup =
            new[] { "skinHue", "skinSat", "skinVal",
                    "freckles", "blemishes", "veins" };

        internal static readonly IReadOnlyList<string> PacingGroup =
            new[] { "glansScale", "glansWidth",
                    "scrotumScale", "leftTesticleScale", "rightTesticleScale", "scrotumDrop",
                    "isCircumcised" };

        // Canonical output order. Aggregate() emits axes in this order;
        // missing axes are dropped, not preserved as gaps.
        internal static readonly IReadOnlyList<string> CanonicalAxisOrder =
            new[]
            {
                "emoji", "shorthand", "grammar", "structure", "length", "tics",
                "stance", "register", "pacing",
            };

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
    }
}
