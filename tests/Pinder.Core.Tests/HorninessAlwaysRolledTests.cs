using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Pinder.Core.Characters;
using Pinder.Core.Conversation;
using Pinder.Core.Interfaces;
using Pinder.Core.Stats;
using Xunit;

namespace Pinder.Core.Tests
{
    /// <summary>
    /// Tests for issue #269: Horniness must always be rolled (1d10) at session start,
    /// regardless of whether an IGameClock is provided.
    /// </summary>
    [Trait("Category", "Core")]
    public class HorninessAlwaysRolledTests
    {
        private static CharacterProfile MakeProfile(string name)
        {
            var stats = TestHelpers.MakeStatBlock();
            var timing = new TimingProfile(5, 0.0f, 0.0f, "neutral");
            return new CharacterProfile(stats, $"You are {name}.", name, timing, 1);
        }

        /// <summary>
        /// AC: Session without clock still has non-zero _sessionHorniness.
        /// Uses a capturing LLM adapter to verify the horniness level passed to the LLM.
        /// </summary>
        [Fact]
        public async Task NoClock_HorninessRolled_NonZero()
        {
            // Dice: 7 = horniness roll, 15 = d20 for turn, 50 = d100 timing
            var dice = new FixedDice(7, 15, 50);
            var capturingLlm = new CapturingLlmAdapter();

            var session = new GameSession(
                MakeProfile("P"), MakeProfile("O"),
                capturingLlm, dice, new NullTrapRegistry(), new GameSessionConfig(clock: TestHelpers.MakeClock()));

            await session.StartTurnAsync();

            // Verify horniness was passed to the LLM as 7 (not 0)
            Assert.Single(capturingLlm.Contexts);
            Assert.Equal(7, capturingLlm.Contexts[0].HorninessLevel);
        }

        /// <summary>
        /// AC: Horniness rolled (1d10) in every session, with or without clock.
        /// Time-of-day modifier applied only when clock is available.
        /// Without clock, modifier is 0, so horniness = dice roll.
        /// </summary>
        [Fact]
        public async Task NoClock_NoTimeOfDayModifier()
        {
            // Dice: 8 = horniness roll, 15 = d20, 50 = timing
            var dice = new FixedDice(8, 15, 50);
            var capturingLlm = new CapturingLlmAdapter();

            var session = new GameSession(
                MakeProfile("P"), MakeProfile("O"),
                capturingLlm, dice, new NullTrapRegistry(), new GameSessionConfig(clock: TestHelpers.MakeClock()));

            await session.StartTurnAsync();

            // Without clock, modifier is 0 -> horniness = dice roll = 8
            Assert.Equal(8, capturingLlm.Contexts[0].HorninessLevel);
        }

        /// <summary>
        /// AC: With clock, time-of-day modifier is applied.
        /// Dice 7 + modifier 3 = 10.
        /// </summary>
        [Fact]
        public async Task WithClock_TimeOfDayModifierApplied()
        {
            var dice = new FixedDice(7, 15, 50);
            var capturingLlm = new CapturingLlmAdapter();
            var clock = new TestClock(horninessModifier: 3);
            var config = new GameSessionConfig(clock: clock);

            var session = new GameSession(
                MakeProfile("P"), MakeProfile("O"),
                capturingLlm, dice, new NullTrapRegistry(), config);

            await session.StartTurnAsync();

            // 7 + 3 = 10
            Assert.Equal(10, capturingLlm.Contexts[0].HorninessLevel);
        }

        /// <summary>
        /// Horniness is clamped to minimum 0 (negative modifier can't make it negative).
        /// </summary>
        [Fact]
        public async Task WithClock_NegativeModifier_ClampedToZero()
        {
            // Dice: 1 (horniness roll), modifier -5 -> 1 + (-5) = -4 -> clamped to 0
            var dice = new FixedDice(1, 15, 50);
            var capturingLlm = new CapturingLlmAdapter();
            var clock = new TestClock(horninessModifier: -5);
            var config = new GameSessionConfig(clock: clock);

            var session = new GameSession(
                MakeProfile("P"), MakeProfile("O"),
                capturingLlm, dice, new NullTrapRegistry(), config);

            await session.StartTurnAsync();

            Assert.Equal(0, capturingLlm.Contexts[0].HorninessLevel);
        }

        /// <summary>
        /// Dice roll is consumed during constructor (not during StartTurnAsync).
        /// Verifying by checking the d20 roll value consumed by StartTurnAsync.
        /// </summary>
        [Fact]
        public async Task HorninessRoll_ConsumedAtConstruction()
        {
            // 3 = horniness roll at construction, 16 = d20 for turn, 50 = timing
            var dice = new FixedDice(3, 16, 50);
            var capturingLlm = new CapturingLlmAdapter();

            var session = new GameSession(
                MakeProfile("P"), MakeProfile("O"),
                capturingLlm, dice, new NullTrapRegistry(), new GameSessionConfig(clock: TestHelpers.MakeClock()));

            await session.StartTurnAsync();

            // Horniness = 3 (no clock, no modifier)
            Assert.Equal(3, capturingLlm.Contexts[0].HorninessLevel);

            // The d20=16 should have been consumed by ResolveTurnAsync
            var result = await session.ResolveTurnAsync(0);
            Assert.True(result.Roll.IsSuccess); // 16+2=18 >= DC 18
        }

        /// <summary>
        /// Capturing LLM adapter that records DialogueContexts.
        /// </summary>
        private sealed class CapturingLlmAdapter : ILlmAdapter
        {
            public List<DialogueContext> Contexts { get; } = new List<DialogueContext>();

            public Task<DialogueOption[]> GetDialogueOptionsAsync(DialogueContext context)
            {
                Contexts.Add(context);
                return Task.FromResult(new[]
                {
                    new DialogueOption(StatType.Charm, "Hey"),
                    new DialogueOption(StatType.Honesty, "Look"),
                    new DialogueOption(StatType.Wit, "Joke"),
                    new DialogueOption(StatType.Chaos, "Wild")
                });
            }

            public Task<string> DeliverMessageAsync(DeliveryContext context)
                => Task.FromResult(context.ChosenOption.IntendedText);

            public Task<OpponentResponse> GetOpponentResponseAsync(OpponentContext context)
                => Task.FromResult(new OpponentResponse("..."));

            public Task<string?> GetInterestChangeBeatAsync(InterestChangeContext context)
                => Task.FromResult<string?>(null);
            public System.Threading.Tasks.Task<string> ApplyHorninessOverlayAsync(string message, string instruction, string? opponentContext = null, string? archetypeDirective = null) => System.Threading.Tasks.Task.FromResult(message);
            public System.Threading.Tasks.Task<string> ApplyShadowCorruptionAsync(string message, string instruction, Pinder.Core.Stats.ShadowStatType shadow, string? archetypeDirective = null) => System.Threading.Tasks.Task.FromResult(message);
            public System.Threading.Tasks.Task<string> ApplyTrapOverlayAsync(string message, string trapInstruction, string trapName, string? opponentContext = null, string? archetypeDirective = null) => System.Threading.Tasks.Task.FromResult(message);
        }

                private sealed class TestClock : IGameClock
        {
            private readonly int _horninessModifier;

            public TestClock(int horninessModifier) => _horninessModifier = horninessModifier;

            public DateTimeOffset Now => DateTimeOffset.UtcNow;
            public void Advance(TimeSpan amount) { }
            public void AdvanceTo(DateTimeOffset target) { }
            public TimeOfDay GetTimeOfDay() => TimeOfDay.Afternoon;
            public int GetHorninessModifier() => _horninessModifier;
        }
    }
}
