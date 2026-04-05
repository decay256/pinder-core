using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Pinder.Core.Conversation;
using Pinder.Core.Stats;

namespace Pinder.SessionRunner
{
    /// <summary>
    /// Deterministic player agent that scores all dialogue options using an expected-value
    /// formula derived from the game's mechanical rules. No LLM — pure math.
    /// Produces consistent, explainable decisions useful for regression testing.
    /// </summary>
    public sealed class ScoringPlayerAgent : IPlayerAgent
    {
        // Trap activation cost added to TropeTrap/Catastrophe/Legendary failure tiers
        // Represents ~1.5 turns of reduced effectiveness from activated trap
        private const float TrapActivationCost = 1.5f;

        // Low success threshold below which combo bonus is further scaled down
        private const float LowSuccessThreshold = 0.20f;

        // Strategic adjustment magnitudes
        private const float MomentumStreakBias = 1.0f;
        private const float NearWinBias = 2.0f;
        private const float BoredBoldBias = 1.0f;
        private const float ActiveTrapPenalty = 2.0f;

        // Shadow growth risk constants (§7)
        private const float FixationGrowthPenalty = 0.5f;
        private const float DenialGrowthPenalty = 0.3f;
        private const float FixationT1EvMultiplier = 0.8f;
        private const float StatVarietyBonus = 0.1f;

        // SYNC: GameSession ResolveTurnAsync tellBonus
        private const int TellBonusValue = 2;

        /// <summary>
        /// Mapping from StatType to the trap name that appears in ActiveTrapNames.
        /// Uses the shadow stat name from StatBlock.ShadowPairs.
        /// </summary>
        private static readonly Dictionary<StatType, string> StatToTrapName = new Dictionary<StatType, string>
        {
            { StatType.Charm,         "Madness" },
            { StatType.Rizz,          "Horniness" },
            { StatType.Honesty,       "Denial" },
            { StatType.Chaos,         "Fixation" },
            { StatType.Wit,           "Dread" },
            { StatType.SelfAwareness, "Overthinking" }
        };

