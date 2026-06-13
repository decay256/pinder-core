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
    /// Spec tests for Issue #308: GameSession must wire shadowThresholds to DeliveryContext
    /// and DateeContext, not just DialogueContext.
    /// 
    /// Acceptance Criteria:
    ///   AC1: DeliveryContext receives player shadowThresholds
    ///   AC2: DateeContext receives datee shadowThresholds (if _dateeShadows is set)
    ///   AC3: Madness=8 → shadow taint appears in delivery prompt
    ///   AC4: Madness=8 → shadow taint appears in datee response prompt
    /// </summary>
    [Trait("Category", "Core")]
    public class Issue308_ShadowThresholdWiringSpecTests
    {
        // ================================================================
        // AC1: DeliveryContext receives player shadowThresholds
        // ================================================================

        // What: AC1 — DeliveryContext receives player shadow thresholds
        // Mutation: Fails if GameSession omits shadowThresholds param in DeliveryContext constructor
        [Fact]
        public async Task AC1_DeliveryContext_ReceivesPlayerShadowThresholds()
        {
            Dictionary<ShadowStatType, int>? capturedDelivery = null;
            var playerShadows = CreateShadowTracker(madness: 8);
            var llm = new CapturingLlmAdapter(
                onDeliver: ctx => capturedDelivery = ctx.ShadowThresholds);

            var session = BuildSession(new[] { 5, 15, 15 }, playerShadows: playerShadows, llm: llm);
            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);

            Assert.NotNull(capturedDelivery);
            Assert.True(capturedDelivery!.ContainsKey(ShadowStatType.Madness));
            Assert.Equal(8, capturedDelivery[ShadowStatType.Madness]);
        }

        // What: AC1 — Delivery receives PLAYER shadows, not datee shadows
        // Mutation: Fails if GameSession accidentally passes datee shadows to DeliveryContext
        [Fact]
        public async Task AC1_DeliveryContext_ReceivesPlayerShadows_NotDateeShadows()
        {
            Dictionary<ShadowStatType, int>? capturedDelivery = null;
            var playerShadows = CreateShadowTracker(madness: 15);
            var dateeShadows = CreateShadowTracker(madness: 4);
            var llm = new CapturingLlmAdapter(
                onDeliver: ctx => capturedDelivery = ctx.ShadowThresholds);

            var session = BuildSession(new[] { 5, 15, 15 },
                playerShadows: playerShadows,
                dateeShadows: dateeShadows,
                llm: llm);
            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);

            Assert.NotNull(capturedDelivery);
            // Must be player's value (15), not datee's (4)
            Assert.Equal(15, capturedDelivery![ShadowStatType.Madness]);
        }

        // ================================================================
        // AC2: DateeContext receives datee shadowThresholds
        // ================================================================

        // What: AC2 — DateeContext receives datee shadow thresholds
        // Mutation: Fails if GameSession omits shadowThresholds param in DateeContext constructor
        [Fact]
        public async Task AC2_DateeContext_ReceivesDateeShadowThresholds()
        {
            Dictionary<ShadowStatType, int>? capturedDatee = null;
            var dateeShadows = CreateShadowTracker(madness: 8);
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
            var playerShadows = CreateShadowTracker(dread: 20);
            var dateeShadows = CreateShadowTracker(dread: 7);
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

        // What: AC1 edge — no player shadows means null thresholds on DeliveryContext
        // Mutation: Fails if GameSession passes empty dict instead of null when no player shadows
        [Fact]
        public async Task AC1_NoPlayerShadows_DeliveryContextThresholdsNull()
        {
            Dictionary<ShadowStatType, int>? capturedDelivery = null;
            bool wasCalled = false;
            var llm = new CapturingLlmAdapter(
                onDeliver: ctx => { capturedDelivery = ctx.ShadowThresholds; wasCalled = true; });

            var session = BuildSession(new[] { 5, 15, 15 }, llm: llm);
            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);

            Assert.True(wasCalled);
            Assert.Null(capturedDelivery);
        }

        // ================================================================
        // AC3/AC4: All 6 shadow stats pass through correctly
        // ================================================================

        // What: AC3 — all shadow stat types appear in DeliveryContext thresholds
        // Mutation: Fails if any shadow stat type is omitted from the threshold dictionary
        [Fact]
        public async Task AC3_AllSixShadowStats_InDeliveryContext()
        {
            Dictionary<ShadowStatType, int>? capturedDelivery = null;
            var playerShadows = CreateShadowTracker(
                dread: 2, denial: 4, fixation: 6,
                madness: 8, overthinking: 10, horniness: 12);
            var llm = new CapturingLlmAdapter(
                onDeliver: ctx => capturedDelivery = ctx.ShadowThresholds);

            var session = BuildSession(new[] { 5, 15, 15 }, playerShadows: playerShadows, llm: llm);
            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);

            Assert.NotNull(capturedDelivery);
            Assert.Equal(2, capturedDelivery![ShadowStatType.Dread]);
            Assert.Equal(4, capturedDelivery[ShadowStatType.Denial]);
            Assert.Equal(6, capturedDelivery[ShadowStatType.Fixation]);
            Assert.Equal(8, capturedDelivery[ShadowStatType.Madness]);
            Assert.Equal(10, capturedDelivery[ShadowStatType.Overthinking]);
            Assert.Equal(12, capturedDelivery[ShadowStatType.Despair]);
        }

        // What: AC4 — all shadow stat types appear in DateeContext thresholds
        // Mutation: Fails if any shadow stat type is omitted from the datee threshold dictionary
        [Fact]
        public async Task AC4_AllSixShadowStats_InDateeContext()
        {
            Dictionary<ShadowStatType, int>? capturedDatee = null;
            var dateeShadows = CreateShadowTracker(
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

        // What: Both contexts receive their respective shadow values in a single turn
        // Mutation: Fails if the same shadow tracker is accidentally used for both contexts
        [Fact]
        public async Task BothContexts_ReceiveCorrectShadows_InSameTurn()
        {
            Dictionary<ShadowStatType, int>? capturedDelivery = null;
            Dictionary<ShadowStatType, int>? capturedDatee = null;
            var playerShadows = CreateShadowTracker(madness: 10, horniness: 3);
            var dateeShadows = CreateShadowTracker(madness: 2, horniness: 14);
            var llm = new CapturingLlmAdapter(
                onDeliver: ctx => capturedDelivery = ctx.ShadowThresholds,
                onDatee: ctx => capturedDatee = ctx.ShadowThresholds);

            var session = BuildSession(new[] { 5, 15, 15 },
                playerShadows: playerShadows,
                dateeShadows: dateeShadows,
                llm: llm);
            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);

            // Delivery = player shadows
            Assert.NotNull(capturedDelivery);
            Assert.Equal(10, capturedDelivery![ShadowStatType.Madness]);
            Assert.Equal(3, capturedDelivery[ShadowStatType.Despair]);

            // Datee = datee shadows
            Assert.NotNull(capturedDatee);
            Assert.Equal(2, capturedDatee![ShadowStatType.Madness]);
            Assert.Equal(14, capturedDatee[ShadowStatType.Despair]);
        }

        // What: Edge — player shadows set but datee not → delivery has thresholds, datee null
        // Mutation: Fails if having player shadows incorrectly populates datee thresholds
        [Fact]
        public async Task PlayerShadowsOnly_DateeContextThresholdsNull()
        {
            Dictionary<ShadowStatType, int>? capturedDelivery = null;
            Dictionary<ShadowStatType, int>? capturedDatee = null;
            var playerShadows = CreateShadowTracker(fixation: 9);
            var llm = new CapturingLlmAdapter(
                onDeliver: ctx => capturedDelivery = ctx.ShadowThresholds,
                onDatee: ctx => capturedDatee = ctx.ShadowThresholds);

            var session = BuildSession(new[] { 5, 15, 15 },
                playerShadows: playerShadows,
                llm: llm);
            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);

            Assert.NotNull(capturedDelivery);
            Assert.Equal(9, capturedDelivery![ShadowStatType.Fixation]);
            Assert.Null(capturedDatee);
        }

        // What: Edge — datee shadows set but player not → datee has thresholds, delivery null
        // Mutation: Fails if having datee shadows incorrectly populates delivery thresholds
        [Fact]
        public async Task DateeShadowsOnly_DeliveryContextThresholdsNull()
        {
            Dictionary<ShadowStatType, int>? capturedDelivery = null;
            Dictionary<ShadowStatType, int>? capturedDatee = null;
            var dateeShadows = CreateShadowTracker(denial: 11);
            var llm = new CapturingLlmAdapter(
                onDeliver: ctx => capturedDelivery = ctx.ShadowThresholds,
                onDatee: ctx => capturedDatee = ctx.ShadowThresholds);

            var session = BuildSession(new[] { 5, 15, 15 },
                dateeShadows: dateeShadows,
                llm: llm);
            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);

            Assert.Null(capturedDelivery);
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
            Dictionary<ShadowStatType, int>? capturedDelivery = null;
            var playerShadows = CreateShadowTracker(); // all zeros
            var llm = new CapturingLlmAdapter(
                onDeliver: ctx => capturedDelivery = ctx.ShadowThresholds);

            var session = BuildSession(new[] { 5, 15, 15 },
                playerShadows: playerShadows,
                llm: llm);
            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);

            Assert.NotNull(capturedDelivery);
            Assert.Equal(0, capturedDelivery![ShadowStatType.Madness]);
        }

        // ================================================================
        // Helpers — test-only utilities
        // ================================================================

        private static SessionShadowTracker CreateShadowTracker(
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

        private static StatBlock CreateStatBlock()
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

        private static CharacterProfile CreateProfile(string name)
        {
            var timing = new TimingProfile(5, 1.0f, 0.0f, "neutral");
            return new CharacterProfile(CreateStatBlock(), "system prompt", name, timing, 1);
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
            private readonly Action<DeliveryContext>? _onDeliver;
            private readonly Action<DateeContext>? _onDatee;

            public CapturingLlmAdapter(
                Action<DeliveryContext>? onDeliver = null,
                Action<DateeContext>? onDatee = null)
            {
                _onDeliver = onDeliver;
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

            public Task<string> DeliverMessageAsync(DeliveryContext context, System.Threading.CancellationToken ct = default)
            {
                _onDeliver?.Invoke(context);
                return Task.FromResult(context.ChosenOption.IntendedText);
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
        }
    }
}
