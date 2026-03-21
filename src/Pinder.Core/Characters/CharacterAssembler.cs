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
    /// anatomy selections, base stats, and shadow stats.
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
        /// Missing item IDs and anatomy parameters are silently skipped.
        /// </summary>
        /// <param name="equippedItemIds">Item IDs of all equipped items.</param>
        /// <param name="anatomySelections">Map of parameterId → tierId (e.g. "length" → "short").</param>
        /// <param name="playerBaseStats">Player-authored base stat values.</param>
        /// <param name="shadowStats">Current shadow stat values.</param>
        public FragmentCollection Assemble(
            IEnumerable<string> equippedItemIds,
            IReadOnlyDictionary<string, string> anatomySelections,
            IReadOnlyDictionary<StatType, int> playerBaseStats,
            IReadOnlyDictionary<ShadowStatType, int> shadowStats)
        {
            // --- 1. Resolve items and anatomy tiers --------------------------------

            var resolvedItems = new List<ItemDefinition>();
            foreach (var id in equippedItemIds)
            {
                var item = _items.GetItem(id);
                if (item != null) resolvedItems.Add(item);
            }

            var resolvedTiers = new List<AnatomyTierDefinition>();
            foreach (var kv in anatomySelections)
            {
                var param = _anatomy.GetParameter(kv.Key);
                if (param == null) continue;
                var tier = param.GetTier(kv.Value);
                if (tier != null) resolvedTiers.Add(tier);
            }

            // --- 2. Sum stat modifiers on top of player base stats -----------------

            var statSums = new Dictionary<StatType, int>();
            foreach (StatType st in Enum.GetValues(typeof(StatType)))
            {
                playerBaseStats.TryGetValue(st, out int baseVal);
                statSums[st] = baseVal;
            }

            foreach (var item in resolvedItems)
                foreach (var kv in item.StatModifiers)
                    statSums[kv.Key] = statSums[kv.Key] + kv.Value;

            foreach (var tier in resolvedTiers)
                foreach (var kv in tier.StatModifiers)
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

            foreach (var tier in resolvedTiers)
                ApplyTiming(tier.ResponseTimingModifier);

            var timingProfile = new TimingProfile(
                Math.Max(0, totalDelayDelta),
                totalVariance,
                Math.Min(1f, Math.Max(0f, totalDrySpell)),
                finalReceipt);

            // --- 5. Concat fragments -----------------------------------------------

            var personality  = new List<string>();
            var backstory    = new List<string>();
            var texting      = new List<string>();
            var allArchetypes = new List<string>();

            void AddFragments(
                string? pf, string? bf, string? tf, string[] archetypes)
            {
                if (!string.IsNullOrEmpty(pf)) personality.Add(pf!);
                if (!string.IsNullOrEmpty(bf)) backstory.Add(bf!);
                if (!string.IsNullOrEmpty(tf)) texting.Add(tf!);
                allArchetypes.AddRange(archetypes);
            }

            foreach (var item in resolvedItems)
                AddFragments(item.PersonalityFragment, item.BackstoryFragment,
                             item.TextingStyleFragment, item.ArchetypeTendencies);

            foreach (var tier in resolvedTiers)
                AddFragments(tier.PersonalityFragment, tier.BackstoryFragment,
                             tier.TextingStyleFragment, tier.ArchetypeTendencies);

            // --- 6. Count and rank archetypes -------------------------------------

            var archetypeCount = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var a in allArchetypes)
            {
                archetypeCount.TryGetValue(a, out int c);
                archetypeCount[a] = c + 1;
            }

            var ranked = archetypeCount
                .OrderByDescending(kv => kv.Value)
                .Select(kv => (kv.Key, kv.Value))
                .ToList();

            // --- 7. Return FragmentCollection -------------------------------------

            return new FragmentCollection(
                personality.AsReadOnly(),
                backstory.AsReadOnly(),
                texting.AsReadOnly(),
                ranked.AsReadOnly(),
                timingProfile,
                statBlock);
        }
    }
}
