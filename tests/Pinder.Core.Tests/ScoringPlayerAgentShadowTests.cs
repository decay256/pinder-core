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
    public partial class ScoringPlayerAgentShadowTests
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
    }
}
