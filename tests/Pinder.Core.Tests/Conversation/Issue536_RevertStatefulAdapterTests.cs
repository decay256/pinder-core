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
    [Trait("Category", "Core")]
    public class Issue536_StatefulAdapterTests
    {
        private sealed class DummyLlmAdapter : ILlmAdapter
        {
            public Task<DialogueOption[]> GetDialogueOptionsAsync(DialogueContext context) => Task.FromResult(new DialogueOption[0]);
            public Task<string> DeliverMessageAsync(DeliveryContext context) => Task.FromResult("");
            public Task<OpponentResponse> GetOpponentResponseAsync(OpponentContext context) => Task.FromResult(new OpponentResponse("", null, null));
            public Task<string?> GetInterestChangeBeatAsync(InterestChangeContext context) => Task.FromResult<string?>("");
            public System.Threading.Tasks.Task<string> ApplyHorninessOverlayAsync(string message, string instruction, string? opponentContext = null) => System.Threading.Tasks.Task.FromResult(message);
        }

                private sealed class StatefulDummyLlmAdapter : IStatefulLlmAdapter
        {
            public bool StartCalled { get; private set; }
            public string? ReceivedPrompt { get; private set; }

            public void StartOpponentSession(string opponentSystemPrompt)
            {
                StartCalled = true;
                ReceivedPrompt = opponentSystemPrompt;
            }

            public bool HasOpponentSession => StartCalled;

            public Task<DialogueOption[]> GetDialogueOptionsAsync(DialogueContext context) => Task.FromResult(new DialogueOption[0]);
            public Task<string> DeliverMessageAsync(DeliveryContext context) => Task.FromResult("");
            public Task<OpponentResponse> GetOpponentResponseAsync(OpponentContext context) => Task.FromResult(new OpponentResponse("", null, null));
            public Task<string?> GetInterestChangeBeatAsync(InterestChangeContext context) => Task.FromResult<string?>("");
            public Task<string> GetSteeringQuestionAsync(SteeringContext context) => Task.FromResult("test steering question");
            public System.Threading.Tasks.Task<string> ApplyHorninessOverlayAsync(string message, string instruction, string? opponentContext = null) => System.Threading.Tasks.Task.FromResult(message);
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

        [Fact]
        public void IStatefulLlmAdapter_ShouldExist()
        {
            var type = Type.GetType("Pinder.Core.Interfaces.IStatefulLlmAdapter, Pinder.Core");
            Assert.NotNull(type);
        }

        [Fact]
        public void GameSession_Constructor_ShouldSucceedWithStatelessAdapter()
        {
            var player = MakeProfile("P1");
            var opponent = MakeProfile("P2");

            // Plain ILlmAdapter — no stateful cast, should not throw
            var session = new GameSession(player, opponent, new DummyLlmAdapter(), new DummyDice(), new NullTrapRegistry(), new GameSessionConfig(clock: TestHelpers.MakeClock()));

            Assert.NotNull(session);
        }

        [Fact]
        public void GameSession_Constructor_WiresStatefulSession()
        {
            var player = MakeProfile("P1");
            var opponent = MakeProfile("P2");
            var statefulAdapter = new StatefulDummyLlmAdapter();

            var session = new GameSession(player, opponent, statefulAdapter, new DummyDice(), new NullTrapRegistry(), new GameSessionConfig(clock: TestHelpers.MakeClock()));

            Assert.True(statefulAdapter.StartCalled, "GameSession should call StartOpponentSession on construction");
            Assert.True(statefulAdapter.HasOpponentSession);
            Assert.Equal("You are P2.", statefulAdapter.ReceivedPrompt);
        }

        [Fact]
        public void NullLlmAdapter_ImplementsIStatefulLlmAdapter()
        {
            var adapter = new NullLlmAdapter();
            Assert.True(adapter is IStatefulLlmAdapter);

            var stateful = (IStatefulLlmAdapter)adapter;
            // Should not throw
            stateful.StartOpponentSession("test prompt");
            // NullLlmAdapter always reports no session
            Assert.False(stateful.HasOpponentSession);
        }
    }
}
