using System;
using System.Collections.Generic;
using Pinder.Core.Interfaces;
using Pinder.Core.Progression;
using Pinder.Core.Stats;
using Pinder.Core.Traps;

namespace Pinder.Core.Rolls
{
    /// <summary>
    /// Core roll resolution engine. Stateless — all state is passed in.
    /// Formula: d20 + statMod + levelBonus >= DC
    /// DC    = 16 + datee defending stat's effective modifier
    /// </summary>
    public static class RollEngine
    {
        /// <summary>
        /// Applies a difficulty DC bias to a base DC.
        /// Positive bias lowers the effective DC, making the check easier.
        /// </summary>
        public static int ApplyDcBias(int baseDc, int bias) => baseDc - bias;

        /// <summary>
        /// Single entry point for all d20 checks.
        /// Rolls a d20 (with optional advantage/disadvantage), sums the modifier bag,
        /// computes total vs DC, and returns a canonical <see cref="RollCheckResult"/>.
        /// </summary>
        /// <remarks>
        /// <c>IsSuccess = Total >= Dc</c> — nat-20 auto-success and nat-1 auto-fail (Legendary)
        /// are game-rule overrides that live in <see cref="ResolveFromComponents"/> for the
        /// main option-roll only. Informational <c>IsNatOne</c>/<c>IsNatTwenty</c> flags are
        /// always populated.
        /// </remarks>
        public static RollCheckResult ResolveCheck(
            RollCheckKind kind,
            IDiceRoller dice,
            IReadOnlyList<NamedModifier> modifiers,
            int dc,
            bool hasAdvantage    = false,
            bool hasDisadvantage = false)
        {
            if (dice == null) throw new ArgumentNullException(nameof(dice));
            if (modifiers == null) throw new ArgumentNullException(nameof(modifiers));

            bool rollTwice = hasAdvantage || hasDisadvantage;
            int roll1 = dice.Roll(20);
            int? roll2 = rollTwice ? (int?)dice.Roll(20) : null;

            int usedRoll;
            if (!rollTwice)           usedRoll = roll1;
            else if (hasDisadvantage) usedRoll = roll2.HasValue ? Math.Min(roll1, roll2.Value) : roll1;
            else                      usedRoll = roll2.HasValue ? Math.Max(roll1, roll2.Value) : roll1;

            int modSum = 0;
            foreach (var m in modifiers) modSum += m.Value;

            int total      = usedRoll + modSum;
            bool isSuccess = total >= dc;
            bool isNatOne  = usedRoll == 1;
            bool isNatTwenty = usedRoll == 20;
            int missMargin = isSuccess ? 0 : dc - total;
            FailureTier tier = isSuccess ? FailureTier.Success : FailureTierLadder.FromMissMargin(missMargin);

            return new RollCheckResult(
                kind, roll1, roll2, usedRoll, modifiers, modSum, total, dc,
                isSuccess, isNatOne, isNatTwenty, tier, missMargin);
        }

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

            // Apply DateeDCIncrease trap effect
            if (activeTrap != null && activeTrap.Definition.Effect == TrapEffect.DateeDCIncrease)
                dc += activeTrap.Definition.EffectValue;

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

            // Apply DateeDCIncrease trap effect
            int effectiveDc = fixedDc;
            if (activeTrap != null && activeTrap.Definition.Effect == TrapEffect.DateeDCIncrease)
                effectiveDc += activeTrap.Definition.EffectValue;

            // --- Determine failure tier ---
            return ResolveFromComponents(stat, usedRoll, statMod, levelBonus, effectiveDc,
                roll1, roll2, externalBonus, attackerTraps, trapRegistry);
        }

        /// <summary>
        /// Shared failure-tier determination and RollResult construction used by both Resolve and ResolveFixedDC.
        /// Routes the miss-margin tier ladder through <see cref="FailureTierLadder.FromMissMargin"/> (#901)
        /// and attaches a <see cref="RollCheckResult"/> on the returned <see cref="RollResult"/>.
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

            // Per #371 (W2a): single-slot trap state. Trap-activating failure tiers
            // (Legendary / TropeTrap / Catastrophe) ALWAYS activate the stat's trap
            // when the roll trips that tier — the new activation REPLACES whatever
            // was active (single-slot rule). This is a behaviour change from the prior
            // "only activate if not already active on this stat" guard.
            if (usedRoll == 1)
            {
                tier = FailureTier.Legendary;
                // Nat 1 activates a trap (rules: Legendary fail = trap)
                newTrap = trapRegistry.GetTrap(stat);
                if (newTrap != null)
                    attackerTraps.Activate(newTrap);
            }
            else if (usedRoll == 20 || finalTotal >= dc)
            {
                tier = FailureTier.Success; // success
            }
            else
            {
                int miss = dc - finalTotal;
                // #901: single tier-ladder source of truth
                tier = FailureTierLadder.FromMissMargin(miss);
                if (tier == FailureTier.TropeTrap || tier == FailureTier.Catastrophe)
                {
                    // Activate the stat's trap (single-slot replacement, #371).
                    newTrap = trapRegistry.GetTrap(stat);
                    if (newTrap != null)
                        attackerTraps.Activate(newTrap);
                }
            }

            // #901: build canonical RollCheckResult (modifier bag view of this roll).
            // Note: Check.Tier uses FailureTierLadder only (no Legendary); RollResult.Tier
            // applies the nat-1 → Legendary game-rule override above.
            var checkModifiers = new NamedModifier[]
            {
                new NamedModifier("stat",  statMod),
                new NamedModifier("level", levelBonus),
            };
            int checkMissMargin = (usedRoll == 20 || total + externalBonus >= dc) ? 0 : dc - (total + externalBonus);
            bool checkIsSuccess = (total + externalBonus) >= dc;
            FailureTier checkTier = checkIsSuccess ? FailureTier.Success : FailureTierLadder.FromMissMargin(checkMissMargin);
            var check = new RollCheckResult(
                RollCheckKind.OptionRoll,
                roll1, roll2, usedRoll,
                checkModifiers,
                modifierSum: statMod + levelBonus,
                total:       total + externalBonus,
                dc:          dc,
                isSuccess:   checkIsSuccess,
                isNatOne:    usedRoll == 1,
                isNatTwenty: usedRoll == 20,
                tier:        checkTier,
                missMargin:  checkMissMargin);

            return new RollResult(
                dieRoll:        roll1,
                secondDieRoll:  roll2,
                usedDieRoll:    usedRoll,
                stat:           stat,
                statModifier:   statMod,
                levelBonus:     levelBonus,
                dc:             dc,
                tier:           tier,
                activatedTrap:  newTrap,
                externalBonus:  externalBonus,
                check:          check,
                defendingStat:  StatBlock.DefenceTable[stat]);
        }
    }
}
