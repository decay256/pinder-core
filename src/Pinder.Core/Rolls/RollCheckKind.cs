namespace Pinder.Core.Rolls
{
    /// <summary>
    /// Identifies which check kind was performed via <see cref="RollEngine.ResolveCheck"/>.
    /// </summary>
    public enum RollCheckKind
    {
        /// <summary>Main option-roll check (d20 + stat mod + level vs datee DC).</summary>
        OptionRoll,

        /// <summary>Steering roll that attempts to append a date-steering question.</summary>
        Steering,

        /// <summary>Per-turn horniness overlay check.</summary>
        Horniness,

        /// <summary>Paired-shadow check that may apply corruption to the delivered message.</summary>
        Shadow,

        /// <summary>Shadow-growth check (reserved; no standalone d20 roll currently).</summary>
        ShadowGrowth,
    }
}
