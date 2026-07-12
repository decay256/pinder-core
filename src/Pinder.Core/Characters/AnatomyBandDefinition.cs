using System;
using System.Collections.Generic;
using Pinder.Core.Stats;

namespace Pinder.Core.Characters
{
    /// <summary>
    /// One band within a scalar anatomy parameter. Defines the [Lower, Upper)
    /// range on the normalised [0..1] scale plus an optional modifier suite.
    ///
    /// As of issue #1175, bands replace the old discrete tier model. All
    /// fragment and modifier fields are optional/nullable; an empty band (no
    /// personality, no stat mods, etc.) is valid and silently contributes
    /// nothing to the assembled character.
    ///
    /// Band-coverage contract: for a parameter with N bands the last band's
    /// upper bound is 1.0 and is inclusive; all other bands use a half-open
    /// [Lower, Upper) interval. Use <see cref="AnatomyParameterDefinition.ResolveBand"/>
    /// rather than testing bounds directly.
    /// </summary>
    public sealed class AnatomyBandDefinition
    {
        /// <summary>Lower bound of this band on the [0..1] scale (inclusive).</summary>
        public float Lower { get; }

        /// <summary>
        /// Upper bound of this band on the [0..1] scale. Exclusive on all
        /// bands except the last, which is inclusive of 1.0.
        /// </summary>
        public float Upper { get; }

        /// <summary>Null when this band has no personality contribution.</summary>
        public string? PersonalityFragment   { get; }

        /// <summary>Null when this band has no backstory contribution.</summary>
        public string? BackstoryFragment     { get; }

        /// <summary>Null when this band has no texting-style contribution.</summary>
        public string? TextingStyleFragment  { get; }

        /// <summary>User-visible one-line summary for displaying the resolved anatomy range.</summary>
        public string SummaryText { get; }

        /// <summary>Archetype tendency strings. Empty array when not set.</summary>
        public string[] ArchetypeTendencies  { get; }

        /// <summary>Timing modifier. Defaults to <see cref="TimingModifier.Zero"/> when not set.</summary>
        public TimingModifier ResponseTimingModifier { get; }

        /// <summary>
        /// Flat stat bonuses/penalties. Empty when not set.
        /// </summary>
        public IReadOnlyDictionary<StatType, int> StatModifiers { get; }

        public AnatomyBandDefinition(
            float lower,
            float upper,
            string? personalityFragment,
            string? backstoryFragment,
            string? textingStyleFragment,
            string[] archetypeTendencies,
            TimingModifier responseTimingModifier,
            IReadOnlyDictionary<StatType, int> statModifiers,
            string summaryText = "")
        {
            Lower                   = lower;
            Upper                   = upper;
            PersonalityFragment     = personalityFragment;
            BackstoryFragment       = backstoryFragment;
            TextingStyleFragment    = textingStyleFragment;
            SummaryText             = summaryText ?? string.Empty;
            ArchetypeTendencies     = archetypeTendencies     ?? Array.Empty<string>();
            ResponseTimingModifier  = responseTimingModifier  ?? TimingModifier.Zero;
            StatModifiers           = statModifiers           ?? new Dictionary<StatType, int>();
        }
    }
}
