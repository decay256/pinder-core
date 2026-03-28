namespace Pinder.Core.Rolls
{
    /// <summary>
    /// Risk tier based on the "need" value (dc - statMod - levelBonus).
    /// Rules v3.4 §5.
    /// </summary>
    public enum RiskTier
    {
        /// <summary>Need ≤ 5: easy roll, no bonus.</summary>
        Safe,

        /// <summary>Need 6–10: moderate roll, no bonus.</summary>
        Medium,

        /// <summary>Need 11–15: risky roll, +1 Interest bonus on success.</summary>
        Hard,

        /// <summary>Need ≥ 16: very risky roll, +2 Interest bonus on success.</summary>
        Bold
    }
}
