using System.Collections.Generic;
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
    /// <summary>
    /// Issue #241 AC3: Verify GameSession populates PlayerName, OpponentName, CurrentTurn
    /// on DeliveryContext and OpponentContext when calling ILlmAdapter methods.
    /// </summary>
    [Trait("Category", "Core")]
    public class Issue241_GameSessionDeliveryContextTests
    {
        [Fact]
        public async Task ResolveTurnAsync_DeliveryContext_has_player_and_opponent_names()
        {
            var llm = new CapturingLlm();
            var dice = new FixedDice(15, 5); // d20=15 (success), timing=5
            var session = new GameSession(
                MakeProfile("Sable"), MakeProfile("Brick"),
                llm, dice, new NullTrapRegistry(), new GameSessionConfig(clock: TestHelpers.MakeClock()));

            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);

            Assert.NotNull(llm.CapturedDeliveryContext);
            Assert.Equal("Sable", llm.CapturedDeliveryContext!.PlayerName);
            Assert.Equal("Brick", llm.CapturedDeliveryContext.OpponentName);
        }

        [Fact]
        public async Task ResolveTurnAsync_OpponentContext_has_player_and_opponent_names()
        {
            var llm = new CapturingLlm();
            var dice = new FixedDice(15, 5);
            var session = new GameSession(
                MakeProfile("Sable"), MakeProfile("Brick"),
                llm, dice, new NullTrapRegistry(), new GameSessionConfig(clock: TestHelpers.MakeClock()));

            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);

            Assert.NotNull(llm.CapturedOpponentContext);
            Assert.Equal("Sable", llm.CapturedOpponentContext!.PlayerName);
            Assert.Equal("Brick", llm.CapturedOpponentContext.OpponentName);
        }

        [Fact]
        public async Task ResolveTurnAsync_DeliveryContext_has_nonzero_turn_on_second_turn()
        {
            var llm = new CapturingLlm();
            // Two turns: d20=15, timing=5 each
            var dice = new FixedDice(15, 5, 15, 5);
            var session = new GameSession(
                MakeProfile("Sable"), MakeProfile("Brick"),
                llm, dice, new NullTrapRegistry(), new GameSessionConfig(clock: TestHelpers.MakeClock()));

            // Turn 1
            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);

            // Turn 2
            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);

            Assert.NotNull(llm.CapturedDeliveryContext);
            Assert.True(llm.CapturedDeliveryContext!.CurrentTurn > 0,
                "CurrentTurn should be non-zero on second turn");
        }

        private static CharacterProfile MakeProfile(string name, int allStats = 2)
        {
            return new CharacterProfile(
                stats: TestHelpers.MakeStatBlock(allStats),
                assembledSystemPrompt: $"You are {name}.",
                displayName: name,
                timing: new TimingProfile(5, 0.0f, 0.0f, "neutral"),
                level: 1);
        }

        /// <summary>
        /// LLM adapter that captures the DeliveryContext and OpponentContext for assertion.
        /// </summary>
        private sealed class CapturingLlm : ILlmAdapter
        {
            public DeliveryContext? CapturedDeliveryContext { get; private set; }
            public OpponentContext? CapturedOpponentContext { get; private set; }

            public Task<DialogueOption[]> GetDialogueOptionsAsync(DialogueContext context, System.Threading.CancellationToken ct = default)
            {
                return Task.FromResult(new[]
                {
                    new DialogueOption(StatType.Charm, "Hey there"),
                    new DialogueOption(StatType.Rizz, "Nice"),
                    new DialogueOption(StatType.Honesty, "Real talk"),
                    new DialogueOption(StatType.Wit, "Clever")
                });
            }

            public Task<string> DeliverMessageAsync(DeliveryContext context, System.Threading.CancellationToken ct = default)
            {
                CapturedDeliveryContext = context;
                return Task.FromResult(context.ChosenOption.IntendedText);
            }

            public Task<OpponentResponse> GetOpponentResponseAsync(OpponentContext context, System.Threading.CancellationToken ct = default)
            {
                CapturedOpponentContext = context;
                return Task.FromResult(new OpponentResponse("Reply from opponent"));
            }

            public Task<string?> GetInterestChangeBeatAsync(InterestChangeContext context, System.Threading.CancellationToken ct = default)
                => Task.FromResult<string?>(null);
            public System.Threading.Tasks.Task<string> ApplyHorninessOverlayAsync(string message, string instruction, string? opponentContext = null, string? archetypeDirective = null, System.Threading.CancellationToken ct = default) => System.Threading.Tasks.Task.FromResult(message);
            public System.Threading.Tasks.Task<string> ApplyShadowCorruptionAsync(string message, string instruction, Pinder.Core.Stats.ShadowStatType shadow, string? archetypeDirective = null, System.Threading.CancellationToken ct = default) => System.Threading.Tasks.Task.FromResult(message);
            public System.Threading.Tasks.Task<string> ApplyTrapOverlayAsync(string message, string trapInstruction, string trapName, string? opponentContext = null, string? archetypeDirective = null, System.Threading.CancellationToken ct = default) => System.Threading.Tasks.Task.FromResult(message);
        }

                private sealed class FixedDice : IDiceRoller
        {
            private readonly Queue<int> _rolls = new Queue<int>();

            public FixedDice(params int[] rolls)
            {
                foreach (var r in rolls)
                    _rolls.Enqueue(r);
            }

            public int Roll(int sides)
            {
                return _rolls.Count > 0 ? _rolls.Dequeue() : 10;
            }
        }

        private sealed class NullTrapRegistry : ITrapRegistry
        {
            public TrapDefinition? GetTrap(StatType stat) => null;
            public string? GetLlmInstruction(StatType stat) => null;
        }
    }
}
