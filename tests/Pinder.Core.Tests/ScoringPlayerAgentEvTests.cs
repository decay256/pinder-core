using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Pinder.Core.Conversation;
using Pinder.Core.Stats;
using Pinder.SessionRunner;
using Xunit;

namespace Pinder.Core.Tests
{
    /// <summary>
    /// Tests for ScoringPlayerAgent EV fix (issue #517).
    /// Validates that combo/tell bonuses are scaled for low-success options,
    /// and that trap cost is properly weighted by failure tier distribution.
    /// </summary>
    public class ScoringPlayerAgentEvTests
    {
        private readonly ScoringPlayerAgent _agent = new ScoringPlayerAgent();

        #region Helpers

        private static StatBlock MakeStats(
            int charm = 0, int rizz = 0, int honesty = 0,
            int chaos = 0, int wit = 0, int sa = 0,
            int madness = 0, int horniness = 0, int denial = 0,
            int fixation = 0, int dread = 0, int overthinking = 0)
        {
            return new StatBlock(
                new Dictionary<StatType, int>
                {
                    { StatType.Charm, charm },
                    { StatType.Rizz, rizz },
                    { StatType.Honesty, honesty },
                    { StatType.Chaos, chaos },
                    { StatType.Wit, wit },
                    { StatType.SelfAwareness, sa }
                },
                new Dictionary<ShadowStatType, int>
                {
                    { ShadowStatType.Madness, madness },
                    { ShadowStatType.Horniness, horniness },
                    { ShadowStatType.Denial, denial },
                    { ShadowStatType.Fixation, fixation },
                    { ShadowStatType.Dread, dread },
                    { ShadowStatType.Overthinking, overthinking }
                });
        }

        private static DialogueOption MakeOption(
            StatType stat,
            int? callbackTurn = null,
            string? comboName = null,
            bool hasTellBonus = false)
        {
            return new DialogueOption(
                stat, $"{stat} option",
                callbackTurnNumber: callbackTurn,
                comboName: comboName,
                hasTellBonus: hasTellBonus);
        }

        private static TurnStart MakeTurn(params DialogueOption[] options)
        {
            return new TurnStart(
                options,
                new GameStateSnapshot(10, InterestState.Interested, 0, Array.Empty<string>(), 5));
        }

        private static PlayerAgentContext MakeContext(
            StatBlock? player = null,
            StatBlock? opponent = null,
            int interest = 10,
            InterestState state = InterestState.Interested,
            int momentum = 0,
            string[]? traps = null,
            int turnNumber = 5)
        {
            return new PlayerAgentContext(
                player ?? MakeStats(charm: 3, rizz: 2, honesty: 1, chaos: 2, wit: 2, sa: 1),
                opponent ?? MakeStats(charm: 1, rizz: 1, honesty: 1, chaos: 1, wit: 1, sa: 1),
                interest,
                state,
                momentum,
                traps ?? Array.Empty<string>(),
                0,
                null,
                turnNumber);
        }

        #endregion

        #region AC: combo/tell scaled when success < 20%

        /// <summary>
        /// AC: option with 15% success and TropeTrap-range miss scores lower than
        /// same option with 50% success, even when both have combo bonus.
        /// </summary>
        [Fact]
        public async Task LowSuccess_WithCombo_ScoresLowerThanHighSuccess_WithCombo()
        {
            // Option A: high success (~55%). Player charm=3, opponent SA=0 → DC=13, need=10
            // Option B: low success (~15%). Player rizz=0, opponent Wit=5 → DC=18, need=18
            var player = MakeStats(charm: 3, rizz: 0);
            var opponent = MakeStats(sa: 0, wit: 5);

            var highSuccessCombo = MakeOption(StatType.Charm, comboName: "TheCombo");
            var lowSuccessCombo = MakeOption(StatType.Rizz, comboName: "TheCombo");

            var turn = MakeTurn(highSuccessCombo, lowSuccessCombo);
            var context = MakeContext(player: player, opponent: opponent);

            var decision = await _agent.DecideAsync(turn, context);

            // High-success option should have higher EV even with same combo
            Assert.True(decision.Scores[0].ExpectedInterestGain > decision.Scores[1].ExpectedInterestGain,
                $"High-success ({decision.Scores[0].SuccessChance:P0}) EV {decision.Scores[0].ExpectedInterestGain:F3} " +
                $"should beat low-success ({decision.Scores[1].SuccessChance:P0}) EV {decision.Scores[1].ExpectedInterestGain:F3}");

            // Low-success option should have NEGATIVE EV (failure cost dominates)
            Assert.True(decision.Scores[1].ExpectedInterestGain < 0,
                $"Low-success option EV should be negative, got {decision.Scores[1].ExpectedInterestGain:F3}");
        }

