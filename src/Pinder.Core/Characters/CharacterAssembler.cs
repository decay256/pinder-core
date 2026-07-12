using System;
using System.Collections.Generic;
using System.Linq;
using Pinder.Core.Conversation;
using Pinder.Core.Interfaces;
using Pinder.Core.Stats;

namespace Pinder.Core.Characters
{
    /// <summary>
    /// Assembles a <see cref="FragmentCollection"/> from a character's equipped items,
    /// anatomy scalar values, base stats, and shadow stats.
    ///
    /// As of issue #1175, anatomy is expressed as a
    /// <c>IReadOnlyDictionary&lt;string,float&gt;</c> (parameter id → normalised
    /// [0..1] value). For each parameter the assembler calls
    /// <see cref="AnatomyParameterDefinition.ResolveBand"/> to find the matching
    /// band and then applies that band's fragment suite exactly as the old tier
    /// system did. The #836 texting-style param-id handle is preserved:
    /// anatomy contributions are still keyed by parameter id in the
    /// <see cref="TextingStyleFragmentSource.SlotOrParameter"/> field.
    ///
    /// As of issue #1176:
    /// - Item ids are Unity-verbatim (e.g. "head_tophat", "vest1", "classic2").
    /// - Unknown item ids (no core definition) → zero modifiers, id collected
    ///   in <see cref="FragmentCollection.UnknownItemIds"/> for admin authoring.
    ///   Player flow never hard-fails.
    /// </summary>
    public sealed class CharacterAssembler
    {
        private readonly IItemRepository    _items;
        private readonly IAnatomyRepository _anatomy;

        public CharacterAssembler(IItemRepository items, IAnatomyRepository anatomy)
        {
            _items   = items   ?? throw new ArgumentNullException(nameof(items));
            _anatomy = anatomy ?? throw new ArgumentNullException(nameof(anatomy));
        }

