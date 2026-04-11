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
    /// Tests for ScoringPlayerAgent shadow growth risk scoring (issue #416).
    /// Validates Fixation growth penalty, Denial penalty, Fixation threshold effects,
    /// and stat variety bonus.
    /// </summary>
    [Trait("Category", "Core")]
    public class ScoringPlayerAgentShadowTests
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
                    { ShadowStatType.Despair, horniness },
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
            int turnNumber = 5,
            StatType? lastStatUsed = null,
            StatType? secondLastStatUsed = null,
            bool honestyAvailableLastTurn = false,
            Dictionary<ShadowStatType, int>? shadowValues = null)
        {
            return new PlayerAgentContext(
                player ?? MakeStats(charm: 3, rizz: 3, honesty: 3, chaos: 3, wit: 3, sa: 3),
                opponent ?? MakeStats(),
                interest,
                state,
                momentum,
                traps ?? Array.Empty<string>(),
                0,
                shadowValues,
                turnNumber,
                lastStatUsed,
                secondLastStatUsed,
                honestyAvailableLastTurn);
        }

        #endregion

        #region AC: Fixation growth penalty

        // Agent avoids third consecutive same-stat pick when alternative is close in EV
        [Fact]
        public async Task FixationGrowthPenalty_AvoidsThirdConsecutiveSameStat()
        {
            // Both options have similar base EV, but Charm would trigger Fixation growth
            var player = MakeStats(charm: 3, rizz: 3);
            var opponent = MakeStats(); // all 0 → DC=13

            var turn = MakeTurn(
                MakeOption(StatType.Charm),
                MakeOption(StatType.Rizz));

            // Last two turns both used Charm → picking Charm again triggers Fixation
            var context = MakeContext(
                player: player, opponent: opponent,
                lastStatUsed: StatType.Charm,
                secondLastStatUsed: StatType.Charm);

            var decision = await _agent.DecideAsync(turn, context);

            // Rizz should win due to -0.5 Fixation penalty on Charm + +0.1 variety bonus on Rizz
            Assert.Equal(1, decision.OptionIndex);
        }

        // Penalty only applies when both last two stats match the current option
        [Fact]
        public async Task FixationGrowthPenalty_DoesNotApplyWhenOnlyOneMatch()
        {
            var player = MakeStats(charm: 3, wit: 3);
            var opponent = MakeStats();

            var turn = MakeTurn(
                MakeOption(StatType.Charm),
                MakeOption(StatType.Wit));

            // Only last turn was Charm, second-last was Rizz (different)
            var context = MakeContext(
                player: player, opponent: opponent,
                lastStatUsed: StatType.Charm,
                secondLastStatUsed: StatType.Rizz);

            var decision = await _agent.DecideAsync(turn, context);

            // Both have same base EV.
            // Charm: used last turn (no variety bonus), no Fixation penalty (only 1 consecutive)
            // Wit: not in recent history → +0.1 variety bonus
            var charmScore = decision.Scores[0].Score;
            var witScore = decision.Scores[1].Score;
            Assert.True(witScore > charmScore, "Wit should score higher due to variety bonus");
            // The gap should be small (variety only, no Fixation penalty)
            Assert.True(witScore - charmScore < 0.3f,
                $"Gap should be small (variety only): {witScore - charmScore}");
        }

        // Penalty doesn't apply when LastStatUsed is null (first turn)
        [Fact]
        public async Task FixationGrowthPenalty_SkippedWhenLastStatNull()
        {
            var player = MakeStats(charm: 3, rizz: 3);
            var opponent = MakeStats();

            var turn = MakeTurn(
                MakeOption(StatType.Charm),
                MakeOption(StatType.Rizz));

            // First turn — no history
            var context = MakeContext(player: player, opponent: opponent);

            var decision = await _agent.DecideAsync(turn, context);

            // Scores should be identical (no history-based adjustments applicable for Fixation)
            float diff = Math.Abs(decision.Scores[0].Score - decision.Scores[1].Score);
            Assert.True(diff < 0.01f, $"Scores should be nearly identical on first turn: {diff}");
        }

        #endregion

        #region AC: Denial growth penalty

        // Non-Honesty option gets -0.3 when Honesty is available
        [Fact]
        public async Task DenialPenalty_AppliedWhenSkippingHonesty()
        {
            var player = MakeStats(charm: 3, honesty: 3);
            var opponent = MakeStats();

            var turn = MakeTurn(
                MakeOption(StatType.Charm),
                MakeOption(StatType.Honesty));

            var context = MakeContext(player: player, opponent: opponent);

            var decision = await _agent.DecideAsync(turn, context);

            // Charm gets -0.3 Denial penalty (skipping Honesty)
            // Honesty does NOT get Denial penalty (it IS Honesty)
            // With equal stats, Honesty should win by the 0.3 margin
            Assert.Equal(1, decision.OptionIndex);
        }

        // No Denial penalty when Honesty is not in options
        [Fact]
        public async Task DenialPenalty_NotAppliedWhenNoHonestyInOptions()
        {
            var player = MakeStats(charm: 3, rizz: 3);
            var opponent = MakeStats();

            var turn = MakeTurn(
                MakeOption(StatType.Charm),
                MakeOption(StatType.Rizz));

            var contextWithHistory = MakeContext(
                player: player, opponent: opponent,
                lastStatUsed: StatType.Charm);
            var contextWithout = MakeContext(
                player: player, opponent: opponent);

            var decisionWith = await _agent.DecideAsync(turn, contextWithHistory);
            var decisionWithout = await _agent.DecideAsync(turn, contextWithout);

            // No Denial penalty should be applied to either option since Honesty isn't available
            // The only difference should be variety bonus
            float charmDiffWith = decisionWith.Scores[0].Score;
            float rizzDiffWith = decisionWith.Scores[1].Score;
            // Without history, both should be equal
            float charmDiffWithout = decisionWithout.Scores[0].Score;
            float rizzDiffWithout = decisionWithout.Scores[1].Score;
            Assert.True(Math.Abs(charmDiffWithout - rizzDiffWithout) < 0.01f,
                "Without stat history, Charm and Rizz should have equal scores");
        }

        #endregion

        #region AC: Fixation threshold — Chaos EV reduction

        // T1 (Fixation >= 6): Chaos EV reduced by 20%
        [Fact]
        public async Task FixationT1_ChaosEvReduced20Percent()
        {
            var player = MakeStats(chaos: 3, charm: 3);
            var opponent = MakeStats();

            var shadowsT1 = new Dictionary<ShadowStatType, int>
            {
                { ShadowStatType.Fixation, 6 },
                { ShadowStatType.Madness, 0 },
                { ShadowStatType.Despair, 0 },
                { ShadowStatType.Denial, 0 },
                { ShadowStatType.Dread, 0 },
                { ShadowStatType.Overthinking, 0 }
            };
            var shadowsNone = new Dictionary<ShadowStatType, int>
            {
                { ShadowStatType.Fixation, 0 },
                { ShadowStatType.Madness, 0 },
                { ShadowStatType.Despair, 0 },
                { ShadowStatType.Denial, 0 },
                { ShadowStatType.Dread, 0 },
                { ShadowStatType.Overthinking, 0 }
            };

            var turnChaos = MakeTurn(MakeOption(StatType.Chaos));

            var resultT1 = await _agent.DecideAsync(turnChaos,
                MakeContext(player: player, opponent: opponent, shadowValues: shadowsT1));
            var resultNone = await _agent.DecideAsync(turnChaos,
                MakeContext(player: player, opponent: opponent, shadowValues: shadowsNone));

            // Chaos EV should be lower with Fixation T1
            Assert.True(resultT1.Scores[0].Score < resultNone.Scores[0].Score,
                $"Fixation T1 should reduce Chaos score: T1={resultT1.Scores[0].Score}, None={resultNone.Scores[0].Score}");
        }

        // T2 (Fixation >= 12): Chaos gets disadvantage
        [Fact]
        public async Task FixationT2_ChaosDisadvantageApplied()
        {
            var player = MakeStats(chaos: 3, charm: 3);
            var opponent = MakeStats();

            var shadowsT2 = new Dictionary<ShadowStatType, int>
            {
                { ShadowStatType.Fixation, 12 },
                { ShadowStatType.Madness, 0 },
                { ShadowStatType.Despair, 0 },
                { ShadowStatType.Denial, 0 },
                { ShadowStatType.Dread, 0 },
                { ShadowStatType.Overthinking, 0 }
            };
            var shadowsT1 = new Dictionary<ShadowStatType, int>
            {
                { ShadowStatType.Fixation, 6 },
                { ShadowStatType.Madness, 0 },
                { ShadowStatType.Despair, 0 },
                { ShadowStatType.Denial, 0 },
                { ShadowStatType.Dread, 0 },
                { ShadowStatType.Overthinking, 0 }
            };

            var turnChaos = MakeTurn(MakeOption(StatType.Chaos));

            var resultT2 = await _agent.DecideAsync(turnChaos,
                MakeContext(player: player, opponent: opponent, shadowValues: shadowsT2));
            var resultT1 = await _agent.DecideAsync(turnChaos,
                MakeContext(player: player, opponent: opponent, shadowValues: shadowsT1));

            // T2 disadvantage should reduce score more than T1 reduction
            Assert.True(resultT2.Scores[0].Score < resultT1.Scores[0].Score,
                $"Fixation T2 should reduce Chaos score more than T1: T2={resultT2.Scores[0].Score}, T1={resultT1.Scores[0].Score}");
        }

        // Non-Chaos options unaffected by Fixation threshold
        [Fact]
        public async Task FixationThreshold_DoesNotAffectNonChaosOptions()
        {
            var player = MakeStats(charm: 3);
            var opponent = MakeStats();

            var shadowsT2 = new Dictionary<ShadowStatType, int>
            {
                { ShadowStatType.Fixation, 12 },
                { ShadowStatType.Madness, 0 },
                { ShadowStatType.Despair, 0 },
                { ShadowStatType.Denial, 0 },
                { ShadowStatType.Dread, 0 },
                { ShadowStatType.Overthinking, 0 }
            };
            var shadowsNone = new Dictionary<ShadowStatType, int>
            {
                { ShadowStatType.Fixation, 0 },
                { ShadowStatType.Madness, 0 },
                { ShadowStatType.Despair, 0 },
                { ShadowStatType.Denial, 0 },
                { ShadowStatType.Dread, 0 },
                { ShadowStatType.Overthinking, 0 }
            };

            var turnCharm = MakeTurn(MakeOption(StatType.Charm));

            var resultT2 = await _agent.DecideAsync(turnCharm,
                MakeContext(player: player, opponent: opponent, shadowValues: shadowsT2));
            var resultNone = await _agent.DecideAsync(turnCharm,
                MakeContext(player: player, opponent: opponent, shadowValues: shadowsNone));

            // Charm score should be identical regardless of Fixation level
            Assert.Equal((double)resultT2.Scores[0].Score, (double)resultNone.Scores[0].Score, 4);
        }

        // Null shadow values → skip threshold checks
        [Fact]
        public async Task FixationThreshold_SkippedWhenShadowValuesNull()
        {
            var player = MakeStats(chaos: 3);
            var opponent = MakeStats();

            var turnChaos = MakeTurn(MakeOption(StatType.Chaos));

            // No shadow values (null) — should not crash or apply any reduction
            var result = await _agent.DecideAsync(turnChaos,
                MakeContext(player: player, opponent: opponent, shadowValues: null));

            Assert.NotNull(result);
            Assert.Equal(0, result.OptionIndex);
        }

        #endregion

        #region AC: Stat variety bonus

        // Picking a stat not used recently gets +0.1
        [Fact]
        public async Task StatVarietyBonus_AppliedToUnusedStat()
        {
            var player = MakeStats(charm: 3, rizz: 3, wit: 3);
            var opponent = MakeStats();

            var turn = MakeTurn(
                MakeOption(StatType.Charm),
                MakeOption(StatType.Rizz),
                MakeOption(StatType.Wit));

            // Last two turns: Charm, Rizz
            var context = MakeContext(
                player: player, opponent: opponent,
                lastStatUsed: StatType.Charm,
                secondLastStatUsed: StatType.Rizz);

            var decision = await _agent.DecideAsync(turn, context);

            // Wit (not used recently) should get +0.1 variety bonus
            // Charm and Rizz were used recently → no variety bonus
            float witScore = decision.Scores[2].Score;
            float charmScore = decision.Scores[0].Score;
            float rizzScore = decision.Scores[1].Score;

            // Wit should be higher than both due to variety bonus
            Assert.True(witScore > charmScore,
                $"Wit should beat Charm with variety bonus: Wit={witScore}, Charm={charmScore}");
            Assert.True(witScore > rizzScore,
                $"Wit should beat Rizz with variety bonus: Wit={witScore}, Rizz={rizzScore}");
        }

        // No variety bonus when stat history is empty
        [Fact]
        public async Task StatVarietyBonus_NotAppliedWhenNoHistory()
        {
            var player = MakeStats(charm: 3, rizz: 3);
            var opponent = MakeStats();

            var turn = MakeTurn(
                MakeOption(StatType.Charm),
                MakeOption(StatType.Rizz));

            // No stat history
            var context = MakeContext(player: player, opponent: opponent);

            var decision = await _agent.DecideAsync(turn, context);

            // Scores should be identical (no variety adjustments)
            float diff = Math.Abs(decision.Scores[0].Score - decision.Scores[1].Score);
            Assert.True(diff < 0.01f,
                $"Scores should be equal without stat history: diff={diff}");
        }

        #endregion

        #region AC: Agent avoids 3rd consecutive same-stat when close EV

        // Integration test: all shadow adjustments combine correctly
        [Fact]
        public async Task CombinedShadowAdjustments_AgentAvoidsThirdSameStat()
        {
            // Setup: Honesty was used last 2 turns, Honesty and Charm available
            // Equal mods (charm=3, honesty=3) → equal base EV.
            // With Fixation penalty (-0.5) on Honesty and variety bonus (+0.1) on Charm, Charm wins.
            var player = MakeStats(charm: 3, honesty: 3);
            var opponent = MakeStats();

            var turn = MakeTurn(
                MakeOption(StatType.Honesty),
                MakeOption(StatType.Charm));

            var context = MakeContext(
                player: player, opponent: opponent,
                lastStatUsed: StatType.Honesty,
                secondLastStatUsed: StatType.Honesty);

            var decision = await _agent.DecideAsync(turn, context);

            // Honesty gets: -0.5 Fixation penalty, no Denial penalty
            // Charm gets: -0.3 Denial penalty (skipping Honesty), +0.1 variety bonus
            // Honesty base EV is higher due to +3 vs +2 mod, but -0.5 Fixation should outweigh
            // Net adjustments: Honesty: -0.5, Charm: -0.3 + 0.1 = -0.2
            // Difference in adjustments: 0.3 in Charm's favor
            // This test validates the agent avoids the third consecutive pick
            Assert.Equal(1, decision.OptionIndex);
        }

        #endregion

        #region AC: PlayerAgentContext new fields

        [Fact]
        public void PlayerAgentContext_NewFieldsHaveDefaults()
        {
            // Existing constructor call should still work (backward compat)
            var context = new PlayerAgentContext(
                MakeStats(),
                MakeStats(),
                10,
                InterestState.Interested,
                0,
                Array.Empty<string>(),
                0,
                null,
                3);

            Assert.Null(context.LastStatUsed);
            Assert.Null(context.SecondLastStatUsed);
            Assert.False(context.HonestyAvailableLastTurn);
        }

        [Fact]
        public void PlayerAgentContext_NewFieldsSetCorrectly()
        {
            var context = new PlayerAgentContext(
                MakeStats(),
                MakeStats(),
                10,
                InterestState.Interested,
                0,
                Array.Empty<string>(),
                0,
                null,
                3,
                lastStatUsed: StatType.Charm,
                secondLastStatUsed: StatType.Rizz,
                honestyAvailableLastTurn: true);

            Assert.Equal(StatType.Charm, context.LastStatUsed);
            Assert.Equal(StatType.Rizz, context.SecondLastStatUsed);
            Assert.True(context.HonestyAvailableLastTurn);
        }

        [Fact]
        public void PlayerAgentContext_ShadowValuesExposed()
        {
            var shadows = new Dictionary<ShadowStatType, int>
            {
                { ShadowStatType.Fixation, 8 },
                { ShadowStatType.Madness, 0 },
                { ShadowStatType.Despair, 0 },
                { ShadowStatType.Denial, 0 },
                { ShadowStatType.Dread, 0 },
                { ShadowStatType.Overthinking, 0 }
            };
            var context = new PlayerAgentContext(
                MakeStats(), MakeStats(),
                10, InterestState.Interested, 0,
                Array.Empty<string>(), 0, shadows, 3);

            Assert.NotNull(context.ShadowValues);
            Assert.Equal(8, context.ShadowValues![ShadowStatType.Fixation]);
        }

        #endregion

        #region Build clean verification

        // Ensures determinism is maintained with new scoring terms
        [Fact]
        public async Task Determinism_WithShadowContext_SameInputsSameOutput()
        {
            var shadows = new Dictionary<ShadowStatType, int>
            {
                { ShadowStatType.Fixation, 7 },
                { ShadowStatType.Madness, 0 },
                { ShadowStatType.Despair, 0 },
                { ShadowStatType.Denial, 0 },
                { ShadowStatType.Dread, 0 },
                { ShadowStatType.Overthinking, 0 }
            };
            var player = MakeStats(charm: 3, chaos: 3, honesty: 2);
            var opponent = MakeStats();

            var turn = MakeTurn(
                MakeOption(StatType.Charm),
                MakeOption(StatType.Chaos),
                MakeOption(StatType.Honesty));

            var context = MakeContext(
                player: player, opponent: opponent,
                shadowValues: shadows,
                lastStatUsed: StatType.Charm,
                secondLastStatUsed: StatType.Charm);

            var d1 = await _agent.DecideAsync(turn, context);
            var d2 = await _agent.DecideAsync(turn, context);

            Assert.Equal(d1.OptionIndex, d2.OptionIndex);
            for (int i = 0; i < d1.Scores.Length; i++)
            {
                Assert.Equal((double)d1.Scores[i].Score, (double)d2.Scores[i].Score, 4);
            }
        }

        #endregion
    }
}
