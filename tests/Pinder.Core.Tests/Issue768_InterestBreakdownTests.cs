using System;
using System.Linq;
using Pinder.Core.Conversation;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;
using Xunit;

namespace Pinder.Core.Tests
{
    /// <summary>
    /// Tests for Issue #768 / PNDR-23 — itemized interest breakdown with sum invariant.
    ///
    /// <para>
    /// Requirements verified here:
    /// <list type="bullet">
    /// <item><term>Sum invariant</term> breakdown.Sum(x => x.Delta) == InterestDelta for all cases.</item>
    /// <item><term>Non-zero filter</term> zero components are excluded from the list.</item>
    /// <item><term>Source keys</term> stable machine-readable keys are used.</item>
    /// <item><term>ShadowInterestDelta</term> exposed on TurnResult and appears in the breakdown.</item>
    /// <item><term>Live session 41cc7a50</term> roll 19 vs DC 17, Dread Misfire shadow overlay,
    ///   Hard risk tier (+3), net interest 10 → 9 (delta_total = −1).</item>
    /// </list>
    /// </para>
    /// </summary>
    [Trait("Category", "Core")]
    public class Issue768_InterestBreakdownTests
    {
        // ── Helpers ───────────────────────────────────────────────────────────

        private static RollResult MakeSuccessRoll(
            int dieRoll = 14,
            int dc = 12,
            int statModifier = 0)
        {
            return new RollResult(
                dieRoll: dieRoll,
                secondDieRoll: null,
                usedDieRoll: dieRoll,
                stat: StatType.Charm,
                statModifier: statModifier,
                levelBonus: 0,
                dc: dc,
                tier: FailureTier.Success,
                activatedTrap: null,
                externalBonus: 0);
        }

        private static GameStateSnapshot MakeSnapshot(int interest = 15) =>
            new GameStateSnapshot(interest, InterestState.Interested, 0, Array.Empty<string>(), 1);

        private static TurnResult MakeTurnResult(
            int interestDelta,
            int baseInterestDelta = 0,
            int riskBonusDelta = 0,
            int comboBonusDelta = 0,
            int shadowInterestDelta = 0,
            int horninessInterestPenalty = 0,
            int delayPenalty = 0,
            RollResult? roll = null,
            GameStateSnapshot? stateAfter = null)
        {
            return new TurnResult(
                roll: roll ?? MakeSuccessRoll(),
                deliveredMessage: "hello",
                opponentMessage: "hi",
                narrativeBeat: null,
                interestDelta: interestDelta,
                stateAfter: stateAfter ?? MakeSnapshot(),
                isGameOver: false,
                outcome: null,
                baseInterestDelta: baseInterestDelta,
                riskBonusDelta: riskBonusDelta,
                comboBonusDelta: comboBonusDelta,
                shadowInterestDelta: shadowInterestDelta,
                horninessInterestPenalty: horninessInterestPenalty,
                delayPenalty: delayPenalty);
        }

        // ── Sum invariant tests ───────────────────────────────────────────────

        /// <summary>
        /// Success-only: base=+1, no risk/combo/shadow/horniness → breakdown has one item.
        /// Sum invariant: 1 == 1.
        /// </summary>
        [Fact]
        public void Breakdown_SuccessOnly_SumEqualsInterestDelta()
        {
            var result = MakeTurnResult(
                interestDelta: 1,
                baseInterestDelta: 1);

            Assert.Equal(1, result.InterestBreakdown.Sum(x => x.Delta));
            Assert.Single(result.InterestBreakdown);
            Assert.Equal("base_roll", result.InterestBreakdown[0].Source);
            Assert.Equal(1, result.InterestBreakdown[0].Delta);
        }

        /// <summary>
        /// Success + risk bonus: base=+1, risk=+2 → total=+3.
        /// Sum invariant: 1 + 2 == 3.
        /// </summary>
        [Fact]
        public void Breakdown_SuccessWithRisk_SumEqualsInterestDelta()
        {
            var result = MakeTurnResult(
                interestDelta: 3,
                baseInterestDelta: 1,
                riskBonusDelta: 2);

            Assert.Equal(3, result.InterestBreakdown.Sum(x => x.Delta));
            Assert.Equal(2, result.InterestBreakdown.Count);
            Assert.Contains(result.InterestBreakdown, x => x.Source == "base_roll" && x.Delta == 1);
            Assert.Contains(result.InterestBreakdown, x => x.Source == "risk_tier" && x.Delta == 2);
        }

