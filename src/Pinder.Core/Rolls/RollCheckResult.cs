using System.Collections.Generic;
using System.Text.Json.Serialization;

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

        /// <summary>
        /// Post-resolution verdict, after every in-engine override (shadow-corruption demotion etc.)
        /// has been applied (#927). Defaults to <see cref="RollVerdict.Success"/> / <see cref="RollVerdict.Miss"/>
        /// matching <see cref="IsSuccess"/> at construction time. Engine code with extra context
        /// (e.g. <c>GameSession</c>'s shadow-corruption block) overrides this via
        /// <see cref="ApplyFinalOverride"/>.
        ///
        /// Single source of truth for downstream consumers (frontend, replay tool, simulator) —
        /// they must NOT re-derive demotion from <c>shadow_check.is_miss && shadow_check.overlay_applied
        /// && roll.is_success</c>. See #927.
        /// </summary>
        [JsonPropertyName("final_verdict")]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public RollVerdict FinalVerdict { get; private set; }

        /// <summary>
        /// Post-resolution failure tier, after every in-engine override has been applied (#927).
        /// Defaults to <see cref="Tier"/> at construction time. <see cref="FailureTier.Success"/>
        /// when the final verdict is <see cref="RollVerdict.Success"/>.
        ///
        /// Single source of truth for the post-shadow-corruption failure tier — frontend and
        /// replay code MUST NOT read <c>shadow_check.tier</c> when <c>shadow_check.overlay_applied</c>
        /// to figure out the "real" tier. See #927.
        /// </summary>
        [JsonPropertyName("final_tier")]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public FailureTier FinalTier { get; private set; }

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

            // #927: FinalVerdict / FinalTier default to the pre-shadow values.
            // GameSession's shadow-corruption block overrides via ApplyFinalOverride.
            FinalVerdict = isSuccess ? RollVerdict.Success : RollVerdict.Miss;
            FinalTier    = tier;
        }

        /// <summary>
        /// Override <see cref="FinalVerdict"/> + <see cref="FinalTier"/> to reflect a
        /// post-resolution outcome (shadow-corruption demotion, etc.). Engine-internal —
        /// callers OUTSIDE the engine (frontend, replay, simulator) must read the fields,
        /// not write them. See #927.
        /// </summary>
        /// <remarks>
        /// This mutates an otherwise-immutable record. The two fields are deliberately
        /// the only mutable surface on <see cref="RollCheckResult"/>; everything else
        /// remains constructor-set. Existing <see cref="IsSuccess"/> / <see cref="Tier"/>
        /// semantics are preserved (back-compat is load-bearing).
        /// </remarks>
        internal void ApplyFinalOverride(RollVerdict verdict, FailureTier tier)
        {
            FinalVerdict = verdict;
            FinalTier    = tier;
        }

        /// <summary>
        /// Human-readable consequence text for this check result (#964).
        /// Population deferred to follow-up. SPA falls back to client-side i18n catalogue when null.
        /// </summary>
        [JsonPropertyName("consequence")]
        public string? Consequence { get; private set; }

        /// <summary>
        /// Apply an engine-populated consequence string. Idempotent — throws
        /// <see cref="System.InvalidOperationException"/> if already set (#964).
        /// </summary>
        public void ApplyConsequence(string consequence)
        {
            if (Consequence != null)
                throw new System.InvalidOperationException("Consequence already applied");
            Consequence = consequence;
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
