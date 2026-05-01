using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Pinder.Core.Characters;
using Pinder.Core.Conversation;
using Pinder.Core.Interfaces;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;
using Xunit;

namespace Pinder.Core.Tests
{
    [Trait("Category", "Core")]
    public class GameSessionConfigTests
    {
        [Fact]
        public void Config_AllNull_AllPropertiesNull()
        {
            var config = new GameSessionConfig();
            Assert.Null(config.Clock);
            Assert.Null(config.PlayerShadows);
            Assert.Null(config.OpponentShadows);
            Assert.Null(config.StartingInterest);
            Assert.Null(config.PreviousOpener);
        }

        [Fact]
        public void Config_AllSet_PropertiesPreserved()
        {
            var stats = MakeStatBlock();
            var playerShadows = new SessionShadowTracker(stats);
            var opponentShadows = new SessionShadowTracker(stats);
            var clock = new TestClock();

            var config = new GameSessionConfig(
                clock: clock,
                playerShadows: playerShadows,
                opponentShadows: opponentShadows,
                startingInterest: 5,
                previousOpener: "hey there");

            Assert.Same(clock, config.Clock);
            Assert.Same(playerShadows, config.PlayerShadows);
            Assert.Same(opponentShadows, config.OpponentShadows);
            Assert.Equal(5, config.StartingInterest);
            Assert.Equal("hey there", config.PreviousOpener);
        }

        [Fact]
        public void GameSession_NullConfig_ThrowsArgumentNullException()
        {
            // Config is required — null config must throw before any field access
            Assert.Throws<ArgumentNullException>(() => new GameSession(
                MakeProfile("player"),
                MakeProfile("opponent"),
                new StubLlmAdapter(),
                new StubDice(),
                new StubTrapRegistry(),
                null));
        }

        [Fact]
        public void GameSession_EmptyConfig_NoClock_ThrowsInvalidOperationException()
        {
            // Clock is required — GameSessionConfig without clock → must throw
            Assert.Throws<InvalidOperationException>(() => new GameSession(
                MakeProfile("player"),
                MakeProfile("opponent"),
                new StubLlmAdapter(),
                new StubDice(),
                new StubTrapRegistry(),
                new GameSessionConfig()));
        }

        [Fact]
        public void GameSession_WithoutClock_ThrowsWithCorrectMessage()
        {
            // Verify the exact exception message
            var ex = Assert.Throws<InvalidOperationException>(() => new GameSession(
                MakeProfile("player"),
                MakeProfile("opponent"),
                new StubLlmAdapter(),
                new StubDice(),
                new StubTrapRegistry(),
                new GameSessionConfig()));
            Assert.Contains("GameClock is required", ex.Message);
        }

        [Fact]
        public async Task GameSession_StartingInterest_IsApplied()
        {
            // Create session with starting interest = 20 (VeryIntoIt → grants advantage)
            var llm = new StubLlmAdapter();
            var session = new GameSession(
                MakeProfile("player"),
                MakeProfile("opponent"),
                llm,
                new StubDice(10),
                new StubTrapRegistry(),
                new GameSessionConfig(clock: new TestClock(), startingInterest: 20));

            // StartTurnAsync should work without throwing — if interest were 0 it would throw Unmatched
            var turn = await session.StartTurnAsync();
            Assert.NotNull(turn);
            // The snapshot should show interest = 20
            Assert.Equal(20, turn.State.Interest);
        }

        // ======================== Helpers ========================

        private static StatBlock MakeStatBlock()
        {
            return new StatBlock(
                new Dictionary<StatType, int>
                {
                    { StatType.Charm, 3 }, { StatType.Rizz, 2 }, { StatType.Honesty, 1 },
                    { StatType.Chaos, 0 }, { StatType.Wit, 4 }, { StatType.SelfAwareness, 2 }
                },
                new Dictionary<ShadowStatType, int>
                {
                    { ShadowStatType.Madness, 0 }, { ShadowStatType.Despair, 0 },
                    { ShadowStatType.Denial, 0 }, { ShadowStatType.Fixation, 0 },
                    { ShadowStatType.Dread, 0 }, { ShadowStatType.Overthinking, 0 }
                });
        }

        private static CharacterProfile MakeProfile(string name)
        {
            var stats = MakeStatBlock();
            var timing = new TimingProfile(5, 1.0f, 0.0f, "neutral");
            return new CharacterProfile(stats, "system prompt", name, timing, 1);
        }

        private sealed class TestClock : IGameClock
        {
            public DateTimeOffset Now => DateTimeOffset.UtcNow;
            public void Advance(TimeSpan amount) { }
            public void AdvanceTo(DateTimeOffset target) { }
            public TimeOfDay GetTimeOfDay() => TimeOfDay.Morning;
            public int GetHorninessModifier() => -2;
        }

        private sealed class StubDice : IDiceRoller
        {
            private readonly int _value;
            public StubDice(int value = 10) => _value = value;
            public int Roll(int sides) => _value;
        }

        private sealed class StubTrapRegistry : ITrapRegistry
        {
            public Traps.TrapDefinition? GetTrap(StatType stat) => null;
            public string? GetLlmInstruction(StatType stat) => null;
        }

        private sealed class StubLlmAdapter : ILlmAdapter
        {
            public Task<DialogueOption[]> GetDialogueOptionsAsync(DialogueContext context, System.Threading.CancellationToken ct = default)
            {
                return Task.FromResult(new[]
                {
                    new DialogueOption(StatType.Charm, "Test option")
                });
            }

            public Task<string> DeliverMessageAsync(DeliveryContext context, System.Threading.CancellationToken ct = default)
                => Task.FromResult("delivered message");

            public Task<OpponentResponse> GetOpponentResponseAsync(OpponentContext context, System.Threading.CancellationToken ct = default)
                => Task.FromResult(new OpponentResponse("opponent reply"));

            public Task<string?> GetInterestChangeBeatAsync(InterestChangeContext context, System.Threading.CancellationToken ct = default)
                => Task.FromResult<string?>(null);
            public System.Threading.Tasks.Task<string> ApplyHorninessOverlayAsync(string message, string instruction, string? opponentContext = null, string? archetypeDirective = null, System.Threading.CancellationToken ct = default) => System.Threading.Tasks.Task.FromResult(message);
            public System.Threading.Tasks.Task<string> ApplyShadowCorruptionAsync(string message, string instruction, Pinder.Core.Stats.ShadowStatType shadow, string? archetypeDirective = null, System.Threading.CancellationToken ct = default) => System.Threading.Tasks.Task.FromResult(message);
            public System.Threading.Tasks.Task<string> ApplyTrapOverlayAsync(string message, string trapInstruction, string trapName, string? opponentContext = null, string? archetypeDirective = null, System.Threading.CancellationToken ct = default) => System.Threading.Tasks.Task.FromResult(message);
        }
    }
}
