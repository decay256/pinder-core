namespace Pinder.Core.Conversation
{
    /// <summary>
    /// One line in the itemized interest breakdown for a turn.
    /// The sum of all <see cref="Delta"/> values across the list equals
    /// <see cref="TurnResult.InterestDelta"/> (the sum invariant).
    /// </summary>
    public sealed class InterestBreakdownItem
    {
        /// <summary>
        /// Stable machine-readable key identifying the source of this delta.
        /// Defined values: base_roll, risk_tier, combo, shadow_misfire,
        /// horniness_trope_trap, active_trap_penalty.
        /// </summary>
        public string Source { get; }

        /// <summary>Human-readable label for display in the UI.</summary>
        public string Label { get; }

        /// <summary>Signed interest delta contributed by this source.</summary>
        public int Delta { get; }

        public InterestBreakdownItem(string source, string label, int delta)
        {
            Source = source;
            Label = label;
            Delta = delta;
        }
    }
}
