using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Pinder.Core.Characters;
using Pinder.Core.Conversation;
using Pinder.Core.Interfaces;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;
using Pinder.Core.Traps;
using Xunit;

namespace Pinder.Core.Tests.Conversation
{
    /// <summary>
    /// Issue #593: <see cref="TurnStart.WeaknessDcReduction"/> exposes the active
    /// weakness window's DC reduction so the frontend can render the magnitude on
    /// the FoldableHintBanner without re-implementing active-window state.
    /// </summary>
    [Trait("Category", "Core")]
    public class Issue593_WeaknessDcReductionOnTurnStartTests
    {
        // ── Helpers ──────────────────────────────────────────────────────────

        private static CharacterProfile MakeProfile(string name, StatBlock? stats = null)
        {
            stats ??= TestHelpers.MakeStatBlock(2);
            return TestHelpers.MakeCharacterProfile(
                stats: stats,
                assembledSystemPrompt: $"You are {name}.",
                displayName: name,
                timing: new TimingProfile(5, 0.0f, 0.0f, "neutral"),
                level: 1);
        }

        private static GameSession MakeSession(ILlmAdapter? llm = null)
        {
            return new GameSession(
                MakeProfile("Player"),
                MakeProfile("Datee"),
                llm ?? new NullLlmAdapter(),
                new FixedDice593(5, 5),
                new NullTrapRegistry593(),
                new GameSessionConfig(clock: TestHelpers.MakeClock(), startingInterest: 10));
        }

        // ── AC1: No active window → WeaknessDcReduction is null ──────────────

        [Fact]
        public async Task StartTurnAsync_NoActiveWeakness_WeaknessDcReductionIsNull()
        {
            var session = MakeSession();
            var turnStart = await session.StartTurnAsync();

            Assert.Null(turnStart.WeaknessDcReduction);
        }

        // ── AC2: Active window → WeaknessDcReduction equals window's DcReduction ─

        [Fact]
        public async Task StartTurnAsync_ActiveWeakness_WeaknessDcReductionMatchesWindow()
        {
            // Inject a weakness window via a custom LLM adapter that returns one.
            const int expectedDcReduction = 3;
            var window = new WeaknessWindow(StatType.SelfAwareness, expectedDcReduction);
            var llm = new WindowInjectingLlmAdapter593(window);
            var session = MakeSession(llm);

            // Turn 1: LLM returns a weakness window in the datee response.
            // The window becomes active for the NEXT turn's StartTurnAsync.
            // We must call ResolveTurnAsync (turn 1) then StartTurnAsync again (turn 2).
            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);

            // Turn 2: window injected by the LLM on turn 1 is now _activeWeakness.
            var turn2Start = await session.StartTurnAsync();

            Assert.NotNull(turn2Start.WeaknessDcReduction);
            Assert.Equal(expectedDcReduction * 2, turn2Start.WeaknessDcReduction!.Value);
        }

        // ── AC3: Window consumed after resolve → next turn has null ──────────

        [Fact]
        public async Task AfterWindowConsumed_NextTurnWeaknessDcReductionIsNull()
        {
            const int dcReduction = 2;
            var window = new WeaknessWindow(StatType.Charm, dcReduction);
            var llm = new WindowInjectingLlmAdapter593(window);
            var session = MakeSession(llm);

            // Turn 1 ends, window set as active.
            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);

            // Turn 2 start: window is active (effective value is doubled).
            var turn2Start = await session.StartTurnAsync();
            Assert.Equal(dcReduction * 2, turn2Start.WeaknessDcReduction);

            // Turn 2 resolve: window is consumed (cleared by ResolveTurnAsync regardless of match).
            await session.ResolveTurnAsync(0);

