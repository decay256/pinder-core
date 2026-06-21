using System;
using System.Collections.Generic;
using System.Linq;
using Pinder.Core.Characters;
using Pinder.Core.Interfaces;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;
using Pinder.Core.Progression;
using Pinder.Core.Traps;

namespace Pinder.Core.Conversation
{
    internal struct RollStageResult
    {
        public DialogueOption ChosenOption { get; set; }
        public string? TrapClearedDisplayName { get; set; }
        public int CallbackBonus { get; set; }
        public int TellBonus { get; set; }
        public int TripleBonusApplied { get; set; }
        public RollResult RollResult { get; set; }
        public IDiceRoller ResolveDice { get; set; }
        public int BaseInterestDelta { get; set; }
        public int RiskBonusDelta { get; set; }
        public int InterestDelta { get; set; }
        public string? ComboTriggered { get; set; }
        public int ComboBonusDelta { get; set; }
        public int InterestBefore { get; set; }
        public InterestState StateBefore { get; set; }
        public int InterestAfter { get; set; }
        public InterestState StateAfter { get; set; }
        public bool IsGameOver { get; set; }
        public GameOutcome? Outcome { get; set; }
        public int TurnXpEarned { get; set; }
        public IReadOnlyList<string> ShadowGrowthEvents { get; set; }
    }

    internal class RollResolutionStage
    {
        private readonly IDiceRoller _dice;
        private readonly ITrapRegistry _trapRegistry;
        private readonly IRuleResolver? _rules;
        private readonly ShadowGrowthEvaluator? _shadowGrowthEvaluator;
        private readonly SessionXpRecorder _xpRecorder;
        private readonly int _globalDcBias;

        public RollResolutionStage(
            IDiceRoller dice,
            ITrapRegistry trapRegistry,
            IRuleResolver? rules,
            ShadowGrowthEvaluator? shadowGrowthEvaluator,
            SessionXpRecorder xpRecorder,
            int globalDcBias)
        {
            _dice = dice ?? throw new ArgumentNullException(nameof(dice));
            _trapRegistry = trapRegistry ?? throw new ArgumentNullException(nameof(trapRegistry));
            _rules = rules;
            _shadowGrowthEvaluator = shadowGrowthEvaluator;
            _xpRecorder = xpRecorder ?? throw new ArgumentNullException(nameof(xpRecorder));
            _globalDcBias = globalDcBias;
        }

