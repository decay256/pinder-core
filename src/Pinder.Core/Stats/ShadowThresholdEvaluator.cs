namespace Pinder.Core.Stats
{
    /// <summary>
    /// Pure static utility: given a shadow value, returns the threshold tier (0/1/2/3).
    /// Thresholds: 6=T1, 12=T2, 18+=T3.
    /// </summary>
    public static class ShadowThresholdEvaluator
    {
        /// <summary>
        /// Returns the threshold level for the given shadow value.
        /// 0 = no threshold reached, 1 = T1 (≥6), 2 = T2 (≥12), 3 = T3 (≥18).
        /// </summary>
        public static int GetThresholdLevel(int shadowValue)
        {
            if (shadowValue >= 18) return 3;
            if (shadowValue >= 12) return 2;
            if (shadowValue >= 6) return 1;
            return 0;
        }
    }
}
