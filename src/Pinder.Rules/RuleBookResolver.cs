using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Pinder.Core.Conversation;
using Pinder.Core.Interfaces;
using Pinder.Core.Rolls;

namespace Pinder.Rules
{
    /// <summary>
    /// Implements IRuleResolver by evaluating conditions against
    /// loaded RuleBook entries. Loads YAML once at construction.
    /// Thread-safe after construction (RuleBook is immutable).
    /// </summary>
    public sealed class RuleBookResolver : IRuleResolver
    {
        private readonly List<RuleBook> _books;

        /// <summary>Create resolver from a single pre-loaded RuleBook.</summary>
        public RuleBookResolver(RuleBook rules)
        {
            _books = new List<RuleBook> { rules ?? throw new ArgumentNullException(nameof(rules)) };
        }

        /// <summary>
        /// Create resolver from one or more RuleBook instances.
        /// Rules from all books are searched in order.
        /// </summary>
        public RuleBookResolver(params RuleBook[] books)
        {
            if (books == null || books.Length == 0)
                throw new ArgumentException("At least one RuleBook is required.", nameof(books));
            _books = new List<RuleBook>(books);
        }

        /// <summary>
        /// Convenience factory: load YAML content strings and create a resolver.
        /// Throws FormatException on bad YAML.
        /// </summary>
        public static RuleBookResolver FromYaml(params string[] yamlContents)
        {
            if (yamlContents == null || yamlContents.Length == 0)
                throw new ArgumentException("At least one YAML content string is required.", nameof(yamlContents));

            var books = new RuleBook[yamlContents.Length];
            for (int i = 0; i < yamlContents.Length; i++)
            {
                books[i] = RuleBook.LoadFrom(yamlContents[i]);
            }
            return new RuleBookResolver(books);
        }

        /// <summary>
        /// §5: Matches fail-tier rules by miss_range/natural_roll.
        /// Returns outcome.interest_delta or null.
        /// </summary>
        public int? GetFailureInterestDelta(int missMargin, int naturalRoll)
        {
            // Legendary fail (Nat 1) takes priority
            if (naturalRoll == 1)
            {
                var legendaryRule = FindById("§7.fail-tier.legendary-fail");
                if (legendaryRule?.Outcome != null)
                    return GetOutcomeInt(legendaryRule.Outcome, "interest_delta");
            }

            // Search fail tier rules by miss margin
            var state = new GameState(missMargin: missMargin, naturalRoll: naturalRoll);
            var failTierIds = new[]
            {
                "§7.fail-tier.fumble",
                "§7.fail-tier.misfire",
                "§7.fail-tier.trope-trap",
                "§7.fail-tier.catastrophe"
            };

            foreach (var id in failTierIds)
            {
                var rule = FindById(id);
                if (rule?.Condition != null && ConditionEvaluator.Evaluate(rule.Condition, state))
                {
                    if (rule.Outcome != null)
                        return GetOutcomeInt(rule.Outcome, "interest_delta");
                }
            }

            return null;
        }

        /// <summary>
        /// §5: Matches success-scale rules by beat_range/natural_roll.
        /// Returns outcome.interest_delta or null.
        /// </summary>
        public int? GetSuccessInterestDelta(int beatMargin, int naturalRoll)
        {
            // Nat 20 takes priority
            if (naturalRoll == 20)
            {
                var nat20Rule = FindById("§7.success-scale.nat-20");
                if (nat20Rule?.Outcome != null)
                    return GetOutcomeInt(nat20Rule.Outcome, "interest_delta");
            }

            // Search success scale rules by beat margin
            var state = new GameState(beatMargin: beatMargin, naturalRoll: naturalRoll);
            var successIds = new[]
            {
                "§7.success-scale.1-4",
                "§7.success-scale.5-9",
                "§7.success-scale.10plus"
            };

            foreach (var id in successIds)
            {
                var rule = FindById(id);
                if (rule?.Condition != null && ConditionEvaluator.Evaluate(rule.Condition, state))
                {
                    if (rule.Outcome != null)
                        return GetOutcomeInt(rule.Outcome, "interest_delta");
                }
            }

            return null;
        }

