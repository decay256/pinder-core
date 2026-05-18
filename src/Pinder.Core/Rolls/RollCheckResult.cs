using System.Collections.Generic;

namespace Pinder.Core.Rolls
{
    /// <summary>
    /// Canonical result of a single d20 check performed by <see cref="RollEngine.ResolveCheck"/>.
    /// All per-check engines (horniness, shadow, steering, option-roll) produce one of these.
    /// Phase 1 (additive): attached as a <c>Check</c> property on each per-check result wrapper.
    /// Wire-DTO serialisation happens in Phase 2.
    /// </summary>
    /// <remarks>
    /// <c>IsNatOne</c> and <c>IsNatTwenty</c> are informational.
    /// <c>Tier</c> is derived solely from <see cref="FailureTierLadder.FromMissMargin"/> —
    /// the <c>Legendary</c> tier (nat-1 in the main option-roll) is a game-rule concern handled
    /// by <see cref="RollEngine.ResolveFromComponents"/>, not by this record.
    /// </remarks>
    public sealed class RollCheckResult
    {
        /// <summary>The kind of check that was performed.</summary>
        public RollCheckKind Kind { get; }

        /// <summary>Raw d20 result (1–20). Always the first die rolled.</summary>
        public int DieRoll { get; }

        /// <summary>Second die roll if advantage/disadvantage was applied, otherwise null.</summary>
        public int? SecondDieRoll { get; }

        /// <summary>The die roll that was actually used after advantage/disadvantage selection.</summary>
        public int UsedDieRoll { get; }

        /// <summary>Modifier bag as supplied by the caller. Preserved as-given.</summary>
        public IReadOnlyList<NamedModifier> Modifiers { get; }

        /// <summary>Sum of all modifier values in the bag.</summary>
        public int ModifierSum { get; }

        /// <summary>UsedDieRoll + ModifierSum.</summary>
        public int Total { get; }

        /// <summary>DC the roll had to meet or exceed.</summary>
        public int Dc { get; }

        /// <summary>True if Total >= Dc. Does NOT apply nat-20 auto-success or nat-1 auto-fail
        /// (those are game-rule overrides in RollEngine.ResolveFromComponents).</summary>
        public bool IsSuccess { get; }

        /// <summary>True if UsedDieRoll == 1 (informational; does not force Legendary here).</summary>
        public bool IsNatOne { get; }

        /// <summary>True if UsedDieRoll == 20 (informational; does not force success here).</summary>
        public bool IsNatTwenty { get; }

        /// <summary>Failure tier from FailureTierLadder.FromMissMargin. None on success.</summary>
        public FailureTier Tier { get; }

        /// <summary>By how much the roll missed the DC. 0 on success.</summary>
        public int MissMargin { get; }

        public RollCheckResult(
            RollCheckKind kind,
            int dieRoll,
            int? secondDieRoll,
            int usedDieRoll,
            IReadOnlyList<NamedModifier> modifiers,
            int modifierSum,
            int total,
            int dc,
            bool isSuccess,
            bool isNatOne,
            bool isNatTwenty,
            FailureTier tier,
            int missMargin)
        {
            Kind         = kind;
            DieRoll      = dieRoll;
            SecondDieRoll = secondDieRoll;
            UsedDieRoll  = usedDieRoll;
            Modifiers    = modifiers;
            ModifierSum  = modifierSum;
            Total        = total;
            Dc           = dc;
            IsSuccess    = isSuccess;
            IsNatOne     = isNatOne;
            IsNatTwenty  = isNatTwenty;
            Tier         = tier;
            MissMargin   = missMargin;
        }

        /// <summary>
        /// Synthesise a <see cref="RollCheckResult"/> from the bespoke fields a
        /// <see cref="RollResult"/> already carries. Used by callers that build a
        /// <see cref="RollResult"/> outside <see cref="RollEngine"/> (e.g.
        /// <c>GameSession.CreateForcedFailResult</c>, test fixtures) so the
        /// <c>Check</c> property is never null. Modifier bag is reconstructed as
        /// <c>[stat, level]</c> from <paramref name="statModifier"/> /
        /// <paramref name="levelBonus"/>; <paramref name="externalBonus"/> is folded
        /// into <c>Total</c> the same way <see cref="RollEngine.ResolveCheck"/> would.
        /// </summary>
        /// <remarks>
        /// <c>Tier</c> here is derived from <see cref="FailureTierLadder.FromMissMargin"/>
        /// only — it can differ from the bespoke <see cref="RollResult.Tier"/> on a
        /// nat-1 (Legendary in RollResult.Tier vs. Catastrophe here). This mirrors the
        /// behaviour documented on <see cref="RollResult.Check"/>.
        /// </remarks>
        public static RollCheckResult Synthesise(
            int dieRoll,
            int? secondDieRoll,
            int usedDieRoll,
            int statModifier,
            int levelBonus,
            int dc,
            int externalBonus = 0,
            RollCheckKind kind = RollCheckKind.OptionRoll)
        {
            var modifiers = new NamedModifier[]
            {
                new NamedModifier("stat",  statModifier),
                new NamedModifier("level", levelBonus),
            };
            int total       = usedDieRoll + statModifier + levelBonus + externalBonus;
            bool isNatOne   = usedDieRoll == 1;
            bool isNatTwenty = usedDieRoll == 20;
            bool isSuccess  = total >= dc;
            int missMargin  = isSuccess ? 0 : dc - total;
            FailureTier tier = isSuccess
                ? FailureTier.Success
                : FailureTierLadder.FromMissMargin(missMargin);

            return new RollCheckResult(
                kind,
                dieRoll,
                secondDieRoll,
                usedDieRoll,
                modifiers,
                modifierSum: statModifier + levelBonus,
                total:       total,
                dc:          dc,
                isSuccess:   isSuccess,
                isNatOne:    isNatOne,
                isNatTwenty: isNatTwenty,
                tier:        tier,
                missMargin:  missMargin);
        }
    }
}