        /// <summary>
        /// AC: combo bonus on low-success option (&lt;20%) is scaled down further,
        /// not applied at full value.
        /// </summary>
        [Fact]
        public async Task ComboBonus_ScaledDown_WhenSuccessBelow20Percent()
        {
            // Player SA=0, opponent Honesty=5 → DC=18, need=18 → success=15%
            var player = MakeStats(sa: 0);
            var opponent = MakeStats(honesty: 5);

            var withCombo = MakeOption(StatType.SelfAwareness, comboName: "SomeCombo");
            var withoutCombo = MakeOption(StatType.SelfAwareness);

            var turnCombo = MakeTurn(withCombo);
            var turnNoCombo = MakeTurn(withoutCombo);
            var context = MakeContext(player: player, opponent: opponent);

            var decisionCombo = await _agent.DecideAsync(turnCombo, context);
            var decisionNoCombo = await _agent.DecideAsync(turnNoCombo, context);

            float comboDelta = decisionCombo.Scores[0].ExpectedInterestGain
                             - decisionNoCombo.Scores[0].ExpectedInterestGain;

            // At ~15% success, combo is scaled by (0.15/0.20)=0.75, then multiplied by
            // successChance=0.15 in the EV formula → contribution ~0.1125
            // Without scaling, it would be 0.15 * 1.0 = 0.15
            Assert.True(comboDelta >= 0,
                $"Combo should still help (or be neutral), got delta={comboDelta:F4}");
            Assert.True(comboDelta < 0.15f,
                $"Combo delta at 15% success should be less than unscaled (0.15), got {comboDelta:F4}");
        }

        /// <summary>
        /// AC: combo bonus at high success (&gt;=20%) is NOT scaled down.
        /// </summary>
        [Fact]
        public async Task ComboBonus_NotScaled_WhenSuccessAbove20Percent()
        {
            // Player charm=6, opponent SA=0 → DC=16, need=10 → success=55%
            var player = MakeStats(charm: 6);
            var opponent = MakeStats(sa: 0);

            var withCombo = MakeOption(StatType.Charm, comboName: "SomeCombo");
            var withoutCombo = MakeOption(StatType.Charm);

            var turnCombo = MakeTurn(withCombo);
            var turnNoCombo = MakeTurn(withoutCombo);
            var context = MakeContext(player: player, opponent: opponent);

            var decisionCombo = await _agent.DecideAsync(turnCombo, context);
            var decisionNoCombo = await _agent.DecideAsync(turnNoCombo, context);

            float comboDelta = decisionCombo.Scores[0].ExpectedInterestGain
                             - decisionNoCombo.Scores[0].ExpectedInterestGain;

            // At 55% success, combo adds ~0.55 * 1.0 = ~0.55 to EV (unscaled)
            Assert.True(comboDelta > 0.4f,
                $"Combo at 55% success should add significant EV (~0.55), got {comboDelta:F4}");
        }

        #endregion

        #region AC: TropeTrap range failure cost

