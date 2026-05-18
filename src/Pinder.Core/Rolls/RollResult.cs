using System.Text.Json.Serialization;
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
        [JsonPropertyName("stat")]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public StatType Stat { get; }

        /// <summary>
        /// The defending stat (opponent's stat used to compute the DC via StatBlock.DefenceTable[Stat]).
        /// Always populated for option-roll results; use StatBlock.DefenceTable[Stat] as the canonical source.
        /// </summary>
        [JsonPropertyName("defending_stat")]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public StatType DefendingStat { get; }

        /// <summary>Effective stat modifier at time of roll (after shadow penalty + traps).</summary>
        public int StatModifier { get; }

        /// <summary>Level bonus applied.</summary>
        public int LevelBonus { get; }

        /// <summary>
        /// Base total: UsedDieRoll + StatModifier + LevelBonus.
        /// Does not include external bonuses (callbacks, tells, combos, momentum).
        /// </summary>
        public int Total { get; }

        /// <summary>
        /// Additional bonus applied outside RollEngine (callback distance, tell read, combo, momentum).
        /// Defaults to 0. Set after construction by the caller that knows the context.
        /// </summary>
        public int ExternalBonus { get; private set; }

        /// <summary>
        /// True total including ExternalBonus. Use this for final success/fail determination
        /// when external bonuses have been applied.
        /// </summary>
        public int FinalTotal => Total + ExternalBonus;

        /// <summary>Apply an external bonus (callback, tell, combo, momentum). Additive.
        /// DEPRECATED: Use the externalBonus parameter on RollEngine.Resolve() or ResolveFixedDC() instead.</summary>
        [System.Obsolete("Use the externalBonus parameter on RollEngine.Resolve() or ResolveFixedDC() instead.")]
        public void AddExternalBonus(int bonus) { ExternalBonus += bonus; }

        /// <summary>DC that had to be beaten.</summary>
        public int DC { get; }

        /// <summary>True if FinalTotal >= DC (or Nat 20). Uses FinalTotal to account for external bonuses.</summary>
        public bool IsSuccess { get; }

        /// <summary>True if UsedDieRoll == 1 (auto-fail regardless of modifiers).</summary>
        public bool IsNatOne { get; }

        /// <summary>True if UsedDieRoll == 20 (auto-success regardless of DC).</summary>
        public bool IsNatTwenty { get; }

        /// <summary>Failure tier. None on success.</summary>
        [JsonPropertyName("tier")]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public FailureTier Tier { get; }

        /// <summary>
        /// Trap activated by this roll, if Tier == TropeTrap and no trap was already active.
        /// Null otherwise.
        /// </summary>
        public TrapDefinition? ActivatedTrap { get; }

        /// <summary>
        /// Canonical check result produced by <see cref="RollEngine.ResolveCheck"/>.
        /// Contains the raw d20 mechanics (roll, modifier bag, total, tier-from-miss-margin).
        /// Note: <c>Check.Tier</c> is from <see cref="FailureTierLadder.FromMissMargin"/> only;
        /// it will differ from <see cref="Tier"/> when UsedDieRoll == 1 (Legendary in Tier,
        /// Catastrophe in Check.Tier). Phase 2 will wire this to the DTO.
        /// </summary>
        public RollCheckResult Check { get; }

        /// <summary>
        /// Risk tier derived from the "need" value (dc - statMod - levelBonus).
        /// Safe (≤5), Medium (6–10), Hard (11–15), Bold (≥16).
        /// </summary>
        [JsonPropertyName("risk_tier")]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public RiskTier RiskTier { get; }

        /// <summary>By how much the roll missed the DC (using FinalTotal). 0 on success.</summary>
        public int MissMargin => IsSuccess ? 0 : DC - FinalTotal;

        /// <summary>
        /// Primary constructor. <paramref name="check"/> is required and stored verbatim on
        /// <see cref="Check"/>; callers outside <see cref="RollEngine"/> typically build it
        /// via <see cref="RollCheckResult.Synthesise"/>. The parameter order matches the
        /// pre-#920 signature so existing positional callers stay compatible — the only
        /// change is that <paramref name="check"/> is now non-nullable and required.
        /// </summary>
        public RollResult(
            int dieRoll,
            int? secondDieRoll,
            int usedDieRoll,
            StatType stat,
            int statModifier,
            int levelBonus,
            int dc,
            FailureTier tier,
            TrapDefinition? activatedTrap,
            int externalBonus,
            RollCheckResult check,
            StatType defendingStat = default)
        {
            DieRoll        = dieRoll;
            SecondDieRoll  = secondDieRoll;
            UsedDieRoll    = usedDieRoll;
            Stat           = stat;
            DefendingStat  = defendingStat;
            StatModifier   = statModifier;
            LevelBonus     = levelBonus;
            Total          = usedDieRoll + statModifier + levelBonus;
            ExternalBonus  = externalBonus;
            DC             = dc;
            IsNatOne       = usedDieRoll == 1;
            IsNatTwenty    = usedDieRoll == 20;
            IsSuccess      = IsNatTwenty || (!IsNatOne && FinalTotal >= dc);
            Tier           = IsSuccess ? FailureTier.Success : tier;
            ActivatedTrap  = activatedTrap;
            RiskTier       = ComputeRiskTier(dc, statModifier, levelBonus);
            Check          = check ?? throw new System.ArgumentNullException(nameof(check));
        }

        /// <summary>
        /// Convenience overload that synthesises <see cref="Check"/> from the bespoke fields
        /// via <see cref="RollCheckResult.Synthesise"/>. Use this when you don't already have
        /// a <see cref="RollCheckResult"/> from <see cref="RollEngine.ResolveCheck"/> — e.g.
        /// <c>GameSession.CreateForcedFailResult</c> and test fixtures that exercise the
        /// bespoke-field code paths without going through <see cref="RollEngine"/>.
        /// </summary>
        public RollResult(
            int dieRoll,
            int? secondDieRoll,
            int usedDieRoll,
            StatType stat,
            int statModifier,
            int levelBonus,
            int dc,
            FailureTier tier,
            TrapDefinition? activatedTrap = null,
            int externalBonus = 0,
            StatType defendingStat = default)
            : this(
                dieRoll,
                secondDieRoll,
                usedDieRoll,
                stat,
                statModifier,
                levelBonus,
                dc,
                tier,
                activatedTrap,
                externalBonus,
                RollCheckResult.Synthesise(
                    dieRoll,
                    secondDieRoll,
                    usedDieRoll,
                    statModifier,
                    levelBonus,
                    dc,
                    externalBonus),
                defendingStat)
        {
        }

        /// <summary>
        /// Compute risk tier from the "need" value: dc - (statMod + levelBonus).
        /// Canonical formula; DTOs/UI MUST call this rather than re-implement (#117).
        /// </summary>
        public static RiskTier ComputeRiskTier(int dc, int statModifier, int levelBonus)
        {
            int need = dc - (statModifier + levelBonus);

            if (need <= 7)  return RiskTier.Safe;
            if (need <= 11) return RiskTier.Medium;
            if (need <= 15) return RiskTier.Hard;
            if (need <= 19) return RiskTier.Bold;
            return RiskTier.Reckless;
        }

        public override string ToString() =>
            $"[{Stat}] d20={UsedDieRoll}+{StatModifier}+{LevelBonus}={Total} vs DC{DC} " +
            (IsSuccess ? "✓ SUCCESS" : $"✗ {Tier} (miss {MissMargin})");
    }
}
