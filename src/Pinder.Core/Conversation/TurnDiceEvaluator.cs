using System;
using Pinder.Core.Characters;
using Pinder.Core.I18n;
using Pinder.Core.Interfaces;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;
using Pinder.Core.Traps;

namespace Pinder.Core.Conversation
{
    /// <summary>
    /// Decouples dice pools arithmetic and roll outcome evaluation from TurnProcessor.
    /// </summary>
    public static class TurnDiceEvaluator
    {
        /// <summary>
        /// Evaluates the dice pools and roll outcomes for a dialogue option turn.
        /// </summary>
        /// <returns>A tuple containing the RollResult and the IDiceRoller used (resolveDice).</returns>
        public static (RollResult RollResult, IDiceRoller ResolveDice) EvaluateRolls(
            GameSessionState state,
            int optionIndex,
            DialogueOption chosenOption,
            CharacterProfile player,
            CharacterProfile opponent,
            IDiceRoller dice,
            ITrapRegistry trapRegistry,
            IConsequenceCatalog? consequenceCatalog,
            int externalBonus,
            int dcAdjustment,
            bool resolveHasDisadvantage)
        {
            // 1. Roll dice
            var chosenPool = state.InjectedNextPool != null
                ? state.InjectedNextPool
                : FillChosenDicePool(optionIndex, chosenOption, resolveHasDisadvantage, state.CurrentHasAdvantage, state.Traps, dice);
            state.InjectedNextPool = null; // single-use
            if (state.CurrentDicePools != null && optionIndex >= 0 && optionIndex < state.CurrentDicePools.Length)
                state.CurrentDicePools[optionIndex] = chosenPool;
                
            var resolveDice = (IDiceRoller)new PlaybackDiceRoller(chosenPool);

            var rollResult = RollEngine.Resolve(
                stat: chosenOption.Stat,
                attacker: player.Stats,
                defender: opponent.Stats,
                attackerTraps: state.Traps,
                level: player.Level,
                trapRegistry: trapRegistry,
                dice: resolveDice,
                hasAdvantage: state.CurrentHasAdvantage,
                hasDisadvantage: resolveHasDisadvantage,
                externalBonus: externalBonus,
                dcAdjustment: dcAdjustment);

            // #976: populate Consequence on the main option roll from i18n catalogue.
            if (consequenceCatalog != null && rollResult.Check != null)
            {
                string rollKey = ConsequenceKeys.ForRoll(rollResult.IsSuccess, rollResult.Tier);
                string? rollTemplate = consequenceCatalog.Lookup(rollKey);
                if (rollTemplate != null)
                {
                    string statName = chosenOption.Stat.ToString();
                    rollResult.Check.ApplyConsequence(ConsequenceKeys.ApplySlots(rollTemplate, statName));
                }
            }

            return (rollResult, resolveDice);
        }

        private static PerOptionDicePool FillChosenDicePool(
            int optionIndex, DialogueOption chosenOption, bool resolveHasDisadvantage, bool currentHasAdvantage, TrapState traps, IDiceRoller dice)
        {
            bool trapDisadvantage = false;
            var activeTrap = traps.GetActive(chosenOption.Stat);
            if (activeTrap != null
                && activeTrap.Definition.Effect == TrapEffect.Disadvantage)
                trapDisadvantage = true;

            bool rollTwice = currentHasAdvantage || resolveHasDisadvantage || trapDisadvantage;

            int rolls = rollTwice ? 3 : 2;
            var values = new int[rolls];
            int idx = 0;
            values[idx++] = dice.Roll(20);
            if (rollTwice)
                values[idx++] = dice.Roll(20);
            values[idx++] = dice.Roll(100);

            return new PerOptionDicePool(optionIndex, values);
        }
    }
}
