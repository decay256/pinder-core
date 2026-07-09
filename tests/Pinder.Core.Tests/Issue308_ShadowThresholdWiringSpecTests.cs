using System;
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
    /// Spec tests for Issue #308: GameSession must wire shadowThresholds to the
    /// LLM contexts, not just DialogueContext.
    ///
    /// #1125: the delivery LLM call (and DeliveryContext) were collapsed into the
    /// deterministic, non-LLM DeliveryOverlay commit step. Player-shadow text
    /// effects now reach the model only via the shadow-corruption OVERLAY
    /// (ApplyShadowCorruptionAsync, driven by state.PlayerShadows) — covered by
    /// Issue307_ShadowTaintRawValueTests / Issue365 — not a DeliveryContext. The
    /// surviving DateeContext shadow-threshold wiring (datee shadows) is asserted
    /// here.
    ///
    /// Acceptance Criteria (post-#1125):
    ///   AC2: DateeContext receives datee shadowThresholds (if _dateeShadows is set)
    ///   AC4: Madness=8 → shadow taint appears in datee response prompt
    /// </summary>
    [Trait("Category", "Core")]
    public class Issue308_ShadowThresholdWiringSpecTests
    {
        // #1125: AC1 DeliveryContext player-shadow tests removed — there is no
        // DeliveryContext; player-shadow text effects now flow through the
        // shadow-corruption overlay (covered by Issue307_ShadowTaintRawValueTests).

        // ================================================================
        // AC2: DateeContext receives datee shadowThresholds
        // ================================================================

        // What: AC2 — DateeContext receives datee shadow thresholds
        // Mutation: Fails if GameSession omits shadowThresholds param in DateeContext constructor
        [Fact]
        public async Task AC2_DateeContext_ReceivesDateeShadowThresholds()
        {
            Dictionary<ShadowStatType, int>? capturedDatee = null;
            var dateeShadows = TestHelpers.MakeShadowTracker(madness: 8);
            var llm = new CapturingLlmAdapter(
                onDatee: ctx => capturedDatee = ctx.ShadowThresholds);

            var session = BuildSession(new[] { 5, 15, 15 }, dateeShadows: dateeShadows, llm: llm);
            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);

            Assert.NotNull(capturedDatee);
            Assert.True(capturedDatee!.ContainsKey(ShadowStatType.Madness));
            Assert.Equal(8, capturedDatee[ShadowStatType.Madness]);
        }

        // What: AC2 — DateeContext receives DATEE shadows, not player shadows
        // Mutation: Fails if GameSession accidentally passes player shadows to DateeContext
        [Fact]
        public async Task AC2_DateeContext_ReceivesDateeShadows_NotPlayerShadows()
        {
            Dictionary<ShadowStatType, int>? capturedDatee = null;
            var playerShadows = TestHelpers.MakeShadowTracker(dread: 20);
            var dateeShadows = TestHelpers.MakeShadowTracker(dread: 7);
            var llm = new CapturingLlmAdapter(
                onDatee: ctx => capturedDatee = ctx.ShadowThresholds);

            var session = BuildSession(new[] { 5, 15, 15 },
                playerShadows: playerShadows,
                dateeShadows: dateeShadows,
                llm: llm);
            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);

            Assert.NotNull(capturedDatee);
            // Must be datee's value (7), not player's (20)
            Assert.Equal(7, capturedDatee![ShadowStatType.Dread]);
        }

        // ================================================================
        // AC2 edge: DateeContext null when no datee shadows configured
        // ================================================================

        // What: AC2 edge — no datee shadows means null thresholds on DateeContext
        // Mutation: Fails if GameSession passes empty dict instead of null when no datee shadows
        [Fact]
        public async Task AC2_NoDateeShadows_DateeContextThresholdsNull()
        {
            Dictionary<ShadowStatType, int>? capturedDatee = null;
            bool wasCalled = false;
            var llm = new CapturingLlmAdapter(
                onDatee: ctx => { capturedDatee = ctx.ShadowThresholds; wasCalled = true; });

            var session = BuildSession(new[] { 5, 15, 15 }, llm: llm);
            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);

            Assert.True(wasCalled);
            Assert.Null(capturedDatee);
        }

        // #1125: AC1 no-player-shadows and AC3 all-stats-in-DeliveryContext tests
        // removed — there is no DeliveryContext. The all-six-stats datee path is
        // covered by AC4_AllSixShadowStats_InDateeContext below.

        // What: AC4 — all shadow stat types appear in DateeContext thresholds
        // Mutation: Fails if any shadow stat type is omitted from the datee threshold dictionary
        [Fact]
        public async Task AC4_AllSixShadowStats_InDateeContext()
        {
            Dictionary<ShadowStatType, int>? capturedDatee = null;
            var dateeShadows = TestHelpers.MakeShadowTracker(
                dread: 1, denial: 3, fixation: 5,
                madness: 7, overthinking: 9, horniness: 11);
            var llm = new CapturingLlmAdapter(
                onDatee: ctx => capturedDatee = ctx.ShadowThresholds);

            var session = BuildSession(new[] { 5, 15, 15 }, dateeShadows: dateeShadows, llm: llm);
            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);

            Assert.NotNull(capturedDatee);
            Assert.Equal(1, capturedDatee![ShadowStatType.Dread]);
            Assert.Equal(3, capturedDatee[ShadowStatType.Denial]);
            Assert.Equal(5, capturedDatee[ShadowStatType.Fixation]);
            Assert.Equal(7, capturedDatee[ShadowStatType.Madness]);
            Assert.Equal(9, capturedDatee[ShadowStatType.Overthinking]);
            Assert.Equal(11, capturedDatee[ShadowStatType.Despair]);
        }

        // ================================================================
        // Cross-wiring guard: both contexts populated simultaneously
        // ================================================================

        // What: datee context receives its own shadow values, distinct from the
        // player's, in a single turn.
        // Mutation: Fails if the player shadow tracker is accidentally used for datee.
        [Fact]
        public async Task Datee_ReceivesItsOwnShadows_NotPlayerShadows_InSameTurn()
        {
            Dictionary<ShadowStatType, int>? capturedDatee = null;
            var playerShadows = TestHelpers.MakeShadowTracker(madness: 10, horniness: 3);
            var dateeShadows = TestHelpers.MakeShadowTracker(madness: 2, horniness: 14);
            var llm = new CapturingLlmAdapter(
                onDatee: ctx => capturedDatee = ctx.ShadowThresholds);

            var session = BuildSession(new[] { 5, 15, 15 },
                playerShadows: playerShadows,
                dateeShadows: dateeShadows,
                llm: llm);
            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);

            // Datee = datee shadows (NOT the player's 10/3).
            Assert.NotNull(capturedDatee);
            Assert.Equal(2, capturedDatee![ShadowStatType.Madness]);
            Assert.Equal(14, capturedDatee[ShadowStatType.Despair]);
        }

        // What: Edge — player shadows set but datee not → datee thresholds null
        // (player shadows route through the overlay, not the datee context).
        [Fact]
        public async Task PlayerShadowsOnly_DateeContextThresholdsNull()
        {
            Dictionary<ShadowStatType, int>? capturedDatee = null;
            bool wasCalled = false;
            var playerShadows = TestHelpers.MakeShadowTracker(fixation: 9);
            var llm = new CapturingLlmAdapter(
                onDatee: ctx => { capturedDatee = ctx.ShadowThresholds; wasCalled = true; });

            var session = BuildSession(new[] { 5, 15, 15 },
                playerShadows: playerShadows,
                llm: llm);
            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);

            Assert.True(wasCalled);
            Assert.Null(capturedDatee);
        }

        // What: Edge — datee shadows set but player not → datee has thresholds.
        [Fact]
        public async Task DateeShadowsOnly_DateeContextHasThresholds()
        {
            Dictionary<ShadowStatType, int>? capturedDatee = null;
            var dateeShadows = TestHelpers.MakeShadowTracker(denial: 11);
            var llm = new CapturingLlmAdapter(
                onDatee: ctx => capturedDatee = ctx.ShadowThresholds);

            var session = BuildSession(new[] { 5, 15, 15 },
                dateeShadows: dateeShadows,
                llm: llm);
            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);

            Assert.NotNull(capturedDatee);
            Assert.Equal(11, capturedDatee![ShadowStatType.Denial]);
        }

        // ================================================================
        // Zero shadows — values present but all zero
        // ================================================================

        // What: Shadow thresholds with all-zero values still propagate (not treated as null)
        // Mutation: Fails if implementation treats all-zero shadows as "no shadows"
        [Fact]
        public async Task ZeroShadows_StillPassedAsNonNullDictionary()
        {
            Dictionary<ShadowStatType, int>? capturedDatee = null;
            var dateeShadows = TestHelpers.MakeShadowTracker(); // all zeros
            var llm = new CapturingLlmAdapter(
                onDatee: ctx => capturedDatee = ctx.ShadowThresholds);

            var session = BuildSession(new[] { 5, 15, 15 },
                dateeShadows: dateeShadows,
                llm: llm);
            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);

            Assert.NotNull(capturedDatee);
            Assert.Equal(0, capturedDatee![ShadowStatType.Madness]);
        }

        // ================================================================
        // Helpers — test-only utilities
        // ================================================================

        private static CharacterProfile CreateProfile(string name)
        {
            var timing = new TimingProfile(5, 1.0f, 0.0f, "neutral");
            return new CharacterProfile(TestHelpers.MakeStatBlock(), "system prompt", name, timing, 1);
        }

        private static GameSession BuildSession(
            int[] diceValues,
            SessionShadowTracker? playerShadows = null,
            SessionShadowTracker? dateeShadows = null,
            ILlmAdapter? llm = null)
        {
            var config = new GameSessionConfig(clock: TestHelpers.MakeClock(), playerShadows: playerShadows,
                dateeShadows: dateeShadows);

            // dice[0] = ghost check (need >1 to avoid ghost), rest = roll
            var allDice = new int[diceValues.Length + 1];
            allDice[0] = 4; // ghost check safe
            Array.Copy(diceValues, 0, allDice, 1, diceValues.Length);

            return new GameSession(
                CreateProfile("TestPlayer"),
                CreateProfile("TestDatee"),
                llm ?? new CapturingLlmAdapter(),
                new QueueDice(allDice),
                new NullTrapRegistry(),
                config);
        }

        private sealed class QueueDice : IDiceRoller
        {
            private readonly Queue<int> _values;
            public QueueDice(int[] values) => _values = new Queue<int>(values);
            public int Roll(int sides) => _values.Count > 0 ? _values.Dequeue() : 10;
        }

        private sealed class NullTrapRegistry : ITrapRegistry
        {
            public TrapDefinition? GetTrap(StatType stat) => null;
            public string? GetLlmInstruction(StatType stat) => null;
        }

        /// <summary>
        /// LLM adapter that captures DeliveryContext and DateeContext for assertion.
        /// Returns minimal valid responses for all methods.
        /// </summary>
        private sealed class CapturingLlmAdapter : ILlmAdapter
        {
            private readonly Action<DateeContext>? _onDatee;

            public CapturingLlmAdapter(
                Action<DateeContext>? onDatee = null)
            {
                _onDatee = onDatee;
            }

            public Task<DialogueOption[]> GetDialogueOptionsAsync(DialogueContext context, System.Threading.CancellationToken ct = default)
            {
                return Task.FromResult(new[]
                {
                    new DialogueOption(StatType.Charm, "Hey there"),
                    new DialogueOption(StatType.Wit, "Clever line")
                });
            }

            public Task<DateeResponse> GetDateeResponseAsync(DateeContext context, System.Threading.CancellationToken ct = default)
            {
                _onDatee?.Invoke(context);
                return Task.FromResult(new DateeResponse("reply"));
            }

            public Task<string?> GetInterestChangeBeatAsync(InterestChangeContext context, System.Threading.CancellationToken ct = default)
            {
                return Task.FromResult<string?>(null);
            }
            public System.Threading.Tasks.Task<string> ApplyHorninessOverlayAsync(string message, string instruction, string? dateeContext = null, string? archetypeDirective = null, System.Threading.CancellationToken ct = default) => System.Threading.Tasks.Task.FromResult(message);
            public System.Threading.Tasks.Task<string> ApplyShadowCorruptionAsync(string message, string instruction, Pinder.Core.Stats.ShadowStatType shadow, string? archetypeDirective = null, System.Threading.CancellationToken ct = default) => System.Threading.Tasks.Task.FromResult(message);
            public System.Threading.Tasks.Task<string> ApplyTrapOverlayAsync(string message, string trapInstruction, string trapName, string? dateeContext = null, string? archetypeDirective = null, System.Threading.CancellationToken ct = default) => System.Threading.Tasks.Task.FromResult(message);
        
        public System.Threading.Tasks.Task<string> ApplyFailureCorruptionAsync(string message, string instruction, Pinder.Core.Stats.StatType stat, Pinder.Core.Rolls.FailureTier tier, string? archetypeDirective = null, System.Threading.CancellationToken ct = default)
        {
            return System.Threading.Tasks.Task.FromResult(message);
        }
}
    }
}
