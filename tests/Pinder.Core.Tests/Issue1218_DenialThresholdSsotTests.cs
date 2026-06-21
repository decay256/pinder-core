using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;
using Pinder.Core.Characters;
using Pinder.Core.Conversation;
using Pinder.Core.Interfaces;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;
using Pinder.Core.Traps;
using Pinder.Core.TestCommon;

namespace Pinder.Core.Tests
{
    public class Issue1218_DenialThresholdSsotTests
    {
        [Fact]
        public void DrawRandomStats_DenialAt17_ContainsHonesty()
        {
            var pool = new[] { StatType.Charm, StatType.Rizz, StatType.Honesty, StatType.Chaos, StatType.Wit, StatType.SelfAwareness };
            var thresholds = new Dictionary<ShadowStatType, int>
            {
                { ShadowStatType.Denial, 17 }
            };
            var rng = new Random(12345);

            var drawn = OptionFilterEngine.DrawRandomStats(pool, pool.Length, thresholds, rng);

            Assert.Contains(StatType.Honesty, drawn);
        }

        [Fact]
        public void DrawRandomStats_DenialAt12_ContainsHonesty()
        {
            var pool = new[] { StatType.Charm, StatType.Rizz, StatType.Honesty, StatType.Chaos, StatType.Wit, StatType.SelfAwareness };
            var thresholds = new Dictionary<ShadowStatType, int>
            {
                { ShadowStatType.Denial, 12 }
            };
            var rng = new Random(12345);

            var drawn = OptionFilterEngine.DrawRandomStats(pool, pool.Length, thresholds, rng);

            Assert.Contains(StatType.Honesty, drawn);
        }

        [Fact]
        public void DrawRandomStats_DenialAt18_DoesNotContainHonesty()
        {
            var pool = new[] { StatType.Charm, StatType.Rizz, StatType.Honesty, StatType.Chaos, StatType.Wit, StatType.SelfAwareness };
            var thresholds = new Dictionary<ShadowStatType, int>
            {
                { ShadowStatType.Denial, 18 }
            };
            var rng = new Random(12345);

            var drawn = OptionFilterEngine.DrawRandomStats(pool, pool.Length, thresholds, rng);

            Assert.DoesNotContain(StatType.Honesty, drawn);
        }

        [Fact]
        public void DrawRandomStats_DenialAt6_ContainsHonesty()
        {
            var pool = new[] { StatType.Charm, StatType.Rizz, StatType.Honesty, StatType.Chaos, StatType.Wit, StatType.SelfAwareness };
            var thresholds = new Dictionary<ShadowStatType, int>
            {
                { ShadowStatType.Denial, 6 }
            };
            var rng = new Random(12345);

            var drawn = OptionFilterEngine.DrawRandomStats(pool, pool.Length, thresholds, rng);

            Assert.Contains(StatType.Honesty, drawn);
        }

        [Fact]
        public async Task StartTurnAsync_DenialAt17_DialogueContextAvailableStatsContainsHonesty()
        {
            var shadows = MakeShadowTracker(denial: 17);
            var llm = new DialogueContextCapturingLlmAdapter();
            var session = MakeSessionWithLlm(diceValues: new[] { 15, 50 }, shadows: shadows, llm: llm);

            await session.StartTurnAsync();

            Assert.NotNull(llm.CapturedContext);
            Assert.NotNull(llm.CapturedContext.AvailableStats);
            Assert.Contains(StatType.Honesty, llm.CapturedContext.AvailableStats);
        }

        [Fact]
        public async Task StartTurnAsync_DenialAt18_DialogueContextAvailableStatsDoesNotContainHonesty()
        {
            var shadows = MakeShadowTracker(denial: 18);
            var llm = new DialogueContextCapturingLlmAdapter();
            var session = MakeSessionWithLlm(diceValues: new[] { 15, 50 }, shadows: shadows, llm: llm);

            await session.StartTurnAsync();

            Assert.NotNull(llm.CapturedContext);
            Assert.NotNull(llm.CapturedContext.AvailableStats);
            Assert.DoesNotContain(StatType.Honesty, llm.CapturedContext.AvailableStats);
        }

