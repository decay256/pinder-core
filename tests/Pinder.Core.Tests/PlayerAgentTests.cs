using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Pinder.Core.Conversation;
using Pinder.Core.Stats;
using Pinder.SessionRunner;
using Xunit;

namespace Pinder.Core.Tests
{
    public class PlayerDecisionTests
    {
        [Fact]
        public void Constructor_ValidArgs_SetsProperties()
        {
            var scores = new[]
            {
                new OptionScore(0, 5.0f, 0.5f, 1.0f, Array.Empty<string>()),
                new OptionScore(1, 3.0f, 0.3f, 0.5f, new[] { "callback +2" })
            };
            var decision = new PlayerDecision(0, "Charm is best", scores);

            Assert.Equal(0, decision.OptionIndex);
            Assert.Equal("Charm is best", decision.Reasoning);
            Assert.Equal(2, decision.Scores.Length);
        }

        [Fact]
        public void Constructor_NullReasoning_Throws()
        {
            var scores = new[] { new OptionScore(0, 1.0f, 0.5f, 0.0f, Array.Empty<string>()) };
            Assert.Throws<ArgumentNullException>(() => new PlayerDecision(0, null!, scores));
        }

        [Fact]
        public void Constructor_NullScores_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new PlayerDecision(0, "reason", null!));
        }

        [Fact]
        public void Constructor_OptionIndexOutOfRange_Throws()
        {
            var scores = new[] { new OptionScore(0, 1.0f, 0.5f, 0.0f, Array.Empty<string>()) };
            Assert.Throws<ArgumentOutOfRangeException>(() => new PlayerDecision(1, "reason", scores));
        }

        [Fact]
        public void Constructor_NegativeOptionIndex_Throws()
        {
            var scores = new[] { new OptionScore(0, 1.0f, 0.5f, 0.0f, Array.Empty<string>()) };
            Assert.Throws<ArgumentOutOfRangeException>(() => new PlayerDecision(-1, "reason", scores));
        }
    }

    public class OptionScoreTests
    {
        [Fact]
        public void Constructor_ValidArgs_SetsProperties()
        {
            var score = new OptionScore(2, 7.5f, 0.65f, 1.5f, new[] { "tell +2", "combo" });

            Assert.Equal(2, score.OptionIndex);
            Assert.Equal(7.5f, score.Score);
            Assert.Equal(0.65f, score.SuccessChance);
            Assert.Equal(1.5f, score.ExpectedInterestGain);
            Assert.Equal(new[] { "tell +2", "combo" }, score.BonusesApplied);
        }

        [Fact]
        public void Constructor_NullBonuses_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new OptionScore(0, 1.0f, 0.5f, 0.0f, null!));
        }

        [Fact]
        public void SuccessChance_ClampedToZeroOne()
        {
            var high = new OptionScore(0, 1.0f, 1.5f, 0.0f, Array.Empty<string>());
            Assert.Equal(1.0f, high.SuccessChance);

            var low = new OptionScore(0, 1.0f, -0.5f, 0.0f, Array.Empty<string>());
            Assert.Equal(0.0f, low.SuccessChance);
        }
    }

    public class PlayerAgentContextTests
    {
        private static StatBlock MakeStats()
        {
            return new StatBlock(
                new Dictionary<StatType, int>
                {
                    { StatType.Charm, 4 }, { StatType.Rizz, 2 }, { StatType.Honesty, 3 },
                    { StatType.Chaos, 1 }, { StatType.Wit, 2 }, { StatType.SelfAwareness, 3 }
                },
                new Dictionary<ShadowStatType, int>
                {
                    { ShadowStatType.Madness, 0 }, { ShadowStatType.Despair, 0 },
                    { ShadowStatType.Denial, 0 }, { ShadowStatType.Fixation, 0 },
                    { ShadowStatType.Dread, 0 }, { ShadowStatType.Overthinking, 0 }
                });
        }

        [Fact]
        public void Constructor_ValidArgs_SetsProperties()
        {
            var player = MakeStats();
            var opponent = MakeStats();
            var ctx = new PlayerAgentContext(player, opponent, 12, InterestState.Interested, 2,
                new[] { "IckTrap" }, 4, null, 5);

            Assert.Same(player, ctx.PlayerStats);
            Assert.Same(opponent, ctx.OpponentStats);
            Assert.Equal(12, ctx.CurrentInterest);
            Assert.Equal(InterestState.Interested, ctx.InterestState);
            Assert.Equal(2, ctx.MomentumStreak);
            Assert.Single(ctx.ActiveTrapNames);
            Assert.Equal(4, ctx.SessionHorniness);
            Assert.Null(ctx.ShadowValues);
            Assert.Equal(5, ctx.TurnNumber);
        }

        [Fact]
        public void Constructor_NullPlayerStats_Throws()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new PlayerAgentContext(null!, MakeStats(), 10, InterestState.Interested, 0,
                    Array.Empty<string>(), 0, null, 1));
        }

        [Fact]
        public void Constructor_NullOpponentStats_Throws()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new PlayerAgentContext(MakeStats(), null!, 10, InterestState.Interested, 0,
                    Array.Empty<string>(), 0, null, 1));
        }

        [Fact]
        public void Constructor_NullActiveTrapNames_Throws()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new PlayerAgentContext(MakeStats(), MakeStats(), 10, InterestState.Interested, 0,
                    null!, 0, null, 1));
        }
    }

    public class HighestModAgentTests
    {
        private static StatBlock MakePlayerStats()
        {
            return new StatBlock(
                new Dictionary<StatType, int>
                {
                    { StatType.Charm, 4 }, { StatType.Rizz, 1 }, { StatType.Honesty, 3 },
                    { StatType.Chaos, 2 }, { StatType.Wit, 2 }, { StatType.SelfAwareness, 3 }
                },
                new Dictionary<ShadowStatType, int>
                {
                    { ShadowStatType.Madness, 0 }, { ShadowStatType.Despair, 0 },
                    { ShadowStatType.Denial, 0 }, { ShadowStatType.Fixation, 0 },
                    { ShadowStatType.Dread, 0 }, { ShadowStatType.Overthinking, 0 }
                });
        }

        private static StatBlock MakeOpponentStats()
        {
            return new StatBlock(
                new Dictionary<StatType, int>
                {
                    { StatType.Charm, 2 }, { StatType.Rizz, 3 }, { StatType.Honesty, 1 },
                    { StatType.Chaos, 2 }, { StatType.Wit, 1 }, { StatType.SelfAwareness, 2 }
                },
                new Dictionary<ShadowStatType, int>
                {
                    { ShadowStatType.Madness, 0 }, { ShadowStatType.Despair, 0 },
                    { ShadowStatType.Denial, 0 }, { ShadowStatType.Fixation, 0 },
                    { ShadowStatType.Dread, 0 }, { ShadowStatType.Overthinking, 0 }
                });
        }

        private static PlayerAgentContext MakeContext(StatBlock player, StatBlock opponent)
        {
            return new PlayerAgentContext(player, opponent, 12, InterestState.Interested, 0,
                Array.Empty<string>(), 0, null, 1);
        }

        [Fact]
        public async Task DecideAsync_PicksHighestModifier()
        {
            var agent = new HighestModAgent();
            var player = MakePlayerStats();
            var opponent = MakeOpponentStats();
            var options = new[]
            {
                new DialogueOption(StatType.Rizz, "rizz line"),   // mod +1
                new DialogueOption(StatType.Charm, "charm line"),  // mod +4
                new DialogueOption(StatType.Chaos, "chaos line"),  // mod +2
            };
            var turn = new TurnStart(options, new GameStateSnapshot(12, InterestState.Interested, 0, Array.Empty<string>(), 1));
            var ctx = MakeContext(player, opponent);

            var decision = await agent.DecideAsync(turn, ctx);

            Assert.Equal(1, decision.OptionIndex); // Charm has highest mod
            Assert.Equal(3, decision.Scores.Length);
            Assert.Contains("Charm", decision.Reasoning);
        }

        [Fact]
        public async Task DecideAsync_SingleOption_ReturnsIndex0()
        {
            var agent = new HighestModAgent();
            var player = MakePlayerStats();
            var opponent = MakeOpponentStats();
            var options = new[] { new DialogueOption(StatType.Wit, "wit line") };
            var turn = new TurnStart(options, new GameStateSnapshot(10, InterestState.Interested, 0, Array.Empty<string>(), 1));
            var ctx = MakeContext(player, opponent);

            var decision = await agent.DecideAsync(turn, ctx);

            Assert.Equal(0, decision.OptionIndex);
            Assert.Single(decision.Scores);
        }

        [Fact]
        public async Task DecideAsync_EmptyOptions_Throws()
        {
            var agent = new HighestModAgent();
            var player = MakePlayerStats();
            var opponent = MakeOpponentStats();
            var turn = new TurnStart(Array.Empty<DialogueOption>(), new GameStateSnapshot(10, InterestState.Interested, 0, Array.Empty<string>(), 1));
            var ctx = MakeContext(player, opponent);

            await Assert.ThrowsAsync<InvalidOperationException>(() => agent.DecideAsync(turn, ctx));
        }

        [Fact]
        public async Task DecideAsync_NullTurn_Throws()
        {
            var agent = new HighestModAgent();
            var ctx = MakeContext(MakePlayerStats(), MakeOpponentStats());

            await Assert.ThrowsAsync<ArgumentNullException>(() => agent.DecideAsync(null!, ctx));
        }

        [Fact]
        public async Task DecideAsync_NullContext_Throws()
        {
            var agent = new HighestModAgent();
            var turn = new TurnStart(
                new[] { new DialogueOption(StatType.Charm, "hi") },
                new GameStateSnapshot(10, InterestState.Interested, 0, Array.Empty<string>(), 1));

            await Assert.ThrowsAsync<ArgumentNullException>(() => agent.DecideAsync(turn, null!));
        }

        [Fact]
        public async Task DecideAsync_ScoresContainSuccessChance()
        {
            var agent = new HighestModAgent();
            var player = MakePlayerStats();
            var opponent = MakeOpponentStats();
            // Charm +4 vs SA defence DC = 16 + 2 = 18. Need 14. Success = (21-14)/20 = 0.35
            var options = new[] { new DialogueOption(StatType.Charm, "charm") };
            var turn = new TurnStart(options, new GameStateSnapshot(10, InterestState.Interested, 0, Array.Empty<string>(), 1));
            var ctx = MakeContext(player, opponent);

            var decision = await agent.DecideAsync(turn, ctx);

            Assert.Equal(0.35f, decision.Scores[0].SuccessChance);
        }

        [Fact]
        public async Task DecideAsync_TieBreaksToLowestIndex()
        {
            var agent = new HighestModAgent();
            // Both Charm and SA have modifier +4 and +3 respectively
            // Let's make them equal: both +3
            var equalStats = new StatBlock(
                new Dictionary<StatType, int>
                {
                    { StatType.Charm, 3 }, { StatType.Rizz, 3 }, { StatType.Honesty, 3 },
                    { StatType.Chaos, 3 }, { StatType.Wit, 3 }, { StatType.SelfAwareness, 3 }
                },
                new Dictionary<ShadowStatType, int>
                {
                    { ShadowStatType.Madness, 0 }, { ShadowStatType.Despair, 0 },
                    { ShadowStatType.Denial, 0 }, { ShadowStatType.Fixation, 0 },
                    { ShadowStatType.Dread, 0 }, { ShadowStatType.Overthinking, 0 }
                });
            var options = new[]
            {
                new DialogueOption(StatType.Charm, "a"),
                new DialogueOption(StatType.Rizz, "b"),
            };
            var turn = new TurnStart(options, new GameStateSnapshot(10, InterestState.Interested, 0, Array.Empty<string>(), 1));
            var ctx = MakeContext(equalStats, MakeOpponentStats());

            var decision = await agent.DecideAsync(turn, ctx);

            Assert.Equal(0, decision.OptionIndex); // tie breaks to lowest index
        }
    }
}
