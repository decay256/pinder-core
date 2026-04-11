namespace Pinder.Core.Rolls
{
    /// <summary>
    /// Risk tier based on the "need" value (dc - statMod - levelBonus).
    /// Rules v3.4 §5.
    /// </summary>
    public enum RiskTier
    {
        /// <summary>Need 1–7: ~70-100% success. +1 Interest bonus on success.</summary>
        Safe,

        /// <summary>Need 8–11: ~50-65% success. +2 Interest bonus on success.</summary>
        Medium,

        /// <summary>Need 12–15: ~30-45% success. +3 Interest bonus on success.</summary>
        Hard,

        /// <summary>Need 16–19: ~10-25% success. +5 Interest bonus on success.</summary>
        Bold,

        /// <summary>Need 20+: ~0-5% success (Nat 20 only). +10 Interest bonus on success.</summary>
        Reckless
    }
}
