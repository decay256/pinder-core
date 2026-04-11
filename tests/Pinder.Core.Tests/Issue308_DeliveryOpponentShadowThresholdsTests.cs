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
    /// Issue #308: GameSession must pass shadowThresholds to DeliveryContext and OpponentContext,
    /// not just DialogueContext. Player shadows go to DeliveryContext, opponent shadows to OpponentContext.
    /// Maturity: Prototype (happy-path per AC).
    /// </summary>
    [Trait("Category", "Core")]
    public class Issue308_DeliveryOpponentShadowThresholdsTests
    {
        // AC: DeliveryContext receives player shadowThresholds
        [Fact]
        public async Task DeliveryContext_ReceivesPlayerShadowThresholds_WhenMadness8()
        {
            Dictionary<ShadowStatType, int>? capturedDelivery = null;
            var playerShadows = MakeShadowTracker(madness: 8);
            var llm = new FullCapturingLlmAdapter(
                onDeliver: ctx => capturedDelivery = ctx.ShadowThresholds);

            var session = MakeSession(new[] { 5, 15, 15 }, playerShadows, null, llm);
            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);

            Assert.NotNull(capturedDelivery);
            Assert.True(capturedDelivery!.ContainsKey(ShadowStatType.Madness));
            Assert.Equal(8, capturedDelivery[ShadowStatType.Madness]);
        }

        // AC: OpponentContext receives opponent shadowThresholds
        [Fact]
        public async Task OpponentContext_ReceivesOpponentShadowThresholds_WhenMadness8()
        {
            Dictionary<ShadowStatType, int>? capturedOpponent = null;
            var opponentShadows = MakeShadowTracker(madness: 8);
            var llm = new FullCapturingLlmAdapter(
                onOpponent: ctx => capturedOpponent = ctx.ShadowThresholds);

            var session = MakeSession(new[] { 5, 15, 15 }, null, opponentShadows, llm);
            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);

            Assert.NotNull(capturedOpponent);
            Assert.True(capturedOpponent!.ContainsKey(ShadowStatType.Madness));
            Assert.Equal(8, capturedOpponent[ShadowStatType.Madness]);
        }

        // Edge: DeliveryContext gets player shadows, OpponentContext gets opponent shadows (different values)
        [Fact]
        public async Task DeliveryAndOpponent_GetDifferentShadows()
        {
            Dictionary<ShadowStatType, int>? capturedDelivery = null;
            Dictionary<ShadowStatType, int>? capturedOpponent = null;
            var playerShadows = MakeShadowTracker(madness: 10, dread: 5);
            var opponentShadows = MakeShadowTracker(madness: 3, dread: 14);
            var llm = new FullCapturingLlmAdapter(
                onDeliver: ctx => capturedDelivery = ctx.ShadowThresholds,
                onOpponent: ctx => capturedOpponent = ctx.ShadowThresholds);

            var session = MakeSession(new[] { 5, 15, 15 }, playerShadows, opponentShadows, llm);
            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);

            // Player shadows in delivery
            Assert.NotNull(capturedDelivery);
            Assert.Equal(10, capturedDelivery![ShadowStatType.Madness]);
            Assert.Equal(5, capturedDelivery[ShadowStatType.Dread]);

            // Opponent shadows in opponent context
            Assert.NotNull(capturedOpponent);
            Assert.Equal(3, capturedOpponent![ShadowStatType.Madness]);
            Assert.Equal(14, capturedOpponent[ShadowStatType.Dread]);
        }

        // Edge: No player shadows -> DeliveryContext.ShadowThresholds is null
        [Fact]
        public async Task NoPlayerShadows_DeliveryContextThresholdsNull()
        {
            Dictionary<ShadowStatType, int>? capturedDelivery = null;
            bool called = false;
            var llm = new FullCapturingLlmAdapter(
                onDeliver: ctx => { capturedDelivery = ctx.ShadowThresholds; called = true; });

            var session = MakeSession(new[] { 5, 15, 15 }, null, null, llm);
            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);

            Assert.True(called);
            Assert.Null(capturedDelivery);
        }

        // Edge: No opponent shadows -> OpponentContext.ShadowThresholds is null
        [Fact]
        public async Task NoOpponentShadows_OpponentContextThresholdsNull()
        {
            Dictionary<ShadowStatType, int>? capturedOpponent = null;
            bool called = false;
            var llm = new FullCapturingLlmAdapter(
                onOpponent: ctx => { capturedOpponent = ctx.ShadowThresholds; called = true; });

            var session = MakeSession(new[] { 5, 15, 15 }, null, null, llm);
            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);

            Assert.True(called);
            Assert.Null(capturedOpponent);
        }

        // AC: All shadow stats pass through to DeliveryContext
        [Fact]
        public async Task AllShadowStats_PassedToDeliveryContext()
        {
            Dictionary<ShadowStatType, int>? capturedDelivery = null;
            var playerShadows = MakeShadowTracker(
                dread: 14, denial: 7, fixation: 3,
                madness: 10, overthinking: 5, horniness: 12);
            var llm = new FullCapturingLlmAdapter(
                onDeliver: ctx => capturedDelivery = ctx.ShadowThresholds);

            var session = MakeSession(new[] { 5, 15, 15 }, playerShadows, null, llm);
            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);

            Assert.NotNull(capturedDelivery);
            Assert.Equal(14, capturedDelivery![ShadowStatType.Dread]);
            Assert.Equal(7, capturedDelivery[ShadowStatType.Denial]);
            Assert.Equal(3, capturedDelivery[ShadowStatType.Fixation]);
            Assert.Equal(10, capturedDelivery[ShadowStatType.Madness]);
            Assert.Equal(5, capturedDelivery[ShadowStatType.Overthinking]);
            Assert.Equal(12, capturedDelivery[ShadowStatType.Despair]);
        }

        // ============ Helpers ============

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
            var timing = new TimingProfile(5, 1.0f, 0.0f, "neutral");
            return new CharacterProfile(MakeStatBlock(), "system prompt", name, timing, 1);
        }

        private static GameSession MakeSession(
            int[] diceValues,
            SessionShadowTracker? playerShadows,
            SessionShadowTracker? opponentShadows,
            ILlmAdapter llm)
        {
            var config = new GameSessionConfig(clock: TestHelpers.MakeClock(), playerShadows: playerShadows,
                opponentShadows: opponentShadows);

            // dice[0] = ghost check, rest = roll dice
            var allDice = new int[diceValues.Length + 1];
            allDice[0] = 5; // ghost check (not triggered)
            Array.Copy(diceValues, 0, allDice, 1, diceValues.Length);

            return new GameSession(
                MakeProfile("player"),
                MakeProfile("opponent"),
                llm,
                new SafeQueueDice(allDice),
                new NullTrapRegistry(),
                config);
        }

        private sealed class SafeQueueDice : IDiceRoller
        {
            private readonly Queue<int> _values;
            public SafeQueueDice(int[] values) => _values = new Queue<int>(values);
            public int Roll(int sides) => _values.Count > 0 ? _values.Dequeue() : 10;
        }

        /// <summary>
        /// LLM adapter that captures DeliveryContext and OpponentContext for assertion.
        /// </summary>
        private sealed class FullCapturingLlmAdapter : ILlmAdapter
        {
            private readonly Action<DeliveryContext>? _onDeliver;
            private readonly Action<OpponentContext>? _onOpponent;

            public FullCapturingLlmAdapter(
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
            public System.Threading.Tasks.Task<string> ApplyHorninessOverlayAsync(string message, string instruction) => System.Threading.Tasks.Task.FromResult(message);
        }
    }
}
