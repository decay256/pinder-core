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
        // AC6-5: Tell bonus factored into need
        [Fact]
        public async Task DecideAsync_TellBonus_LowersNeedBy2()
        {
            var player = MakeStats(charm: 3);
            var opponent = MakeStats(); // DC=13 for Charm
            // Without tell: need = 13 - 3 = 10, successChance = 11/20 = 0.55
            // With tell: need = 13 - (3+2) = 8, successChance = 13/20 = 0.65
            var optionNoTell = MakeOption(StatType.Charm, hasTellBonus: false);
            var optionWithTell = MakeOption(StatType.Charm, hasTellBonus: true);

            var turnNoTell = MakeTurn(optionNoTell);
            var turnWithTell = MakeTurn(optionWithTell);
            var context = MakeContext(player: player, opponent: opponent);

            var dNoTell = await _agent.DecideAsync(turnNoTell, context);
            var dWithTell = await _agent.DecideAsync(turnWithTell, context);

            Assert.True(dWithTell.Scores[0].SuccessChance > dNoTell.Scores[0].SuccessChance);
            // Tell lowers need by 2, so successChance increases by 2/20 = 0.10
            Assert.Equal((double)(dNoTell.Scores[0].SuccessChance + 0.10f),
                         (double)dWithTell.Scores[0].SuccessChance, 3);
        }

        // AC6-6: Combo bonus adds interest on success
        [Fact]
        public async Task DecideAsync_ComboBonus_IncreasesExpectedGain()
        {
            var player = MakeStats(charm: 3);
            var opponent = MakeStats();
            var optionNoCombo = MakeOption(StatType.Charm);
            var optionWithCombo = MakeOption(StatType.Charm, comboName: "The Switcheroo");

            var turnNoCombo = MakeTurn(optionNoCombo);
            var turnWithCombo = MakeTurn(optionWithCombo);
            var context = MakeContext(player: player, opponent: opponent);

            var dNoCombo = await _agent.DecideAsync(turnNoCombo, context);
            var dWithCombo = await _agent.DecideAsync(turnWithCombo, context);

            // Combo adds +1 to expected gain on success, weighted by successChance
            Assert.True(dWithCombo.Scores[0].ExpectedInterestGain >
                        dNoCombo.Scores[0].ExpectedInterestGain);
        }

        // AC6-7: Callback bonus lowers need
        [Fact]
        public async Task DecideAsync_CallbackBonus_LowersNeed()
        {
            var player = MakeStats(charm: 3);
            var opponent = MakeStats();
            // Turn 5, callback to turn 0 (opener) → CallbackBonus.Compute(5, 0) = 3
            var optionWithCallback = MakeOption(StatType.Charm, callbackTurn: 0);
            var optionNoCallback = MakeOption(StatType.Charm);

            var turnCallback = MakeTurn(optionWithCallback);
            var turnNoCallback = MakeTurn(optionNoCallback);
            var context = MakeContext(player: player, opponent: opponent, turnNumber: 5);

            var dCallback = await _agent.DecideAsync(turnCallback, context);
            var dNoCallback = await _agent.DecideAsync(turnNoCallback, context);

            // Callback bonus = 3 → need drops by 3 → successChance increases by 3/20 = 0.15
            Assert.True(dCallback.Scores[0].SuccessChance > dNoCallback.Scores[0].SuccessChance);
            Assert.Equal((double)(dNoCallback.Scores[0].SuccessChance + 0.15f),
                         (double)dCallback.Scores[0].SuccessChance, 3);
        }

        // Null argument tests
        [Fact]
        public async Task DecideAsync_NullTurn_Throws()
        {
            await Assert.ThrowsAsync<ArgumentNullException>(
                () => _agent.DecideAsync(null!, MakeContext()));
        }

        [Fact]
        public async Task DecideAsync_NullContext_Throws()
        {
            var turn = MakeTurn(MakeOption(StatType.Charm));
            await Assert.ThrowsAsync<ArgumentNullException>(
                () => _agent.DecideAsync(turn, null!));
        }

        // Single option
        [Fact]
        public async Task DecideAsync_SingleOption_ReturnsIt()
        {
            var turn = MakeTurn(MakeOption(StatType.Charm));
            var context = MakeContext();

            var decision = await _agent.DecideAsync(turn, context);

            Assert.Equal(0, decision.OptionIndex);
            Assert.Single(decision.Scores);
        }

        // Reasoning is non-empty
        [Fact]
        public async Task DecideAsync_ReasoningIsNotEmpty()
        {
            var turn = MakeTurn(
                MakeOption(StatType.Charm),
                MakeOption(StatType.Rizz));
            var context = MakeContext();

            var decision = await _agent.DecideAsync(turn, context);

            Assert.False(string.IsNullOrWhiteSpace(decision.Reasoning));
        }

        // BonusesApplied populated correctly
        [Fact]
        public async Task DecideAsync_BonusesApplied_IncludesTellAndCombo()
        {
            var player = MakeStats(charm: 3);
            var opponent = MakeStats();
            var turn = MakeTurn(
                MakeOption(StatType.Charm, hasTellBonus: true, comboName: "TestCombo", callbackTurn: 0));
            var context = MakeContext(player: player, opponent: opponent, turnNumber: 5);

            var decision = await _agent.DecideAsync(turn, context);

            var bonuses = decision.Scores[0].BonusesApplied;
            Assert.Contains("tell +2", bonuses);
            Assert.Contains("combo: TestCombo", bonuses);
            Assert.Contains("callback +3", bonuses);
        }

        // Momentum bonus in bonuses applied
        [Fact]
        public async Task DecideAsync_MomentumBonus_InBonusesApplied()
        {
            var turn = MakeTurn(MakeOption(StatType.Charm));
            var context = MakeContext(momentum: 3);

            var decision = await _agent.DecideAsync(turn, context);

            Assert.Contains("momentum +2", decision.Scores[0].BonusesApplied);
        }

        // Tie-breaking: first option wins on equal score
        [Fact]
        public async Task DecideAsync_TiedScores_PicksFirstOption()
        {
            // Two identical options should produce identical scores, first wins
            var player = MakeStats(charm: 3, rizz: 3);
            // Need same DC for both — opponent SA = opponent Wit
            var opponent = MakeStats(sa: 2, wit: 2);
            var turn = MakeTurn(
                MakeOption(StatType.Charm),
                MakeOption(StatType.Rizz));
            var context = MakeContext(player: player, opponent: opponent);

            var decision = await _agent.DecideAsync(turn, context);

            // Scores should be equal
            Assert.Equal((double)decision.Scores[0].Score, (double)decision.Scores[1].Score, 5);
            // First option wins tie
            Assert.Equal(0, decision.OptionIndex);
        }
    }
}
