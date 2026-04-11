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
    /// Issue #307: GameSession must store raw shadow values (not tiers 0-3) in shadowThresholds.
    /// The bug was that tiers (0-3) were stored, but BuildShadowTaintBlock checks raw values (>5),
    /// so taint never fired. Fix: store raw values.
    /// Maturity: Prototype (happy-path per AC).
    /// </summary>
    public class Issue307_ShadowTaintRawValueTests
    {
        // ============== AC: Madness=8 (T1) → raw value 8 passed in context ==============

        // Mutation: would catch if GameSession stores tier (1) instead of raw value (8)
        [Fact]
        public async Task Madness8_ContextReceivesRawValue8_NotTier1()
        {
            Dictionary<ShadowStatType, int>? captured = null;
            var shadows = MakeShadowTracker(madness: 8);
            var llm = new CapturingLlmAdapter(ctx => captured = ctx.ShadowThresholds);

            var session = MakeSessionWithLlm(new[] { 15, 50 }, shadows, llm);
            await session.StartTurnAsync();

            Assert.NotNull(captured);
            Assert.True(captured!.ContainsKey(ShadowStatType.Madness));
            // Raw value must be 8, not tier 1
            Assert.Equal(8, captured[ShadowStatType.Madness]);
        }

        // ============== AC: Madness=3 (T0) → raw value 3 passed in context ==============

        // Mutation: would catch if GameSession omits T0 shadows or stores 0 instead of 3
        [Fact]
        public async Task Madness3_ContextReceivesRawValue3()
        {
            Dictionary<ShadowStatType, int>? captured = null;
            var shadows = MakeShadowTracker(madness: 3);
            var llm = new CapturingLlmAdapter(ctx => captured = ctx.ShadowThresholds);

            var session = MakeSessionWithLlm(new[] { 15, 50 }, shadows, llm);
            await session.StartTurnAsync();

            Assert.NotNull(captured);
            Assert.Equal(3, captured![ShadowStatType.Madness]);
        }

        // ============== Edge: All six shadow stats pass raw values ==============

        // Mutation: would catch if only some shadow stats are converted to raw (e.g., only Madness)
        [Fact]
        public async Task AllShadowStats_PassRawValues()
        {
            Dictionary<ShadowStatType, int>? captured = null;
            var shadows = MakeShadowTracker(
                dread: 14, denial: 7, fixation: 3,
                madness: 10, overthinking: 5, horniness: 12);
            var llm = new CapturingLlmAdapter(ctx => captured = ctx.ShadowThresholds);

            var session = MakeSessionWithLlm(new[] { 15, 50 }, shadows, llm);
            await session.StartTurnAsync();

            Assert.NotNull(captured);
            Assert.Equal(14, captured![ShadowStatType.Dread]);
            Assert.Equal(7, captured[ShadowStatType.Denial]);
            Assert.Equal(3, captured[ShadowStatType.Fixation]);
            Assert.Equal(10, captured[ShadowStatType.Madness]);
            Assert.Equal(5, captured[ShadowStatType.Overthinking]);
            Assert.Equal(12, captured[ShadowStatType.Despair]);
        }

        // ============== Edge: Zero shadow value passes as 0 ==============

        // Mutation: would catch if zero shadows are filtered out of the dictionary
        [Fact]
        public async Task ZeroShadow_StillPassedInContext()
        {
            Dictionary<ShadowStatType, int>? captured = null;
            var shadows = MakeShadowTracker(madness: 0);
            var llm = new CapturingLlmAdapter(ctx => captured = ctx.ShadowThresholds);

            var session = MakeSessionWithLlm(new[] { 15, 50 }, shadows, llm);
            await session.StartTurnAsync();

            Assert.NotNull(captured);
            Assert.Equal(0, captured![ShadowStatType.Madness]);
        }

        // ============== Edge: High T3 value (18+) passes raw, not capped at 3 ==============

        // Mutation: would catch if GetThresholdLevel (max 3) is used instead of raw value
        [Fact]
        public async Task T3Shadow_PassesRawValue_NotCappedAt3()
        {
            Dictionary<ShadowStatType, int>? captured = null;
            var shadows = MakeShadowTracker(dread: 22);
            var llm = new CapturingLlmAdapter(ctx => captured = ctx.ShadowThresholds);

            var session = MakeSessionWithLlm(new[] { 15, 50 }, shadows, llm);
            await session.StartTurnAsync();

            Assert.NotNull(captured);
            // Tier would be 3, but raw value is 22
            Assert.Equal(22, captured![ShadowStatType.Dread]);
        }

        // ============== AC: T3 mechanical effects still work with raw values ==============

        // Mutation: would catch if Denial T3 check breaks after switching from tier >= 3 to raw >= 18
        [Fact]
        public async Task DenialT3_StillRemovesHonestyOptions_WithRawValues()
        {
            var shadows = MakeShadowTracker(denial: 18);
            var options = new[]
            {
                new DialogueOption(StatType.Charm, "Hey"),
                new DialogueOption(StatType.Honesty, "Truth"),
                new DialogueOption(StatType.Wit, "Clever")
            };

            var session = MakeSession(new[] { 15, 50 }, shadows, llmOptions: options);
            var turn = await session.StartTurnAsync();

            Assert.DoesNotContain(turn.Options, o => o.Stat == StatType.Honesty);
        }

        // Mutation: would catch if Denial T3 threshold changed from 18 to something else
        [Fact]
        public async Task Denial17_DoesNotRemoveHonestyOptions()
        {
            var shadows = MakeShadowTracker(denial: 17);
            var options = new[]
            {
                new DialogueOption(StatType.Charm, "Hey"),
                new DialogueOption(StatType.Honesty, "Truth"),
                new DialogueOption(StatType.Wit, "Clever")
            };

            var session = MakeSession(new[] { 15, 50 }, shadows, llmOptions: options);
            var turn = await session.StartTurnAsync();

            Assert.Contains(turn.Options, o => o.Stat == StatType.Honesty);
        }

        // ============== AC: No shadow tracker → null thresholds ==============

        // Mutation: would catch if null shadow tracker produces empty dict instead of null
        [Fact]
        public async Task NoShadowTracker_ContextThresholdsAreNull()
        {
            Dictionary<ShadowStatType, int>? captured = null;
            bool called = false;
            var llm = new CapturingLlmAdapter(ctx =>
            {
                captured = ctx.ShadowThresholds;
                called = true;
            });

            var session = MakeSessionWithLlm(new[] { 15, 50 }, null, llm);
            await session.StartTurnAsync();

            Assert.True(called);
            Assert.Null(captured);
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
            SessionShadowTracker? shadows,
            DialogueOption[]? llmOptions = null)
        {
            var config = new GameSessionConfig(clock: TestHelpers.MakeClock(), playerShadows: shadows);
            ILlmAdapter llm = llmOptions != null
                ? new CustomOptionsLlmAdapter(llmOptions)
                : (ILlmAdapter)new NullLlmAdapter();

            var allDice = new int[diceValues.Length + 1];
            allDice[0] = 5; // ghost check dice
            Array.Copy(diceValues, 0, allDice, 1, diceValues.Length);

            return new GameSession(
                MakeProfile("player"),
                MakeProfile("opponent"),
                llm,
                new SafeQueueDice(allDice),
                new NullTrapRegistry(),
                config);
        }

        private static GameSession MakeSessionWithLlm(
            int[] diceValues,
            SessionShadowTracker? shadows,
            ILlmAdapter llm)
        {
            var config = new GameSessionConfig(clock: TestHelpers.MakeClock(), playerShadows: shadows);
            var allDice = new int[diceValues.Length + 1];
            allDice[0] = 5;
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

        private sealed class CustomOptionsLlmAdapter : ILlmAdapter
        {
            private readonly DialogueOption[] _options;
            public CustomOptionsLlmAdapter(DialogueOption[] options) => _options = options;
            public Task<DialogueOption[]> GetDialogueOptionsAsync(DialogueContext context)
                => Task.FromResult(_options);
            public Task<string> DeliverMessageAsync(DeliveryContext context)
                => Task.FromResult(context.ChosenOption.IntendedText);
            public Task<OpponentResponse> GetOpponentResponseAsync(OpponentContext context)
                => Task.FromResult(new OpponentResponse("..."));
            public Task<string?> GetInterestChangeBeatAsync(InterestChangeContext context)
                => Task.FromResult<string?>(null);
        }

        private sealed class CapturingLlmAdapter : ILlmAdapter
        {
            private readonly Action<DialogueContext> _onGetOptions;
            public CapturingLlmAdapter(Action<DialogueContext> onGetOptions) => _onGetOptions = onGetOptions;
            public Task<DialogueOption[]> GetDialogueOptionsAsync(DialogueContext context)
            {
                _onGetOptions(context);
                return Task.FromResult(new[]
                {
                    new DialogueOption(StatType.Charm, "Hey"),
                    new DialogueOption(StatType.Wit, "Clever")
                });
            }
            public Task<string> DeliverMessageAsync(DeliveryContext context)
                => Task.FromResult(context.ChosenOption.IntendedText);
            public Task<OpponentResponse> GetOpponentResponseAsync(OpponentContext context)
                => Task.FromResult(new OpponentResponse("..."));
            public Task<string?> GetInterestChangeBeatAsync(InterestChangeContext context)
                => Task.FromResult<string?>(null);
        }
    }
}
