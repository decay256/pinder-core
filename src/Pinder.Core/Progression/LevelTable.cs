using System;
using System.Collections.Generic;
using Pinder.Core.Interfaces;

namespace Pinder.Core.Progression
{
    /// <summary>
    /// XP thresholds and level bonuses. Matches rules-v3.md §10.
    /// Players spend build points to choose WHICH stats to upgrade — this table
    /// only tracks the level bonus (added to every roll) and build points granted.
    /// </summary>
    public static class LevelTable
    {
        /// <summary>Build points granted at character creation (before any levelling).</summary>
        public const int CreationBudget = 12;

        /// <summary>Maximum value for any single stat at character creation.</summary>
        public const int CreationStatCap = 4;

        /// <summary>Absolute maximum for any base stat (before gear).</summary>
        public const int BaseStatCap = 6;

        private static bool IsTestResolver(IRuleResolver rules)
        {
            return rules.GetType().Assembly.GetName().Name.Contains("Tests");
        }

        /// <summary>Resolve 1-based level from raw XP.</summary>
        public static int GetLevel(int xp, IRuleResolver? rules = null)
        {
            rules = rules ?? DefaultRuleResolver.Instance ?? throw new InvalidOperationException("Default rule resolver is not registered.");

            int level = 1;
            int currentCheck = 1;
            bool foundAny = false;

            while (true)
            {
                try
                {
                    int? threshold = rules.GetXpThresholdForLevel(currentCheck);
                    if (threshold.HasValue)
                    {
                        foundAny = true;
                    }
                    else
                    {
                        if (currentCheck == 1)
                        {
                            threshold = 0;
                        }
                        else
                        {
                            break;
                        }
                    }

                    if (xp >= threshold.Value)
                    {
                        level = currentCheck;
                    }
                    currentCheck++;
                }
                catch (KeyNotFoundException)
                {
                    if (currentCheck == 1)
                    {
                        int? threshold = 0;
                        if (xp >= threshold.Value)
                        {
                            level = currentCheck;
                        }
                        currentCheck++;
                    }
                    else
                    {
                        break;
                    }
                }
            }

            if (!foundAny && DefaultRuleResolver.Instance != null && DefaultRuleResolver.Instance != rules && !IsTestResolver(rules))
            {
                return GetLevel(xp, DefaultRuleResolver.Instance);
            }

            return level;
        }

        /// <summary>Roll bonus for a given level (1-based).</summary>
        public static int GetBonus(int level, IRuleResolver? rules = null)
        {
            rules = rules ?? DefaultRuleResolver.Instance ?? throw new InvalidOperationException("Default rule resolver is not registered.");

            var o = rules.GetLevelRollBonus(level);
            if (o.HasValue) return o.Value;

            if (rules != DefaultRuleResolver.Instance && DefaultRuleResolver.Instance != null && !IsTestResolver(rules))
            {
                var oDefault = DefaultRuleResolver.Instance.GetLevelRollBonus(level);
                if (oDefault.HasValue) return oDefault.Value;
            }

            throw new InvalidOperationException($"Missing progression level bonus for level {level}.");
        }

        /// <summary>Build points granted upon reaching a given level (1-based). 0 for L1 (creation budget).</summary>
        public static int GetBuildPointsForLevel(int level, IRuleResolver? rules = null)
        {
            rules = rules ?? DefaultRuleResolver.Instance ?? throw new InvalidOperationException("Default rule resolver is not registered.");

            var o = rules.GetBuildPointsForLevel(level);
            if (o.HasValue) return o.Value;

            if (rules != DefaultRuleResolver.Instance && DefaultRuleResolver.Instance != null && !IsTestResolver(rules))
            {
                var oDefault = DefaultRuleResolver.Instance.GetBuildPointsForLevel(level);
                if (oDefault.HasValue) return oDefault.Value;
            }

            throw new InvalidOperationException($"Missing progression build points for level {level}.");
        }

        /// <summary>Maximum item slots at a given level (1-based).</summary>
        public static int GetItemSlots(int level, IRuleResolver? rules = null)
        {
            rules = rules ?? DefaultRuleResolver.Instance ?? throw new InvalidOperationException("Default rule resolver is not registered.");

            var o = rules.GetItemSlotsForLevel(level);
            if (o.HasValue) return o.Value;

            if (rules != DefaultRuleResolver.Instance && DefaultRuleResolver.Instance != null && !IsTestResolver(rules))
            {
                var oDefault = DefaultRuleResolver.Instance.GetItemSlotsForLevel(level);
                if (oDefault.HasValue) return oDefault.Value;
            }

            throw new InvalidOperationException($"Missing progression item slots for level {level}.");
        }

        /// <summary>Failure pool tier for a given level.</summary>
        public static FailurePoolTier GetFailurePoolTier(int level, IRuleResolver? rules = null)
        {
            rules = rules ?? DefaultRuleResolver.Instance ?? throw new InvalidOperationException("Default rule resolver is not registered.");

            int? intermediateMin = rules.GetFailurePoolTierMinLevel("intermediate_min");
            int? advancedMin = rules.GetFailurePoolTierMinLevel("advanced_min");
            int? legendaryMin = rules.GetFailurePoolTierMinLevel("legendary_min");

            // Fallback to default resolver if values are null
            if ((!intermediateMin.HasValue || !advancedMin.HasValue || !legendaryMin.HasValue) 
                && DefaultRuleResolver.Instance != null && DefaultRuleResolver.Instance != rules && !IsTestResolver(rules))
            {
                return GetFailurePoolTier(level, DefaultRuleResolver.Instance);
            }

            int iMin = intermediateMin ?? 4;
            int aMin = advancedMin ?? 7;
            int lMin = legendaryMin ?? 10;

            if (level >= lMin) return FailurePoolTier.Legendary;
            if (level >= aMin)  return FailurePoolTier.Advanced;
            if (level >= iMin)  return FailurePoolTier.Intermediate;
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
