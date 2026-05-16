namespace Pinder.Core.Rolls
{
    /// <summary>
    /// Maps (FailureTier, RollCheckKind) → player-facing label.
    /// Decouples display from enum identity: TropeTrap activates a trap ONLY on option rolls;
    /// on other check kinds it's just "miss margin 6-9" and should not imply a trap fired.
    /// Wire DTOs, YAML keys, and the FailureTier enum itself are unchanged — only the
    /// human-readable string changes per kind.
    /// </summary>
    public static class FailureTierDisplay
    {
        /// <summary>
        /// Returns the player-facing label for a <see cref="FailureTier"/> in the context of a
        /// specific <see cref="RollCheckKind"/>.
        /// <para>
        /// <c>TropeTrap</c> is labelled <c>"TropeTrap"</c> on <c>OptionRoll</c> (the trap fires)
        /// and <c>"Severe"</c> on all other check kinds (no trap; scale-consistent label only).
        /// All other tiers pass through <c>tier.ToString()</c> regardless of kind.
        /// </para>
        /// </summary>
        public static string Label(FailureTier tier, RollCheckKind kind)
        {
            if (tier == FailureTier.TropeTrap && kind != RollCheckKind.OptionRoll)
                return "Severe";
            return tier.ToString();
        }
    }
}
