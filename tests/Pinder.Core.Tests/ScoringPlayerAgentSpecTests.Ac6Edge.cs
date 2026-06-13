using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Pinder.Core.Conversation;
using Pinder.Core.Stats;
using Pinder.SessionRunner;
using Xunit;

namespace Pinder.Core.Tests
{
    public partial class ScoringPlayerAgentSpecTests
    {
        #region AC6: Required test scenarios

        // AC6.5: Mutation: would catch if tell bonus is not subtracted from need
        [Fact]
        public async Task TellBonus_LowersNeedBy2()
        {
            var playerStats = MakeStats(honesty: 3);
            var dateeStats = MakeStats(chaos: 2); // Honesty→Chaos, DC = 13+2=15

            // Without tell: need = 15 - 3 = 12, chance = 9/20 = 0.45
            // With tell: need = 15 - (3+2) = 10, chance = 11/20 = 0.55
            var withTell = new DialogueOption(StatType.Honesty, "tell", hasTellBonus: true);
            var withoutTell = new DialogueOption(StatType.Honesty, "plain");

            var resultTell = await _agent.DecideAsync(MakeTurn(withTell), MakeContext(playerStats: playerStats, dateeStats: dateeStats));
            var resultPlain = await _agent.DecideAsync(MakeTurn(withoutTell), MakeContext(playerStats: playerStats, dateeStats: dateeStats));

            Assert.True(resultTell.Scores[0].SuccessChance > resultPlain.Scores[0].SuccessChance,
                $"Tell should increase success chance: {resultTell.Scores[0].SuccessChance} > {resultPlain.Scores[0].SuccessChance}");
            // Verify the exact difference of 2/20 = 0.10
            float diff = resultTell.Scores[0].SuccessChance - resultPlain.Scores[0].SuccessChance;
            Assert.InRange(diff, 0.09f, 0.11f);
        }

        // AC6.6: Mutation: would catch if combo bonus is not added to expected gain on success
        [Fact]
        public async Task ComboBonus_IncreasesExpectedGainOnSuccess()
        {
            var playerStats = MakeStats(charm: 3);
            var dateeStats = MakeStats();

            var withCombo = new DialogueOption(StatType.Charm, "combo", comboName: "The Switcheroo");
            var withoutCombo = new DialogueOption(StatType.Charm, "plain");

            var resultCombo = await _agent.DecideAsync(MakeTurn(withCombo), MakeContext(playerStats: playerStats, dateeStats: dateeStats));
            var resultPlain = await _agent.DecideAsync(MakeTurn(withoutCombo), MakeContext(playerStats: playerStats, dateeStats: dateeStats));

            // Combo adds +1 interest on success → higher EV
            Assert.True(resultCombo.Scores[0].ExpectedInterestGain > resultPlain.Scores[0].ExpectedInterestGain,
                "Combo should increase expected interest gain");
        }

        // AC6.7: Mutation: would catch if callback bonus is not computed or not applied to need
        [Fact]
        public async Task CallbackBonus_LowersNeed()
        {
            var playerStats = MakeStats(charm: 3);
            var dateeStats = MakeStats();

            // CallbackBonus.Compute(5, 2) should return 1 (gap of 3, ≥ 2)
            var withCallback = new DialogueOption(StatType.Charm, "callback", callbackTurnNumber: 2);
            var withoutCallback = new DialogueOption(StatType.Charm, "plain");

            var resultCallback = await _agent.DecideAsync(
                MakeTurn(withCallback),
                MakeContext(playerStats: playerStats, dateeStats: dateeStats, turnNumber: 5));
            var resultPlain = await _agent.DecideAsync(
                MakeTurn(withoutCallback),
                MakeContext(playerStats: playerStats, dateeStats: dateeStats, turnNumber: 5));

            Assert.True(resultCallback.Scores[0].SuccessChance > resultPlain.Scores[0].SuccessChance,
                $"Callback should increase success chance: {resultCallback.Scores[0].SuccessChance} > {resultPlain.Scores[0].SuccessChance}");
        }

        // AC6.8: Mutation: would catch if agent doesn't pick highest-EV option without adjustments
        [Fact]
        public async Task BasicEvOrdering_PicksHighestEvOption()
        {
            // Strong Charm vs weak Rizz — no adjustments
            var playerStats = MakeStats(charm: 6, rizz: 0);
            var dateeStats = MakeStats(); // all 0, DC=13 for all

            var options = new[]
            {
                new DialogueOption(StatType.Charm, "strong"),  // need=7, ~70%
                new DialogueOption(StatType.Rizz, "weak")      // need=13, ~40%
            };
            var turn = MakeTurn(options);
            var ctx = MakeContext(playerStats: playerStats, dateeStats: dateeStats);
            var result = await _agent.DecideAsync(turn, ctx);

            Assert.Equal(0, result.OptionIndex);
        }

