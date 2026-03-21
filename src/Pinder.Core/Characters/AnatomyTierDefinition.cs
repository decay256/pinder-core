using System.Collections.Generic;
using Pinder.Core.Stats;

namespace Pinder.Core.Characters
{
    /// <summary>
    /// One selectable tier within an anatomy parameter (e.g. Length=Short).
    /// Produces the same six fragment types as an item.
    /// </summary>
    public sealed class AnatomyTierDefinition
    {
        public string ParameterId { get; }
        public string TierId      { get; }
        public string TierName    { get; }

        /// <summary>Flat bonuses/penalties to base stats. Empty for cosmetic-only tiers.</summary>
        public IReadOnlyDictionary<StatType, int> StatModifiers { get; }

        /// <summary>Null for cosmetic-only tiers (e.g. Skin Tone).</summary>
        public string? PersonalityFragment   { get; }
        public string? BackstoryFragment     { get; }
        public string? TextingStyleFragment  { get; }
        public string[] ArchetypeTendencies  { get; }
        public TimingModifier ResponseTimingModifier { get; }

        /// <summary>Set for cosmetic-only tiers (Skin Tone). Null for all others.</summary>
        public string? VisualDescription { get; }

        public AnatomyTierDefinition(
            string parameterId,
            string tierId,
            string tierName,
            IReadOnlyDictionary<StatType, int> statModifiers,
            string? personalityFragment,
            string? backstoryFragment,
            string? textingStyleFragment,
            string[] archetypeTendencies,
            TimingModifier responseTimingModifier,
            string? visualDescription = null)
        {
            ParameterId             = parameterId;
            TierId                  = tierId;
            TierName                = tierName;
            StatModifiers           = statModifiers;
            PersonalityFragment     = personalityFragment;
            BackstoryFragment       = backstoryFragment;
            TextingStyleFragment    = textingStyleFragment;
            ArchetypeTendencies     = archetypeTendencies;
            ResponseTimingModifier  = responseTimingModifier;
            VisualDescription       = visualDescription;
        }
    }
}
