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
using Pinder.LlmAdapters;
using Xunit;

namespace Pinder.Core.Tests
{
    /// <summary>
    /// Tests for shadow growth events — §7 growth table in GameSession (#44).
    /// Maturity: Prototype (happy-path tests for key triggers).
    /// </summary>
    [Trait("Category", "Core")]
    public partial class ShadowGrowthEventTests
    {
        // ======================== Helpers ========================

        private static SessionShadowTracker MakeShadowTracker()
        {
            return new SessionShadowTracker(MakeStatBlock());
        }

        private static StatBlock MakeStatBlock(
            int charm = 3, int rizz = 2, int honesty = 1,
            int chaos = 0, int wit = 4, int sa = 2)
        {
            return new StatBlock(
                new Dictionary<StatType, int>
                {
                    { StatType.Charm, charm }, { StatType.Rizz, rizz }, { StatType.Honesty, honesty },
                    { StatType.Chaos, chaos }, { StatType.Wit, wit }, { StatType.SelfAwareness, sa }
                },
                new Dictionary<ShadowStatType, int>
                {
                    { ShadowStatType.Madness, 0 }, { ShadowStatType.Despair, 0 },
                    { ShadowStatType.Denial, 0 }, { ShadowStatType.Fixation, 0 },
                    { ShadowStatType.Dread, 0 }, { ShadowStatType.Overthinking, 0 }
                });
        }

        private static CharacterProfile MakeProfile(string name, StatBlock stats)
        {
            var timing = new TimingProfile(5, 1.0f, 0.0f, "neutral");
            return TestHelpers.MakeCharacterProfile(
                stats,
                "system prompt",
                name,
                timing,
                1,
                backstory: TestHelpers.MakeBackstory(),
                stakeLines: TestHelpers.MakeStakeLines(),
                psychiatricDiagnosis: TestHelpers.MakePsychiatricDiagnosis());
        }

        private static GameSession MakeSession(
            int[] diceValues,
            StatBlock? playerStats = null,
            StatBlock? dateeStats = null,
            SessionShadowTracker? shadows = null,
            DialogueOption[]? llmOptions = null,
            string? previousOpener = null,
            int? startingInterest = null)
        {
            playerStats = playerStats ?? MakeStatBlock();
            dateeStats = dateeStats ?? MakeStatBlock();

            var config = new GameSessionConfig(
                clock: TestHelpers.MakeClock(),
                playerShadows: shadows,
                previousOpener: previousOpener,
                startingInterest: startingInterest,
                rules: GameDefinition.PinderDefaults);

            var llm = llmOptions != null ? (ILlmAdapter)new CustomLlmAdapter(llmOptions) : new NullLlmAdapter();

            // Prepend horniness roll (1d10)
            var allDice = new int[diceValues.Length + 1];
            allDice[0] = 5;
            Array.Copy(diceValues, 0, allDice, 1, diceValues.Length);

            return new GameSession(
                MakeProfile("player", playerStats),
                MakeProfile("datee", dateeStats),
                llm,
                new QueueDice(allDice),
                new NullTrapRegistry(),
                config);
        }

        private static GameSession MakeSessionWithDice(
            QueueDice dice,
            StatBlock? playerStats = null,
            StatBlock? dateeStats = null,
            SessionShadowTracker? shadows = null,
            DialogueOption[]? llmOptions = null,
            string? previousOpener = null,
            int? startingInterest = null)
        {
            playerStats = playerStats ?? MakeStatBlock();
            dateeStats = dateeStats ?? MakeStatBlock();

            var config = new GameSessionConfig(
                clock: TestHelpers.MakeClock(),
                playerShadows: shadows,
                previousOpener: previousOpener,
                startingInterest: startingInterest,
                rules: GameDefinition.PinderDefaults);

            var llm = llmOptions != null ? (ILlmAdapter)new CustomLlmAdapter(llmOptions) : new NullLlmAdapter();

            // Prepend horniness roll via wrapper
            var wrappedDice = new PrependedDice(5, dice);

            return new GameSession(
                MakeProfile("player", playerStats),
                MakeProfile("datee", dateeStats),
                llm,
                wrappedDice,
                new NullTrapRegistry(),
                config);
        }

        /// <summary>Wraps a dice roller, returning a prepended value first.</summary>
        private sealed class PrependedDice : IDiceRoller
        {
            private int? _first;
            private readonly IDiceRoller _inner;
            public PrependedDice(int firstValue, IDiceRoller inner) { _first = firstValue; _inner = inner; }
            public int Roll(int sides) { if (_first.HasValue) { var v = _first.Value; _first = null; return v; } return _inner.Roll(sides); }
        }

        private static void ActivateTrap(GameSession session)
        {
            var trap = new TrapDefinition("test-trap", StatType.Charm, TrapEffect.Disadvantage, 1, 3, "test instruction", "clear", "");
            session.State.Traps.Activate(trap);
        }

        /// <summary>Dice that returns values from a queue.</summary>
        private sealed class QueueDice : IDiceRoller
        {
            private readonly Queue<int> _values;

            public QueueDice(int[] values)
            {
                _values = new Queue<int>(values);
            }

            public void Enqueue(params int[] values)
            {
                foreach (var v in values)
                    _values.Enqueue(v);
            }

            public int Roll(int sides)
            {
                if (_values.Count == 0)
                    return 10; // safe default
                return _values.Dequeue();
            }
        }

        /// <summary>LLM adapter that returns custom options.</summary>
        private sealed class CustomLlmAdapter : ILlmAdapter
        {
            private readonly DialogueOption[] _options;

            public CustomLlmAdapter(DialogueOption[] options) => _options = options;

            public Task<DialogueOption[]> GetDialogueOptionsAsync(DialogueContext context, System.Threading.CancellationToken ct = default)
                => Task.FromResult(_options);


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
