using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Pinder.Core.Conversation;
using Pinder.Core.Stats;
using Pinder.SessionRunner;
using Xunit;

namespace Pinder.Core.Tests
{
    public partial class HighestModAgentSpecTests
    {
        // -- Error conditions --

        [Fact]
        public async Task DecideAsync_EmptyOptions_ThrowsInvalidOperationException()
        {
            var agent = new HighestModAgent();
            var turn = MakeTurn(Array.Empty<DialogueOption>());
            var ctx = MakeContext(MakePlayerStats(), MakeDateeStats());

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => agent.DecideAsync(turn, ctx));
        }

        [Fact]
        public async Task DecideAsync_NullTurn_ThrowsArgumentNullException()
        {
            var agent = new HighestModAgent();
            var ctx = MakeContext(MakePlayerStats(), MakeDateeStats());

            await Assert.ThrowsAsync<ArgumentNullException>(
                () => agent.DecideAsync(null!, ctx));
        }

        [Fact]
        public async Task DecideAsync_NullContext_ThrowsArgumentNullException()
        {
            var agent = new HighestModAgent();
            var options = new[] { new DialogueOption(StatType.Charm, "hi") };
            var turn = MakeTurn(options);

            await Assert.ThrowsAsync<ArgumentNullException>(
                () => agent.DecideAsync(turn, null!));
        }

        // -- Spec properties --

        [Fact]
        public async Task DecideAsync_SuccessChance_IsProbabilityNotPercentage()
        {
            var agent = new HighestModAgent();
            var player = MakePlayerStats(); // Charm +4
            var datee = MakeDateeStats(); // SA defence DC = 16 + 2 = 18
            var options = new[] { new DialogueOption(StatType.Charm, "charm line") };
            var turn = MakeTurn(options);
            var ctx = MakeContext(player, datee);

            var decision = await agent.DecideAsync(turn, ctx);

            Assert.True(decision.Scores[0].SuccessChance >= 0.0f);
            Assert.True(decision.Scores[0].SuccessChance <= 1.0f);
            Assert.Equal(0.35f, decision.Scores[0].SuccessChance);
        }

        [Fact]
        public async Task DecideAsync_ReasoningIsNeverNull()
        {
            var agent = new HighestModAgent();
            var options = new[] { new DialogueOption(StatType.Charm, "hi") };
            var turn = MakeTurn(options);
            var ctx = MakeContext(MakePlayerStats(), MakeDateeStats());

            var decision = await agent.DecideAsync(turn, ctx);

            Assert.NotNull(decision.Reasoning);
        }

        [Fact]
        public async Task DecideAsync_OptionIndex_WithinScoresRange()
        {
            var agent = new HighestModAgent();
            var options = new[]
            {
                new DialogueOption(StatType.Charm, "a"),
                new DialogueOption(StatType.Rizz, "b"),
                new DialogueOption(StatType.Honesty, "c"),
            };
            var turn = MakeTurn(options);
            var ctx = MakeContext(MakePlayerStats(), MakeDateeStats());

            var decision = await agent.DecideAsync(turn, ctx);

            Assert.True(decision.OptionIndex >= 0);
            Assert.True(decision.OptionIndex < decision.Scores.Length);
        }

        [Fact]
        public async Task DecideAsync_ScoreOptionIndicesMatchPositions()
        {
            var agent = new HighestModAgent();
            var options = new[]
            {
                new DialogueOption(StatType.Charm, "a"),
                new DialogueOption(StatType.Rizz, "b"),
            };
            var turn = MakeTurn(options);
            var ctx = MakeContext(MakePlayerStats(), MakeDateeStats());

            var decision = await agent.DecideAsync(turn, ctx);

            for (int i = 0; i < decision.Scores.Length; i++)
            {
                Assert.Equal(i, decision.Scores[i].OptionIndex);
            }
        }

        [Fact]
        public async Task DecideAsync_BonusesApplied_NeverNull()
        {
            var agent = new HighestModAgent();
            var options = new[]
            {
                new DialogueOption(StatType.Charm, "a"),
                new DialogueOption(StatType.Rizz, "b", callbackTurnNumber: 2),
                new DialogueOption(StatType.Honesty, "c", hasTellBonus: true),
            };
            var turn = MakeTurn(options);
            var ctx = MakeContext(MakePlayerStats(), MakeDateeStats());

            var decision = await agent.DecideAsync(turn, ctx);

            foreach (var score in decision.Scores)
            {
                Assert.NotNull(score.BonusesApplied);
            }
        }

        [Fact]
        public async Task DecideAsync_SpecExample1_CharmSuccessChance()
        {
            var agent = new HighestModAgent();
            var player = new StatBlock(
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
            var datee = new StatBlock(
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

            var options = new[]
            {
                new DialogueOption(StatType.Charm, "charm line"),
                new DialogueOption(StatType.Rizz, "rizz line"),
                new DialogueOption(StatType.Honesty, "hon line"),
                new DialogueOption(StatType.Chaos, "chaos line"),
            };
            var turn = MakeTurn(options, interest: 12, state: InterestState.Interested, momentum: 2, turn: 5);
            var ctx = MakeContext(player, datee, interest: 12, state: InterestState.Interested, momentum: 2);

            var decision = await agent.DecideAsync(turn, ctx);

            Assert.Equal(0, decision.OptionIndex);

            Assert.Equal(0.35f, decision.Scores[0].SuccessChance);
            Assert.Equal(0.25f, decision.Scores[1].SuccessChance);
            Assert.Equal(0.30f, decision.Scores[2].SuccessChance);
            Assert.Equal(0.25f, decision.Scores[3].SuccessChance);
        }

        [Fact]
        public async Task DecideAsync_AllRizzOptions_ReturnsIndex0()
        {
            var agent = new HighestModAgent();
            var options = new[]
            {
                new DialogueOption(StatType.Rizz, "rizz a"),
                new DialogueOption(StatType.Rizz, "rizz b"),
                new DialogueOption(StatType.Rizz, "rizz c"),
                new DialogueOption(StatType.Rizz, "rizz d"),
            };
            var turn = MakeTurn(options);
            var ctx = MakeContext(MakePlayerStats(), MakeDateeStats());

            var decision = await agent.DecideAsync(turn, ctx);

            Assert.Equal(0, decision.OptionIndex);
            var firstChance = decision.Scores[0].SuccessChance;
            foreach (var score in decision.Scores)
            {
                Assert.Equal(firstChance, score.SuccessChance);
            }
        }
    }
}
