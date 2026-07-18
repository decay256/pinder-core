using System;
using System.Collections.Generic;
using System.Globalization;

namespace Pinder.Rules
{
    /// <summary>
    /// Evaluates whether a rule's condition dictionary matches the current game state.
    /// All conditions in the dict must match (AND logic).
    /// Unknown keys are rejected as malformed rule configuration.
    /// </summary>
    public static class ConditionEvaluator
    {
        /// <summary>
        /// Evaluate whether a rule's condition matches the current game state.
        /// Returns false if condition is null or empty.
        /// </summary>
        public static bool Evaluate(Dictionary<string, object>? condition, GameState state)
        {
            if (condition == null || condition.Count == 0)
                return false;

            foreach (var kvp in condition)
            {
                var key = kvp.Key.ToLowerInvariant();
                var value = kvp.Value;

                switch (key)
                {
                    case "miss_range":
                        if (!CheckRange(value, state.MissMargin))
                            return false;
                        break;

                    case "miss_minimum":
                        if (!CheckMinimum(value, state.MissMargin))
                            return false;
                        break;

                    case "beat_range":
                        if (!CheckRange(value, state.BeatMargin))
                            return false;
                        break;

                    case "interest_range":
                        if (!CheckRange(value, state.Interest))
                            return false;
                        break;

                    case "need_range":
                        if (!CheckRange(value, state.NeedToHit))
                            return false;
                        break;

                    case "level_range":
                        if (!CheckRange(value, state.Level))
                            return false;
                        break;

                    case "natural_roll":
                        if (ToInt(value, kvp.Key) != state.NaturalRoll)
                            return false;
                        break;

                    case "streak":
                        if (ToInt(value, kvp.Key) != state.Streak)
                            return false;
                        break;

                    case "streak_minimum":
                        if (state.Streak < ToInt(value, kvp.Key))
                            return false;
                        break;

                    case "action":
                        if (!string.Equals(value?.ToString(), state.Action, StringComparison.OrdinalIgnoreCase))
                            return false;
                        break;

                    case "conversation_start":
                        if (ToBool(value) != state.IsConversationStart)
                            return false;
                        break;

                    default:
                        throw new FormatException($"Unknown rule condition key '{kvp.Key}'.");
                }
            }

            return true;
        }

        private static bool CheckRange(object value, int actual)
        {
            if (value is List<object> list && list.Count == 2)
            {
                int lo = ToInt(list[0], "range lower bound");
                int hi = ToInt(list[1], "range upper bound");
                return actual >= lo && actual <= hi;
            }
            if (value is List<object>)
                throw new FormatException("Rule condition range must contain exactly two numeric values.");
            return false;
        }

        private static bool CheckMinimum(object value, int actual)
        {
            return actual >= ToInt(value, "minimum");
        }

        private static int ToInt(object? value, string context)
        {
            if (value == null)
                throw new FormatException($"Rule condition {context} must be numeric, got null.");
            if (value is int i) return i;
            if (value is long l) return (int)l;
            if (value is double d) return (int)d;
            if (value is float f) return (int)f;
            if (value is string s && int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                return parsed;
            throw new FormatException($"Rule condition {context} must be numeric, got '{value}'.");
        }

        private static bool ToBool(object? value)
        {
            if (value == null) return false;
            if (value is bool b) return b;
            if (value is string s)
                return string.Equals(s, "true", StringComparison.OrdinalIgnoreCase);
            return false;
        }
    }
}
