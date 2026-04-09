using System.Collections.Generic;
using Pinder.Core.Stats;

namespace Pinder.Core.Characters
{
    /// <summary>
    /// Immutable definition of a wearable item. Carries all six fragment types
    /// that feed the character-assembly pipeline.
    /// </summary>
    public sealed class ItemDefinition
    {
        public string ItemId       { get; }
        public string DisplayName   { get; }
        public string Slot          { get; }
        public string Tier          { get; }

        /// <summary>Flat bonuses/penalties to base stats.</summary>
        public IReadOnlyDictionary<StatType, int> StatModifiers { get; }

        public string PersonalityFragment    { get; }
        public string BackstoryFragment      { get; }
        public string TextingStyleFragment   { get; }
        public string[] ArchetypeTendencies  { get; }
        public TimingModifier ResponseTimingModifier { get; }

        public ItemDefinition(
            string itemId,
            string displayName,
            string slot,
            string tier,
            IReadOnlyDictionary<StatType, int> statModifiers,
            string personalityFragment,
            string backstoryFragment,
            string textingStyleFragment,
            string[] archetypeTendencies,
            TimingModifier responseTimingModifier)
        {
            ItemId                  = itemId;
            DisplayName             = displayName ?? itemId;
            Slot                    = slot;
            Tier                    = tier;
            StatModifiers           = statModifiers;
            PersonalityFragment     = personalityFragment;
            BackstoryFragment       = backstoryFragment;
            TextingStyleFragment    = textingStyleFragment;
            ArchetypeTendencies     = archetypeTendencies;
            ResponseTimingModifier  = responseTimingModifier;
        }
    }
}
