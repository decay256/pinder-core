using System;
using System.Collections.Generic;
using System.Globalization;

namespace Pinder.Rules
{
    /// <summary>
    /// Reads outcome dictionaries and dispatches effects to an IEffectHandler.
    /// Unknown keys and malformed values are rejected before any effects are applied.
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

            Validate(outcome);

            foreach (var kvp in outcome)
            {
                var key = kvp.Key.ToLowerInvariant();
                var value = kvp.Value;

                switch (key)
                {
                    case "interest_delta":
                        handler.ApplyInterestDelta(ToInt(value, key));
                        break;

                    case "trap":
                        if (ToBool(value))
                            handler.ActivateTrap("");
                        break;

                    case "trap_name":
                        handler.ActivateTrap(value?.ToString() ?? "");
                        break;

                    case "roll_bonus":
                        handler.SetRollModifier("+" + ToInt(value, key).ToString(CultureInfo.InvariantCulture));
                        break;

                    case "effect":
                        handler.SetRollModifier(value?.ToString() ?? "");
                        break;

                    case "risk_tier":
                        handler.SetRiskTier(value?.ToString() ?? "");
                        break;

                    case "xp_multiplier":
                        handler.SetXpMultiplier(ToDouble(value, key));
                        break;

                    case "shadow_effect":
                        DispatchShadowEffect(value, handler);
                        break;

                    case "starting_interest":
                        handler.ApplyInterestDelta(ToInt(value, key));
                        break;

                    case "tier":
                    case "state":
                    case "xp":
                    case "multiplier":
                    case "base_xp":
                    case "xp_threshold":
                    case "build_points":
                    case "level_bonus":
                    case "item_slots":
                    case "min_level":
                        break;

                    default:
                        throw new FormatException($"Unknown rule outcome key '{kvp.Key}'.");
                }
            }
        }

        private static void Validate(Dictionary<string, object> outcome)
        {
            foreach (var kvp in outcome)
            {
                var key = kvp.Key.ToLowerInvariant();
                var value = kvp.Value;

                switch (key)
                {
                    case "interest_delta":
                    case "roll_bonus":
                    case "starting_interest":
                        _ = ToInt(value, key);
                        break;

                    case "trap":
                        _ = ToBool(value);
                        break;

                    case "trap_name":
                    case "effect":
                    case "risk_tier":
                    case "tier":
                    case "state":
                        break;

                    case "xp_multiplier":
                        _ = ToDouble(value, key);
                        break;

                    case "shadow_effect":
                        ValidateShadowEffect(value);
                        break;

                    case "xp":
                    case "multiplier":
                    case "base_xp":
                    case "xp_threshold":
                    case "build_points":
                    case "level_bonus":
                    case "item_slots":
                    case "min_level":
                        _ = ToDouble(value, key);
                        break;

                    default:
                        throw new FormatException($"Unknown rule outcome key '{kvp.Key}'.");
                }
            }
        }

        private static void DispatchShadowEffect(object? value, IEffectHandler handler)
        {
            if (value is Dictionary<string, object> dict)
            {
                var shadow = dict.ContainsKey("shadow") ? dict["shadow"]?.ToString() ?? "" : "";
                var delta = dict.ContainsKey("delta") ? ToInt(dict["delta"], "shadow_effect.delta") : 0;
                handler.ApplyShadowGrowth(shadow, delta, "rule engine");
            }
        }

        private static void ValidateShadowEffect(object? value)
        {
            if (!(value is Dictionary<string, object> dict))
                throw new FormatException("Rule outcome shadow_effect must be an object.");
            if (!dict.ContainsKey("shadow"))
                throw new FormatException("Rule outcome shadow_effect missing required key 'shadow'.");
            if (!dict.ContainsKey("delta"))
                throw new FormatException("Rule outcome shadow_effect missing required key 'delta'.");
            _ = ToInt(dict["delta"], "shadow_effect.delta");
        }

        private static int ToInt(object? value, string context)
        {
            return RuleValueCoercion.ToInt(value, "Rule outcome", context);
        }

        private static double ToDouble(object? value, string context)
        {
            return RuleValueCoercion.ToDouble(value, "Rule outcome", context);
        }

        private static bool ToBool(object? value)
        {
            return RuleValueCoercion.ToBool(value, "Rule outcome", "trap");
        }
    }
}
