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
    /// Spec-based tests for issue #45: Shadow thresholds — §7 threshold effects on gameplay.
    /// Written from docs/specs/issue-45-spec.md only (context-isolated from implementation).
    /// Maturity: Prototype — happy-path per AC + key edge cases.
    /// </summary>
    [Trait("Category", "Core")]
    public partial class ShadowThresholdSpecTests
    {
        // =====================================================================
        // AC1: ShadowThresholdEvaluator computes threshold level (0/1/2/3)
        // =====================================================================

        // Mutation: would catch if boundary at 5 used >= instead of >, or threshold 6 mapped wrong
        [Theory]
        [InlineData(0, 0)]
        [InlineData(1, 0)]
        [InlineData(5, 0)]
        [InlineData(6, 1)]
        [InlineData(7, 1)]
        [InlineData(11, 1)]
        [InlineData(12, 2)]
        [InlineData(13, 2)]
        [InlineData(17, 2)]
        [InlineData(18, 3)]
        [InlineData(19, 3)]
        [InlineData(25, 3)]
        [InlineData(100, 3)]
        public void AC1_GetThresholdLevel_ReturnsCorrectTier(int shadowValue, int expected)
        {
            // What: §7 threshold boundaries — 6=T1, 12=T2, 18+=T3
            Assert.Equal(expected, ShadowThresholdEvaluator.GetThresholdLevel(shadowValue));
        }

        // Mutation: would catch if negative values throw instead of returning 0
        // Edge case 6.1: negative shadow values
        [Theory]
        [InlineData(-1, 0)]
        [InlineData(-100, 0)]
        [InlineData(int.MinValue, 0)]
        public void AC1_NegativeShadowValues_ReturnTierZero(int shadowValue, int expected)
        {
            // What: Defensive guard — negative values should not throw (spec §6.1)
            Assert.Equal(expected, ShadowThresholdEvaluator.GetThresholdLevel(shadowValue));
        }

        // =====================================================================
        // AC2 + Spec §2.2: InterestMeter(int) constructor overload
        // =====================================================================

        // Mutation: would catch if InterestMeter(int) uses default 10 instead of the parameter
        [Fact]
        public void AC2_InterestMeter_IntConstructor_SetsCurrentToValue()
        {
            // What: InterestMeter(8) should start at 8, not default 10
            var meter = new InterestMeter(8);
            Assert.Equal(8, meter.Current);
        }

        // Mutation: would catch if parameterless constructor is broken by new overload
        [Fact]
        public void AC2_InterestMeter_DefaultConstructor_StillStartsAt10()
        {
            // What: Existing parameterless constructor unchanged (spec §6.10)
            var meter = new InterestMeter();
            Assert.Equal(10, meter.Current);
        }

        // Mutation: would catch if clamping uses wrong upper bound
        [Theory]
        [InlineData(30, 25)]
        [InlineData(100, 25)]
        public void AC2_InterestMeter_ClampsToMax(int input, int expected)
        {
            // What: Values above Max (25) clamped silently (spec §6.9)
            Assert.Equal(expected, new InterestMeter(input).Current);
        }

        // Mutation: would catch if clamping uses wrong lower bound
        [Theory]
        [InlineData(-5, 0)]
        [InlineData(-100, 0)]
        public void AC2_InterestMeter_ClampsToMin(int input, int expected)
        {
            // What: Values below Min (0) clamped silently (spec §6.9)
            Assert.Equal(expected, new InterestMeter(input).Current);
        }

        // Mutation: would catch if boundary values (0, 25) are off-by-one
        [Fact]
        public void AC2_InterestMeter_BoundaryValues()
        {
            Assert.Equal(0, new InterestMeter(0).Current);
            Assert.Equal(25, new InterestMeter(25).Current);
        }

        // =====================================================================
        // AC5: Dread ≥18 → Starting Interest 8
        // =====================================================================

        // Mutation: would catch if Dread T3 doesn't change starting interest
        [Fact]
        public async Task AC5_DreadT3_StartsAt8Interest()
        {
            // What: Dread shadow ≥18 → InterestMeter(8) at construction (spec §3.3)
            var shadows = MakeShadowTracker(dread: 18);
            var session = MakeSession(diceValues: new[] { 15, 50 }, shadows: shadows);

            var turn = await session.StartTurnAsync();
            Assert.Equal(8, turn.State.Interest);
        }

        // Mutation: would catch if Dread T3 check uses ≥17 instead of ≥18
        [Fact]
        public async Task AC5_DreadAt17_StartsAt10Interest()
        {
            // What: Dread=17 is T2, NOT T3 — interest stays 10
            var shadows = MakeShadowTracker(dread: 17);
            var session = MakeSession(diceValues: new[] { 15, 50 }, shadows: shadows);

            var turn = await session.StartTurnAsync();
            Assert.Equal(10, turn.State.Interest);
        }

        // Mutation: would catch if Dread T2 incorrectly triggers starting interest change
        [Fact]
        public async Task AC5_DreadT2_StartsAt10Interest()
        {
            // What: Dread=12 (T2) should NOT affect starting interest
            var shadows = MakeShadowTracker(dread: 12);
            var session = MakeSession(diceValues: new[] { 15, 50 }, shadows: shadows);

            var turn = await session.StartTurnAsync();
            Assert.Equal(10, turn.State.Interest);
        }

        // Mutation: would catch if explicit startingInterest doesn't override Dread T3
        [Fact]
        public async Task AC5_ExplicitStartingInterest_OverridesDreadT3()
        {
            // What: If config has explicit StartingInterest, that takes priority over Dread T3
            var shadows = MakeShadowTracker(dread: 20);
            var session = MakeSession(diceValues: new[] { 15, 50 }, shadows: shadows, startingInterest: 5);

            var turn = await session.StartTurnAsync();
            Assert.Equal(5, turn.State.Interest);
        }

        // Mutation: would catch if large Dread value breaks (e.g., only checks ==18)
        [Fact]
        public async Task AC5_DreadAt25_StartsAt8Interest()
        {
            // What: Any Dread ≥18 should trigger T3, not just exactly 18
            var shadows = MakeShadowTracker(dread: 25);
            var session = MakeSession(diceValues: new[] { 15, 50 }, shadows: shadows);

            var turn = await session.StartTurnAsync();
            Assert.Equal(8, turn.State.Interest);
        }

        // =====================================================================
        // Helpers (test-only utilities, not copied from implementation)
        // =====================================================================

        private static SessionShadowTracker MakeShadowTracker(
            int dread = 0, int denial = 0, int fixation = 0,
            int madness = 0, int overthinking = 0, int horniness = 0)
        {
            var stats = new StatBlock(
                new Dictionary<StatType, int>
                {
                    { StatType.Charm, 2 }, { StatType.Rizz, 2 }, { StatType.Honesty, 2 },
                    { StatType.Chaos, 2 }, { StatType.Wit, 2 }, { StatType.SelfAwareness, 2 }
                },
                new Dictionary<ShadowStatType, int>
                {
                    { ShadowStatType.Dread, dread }, { ShadowStatType.Denial, denial },
                    { ShadowStatType.Fixation, fixation }, { ShadowStatType.Madness, madness },
                    { ShadowStatType.Overthinking, overthinking }, { ShadowStatType.Despair, horniness }
                });
            return new SessionShadowTracker(stats);
        }

        private static StatBlock MakeStatBlock()
        {
            return new StatBlock(
                new Dictionary<StatType, int>
                {
                    { StatType.Charm, 2 }, { StatType.Rizz, 2 }, { StatType.Honesty, 2 },
                    { StatType.Chaos, 2 }, { StatType.Wit, 2 }, { StatType.SelfAwareness, 2 }
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

        private static GameSession MakeSession(
            int[] diceValues,
            SessionShadowTracker? shadows,
            DialogueOption[]? llmOptions = null,
            int? startingInterest = null)
        {
            var config = new GameSessionConfig(clock: TestHelpers.MakeClock(), playerShadows: shadows,
                startingInterest: startingInterest);

            ILlmAdapter llm = llmOptions != null
                ? (ILlmAdapter)new FixedOptionsLlmAdapter(llmOptions)
                : new NullLlmAdapter();

            var allDice = new int[diceValues.Length + 1];
            allDice[0] = 5;
            Array.Copy(diceValues, 0, allDice, 1, diceValues.Length);

            return new GameSession(
                MakeProfile("player"),
                MakeProfile("datee"),
                llm,
                new QueueDice(allDice),
                new EmptyTrapRegistry(),
                config);
        }

        private static GameSession MakeSessionWithLlm(
            int[] diceValues,
            SessionShadowTracker? shadows,
            ILlmAdapter llm)
        {
            var config = new GameSessionConfig(clock: TestHelpers.MakeClock(), playerShadows: shadows);

            var allDice2 = new int[diceValues.Length + 1];
            allDice2[0] = 5;
            Array.Copy(diceValues, 0, allDice2, 1, diceValues.Length);

            return new GameSession(
                MakeProfile("player"),
                MakeProfile("datee"),
                llm,
                new QueueDice(allDice2),
                new EmptyTrapRegistry(),
                config);
        }

        // ---- Test doubles ----

        private sealed class QueueDice : IDiceRoller
        {
            private readonly Queue<int> _values;
            public QueueDice(int[] values) => _values = new Queue<int>(values);
            public int Roll(int sides) => _values.Count > 0 ? _values.Dequeue() : 10;
        }

        private sealed class EmptyTrapRegistry : ITrapRegistry
        {
            public TrapDefinition? GetTrap(StatType stat) => null;
            public string? GetLlmInstruction(StatType stat) => null;
        }

        private sealed class FixedOptionsLlmAdapter : ILlmAdapter
        {
            private readonly DialogueOption[] _options;
            public FixedOptionsLlmAdapter(DialogueOption[] options) => _options = options;

            public Task<DialogueOption[]> GetDialogueOptionsAsync(DialogueContext context, System.Threading.CancellationToken ct = default)
                => Task.FromResult(_options);
            public Task<string> DeliverMessageAsync(DeliveryContext context, System.Threading.CancellationToken ct = default)
                => Task.FromResult(context.ChosenOption.IntendedText);
            public Task<DateeResponse> GetDateeResponseAsync(DateeContext context, System.Threading.CancellationToken ct = default)
                => Task.FromResult(new DateeResponse("..."));
            public Task<string?> GetInterestChangeBeatAsync(InterestChangeContext context, System.Threading.CancellationToken ct = default)
                => Task.FromResult<string?>(null);
            public System.Threading.Tasks.Task<string> ApplyHorninessOverlayAsync(string message, string instruction, string? dateeContext = null, string? archetypeDirective = null, System.Threading.CancellationToken ct = default) => System.Threading.Tasks.Task.FromResult(message);
            public System.Threading.Tasks.Task<string> ApplyShadowCorruptionAsync(string message, string instruction, Pinder.Core.Stats.ShadowStatType shadow, string? archetypeDirective = null, System.Threading.CancellationToken ct = default) => System.Threading.Tasks.Task.FromResult(message);
            public System.Threading.Tasks.Task<string> ApplyTrapOverlayAsync(string message, string trapInstruction, string trapName, string? dateeContext = null, string? archetypeDirective = null, System.Threading.CancellationToken ct = default) => System.Threading.Tasks.Task.FromResult(message);
        }

                private sealed class CapturingLlmAdapter : ILlmAdapter
        {
            private readonly Action<DialogueContext> _onGetOptions;
            public CapturingLlmAdapter(Action<DialogueContext> onGetOptions) => _onGetOptions = onGetOptions;

            public Task<DialogueOption[]> GetDialogueOptionsAsync(DialogueContext context, System.Threading.CancellationToken ct = default)
            {
                _onGetOptions(context);
                return Task.FromResult(new[]
                {
                    new DialogueOption(StatType.Charm, "Hey"),
                    new DialogueOption(StatType.Wit, "Clever")
                });
            }
            public Task<string> DeliverMessageAsync(DeliveryContext context, System.Threading.CancellationToken ct = default)
                => Task.FromResult(context.ChosenOption.IntendedText);
            public Task<DateeResponse> GetDateeResponseAsync(DateeContext context, System.Threading.CancellationToken ct = default)
                => Task.FromResult(new DateeResponse("..."));
            public Task<string?> GetInterestChangeBeatAsync(InterestChangeContext context, System.Threading.CancellationToken ct = default)
                => Task.FromResult<string?>(null);
            public System.Threading.Tasks.Task<string> ApplyHorninessOverlayAsync(string message, string instruction, string? dateeContext = null, string? archetypeDirective = null, System.Threading.CancellationToken ct = default) => System.Threading.Tasks.Task.FromResult(message);
            public System.Threading.Tasks.Task<string> ApplyShadowCorruptionAsync(string message, string instruction, Pinder.Core.Stats.ShadowStatType shadow, string? archetypeDirective = null, System.Threading.CancellationToken ct = default) => System.Threading.Tasks.Task.FromResult(message);
            public System.Threading.Tasks.Task<string> ApplyTrapOverlayAsync(string message, string trapInstruction, string trapName, string? dateeContext = null, string? archetypeDirective = null, System.Threading.CancellationToken ct = default) => System.Threading.Tasks.Task.FromResult(message);
        }
    }
}
