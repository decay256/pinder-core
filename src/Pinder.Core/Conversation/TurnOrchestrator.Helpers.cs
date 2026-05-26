using System;
using System.Collections.Generic;
using Pinder.Core.Characters;
using Pinder.Core.Interfaces;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;
using Pinder.Core.Traps;

namespace Pinder.Core.Conversation
{
    internal partial class TurnOrchestrator
    {
        internal static int GetMomentumBonus(int streak, IRuleResolver? rules)
        {
            if (rules != null)
            {
                var resolved = rules.GetMomentumBonus(streak);
                if (resolved.HasValue)
                    return resolved.Value;
            }
            if (streak >= 5) return 3;
            if (streak >= 3) return 2;
            return 0;
        }

        internal static int ResolveFailureInterestDelta(RollResult rollResult, IRuleResolver? rules)
        {
            if (rules != null)
            {
                var resolved = rules.GetFailureInterestDelta(rollResult.MissMargin, rollResult.UsedDieRoll);
                if (resolved.HasValue)
                    return resolved.Value;
            }
            return FailureScale.GetInterestDelta(rollResult);
        }

        internal static int ResolveSuccessInterestDelta(RollResult rollResult, IRuleResolver? rules)
        {
            if (rules != null)
            {
                int beatMargin = rollResult.FinalTotal - rollResult.DC;
                var resolved = rules.GetSuccessInterestDelta(beatMargin, rollResult.UsedDieRoll);
                if (resolved.HasValue)
                    return resolved.Value;
            }
            return SuccessScale.GetInterestDelta(rollResult);
        }

        internal static InterestState ResolveInterestState(GameSessionState state, IRuleResolver? rules)
        {
            if (rules != null)
            {
                var resolved = rules.GetInterestState(state.Interest.Current);
                if (resolved.HasValue)
                    return resolved.Value;
            }
            return state.Interest.GetState();
        }

        internal static int ResolveThresholdLevel(int shadowValue, IRuleResolver? rules)
        {
            if (rules != null)
            {
                var resolved = rules.GetShadowThresholdLevel(shadowValue);
                if (resolved.HasValue)
                    return resolved.Value;
            }
            return ShadowThresholdEvaluator.GetThresholdLevel(shadowValue);
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
                state.OpponentHistory);
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

        internal static string BuildOpponentContext(CharacterProfile opponent)
        {
            if (opponent == null) return string.Empty;
            string bio = string.IsNullOrWhiteSpace(opponent.Bio) ? "(no bio)" : opponent.Bio;
            string items = opponent.EquippedItemDisplayNames != null && opponent.EquippedItemDisplayNames.Count > 0
                ? string.Join(", ", opponent.EquippedItemDisplayNames)
                : "(none)";
            return $"Opponent: {opponent.DisplayName} | Bio: \"{bio}\" | Wearing: {items}";
        }

        internal static void EmitTextLayerNoop(Action<TextLayerNoopEvent>? onTextLayerNoop, int turnNumber, string layer, string beforeText, string afterText)
        {
            if (onTextLayerNoop == null) return;
            try
            {
                string beforeHash = ComputeStableHash(beforeText);
                string afterHash = ComputeStableHash(afterText);
                onTextLayerNoop(new TextLayerNoopEvent(turnNumber, layer, beforeHash, afterHash));
            }
            catch
            {
                // Diagnostic-only path — swallow
            }
        }

        internal static string ComputeStableHash(string? text)
        {
            if (text == null) return "";
            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                byte[] bytes = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(text));
                var sb = new System.Text.StringBuilder(16);
                for (int i = 0; i < Math.Min(8, bytes.Length); i++)
                {
                    sb.Append(bytes[i].ToString("x2"));
                }
                return sb.ToString();
            }
        }
    }
}
