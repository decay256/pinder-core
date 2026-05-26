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
    /// Additional spec-driven tests for Issue #139 Wave 0 Infrastructure Prerequisites.
    /// These tests target mutation-catching gaps in the existing test suite, covering
    /// edge cases, error conditions, and acceptance criteria from docs/specs/issue-139-spec.md.
    /// </summary>
    [Trait("Category", "Core")]
    public partial class Wave0SpecTests
    {
        #region Helpers

        private static StatBlock MakeStatBlock(
            int charm = 3, int rizz = 2, int honesty = 1, int chaos = 0, int wit = 4, int sa = 2,
            int madness = 2, int horniness = 0, int denial = 0, int fixation = 0, int dread = 5, int overthinking = 1)
        {
            return new StatBlock(
                new Dictionary<StatType, int>
                {
                    { StatType.Charm, charm },
                    { StatType.Rizz, rizz },
                    { StatType.Honesty, honesty },
                    { StatType.Chaos, chaos },
                    { StatType.Wit, wit },
                    { StatType.SelfAwareness, sa }
                },
                new Dictionary<ShadowStatType, int>
                {
                    { ShadowStatType.Madness, madness },
                    { ShadowStatType.Despair, horniness },
                    { ShadowStatType.Denial, denial },
                    { ShadowStatType.Fixation, fixation },
                    { ShadowStatType.Dread, dread },
                    { ShadowStatType.Overthinking, overthinking }
                });
        }

        private static CharacterProfile MakeProfile(string name)
        {
            var stats = MakeStatBlock(madness: 0, dread: 0, overthinking: 0);
            var timing = new TimingProfile(5, 1.0f, 0.0f, "neutral");
            return new CharacterProfile(stats, "system prompt", name, timing, 1);
        }

        private sealed class FixedDice : IDiceRoller
        {
            private readonly int _value;
            public FixedDice(int value) => _value = value;
            public int Roll(int sides) => _value;
        }

        private sealed class NullTrapRegistry : ITrapRegistry
        {
            public TrapDefinition? GetTrap(StatType stat) => null;
            public string? GetLlmInstruction(StatType stat) => null;
        }

        private sealed class StubLlmAdapter : ILlmAdapter
        {
            public Task<DialogueOption[]> GetDialogueOptionsAsync(DialogueContext context, System.Threading.CancellationToken ct = default)
                => Task.FromResult(new[] { new DialogueOption(StatType.Charm, "Test option") });
            public Task<string> DeliverMessageAsync(DeliveryContext context, System.Threading.CancellationToken ct = default)
                => Task.FromResult("delivered");
            public Task<OpponentResponse> GetOpponentResponseAsync(OpponentContext context, System.Threading.CancellationToken ct = default)
                => Task.FromResult(new OpponentResponse("reply"));
            public Task<string?> GetInterestChangeBeatAsync(InterestChangeContext context, System.Threading.CancellationToken ct = default)
                => Task.FromResult<string?>(null);
            public System.Threading.Tasks.Task<string> ApplyHorninessOverlayAsync(string message, string instruction, string? opponentContext = null, string? archetypeDirective = null, System.Threading.CancellationToken ct = default) => System.Threading.Tasks.Task.FromResult(message);
            public System.Threading.Tasks.Task<string> ApplyShadowCorruptionAsync(string message, string instruction, Pinder.Core.Stats.ShadowStatType shadow, string? archetypeDirective = null, System.Threading.CancellationToken ct = default) => System.Threading.Tasks.Task.FromResult(message);
            public System.Threading.Tasks.Task<string> ApplyTrapOverlayAsync(string message, string trapInstruction, string trapName, string? opponentContext = null, string? archetypeDirective = null, System.Threading.CancellationToken ct = default) => System.Threading.Tasks.Task.FromResult(message);
        }

        private sealed class TestFixedClock : IGameClock
        {
            public DateTimeOffset Now => DateTimeOffset.UtcNow;
            public void Advance(TimeSpan amount) { }
            public void AdvanceTo(DateTimeOffset target) { }
            public TimeOfDay GetTimeOfDay() => TimeOfDay.Morning;
            public int GetHorninessModifier() => -2;
        }

        #endregion
    }
}
