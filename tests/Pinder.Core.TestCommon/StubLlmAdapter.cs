using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Pinder.Core.Conversation;
using Pinder.Core.Interfaces;
using Pinder.Core.Stats;

namespace Pinder.Core.TestCommon
{
    /// <summary>
    /// Shared stub LLM adapter for unit tests.
    /// </summary>
    public class StubLlmAdapter : ILlmAdapter
    {
        private readonly Queue<DialogueOption[]> _optionSets = new Queue<DialogueOption[]>();
        private DialogueOption[]? _lastOptions;

        public StubLlmAdapter()
        {
        }

        public StubLlmAdapter(params DialogueOption[] initialOptions)
        {
            if (initialOptions != null && initialOptions.Length > 0)
            {
                EnqueueOptions(initialOptions);
            }
        }

        public void EnqueueOptions(params DialogueOption[] options)
        {
            _optionSets.Enqueue(options);
        }

        public Task<DialogueOption[]> GetDialogueOptionsAsync(DialogueContext context, CancellationToken ct = default)
        {
            if (_optionSets.Count > 0)
            {
                _lastOptions = _optionSets.Dequeue();
                return Task.FromResult(_lastOptions);
            }
            if (_lastOptions != null)
            {
                return Task.FromResult(_lastOptions);
            }
            return Task.FromResult(new[] { new DialogueOption(StatType.Charm, "Default") });
        }

        public Task<string> DeliverMessageAsync(DeliveryContext context, CancellationToken ct = default)
            => Task.FromResult(context.ChosenOption.IntendedText);

        public Task<OpponentResponse> GetOpponentResponseAsync(OpponentContext context, CancellationToken ct = default)
            => Task.FromResult(new OpponentResponse("..."));

        public Task<string?> GetInterestChangeBeatAsync(InterestChangeContext context, CancellationToken ct = default)
            => Task.FromResult<string?>(null);

        public Task<string> ApplyHorninessOverlayAsync(string message, string instruction, string? opponentContext = null, string? archetypeDirective = null, CancellationToken ct = default)
            => Task.FromResult(message);

        public Task<string> ApplyShadowCorruptionAsync(string message, string instruction, ShadowStatType shadow, string? archetypeDirective = null, CancellationToken ct = default)
            => Task.FromResult(message);

        public Task<string> ApplyTrapOverlayAsync(string message, string trapInstruction, string trapName, string? opponentContext = null, string? archetypeDirective = null, CancellationToken ct = default)
            => Task.FromResult(message);

        public static StatBlock MakeStatBlock(int allStats = 2, int allShadow = 0)
        {
            var stats = new Dictionary<StatType, int>
            {
                { StatType.Charm, allStats },
                { StatType.Rizz, allStats },
                { StatType.Honesty, allStats },
                { StatType.Chaos, allStats },
                { StatType.Wit, allStats },
                { StatType.SelfAwareness, allStats }
            };
            var shadow = new Dictionary<ShadowStatType, int>
            {
                { ShadowStatType.Madness, allShadow },
                { ShadowStatType.Despair, allShadow },
                { ShadowStatType.Denial, allShadow },
                { ShadowStatType.Fixation, allShadow },
                { ShadowStatType.Dread, allShadow },
                { ShadowStatType.Overthinking, allShadow }
            };
            return new StatBlock(stats, shadow);
        }
    }
}