        /// <summary>
        /// Combo bonus: base=+1, risk=+1, combo=+2 → total=+4.
        /// Sum invariant: 1 + 1 + 2 == 4.
        /// </summary>
        [Fact]
        public void Breakdown_WithCombo_SumEqualsInterestDelta()
        {
            var result = MakeTurnResult(
                interestDelta: 4,
                baseInterestDelta: 1,
                riskBonusDelta: 1,
                comboBonusDelta: 2);

            Assert.Equal(4, result.InterestBreakdown.Sum(x => x.Delta));
            Assert.Equal(3, result.InterestBreakdown.Count);
            Assert.Contains(result.InterestBreakdown, x => x.Source == "combo" && x.Delta == 2);
        }

        /// <summary>
        /// Shadow misfire on success: base=+1, risk=+1, shadow correction=-3 → total=-1.
        /// Sum invariant: 1 + 1 + (-3) == -1.
        /// ShadowInterestDelta property equals -3.
        /// </summary>
        [Fact]
        public void Breakdown_WithShadowMisfire_SumEqualsInterestDelta()
        {
            // initial interestDelta = base + risk = +2; shadow forces to failure delta -1
            // shadowCorrection = -1 - 2 = -3; finalInterestDelta = -1
            var result = MakeTurnResult(
                interestDelta: -1,
                baseInterestDelta: 1,
                riskBonusDelta: 1,
                shadowInterestDelta: -3);

            Assert.Equal(-1, result.InterestDelta);
            Assert.Equal(-3, result.ShadowInterestDelta);
            Assert.Equal(-1, result.InterestBreakdown.Sum(x => x.Delta));
            Assert.Equal(3, result.InterestBreakdown.Count);
            Assert.Contains(result.InterestBreakdown, x => x.Source == "shadow_misfire" && x.Delta == -3);
            Assert.Equal("Shadow misfire correction", result.InterestBreakdown.First(x => x.Source == "shadow_misfire").Label);
        }

        /// <summary>
        /// Horniness trope-trap: base=+2, risk=+1, horniness=-1 → total=+2.
        /// (horniness penalty halves interestDelta: floor(3/2) - 3 = -1)
        /// Sum invariant: 2 + 1 + (-1) == 2.
        /// </summary>
        [Fact]
        public void Breakdown_WithHorninessTropeTrap_SumEqualsInterestDelta()
        {
            // base=+2, risk=+1 → interestDelta before horniness = +3
            // horniness penalty: floor(3/2) - 3 = 1 - 3 = -2? Let's use a simple -1 for this test
            var result = MakeTurnResult(
                interestDelta: 2,
                baseInterestDelta: 2,
                riskBonusDelta: 1,
                horninessInterestPenalty: -1);

            Assert.Equal(2, result.InterestDelta);
            Assert.Equal(2, result.InterestBreakdown.Sum(x => x.Delta));
            Assert.Equal(3, result.InterestBreakdown.Count);
            Assert.Contains(result.InterestBreakdown, x => x.Source == "horniness_trope_trap" && x.Delta == -1);
            Assert.Equal("Horniness trope-trap", result.InterestBreakdown.First(x => x.Source == "horniness_trope_trap").Label);
        }

        /// <summary>
        /// All sources present: base, risk, combo, shadow_misfire, horniness.
        /// Sum invariant must hold across the full component set.
        /// </summary>
        [Fact]
        public void Breakdown_AllSourcesPresent_SumEqualsInterestDelta()
        {
            // base=2, risk=3, combo=1, shadow=-8, horniness=0 (interestDelta after shadow=-2, ≤0 so no horniness)
            // sum: 2+3+1-8 = -2
            var result = MakeTurnResult(
                interestDelta: -2,
                baseInterestDelta: 2,
                riskBonusDelta: 3,
                comboBonusDelta: 1,
                shadowInterestDelta: -8);

            Assert.Equal(-2, result.InterestDelta);
            Assert.Equal(-2, result.InterestBreakdown.Sum(x => x.Delta));
            // shadow_misfire present
            Assert.Contains(result.InterestBreakdown, x => x.Source == "shadow_misfire");
            // horniness NOT present (delta is 0)
            Assert.DoesNotContain(result.InterestBreakdown, x => x.Source == "horniness_trope_trap");
        }

        // ── Non-zero filter ───────────────────────────────────────────────────

