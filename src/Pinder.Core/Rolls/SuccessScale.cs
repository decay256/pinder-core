namespace Pinder.Core.Rolls
{
    /// <summary>
    /// Maps roll success margin to interest delta. Rules-v3.md §5.
    /// Beat DC by 1–4  → +1
    /// Beat DC by 5–9  → +2
    /// Beat DC by 10+  → +3 (Crit)
    /// Nat 20          → +4
    /// Failure         → 0
    /// </summary>
    public static class SuccessScale
    {
        /// <summary>
        /// Compute the interest delta for a successful roll.
        /// Returns 0 for failures.
        /// </summary>
        public static int GetInterestDelta(RollResult result)
        {
            if (!result.IsSuccess)
                return 0;

            if (result.IsNatTwenty)
                return 4;

            int margin = result.FinalTotal - result.DC;

            if (margin >= 10)
                return 3;
            if (margin >= 5)
                return 2;
            return 1;
        }
    }
}
