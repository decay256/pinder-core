using Pinder.Core.Interfaces;
using Pinder.Core.Progression;
using Pinder.Core.Stats;
using Pinder.Core.Traps;

namespace Pinder.Core.Rolls
{
    /// <summary>
    /// Core roll resolution engine. Stateless — all state is passed in.
    /// Formula: d20 + statMod + levelBonus >= DC
    /// DC    = 16 + opponent defending stat's effective modifier
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
            bool hasDisadvantage  = false,
            int externalBonus     = 0,
            int dcAdjustment      = 0)
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
            int dc = defender.GetDefenceDC(stat) - dcAdjustment;

            // --- Determine failure tier ---
            return ResolveFromComponents(stat, usedRoll, statMod, levelBonus, dc,
                roll1, roll2, externalBonus, attackerTraps, trapRegistry);
        }

        /// <summary>
        /// Resolve a roll against a fixed DC instead of computing DC from a defender.
        /// All other mechanics (trap effects, advantage/disadvantage, failure tiers) are identical to Resolve().
        /// </summary>
        /// <param name="stat">Stat used by the attacker.</param>
        /// <param name="attacker">Attacker's stat block.</param>
        /// <param name="fixedDc">The DC to roll against (caller-specified).</param>
        /// <param name="attackerTraps">Active traps on the attacker.</param>
        /// <param name="level">Attacker's current level (1-based).</param>
        /// <param name="trapRegistry">Trap definitions for TropeTrap activation.</param>
        /// <param name="dice">Dice roller implementation.</param>
        /// <param name="hasAdvantage">Roll twice, take higher.</param>
        /// <param name="hasDisadvantage">Roll twice, take lower. Overrides advantage.</param>
        /// <param name="externalBonus">External bonus passed into RollResult.</param>
        public static RollResult ResolveFixedDC(
            StatType stat,
            StatBlock attacker,
            int fixedDc,
            TrapState attackerTraps,
            int level,
            ITrapRegistry trapRegistry,
            IDiceRoller dice,
            bool hasAdvantage     = false,
            bool hasDisadvantage  = false,
            int externalBonus     = 0)
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

            // --- Determine failure tier ---
            return ResolveFromComponents(stat, usedRoll, statMod, levelBonus, fixedDc,
                roll1, roll2, externalBonus, attackerTraps, trapRegistry);
        }

        /// <summary>
        /// Shared failure-tier determination and RollResult construction used by both Resolve and ResolveFixedDC.
        /// </summary>
        private static RollResult ResolveFromComponents(
            StatType stat,
            int usedRoll,
            int statMod,
            int levelBonus,
            int dc,
            int roll1,
            int? roll2,
            int externalBonus,
            TrapState attackerTraps,
            ITrapRegistry trapRegistry)
        {
            FailureTier tier;
            TrapDefinition? newTrap = null;

            int total = usedRoll + statMod + levelBonus;
            int finalTotal = total + externalBonus;

            if (usedRoll == 1)
            {
                tier = FailureTier.Legendary;
            }
            else if (usedRoll == 20 || finalTotal >= dc)
            {
                tier = FailureTier.None; // success
            }
            else
            {
                int miss = dc - finalTotal;

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
                else
                {
                    tier = FailureTier.Catastrophe;
                    // Catastrophe also activates trap (rules §5: miss 10+ = -3 + trap)
                    if (!attackerTraps.IsActive(stat))
                    {
                        newTrap = trapRegistry.GetTrap(stat);
                        if (newTrap != null)
                            attackerTraps.Activate(newTrap);
                    }
                }
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
                activatedTrap: newTrap,
                externalBonus: externalBonus);
        }
    }
}
