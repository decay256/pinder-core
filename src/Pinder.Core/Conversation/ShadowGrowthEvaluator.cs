using System.Collections.Generic;
using System.Linq;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;

namespace Pinder.Core.Conversation
{
    /// <summary>
    /// Evaluates shadow growth triggers per-turn and end-of-game.
    /// Owns the mutable tracking state (stat usage counts, streak flags).
    /// Created once per GameSession and mutated across turns.
    /// </summary>
    internal sealed class ShadowGrowthEvaluator
    {
        private readonly SessionShadowTracker _playerShadows;
        private readonly List<StatType> _statsUsedPerTurn = new List<StatType>();
        private readonly List<bool> _highestPctOptionPicked = new List<bool>();
        private int _honestySuccessCount;
        private int _saUsageCount;
        private int _charmUsageCount;
        private bool _charmMadnessTriggered;
        private bool _saOverthinkingTriggered;
        private int _rizzCumulativeFailureCount;

        public ShadowGrowthEvaluator(SessionShadowTracker playerShadows)
        {
            _playerShadows = playerShadows;
        }

        /// <summary>Number of successful Honesty rolls this session.</summary>
        public int HonestySuccessCount => _honestySuccessCount;

        /// <summary>All stats used per turn (for end-of-game evaluation).</summary>
        public IReadOnlyList<StatType> StatsUsedPerTurn => _statsUsedPerTurn;

        /// <summary>
        /// Evaluates per-turn shadow growth triggers after a Speak action resolves.
        /// </summary>
        public void EvaluatePerTurn(
            DialogueOption chosenOption,
            int optionIndex,
            RollResult rollResult,
            int interestAfter,
            string? comboTriggered,
            bool hasTellOption,
            DialogueOption[] currentOptions,
            IsHighestProbabilityFunc isHighestProbability)
        {
            // Trigger 1: Nat 1 → +1 to paired shadow (+2 for Rizz Nat 1 → Despair #708)
            if (rollResult.IsNatOne)
            {
                var pairedShadow = StatBlock.ShadowPairs[chosenOption.Stat];
                int natOneAmount = chosenOption.Stat == StatType.Rizz ? 2 : 1;
                _playerShadows.ApplyGrowth(pairedShadow, natOneAmount,
                    $"Nat 1 on {chosenOption.Stat}");
            }

            // Trigger 2: Catastrophic Wit failure → +1 Dread
            if (chosenOption.Stat == StatType.Wit
                && !rollResult.IsSuccess
                && rollResult.Tier == FailureTier.Catastrophe)
            {
                _playerShadows.ApplyGrowth(ShadowStatType.Dread, 1,
                    "Catastrophic Wit failure (miss by 10+)");
            }

            // Trigger 3: Every TropeTrap failure → +1 Madness (Legendary/Nat1 excluded — handled by Trigger 1)
            if (!rollResult.IsSuccess && rollResult.Tier >= FailureTier.TropeTrap
                && rollResult.Tier != FailureTier.Legendary)
            {
                _playerShadows.ApplyGrowth(ShadowStatType.Madness, 1,
                    "TropeTrap failure");
            }

            // Trigger 3b: RIZZ TropeTrap failure → +1 Despair (#708)
            if (chosenOption.Stat == StatType.Rizz
                && !rollResult.IsSuccess
                && rollResult.Tier >= FailureTier.TropeTrap
                && rollResult.Tier != FailureTier.Legendary)
            {
                _playerShadows.ApplyGrowth(ShadowStatType.Despair, 1,
                    "RIZZ TropeTrap failure");
            }

            // Trigger 3c: Every 3rd cumulative RIZZ failure → +1 Despair (#717)
            if (chosenOption.Stat == StatType.Rizz && !rollResult.IsSuccess)
            {
                _rizzCumulativeFailureCount++;
                if (_rizzCumulativeFailureCount % 3 == 0)
                {
                    _playerShadows.ApplyGrowth(ShadowStatType.Despair, 1,
                        "3rd cumulative RIZZ failure");
                }
            }

            // Trigger 4: Same stat 3 turns in a row → +1 Fixation
            _statsUsedPerTurn.Add(chosenOption.Stat);
            if (_statsUsedPerTurn.Count >= 3)
            {
                int tail = _statsUsedPerTurn.Count;
                if (_statsUsedPerTurn[tail - 1] == _statsUsedPerTurn[tail - 2]
                    && _statsUsedPerTurn[tail - 2] == _statsUsedPerTurn[tail - 3])
                {
                    int consecutiveCount = 1;
                    for (int i = tail - 2; i >= 0; i--)
                    {
                        if (_statsUsedPerTurn[i] == _statsUsedPerTurn[tail - 1])
                            consecutiveCount++;
                        else
                            break;
                    }
                    if (consecutiveCount >= 3 && consecutiveCount % 3 == 0)
                    {
                        _playerShadows.ApplyGrowth(ShadowStatType.Fixation, 1,
                            $"Same stat ({chosenOption.Stat}) used 3 turns in a row");
                    }
                }
            }

            // Trigger 5: Highest-% option picked 3 turns in a row → +1 Fixation
            _highestPctOptionPicked.Add(isHighestProbability(chosenOption, currentOptions));
            if (_highestPctOptionPicked.Count >= 3)
            {
                int tail = _highestPctOptionPicked.Count;
                if (_highestPctOptionPicked[tail - 1]
                    && _highestPctOptionPicked[tail - 2]
                    && _highestPctOptionPicked[tail - 3])
                {
                    int consecutiveCount = 0;
                    for (int i = tail - 1; i >= 0; i--)
                    {
                        if (_highestPctOptionPicked[i])
                            consecutiveCount++;
                        else
                            break;
                    }
                    if (consecutiveCount >= 3 && consecutiveCount % 3 == 0)
                    {
                        _playerShadows.ApplyGrowth(ShadowStatType.Fixation, 1,
                            "Highest-% option picked 3 turns in a row");
                    }
                }
            }

            // Trigger 6: Honesty success tracking + Denial reduction at high interest
            if (chosenOption.Stat == StatType.Honesty && rollResult.IsSuccess)
            {
                _honestySuccessCount++;

                if (interestAfter >= 15)
                {
                    _playerShadows.ApplyOffset(ShadowStatType.Denial, -1,
                        "Honesty success at high interest");
                }
            }

            // Shadow reduction: SA/Honesty success at Interest >18 → Despair −1 (#717)
            if (rollResult.IsSuccess
                && (chosenOption.Stat == StatType.SelfAwareness || chosenOption.Stat == StatType.Honesty)
                && interestAfter > 18)
            {
                _playerShadows.ApplyOffset(ShadowStatType.Despair, -1,
                    "SA/Honesty success at high interest");
            }

            // Trigger 7: Interest hits 0 → +2 Dread
            if (interestAfter == 0)
            {
                _playerShadows.ApplyGrowth(ShadowStatType.Dread, 2,
                    "Interest hit 0 (unmatch)");
            }

            // Trigger 9: SA used 3+ times → +1 Overthinking (once)
            if (chosenOption.Stat == StatType.SelfAwareness)
            {
                _saUsageCount++;
                if (_saUsageCount == 3 && !_saOverthinkingTriggered)
                {
                    _saOverthinkingTriggered = true;
                    _playerShadows.ApplyGrowth(ShadowStatType.Overthinking, 1,
                        "SA used 3+ times in one conversation");
                }
            }

            // Trigger 15: CHARM used 3+ times → +1 Madness (once)
            if (chosenOption.Stat == StatType.Charm)
            {
                _charmUsageCount++;
                if (_charmUsageCount == 3 && !_charmMadnessTriggered)
                {
                    _charmMadnessTriggered = true;
                    _playerShadows.ApplyGrowth(ShadowStatType.Madness, 1,
                        "CHARM used 3+ times in one conversation");
                }
            }

            // Shadow reduction: Combo success → Madness -1
            if (comboTriggered != null)
            {
                _playerShadows.ApplyOffset(ShadowStatType.Madness, -1,
                    $"Combo success ({comboTriggered})");
            }

            // Shadow reduction: CHAOS combo → Fixation -1
            if (comboTriggered != null && chosenOption.Stat == StatType.Chaos)
            {
                _playerShadows.ApplyOffset(ShadowStatType.Fixation, -1,
                    $"CHAOS combo ({comboTriggered})");
            }

            // Shadow reduction: Tell option selected → Madness -1
            if (hasTellOption)
            {
                _playerShadows.ApplyOffset(ShadowStatType.Madness, -1,
                    "Tell option selected");
            }
        }

