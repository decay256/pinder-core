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
    /// Spec-driven tests for issue #272: Denial +1 when player skips available Honesty option (§7).
    /// These tests verify behavioral acceptance criteria from docs/specs/issue-272-spec.md.
    /// </summary>
    [Trait("Category", "Core")]
    public class DenialSkipHonestySpecTests
    {
        // =====================================================================
        // AC1: Denial +1 when Honesty available and player chose different stat
        // =====================================================================

        // Mutation: would catch if the Denial growth call is removed entirely
        [Fact]
        public async Task AC1_SkipHonesty_DenialDeltaIncrements()
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

        // Mutation: would catch if growth event is not added to TurnResult.ShadowGrowthEvents
        [Fact]
        public async Task AC1_SkipHonesty_GrowthEventAppearsInTurnResult()
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
            var result = await session.ResolveTurnAsync(0);

            // The growth event from ApplyGrowth should appear in ShadowGrowthEvents
            Assert.NotNull(result.ShadowGrowthEvents);
            Assert.Contains(result.ShadowGrowthEvents, e =>
                e.IndexOf("Denial", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        // =====================================================================
        // AC2: No Denial growth when no Honesty option was in the lineup
        // =====================================================================

        // Mutation: would catch if the check for Honesty availability is missing
        // (i.e., Denial always grows when picking non-Honesty regardless of lineup)
        [Fact]
        public async Task AC2_NoHonestyInLineup_DenialUnchanged()
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
            await session.ResolveTurnAsync(0);

            Assert.Equal(0, shadows.GetDelta(ShadowStatType.Denial));
        }

        // =====================================================================
        // AC3: No Denial growth when player chose Honesty
        // =====================================================================

        // Mutation: would catch if the check is "Honesty in lineup" without
        // verifying the chosen option is non-Honesty
        [Fact]
        public async Task AC3_ChooseHonesty_DenialUnchanged()
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

        // =====================================================================
        // Edge: Multiple Honesty options → still only +1
        // =====================================================================

        // Mutation: would catch if implementation iterates all Honesty options
        // and applies +1 per Honesty option found (N instead of 1)
        [Fact]
        public async Task Edge_MultipleHonestyOptions_StillOnlyPlusOne()
        {
            var shadows = MakeTracker();
            var options = new[]
            {
                new DialogueOption(StatType.Honesty, "truth bomb A"),
                new DialogueOption(StatType.Honesty, "truth bomb B"),
                new DialogueOption(StatType.Charm, "smooth line")
            };
            var session = BuildSession(dice: Dice(15, 50), shadows: shadows, options: options);

            await session.StartTurnAsync();
            await session.ResolveTurnAsync(2); // picks Charm, skipping both Honesty options

            Assert.Equal(1, shadows.GetDelta(ShadowStatType.Denial));
        }

        // =====================================================================
        // Edge: Repeated turns accumulate Denial
        // =====================================================================

        // Mutation: would catch if Denial growth is applied only on the first turn
        // or if the growth resets between turns
        [Fact]
        public async Task Edge_ThreeSkippedTurns_DenialGrowsToThree()
        {
            var shadows = MakeTracker();
            var options = new[]
            {
                new DialogueOption(StatType.Charm, "smooth line"),
                new DialogueOption(StatType.Honesty, "truth bomb"),
                new DialogueOption(StatType.Wit, "clever quip")
            };
            var session = BuildSession(
                dice: Dice(15, 50, 15, 50, 15, 15, 50),
                shadows: shadows,
                options: options);

            for (int i = 0; i < 3; i++)
            {
                await session.StartTurnAsync();
                await session.ResolveTurnAsync(0); // skip Honesty each turn
            }

            Assert.Equal(3, shadows.GetDelta(ShadowStatType.Denial));
        }

        // =====================================================================
        // Edge: Null shadow tracker — no crash
        // =====================================================================

        // Mutation: would catch if null guard on _playerShadows is missing
        [Fact]
        public async Task Edge_NullShadowTracker_DoesNotThrow()
        {
            var options = new[]
            {
                new DialogueOption(StatType.Charm, "smooth line"),
                new DialogueOption(StatType.Honesty, "truth bomb")
            };
            var session = BuildSession(dice: Dice(15, 50), shadows: null, options: options);

            await session.StartTurnAsync();
            var ex = await Record.ExceptionAsync(() => session.ResolveTurnAsync(0));

            Assert.Null(ex);
        }

        // =====================================================================
        // Edge: Only other shadows unaffected
        // =====================================================================

        // Mutation: would catch if the wrong ShadowStatType is used
        // (e.g., Fixation instead of Denial)
        [Fact]
        public async Task Edge_SkipHonesty_OnlyDenialGrows_OtherShadowsUnchanged()
        {
            var shadows = MakeTracker();
            var options = new[]
            {
                new DialogueOption(StatType.Charm, "smooth line"),
                new DialogueOption(StatType.Honesty, "truth bomb"),
            };
            var session = BuildSession(dice: Dice(15, 50), shadows: shadows, options: options);

            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);

            // Denial should grow
            Assert.Equal(1, shadows.GetDelta(ShadowStatType.Denial));
            // Other shadows should NOT grow from this trigger specifically
            // Note: other shadow growth may occur from roll outcomes (§7),
            // so we check Fixation which is unrelated to Charm rolls
            Assert.Equal(0, shadows.GetDelta(ShadowStatType.Fixation));
        }

        // =====================================================================
        // Helpers (test-only utilities — not copied from implementation)
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

            // First dice roll is ghost check — need non-ghost value (not 1 on d4)
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
            public System.Threading.Tasks.Task<string> ApplyHorninessOverlayAsync(string message, string instruction, string? opponentContext = null, string? archetypeDirective = null) => System.Threading.Tasks.Task.FromResult(message);
            public System.Threading.Tasks.Task<string> ApplyShadowCorruptionAsync(string message, string instruction, Pinder.Core.Stats.ShadowStatType shadow, string? archetypeDirective = null) => System.Threading.Tasks.Task.FromResult(message);
        }
    }
}
