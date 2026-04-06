using System;
using System.Reflection;
using System.Threading.Tasks;
using Xunit;
using Pinder.Core.Characters;
using Pinder.Core.Conversation;
using Pinder.Core.Interfaces;
using Pinder.Core.Traps;

namespace Pinder.Core.Tests.Conversation
{
    public class Issue536_RevertStatefulAdapterTests
    {
        private sealed class DummyLlmAdapter : ILlmAdapter
        {
            public Task<DialogueOption[]> GetDialogueOptionsAsync(DialogueContext context) => Task.FromResult(new DialogueOption[0]);
            public Task<string> DeliverMessageAsync(DeliveryContext context) => Task.FromResult("");
            public Task<OpponentResponse> GetOpponentResponseAsync(OpponentContext context) => Task.FromResult(new OpponentResponse("", null, null));
            public Task<string?> GetInterestChangeBeatAsync(InterestChangeContext context) => Task.FromResult<string?>("");
        }

        private sealed class DummyDice : IDiceRoller
        {
            public int Roll(int sides) => 10;
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

        // What: 1. Delete Interface: The file src/Pinder.Core/Interfaces/IStatefulLlmAdapter.cs is deleted.
        // Mutation: Fails if IStatefulLlmAdapter is recreated or still exists.
        [Fact]
        public void IStatefulLlmAdapter_ShouldNotExist()
        {
            var type = Type.GetType("Pinder.Core.Interfaces.IStatefulLlmAdapter, Pinder.Core");
            Assert.Null(type);
        }

        // What: 5. Revert GameSession Wiring: The GameSession constructor has the type check removed.
        // Mutation: Fails if GameSession constructor tries to do stateful casts and throws or fails.
        [Fact]
        public void GameSession_Constructor_ShouldSucceedWithStatelessAdapter()
        {
            var player = MakeProfile("P1");
            var opponent = MakeProfile("P2");
            
            // Should not throw or do anything with llmMock besides storing it.
            var session = new GameSession(player, opponent, new DummyLlmAdapter(), new DummyDice(), new NullTrapRegistry());
            
            Assert.NotNull(session);
        }
    }
}