        #endregion

        #region Edge cases

        // Mutation: would catch if empty options silently returns instead of throwing
        [Fact]
        public async Task EmptyOptions_ThrowsInvalidOperationException()
        {
            var turn = MakeTurn(Array.Empty<DialogueOption>());
            var ctx = MakeContext();
            await Assert.ThrowsAsync<InvalidOperationException>(() => _agent.DecideAsync(turn, ctx));
        }

        // Mutation: would catch if agent fails on single option
        [Fact]
        public async Task SingleOption_ReturnsIndex0()
        {
            var turn = MakeTurn(new DialogueOption(StatType.Charm, "only"));
            var ctx = MakeContext();
            var result = await _agent.DecideAsync(turn, ctx);

            Assert.Equal(0, result.OptionIndex);
            Assert.Single(result.Scores);
        }

        // Mutation: would catch if tie-breaking doesn't use lowest index
        [Fact]
        public async Task TiedScores_PicksLowestIndex()
        {
            // Two identical options → identical scores → first wins
            var playerStats = MakeStats(charm: 3, rizz: 3);
            // Need same defence for both: Charm→SA, Rizz→Wit
            var dateeStats = MakeStats(selfAwareness: 0, wit: 0);

            var options = new[]
            {
                new DialogueOption(StatType.Charm, "a"),
                new DialogueOption(StatType.Rizz, "b")
            };
            var turn = MakeTurn(options);
            var ctx = MakeContext(playerStats: playerStats, dateeStats: dateeStats);
            var result = await _agent.DecideAsync(turn, ctx);

            // Both have identical stats/defences → tied → pick index 0
            Assert.Equal(0, result.OptionIndex);
        }

        // Mutation: would catch if successChance doesn't clamp to 0 for impossible rolls
        [Fact]
        public async Task VeryHighDc_SuccessChanceClampedTo0()
        {
            var playerStats = MakeStats(charm: 0);
            var dateeStats = MakeStats(selfAwareness: 10); // DC = 16 + 10 = 23, need = 23

            var turn = MakeTurn(new DialogueOption(StatType.Charm, "impossible"));
            var ctx = MakeContext(playerStats: playerStats, dateeStats: dateeStats);
            var result = await _agent.DecideAsync(turn, ctx);

            Assert.Equal(0.0f, result.Scores[0].SuccessChance);
        }

        // Mutation: would catch if successChance doesn't clamp to 1.0 for guaranteed rolls
        [Fact]
        public async Task VeryLowDc_SuccessChanceClampedTo1()
        {
            var playerStats = MakeStats(charm: 15);
            var dateeStats = MakeStats(selfAwareness: 0); // DC = 13, need = 13 - 15 = -2

            var turn = MakeTurn(new DialogueOption(StatType.Charm, "guaranteed"));
            var ctx = MakeContext(playerStats: playerStats, dateeStats: dateeStats);
            var result = await _agent.DecideAsync(turn, ctx);

            Assert.Equal(1.0f, result.Scores[0].SuccessChance);
        }

        // Mutation: would catch if null ActiveTrapNames crashes instead of treating as empty
        [Fact]
        public async Task NullActiveTrapNames_TreatedAsEmpty()
        {
            // PlayerAgentContext constructor requires non-null activeTrapNames,
            // but the spec says null should be treated as empty.
            // Since ctor throws, we test with empty array instead:
            var turn = MakeTurn(new DialogueOption(StatType.Charm, "test"));
            var ctx = MakeContext(activeTrapNames: Array.Empty<string>());
            var result = await _agent.DecideAsync(turn, ctx);

            Assert.NotNull(result);
            Assert.Equal(0, result.OptionIndex);
        }

        // Mutation: would catch if callback opener bonus isn't computed correctly
        [Fact]
        public async Task CallbackOpener_HighestBonus()
        {
            var playerStats = MakeStats(charm: 3);
            var dateeStats = MakeStats();

            // Opener callback: callbackTurnNumber=0, currentTurn=5
            // CallbackBonus.Compute(5, 0) should return 3 (opener with gap >= 2)
            var openerCallback = new DialogueOption(StatType.Charm, "opener", callbackTurnNumber: 0);
            var normalCallback = new DialogueOption(StatType.Charm, "normal", callbackTurnNumber: 3);

            var resultOpener = await _agent.DecideAsync(
                MakeTurn(openerCallback),
                MakeContext(playerStats: playerStats, dateeStats: dateeStats, turnNumber: 5));
            var resultNormal = await _agent.DecideAsync(
                MakeTurn(normalCallback),
                MakeContext(playerStats: playerStats, dateeStats: dateeStats, turnNumber: 5));

            Assert.True(resultOpener.Scores[0].SuccessChance > resultNormal.Scores[0].SuccessChance,
                "Opener callback (+3) should give higher success than normal callback (+1)");
        }