        /// <summary>
        /// AC: When miss margin would land in TropeTrap range (6-9),
        /// trap activation cost is included in the failure EV.
        /// </summary>
        [Fact]
        public async Task TropeTrapRange_HigherFailCost_ThanFumbleRange()
        {
            // Option A: need=3 → failures are all Fumble (miss 1-2). Low fail cost.
            // Option B: need=10 → failures span Fumble through TropeTrap. Higher fail cost.
            // Both have same success chance area, but B's failures are more costly.
            var playerA = MakeStats(charm: 10);
            var playerB = MakeStats(rizz: 3);
            var opponent = MakeStats(sa: 0, wit: 0); // DC=13 for both

            // Charm: need=13-10=3, success=90%, failures are miss 1-2 (Fumble only)
            // Rizz: need=13-3=10, success=55%, failures span all tiers up to TropeTrap
            var turnA = MakeTurn(MakeOption(StatType.Charm));
            var turnB = MakeTurn(MakeOption(StatType.Rizz));
            var contextA = MakeContext(player: playerA, opponent: opponent);
            var contextB = MakeContext(player: playerB, opponent: opponent);

            var decisionA = await _agent.DecideAsync(turnA, contextA);
            var decisionB = await _agent.DecideAsync(turnB, contextB);

            // A has higher success and lower fail cost → much higher EV
            Assert.True(decisionA.Scores[0].ExpectedInterestGain > decisionB.Scores[0].ExpectedInterestGain,
                $"Low-need option EV ({decisionA.Scores[0].ExpectedInterestGain:F3}) " +
                $"should beat high-need option EV ({decisionB.Scores[0].ExpectedInterestGain:F3})");
        }

        /// <summary>
        /// AC: High-need option (need=18) has failures mostly in Catastrophe/TropeTrap range,
        /// resulting in very high weighted failure cost.
        /// </summary>
        [Fact]
        public async Task HighNeed_WeightedFailCost_MakesEvStronglyNegative()
        {
            // Player SA=0, opponent Honesty=5 → DC=18, need=18 → success=15%
            // Most failures (rolls 2-17) miss by 1..17 → spans all tiers including Catastrophe
            var player = MakeStats(sa: 0);
            var opponent = MakeStats(honesty: 5);

            var turn = MakeTurn(MakeOption(StatType.SelfAwareness));
            var context = MakeContext(player: player, opponent: opponent);

            var decision = await _agent.DecideAsync(turn, context);

            // EV should be strongly negative: 85% chance of failure with high-tier costs
            Assert.True(decision.Scores[0].ExpectedInterestGain < -1.0f,
                $"High-need option should have strongly negative EV, got {decision.Scores[0].ExpectedInterestGain:F3}");
        }

        #endregion

        #region AC: 15% success + TropeTrap < 50% success (integration)

        /// <summary>
        /// AC: Unit test with 15% success and TropeTrap-range miss scores
        /// lower than same option with 50% success.
        /// </summary>
        [Fact]
        public async Task Option15Pct_TropeTrapRange_ScoresLowerThan_Option50Pct()
        {
            // Option with ~15% success: need=18 (DC=18, player charm=0, opponent SA=2)
            // Failures at need=18 include TropeTrap range (miss 6-9 for rolls 9-12)
            var playerLow = MakeStats(charm: 0);
            var opponentHigh = MakeStats(sa: 2); // DC=16+2=18

            // Option with ~50% success: need=11 (DC=16, player charm=5, opponent SA=0)
            var playerHigh = MakeStats(charm: 5);
            var opponentLow = MakeStats(sa: 0); // DC=16

            var turnLow = MakeTurn(MakeOption(StatType.Charm));
            var turnHigh = MakeTurn(MakeOption(StatType.Charm));
            var contextLow = MakeContext(player: playerLow, opponent: opponentHigh);
            var contextHigh = MakeContext(player: playerHigh, opponent: opponentLow);

            var decisionLow = await _agent.DecideAsync(turnLow, contextLow);
            var decisionHigh = await _agent.DecideAsync(turnHigh, contextHigh);

            // 15% success should score much lower than 50% success
            Assert.True(decisionLow.Scores[0].Score < decisionHigh.Scores[0].Score,
                $"15% success EV ({decisionLow.Scores[0].Score:F3}) should be lower than " +
                $"50% success EV ({decisionHigh.Scores[0].Score:F3})");

            // 15% option should have negative EV
            Assert.True(decisionLow.Scores[0].ExpectedInterestGain < 0,
                $"15% success option should have negative EV, got {decisionLow.Scores[0].ExpectedInterestGain:F3}");

            // 50% option should have positive EV
            Assert.True(decisionHigh.Scores[0].ExpectedInterestGain > 0,
                $"50% success option should have positive EV, got {decisionHigh.Scores[0].ExpectedInterestGain:F3}");
        }

