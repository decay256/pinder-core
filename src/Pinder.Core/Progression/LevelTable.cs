namespace Pinder.Core.Progression
{
    /// <summary>
    /// XP thresholds and level bonuses. Matches rules-v3.md §10.
    /// Players spend build points to choose WHICH stats to upgrade — this table
    /// only tracks the level bonus (added to every roll) and build points granted.
    /// </summary>
    public static class LevelTable
    {
        // XP required to REACH each level (0-indexed, level = index + 1)
        private static readonly int[] XpThresholds =
        {
            0,    // L1
            50,   // L2
            150,  // L3
            300,  // L4
            500,  // L5
            750,  // L6
            1100, // L7
            1500, // L8
            2000, // L9
            2750, // L10
            3500, // L11
        };

        // Build points granted on reaching this level (0-indexed, level = index + 1)
        // L1 grants 12 points at character creation (handled separately — see CharacterCreation)
        private static readonly int[] BuildPointsGranted =
        {
            0, // L1 (creation budget handled separately)
            2, // L2
            2, // L3
            2, // L4
            3, // L5
            3, // L6
            3, // L7
            4, // L8
            4, // L9
            5, // L10
            0, // L11+ — prestige resets to L1
        };

        // Flat bonus added to every d20 roll (0-indexed, level = index + 1)
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

        /// <summary>Maximum item slots at a given level (1-based).</summary>
        private static readonly int[] ItemSlots =
        {
            2, // L1
            2, // L2
            3, // L3
            3, // L4
            4, // L5
            4, // L6
            5, // L7
            5, // L8
            6, // L9
            6, // L10
            6, // L11+
        };

        /// <summary>Build points granted at character creation (before any levelling).</summary>
        public const int CreationBudget = 12;

        /// <summary>Maximum value for any single stat at character creation.</summary>
        public const int CreationStatCap = 4;

        /// <summary>Absolute maximum for any base stat (before gear).</summary>
        public const int BaseStatCap = 6;

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

        /// <summary>Build points granted upon reaching a given level (1-based). 0 for L1 (creation budget).</summary>
        public static int GetBuildPointsForLevel(int level)
        {
            int idx = System.Math.Max(0, System.Math.Min(level - 1, BuildPointsGranted.Length - 1));
            return BuildPointsGranted[idx];
        }

        /// <summary>Maximum item slots at a given level (1-based).</summary>
        public static int GetItemSlots(int level)
        {
            int idx = System.Math.Max(0, System.Math.Min(level - 1, ItemSlots.Length - 1));
            return ItemSlots[idx];
        }

        /// <summary>Failure tier pool for a given level.</summary>
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
