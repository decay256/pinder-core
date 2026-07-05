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
using Pinder.Core.TestCommon;
using Xunit;

namespace Pinder.Core.Tests
{
    public partial class ShadowGrowthSpecTests
    {
        // =====================================================================
        // Helpers — test-only utilities (no production code copied)
        // =====================================================================

        private static SessionShadowTracker MakeTracker()
            => new SessionShadowTracker(Stats());

        private static StatBlock Stats(
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

        private static CharacterProfile MakeProfile(string name, StatBlock stats)
            => new CharacterProfile(stats, "system prompt", name, new TimingProfile(5, 1.0f, 0.0f, "neutral"), 1);

        private static TestDice Dice(params int[] values) => new TestDice(values);

        private static GameSession BuildSession(
            TestDice? dice = null,
            StatBlock? playerStats = null,
            StatBlock? dateeStats = null,
            SessionShadowTracker? shadows = null,
            DialogueOption[]? options = null,
            string? previousOpener = null,
            int? startingInterest = null)
        {
            return BuildSessionWithLlm(
                dice: dice ?? Dice(15, 50),
                llm: options != null ? new StubLlmAdapter(options) : null,
                playerStats: playerStats,
                dateeStats: dateeStats,
                shadows: shadows,
                previousOpener: previousOpener,
                startingInterest: startingInterest);
        }

        private static GameSession BuildSessionWithLlm(
            TestDice dice,
            ILlmAdapter? llm = null,
            StatBlock? playerStats = null,
            StatBlock? dateeStats = null,
            SessionShadowTracker? shadows = null,
            string? previousOpener = null,
            int? startingInterest = null)
        {
            playerStats ??= Stats();
            dateeStats ??= Stats();
            llm ??= new NullLlmAdapter();

            var config = new GameSessionConfig(clock: TestHelpers.MakeClock(), playerShadows: shadows,
                previousOpener: previousOpener,
                startingInterest: startingInterest);

            var wrappedDice = new PrependedDice(5, dice);

            return new GameSession(
                MakeProfile("player", playerStats),
                MakeProfile("datee", dateeStats),
                llm,
                wrappedDice,
                new NullTrapRegistry(),
                config);
        }

        private sealed class PrependedDice : IDiceRoller
        {
            private int? _first;
            private readonly IDiceRoller _inner;
            public PrependedDice(int firstValue, IDiceRoller inner) { _first = firstValue; _inner = inner; }
            public int Roll(int sides) { if (_first.HasValue) { var v = _first.Value; _first = null; return v; } return _inner.Roll(sides); }
        }

        /// <summary>Deterministic dice for tests — dequeues values in order.</summary>
        private sealed class TestDice : IDiceRoller
        {
            private readonly Queue<int> _values;

            public TestDice(int[] values) => _values = new Queue<int>(values);

            public int Roll(int sides)
                => _values.Count > 0 ? _values.Dequeue() : 10;
        }

        /// <summary>LLM adapter that returns a Tell on the datee's response for a specific stat.</summary>
        private sealed class TellLlmAdapter : ILlmAdapter
        {
            private readonly StatType _tellStat;
            public TellLlmAdapter(StatType tellStat) => _tellStat = tellStat;

            public Task<DialogueOption[]> GetDialogueOptionsAsync(DialogueContext context, System.Threading.CancellationToken ct = default)
            {
                var options = new[]
                {
                    new DialogueOption(StatType.Charm, "Hey, you come here often?"),
                    new DialogueOption(StatType.Honesty, "I have to be real with you..."),
                    new DialogueOption(StatType.Wit, "Did you know that penguins propose with pebbles?"),
                    new DialogueOption(StatType.Chaos, "I once ate a whole pizza in a bouncy castle.")
                };
                return Task.FromResult(options);
            }
            public Task<DateeResponse> GetDateeResponseAsync(DateeContext context, System.Threading.CancellationToken ct = default)
                => Task.FromResult(new DateeResponse("...",
                    detectedTell: new Tell(_tellStat, $"Tell on {_tellStat}")));
            public Task<string?> GetInterestChangeBeatAsync(InterestChangeContext context, System.Threading.CancellationToken ct = default)
                => Task.FromResult<string?>(null);
            public System.Threading.Tasks.Task<string> ApplyHorninessOverlayAsync(string message, string instruction, string? dateeContext = null, string? archetypeDirective = null, System.Threading.CancellationToken ct = default) => System.Threading.Tasks.Task.FromResult(message);
            public System.Threading.Tasks.Task<string> ApplyShadowCorruptionAsync(string message, string instruction, Pinder.Core.Stats.ShadowStatType shadow, string? archetypeDirective = null, System.Threading.CancellationToken ct = default) => System.Threading.Tasks.Task.FromResult(message);
            public System.Threading.Tasks.Task<string> ApplyTrapOverlayAsync(string message, string trapInstruction, string trapName, string? dateeContext = null, string? archetypeDirective = null, System.Threading.CancellationToken ct = default) => System.Threading.Tasks.Task.FromResult(message);
        
        public System.Threading.Tasks.Task<string> ApplyFailureCorruptionAsync(string message, string instruction, Pinder.Core.Stats.StatType stat, Pinder.Core.Rolls.FailureTier tier, string? archetypeDirective = null, System.Threading.CancellationToken ct = default)
        {
            return System.Threading.Tasks.Task.FromResult(message);
        }
}

        /// <summary>LLM adapter that rotates through different option sets per turn.</summary>
        private sealed class RotatingLlmAdapter : ILlmAdapter
        {
            private readonly DialogueOption[][] _optionSets;
            private int _call;
            public RotatingLlmAdapter(DialogueOption[][] optionSets) => _optionSets = optionSets;

            public Task<DialogueOption[]> GetDialogueOptionsAsync(DialogueContext context, System.Threading.CancellationToken ct = default)
            {
                var idx = _call < _optionSets.Length ? _call : _optionSets.Length - 1;
                _call++;
                return Task.FromResult(_optionSets[idx]);
            }
            public Task<DateeResponse> GetDateeResponseAsync(DateeContext context, System.Threading.CancellationToken ct = default)
                => Task.FromResult(new DateeResponse("..."));
            public Task<string?> GetInterestChangeBeatAsync(InterestChangeContext context, System.Threading.CancellationToken ct = default)
                => Task.FromResult<string?>(null);
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