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
    /// Tests for HighestModAgent (the baseline IPlayerAgent implementation from #346).
    /// Verifies AC1 (IPlayerAgent interface contract) and AC4 (session runner integration).
    /// </summary>
    [Trait("Category", "Core")]
    public partial class HighestModAgentSpecTests
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

        private static StatBlock MakeDateeStats()
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

        private static PlayerAgentContext MakeContext(StatBlock player, StatBlock datee,
            int interest = 12, InterestState state = InterestState.Interested, int momentum = 0)
        {
            return new PlayerAgentContext(player, datee, interest, state, momentum,
                Array.Empty<string>(), 0, null, 1);
        }

        private static TurnStart MakeTurn(DialogueOption[] options, int interest = 12,
            InterestState state = InterestState.Interested, int momentum = 0, int turn = 1)
        {
            return new TurnStart(options,
                new GameStateSnapshot(interest, state, momentum, Array.Empty<string>(), turn));
        }

        // -- AC1: IPlayerAgent interface compliance --

        [Fact]
        public void HighestModAgent_ImplementsIPlayerAgent()
        {
            IPlayerAgent agent = new HighestModAgent();
            Assert.NotNull(agent);
        }

        // -- AC1: DecideAsync returns Task<PlayerDecision> --

        [Fact]
        public async Task DecideAsync_ReturnsCompletedTask()
        {
            var agent = new HighestModAgent();
            var options = new[] { new DialogueOption(StatType.Charm, "hi") };
            var turn = MakeTurn(options);
            var ctx = MakeContext(MakePlayerStats(), MakeDateeStats());

            var decision = await agent.DecideAsync(turn, ctx);
            Assert.NotNull(decision);
        }

        // -- AC1 + AC4: Picks option with highest modifier --

        [Fact]
        public async Task DecideAsync_PicksHighestModifierOption()
        {
            var agent = new HighestModAgent();
            var player = MakePlayerStats(); // Charm=4 is highest
            var options = new[]
            {
                new DialogueOption(StatType.Rizz, "low"),     // mod +1
                new DialogueOption(StatType.Charm, "high"),    // mod +4
                new DialogueOption(StatType.Honesty, "mid"),   // mod +3
            };
            var turn = MakeTurn(options);
            var ctx = MakeContext(player, MakeDateeStats());

            var decision = await agent.DecideAsync(turn, ctx);

            Assert.Equal(1, decision.OptionIndex);
        }

        // -- Edge case: Scores array length matches options --

        [Fact]
        public async Task DecideAsync_ScoresLengthMatchesOptionsLength()
        {
            var agent = new HighestModAgent();
            var options = new[]
            {
                new DialogueOption(StatType.Charm, "a"),
                new DialogueOption(StatType.Rizz, "b"),
                new DialogueOption(StatType.Honesty, "c"),
                new DialogueOption(StatType.Chaos, "d"),
            };
            var turn = MakeTurn(options);
            var ctx = MakeContext(MakePlayerStats(), MakeDateeStats());

            var decision = await agent.DecideAsync(turn, ctx);

            Assert.Equal(4, decision.Scores.Length);
        }

        // -- Edge case: single option always picks index 0 --

        [Fact]
        public async Task DecideAsync_SingleOption_ReturnsZero()
        {
            var agent = new HighestModAgent();
            var options = new[] { new DialogueOption(StatType.Wit, "only") };
            var turn = MakeTurn(options);
            var ctx = MakeContext(MakePlayerStats(), MakeDateeStats());

            var decision = await agent.DecideAsync(turn, ctx);

            Assert.Equal(0, decision.OptionIndex);
            Assert.Single(decision.Scores);
        }

        // -- Edge case: all identical stats → tiebreak to lowest index --

        [Fact]
        public async Task DecideAsync_IdenticalStats_TiebreaksToLowestIndex()
        {
            var agent = new HighestModAgent();
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
                new DialogueOption(StatType.Wit, "c"),
            };
            var turn = MakeTurn(options);
            var ctx = MakeContext(equalStats, MakeDateeStats());

            var decision = await agent.DecideAsync(turn, ctx);

            Assert.Equal(0, decision.OptionIndex);
        }
    }
}