        /// <summary>
        /// Run the full assembly pipeline.
        /// Unknown item IDs resolve to zero modifiers; unknown anatomy parameters
        /// are silently skipped.
        /// </summary>
        /// <param name="equippedItemIds">Item IDs of all equipped items.</param>
        /// <param name="anatomyValues">
        /// Map of parameterId → normalised float value [0..1]
        /// (e.g. "trunkLengthBase" → 0.42).
        /// </param>
        /// <param name="playerBaseStats">Player-authored base stat values.</param>
        /// <param name="shadowStats">Current shadow stat values.</param>
        /// <param name="characterLevel">
        /// Character level used to filter archetypes by their eligible level range.
        /// When 0 (default), no level filtering is applied (backward-compatible).
        /// </param>
        /// <param name="archetypesEnabled">Whether archetype selection/resolution is enabled.</param>
        public FragmentCollection Assemble(
            IEnumerable<string> equippedItemIds,
            IReadOnlyDictionary<string, float> anatomyValues,
            IReadOnlyDictionary<StatType, int> playerBaseStats,
            IReadOnlyDictionary<ShadowStatType, int> shadowStats,
            int characterLevel = 0,
            bool archetypesEnabled = false)
        {
            // --- 1. Resolve items, track unknowns ---

            var unknownIds    = new List<string>();
            var resolvedItems = new List<ItemDefinition>();

            foreach (var id in equippedItemIds)
            {
                var item = _items.GetItem(id);
                if (item != null)
                {
                    resolvedItems.Add(item);
                }
                else
                {
                    // Unknown id: record for admin surfacing; zero modifiers applied.
                    unknownIds.Add(id);
                }
            }

            var resolvedBands = new List<(string ParamId, int BandIndex, AnatomyBandDefinition Band)>();
            foreach (var kv in anatomyValues)
            {
                var param = _anatomy.GetParameter(kv.Key);
                if (param == null) continue;
                var band = param.ResolveBand(kv.Value);
                if (band != null)
                {
                    int bandIndex = 0;
                    for (int i = 0; i < param.Bands.Count; i++)
                    {
                        if (ReferenceEquals(param.Bands[i], band))
                        {
                            bandIndex = i;
                            break;
                        }
                    }
                    resolvedBands.Add((kv.Key, bandIndex, band));
                }
            }

            // --- 2. Sum stat modifiers on top of player base stats -----------------
            // Stat modifiers apply to ALL resolved items.

            var statSums = new Dictionary<StatType, int>();
            foreach (StatType st in Enum.GetValues(typeof(StatType)))
            {
                playerBaseStats.TryGetValue(st, out int baseVal);
                statSums[st] = baseVal;
            }

            foreach (var item in resolvedItems)
                foreach (var kv in item.StatModifiers)
                    statSums[kv.Key] = statSums[kv.Key] + kv.Value;

            foreach (var (_, _, band) in resolvedBands)
                foreach (var kv in band.StatModifiers)
                    statSums[kv.Key] = statSums[kv.Key] + kv.Value;

            // --- 3. Build StatBlock ------------------------------------------------

            var shadowDict = new Dictionary<ShadowStatType, int>();
            foreach (ShadowStatType sst in Enum.GetValues(typeof(ShadowStatType)))
            {
                shadowStats.TryGetValue(sst, out int sv);
                shadowDict[sst] = sv;
            }

            var statBlock = new StatBlock(statSums, shadowDict);

            // --- 4. Sum timing modifiers -------------------------------------------
            // Base: delay=0, variance=1.0f, drySpell=0f, readReceipt="neutral"
            // delayDelta is additive; varianceMult is multiplicative;
            // drySpellDelta is additive; last non-neutral readReceipt wins.

            int   totalDelayDelta = 0;
            float totalVariance   = 1.0f;
            float totalDrySpell   = 0f;
            string finalReceipt   = "neutral";

            void ApplyTiming(TimingModifier tm)
            {
                totalDelayDelta += tm.BaseDelayDeltaMinutes;
                totalVariance   *= tm.DelayVarianceMultiplier;
                totalDrySpell   += tm.DrySpellProbabilityDelta;
                if (tm.ReadReceipt != "neutral")
                    finalReceipt = tm.ReadReceipt;
            }

            foreach (var item in resolvedItems)
                ApplyTiming(item.ResponseTimingModifier);

            foreach (var (_, _, band) in resolvedBands)
                ApplyTiming(band.ResponseTimingModifier);

            var timingProfile = new TimingProfile(
                Math.Max(0, totalDelayDelta),
                totalVariance,
                Math.Min(1f, Math.Max(0f, totalDrySpell)),
                finalReceipt);

            // --- 5. Concat fragments -----------------------------------------------
            // Stat modifiers (above) were already applied to all items.

            var personality  = new List<string>();
            var backstory    = new List<string>();
            var texting      = new List<string>();
            var textingSources = new List<TextingStyleFragmentSource>();
            var allArchetypes = new List<string>();

            // Issue #404: AddFragments now also captures the (kind, sourceName)
            // for each non-empty texting fragment so the Character Sheet
            // 'Texting Style' tab can render a per-source breakdown without
            // re-deriving from item / anatomy definitions on the controller side.
            void AddFragments(
                string? pf, string? bf, string? tf, string[] archetypes,
                string textingKind, string textingSourceName,
                string? slotOrParameter,
                string? sourceId,
                int? bandIndex)
            {
                if (!string.IsNullOrEmpty(pf)) personality.Add(pf!);
                if (!string.IsNullOrEmpty(bf)) backstory.Add(bf!);
                if (!string.IsNullOrEmpty(tf))
                {
                    texting.Add(tf!);
                    textingSources.Add(new TextingStyleFragmentSource(
                        textingKind, textingSourceName, tf!, slotOrParameter, sourceId, bandIndex));
                }
                allArchetypes.AddRange(archetypes);
            }

            // #836: thread the item slot (e.g. "Head", "Face", "Hair", "Tattoo") into
            // the per-source breakdown so the new aggregator can do the
            // slot → syntax-axis lookup without re-resolving the item.
            for (int i = 0; i < resolvedItems.Count; i++)
            {
                var item = resolvedItems[i];
                AddFragments(item.PersonalityFragment, item.BackstoryFragment,
                             item.TextingStyleFragment, item.ArchetypeTendencies,
                             "item", item.DisplayName, item.Slot, item.ItemId, null);
            }

            // #836: anatomy parameter id is the engine-side handle for
            // the param (e.g. "trunkLengthBase", "trunkGirth"); it's stable
            // and grouped by the aggregator's tone-axis groups.
            // #1175: band name is used as the source name for auditing.
            foreach (var (paramId, bandIndex, band) in resolvedBands)
            {
                // Build a descriptive source name from the param + band bounds
                string bandLabel = $"{paramId}[{band.Lower:F2},{band.Upper:F2})";
                AddFragments(band.PersonalityFragment, band.BackstoryFragment,
                             band.TextingStyleFragment, band.ArchetypeTendencies,
                             "anatomy", bandLabel, paramId, paramId, bandIndex);
            }

            // --- 6. Count and rank archetypes -------------------------------------
            // When characterLevel > 0, filter to archetypes whose level range
            // includes the character's level. Unknown archetypes (not in catalog)
            // are always kept. If no eligible archetypes remain, fall back to
            // the unfiltered list so the character always has archetype data.

            var archetypeCount = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var a in allArchetypes)
            {
                archetypeCount.TryGetValue(a, out int c);
                archetypeCount[a] = c + 1;
            }

