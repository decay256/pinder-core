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

namespace Pinder.Core.Tests.Integration
{
    /// <summary>
    /// End-to-end integration tests running full GameSession conversations.
    /// Verifies the complete rules stack: interest deltas with success/failure scaling,
    /// momentum streaks, combo detection, trap activation and recovery, shadow growth
    /// events, XP accumulation, and game outcome resolution.
    ///
    /// All randomness is controlled via FixedDice; no real LLM API calls are made.
    /// </summary>
    [Trait("Category", "Core")]
    public partial class FullConversationIntegrationTest
    {
        // ---- Helper methods ----

        private static StatBlock CreateGeraldStats()
        {
            return new StatBlock(
                new Dictionary<StatType, int>
                {
                    { StatType.Charm, 13 },
                    { StatType.Wit, 3 },
                    { StatType.Honesty, 3 },
                    { StatType.Chaos, 2 },
                    { StatType.SelfAwareness, 4 },
                    { StatType.Rizz, 2 }
                },
                new Dictionary<ShadowStatType, int>
                {
                    { ShadowStatType.Madness, 0 },
                    { ShadowStatType.Despair, 0 },
                    { ShadowStatType.Denial, 0 },
                    { ShadowStatType.Fixation, 0 },
                    { ShadowStatType.Dread, 0 },
                    { ShadowStatType.Overthinking, 0 }
                });
        }

        private static StatBlock CreateVelvetStats()
        {
            // Stat values adjusted by -3 from old values to preserve DC values with new DC base 16.
            // Old DC = 13 + stat, New DC = 16 + (stat-3) = 13 + stat → same DC.
            return new StatBlock(
                new Dictionary<StatType, int>
                {
                    { StatType.Chaos, 11 },
                    { StatType.Honesty, 7 },
                    { StatType.Charm, 2 },
                    { StatType.SelfAwareness, 2 },
                    { StatType.Wit, 1 },
                    { StatType.Rizz, 1 }
                },
                new Dictionary<ShadowStatType, int>
                {
                    { ShadowStatType.Madness, 0 },
                    { ShadowStatType.Despair, 0 },
                    { ShadowStatType.Denial, 0 },
                    { ShadowStatType.Fixation, 0 },
                    { ShadowStatType.Dread, 0 },
                    { ShadowStatType.Overthinking, 0 }
                });
        }

        private static DialogueOption Opt(StatType stat, string text = "Test option")
        {
            return new DialogueOption(stat, text);
        }

        // ---- Test doubles ----

        private sealed class FixedDice : IDiceRoller
        {
            private readonly Queue<int> _values;

            public FixedDice(params int[] values)
            {
                _values = new Queue<int>(values);
            }

            public int Roll(int sides)
            {
                if (_values.Count == 0)
                    throw new InvalidOperationException(
                        $"FixedDice: no more values in queue (requested d{sides}).");
                return _values.Dequeue();
            }
        }

        private sealed class NullTrapRegistry : ITrapRegistry
        {
            public TrapDefinition? GetTrap(StatType stat) => null;
            public string? GetLlmInstruction(StatType stat) => null;
        }

        private sealed class TestTrapRegistry : ITrapRegistry
        {
            private readonly TrapDefinition _chaosTrap = new TrapDefinition(
                id: "unhinged",
                stat: StatType.Chaos,
                effect: TrapEffect.Disadvantage,
                effectValue: 0,
                durationTurns: 3,
                llmInstruction: "You're unhinged now",
                clearMethod: "Recover",
                nat1Bonus: "Extra chaos");

            public TrapDefinition? GetTrap(StatType stat)
            {
                return stat == StatType.Chaos ? _chaosTrap : null;
            }

            public string? GetLlmInstruction(StatType stat) => null;
        }

        private sealed class ScriptedLlmAdapter : ILlmAdapter
        {
            private readonly Queue<DialogueOption[]> _optionSets;

            public ScriptedLlmAdapter(IEnumerable<DialogueOption[]> optionSets)
            {
                _optionSets = new Queue<DialogueOption[]>(optionSets);
            }

            public Task<DialogueOption[]> GetDialogueOptionsAsync(DialogueContext context, System.Threading.CancellationToken ct = default)
            {
                if (_optionSets.Count == 0)
                    throw new InvalidOperationException(
                        "ScriptedLlmAdapter: no more option sets in queue.");
                return Task.FromResult(_optionSets.Dequeue());
            }

            public Task<string> DeliverMessageAsync(DeliveryContext context, System.Threading.CancellationToken ct = default)
            {
                string message = context.Outcome == FailureTier.Success
                    ? context.ChosenOption.IntendedText
                    : $"[{context.Outcome}] {context.ChosenOption.IntendedText}";
                return Task.FromResult(message);
            }

            public Task<DateeResponse> GetDateeResponseAsync(DateeContext context, System.Threading.CancellationToken ct = default)
            {
                return Task.FromResult(new DateeResponse("..."));
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
