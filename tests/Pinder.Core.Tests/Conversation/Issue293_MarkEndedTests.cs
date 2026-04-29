using System.Threading.Tasks;
using Xunit;
using Pinder.Core.Characters;
using Pinder.Core.Conversation;
using Pinder.Core.Interfaces;
using Pinder.Core.Traps;

namespace Pinder.Core.Tests.Conversation
{
    /// <summary>
    /// Regression tests for the typed <see cref="GameSession.MarkEnded"/> API
    /// added in pinder-web#293. Replaces the reflection-based wrapper used by
    /// SessionStore.SetEngineEnded when rehydrating an already-finished
    /// session from persistent storage.
    /// </summary>
    [Trait("Category", "Core")]
    public class Issue293_MarkEndedTests
    {
        private sealed class NullLlm : ILlmAdapter
        {
            public Task<DialogueOption[]> GetDialogueOptionsAsync(DialogueContext context) => Task.FromResult(new DialogueOption[0]);
            public Task<string> DeliverMessageAsync(DeliveryContext context) => Task.FromResult("");
            public Task<OpponentResponse> GetOpponentResponseAsync(OpponentContext context) => Task.FromResult(new OpponentResponse("", null, null));
            public Task<string?> GetInterestChangeBeatAsync(InterestChangeContext context) => Task.FromResult<string?>("");
            public Task<string> ApplyHorninessOverlayAsync(string message, string instruction, string? opponentContext = null) => Task.FromResult(message);
            public Task<string> ApplyShadowCorruptionAsync(string message, string instruction, Pinder.Core.Stats.ShadowStatType shadow) => Task.FromResult(message);
            public Task<string> ApplyTrapOverlayAsync(string message, string trapInstruction, string trapName, string? opponentContext = null) => Task.FromResult(message);
        }

        private sealed class FixedDice : IDiceRoller
        {
            public int Roll(int sides) => 5;
        }

        private static CharacterProfile MakeProfile(string name)
        {
            return new CharacterProfile(
                stats: TestHelpers.MakeStatBlock(2),
                assembledSystemPrompt: $"You are {name}.",
                displayName: name,
                timing: new TimingProfile(5, 0.0f, 0.0f, "neutral"),
                level: 1);
        }

        private static GameSession NewSession()
        {
            return new GameSession(
                MakeProfile("P"),
                MakeProfile("O"),
                new NullLlm(),
                new FixedDice(),
                new NullTrapRegistry(),
                new GameSessionConfig(clock: TestHelpers.MakeClock()));
        }

        [Theory]
        [InlineData(GameOutcome.DateSecured)]
        [InlineData(GameOutcome.Unmatched)]
        [InlineData(GameOutcome.Ghosted)]
        public void MarkEnded_SetsTerminalFlags(GameOutcome outcome)
        {
            var session = NewSession();
            Assert.False(session.IsEnded);
            Assert.Null(session.Outcome);

            session.MarkEnded(outcome);

            Assert.True(session.IsEnded);
            Assert.Equal(outcome, session.Outcome);
        }

        [Fact]
        public async Task MarkEnded_StartTurnAsync_ThrowsGameEndedExceptionWithOutcome()
        {
            var session = NewSession();

            session.MarkEnded(GameOutcome.DateSecured);

            var ex = await Assert.ThrowsAsync<GameEndedException>(() => session.StartTurnAsync());
            Assert.Equal(GameOutcome.DateSecured, ex.Outcome);
        }

        [Fact]
        public async Task MarkEnded_OverridesUnmatched_ThrowsWithCorrectOutcome()
        {
            // Different outcome to prove the value flows through, not a default.
            var session = NewSession();
            session.MarkEnded(GameOutcome.Unmatched);

            var ex = await Assert.ThrowsAsync<GameEndedException>(() => session.StartTurnAsync());
            Assert.Equal(GameOutcome.Unmatched, ex.Outcome);
        }
    }
}
