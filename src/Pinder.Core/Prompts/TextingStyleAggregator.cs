using System;
using System.Collections.Generic;
using System.Linq;
using Pinder.Core.Characters;

namespace Pinder.Core.Prompts
{
    /// <summary>
    /// Canonical texting-style axis taxonomy shared by runtime aggregation,
    /// content validation, and tooling. The exposed collections are immutable
    /// and ordered: syntax axes precede expression axes in model-facing output.
    /// </summary>
    public static class TextingStyleTaxonomy
    {
        public static IReadOnlyList<string> SyntaxAxes { get; } = Array.AsReadOnly(new[]
        {
            "emoji", "shorthand", "grammar", "structure", "length", "tics",
        });

        public static IReadOnlyList<string> ExpressionAxes { get; } = Array.AsReadOnly(new[]
        {
            "directness", "affect", "rhythm",
        });

        public static IReadOnlyList<string> CanonicalAxes { get; } = Array.AsReadOnly(
            SyntaxAxes.Concat(ExpressionAxes).ToArray());

        public static bool IsSyntaxAxis(string? axis)
            => Contains(SyntaxAxes, axis);

        public static bool IsExpressionAxis(string? axis)
            => Contains(ExpressionAxes, axis);

        private static bool Contains(IReadOnlyList<string> axes, string? candidate)
        {
            if (candidate == null) return false;
            for (int index = 0; index < axes.Count; index++)
            {
                if (string.Equals(axes[index], candidate, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }
    }

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
    ///   - The anatomy parameters partition into 3 expression groups: directness,
    ///     affect, and rhythm. Each group decides its axis by majority vote
    ///     across the equipped tiers' EXPRESSION block, ties broken by group
    ///     order. Empty contributions are dropped.
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
    /// Personality / backstory channels are unaffected — they remain a flat join across
    /// items + anatomy and travel through different prompt sections.
    ///
    /// Determinism: the rule is fully deterministic for a given
    /// (character_id, equipped items, anatomy tiers). When an axis has
    /// multiple authored candidates, <paramref name="seedKey"/> participates
    /// in a stable SHA-256 selector so each character gets one bounded,
    /// reproducible candidate per source/axis.
    ///
    /// Determinism guarantee: aggregation output is a pure function of the
    /// input sources, conflict catalog, and <paramref name="seedKey"/>. The same
    /// seed produces byte-for-byte stable output across repeated calls and
    /// sessions; different seeds may select different authored candidates.
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
                // Legacy fictional slot names (pre-#1176, kept for backward compatibility)
                { "shoes",     "emoji" },
                { "hat",       "shorthand" },
                { "shirt",     "grammar" },
                { "trousers",  "structure" },
                { "frame",     "length" },
                { "accessory", "tics" },

                // Issue #1176: Unity-verbatim slot names
                { "Special",   "emoji" },     // Unity slot 4 = Special (shoes/footwear)
                { "Head",      "shorthand" }, // Unity slot 0 = Head (hats/headwear)
                { "Body",      "grammar" },   // Unity slot 2 = Body (outfits)
                { "Hair",      "structure" }, // LookCatalog hair items
                { "Arms",      "length" },    // LookCatalog arms items
                { "Face",      "tics" },      // Unity slot 1 = Face (face accessories)
                // Waist (slot 3) is currently empty in Unity; no axis mapping yet.
                // Tattoo/Sticker items contribute to personality but not syntax axes.
            };

        // ------------------------------------------------------------------
        // Anatomy parameter → expression axis groupings. The order of parameters
        // inside each group is load-bearing: it's the tie-breaker when
        // two distinct lines share the highest count. See the design doc
        // for the rationale.
        //
        // Updated for #1175: parameter ids now mirror Unity CharacterData
        // field names. Old ids (length, girth, etc.) are replaced with the
        // Unity scalar ids. The three expression groups cover the new
        // Unity params while preserving deterministic group-order ties.
        // ------------------------------------------------------------------

        internal static readonly IReadOnlyList<string> DirectnessGroup =
            new[] { "trunkLengthBase", "trunkLengthMid", "trunkLengthTip",
                    "trunkGirth", "trunkCurvature" };

        internal static readonly IReadOnlyList<string> AffectGroup =
            new[] { "skinHue", "skinSat", "skinVal",
                    "freckles", "smoothness", "veins" };

        internal static readonly IReadOnlyList<string> RhythmGroup =
            new[] { "glansScale", "glansWidth",
                    "scrotumScale", "leftTesticleScale", "rightTesticleScale", "scrotumDrop",
                    "isCircumcised" };

        // Canonical output order. Aggregate() emits axes in this order;
        // missing axes are dropped, not preserved as gaps.
        internal static IReadOnlyList<string> CanonicalAxisOrder => TextingStyleTaxonomy.CanonicalAxes;

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
        // Public surface. The seedKey parameter participates in the
        // stable candidate selector when a source offers multiple fragments
        // for one axis.
        // ------------------------------------------------------------------

        /// <summary>
        /// Aggregate the texting-style sources into the joined string that
        /// gets injected into the LLM system prompt / runtime player
        /// style. Implements the #836 v1 rule with #907 conflict resolution.
        ///
        /// Determinism Guarantee: The aggregation output is a pure function of the
        /// input sources, conflict catalog, and seedKey. The same seed is stable
        /// byte-for-byte across repeated calls and sessions; different seeds may
        /// select different candidates from a multi-fragment axis.
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
            if (sources == null || sources.Count == 0)
                return new AggregationResult(Array.Empty<string>(), Array.Empty<ConflictDropEntry>(), Array.Empty<AttributedTextingStyleLine>());

            // Index syntax inputs by slot, expression inputs by parameter id.
            // Multiple sources for the same slot would be a content bug
            // (two items in one slot is not supposed to happen); first
            // wins so the assembler's ordering decides.
            var bySlot = new Dictionary<string, TextingStyleFragmentSource>(StringComparer.OrdinalIgnoreCase);
            var anatomyByParam = new Dictionary<string, TextingStyleFragmentSource>(StringComparer.OrdinalIgnoreCase);

            foreach (var src in sources)
            {
                if (src == null) continue;
                if (string.IsNullOrEmpty(src.Fragment)) continue;
                if (string.IsNullOrEmpty(src.SlotOrParameter)) continue;

                if (string.Equals(src.Kind, "item", StringComparison.Ordinal))
                {
                    if (!bySlot.ContainsKey(src.SlotOrParameter))
                        bySlot[src.SlotOrParameter] = src;
                }
                else if (string.Equals(src.Kind, "anatomy", StringComparison.Ordinal))
                {
                    if (!anatomyByParam.ContainsKey(src.SlotOrParameter))
                        anatomyByParam[src.SlotOrParameter] = src;
                }
            }

            // Pre-parse each anatomy fragment into its expression-axis map and
            // select one candidate per parameter/axis so the group-vote step
            // stays bounded and deterministic.
            var expressionByParam = new Dictionary<string, IReadOnlyDictionary<string, string>>(
                StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in anatomyByParam)
            {
                expressionByParam[kvp.Key] = SelectAxisCandidates(
                    ParseExpressionAxisCandidates(kvp.Value.Fragment),
                    kvp.Value,
                    seedKey);
            }

            // Resolve axis-by-axis in canonical order. Missing axes drop.
            // Collect as (axis, value) pairs first so we can run conflict
            // resolution across all picked values before emitting strings.
            var pickedPairs = new List<AttributedTextingStyleLine>();

            // Syntax axes — read from the slot's item, if equipped.
            // Order slot-to-axis mappings canonically/deterministically to ensure stable evaluation sequence across different environments and sessions.
            var canonicalAxisList = CanonicalAxisOrder.ToList();
            var orderedSlotToSyntaxAxis = SlotToSyntaxAxis
                .OrderBy(kv => canonicalAxisList.IndexOf(kv.Value))
                .ThenBy(kv => kv.Key, StringComparer.Ordinal);

            foreach (var kv in orderedSlotToSyntaxAxis)
            {
                string slot = kv.Key;
                string axis = kv.Value;
                if (!bySlot.TryGetValue(slot, out var src)) continue;
                var syntax = SelectAxisCandidates(
                    ParseSyntaxAxisCandidates(src.Fragment),
                    src,
                    seedKey);
                if (syntax.TryGetValue(axis, out var line) && !string.IsNullOrWhiteSpace(line))
                {
                    pickedPairs.Add(new AttributedTextingStyleLine(
                        axis, line, src.Source, src.Kind,
                        src.SourceId, src.SlotOrParameter, src.BandIndex));
                }
            }

            // Expression axes — majority vote per group.
            var directnessResult = MajorityVote("directness", DirectnessGroup, expressionByParam);
            var affectResult     = MajorityVote("affect",     AffectGroup,     expressionByParam);
            var rhythmResult     = MajorityVote("rhythm",     RhythmGroup,     expressionByParam);

            if (directnessResult != null && anatomyByParam.TryGetValue(directnessResult.ParamId, out var directnessSrc))
            {
                var pair = AxisValuePairOf(directnessResult.WinnerLine);
                pickedPairs.Add(new AttributedTextingStyleLine(
                    pair.axis, pair.value, directnessSrc.Source, directnessSrc.Kind,
                    directnessSrc.SourceId, directnessSrc.SlotOrParameter, directnessSrc.BandIndex));
            }
            if (affectResult != null && anatomyByParam.TryGetValue(affectResult.ParamId, out var affectSrc))
            {
                var pair = AxisValuePairOf(affectResult.WinnerLine);
                pickedPairs.Add(new AttributedTextingStyleLine(
                    pair.axis, pair.value, affectSrc.Source, affectSrc.Kind,
                    affectSrc.SourceId, affectSrc.SlotOrParameter, affectSrc.BandIndex));
            }
            if (rhythmResult != null && anatomyByParam.TryGetValue(rhythmResult.ParamId, out var rhythmSrc))
            {
                var pair = AxisValuePairOf(rhythmResult.WinnerLine);
                pickedPairs.Add(new AttributedTextingStyleLine(
                    pair.axis, pair.value, rhythmSrc.Source, rhythmSrc.Kind,
                    rhythmSrc.SourceId, rhythmSrc.SlotOrParameter, rhythmSrc.BandIndex));
            }

            // ------------------------------------------------------------------
            // #907: Conflict resolution.
            //
            // Walk the picked set; on conflict, drop the LATER-picked value
            // (the one that conflicts with an already-kept earlier value).
            // The resolver is O(n²) over the picked set — fine for n ≤ 9.
            // ------------------------------------------------------------------
            var kept  = new List<AttributedTextingStyleLine>(pickedPairs.Count);
            var drops = new List<ConflictDropEntry>();

            foreach (var candidate in pickedPairs)
            {
                string? conflictReason = null;
                AttributedTextingStyleLine? conflictKept = null;

                foreach (var alreadyKept in kept)
                {
                    var reason = conflicts.GetReason((alreadyKept.Axis, alreadyKept.Value), (candidate.Axis, candidate.Value));
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
                        axis:         candidate.Axis,
                        droppedValue: candidate.Value,
                        conflictAxis: conflictKept.Axis,
                        keptValue:    conflictKept.Value,
                        reason:       conflictReason));
                    // Do NOT add to kept — this axis is silenced for this character.
                }
                else
                {
                    kept.Add(candidate);
                }
            }

            // Re-order to match the canonical sequence.
            var orderedKept = kept
                .OrderBy(line => canonicalAxisList.IndexOf(line.Axis))
                .ToList();

            var result = orderedKept
                .Select(p => $"{p.Axis}: {p.Value}")
                .ToList();

            return new AggregationResult(result, drops, orderedKept);
        }
    }
}