        /// <summary>
        /// Scores all options in the TurnStart and picks the highest-scoring one.
        /// Deterministic: same inputs always produce the same output.
        /// </summary>
        public Task<PlayerDecision> DecideAsync(TurnStart turn, PlayerAgentContext context)
        {
            if (turn == null) throw new ArgumentNullException(nameof(turn));
            if (context == null) throw new ArgumentNullException(nameof(context));

            var options = turn.Options;

            // Edge case: no options available
            if (options.Length == 0)
            {
                throw new InvalidOperationException("No options available to score.");
            }

            // SYNC: GameSession.GetMomentumBonus()
            int momentumBonus;
            if (context.MomentumStreak >= 5) momentumBonus = 3;
            else if (context.MomentumStreak >= 3) momentumBonus = 2;
            else momentumBonus = 0;

            // Pre-compute active trap set for O(1) lookups
            var activeTrapSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (context.ActiveTrapNames != null)
            {
                for (int i = 0; i < context.ActiveTrapNames.Length; i++)
                {
                    activeTrapSet.Add(context.ActiveTrapNames[i]);
                }
            }

            var scores = new OptionScore[options.Length];
            int bestIndex = 0;
            float bestScore = float.MinValue;

            for (int i = 0; i < options.Length; i++)
            {
                DialogueOption option = options[i];

                // Step 1: Compute need
                int attackerMod = context.PlayerStats.GetEffective(option.Stat);
                int defenceDC = context.OpponentStats.GetDefenceDC(option.Stat);

                // Callback bonus — MUST call CallbackBonus.Compute() directly (per #386 ADR)
                int callbackBonus = option.CallbackTurnNumber.HasValue
                    ? CallbackBonus.Compute(context.TurnNumber, option.CallbackTurnNumber.Value)
                    : 0;

                int tellBonus = option.HasTellBonus ? TellBonusValue : 0;

                int totalMod = attackerMod + momentumBonus + tellBonus + callbackBonus;
                int need = defenceDC - totalMod;

                // Step 2: Compute success/fail chances
                float successChance = Math.Max(0.0f, Math.Min(1.0f, (21.0f - need) / 20.0f));
                float failChance = 1.0f - successChance;

                // Step 3: Risk tier and bonus
                RiskTierInfo riskInfo = ComputeRiskTier(need);

                // Step 4: Expected interest on success (Option A — midpoint approximation)
                float baseInterestGain;
                if (successChance > 0.0f)
                {
                    // Midpoint of the success range: average margin when succeeding
                    float avgMargin = (21.0f - need) / 2.0f;
                    if (avgMargin >= 10.0f) baseInterestGain = 3.0f;
                    else if (avgMargin >= 5.0f) baseInterestGain = 2.0f;
                    else baseInterestGain = 1.0f;
                }
                else
                {
                    baseInterestGain = 0.0f;
                }

                // Scale combo bonus when success probability is low (<20%).
                // A combo that fires 15% of the time is worth much less than full value.
                float comboScale = successChance < LowSuccessThreshold
                    ? successChance / LowSuccessThreshold
                    : 1.0f;
                float comboBonus = option.ComboName != null ? 1.0f * comboScale : 0.0f;
                float expectedGainOnSuccess = baseInterestGain + riskInfo.Bonus + comboBonus;

                // Step 5: Weighted failure cost based on miss margin distribution
                // Accounts for trap activation on TropeTrap/Catastrophe/Legendary
                float failCost = ComputeWeightedFailCost(need);

                // Step 6: Raw EV
                float expectedInterestGain = successChance * expectedGainOnSuccess
                                           - failChance * failCost;
                float score = expectedInterestGain;

                // Step 7: Strategic adjustments
                var bonuses = new List<string>();
                var strategicReasons = new List<string>();

                if (momentumBonus > 0) bonuses.Add($"momentum +{momentumBonus}");
                if (tellBonus > 0) bonuses.Add($"tell +{tellBonus}");
                if (callbackBonus > 0) bonuses.Add($"callback +{callbackBonus}");
                if (option.ComboName != null) bonuses.Add($"combo: {option.ComboName}");

                // Momentum streak == 2: bias toward safe success
                if (context.MomentumStreak == 2 && successChance >= 0.5f)
                {
                    score += MomentumStreakBias;
                    strategicReasons.Add("Momentum at 2 — prioritizing reliable success");
                }

                // Near win (interest 19-24): prefer safe/medium
                if (context.CurrentInterest >= 19 && context.CurrentInterest <= 24
                    && (riskInfo.Tier == RiskTierCategory.Safe || riskInfo.Tier == RiskTierCategory.Medium))
                {
                    score += NearWinBias;
                    strategicReasons.Add("Near win — preferring low-variance option");
                }

                // Bored state: prefer bold
                if (context.InterestState == InterestState.Bored
                    && (riskInfo.Tier == RiskTierCategory.Hard || riskInfo.Tier == RiskTierCategory.Bold))
                {
                    score += BoredBoldBias;
                    strategicReasons.Add("Bored — swinging for the fences");
                }

                // Active trap penalty
                if (StatToTrapName.TryGetValue(option.Stat, out string? trapName)
                    && trapName != null
                    && activeTrapSet.Contains(trapName))
                {
                    score -= ActiveTrapPenalty;
                    strategicReasons.Add($"Active {trapName} trap — penalty applied");
                }

                // Shadow growth risk: Fixation growth penalty (§7: same stat 3x → +1 Fixation)
                if (context.LastStatUsed.HasValue
                    && context.SecondLastStatUsed.HasValue
                    && option.Stat == context.LastStatUsed.Value
                    && context.LastStatUsed.Value == context.SecondLastStatUsed.Value)
                {
                    score -= FixationGrowthPenalty;
                    strategicReasons.Add("Fixation growth risk — same stat 3x");
                }

                // Shadow growth risk: Denial penalty (§7: skip Honesty when available → +1 Denial)
                bool honestyInOptions = false;
                for (int j = 0; j < options.Length; j++)
                {
                    if (options[j].Stat == StatType.Honesty)
                    {
                        honestyInOptions = true;
                        break;
                    }
                }
                if (option.Stat != StatType.Honesty && honestyInOptions)
                {
                    score -= DenialGrowthPenalty;
                    strategicReasons.Add("Denial growth risk — skipping available Honesty");
                }

                // Shadow threshold: Fixation effects on Chaos options (§7)
                if (option.Stat == StatType.Chaos && context.ShadowValues != null)
                {
                    int fixation = 0;
                    context.ShadowValues.TryGetValue(ShadowStatType.Fixation, out fixation);
                    if (fixation >= 12)
                    {
                        // T2+: apply disadvantage to success chance calculation
                        // Disadvantage: roll 2d20 take lowest → P(success) = 1 - (1 - p)^2... actually P = p^2
                        // For d20 with threshold: P(both >= need) = p * p
                        float disadvSuccessChance = successChance * successChance;
                        float disadvExpectedGain = disadvSuccessChance * expectedGainOnSuccess
                                                 - (1.0f - disadvSuccessChance) * failCost;
                        // Replace the EV component of score with disadvantaged version
                        score = score - expectedInterestGain + disadvExpectedGain;
                        expectedInterestGain = disadvExpectedGain;
                        successChance = disadvSuccessChance;
                        strategicReasons.Add($"Fixation T2 ({fixation}) — Chaos disadvantage applied");
                    }
                    else if (fixation >= 6)
                    {
                        // T1: reduce expected gain on success by 20% (LLM quality degradation)
                        float reducedGain = expectedGainOnSuccess * FixationT1EvMultiplier;
                        float reducedEv = successChance * reducedGain - failChance * failCost;
                        score = score - expectedInterestGain + reducedEv;
                        expectedInterestGain = reducedEv;
                        strategicReasons.Add($"Fixation T1 ({fixation}) — Chaos EV reduced 20%");
                    }
                }

                // Stat variety bonus: prefer stats not used recently
                if (context.LastStatUsed.HasValue || context.SecondLastStatUsed.HasValue)
                {
                    bool usedRecently = false;
                    if (context.LastStatUsed.HasValue && option.Stat == context.LastStatUsed.Value)
                        usedRecently = true;
                    if (context.SecondLastStatUsed.HasValue && option.Stat == context.SecondLastStatUsed.Value)
                        usedRecently = true;
                    if (!usedRecently)
                    {
                        score += StatVarietyBonus;
                        strategicReasons.Add("Stat variety bonus — not used recently");
                    }
                }

                scores[i] = new OptionScore(
                    optionIndex: i,
                    score: score,
                    successChance: successChance,
                    expectedInterestGain: expectedInterestGain,
                    bonusesApplied: bonuses.ToArray());

                if (score > bestScore)
                {
                    bestScore = score;
                    bestIndex = i;
                }
            }

            // Step 8: Build reasoning
            string reasoning = BuildReasoning(options, scores, bestIndex, context);

            var decision = new PlayerDecision(bestIndex, reasoning, scores);
            return Task.FromResult(decision);
        }