        /// <summary>
        /// Zero-delta components must be excluded from the breakdown list.
        /// </summary>
        [Fact]
        public void Breakdown_ZeroDeltaComponents_ExcludedFromList()
        {
            var result = MakeTurnResult(
                interestDelta: 1,
                baseInterestDelta: 1,
                riskBonusDelta: 0,
                comboBonusDelta: 0,
                shadowInterestDelta: 0,
                horninessInterestPenalty: 0,
                delayPenalty: 0);

            Assert.DoesNotContain(result.InterestBreakdown, x => x.Source == "risk_tier");
            Assert.DoesNotContain(result.InterestBreakdown, x => x.Source == "combo");
            Assert.DoesNotContain(result.InterestBreakdown, x => x.Source == "shadow_misfire");
            Assert.DoesNotContain(result.InterestBreakdown, x => x.Source == "horniness_trope_trap");
            Assert.DoesNotContain(result.InterestBreakdown, x => x.Source == "delay_penalty");
        }

        /// <summary>
        /// Breakdown with all zeros (interestDelta 0) is an empty list.
        /// Sum invariant: sum == 0 == interestDelta.
        /// </summary>
        [Fact]
        public void Breakdown_AllZero_EmptyList()
        {
            var result = MakeTurnResult(
                interestDelta: 0,
                baseInterestDelta: 0);

            Assert.Empty(result.InterestBreakdown);
            Assert.Equal(0, result.InterestBreakdown.Sum(x => x.Delta));
        }

        // ── Delay penalty ─────────────────────────────────────────────────────

        /// <summary>
        /// Delay penalty source key present and included when non-zero.
        /// (Defined but always 0 in the current ResolveTurnAsync interest path.)
        /// </summary>
        [Fact]
        public void Breakdown_DelayPenalty_IncludedWhenNonZero()
        {
            var result = MakeTurnResult(
                interestDelta: 0,
                baseInterestDelta: 1,
                delayPenalty: -1);

            Assert.Equal(0, result.InterestBreakdown.Sum(x => x.Delta));
            Assert.Contains(result.InterestBreakdown, x => x.Source == "delay_penalty" && x.Delta == -1);
            Assert.Equal("Delay penalty", result.InterestBreakdown.First(x => x.Source == "delay_penalty").Label);
        }

        // ── ShadowInterestDelta property ──────────────────────────────────────

        [Fact]
        public void ShadowInterestDelta_DefaultsToZero_WhenNotProvided()
        {
            var result = MakeTurnResult(interestDelta: 1, baseInterestDelta: 1);
            Assert.Equal(0, result.ShadowInterestDelta);
        }

        [Fact]
        public void ShadowInterestDelta_Negative_WhenShadowCorrectionApplied()
        {
            var result = MakeTurnResult(
                interestDelta: -1,
                baseInterestDelta: 1,
                riskBonusDelta: 3,
                shadowInterestDelta: -5);

            Assert.Equal(-5, result.ShadowInterestDelta);
        }

        // ── Source labels ─────────────────────────────────────────────────────

        [Fact]
        public void Breakdown_SourceLabels_HumanReadable()
        {
            var result = MakeTurnResult(
                interestDelta: -1,
                baseInterestDelta: 1,
                riskBonusDelta: 3,
                shadowInterestDelta: -5);

            var bySource = result.InterestBreakdown.ToDictionary(x => x.Source, x => x.Label);
            Assert.Equal("Base roll", bySource["base_roll"]);
            Assert.Equal("Risk tier bonus", bySource["risk_tier"]);
            Assert.Equal("Shadow misfire correction", bySource["shadow_misfire"]);
        }

        // ── Live session 41cc7a50 case ────────────────────────────────────────

