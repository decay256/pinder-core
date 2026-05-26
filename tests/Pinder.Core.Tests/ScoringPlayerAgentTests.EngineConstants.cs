using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Pinder.Core.Conversation;
using Pinder.Core.Stats;
using Pinder.SessionRunner;
using Xunit;

namespace Pinder.Core.Tests
{
    public partial class ScoringPlayerAgentTests
    {
        // ================================================================
        // Issue #386: Verify ScoringPlayerAgent uses engine constants correctly
        // These tests guard against silent drift between the agent and the engine.
        // ================================================================

        [Fact]
        public async Task CallbackBonus_UsesEngineMethod_OpenerReturns3()
        {
            // ScoringPlayerAgent must call CallbackBonus.Compute() directly.
            // Verify opener callback (turn 0, current turn 5) yields +3 by checking
            // that the agent's bonus matches CallbackBonus.Compute(5, 0).
            int engineBonus = CallbackBonus.Compute(5, 0);
            Assert.Equal(3, engineBonus);

            var optionWithOpenerCallback = MakeOption(StatType.Charm, callbackTurn: 0);
            var optionPlain = MakeOption(StatType.Charm);
            var player = MakeStats(charm: 3);
            var opponent = MakeStats(sa: 2);

            var turnCb = MakeTurn(optionWithOpenerCallback);
            var turnPlain = MakeTurn(optionPlain);
            var context = MakeContext(player: player, opponent: opponent, turnNumber: 5);

            var decisionCb = await _agent.DecideAsync(turnCb, context);
            var decisionPlain = await _agent.DecideAsync(turnPlain, context);

            // Opener callback should raise success chance (lower need)
            Assert.True(decisionCb.Scores[0].SuccessChance > decisionPlain.Scores[0].SuccessChance,
                "Opener callback (+3) should increase success chance vs no callback");
        }

        [Fact]
        public async Task CallbackBonus_MidDistance_MatchesEngine()
        {
            // Mid-distance callback (gap 2-3, non-opener) → engine returns 1
            int engineBonus = CallbackBonus.Compute(5, 3);
            Assert.Equal(1, engineBonus);

            var option = MakeOption(StatType.Charm, callbackTurn: 3);
            var player = MakeStats(charm: 3);
            var opponent = MakeStats(sa: 2);
            var turn = MakeTurn(option);
            var context = MakeContext(player: player, opponent: opponent, turnNumber: 5);

            var decision = await _agent.DecideAsync(turn, context);
            Assert.Contains(decision.Scores[0].BonusesApplied,
                b => b.Contains("callback", StringComparison.OrdinalIgnoreCase));
        }

        // What: Momentum bonus thresholds match GameSession rules (§15)
        // Mutation: Would catch if agent used wrong streak thresholds (e.g. >=4 instead of >=3)
        [Theory]
        [InlineData(0, null)]
        [InlineData(1, null)]
        [InlineData(2, null)]
        [InlineData(3, "momentum +2")]
        [InlineData(4, "momentum +2")]
        [InlineData(5, "momentum +3")]
        [InlineData(10, "momentum +3")]
        public async Task MomentumBonus_MatchesGameSessionThresholds(int streak, string? expectedBonusLabel)
        {
            // Verify the agent's momentum bonus at each threshold by calling DecideAsync
            // and inspecting BonusesApplied — exercises real production scoring code.
            var turn = MakeTurn(MakeOption(StatType.Charm));
            var context = MakeContext(momentum: streak);

            var decision = await _agent.DecideAsync(turn, context);

            if (expectedBonusLabel == null)
            {
                // No momentum bonus should appear
                Assert.DoesNotContain(decision.Scores[0].BonusesApplied,
                    b => b.Contains("momentum", StringComparison.OrdinalIgnoreCase));
            }
            else
            {
                Assert.Contains(expectedBonusLabel, decision.Scores[0].BonusesApplied);
            }
        }

        [Fact]
        public async Task TellBonus_Hardcoded2_MatchesEngine()
        {
            // SYNC: GameSession ResolveTurnAsync tellBonus = 2.
            // Verify that tell bonus is applied as exactly +2 to need calculation.
            var optionWithTell = MakeOption(StatType.Charm, hasTellBonus: true);
            var optionPlain = MakeOption(StatType.Charm);
            var player = MakeStats(charm: 3);
            var opponent = MakeStats(sa: 5);
            var turnTell = MakeTurn(optionWithTell);
            var turnPlain = MakeTurn(optionPlain);
            var context = MakeContext(player: player, opponent: opponent);

            var decisionTell = await _agent.DecideAsync(turnTell, context);
            var decisionPlain = await _agent.DecideAsync(turnPlain, context);

            // Tell bonus (+2) should raise success chance (lower need by 2)
            Assert.True(decisionTell.Scores[0].SuccessChance > decisionPlain.Scores[0].SuccessChance,
                "Tell bonus (+2) should increase success chance");

            // Verify the delta corresponds to exactly +2 on a d20
            // successChance = (21 - need) / 20; +2 to mod means need drops by 2 → chance increases by 2/20 = 0.1
            float delta = decisionTell.Scores[0].SuccessChance - decisionPlain.Scores[0].SuccessChance;
            Assert.True(Math.Abs(delta - 0.1f) < 0.001f,
                $"Tell bonus should shift success chance by exactly 0.1 (2/20), got {delta}");
        }
    }
}