            IEnumerable<KeyValuePair<string, int>> archetypesToRank = archetypeCount;

            if (characterLevel > 0)
            {
                var eligible = archetypeCount
                    .Where(kv => ArchetypeCatalog.IsEligibleAtLevel(kv.Key, characterLevel))
                    .ToList();

                if (eligible.Count > 0)
                    archetypesToRank = eligible;
                // else: fall back to unfiltered list
            }

            var ranked = archetypesToRank
                .OrderByDescending(kv => kv.Value)
                .Select(kv => (kv.Key, kv.Value))
                .ToList();

            // --- 7. Resolve active archetype ------------------------------------

            ActiveArchetype activeArchetype = archetypesEnabled ? ResolveActiveArchetype(ranked, characterLevel) : null;

            // --- 8. Return FragmentCollection -------------------------------------

            return new FragmentCollection(
                personality.AsReadOnly(),
                backstory.AsReadOnly(),
                texting.AsReadOnly(),
                ranked.AsReadOnly(),
                timingProfile,
                statBlock,
                activeArchetype,
                textingSources.AsReadOnly(),
                unknownIds.AsReadOnly());
        }

        /// <summary>
        /// Selects the active archetype from ranked archetypes based on character level.
        /// If characterLevel > 0, prefers the highest-count archetype whose level range
        /// includes the character's level. Falls back to highest-count overall.
        /// The total archetype-tendency vote count is propagated to
        /// <see cref="ActiveArchetype"/> so its <c>InterferenceLevel</c> reflects
        /// share-of-votes (#375), not raw count.
        /// </summary>
        internal static ActiveArchetype ResolveActiveArchetype(
            IReadOnlyList<(string Archetype, int Count)> ranked,
            int characterLevel)
        {
            if (ranked == null || ranked.Count == 0)
                return null;

            // Compute total archetype-tendency votes across the entire ranked
            // set so InterferenceLevel can be expressed as a share, not raw
            // count (#375).
            int totalCount = 0;
            for (int i = 0; i < ranked.Count; i++)
                totalCount += ranked[i].Count;

            // Try to find the highest-count archetype eligible at this level
            if (characterLevel > 0)
            {
                foreach (var entry in ranked)
                {
                    var def = ArchetypeCatalog.GetByName(entry.Archetype);
                    if (def != null && def.IsEligibleAtLevel(characterLevel))
                    {
                        string behavior = ArchetypeCatalog.GetBehavior(entry.Archetype);
                        return new ActiveArchetype(entry.Archetype, behavior, entry.Count, totalCount);
                    }
                }
            }

            // Fallback: use the highest-count archetype overall
            var top = ranked[0];
            string topBehavior = ArchetypeCatalog.GetBehavior(top.Archetype);
            return new ActiveArchetype(top.Archetype, topBehavior, top.Count, totalCount);
        }
    }
}