        /// <summary>
        /// Live session 41cc7a50 regression.
        ///
        /// <para>
        /// Roll SUCCEEDED with total 19 vs DC 17 (beat by 2 → SuccessScale +1).
        /// Risk tier is Hard (+3). Initial interestDelta = base + risk = 1 + 3 = +4.
        /// Dread shadow Misfire overlay fired on a success:
        ///   shadowFailDelta = FailureScale(Misfire) = −1
        ///   shadowCorrection = −1 − 4 = −5
        ///   interestDelta after shadow = −1
        /// Horniness TropeTrap overlay fired, but since interestDelta ≤ 0 after shadow,
        ///   the interest penalty is NOT applied (horninessInterestPenalty = 0).
        /// Net: interest 10 → 9, delta_total = −1.
        /// </para>
        ///
        /// <para>
        /// The breakdown must itemize base_roll, risk_tier, shadow_misfire,
        /// their sum must equal −1, and the shadow_misfire line must be present
        /// (previously invisible in the UI — the bug this ticket fixes).
        /// </para>
        /// </summary>
        [Fact]
        public void LiveCase_Session41cc7a50_BreakdownItemizesAllComponents_SumMinusOne()
        {
            // Roll: 19 vs DC 17 → beat by 2 → SuccessScale = +1
            const int baseInterestDelta = 1;
            // Risk tier: Hard → bonus = +3
            const int riskBonusDelta = 3;
            // No combo on this turn
            const int comboBonusDelta = 0;
            // Shadow: Dread Misfire, overlay fired on success.
            //   shadowFailDelta = FailureScale(Misfire) = -1
            //   shadowCorrection = -1 - (base+risk) = -1 - 4 = -5
            const int shadowInterestDelta = -5;
            // Horniness TropeTrap overlay fired, but interestDelta after shadow = -1 <= 0
            //   → penalty NOT applied.
            const int horninessInterestPenalty = 0;

            const int expectedInterestDelta = baseInterestDelta + riskBonusDelta + comboBonusDelta
                                              + shadowInterestDelta + horninessInterestPenalty; // = -1

            // Reproduce the RollResult: d20=16 + stat=3 = 19 total, DC=17
            // need = DC - (stat+level) = 17 - 3 = 14 → Hard tier (12–15) → RiskTierBonus.Hard = +3
            var roll = new RollResult(
                dieRoll: 16,
                secondDieRoll: null,
                usedDieRoll: 16,
                stat: StatType.Charm,
                statModifier: 3,
                levelBonus: 0,
                dc: 17,
                tier: FailureTier.Success,
                activatedTrap: null,
                externalBonus: 0);

            Assert.Equal(19, roll.FinalTotal);
            Assert.True(roll.IsSuccess);
            Assert.Equal(2, roll.FinalTotal - roll.DC); // beat by 2 → SuccessScale +1
            Assert.Equal(RiskTier.Hard, roll.RiskTier);  // need=14 → Hard (+3)

            var result = new TurnResult(
                roll: roll,
                deliveredMessage: "hi",
                opponentMessage: "reply",
                narrativeBeat: null,
                interestDelta: expectedInterestDelta,
                stateAfter: MakeSnapshot(interest: 9), // after: 10 → 9
                isGameOver: false,
                outcome: null,
                baseInterestDelta: baseInterestDelta,
                riskBonusDelta: riskBonusDelta,
                comboBonusDelta: comboBonusDelta,
                shadowInterestDelta: shadowInterestDelta,
                horninessInterestPenalty: horninessInterestPenalty);

            // Net delta is -1 (interest 10 → 9)
            Assert.Equal(-1, result.InterestDelta);

            // ShadowInterestDelta surfaced on TurnResult
            Assert.Equal(-5, result.ShadowInterestDelta);

            // Sum invariant: breakdown items sum == InterestDelta
            int breakdownSum = result.InterestBreakdown.Sum(x => x.Delta);
            Assert.Equal(expectedInterestDelta, breakdownSum);
            Assert.Equal(-1, breakdownSum);

            // base_roll present: +1
            var baseItem = result.InterestBreakdown.Single(x => x.Source == "base_roll");
            Assert.Equal(1, baseItem.Delta);

            // risk_tier present: +3
            var riskItem = result.InterestBreakdown.Single(x => x.Source == "risk_tier");
            Assert.Equal(3, riskItem.Delta);

            // shadow_misfire present: -5 (the previously invisible correction)
            var shadowItem = result.InterestBreakdown.Single(x => x.Source == "shadow_misfire");
            Assert.Equal(-5, shadowItem.Delta);

            // combo not present (zero)
            Assert.DoesNotContain(result.InterestBreakdown, x => x.Source == "combo");

            // horniness_trope_trap not present (penalty was 0 because interestDelta <= 0 after shadow)
            Assert.DoesNotContain(result.InterestBreakdown, x => x.Source == "horniness_trope_trap");

            // Exactly 3 items
            Assert.Equal(3, result.InterestBreakdown.Count);
        }

        /// <summary>
        /// Variant: horniness TropeTrap fires on a success turn with no shadow.
        /// base=+2, horniness penalty halves: floor(2/2) - 2 = -1, final=+1.
        /// Sum invariant: 2 + (-1) == 1.
        /// </summary>
        [Fact]
        public void LiveCase_HorninessOnPositiveInterest_PenaltyApplied_SumInvariantHolds()
        {
            // base=+2, risk=0, horniness penalty = floor(2/2) - 2 = -1
            var result = MakeTurnResult(
                interestDelta: 1,
                baseInterestDelta: 2,                horninessInterestPenalty: -1);

            Assert.Equal(1, result.InterestDelta);
            Assert.Equal(1, result.InterestBreakdown.Sum(x => x.Delta));
            Assert.Equal(2, result.InterestBreakdown.Count);
            Assert.Contains(result.InterestBreakdown, x => x.Source == "base_roll" && x.Delta == 2);
            Assert.Contains(result.InterestBreakdown, x => x.Source == "horniness_trope_trap" && x.Delta == -1);
        }
    }
}
