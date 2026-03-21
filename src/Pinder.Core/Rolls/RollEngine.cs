using Pinder.Core.Interfaces;
using Pinder.Core.Progression;
using Pinder.Core.Stats;
using Pinder.Core.Traps;

namespace Pinder.Core.Rolls
{
    /// <summary>
    /// Core roll resolution engine. Stateless — all state is passed in.
    /// Formula: d20 + statMod + levelBonus >= DC
    /// DC    = 10 + opponent defending stat's effective modifier
    /// </summary>
    public static class RollEngine
    {
        /// <summary>
        /// Resolve a full roll.
        /// </summary>
        /// <param name="stat">Stat used by the attacker.</param>
        /// <param name="attacker">Attacker's stat block.</param>
        /// <param name="defender">Defender's stat block (used to compute DC).</param>
        /// <param name="attackerTraps">Active traps on the attacker (may force disadvantage or stat penalty).</param>
        /// <param name="level">Attacker's current level (1-based).</param>
        /// <param name="trapRegistry">Trap definitions for TropeTrap activation.</param>
        /// <param name="dice">Dice roller implementation.</param>
        /// <param name="hasAdvantage">Roll twice, take higher.</param>
        /// <param name="hasDisadvantage">Roll twice, take lower. Overrides advantage.</param>
        public static RollResult Resolve(
            StatType stat,
            StatBlock attacker,
            StatBlock defender,
            TrapState attackerTraps,
            int level,
            ITrapRegistry trapRegistry,
            IDiceRoller dice,
            bool hasAdvantage     = false,
            bool hasDisadvantage  = false)
        {
            // --- Determine advantage/disadvantage from active traps ---
            var activeTrap = attackerTraps.GetActive(stat);
            if (activeTrap != null)
            {
                if (activeTrap.Definition.Effect == TrapEffect.Disadvantage)
                    hasDisadvantage = true;
            }

            // Disadvantage overrides advantage (standard rule)
            bool rollTwice = hasAdvantage || hasDisadvantage;

            // --- Roll the dice ---
            int roll1 = dice.Roll(20);
            int? roll2 = rollTwice ? (int?)dice.Roll(20) : null;

            int usedRoll;
            if (!rollTwice)          usedRoll = roll1;
            else if (hasDisadvantage) usedRoll = roll2.HasValue ? System.Math.Min(roll1, roll2.Value) : roll1;
            else                      usedRoll = roll2.HasValue ? System.Math.Max(roll1, roll2.Value) : roll1;

            // --- Compute modifiers ---
            int statMod = attacker.GetEffective(stat);

            // Apply flat stat penalty from traps
            if (activeTrap != null && activeTrap.Definition.Effect == TrapEffect.StatPenalty)
                statMod -= activeTrap.Definition.EffectValue;

            int levelBonus = LevelTable.GetBonus(level);

            // --- Compute DC ---
            int dc = defender.GetDefenceDC(stat);

            // Apply DC increase traps (e.g. OpponentDCIncrease)
            // Note: caller should pre-compute these from the *defender's* active traps
            // but we honour them via the defender's stat block directly for now.

            // --- Determine failure tier ---
            FailureTier tier;
            TrapDefinition? newTrap = null;

            if (usedRoll == 1)
            {
                tier = FailureTier.Legendary;
            }
            else if (usedRoll == 20 || (usedRoll + statMod + levelBonus) >= dc)
            {
                tier = FailureTier.None; // success
            }
            else
            {
                int total    = usedRoll + statMod + levelBonus;
                int miss     = dc - total;

                if      (miss <= 2) tier = FailureTier.Fumble;
                else if (miss <= 5) tier = FailureTier.Misfire;
                else if (miss <= 9)
                {
                    tier = FailureTier.TropeTrap;
                    // Activate trap if one is defined and not already active on this stat
                    if (!attackerTraps.IsActive(stat))
                    {
                        newTrap = trapRegistry.GetTrap(stat);
                        if (newTrap != null)
                            attackerTraps.Activate(newTrap);
                    }
                }
                else tier = FailureTier.Catastrophe;
            }

            return new RollResult(
                dieRoll:       roll1,
                secondDieRoll: roll2,
                usedDieRoll:   usedRoll,
                stat:          stat,
                statModifier:  statMod,
                levelBonus:    levelBonus,
                dc:            dc,
                tier:          tier,
                activatedTrap: newTrap);
        }
    }
}