        [Fact]
        public void DrawRandomStats_DenialAt18_EmitsTrace()
        {
            var pool = new[] { StatType.Charm, StatType.Rizz, StatType.Honesty, StatType.Chaos, StatType.Wit, StatType.SelfAwareness };
            var thresholds = new Dictionary<ShadowStatType, int>
            {
                { ShadowStatType.Denial, 18 }
            };

            ShadowFilterTraceEvent? captured = null;
            Action<ShadowFilterTraceEvent> onTrace = ev => captured = ev;

            var drawn = OptionFilterEngine.DrawRandomStats(pool, pool.Length, thresholds, new Random(12345), onTrace);

            Assert.NotNull(captured);
            Assert.Equal(ShadowStatType.Denial, captured!.ShadowStat);
            Assert.Equal(18, captured.RawValue);
            Assert.Equal(3, captured.ComputedTier);
            Assert.Contains(StatType.Honesty, captured.RemovedStats);
            Assert.Equal("pre_llm_stat_draw", captured.SourcePath);
        }

        [Fact]
        public void ApplyT3Filters_DenialAt18_EmitsTrace()
        {
            var options = new[]
            {
                new DialogueOption(StatType.Charm, "Hi"),
                new DialogueOption(StatType.Honesty, "Truth"),
                new DialogueOption(StatType.Wit, "Joke")
            };
            var thresholds = new Dictionary<ShadowStatType, int>
            {
                { ShadowStatType.Denial, 18 }
            };

            ShadowFilterTraceEvent? captured = null;
            Action<ShadowFilterTraceEvent> onTrace = ev => captured = ev;

            var filtered = OptionFilterEngine.ApplyT3Filters(options, thresholds, lastStatUsed: null, dice: new QueueDice(new[] { 1 }), onTrace);

            Assert.NotNull(captured);
            Assert.Equal(ShadowStatType.Denial, captured!.ShadowStat);
            Assert.Equal(18, captured.RawValue);
            Assert.Equal(3, captured.ComputedTier);
            Assert.Contains(StatType.Honesty, captured.RemovedStats);
            Assert.Equal("post_llm_filter", captured.SourcePath);
        }

        // ---- Test Helpers ----

        private static SessionShadowTracker MakeShadowTracker(int denial)
        {
            var stats = new StatBlock(
                new Dictionary<StatType, int>
                {
                    { StatType.Charm, 2 }, { StatType.Rizz, 2 }, { StatType.Honesty, 2 },
                    { StatType.Chaos, 2 }, { StatType.Wit, 2 }, { StatType.SelfAwareness, 2 }
                },
                new Dictionary<ShadowStatType, int>
                {
                    { ShadowStatType.Dread, 0 }, { ShadowStatType.Denial, denial },
                    { ShadowStatType.Fixation, 0 }, { ShadowStatType.Madness, 0 },
                    { ShadowStatType.Overthinking, 0 }, { ShadowStatType.Despair, 0 }
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

        private static GameSession MakeSessionWithLlm(
            int[] diceValues,
            SessionShadowTracker? shadows,
            ILlmAdapter llm)
        {
            var config = new GameSessionConfig(
                clock: TestHelpers.MakeClock(),
                playerShadows: shadows,
                maxDialogueOptions: 6);

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

        // ---- Test Doubles ----

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

        private sealed class DialogueContextCapturingLlmAdapter : ILlmAdapter
        {
            public DialogueContext? CapturedContext { get; private set; }

            public Task<DialogueOption[]> GetDialogueOptionsAsync(DialogueContext context, System.Threading.CancellationToken ct = default)
            {
                CapturedContext = context;
                return Task.FromResult(new[]
                {
                    new DialogueOption(StatType.Charm, "Hey"),
                    new DialogueOption(StatType.Wit, "Clever")
                });
            }

            public Task<DateeResponse> GetDateeResponseAsync(DateeContext context, System.Threading.CancellationToken ct = default)
                => Task.FromResult(new DateeResponse("..."));

            public Task<string?> GetInterestChangeBeatAsync(InterestChangeContext context, System.Threading.CancellationToken ct = default)
                => Task.FromResult<string?>(null);

            public Task<string> ApplyHorninessOverlayAsync(string message, string instruction, string? dateeContext = null, string? archetypeDirective = null, System.Threading.CancellationToken ct = default)
                => Task.FromResult(message);

            public Task<string> ApplyShadowCorruptionAsync(string message, string instruction, ShadowStatType shadow, string? archetypeDirective = null, System.Threading.CancellationToken ct = default)
                => Task.FromResult(message);

            public Task<string> ApplyTrapOverlayAsync(string message, string trapInstruction, string trapName, string? dateeContext = null, string? archetypeDirective = null, System.Threading.CancellationToken ct = default)
                => Task.FromResult(message);
        }
    }
}