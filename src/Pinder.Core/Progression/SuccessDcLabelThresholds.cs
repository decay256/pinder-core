using System;

namespace Pinder.Core.Progression
{
    /// <summary>
    /// Configured DC cutoffs used to label successful roll XP ledger entries.
    /// </summary>
    public readonly struct SuccessDcLabelThresholds
    {
        public SuccessDcLabelThresholds(int lowMax, int midMax)
        {
            if (lowMax > midMax)
            {
                throw new ArgumentException("dc_low_max must be less than or equal to dc_mid_max.", nameof(lowMax));
            }

            LowMax = lowMax;
            MidMax = midMax;
        }

        public int LowMax { get; }
        public int MidMax { get; }
    }
}
