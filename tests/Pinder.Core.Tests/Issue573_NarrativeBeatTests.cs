using System.Threading.Tasks;
using Pinder.Core.Characters;
using Pinder.Core.Conversation;
using Pinder.Core.Interfaces;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;
using Pinder.Core.Traps;
using Xunit;

namespace Pinder.Core.Tests
{
    public class Issue573_NarrativeBeatTests
    {
        private static CharacterProfile MakeProfile(string name)
        {
            return new CharacterProfile(
                stats: TestHelpers.MakeStatBlock(),
                assembledSystemPrompt: $"You are {name}.",
                displayName: name,
                timing: new TimingProfile(5, 1.0f, 0.0f, "neutral"),
                level: 1);
        }

        // What: AC3 — TurnResult.NarrativeBeat field holds no LLM-generated string but a hardcoded state signal
        // Mutation: Fails if NarrativeBeat is null on state change or uses an old format like the quoted beat text
        [Fact]
        public async Task ResolveTurnAsync_StateChanges_ReturnsHardcodedNarrativeBeat()
        {
            var dice = new FixedDice(5, 5, 50); 
            var session = new GameSession(
                MakeProfile("Player"), MakeProfile("Opponent"),
                new NullLlmAdapter(), dice, new NullTrapRegistry());

            var start = await session.StartTurnAsync();
            Assert.Equal(InterestState.Interested, start.State.State);

            var result = await session.ResolveTurnAsync(0);

            Assert.Equal(InterestState.Lukewarm, result.StateAfter.State);
            
            Assert.NotNull(result.NarrativeBeat);
            Assert.Contains("Lukewarm", result.NarrativeBeat);
            Assert.StartsWith("***", result.NarrativeBeat);
            Assert.EndsWith("***", result.NarrativeBeat);
        }

        // What: Edge Case — If the interest roll does not result in a state change, TurnResult.NarrativeBeat must remain null
        // Mutation: Fails if NarrativeBeat is populated when the interest state has not changed (e.g. remains Interested)
        [Fact]
        public async Task ResolveTurnAsync_NoStateChange_NarrativeBeatIsNull()
        {
            var dice = new FixedDice(5, 14, 50); 
            var session = new GameSession(
                MakeProfile("Player"), MakeProfile("Opponent"),
                new NullLlmAdapter(), dice, new NullTrapRegistry());

            var start = await session.StartTurnAsync();
            Assert.Equal(InterestState.Interested, start.State.State);

            var result = await session.ResolveTurnAsync(0);

            Assert.Equal(InterestState.Interested, result.StateAfter.State);
            Assert.Equal(12, result.StateAfter.Interest); 
            
            Assert.Null(result.NarrativeBeat);
        }

        // What: Reviewer feedback — GetInterestChangeBeatAsync exhibits new null-return behavior
        // Mutation: Fails if GetInterestChangeBeatAsync returns anything other than null
        [Fact]
        public async Task NullLlmAdapter_GetInterestChangeBeatAsync_ReturnsNull()
        {
            var adapter = new NullLlmAdapter();
            var context = new InterestChangeContext("Opponent", 10, 5, InterestState.Lukewarm);
            var result = await adapter.GetInterestChangeBeatAsync(context);
            Assert.Null(result);
        }
    }
}
