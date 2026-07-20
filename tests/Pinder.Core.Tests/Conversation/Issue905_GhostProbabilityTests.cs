using Xunit;
using Pinder.Core.Characters;
using Pinder.Core.Conversation;

namespace Pinder.Core.Tests.Conversation
{
    /// <summary>
    /// Issue #905: <see cref="GameStateSnapshot.GhostProbabilityPerTurn"/> is
    /// 0.25 when Bored and 0.0 otherwise.
    /// </summary>
    [Trait("Category", "Core")]
    public class Issue905_GhostProbabilityTests
    {
        private static CharacterProfile MakeProfile(string name)
        {
            return TestHelpers.MakeCharacterProfile(
                stats: TestHelpers.MakeStatBlock(2),
                assembledSystemPrompt: $"You are {name}.",
                displayName: name,
                timing: new TimingProfile(5, 0.0f, 0.0f, "neutral"),
                level: 1);
        }

        private static GameStateSnapshot MakeSnapshot(InterestState state)
        {
            return new GameStateSnapshot(
                interest: 0,
                state: state,
                momentumStreak: 0,
                activeTrapNames: System.Array.Empty<string>(),
                turnNumber: 1,
                ghostProbabilityPerTurn: state == InterestState.Bored ? 0.25 : 0.0);
        }

        [Fact]
        public void GhostProbability_WhenBored_Is025()
        {
            var snap = MakeSnapshot(InterestState.Bored);
            Assert.Equal(0.25, snap.GhostProbabilityPerTurn);
        }

        [Fact]
        public void GhostProbability_WhenLukewarm_IsZero()
        {
            var snap = MakeSnapshot(InterestState.Lukewarm);
            Assert.Equal(0.0, snap.GhostProbabilityPerTurn);
        }

        [Fact]
        public void GhostProbability_WhenInterested_IsZero()
        {
            var snap = MakeSnapshot(InterestState.Interested);
            Assert.Equal(0.0, snap.GhostProbabilityPerTurn);
        }

        [Fact]
        public void GhostProbability_WhenVeryIntoIt_IsZero()
        {
            var snap = MakeSnapshot(InterestState.VeryIntoIt);
            Assert.Equal(0.0, snap.GhostProbabilityPerTurn);
        }

        [Fact]
        public void GameSession_CreateSnapshot_WhenBored_HasGhostProb025()
        {
            var session = new GameSession(
                MakeProfile("P1"),
                MakeProfile("P2"),
                new NullLlmAdapter(),
                new FixedDice(5),
                new NullTrapRegistry(),
                new GameSessionConfig(clock: TestHelpers.MakeClock(), startingInterest: 2));

            var snap = session.CreateSnapshot();
            Assert.Equal(InterestState.Bored, snap.State);
            Assert.Equal(0.25, snap.GhostProbabilityPerTurn);
        }

        [Fact]
        public void GameSession_CreateSnapshot_WhenNotBored_HasGhostProb0()
        {
            var session = new GameSession(
                MakeProfile("P1"),
                MakeProfile("P2"),
                new NullLlmAdapter(),
                new FixedDice(5),
                new NullTrapRegistry(),
                new GameSessionConfig(clock: TestHelpers.MakeClock(), startingInterest: 10));

            var snap = session.CreateSnapshot();
            Assert.NotEqual(InterestState.Bored, snap.State);
            Assert.Equal(0.0, snap.GhostProbabilityPerTurn);
        }
    }
}
