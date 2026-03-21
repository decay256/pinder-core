namespace Pinder.Core.Progression
{
    /// <summary>
    /// XP thresholds and level bonuses.
    /// Level bonus is added to every d20 roll.
    /// Failure tier pool (Basic / Intermediate / Advanced / Legendary) is also level-gated.
    /// </summary>
    public static class LevelTable
    {
        // XP required to reach each level (level = index)
        private static readonly int[] XpThresholds =
        {
            0,    // L1
            100,  // L2
            250,  // L3
            450,  // L4
            700,  // L5
            1000, // L6
            1350, // L7
            1750, // L8
            2200, // L9
            2700, // L10
            3250, // L11
        };

        // Level bonus added to all rolls
        private static readonly int[] LevelBonuses =
        {
            0, // L1
            0, // L2
            1, // L3
            1, // L4
            2, // L5
            2, // L6
            3, // L7
            3, // L8
            4, // L9
            4, // L10
            5, // L11+
        };

        /// <summary>Resolve 1-based level from raw XP.</summary>
        public static int GetLevel(int xp)
        {
            int level = 1;
            for (int i = XpThresholds.Length - 1; i >= 0; i--)
            {
                if (xp >= XpThresholds[i])
                {
                    level = i + 1;
                    break;
                }
            }
            return level;
        }

        /// <summary>Roll bonus for a given level (1-based).</summary>
        public static int GetBonus(int level)
        {
            int idx = System.Math.Max(0, System.Math.Min(level - 1, LevelBonuses.Length - 1));
            return LevelBonuses[idx];
        }

        /// <summary>Failure tier pool tier for a given level.</summary>
        public static FailurePoolTier GetFailurePoolTier(int level)
        {
            if (level >= 10) return FailurePoolTier.Legendary;
            if (level >= 7)  return FailurePoolTier.Advanced;
            if (level >= 4)  return FailurePoolTier.Intermediate;
            return FailurePoolTier.Basic;
        }
    }

    public enum FailurePoolTier
    {
        Basic,
        Intermediate,
        Advanced,
        Legendary
    }
}
