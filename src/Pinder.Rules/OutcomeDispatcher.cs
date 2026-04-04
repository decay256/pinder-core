using System;
using System.Collections.Generic;
using System.Globalization;

namespace Pinder.Rules
{
    /// <summary>
    /// Reads outcome dictionaries and dispatches effects to an IEffectHandler.
    /// Unknown keys are silently ignored.
    /// </summary>
    public static class OutcomeDispatcher
    {
        /// <summary>
        /// Read outcome dict and dispatch effects to handler.
        /// Does nothing if outcome is null.
        /// </summary>
        public static void Dispatch(
            Dictionary<string, object>? outcome,
            GameState state,
            IEffectHandler handler)
        {
            if (outcome == null || handler == null)
                return;

            foreach (var kvp in outcome)
            {
                var key = kvp.Key.ToLowerInvariant();
                var value = kvp.Value;

                switch (key)
                {
                    case "interest_delta":
                        handler.ApplyInterestDelta(ToInt(value));
                        break;

                    case "trap":
                        if (ToBool(value))
                            handler.ActivateTrap("");
                        break;

                    case "trap_name":
                        handler.ActivateTrap(value?.ToString() ?? "");
                        break;

                    case "roll_bonus":
                        handler.SetRollModifier("+" + ToInt(value).ToString(CultureInfo.InvariantCulture));
                        break;

                    case "effect":
                        handler.SetRollModifier(value?.ToString() ?? "");
                        break;

                    case "risk_tier":
                        handler.SetRiskTier(value?.ToString() ?? "");
                        break;

                    case "xp_multiplier":
                        handler.SetXpMultiplier(ToDouble(value));
                        break;

                    case "shadow_effect":
                        DispatchShadowEffect(value, handler);
                        break;

                    case "starting_interest":
                        handler.ApplyInterestDelta(ToInt(value));
                        break;

                    // Unknown outcome keys are silently ignored.
                    default:
                        break;
                }
            }
        }

        private static void DispatchShadowEffect(object? value, IEffectHandler handler)
        {
            if (value is Dictionary<string, object> dict)
            {
                var shadow = dict.ContainsKey("shadow") ? dict["shadow"]?.ToString() ?? "" : "";
                var delta = dict.ContainsKey("delta") ? ToInt(dict["delta"]) : 0;
                handler.ApplyShadowGrowth(shadow, delta, "rule engine");
            }
        }

        private static int ToInt(object? value)
        {
            if (value == null) return 0;
            if (value is int i) return i;
            if (value is long l) return (int)l;
            if (value is double d) return (int)d;
            if (value is float f) return (int)f;
            if (value is string s && int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                return parsed;
            return 0;
        }

        private static double ToDouble(object? value)
        {
            if (value == null) return 0.0;
            if (value is double d) return d;
            if (value is int i) return i;
            if (value is long l) return l;
            if (value is float f) return f;
            if (value is string s && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
                return parsed;
            return 0.0;
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
