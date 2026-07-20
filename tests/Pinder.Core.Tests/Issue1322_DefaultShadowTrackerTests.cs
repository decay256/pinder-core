using System.Collections.Generic;
using Pinder.Core.Characters;
using Pinder.Core.Conversation;
using Pinder.Core.Interfaces;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;
using Pinder.Core.Traps;
using Xunit;

namespace Pinder.Core.Tests
{
    [Trait("Category", "Core")]
    public sealed class Issue1322_DefaultShadowTrackerTests
    {
        [Fact]
        public void Constructor_WhenPlayerShadowsOmitted_CreatesTrackerFromPlayerStats()
        {
            var playerStats = MakeStats(dread: 2, madness: 3);
            var session = new GameSession(
                MakeProfile("Player", playerStats),
                MakeProfile("Datee", MakeStats()),
                new NullLlmAdapter(),
                new FixedDice(),
                new NullTrapRegistry(),
                new GameSessionConfig(clock: TestHelpers.MakeClock()));

            var snapshot = session.CreateSnapshot();

            Assert.NotNull(session.State.PlayerShadows);
            Assert.Equal(2, snapshot.ShadowValues["Dread"]);
            Assert.Equal(3, snapshot.ShadowValues["Madness"]);
        }

        [Fact]
        public void CreateSnapshot_ReflectsGrowthOnDefaultPlayerShadowTracker()
        {
            var session = new GameSession(
                MakeProfile("Player", MakeStats(dread: 1)),
                MakeProfile("Datee", MakeStats()),
                new NullLlmAdapter(),
                new FixedDice(),
                new NullTrapRegistry(),
                new GameSessionConfig(clock: TestHelpers.MakeClock()));

            session.State.PlayerShadows!.ApplyGrowth(ShadowStatType.Dread, 2, "test growth");

            var snapshot = session.CreateSnapshot();

            Assert.Equal(3, snapshot.ShadowValues["Dread"]);
        }

        [Fact]
        public void Constructor_WhenDefaultTrackerStartsAtDreadTierThree_AppliesReducedInterest()
        {
            var session = new GameSession(
                MakeProfile("Player", MakeStats(dread: 18)),
                MakeProfile("Datee", MakeStats()),
                new NullLlmAdapter(),
                new FixedDice(),
                new NullTrapRegistry(),
                new GameSessionConfig(clock: TestHelpers.MakeClock()));

            Assert.Equal(8, session.State.Interest.Current);
        }

        [Fact]
        public void TurnOrchestratorSnapshot_ForwardsDefaultPlayerShadowTracker()
        {
            var session = new GameSession(
                MakeProfile("Player", MakeStats(dread: 1)),
                MakeProfile("Datee", MakeStats()),
                new NullLlmAdapter(),
                new FixedDice(),
                new NullTrapRegistry(),
                new GameSessionConfig(clock: TestHelpers.MakeClock()));
            session.State.PlayerShadows!.ApplyGrowth(ShadowStatType.Dread, 2, "test growth");

            var snapshot = TurnOrchestratorHelpers.CreateSnapshot(session.State, rules: null);

            Assert.Equal(3, snapshot.ShadowValues["Dread"]);
        }

        private static CharacterProfile MakeProfile(string name, StatBlock stats)
        {
            return TestHelpers.MakeCharacterProfile(stats, "system prompt", name, new TimingProfile(5, 1.0f, 0.0f, "neutral"), 1);
        }

        private static StatBlock MakeStats(int dread = 0, int madness = 0)
        {
            return new StatBlock(
                new Dictionary<StatType, int>
                {
                    [StatType.Charm] = 3,
                    [StatType.Rizz] = 3,
                    [StatType.Honesty] = 3,
                    [StatType.Chaos] = 3,
                    [StatType.Wit] = 3,
                    [StatType.SelfAwareness] = 3,
                },
                new Dictionary<ShadowStatType, int>
                {
                    [ShadowStatType.Dread] = dread,
                    [ShadowStatType.Madness] = madness,
                    [ShadowStatType.Despair] = 0,
                    [ShadowStatType.Denial] = 0,
                    [ShadowStatType.Fixation] = 0,
                    [ShadowStatType.Overthinking] = 0,
                });
        }

        private sealed class FixedDice : IDiceRoller
        {
            public int Roll(int sides) => 5;
        }
    }
}
