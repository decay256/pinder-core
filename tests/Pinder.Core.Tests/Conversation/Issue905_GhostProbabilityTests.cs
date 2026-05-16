using System.Text.Json;
using Xunit;
using Pinder.Core.Characters;
using Pinder.Core.Conversation;

namespace Pinder.Core.Tests.Conversation
{
    /// <summary>
    /// Issue #905: <see cref="GameStateSnapshot.GhostProbabilityPerTurn"/> —
    /// 0.25 when Bored, 0.0 otherwise; serializes as
    /// <c>ghost_probability_per_turn</c>.
    /// </summary>
    [Trait("Category", "Core")]
    public class Issue905_GhostProbabilityTests
    {
        // ── Helpers ──────────────────────────────────────────────────────────

        private static CharacterProfile MakeProfile(string name)
        {
            return new CharacterProfile(
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

        // ── Value derivation ─────────────────────────────────────────────────

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

        // ── Serialization round-trip ─────────────────────────────────────────

        [Fact]
        public void Serialization_BoredState_ContainsSnakeCaseKey()
        {
            var snap = MakeSnapshot(InterestState.Bored);
            string json = JsonSerializer.Serialize(snap);

            Assert.Contains("\"ghost_probability_per_turn\"", json);
            Assert.Contains("0.25", json);
        }

        [Fact]
        public void Serialization_RoundTrip_PreservesValue()
        {
            var snap = MakeSnapshot(InterestState.Bored);
            string json = JsonSerializer.Serialize(snap);

            var restored = JsonSerializer.Deserialize<GameStateSnapshot>(json);
            Assert.NotNull(restored);
            Assert.Equal(0.25, restored!.GhostProbabilityPerTurn);
        }

        [Fact]
        public void Serialization_NonBored_GhostProbabilityIsZero()
        {
            var snap = MakeSnapshot(InterestState.Lukewarm);
            string json = JsonSerializer.Serialize(snap);

            Assert.Contains("\"ghost_probability_per_turn\"", json);

            var restored = JsonSerializer.Deserialize<GameStateSnapshot>(json);
            Assert.NotNull(restored);
            Assert.Equal(0.0, restored!.GhostProbabilityPerTurn);
        }

        // ── Session integration: CreateSnapshot derives correctly ────────────

        [Fact]
        public void GameSession_CreateSnapshot_WhenBored_HasGhostProb025()
        {
            // Force starting interest=2 (Bored territory: 1–4) via config.
            var session = new GameSession(
                MakeProfile("P1"),
                MakeProfile("P2"),
                new NullLlmAdapter(),
                new FixedDice(5),
                new NullTrapRegistry(),
                new GameSessionConfig(clock: TestHelpers.MakeClock(), startingInterest: 2));

            var snap = session.CreateSnapshot();
            // Confirm this is actually Bored state before asserting the probability.
            Assert.Equal(InterestState.Bored, snap.State);
            Assert.Equal(0.25, snap.GhostProbabilityPerTurn);
        }

        [Fact]
        public void GameSession_CreateSnapshot_WhenNotBored_HasGhostProb0()
        {
            // Force starting interest=10 (Interested territory: 10–15) via config.
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
