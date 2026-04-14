using System;
using System.Collections.Generic;
using System.Linq;
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
    /// Tests for issue #272: Denial +1 when player skips available Honesty option (§7).
    /// Maturity: Prototype — happy-path per AC.
    /// </summary>
    [Trait("Category", "Core")]
    public class DenialSkipHonestyTests
    {
        // AC1: Denial +1 when Honesty option available and player chose different stat
        [Fact]
        public async Task SkippingHonesty_GrowsDenialByOne()
        {
            var shadows = MakeTracker();
            var options = new[]
            {
                new DialogueOption(StatType.Charm, "smooth line"),
                new DialogueOption(StatType.Honesty, "truth bomb"),
                new DialogueOption(StatType.Wit, "clever quip")
            };
            var session = BuildSession(dice: Dice(15, 50), shadows: shadows, options: options);

            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0); // picks Charm, skipping Honesty

            Assert.Equal(1, shadows.GetDelta(ShadowStatType.Denial));
        }

        // AC2: No Denial growth when no Honesty option was in the lineup
        [Fact]
        public async Task NoHonestyInLineup_NoDenialGrowth()
        {
            var shadows = MakeTracker();
            var options = new[]
            {
                new DialogueOption(StatType.Charm, "smooth line"),
                new DialogueOption(StatType.Wit, "clever quip"),
                new DialogueOption(StatType.Rizz, "rizz move")
            };
            var session = BuildSession(dice: Dice(15, 50), shadows: shadows, options: options);

            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0); // picks Charm, no Honesty available

            Assert.Equal(0, shadows.GetDelta(ShadowStatType.Denial));
        }

        // AC3: No Denial growth when player chose Honesty
        [Fact]
        public async Task ChoosingHonesty_NoDenialGrowth()
        {
            var shadows = MakeTracker();
            var options = new[]
            {
                new DialogueOption(StatType.Charm, "smooth line"),
                new DialogueOption(StatType.Honesty, "truth bomb"),
                new DialogueOption(StatType.Wit, "clever quip")
            };
            var session = BuildSession(dice: Dice(15, 50), shadows: shadows, options: options);

            await session.StartTurnAsync();
            await session.ResolveTurnAsync(1); // picks Honesty

            Assert.Equal(0, shadows.GetDelta(ShadowStatType.Denial));
        }

        // Edge: Denial grows each turn Honesty is skipped (per turn)
        [Fact]
        public async Task SkippingHonestyTwice_GrowsDenialByTwo()
        {
            var shadows = MakeTracker();
            var options = new[]
            {
                new DialogueOption(StatType.Charm, "smooth line"),
                new DialogueOption(StatType.Honesty, "truth bomb"),
                new DialogueOption(StatType.Wit, "clever quip")
            };
            var session = BuildSession(
                dice: Dice(15, 50, 15, 50),
                shadows: shadows,
                options: options);

            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0); // skip Honesty turn 1

            await session.StartTurnAsync();
            await session.ResolveTurnAsync(2); // skip Honesty turn 2 (pick Wit)

            Assert.Equal(2, shadows.GetDelta(ShadowStatType.Denial));
        }

        // Edge: No shadows tracker → no crash
        [Fact]
        public async Task NoShadowTracker_NoCrash()
        {
            var options = new[]
            {
                new DialogueOption(StatType.Charm, "smooth line"),
                new DialogueOption(StatType.Honesty, "truth bomb")
            };
            var session = BuildSession(dice: Dice(15, 50), shadows: null, options: options);

            await session.StartTurnAsync();
            // Should not throw
            await session.ResolveTurnAsync(0);
        }

        // =====================================================================
        // Helpers
        // =====================================================================

        private static SessionShadowTracker MakeTracker()
            => new SessionShadowTracker(MakeStats());

        private static StatBlock MakeStats(
            int charm = 3, int rizz = 2, int honesty = 1,
            int chaos = 0, int wit = 4, int sa = 2)
        {
            return new StatBlock(
                new Dictionary<StatType, int>
                {
                    { StatType.Charm, charm }, { StatType.Rizz, rizz },
                    { StatType.Honesty, honesty }, { StatType.Chaos, chaos },
                    { StatType.Wit, wit }, { StatType.SelfAwareness, sa }
                },
                new Dictionary<ShadowStatType, int>
                {
                    { ShadowStatType.Madness, 0 }, { ShadowStatType.Despair, 0 },
                    { ShadowStatType.Denial, 0 }, { ShadowStatType.Fixation, 0 },
                    { ShadowStatType.Dread, 0 }, { ShadowStatType.Overthinking, 0 }
                });
        }

        private static CharacterProfile MakeProfile(string name, StatBlock? stats = null)
            => new CharacterProfile(
                stats ?? MakeStats(),
                "system prompt",
                name,
                new TimingProfile(5, 1.0f, 0.0f, "neutral"),
                1);

        private static TestDice Dice(params int[] values) => new TestDice(values);

        private static GameSession BuildSession(
            TestDice? dice = null,
            SessionShadowTracker? shadows = null,
            DialogueOption[]? options = null)
        {
            var d = dice ?? Dice(15, 50);
            ILlmAdapter llm = options != null
                ? (ILlmAdapter)new StubLlmAdapter(options)
                : new NullLlmAdapter();

            var config = new GameSessionConfig(clock: TestHelpers.MakeClock(), playerShadows: shadows);

            // PrependedDice: first roll is ghost check (need non-ghost value)
            var wrappedDice = new PrependedDice(5, d);

            return new GameSession(
                MakeProfile("player"),
                MakeProfile("opponent"),
                llm,
                wrappedDice,
                new NullTrapRegistry(),
                config);
        }

        private sealed class PrependedDice : IDiceRoller
        {
            private int? _first;
            private readonly IDiceRoller _inner;
            public PrependedDice(int firstValue, IDiceRoller inner)
            {
                _first = firstValue;
                _inner = inner;
            }
            public int Roll(int sides)
            {
                if (_first.HasValue) { var v = _first.Value; _first = null; return v; }
                return _inner.Roll(sides);
            }
        }

        private sealed class TestDice : IDiceRoller
        {
            private readonly Queue<int> _values;
            public TestDice(int[] values) => _values = new Queue<int>(values);
            public int Roll(int sides)
            {
                if (_values.Count == 0)
                    throw new InvalidOperationException("TestDice ran out of values");
                return _values.Dequeue();
            }
        }

        private sealed class StubLlmAdapter : ILlmAdapter
        {
            private readonly DialogueOption[] _options;
            public StubLlmAdapter(DialogueOption[] options) => _options = options;
            public Task<DialogueOption[]> GetDialogueOptionsAsync(DialogueContext context)
                => Task.FromResult(_options);
            public Task<string> DeliverMessageAsync(DeliveryContext context)
                => Task.FromResult(context.ChosenOption.IntendedText);
            public Task<OpponentResponse> GetOpponentResponseAsync(OpponentContext context)
                => Task.FromResult(new OpponentResponse("..."));
            public Task<string?> GetInterestChangeBeatAsync(InterestChangeContext context)
                => Task.FromResult<string?>(null);
            public System.Threading.Tasks.Task<string> ApplyHorninessOverlayAsync(string message, string instruction, string? opponentContext = null) => System.Threading.Tasks.Task.FromResult(message);
            public System.Threading.Tasks.Task<string> ApplyShadowCorruptionAsync(string message, string instruction, Pinder.Core.Stats.ShadowStatType shadow) => System.Threading.Tasks.Task.FromResult(message);
        }
    }
}