        /// <summary>
        /// Evaluates end-of-game shadow growth triggers.
        /// </summary>
        public void EvaluateEndOfGame(GameOutcome outcome)
        {
            // Shadow reduction: Date secured → Dread −1
            if (outcome == GameOutcome.DateSecured)
            {
                _playerShadows.ApplyOffset(ShadowStatType.Dread, -1,
                    "Date secured");
            }

            // Trigger 11: Date secured without Honesty success → +1 Denial
            if (outcome == GameOutcome.DateSecured && _honestySuccessCount == 0)
            {
                _playerShadows.ApplyGrowth(ShadowStatType.Denial, 1,
                    "Date secured without any Honesty successes");
            }

            // Trigger 12: Never picked Chaos → +1 Fixation
            if (!_statsUsedPerTurn.Contains(StatType.Chaos))
            {
                _playerShadows.ApplyGrowth(ShadowStatType.Fixation, 1,
                    "Never picked Chaos in whole conversation");
            }

            // Trigger 13: 4+ different stats used → −1 Fixation (offset)
            int distinctStats = _statsUsedPerTurn.Distinct().Count();
            if (distinctStats >= 4)
            {
                _playerShadows.ApplyOffset(ShadowStatType.Fixation, -1,
                    "4+ different stats used in conversation");
            }
        }

        /// <summary>
        /// Delegate type for checking if a chosen option is the highest probability option.
        /// </summary>
        public delegate bool IsHighestProbabilityFunc(DialogueOption chosen, DialogueOption[] options);
    }
}
