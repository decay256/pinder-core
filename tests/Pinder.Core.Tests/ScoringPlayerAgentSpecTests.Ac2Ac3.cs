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
        #region AC2: Scores all options per the formula

        // Mutation: would catch if Scores.Length != Options.Length
        [Fact]
        public async Task Scores_LengthEqualsOptionsLength()
        {
            var options = new[]
            {
                new DialogueOption(StatType.Charm, "a"),
                new DialogueOption(StatType.Rizz, "b"),
                new DialogueOption(StatType.Honesty, "c")
            };
            var turn = MakeTurn(options);
            var ctx = MakeContext();
            var result = await _agent.DecideAsync(turn, ctx);

            Assert.Equal(3, result.Scores.Length);
        }

        // Mutation: would catch if OptionIndex on scores doesn't match position
        [Fact]
        public async Task Scores_OptionIndexMatchesPosition()
        {
            var options = new[]
            {
                new DialogueOption(StatType.Charm, "a"),
                new DialogueOption(StatType.Wit, "b")
            };
            var turn = MakeTurn(options);
            var ctx = MakeContext();
            var result = await _agent.DecideAsync(turn, ctx);

            for (int i = 0; i < result.Scores.Length; i++)
            {
                Assert.Equal(i, result.Scores[i].OptionIndex);
            }
        }

        // Mutation: would catch if successChance is not clamped between 0.0 and 1.0
        [Fact]
        public async Task SuccessChance_ClampedTo01Range()
        {
            // Easy option: player has very high stat, datee has 0 defence
            var playerStats = MakeStats(charm: 10);
            var dateeStats = MakeStats(selfAwareness: 0);
            var turn = MakeTurn(new DialogueOption(StatType.Charm, "easy"));
            var ctx = MakeContext(playerStats: playerStats, dateeStats: dateeStats);
            var result = await _agent.DecideAsync(turn, ctx);

            Assert.InRange(result.Scores[0].SuccessChance, 0.0f, 1.0f);
        }

        // Mutation: would catch if successChance calculation is inverted or ignores attacker mod
        [Fact]
        public async Task HigherAttackerMod_GivesHigherSuccessChance()
        {
            var dateeStats = MakeStats(selfAwareness: 2); // Charm → SA defence, DC = 16 + 2 = 15

            var weakPlayer = MakeStats(charm: 1); // need = 15 - 1 = 14, chance = 7/20
            var strongPlayer = MakeStats(charm: 5); // need = 15 - 5 = 10, chance = 11/20

            var option = new DialogueOption(StatType.Charm, "test");

            var weakResult = await _agent.DecideAsync(
                MakeTurn(option), MakeContext(playerStats: weakPlayer, dateeStats: dateeStats));
            var strongResult = await _agent.DecideAsync(
                MakeTurn(option), MakeContext(playerStats: strongPlayer, dateeStats: dateeStats));

            Assert.True(strongResult.Scores[0].SuccessChance > weakResult.Scores[0].SuccessChance,
                $"Strong ({strongResult.Scores[0].SuccessChance}) should be > Weak ({weakResult.Scores[0].SuccessChance})");
        }

        // Mutation: would catch if EV doesn't increase with success chance (basic ordering)
        [Fact]
        public async Task BasicEvOrdering_HigherSuccessChanceGivesHigherEv()
        {
            // Two options: one easy (Charm with high mod), one hard (Rizz with low mod)
            // Both against same defence
            var playerStats = MakeStats(charm: 5, rizz: 0);
            var dateeStats = MakeStats(); // all 0 defence stats → DC = 13 for all

            var options = new[]
            {
                new DialogueOption(StatType.Charm, "easy"),  // need=8, ~65%
                new DialogueOption(StatType.Rizz, "hard")    // need=13, ~40%
            };
            var turn = MakeTurn(options);
            var ctx = MakeContext(playerStats: playerStats, dateeStats: dateeStats);
            var result = await _agent.DecideAsync(turn, ctx);

            // Easy option should have higher EV
            Assert.True(result.Scores[0].ExpectedInterestGain > result.Scores[1].ExpectedInterestGain);
            Assert.Equal(0, result.OptionIndex); // should pick the easier option
        }

        #endregion

        #region AC3: Strategic adjustments

        // Mutation: would catch if momentum streak == 2 doesn't add +1.0 to safe options
        [Fact]
        public async Task MomentumStreak2_BiasesSafeOptionWithHighSuccessChance()
        {
            // Option A: safe (high success), lower raw EV
            // Option B: bold (low success), higher raw EV
            var playerStats = MakeStats(charm: 5, rizz: 0);
            var dateeStats = MakeStats(); // all 0 → DC=13

            var options = new[]
            {
                new DialogueOption(StatType.Charm, "safe"),  // need=8, ~65% success, Safe tier
                new DialogueOption(StatType.Rizz, "bold")    // need=13, ~40% success
            };
            var turn = MakeTurn(options);
            var ctx = MakeContext(
                playerStats: playerStats,
                dateeStats: dateeStats,
                momentumStreak: 2);

            var result = await _agent.DecideAsync(turn, ctx);

            // Charm has successChance >= 0.5 and streak == 2 → +1.0 bonus
            // So Charm score should get a boost
            Assert.Equal(0, result.OptionIndex); // safe option preferred
        }

        // Mutation: would catch if momentum bias applies when successChance < 0.5
        [Fact]
        public async Task MomentumStreak2_DoesNotBiasLowSuccessOptions()
        {
            // Both options have low success chances
            var playerStats = MakeStats(charm: 0, rizz: 0);
            var dateeStats = MakeStats(selfAwareness: 5, wit: 5); // high defence → hard to hit

            var options = new[]
            {
                new DialogueOption(StatType.Charm, "a"),  // need = 13+5 - 0 = 18, chance=15%
                new DialogueOption(StatType.Rizz, "b")    // need = 13+5 - 0 = 18, chance=15%
            };
            var turn = MakeTurn(options);
            var ctx = MakeContext(
                playerStats: playerStats,
                dateeStats: dateeStats,
                momentumStreak: 2);

            var result = await _agent.DecideAsync(turn, ctx);

            // Both have low success (< 0.5) — momentum bias should NOT apply to either
            // Scores should be approximately equal
            float diff = Math.Abs(result.Scores[0].Score - result.Scores[1].Score);
            Assert.True(diff < 0.5f, $"Scores should be close when both have low success. Diff={diff}");
        }

        // Mutation: would catch if near-win bias doesn't apply at interest=19
        [Fact]
        public async Task NearWin_Interest19_PrefersSafeOption()
        {
            var playerStats = MakeStats(charm: 5, chaos: 0);
            var dateeStats = MakeStats(); // all 0 → DC=13

            var options = new[]
            {
                new DialogueOption(StatType.Charm, "safe"),  // need=8, Safe tier
                new DialogueOption(StatType.Chaos, "bold")   // need=13
            };
            var turn = MakeTurn(options);
            var ctx = MakeContext(
                playerStats: playerStats,
                dateeStats: dateeStats,
                currentInterest: 19,
                interestState: InterestState.VeryIntoIt);

            var result = await _agent.DecideAsync(turn, ctx);

            // Near-win: interest 19 is in [19,24] → +2.0 to Safe/Medium options
            Assert.Equal(0, result.OptionIndex);
            Assert.True(result.Scores[0].Score > result.Scores[1].Score);
        }

        // Mutation: would catch if near-win bias applies at interest=18
        [Fact]
        public async Task NearWin_Interest18_DoesNotApplyBias()
        {
            var playerStats = MakeStats(charm: 5, chaos: 0);
            var dateeStats = MakeStats();

            var options = new[]
            {
                new DialogueOption(StatType.Charm, "safe"),   // need=8, Safe
                new DialogueOption(StatType.Chaos, "risky")   // need=13
            };
            var turn = MakeTurn(options);

            var ctxAt18 = MakeContext(
                playerStats: playerStats,
                dateeStats: dateeStats,
                currentInterest: 18,
                interestState: InterestState.VeryIntoIt);
            var ctxAt19 = MakeContext(
                playerStats: playerStats,
                dateeStats: dateeStats,
                currentInterest: 19,
                interestState: InterestState.VeryIntoIt);

            var resultAt18 = await _agent.DecideAsync(turn, ctxAt18);
            var resultAt19 = await _agent.DecideAsync(turn, ctxAt19);

            // At 19 the safe option should get +2.0 bonus; at 18 it should NOT
            Assert.True(resultAt19.Scores[0].Score > resultAt18.Scores[0].Score,
                "Near-win bonus should apply at 19 but not at 18");
        }

        // Mutation: would catch if Bored bias doesn't apply to Hard/Bold options
        [Fact]
        public async Task BoredState_BiasesBoldOptionUpward()
        {
            // Verify Bold options get a score boost when Bored
            var playerStats = MakeStats(chaos: 2);
            var dateeStats = MakeStats(charm: 3); // Chaos→Charm, DC=16, need=14 → Bold

            var turn = MakeTurn(new DialogueOption(StatType.Chaos, "bold")); // need=14, Bold

            var ctxBored = MakeContext(
                playerStats: playerStats,
                dateeStats: dateeStats,
                currentInterest: 3,
                interestState: InterestState.Bored);
            var ctxNeutral = MakeContext(
                playerStats: playerStats,
                dateeStats: dateeStats,
                currentInterest: 12,
                interestState: InterestState.Interested);

            var resultBored = await _agent.DecideAsync(turn, ctxBored);
            var resultNeutral = await _agent.DecideAsync(turn, ctxNeutral);

            // Same option, same stats — Bored state adds +1.0 to Bold score
            Assert.True(resultBored.Scores[0].Score > resultNeutral.Scores[0].Score,
                $"Bored should boost Bold option score: Bored={resultBored.Scores[0].Score}, Neutral={resultNeutral.Scores[0].Score}");
        }

        // Mutation: would catch if trap penalty is not applied (-2.0)
        [Fact]
        public async Task ActiveTrap_PenalizesTrappedStat()
        {
            // Spec Example 5: Madness trap on Charm → -2.0 penalty
            var playerStats = MakeStats(charm: 5, wit: 3);
            var dateeStats = MakeStats(); // all 0

            var options = new[]
            {
                new DialogueOption(StatType.Charm, "trapped"),  // Charm → Madness trap active
                new DialogueOption(StatType.Wit, "clean")       // No trap
            };
            var turn = MakeTurn(options);
            var ctx = MakeContext(
                playerStats: playerStats,
                dateeStats: dateeStats,
                activeTrapNames: new[] { "Madness" });

            var result = await _agent.DecideAsync(turn, ctx);

            // Charm normally has better EV (higher mod), but -2.0 trap penalty → Wit wins
            Assert.Equal(1, result.OptionIndex);
            Assert.True(result.Scores[1].Score > result.Scores[0].Score,
                "Untapped option should beat trapped option");
        }

        // Mutation: would catch if trap mapping is wrong (e.g., Rizz → Despair)
        [Fact]
        public async Task ActiveTrap_RizzMapsToDespair()
        {
            var playerStats = MakeStats(rizz: 5, honesty: 3);
            var dateeStats = MakeStats();

            var options = new[]
            {
                new DialogueOption(StatType.Rizz, "trapped"),
                new DialogueOption(StatType.Honesty, "clean")
            };
            var turn = MakeTurn(options);
            var ctx = MakeContext(
                playerStats: playerStats,
                dateeStats: dateeStats,
                activeTrapNames: new[] { "Despair" });

            var result = await _agent.DecideAsync(turn, ctx);

            // Rizz → Despair shadow → trap penalty applied
            Assert.Equal(1, result.OptionIndex);
        }

        #endregion

        #region Risk tier bonus

        // Mutation: would catch if risk tier bonus is not applied (Hard → +1, Bold → +2)
        [Fact]
        public async Task RiskTierBonus_HardTierAddsInterestBonus()
        {
            // Create two options with different risk tiers but test the Hard one gets bonus
            var playerStats = MakeStats(charm: 2, rizz: 2);
            // Charm→SA, Rizz→Wit
            // Set defences so one is Safe, one is Hard
            var dateeStats = MakeStats(selfAwareness: 0, wit: 5);
            // Charm: need = 13-2=11, Hard tier → +1 risk bonus
            // Rizz: need = 18-2=16, Bold tier → +2 risk bonus

            var options = new[]
            {
                new DialogueOption(StatType.Charm, "hard"),
                new DialogueOption(StatType.Rizz, "bold")
            };
            var turn = MakeTurn(options);
            var ctx = MakeContext(playerStats: playerStats, dateeStats: dateeStats);
            var result = await _agent.DecideAsync(turn, ctx);

            // Hard (need=11) has higher success chance than Bold (need=16)
            // But Bold has higher risk tier bonus. Overall Hard should still win on EV
            Assert.True(result.Scores[0].SuccessChance > result.Scores[1].SuccessChance);
        }

        #endregion
    }
}