            // Turn 3 start: no new window from LLM → null.
            var turn3Start = await session.StartTurnAsync();
            Assert.Null(turn3Start.WeaknessDcReduction);
        }

        // ── AC4: Wire serialization — JSON key is "weakness_dc_reduction" ─────

        [Fact]
        public async Task TurnStart_SerializesWeaknessDcReductionWithCorrectKey_WhenActive()
        {
            const int dcReduction = 3;
            var window = new WeaknessWindow(StatType.SelfAwareness, dcReduction);
            var llm = new WindowInjectingLlmAdapter593(window);
            var session = MakeSession(llm);

            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);
            var turn2Start = await session.StartTurnAsync();

            string json = JsonSerializer.Serialize(turn2Start);

            Assert.Contains("\"weakness_dc_reduction\"", json);
            Assert.Contains($":{dcReduction * 2}", json);
        }

        [Fact]
        public async Task TurnStart_SerializesWeaknessDcReductionAsNull_WhenNoWindow()
        {
            var session = MakeSession();
            var turnStart = await session.StartTurnAsync();

            string json = JsonSerializer.Serialize(turnStart);

            // Field must be present and null in the wire format.
            Assert.Contains("\"weakness_dc_reduction\":null", json);
        }

        // ── Private helpers ───────────────────────────────────────────────────

        private sealed class FixedDice593 : IDiceRoller
        {
            private readonly Queue<int> _values;
            public FixedDice593(params int[] values) => _values = new Queue<int>(values);
            public int Roll(int sides)
            {
                if (_values.Count == 0) return sides / 2 + 1;
                return _values.Dequeue();
            }
        }

        private sealed class NullTrapRegistry593 : ITrapRegistry
        {
            public TrapDefinition? GetTrap(StatType stat) => null;
            public string? GetLlmInstruction(StatType stat) => null;
        }

        /// <summary>
        /// LLM adapter that injects a WeaknessWindow on the first datee response,
        /// then returns no window on subsequent calls.
        /// </summary>
        private sealed class WindowInjectingLlmAdapter593 : ILlmAdapter
        {
            private readonly WeaknessWindow _window;
            private int _dateeCallCount;

            public WindowInjectingLlmAdapter593(WeaknessWindow window) => _window = window;

            public Task<DialogueOption[]> GetDialogueOptionsAsync(DialogueContext context, CancellationToken ct = default)
            {
                ct.ThrowIfCancellationRequested();
                var options = new[]
                {
                    new DialogueOption(StatType.Charm,   "Hey, you come here often?"),
                    new DialogueOption(StatType.Honesty, "I have to be real with you..."),
                    new DialogueOption(StatType.Wit,     "Did you know that penguins propose with pebbles?"),
                    new DialogueOption(StatType.Chaos,   "I once ate a whole pizza in a bouncy castle."),
                };
                return Task.FromResult(options);
            }


            public Task<DateeResponse> GetDateeResponseAsync(DateeContext context, CancellationToken ct = default)
            {
                ct.ThrowIfCancellationRequested();
                var weakness = _dateeCallCount == 0 ? _window : null;
                _dateeCallCount++;
                return Task.FromResult(new DateeResponse("Test response", weaknessWindow: weakness));
            }

            public Task<string?> GetInterestChangeBeatAsync(InterestChangeContext context, CancellationToken ct = default)
            {
                ct.ThrowIfCancellationRequested();
                return Task.FromResult<string?>(null);
            }

            public Task<string> ApplyHorninessOverlayAsync(string message, string instruction, string? dateeContext = null, string? archetypeDirective = null, CancellationToken ct = default)
            {
                ct.ThrowIfCancellationRequested();
                return Task.FromResult(message);
            }

            public Task<string> ApplyShadowCorruptionAsync(string message, string instruction, ShadowStatType shadow, string? archetypeDirective = null, CancellationToken ct = default)
            {
                ct.ThrowIfCancellationRequested();
                return Task.FromResult(message);
            }

            public Task<string> ApplyTrapOverlayAsync(string message, string trapInstruction, string trapName, string? dateeContext = null, string? archetypeDirective = null, CancellationToken ct = default)
            {
                ct.ThrowIfCancellationRequested();
                return Task.FromResult(message);
            }
        
        public Task<string> ApplyFailureCorruptionAsync(string message, string instruction, Pinder.Core.Stats.StatType stat, Pinder.Core.Rolls.FailureTier tier, string? archetypeDirective = null, System.Threading.CancellationToken ct = default)
        {
            return Task.FromResult(message);
        }
}
    }
}