        public RollStageResult Execute(
            GameSessionState state,
            int optionIndex,
            CharacterProfile player,
            CharacterProfile datee)
        {
            var chosenOption = state.CurrentOptions![optionIndex];

            // ---- Trap SA-disarm (issue #371) ----
            string? trapClearedDisplayName = null;
            if (chosenOption.Stat == StatType.SelfAwareness && state.Traps.HasActive)
            {
                trapClearedDisplayName = state.Traps.Active!.Definition.DisplayName;
                state.Traps.Clear();
            }

            // Denial +1 when Honesty was available but player chose a different stat (#272 — §7)
            if (state.PlayerShadows != null
                && chosenOption.Stat != StatType.Honesty
                && state.CurrentOptions.Any(o => o.Stat == StatType.Honesty))
            {
                state.PlayerShadows.ApplyGrowth(ShadowStatType.Denial, 1,
                    "Skipped Honesty option");
            }

            // Compute callback bonus (#47)
            int callbackBonus = 0;
            if (chosenOption.CallbackTurnNumber.HasValue)
            {
                callbackBonus = CallbackBonus.Compute(state.TurnNumber, chosenOption.CallbackTurnNumber.Value);
            }

            // Compute tell bonus (#50)
            bool hasTellOption = state.ActiveTell != null && chosenOption.Stat == state.ActiveTell.Stat;
            int tellBonus = hasTellOption ? 4 : 0;

            // Compute external bonus: tell + callback + Triple combo + momentum (#46, #47, #50, #268)
            int externalBonus = tellBonus + callbackBonus + state.PendingMomentumBonus;
            int tripleBonusApplied = 0;
            if (state.ComboTracker.HasTripleBonus)
            {
                tripleBonusApplied = 2;
                externalBonus += tripleBonusApplied;
                state.ComboTracker.ConsumeTripleBonus(); // Consume after applying (#46 edge case 7)
            }

            // Compute DC adjustment from weakness window (#49) + global difficulty bias
            int dcAdjustment = 0;
            if (state.ActiveWeakness != null
                && StatBlock.DefenceTable[chosenOption.Stat] == state.ActiveWeakness.DefendingStat)
            {
                dcAdjustment = state.ActiveWeakness.DcReduction;
            }
            if (_globalDcBias != 0)
            {
                // Positive bias lowers the effective DC (easier).
                // Under ApplyDcBias semantics: ApplyDcBias(baseDc, bias) => baseDc - bias.
                // In RollEngine.Resolve, the final DC is computed as defender.GetDefenceDC(stat) - dcAdjustment.
                // Therefore, adding _globalDcBias to dcAdjustment is mathematically identical to routing through the shared helper:
                // defenceDC - (dcAdjustment_without_bias + globalDcBias) == ApplyDcBias(defenceDC - dcAdjustment_without_bias, globalDcBias).
                dcAdjustment += _globalDcBias;
            }

            // Clear weakness window — consumed this turn regardless of match (#49)
            state.ActiveWeakness = null;

            // Clear active tell — consumed this turn regardless of match (#50)
            state.ActiveTell = null;

            // Shadow threshold per-stat disadvantage (#45)
            bool resolveHasDisadvantage = state.CurrentHasDisadvantage;
            if (state.ShadowDisadvantagedStats != null && state.ShadowDisadvantagedStats.Contains(chosenOption.Stat))
            {
                resolveHasDisadvantage = true;
            }

            // 1. Roll dice
            IDiceRoller resolveDice;
            RollResult rollResult;
            (rollResult, resolveDice) = TurnDiceEvaluator.EvaluateRolls(
                state,
                optionIndex,
                chosenOption,
                player,
                datee,
                _dice,
                _trapRegistry,
                null, // consequenceCatalog is not needed inside EvaluateRolls
                externalBonus,
                dcAdjustment,
                resolveHasDisadvantage,
                _rules);

            // 2. Compute interest delta from roll outcome
            int baseInterestDelta;
            int riskBonusDelta = 0;
            if (rollResult.IsSuccess)
            {
                baseInterestDelta = TurnOrchestratorHelpers.ResolveSuccessInterestDelta(rollResult, _rules);
                riskBonusDelta = RiskTierBonus.GetInterestBonus(rollResult);
            }
            else
            {
                baseInterestDelta = TurnOrchestratorHelpers.ResolveFailureInterestDelta(rollResult, _rules);
            }
            int interestDelta = baseInterestDelta + riskBonusDelta;

            // 3. Update momentum streak
            state.PendingMomentumBonus = 0;
            if (rollResult.IsSuccess)
            {
                state.MomentumStreak++;
            }
            else
            {
                state.MomentumStreak = 0;
            }

            // 3b. Nat 20 crit advantage (#271) — set for next roll
            if (rollResult.IsNatTwenty)
            {
                state.PendingCritAdvantage = true;

                // Nat 20 on CHAOS → Madness −1
                if (chosenOption.Stat == StatType.Chaos)
                {
                    state.PlayerShadows?.ApplyOffset(ShadowStatType.Madness, -1,
                        "Nat 20 on Chaos — chaos mastered, not consumed");
                }

                // Nat 20 (any stat) → Dread −1 (#720)
                state.PlayerShadows?.ApplyOffset(ShadowStatType.Dread, -1,
                    "Nat 20 — existential confidence surge");
            }

            // 3c. Track last stat used for Fixation T3 (#45)
            state.LastStatUsed = chosenOption.Stat;

            // 3d. Combo detection (#46)
            state.ComboTracker.RecordTurn(chosenOption.Stat, rollResult.IsSuccess);
            var combo = state.ComboTracker.CheckCombo();
            string? comboTriggered = null;
            int comboBonusDelta = 0;
            if (combo != null)
            {
                comboBonusDelta = combo.InterestBonus;
                interestDelta += comboBonusDelta;
                comboTriggered = combo.Name;
            }

            // 3d. Record roll XP (#48)
            _xpRecorder.RecordRollXp(rollResult);

            // 4. Record interest before applying delta
            int interestBefore = state.Interest.Current;
            InterestState stateBefore = TurnOrchestratorHelpers.ResolveInterestState(state, _rules);

            // 5. Apply interest delta
            state.Interest.Apply(interestDelta);

            int interestAfter = state.Interest.Current;
            InterestState stateAfter = TurnOrchestratorHelpers.ResolveInterestState(state, _rules);

            // ---- Shadow growth evaluation (#44) ----
            _shadowGrowthEvaluator?.EvaluatePerTurn(
                chosenOption, optionIndex, rollResult, interestAfter, comboTriggered, hasTellOption,
                state.CurrentOptions,
                (chosen, opts) => GameSessionHelpers.IsHighestProbabilityOption(chosen, opts, player, datee, _rules));

            // Shadow reduction: Winning despite Overthinking disadvantage → Overthinking −1
            if (rollResult.IsSuccess
                && state.PlayerShadows != null
                && state.ShadowDisadvantagedStats != null
                && state.ShadowDisadvantagedStats.Contains(chosenOption.Stat)
                && StatBlock.ShadowPairs[chosenOption.Stat] == ShadowStatType.Overthinking)
            {
                state.PlayerShadows.ApplyOffset(ShadowStatType.Overthinking, -1,
                    "Succeeded despite Overthinking disadvantage");
            }

            // Shadow reduction: Success at interest ≥20 → Overthinking -1
            if (rollResult.IsSuccess && interestAfter >= 20)
            {
                state.PlayerShadows?.ApplyOffset(ShadowStatType.Overthinking, -1,
                    "Success at high interest — pressure lifts");
            }

            // Check end conditions for end-of-game triggers
            bool isGameOver = false;
            GameOutcome? outcome = null;

            if (state.Interest.IsZero)
            {
                state.Ended = true;
                state.Outcome = GameOutcome.Unmatched;
                isGameOver = true;
                outcome = GameOutcome.Unmatched;
                // End-of-game Dread +1: conversation ended without date
                state.PlayerShadows?.ApplyGrowth(ShadowStatType.Dread, 1, "Conversation ended without date");
            }
            else if (state.Interest.IsMaxed)
            {
                state.Ended = true;
                state.Outcome = GameOutcome.DateSecured;
                isGameOver = true;
                outcome = GameOutcome.DateSecured;
            }

            // End-of-game shadow growth checks
            if (isGameOver)
            {
                _shadowGrowthEvaluator?.EvaluateEndOfGame(outcome!.Value);
                _xpRecorder.RecordEndOfGameXp(outcome!.Value);
            }

            // Drain XP events for this turn (#48)
            var turnXpEvents = state.XpLedger.DrainTurnEvents();
            int turnXpEarned = 0;
            for (int i = 0; i < turnXpEvents.Count; i++)
                turnXpEarned += turnXpEvents[i].Amount;

            // Drain shadow growth events for this turn
            var shadowGrowthEvents = state.PlayerShadows != null
                ? state.PlayerShadows.DrainGrowthEvents()
                : (IReadOnlyList<string>)Array.Empty<string>();

            return new RollStageResult
            {
                ChosenOption = chosenOption,
                TrapClearedDisplayName = trapClearedDisplayName,
                CallbackBonus = callbackBonus,
                TellBonus = tellBonus,
                TripleBonusApplied = tripleBonusApplied,
                RollResult = rollResult,
                ResolveDice = resolveDice,
                BaseInterestDelta = baseInterestDelta,
                RiskBonusDelta = riskBonusDelta,
                InterestDelta = interestDelta,
                ComboTriggered = comboTriggered,
                ComboBonusDelta = comboBonusDelta,
                InterestBefore = interestBefore,
                StateBefore = stateBefore,
                InterestAfter = interestAfter,
                StateAfter = stateAfter,
                IsGameOver = isGameOver,
                Outcome = outcome,
                TurnXpEarned = turnXpEarned,
                ShadowGrowthEvents = shadowGrowthEvents
            };
        }
    }
}