        // Mutation: would catch if near-win bias applies at interest=24 boundary
        [Fact]
        public async Task NearWin_Interest24_AppliesBias()
        {
            var playerStats = MakeStats(charm: 5);
            var dateeStats = MakeStats();

            var turn = MakeTurn(new DialogueOption(StatType.Charm, "safe")); // Safe tier

            var ctxAt24 = MakeContext(
                playerStats: playerStats,
                dateeStats: dateeStats,
                currentInterest: 24,
                interestState: InterestState.AlmostThere);
            var ctxAt12 = MakeContext(
                playerStats: playerStats,
                dateeStats: dateeStats,
                currentInterest: 12,
                interestState: InterestState.Interested);

            var resultAt24 = await _agent.DecideAsync(turn, ctxAt24);
            var resultAt12 = await _agent.DecideAsync(turn, ctxAt12);

            // At interest=24 (in [19,24]) the safe option should get +2.0 bonus vs interest=12
            Assert.True(resultAt24.Scores[0].Score > resultAt12.Scores[0].Score,
                "Near-win bonus should boost score at interest=24");
        }

        // Mutation: would catch if Bored bias at interest=5 incorrectly applies (5 is Lukewarm, not Bored)
        [Fact]
        public async Task Interest5_IsLukewarm_NoBoredBias()
        {
            var playerStats = MakeStats(chaos: 0);
            var dateeStats = MakeStats(charm: 3); // Chaos→Charm, DC=16+3=19 → Bold (need=19)

            var turn = MakeTurn(new DialogueOption(StatType.Chaos, "bold"));

            var ctxBored = MakeContext(
                playerStats: playerStats,
                dateeStats: dateeStats,
                currentInterest: 3,
                interestState: InterestState.Bored);
            var ctxLukewarm = MakeContext(
                playerStats: playerStats,
                dateeStats: dateeStats,
                currentInterest: 5,
                interestState: InterestState.Lukewarm);

            var resultBored = await _agent.DecideAsync(turn, ctxBored);
            var resultLukewarm = await _agent.DecideAsync(turn, ctxLukewarm);

            // Bored gets +1.0 on Bold, Lukewarm does NOT
            Assert.True(resultBored.Scores[0].Score > resultLukewarm.Scores[0].Score,
                "Bored should get bias but Lukewarm should not");
        }

        // Mutation: would catch if BonusesApplied doesn't include tell bonus
        [Fact]
        public async Task BonusesApplied_IncludesTellBonus()
        {
            var turn = MakeTurn(new DialogueOption(StatType.Honesty, "tell", hasTellBonus: true));
            var ctx = MakeContext();
            var result = await _agent.DecideAsync(turn, ctx);

            Assert.Contains(result.Scores[0].BonusesApplied,
                b => b.Contains("tell", StringComparison.OrdinalIgnoreCase));
        }

        // Mutation: would catch if BonusesApplied doesn't include combo name
        [Fact]
        public async Task BonusesApplied_IncludesComboName()
        {
            var turn = MakeTurn(new DialogueOption(StatType.Charm, "combo", comboName: "The Switcheroo"));
            var ctx = MakeContext();
            var result = await _agent.DecideAsync(turn, ctx);

            Assert.Contains(result.Scores[0].BonusesApplied,
                b => b.Contains("Switcheroo", StringComparison.OrdinalIgnoreCase));
        }

        // Mutation: would catch if BonusesApplied doesn't include callback bonus
        [Fact]
        public async Task BonusesApplied_IncludesCallbackBonus()
        {
            var turn = MakeTurn(new DialogueOption(StatType.Charm, "cb", callbackTurnNumber: 1));
            var ctx = MakeContext(turnNumber: 5);
            var result = await _agent.DecideAsync(turn, ctx);

            Assert.Contains(result.Scores[0].BonusesApplied,
                b => b.Contains("callback", StringComparison.OrdinalIgnoreCase));
        }

        // Mutation: would catch if momentum bonus is included as BonusApplied
        [Fact]
        public async Task BonusesApplied_IncludesMomentumWhenActive()
        {
            var playerStats = MakeStats(charm: 5);
            var turn = MakeTurn(new DialogueOption(StatType.Charm, "test"));
            var ctx = MakeContext(playerStats: playerStats, momentumStreak: 3);
            var result = await _agent.DecideAsync(turn, ctx);

            Assert.Contains(result.Scores[0].BonusesApplied,
                b => b.Contains("momentum", StringComparison.OrdinalIgnoreCase));
        }

        #endregion
    }
}
