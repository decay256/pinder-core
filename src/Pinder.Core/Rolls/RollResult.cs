using Pinder.Core.Stats;
using Pinder.Core.Traps;

namespace Pinder.Core.Rolls
{
    /// <summary>
    /// Full result of a single roll resolution.
    /// </summary>
    public sealed class RollResult
    {
        /// <summary>Raw d20 result (1–20).</summary>
        public int DieRoll { get; }

        /// <summary>Second die roll if advantage/disadvantage was applied.</summary>
        public int? SecondDieRoll { get; }

        /// <summary>The die roll that was actually used after advantage/disadvantage.</summary>
        public int UsedDieRoll { get; }

        /// <summary>The stat used for the roll.</summary>
        public StatType Stat { get; }

        /// <summary>Effective stat modifier at time of roll (after shadow penalty + traps).</summary>
        public int StatModifier { get; }

        /// <summary>Level bonus applied.</summary>
        public int LevelBonus { get; }

        /// <summary>Total: UsedDieRoll + StatModifier + LevelBonus.</summary>
        public int Total { get; }

        /// <summary>DC that had to be beaten.</summary>
        public int DC { get; }

        /// <summary>True if Total >= DC (or Nat 20).</summary>
        public bool IsSuccess { get; }

        /// <summary>True if UsedDieRoll == 1 (auto-fail regardless of modifiers).</summary>
        public bool IsNatOne { get; }

        /// <summary>True if UsedDieRoll == 20 (auto-success regardless of DC).</summary>
        public bool IsNatTwenty { get; }

        /// <summary>Failure tier. None on success.</summary>
        public FailureTier Tier { get; }

        /// <summary>
        /// Trap activated by this roll, if Tier == TropeTrap and no trap was already active.
        /// Null otherwise.
        /// </summary>
        public TrapDefinition? ActivatedTrap { get; }

        /// <summary>
        /// Risk tier derived from the "need" value (dc - statMod - levelBonus).
        /// Safe (≤5), Medium (6–10), Hard (11–15), Bold (≥16).
        /// </summary>
        public RiskTier RiskTier { get; }

        /// <summary>By how much the roll missed the DC. 0 on success.</summary>
        public int MissMargin => IsSuccess ? 0 : DC - Total;

        public RollResult(
            int dieRoll,
            int? secondDieRoll,
            int usedDieRoll,
            StatType stat,
            int statModifier,
            int levelBonus,
            int dc,
            FailureTier tier,
            TrapDefinition? activatedTrap = null)
        {
            DieRoll        = dieRoll;
            SecondDieRoll  = secondDieRoll;
            UsedDieRoll    = usedDieRoll;
            Stat           = stat;
            StatModifier   = statModifier;
            LevelBonus     = levelBonus;
            Total          = usedDieRoll + statModifier + levelBonus;
            DC             = dc;
            IsNatOne       = usedDieRoll == 1;
            IsNatTwenty    = usedDieRoll == 20;
            IsSuccess      = IsNatTwenty || (!IsNatOne && Total >= dc);
            Tier           = IsSuccess ? FailureTier.None : tier;
            ActivatedTrap  = activatedTrap;
            RiskTier       = ComputeRiskTier(dc, statModifier, levelBonus);
        }

        /// <summary>
        /// Compute risk tier from the "need" value: dc - (statMod + levelBonus).
        /// </summary>
        private static RiskTier ComputeRiskTier(int dc, int statModifier, int levelBonus)
        {
            int need = dc - (statModifier + levelBonus);

            if (need <= 5)  return RiskTier.Safe;
            if (need <= 10) return RiskTier.Medium;
            if (need <= 15) return RiskTier.Hard;
            return RiskTier.Bold;
        }

        public override string ToString() =>
            $"[{Stat}] d20={UsedDieRoll}+{StatModifier}+{LevelBonus}={Total} vs DC{DC} " +
            (IsSuccess ? "✓ SUCCESS" : $"✗ {Tier} (miss {MissMargin})");
    }
}