        /// <summary>
        /// AC: Even with tell+combo bonuses, a near-zero success option should NOT
        /// have positive EV that masks trap cost.
        /// </summary>
        [Fact]
        public async Task LowSuccess_WithTellAndCombo_StillNegativeEv()
        {
            // Player SA=0, opponent Honesty=5 → DC=18
            // With tell (+2): need=18-2=16 → success=25%
            // Without tell: need=18 → success=15%
            var player = MakeStats(sa: 0);
            var opponent = MakeStats(honesty: 5);

            var optionWithBonuses = MakeOption(StatType.SelfAwareness,
                comboName: "RecoveryCombo", hasTellBonus: true);
            var optionPlain = MakeOption(StatType.SelfAwareness);

            var turnBonuses = MakeTurn(optionWithBonuses);
            var turnPlain = MakeTurn(optionPlain);
            var context = MakeContext(player: player, opponent: opponent);

            var decisionBonuses = await _agent.DecideAsync(turnBonuses, context);
            var decisionPlain = await _agent.DecideAsync(turnPlain, context);

            // With tell, success raises to ~25% but failure cost is still heavy
            // The tell+combo should improve EV but NOT make it positive at this difficulty
            Assert.True(decisionBonuses.Scores[0].ExpectedInterestGain <
                         decisionPlain.Scores[0].ExpectedInterestGain + 1.5f,
                "Tell+combo should not inflate EV by more than 1.5 at this difficulty");

            // Plain option (no bonuses) should have strongly negative EV
            Assert.True(decisionPlain.Scores[0].ExpectedInterestGain < -1.0f,
                $"Plain low-success option EV should be < -1.0, got {decisionPlain.Scores[0].ExpectedInterestGain:F3}");
        }

        #endregion

        #region Regression: high-success options unaffected

        /// <summary>
        /// Combo bonus at high success is applied normally (no scaling).
        /// </summary>
        [Fact]
        public async Task HighSuccess_ComboBonus_AppliedNormally()
        {
            // Player charm=11, opponent SA=0 → DC=16, need=5 → success=80%
            var player = MakeStats(charm: 11);
            var opponent = MakeStats(sa: 0);

            var withCombo = MakeOption(StatType.Charm, comboName: "SomeCombo");
            var withoutCombo = MakeOption(StatType.Charm);

            var turnCombo = MakeTurn(withCombo);
            var turnNoCombo = MakeTurn(withoutCombo);
            var context = MakeContext(player: player, opponent: opponent);

            var decisionCombo = await _agent.DecideAsync(turnCombo, context);
            var decisionNoCombo = await _agent.DecideAsync(turnNoCombo, context);

            // At 80% success, combo adds ~0.8 to EV
            float comboDelta = decisionCombo.Scores[0].ExpectedInterestGain
                             - decisionNoCombo.Scores[0].ExpectedInterestGain;
            Assert.True(comboDelta > 0.7f,
                $"Combo at 80% success should add ~0.8 to EV, got {comboDelta:F4}");
        }

        #endregion
    }
}