        /// <summary>
        /// §6: Matches interest-state rules by interest_range.
        /// Parses outcome.state → InterestState enum.
        /// Returns null if no match or unparseable state.
        /// </summary>
        public InterestState? GetInterestState(int interest)
        {
            var state = new GameState(interest: interest);

            // Check all interest state rules
            var stateIds = new[]
            {
                "§6.interest-state.💀-unmatched",
                "§6.interest-state.😐-bored",
                "§6.interest-state.🤔-lukewarm",
                "§6.interest-state.😊-interested",
                "§6.interest-state.😍-very-into-it",
                "§6.interest-state.🔥-almost-there",
                "§6.interest-state.✅-date-secured"
            };

            foreach (var id in stateIds)
            {
                var rule = FindById(id);
                if (rule?.Condition != null && ConditionEvaluator.Evaluate(rule.Condition, state))
                {
                    if (rule.Outcome != null)
                    {
                        var stateStr = GetOutcomeString(rule.Outcome, "state");
                        if (stateStr != null)
                            return ParseInterestState(stateStr);
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// §7: Shadow thresholds are at fixed values (6/12/18).
        /// Returns highest matching tier for the value.
        /// Returns null if no shadow threshold rules found.
        /// </summary>
        public int? GetShadowThresholdLevel(int shadowValue)
        {
            // Shadow thresholds are at 6 (T1), 12 (T2), 18 (T3).
            // We look for any shadow threshold rule and determine the tier from threshold values.
            // The threshold rules have condition.threshold values.
            int tier = 0;
            bool foundAny = false;

            // Search across all shadow types — thresholds are the same for all
            var prefixes = new[] { "dread", "madness", "denial", "fixation", "overthinking", "horniness" };
            var firstPrefix = prefixes[0]; // Just use dread to determine tiers
            var tierIds = new[]
            {
                $"§9.shadow-threshold.{firstPrefix}.t1",
                $"§9.shadow-threshold.{firstPrefix}.t2",
                $"§9.shadow-threshold.{firstPrefix}.t3"
            };

            for (int i = 0; i < tierIds.Length; i++)
            {
                var rule = FindById(tierIds[i]);
                if (rule?.Condition != null)
                {
                    foundAny = true;
                    int threshold = GetConditionInt(rule.Condition, "threshold");
                    if (shadowValue >= threshold)
                        tier = i + 1;
                }
            }

            return foundAny ? tier : (int?)null;
        }

        /// <summary>
        /// §15: Momentum bonus from streak.
        /// Returns outcome.roll_bonus or null.
        /// </summary>
        public int? GetMomentumBonus(int streak)
        {
            // Check specific streak rules first, then streak_minimum rules
            var momentumIds = new[]
            {
                "§6.momentum.5plus-wins",
                "§6.momentum.4-wins",
                "§6.momentum.3-wins",
                "§6.momentum.2-wins"
            };

            var state = new GameState(streak: streak);
            foreach (var id in momentumIds)
            {
                var rule = FindById(id);
                if (rule?.Condition != null && ConditionEvaluator.Evaluate(rule.Condition, state))
                {
                    if (rule.Outcome != null)
                    {
                        var bonus = GetOutcomeInt(rule.Outcome, "roll_bonus");
                        return bonus;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// §15: Risk tier XP multiplier.
        /// Returns outcome.xp_multiplier or null.
        /// </summary>
        public double? GetRiskTierXpMultiplier(RiskTier riskTier)
        {
            // Map RiskTier to need range and search rules
            var riskIds = new[]
            {
                "§2.risk-tier.safe",
                "§2.risk-tier.medium",
                "§2.risk-tier.hard",
                "§2.risk-tier.bold"
            };

            // Map enum to expected rule id
            string targetId;
            switch (riskTier)
            {
                case RiskTier.Safe: targetId = "§2.risk-tier.safe"; break;
                case RiskTier.Medium: targetId = "§2.risk-tier.medium"; break;
                case RiskTier.Hard: targetId = "§2.risk-tier.hard"; break;
                case RiskTier.Bold: targetId = "§2.risk-tier.bold"; break;
                default: return null;
            }

            var rule = FindById(targetId);
            if (rule?.Outcome != null)
                return GetOutcomeDouble(rule.Outcome, "xp_multiplier");

            return null;
        }

        // --- Helpers ---

        private RuleEntry? FindById(string id)
        {
            foreach (var book in _books)
            {
                var entry = book.GetById(id);
                if (entry != null)
                    return entry;
            }
            return null;
        }

        private static int? GetOutcomeInt(Dictionary<string, object> outcome, string key)
        {
            if (outcome.TryGetValue(key, out var val))
            {
                return ToInt(val);
            }
            return null;
        }

        private static double? GetOutcomeDouble(Dictionary<string, object> outcome, string key)
        {
            if (outcome.TryGetValue(key, out var val))
            {
                return ToDouble(val);
            }
            return null;
        }

        private static string? GetOutcomeString(Dictionary<string, object> outcome, string key)
        {
            if (outcome.TryGetValue(key, out var val) && val != null)
            {
                return val.ToString();
            }
            return null;
        }

        private static int GetConditionInt(Dictionary<string, object> condition, string key)
        {
            if (condition.TryGetValue(key, out var val))
            {
                return ToInt(val) ?? 0;
            }
            return 0;
        }

        private static int? ToInt(object? value)
        {
            if (value == null) return null;
            if (value is int i) return i;
            if (value is long l) return (int)l;
            if (value is double d) return (int)d;
            if (value is float f) return (int)f;
            if (value is string s && int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                return parsed;
            return null;
        }

        private static double? ToDouble(object? value)
        {
            if (value == null) return null;
            if (value is double d) return d;
            if (value is float f) return f;
            if (value is int i) return i;
            if (value is long l) return l;
            if (value is string s && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
                return parsed;
            return null;
        }

        /// <summary>
        /// Parse emoji-prefixed interest state strings to InterestState enum.
        /// E.g. "💀 Unmatched" → Unmatched, "😍 Very Into It" → VeryIntoIt.
        /// </summary>
        private static InterestState? ParseInterestState(string stateStr)
        {
            // Strip emoji prefix if present
            var cleaned = stateStr.Trim();

            // Map known YAML state strings to enum values
            if (cleaned.Contains("Unmatched")) return InterestState.Unmatched;
            if (cleaned.Contains("Bored")) return InterestState.Bored;
            if (cleaned.Contains("Lukewarm")) return InterestState.Lukewarm;
            if (cleaned.Contains("Interested") && !cleaned.Contains("Very")) return InterestState.Interested;
            if (cleaned.Contains("Very Into It")) return InterestState.VeryIntoIt;
            if (cleaned.Contains("Almost There")) return InterestState.AlmostThere;
            if (cleaned.Contains("Date Secured")) return InterestState.DateSecured;

            // Try direct enum parse as fallback
            try
            {
                return (InterestState)Enum.Parse(typeof(InterestState), cleaned, true);
            }
            catch (ArgumentException)
            {
                return null;
            }
        }
    }
}
