using System;
using System.Collections.Generic;
using Pinder.Core.Characters;
using Pinder.Core.Interfaces;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;
using Pinder.Core.Traps;

namespace Pinder.Core.Conversation
{
    internal static class TurnOrchestratorHelpers
    {
        internal static int GetMomentumBonus(int streak, IRuleResolver? rules, Action<RuleResolutionTraceEvent>? onRuleResolution = null)
        {
            if (rules != null)
            {
                var resolved = rules.GetMomentumBonus(streak);
                if (resolved.HasValue)
                {
                    onRuleResolution?.Invoke(new RuleResolutionTraceEvent("momentum_bonus", "resolver", resolverConfigured: true, numericValue: resolved.Value, stateValue: null));
                    return resolved.Value;
                }
                ThrowIfFallbackDisallowed(rules, "momentum_bonus", $"streak={streak}");
            }
            int fallback = 0;
            if (streak >= 5) fallback = 3;
            else if (streak >= 3) fallback = 2;
            onRuleResolution?.Invoke(new RuleResolutionTraceEvent("momentum_bonus", "hardcoded_fallback", resolverConfigured: rules != null, numericValue: fallback, stateValue: null));
            return fallback;
        }

        internal static int ResolveFailureInterestDelta(RollResult rollResult, IRuleResolver? rules, Action<RuleResolutionTraceEvent>? onRuleResolution = null)
        {
            if (rules != null)
            {
                var resolved = rules.GetFailureInterestDelta(rollResult.MissMargin, rollResult.UsedDieRoll);
                if (resolved.HasValue)
                {
                    onRuleResolution?.Invoke(new RuleResolutionTraceEvent("failure_interest_delta", "resolver", resolverConfigured: true, numericValue: resolved.Value, stateValue: null));
                    return resolved.Value;
                }
                ThrowIfFallbackDisallowed(rules, "failure_interest_delta", $"missMargin={rollResult.MissMargin}, naturalRoll={rollResult.UsedDieRoll}");
            }
            int fallback = FailureScale.GetInterestDelta(rollResult);
            onRuleResolution?.Invoke(new RuleResolutionTraceEvent("failure_interest_delta", "hardcoded_fallback", resolverConfigured: rules != null, numericValue: fallback, stateValue: null));
            return fallback;
        }

        internal static int ResolveSuccessInterestDelta(RollResult rollResult, IRuleResolver? rules, Action<RuleResolutionTraceEvent>? onRuleResolution = null)
        {
            if (rules != null)
            {
                int beatMargin = rollResult.FinalTotal - rollResult.DC;
                var resolved = rules.GetSuccessInterestDelta(beatMargin, rollResult.UsedDieRoll);
                if (resolved.HasValue)
                {
                    onRuleResolution?.Invoke(new RuleResolutionTraceEvent("success_interest_delta", "resolver", resolverConfigured: true, numericValue: resolved.Value, stateValue: null));
                    return resolved.Value;
                }
                ThrowIfFallbackDisallowed(rules, "success_interest_delta", $"beatMargin={beatMargin}, naturalRoll={rollResult.UsedDieRoll}");
            }
            int fallback = SuccessScale.GetInterestDelta(rollResult);
            onRuleResolution?.Invoke(new RuleResolutionTraceEvent("success_interest_delta", "hardcoded_fallback", resolverConfigured: rules != null, numericValue: fallback, stateValue: null));
            return fallback;
        }

        internal static InterestState ResolveInterestState(GameSessionState state, IRuleResolver? rules, Action<RuleResolutionTraceEvent>? onRuleResolution = null)
        {
            if (rules != null)
            {
                var resolved = rules.GetInterestState(state.Interest.Current);
                if (resolved.HasValue)
                {
                    onRuleResolution?.Invoke(new RuleResolutionTraceEvent("interest_state", "resolver", resolverConfigured: true, numericValue: null, stateValue: resolved.Value.ToString()));
                    return resolved.Value;
                }
                ThrowIfFallbackDisallowed(rules, "interest_state", $"interest={state.Interest.Current}");
            }
            InterestState fallback = state.Interest.GetState();
            onRuleResolution?.Invoke(new RuleResolutionTraceEvent("interest_state", "hardcoded_fallback", resolverConfigured: rules != null, numericValue: null, stateValue: fallback.ToString()));
            return fallback;
        }

