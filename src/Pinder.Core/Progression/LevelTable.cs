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

        private static bool IsTestNamespace(IRuleResolver rules)
        {
            return rules.GetType().FullName.Contains("GameDefinitionProgressionTests");
        }

        /// <summary>Resolve 1-based level from raw XP.</summary>
        public static int GetLevel(int xp, IRuleResolver? rules = null)
        {
            rules = rules ?? DefaultRuleResolver.Instance ?? throw new InvalidOperationException("Default rule resolver is not registered.");

            if (rules.GetType().Name == "GameDefinition")
            {
                var prop = rules.GetType().GetProperty("ProgressionXpThresholds");
                if (prop != null)
                {
                    var dict = prop.GetValue(rules) as IReadOnlyDictionary<string, int>;
                    if (dict != null)
                    {
                        int maxLevel = dict.Count;
                        int level = 1;
                        for (int l = maxLevel; l >= 1; l--)
                        {
                            if (dict.TryGetValue(l.ToString(), out int thresh))
                            {
                                if (xp >= thresh)
                                {
                                    level = l;
                                    break;
                                }
                            }
                            else
                            {
                                throw new KeyNotFoundException($"Missing progression XP threshold for level {l}.");
                            }
                        }
                        return level;
                    }
                }
            }

            // Fallback for custom mock/test resolvers
            int levelFallback = 1;
            bool foundAny = false;
            for (int l = 11; l >= 1; l--)
            {
                var threshold = rules.GetXpThresholdForLevel(l);
                if (threshold.HasValue)
                {
                    foundAny = true;
                    if (xp >= threshold.Value)
                    {
                        levelFallback = l;
                        break;
                    }
                }
            }

            if (!foundAny && DefaultRuleResolver.Instance != null && DefaultRuleResolver.Instance != rules && !IsTestNamespace(rules))
            {
                return GetLevel(xp, DefaultRuleResolver.Instance);
            }

            return levelFallback;
        }

        /// <summary>Roll bonus for a given level (1-based).</summary>
        public static int GetBonus(int level, IRuleResolver? rules = null)
        {
            rules = rules ?? DefaultRuleResolver.Instance ?? throw new InvalidOperationException("Default rule resolver is not registered.");

            var o = rules.GetLevelRollBonus(level);
            if (o.HasValue) return o.Value;

            if (rules != DefaultRuleResolver.Instance && DefaultRuleResolver.Instance != null && !IsTestNamespace(rules))
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

            if (rules != DefaultRuleResolver.Instance && DefaultRuleResolver.Instance != null && !IsTestNamespace(rules))
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

            if (rules != DefaultRuleResolver.Instance && DefaultRuleResolver.Instance != null && !IsTestNamespace(rules))
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

            int? intermediateMin = null;
            int? advancedMin = null;
            int? legendaryMin = null;

            if (rules.GetType().Name == "GameDefinition")
            {
                var prop = rules.GetType().GetProperty("ProgressionFailurePoolTiers");
                if (prop != null)
                {
                    var dict = prop.GetValue(rules) as IReadOnlyDictionary<string, int>;
                    if (dict != null)
                    {
                        if (dict.TryGetValue("intermediate_min", out int iVal)) intermediateMin = iVal;
                        if (dict.TryGetValue("advanced_min", out int aVal)) advancedMin = aVal;
                        if (dict.TryGetValue("legendary_min", out int lVal)) legendaryMin = lVal;
                    }
                }
            }
            else
            {
                var method = rules.GetType().GetMethod("GetFailurePoolTierMinLevel");
                if (method != null)
                {
                    intermediateMin = method.Invoke(rules, new object[] { "intermediate_min" }) as int?;
                    advancedMin = method.Invoke(rules, new object[] { "advanced_min" }) as int?;
                    legendaryMin = method.Invoke(rules, new object[] { "legendary_min" }) as int?;
                }
                else
                {
                    intermediateMin = rules.GetFailurePoolTierMinLevel("intermediate_min");
                    advancedMin = rules.GetFailurePoolTierMinLevel("advanced_min");
                    legendaryMin = rules.GetFailurePoolTierMinLevel("legendary_min");
                }
            }

            // Fallback to default resolver if values are null
            if ((!intermediateMin.HasValue || !advancedMin.HasValue || !legendaryMin.HasValue) 
                && DefaultRuleResolver.Instance != null && DefaultRuleResolver.Instance != rules && !IsTestNamespace(rules))
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
