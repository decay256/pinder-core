using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Xunit;
using Pinder.Core.Characters;
using Pinder.Core.Conversation;
using Pinder.Core.Interfaces;
using Pinder.Core.Traps;

namespace Pinder.Core.Tests.Conversation
{
    /// <summary>
    /// Originally: locked the #536 stateful-adapter contract (StartOpponentSession on construction,
    /// HasOpponentSession query). Updated for #788: the engine now owns opponent conversation
    /// state, so the locked contract becomes:
    /// <list type="bullet">
    ///   <item><description><c>IStatefulLlmAdapter</c> exists and is implementable by stateless adapters.</description></item>
    ///   <item><description><c>GameSession</c> constructor does NOT call any "start opponent session" hook.</description></item>
    ///   <item><description><c>NullLlmAdapter</c> implements <c>IStatefulLlmAdapter</c> via the history-passing overload.</description></item>
    /// </list>
    /// </summary>
    [Trait("Category", "Core")]
    public class Issue536_StatefulAdapterTests
    {
        private sealed class DummyLlmAdapter : ILlmAdapter
        {
            public Task<DialogueOption[]> GetDialogueOptionsAsync(DialogueContext context) => Task.FromResult(new DialogueOption[0]);
            public Task<string> DeliverMessageAsync(DeliveryContext context) => Task.FromResult("");
            public Task<OpponentResponse> GetOpponentResponseAsync(OpponentContext context) => Task.FromResult(new OpponentResponse("", null, null));
            public Task<string?> GetInterestChangeBeatAsync(InterestChangeContext context) => Task.FromResult<string?>("");
            public Task<string> ApplyHorninessOverlayAsync(string message, string instruction, string? opponentContext = null, string? archetypeDirective = null) => Task.FromResult(message);
            public Task<string> ApplyShadowCorruptionAsync(string message, string instruction, Pinder.Core.Stats.ShadowStatType shadow, string? archetypeDirective = null) => Task.FromResult(message);
            public Task<string> ApplyTrapOverlayAsync(string message, string trapInstruction, string trapName, string? opponentContext = null, string? archetypeDirective = null) => Task.FromResult(message);
        }

        private sealed class StatefulDummyLlmAdapter : IStatefulLlmAdapter
        {
            // #788: track that the engine routed through the stateful overload, not just StateLESS one.
            public int StatefulCallCount { get; private set; }
            public IReadOnlyList<ConversationMessage>? LastHistorySeen { get; private set; }

            public Task<DialogueOption[]> GetDialogueOptionsAsync(DialogueContext context) => Task.FromResult(new DialogueOption[0]);
            public Task<string> DeliverMessageAsync(DeliveryContext context) => Task.FromResult("");
            public Task<OpponentResponse> GetOpponentResponseAsync(OpponentContext context) => Task.FromResult(new OpponentResponse("", null, null));

            public Task<StatefulOpponentResult> GetOpponentResponseAsync(
                OpponentContext context,
                IReadOnlyList<ConversationMessage> history,
                System.Threading.CancellationToken ct = default)
            {
                StatefulCallCount++;
                LastHistorySeen = history;
                return Task.FromResult(new StatefulOpponentResult(
                    new OpponentResponse("...", null, null),
                    new ConversationMessage[]
                    {
                        ConversationMessage.User("u"),
                        ConversationMessage.Assistant("a"),
                    }));
            }

            public Task<string?> GetInterestChangeBeatAsync(InterestChangeContext context) => Task.FromResult<string?>("");
            public Task<string> GetSteeringQuestionAsync(SteeringContext context) => Task.FromResult("test steering question");
            public Task<string> ApplyHorninessOverlayAsync(string message, string instruction, string? opponentContext = null, string? archetypeDirective = null) => Task.FromResult(message);
            public Task<string> ApplyShadowCorruptionAsync(string message, string instruction, Pinder.Core.Stats.ShadowStatType shadow, string? archetypeDirective = null) => Task.FromResult(message);
            public Task<string> ApplyTrapOverlayAsync(string message, string trapInstruction, string trapName, string? opponentContext = null, string? archetypeDirective = null) => Task.FromResult(message);
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

            // Plain ILlmAdapter — no stateful cast, should not throw.
            var session = new GameSession(player, opponent, new DummyLlmAdapter(), new DummyDice(), new NullTrapRegistry(), new GameSessionConfig(clock: TestHelpers.MakeClock()));

            Assert.NotNull(session);
        }

        // #788 contract: GameSession.ctor MUST NOT call any session-start hook on the
        // adapter. The opponent history is owned by the engine and starts empty.
        [Fact]
        public void GameSession_Constructor_DoesNotInvokeAdapterOnConstruction()
        {
            var player = MakeProfile("P1");
            var opponent = MakeProfile("P2");
            var statefulAdapter = new StatefulDummyLlmAdapter();

            var session = new GameSession(player, opponent, statefulAdapter, new DummyDice(), new NullTrapRegistry(), new GameSessionConfig(clock: TestHelpers.MakeClock()));

            Assert.Equal(0, statefulAdapter.StatefulCallCount);
            Assert.NotNull(session);
            // Engine-owned history starts empty — locks the new ownership boundary.
            Assert.Empty(session.OpponentHistory);
        }

        [Fact]
        public void NullLlmAdapter_ImplementsIStatefulLlmAdapter()
        {
            var adapter = new NullLlmAdapter();
            Assert.True(adapter is IStatefulLlmAdapter);
        }

        // #788 sanity: the old StartOpponentSession / HasOpponentSession surface MUST be
        // gone from the interface. If a regression re-introduces them, this test catches it.
        [Fact]
        public void IStatefulLlmAdapter_HasNoStartOpponentSessionOrHasOpponentSession()
        {
            var iface = typeof(IStatefulLlmAdapter);
            Assert.Null(iface.GetMethod("StartOpponentSession"));
            Assert.Null(iface.GetProperty("HasOpponentSession"));
        }
    }
}
