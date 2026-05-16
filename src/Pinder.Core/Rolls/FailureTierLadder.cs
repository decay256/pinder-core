namespace Pinder.Core.Rolls
{
    /// <summary>
    /// Single source of truth for mapping a miss-margin to a <see cref="FailureTier"/>.
    /// All check engines must call <see cref="FromMissMargin"/> instead of re-implementing
    /// the ladder inline.
    /// </summary>
    /// <remarks>
    /// The <c>Legendary</c> tier (nat-1 in the main option-roll) is NOT produced here —
    /// it is a game-rule special case handled by <see cref="RollEngine.ResolveFromComponents"/>.
    /// This ladder covers the miss-margin–driven tiers only: Fumble, Misfire, TropeTrap, Catastrophe.
    /// </remarks>
    public static class FailureTierLadder
    {
        /// <summary>
        /// Maps a miss margin to a <see cref="FailureTier"/>.
        /// Returns <see cref="FailureTier.None"/> when <paramref name="missMargin"/> ≤ 0 (success).
        /// </summary>
        public static FailureTier FromMissMargin(int missMargin)
        {
            if (missMargin <= 0) return FailureTier.None;
            if (missMargin <= 2) return FailureTier.Fumble;
            if (missMargin <= 5) return FailureTier.Misfire;
            if (missMargin <= 9) return FailureTier.TropeTrap;
            return FailureTier.Catastrophe;
        }
    }
}