        internal static int ResolveThresholdLevel(int shadowValue, IRuleResolver? rules, Action<RuleResolutionTraceEvent>? onRuleResolution = null)
        {
            if (rules != null)
            {
                var resolved = rules.GetShadowThresholdLevel(shadowValue);
                if (resolved.HasValue)
                {
                    onRuleResolution?.Invoke(new RuleResolutionTraceEvent("shadow_threshold_level", "resolver", resolverConfigured: true, numericValue: resolved.Value, stateValue: null));
                    return resolved.Value;
                }
                ThrowIfFallbackDisallowed(rules, "shadow_threshold_level", $"shadowValue={shadowValue}");
            }
            int fallback = ShadowThresholdEvaluator.GetThresholdLevel(shadowValue);
            onRuleResolution?.Invoke(new RuleResolutionTraceEvent("shadow_threshold_level", "hardcoded_fallback", resolverConfigured: rules != null, numericValue: fallback, stateValue: null));
            return fallback;
        }

        private static void ThrowIfFallbackDisallowed(IRuleResolver rules, string ruleKey, string inputs)
        {
            if (rules.AllowDefaultFallback)
                return;

            throw new InvalidOperationException(
                $"Configured rule resolver returned no value for '{ruleKey}' ({inputs}) and AllowDefaultFallback is false.");
        }

        internal static GameStateSnapshot CreateSnapshot(GameSessionState state, IRuleResolver? rules)
        {
            return GameSessionHelpers.CreateSnapshot(
                state.Interest,
                ResolveInterestState(state, rules),
                state.MomentumStreak,
                state.Traps,
                state.TurnNumber,
                state.ComboTracker.HasTripleBonus,
                state.DateeHistory,
                state.AvatarHistory,
                state.PlayerShadows);
        }

        internal static System.Collections.Generic.IReadOnlyList<(string Sender, string Text)> BuildHistoryForLlmContext(GameSessionState state)
        {
            var history = state.History;
            bool anyScene = false;
            for (int i = 0; i < history.Count; i++)
            {
                if (Senders.IsScene(history[i].Sender)) { anyScene = true; break; }
            }
            if (!anyScene) return history.AsReadOnly();

            var view = new List<(string Sender, string Text)>(history.Count);
            for (int i = 0; i < history.Count; i++)
            {
                var entry = history[i];
                if (Senders.IsScene(entry.Sender)) continue;
                view.Add(entry);
            }
            return view.AsReadOnly();
        }

        internal static ShadowStatType? GetPairedShadow(StatType stat)
        {
            switch (stat)
            {
                case StatType.Charm:         return ShadowStatType.Madness;
                case StatType.Rizz:          return ShadowStatType.Despair;
                case StatType.Honesty:       return ShadowStatType.Denial;
                case StatType.Chaos:         return ShadowStatType.Fixation;
                case StatType.Wit:           return ShadowStatType.Dread;
                case StatType.SelfAwareness: return ShadowStatType.Overthinking;
                default:                     return null;
            }
        }

        internal static RollResult CreateForcedFailResult(RollResult original, FailureTier shadowTier)
        {
            int fakeDie = original.DC > 1 ? original.DC - 1 : 1;
            var check = Pinder.Core.Rolls.RollCheckResult.Synthesise(
                dieRoll:       fakeDie,
                secondDieRoll: null,
                usedDieRoll:   fakeDie,
                statModifier:  0,
                levelBonus:    0,
                dc:            original.DC);
            return new RollResult(
                dieRoll:        fakeDie,
                secondDieRoll:  null,
                usedDieRoll:    fakeDie,
                stat:           original.Stat,
                statModifier:   0,
                levelBonus:     0,
                dc:             original.DC,
                tier:           shadowTier,
                activatedTrap:  null,
                externalBonus:  0,
                check:          check,
                defendingStat:  Pinder.Core.Stats.StatBlock.DefenceTable[original.Stat]);
        }

        internal static string BuildDateeContext(CharacterProfile datee)
        {
            if (datee == null) return string.Empty;
            string bio = string.IsNullOrWhiteSpace(datee.Bio) ? "(no bio)" : datee.Bio;
            string items = datee.EquippedItemDisplayNames != null && datee.EquippedItemDisplayNames.Count > 0
                ? string.Join(", ", datee.EquippedItemDisplayNames)
                : "(none)";
            return $"Datee: {datee.DisplayName} | Bio: \"{bio}\" | Wearing: {items}";
        }

    }
}