        private static string BuildReasoning(
            DialogueOption[] options,
            OptionScore[] scores,
            int bestIndex,
            PlayerAgentContext context)
        {
            DialogueOption chosen = options[bestIndex];
            OptionScore chosenScore = scores[bestIndex];

            string statName = StatLabel(chosen.Stat);
            string pct = $"{chosenScore.SuccessChance * 100:F0}%";

            // Find runner-up
            int runnerIndex = -1;
            float runnerScore = float.MinValue;
            for (int i = 0; i < scores.Length; i++)
            {
                if (i != bestIndex && scores[i].Score > runnerScore)
                {
                    runnerScore = scores[i].Score;
                    runnerIndex = i;
                }
            }

            string comparison = "";
            if (runnerIndex >= 0)
            {
                string runnerName = StatLabel(options[runnerIndex].Stat);
                string runnerPct = $"{scores[runnerIndex].SuccessChance * 100:F0}%";
                comparison = $" beats {runnerName} at {runnerPct}";
            }

            string result = $"{statName} at {pct}{comparison} — EV {chosenScore.ExpectedInterestGain:F2}.";

            // Add strategic notes
            if (context.MomentumStreak == 2)
                result += " Momentum at 2 — prioritizing success to reach +2 bonus.";
            if (context.CurrentInterest >= 19 && context.CurrentInterest <= 24)
                result += " Near win — preferring safe options.";
            if (context.InterestState == InterestState.Bored)
                result += " Bored state — favouring bold plays.";

            return result;
        }

        private static string StatLabel(StatType s)
        {
            switch (s)
            {
                case StatType.Charm: return "Charm";
                case StatType.Rizz: return "Rizz";
                case StatType.Honesty: return "Honesty";
                case StatType.Chaos: return "Chaos";
                case StatType.Wit: return "Wit";
                case StatType.SelfAwareness: return "SA";
                default: return s.ToString();
            }
        }

        /// <summary>
        /// Computes weighted average failure cost based on the distribution of miss margins
        /// across failure tiers. Includes trap activation cost for TropeTrap, Catastrophe,
        /// and Legendary tiers. Replaces flat DefaultFailCost for more accurate EV.
        /// </summary>
        private static float ComputeWeightedFailCost(int need)
        {
            // If need <= 1, only nat1 can fail (handled by d20 min=1)
            if (need <= 1) return 0f;

            int maxFailRoll = Math.Min(need - 1, 20);
            if (maxFailRoll <= 0) return 0f;

            float totalCost = 0f;

            for (int roll = 1; roll <= maxFailRoll; roll++)
            {
                if (roll == 1)
                {
                    // Nat1 → Legendary: -4 interest + trap activation
                    totalCost += 4.0f + TrapActivationCost;
                }
                else
                {
                    int missMargin = need - roll;
                    if (missMargin >= 10)
                    {
                        // Catastrophe: -3 interest + trap activation
                        totalCost += 3.0f + TrapActivationCost;
                    }
                    else if (missMargin >= 6)
                    {
                        // TropeTrap: -2 interest + trap activation
                        totalCost += 2.0f + TrapActivationCost;
                    }
                    else if (missMargin >= 3)
                    {
                        // Misfire: -1 interest
                        totalCost += 1.0f;
                    }
                    else
                    {
                        // Fumble (miss 1-2): -1 interest
                        totalCost += 1.0f;
                    }
                }
            }

            return totalCost / maxFailRoll;
        }

        private static RiskTierInfo ComputeRiskTier(int need)
        {
            if (need <= 5) return new RiskTierInfo(RiskTierCategory.Safe, 0);
            if (need <= 10) return new RiskTierInfo(RiskTierCategory.Medium, 0);
            if (need <= 15) return new RiskTierInfo(RiskTierCategory.Hard, 1);
            return new RiskTierInfo(RiskTierCategory.Bold, 2);
        }

        private enum RiskTierCategory
        {
            Safe,
            Medium,
            Hard,
            Bold
        }

        private sealed class RiskTierInfo
        {
            public RiskTierCategory Tier { get; }
            public int Bonus { get; }

            public RiskTierInfo(RiskTierCategory tier, int bonus)
            {
                Tier = tier;
                Bonus = bonus;
            }
        }
    }
}
