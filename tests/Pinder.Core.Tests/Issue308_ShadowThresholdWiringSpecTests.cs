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
    /// and OpponentContext, not just DialogueContext.
    /// 
    /// Acceptance Criteria:
    ///   AC1: DeliveryContext receives player shadowThresholds
    ///   AC2: OpponentContext receives opponent shadowThresholds (if _opponentShadows is set)
    ///   AC3: Madness=8 → shadow taint appears in delivery prompt
    ///   AC4: Madness=8 → shadow taint appears in opponent response prompt
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

        // What: AC1 — Delivery receives PLAYER shadows, not opponent shadows
        // Mutation: Fails if GameSession accidentally passes opponent shadows to DeliveryContext
        [Fact]
        public async Task AC1_DeliveryContext_ReceivesPlayerShadows_NotOpponentShadows()
        {
            Dictionary<ShadowStatType, int>? capturedDelivery = null;
            var playerShadows = CreateShadowTracker(madness: 15);
            var opponentShadows = CreateShadowTracker(madness: 4);
            var llm = new CapturingLlmAdapter(
                onDeliver: ctx => capturedDelivery = ctx.ShadowThresholds);

            var session = BuildSession(new[] { 5, 15, 15 },
                playerShadows: playerShadows,
                opponentShadows: opponentShadows,
                llm: llm);
            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);

            Assert.NotNull(capturedDelivery);
            // Must be player's value (15), not opponent's (4)
            Assert.Equal(15, capturedDelivery![ShadowStatType.Madness]);
        }

        // ================================================================
        // AC2: OpponentContext receives opponent shadowThresholds
        // ================================================================

        // What: AC2 — OpponentContext receives opponent shadow thresholds
        // Mutation: Fails if GameSession omits shadowThresholds param in OpponentContext constructor
        [Fact]
        public async Task AC2_OpponentContext_ReceivesOpponentShadowThresholds()
        {
            Dictionary<ShadowStatType, int>? capturedOpponent = null;
            var opponentShadows = CreateShadowTracker(madness: 8);
            var llm = new CapturingLlmAdapter(
                onOpponent: ctx => capturedOpponent = ctx.ShadowThresholds);

            var session = BuildSession(new[] { 5, 15, 15 }, opponentShadows: opponentShadows, llm: llm);
            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);

            Assert.NotNull(capturedOpponent);
            Assert.True(capturedOpponent!.ContainsKey(ShadowStatType.Madness));
            Assert.Equal(8, capturedOpponent[ShadowStatType.Madness]);
        }

        // What: AC2 — OpponentContext receives OPPONENT shadows, not player shadows
        // Mutation: Fails if GameSession accidentally passes player shadows to OpponentContext
        [Fact]
        public async Task AC2_OpponentContext_ReceivesOpponentShadows_NotPlayerShadows()
        {
            Dictionary<ShadowStatType, int>? capturedOpponent = null;
            var playerShadows = CreateShadowTracker(dread: 20);
            var opponentShadows = CreateShadowTracker(dread: 7);
            var llm = new CapturingLlmAdapter(
                onOpponent: ctx => capturedOpponent = ctx.ShadowThresholds);

            var session = BuildSession(new[] { 5, 15, 15 },
                playerShadows: playerShadows,
                opponentShadows: opponentShadows,
                llm: llm);
            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);

            Assert.NotNull(capturedOpponent);
            // Must be opponent's value (7), not player's (20)
            Assert.Equal(7, capturedOpponent![ShadowStatType.Dread]);
        }

        // ================================================================
        // AC2 edge: OpponentContext null when no opponent shadows configured
        // ================================================================

        // What: AC2 edge — no opponent shadows means null thresholds on OpponentContext
        // Mutation: Fails if GameSession passes empty dict instead of null when no opponent shadows
        [Fact]
        public async Task AC2_NoOpponentShadows_OpponentContextThresholdsNull()
        {
            Dictionary<ShadowStatType, int>? capturedOpponent = null;
            bool wasCalled = false;
            var llm = new CapturingLlmAdapter(
                onOpponent: ctx => { capturedOpponent = ctx.ShadowThresholds; wasCalled = true; });

            var session = BuildSession(new[] { 5, 15, 15 }, llm: llm);
            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);

            Assert.True(wasCalled);
            Assert.Null(capturedOpponent);
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

        // What: AC4 — all shadow stat types appear in OpponentContext thresholds
        // Mutation: Fails if any shadow stat type is omitted from the opponent threshold dictionary
        [Fact]
        public async Task AC4_AllSixShadowStats_InOpponentContext()
        {
            Dictionary<ShadowStatType, int>? capturedOpponent = null;
            var opponentShadows = CreateShadowTracker(
                dread: 1, denial: 3, fixation: 5,
                madness: 7, overthinking: 9, horniness: 11);
            var llm = new CapturingLlmAdapter(
                onOpponent: ctx => capturedOpponent = ctx.ShadowThresholds);

            var session = BuildSession(new[] { 5, 15, 15 }, opponentShadows: opponentShadows, llm: llm);
            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);

            Assert.NotNull(capturedOpponent);
            Assert.Equal(1, capturedOpponent![ShadowStatType.Dread]);
            Assert.Equal(3, capturedOpponent[ShadowStatType.Denial]);
            Assert.Equal(5, capturedOpponent[ShadowStatType.Fixation]);
            Assert.Equal(7, capturedOpponent[ShadowStatType.Madness]);
            Assert.Equal(9, capturedOpponent[ShadowStatType.Overthinking]);
            Assert.Equal(11, capturedOpponent[ShadowStatType.Despair]);
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
            Dictionary<ShadowStatType, int>? capturedOpponent = null;
            var playerShadows = CreateShadowTracker(madness: 10, horniness: 3);
            var opponentShadows = CreateShadowTracker(madness: 2, horniness: 14);
            var llm = new CapturingLlmAdapter(
                onDeliver: ctx => capturedDelivery = ctx.ShadowThresholds,
                onOpponent: ctx => capturedOpponent = ctx.ShadowThresholds);

            var session = BuildSession(new[] { 5, 15, 15 },
                playerShadows: playerShadows,
                opponentShadows: opponentShadows,
                llm: llm);
            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);

            // Delivery = player shadows
            Assert.NotNull(capturedDelivery);
            Assert.Equal(10, capturedDelivery![ShadowStatType.Madness]);
            Assert.Equal(3, capturedDelivery[ShadowStatType.Despair]);

            // Opponent = opponent shadows
            Assert.NotNull(capturedOpponent);
            Assert.Equal(2, capturedOpponent![ShadowStatType.Madness]);
            Assert.Equal(14, capturedOpponent[ShadowStatType.Despair]);
        }

        // What: Edge — player shadows set but opponent not → delivery has thresholds, opponent null
        // Mutation: Fails if having player shadows incorrectly populates opponent thresholds
        [Fact]
        public async Task PlayerShadowsOnly_OpponentContextThresholdsNull()
        {
            Dictionary<ShadowStatType, int>? capturedDelivery = null;
            Dictionary<ShadowStatType, int>? capturedOpponent = null;
            var playerShadows = CreateShadowTracker(fixation: 9);
            var llm = new CapturingLlmAdapter(
                onDeliver: ctx => capturedDelivery = ctx.ShadowThresholds,
                onOpponent: ctx => capturedOpponent = ctx.ShadowThresholds);

            var session = BuildSession(new[] { 5, 15, 15 },
                playerShadows: playerShadows,
                llm: llm);
            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);

            Assert.NotNull(capturedDelivery);
            Assert.Equal(9, capturedDelivery![ShadowStatType.Fixation]);
            Assert.Null(capturedOpponent);
        }

        // What: Edge — opponent shadows set but player not → opponent has thresholds, delivery null
        // Mutation: Fails if having opponent shadows incorrectly populates delivery thresholds
        [Fact]
        public async Task OpponentShadowsOnly_DeliveryContextThresholdsNull()
        {
            Dictionary<ShadowStatType, int>? capturedDelivery = null;
            Dictionary<ShadowStatType, int>? capturedOpponent = null;
            var opponentShadows = CreateShadowTracker(denial: 11);
            var llm = new CapturingLlmAdapter(
                onDeliver: ctx => capturedDelivery = ctx.ShadowThresholds,
                onOpponent: ctx => capturedOpponent = ctx.ShadowThresholds);

            var session = BuildSession(new[] { 5, 15, 15 },
                opponentShadows: opponentShadows,
                llm: llm);
            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);

            Assert.Null(capturedDelivery);
            Assert.NotNull(capturedOpponent);
            Assert.Equal(11, capturedOpponent![ShadowStatType.Denial]);
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
            SessionShadowTracker? opponentShadows = null,
            ILlmAdapter? llm = null)
        {
            var config = new GameSessionConfig(clock: TestHelpers.MakeClock(), playerShadows: playerShadows,
                opponentShadows: opponentShadows);

            // dice[0] = ghost check (need >1 to avoid ghost), rest = roll
            var allDice = new int[diceValues.Length + 1];
            allDice[0] = 4; // ghost check safe
            Array.Copy(diceValues, 0, allDice, 1, diceValues.Length);

            return new GameSession(
                CreateProfile("TestPlayer"),
                CreateProfile("TestOpponent"),
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
        /// LLM adapter that captures DeliveryContext and OpponentContext for assertion.
        /// Returns minimal valid responses for all methods.
        /// </summary>
        private sealed class CapturingLlmAdapter : ILlmAdapter
        {
            private readonly Action<DeliveryContext>? _onDeliver;
            private readonly Action<OpponentContext>? _onOpponent;

            public CapturingLlmAdapter(
                Action<DeliveryContext>? onDeliver = null,
                Action<OpponentContext>? onOpponent = null)
            {
                _onDeliver = onDeliver;
                _onOpponent = onOpponent;
            }

            public Task<DialogueOption[]> GetDialogueOptionsAsync(DialogueContext context)
            {
                return Task.FromResult(new[]
                {
                    new DialogueOption(StatType.Charm, "Hey there"),
                    new DialogueOption(StatType.Wit, "Clever line")
                });
            }

            public Task<string> DeliverMessageAsync(DeliveryContext context)
            {
                _onDeliver?.Invoke(context);
                return Task.FromResult(context.ChosenOption.IntendedText);
            }

            public Task<OpponentResponse> GetOpponentResponseAsync(OpponentContext context)
            {
                _onOpponent?.Invoke(context);
                return Task.FromResult(new OpponentResponse("reply"));
            }

            public Task<string?> GetInterestChangeBeatAsync(InterestChangeContext context)
            {
                return Task.FromResult<string?>(null);
            }
            public System.Threading.Tasks.Task<string> ApplyHorninessOverlayAsync(string message, string instruction, string? opponentContext = null) => System.Threading.Tasks.Task.FromResult(message);
            public System.Threading.Tasks.Task<string> ApplyShadowCorruptionAsync(string message, string instruction, Pinder.Core.Stats.ShadowStatType shadow) => System.Threading.Tasks.Task.FromResult(message);
            public System.Threading.Tasks.Task<string> ApplyTrapOverlayAsync(string message, string trapInstruction, string trapName, string? opponentContext = null) => System.Threading.Tasks.Task.FromResult(message);
        }
    }
}
