using System;
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
        private readonly Queue<DateeResponse> _dateeResponses = new Queue<DateeResponse>();
        private readonly List<DialogueContext> _dialogueContexts = new List<DialogueContext>();
        private readonly List<DateeContext> _dateeContexts = new List<DateeContext>();
        private DialogueOption[]? _lastOptions;

        public IReadOnlyList<DialogueContext> DialogueContexts => _dialogueContexts;
        public IReadOnlyList<DateeContext> DateeContexts => _dateeContexts;
        public Action<DialogueContext>? OnGetDialogueOptions { get; set; }
        public Action<DateeContext>? OnGetDateeResponse { get; set; }

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

        public void EnqueueDateeResponse(DateeResponse response)
        {
            _dateeResponses.Enqueue(response);
        }

        public virtual Task<DialogueOption[]> GetDialogueOptionsAsync(DialogueContext context, CancellationToken ct = default)
        {
            _dialogueContexts.Add(context);
            OnGetDialogueOptions?.Invoke(context);

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

        // #1125 — delivery collapsed: the creative "delivery" LLM call was
        // removed from ILlmAdapter. Dialogue options now carry the FULL sendable
        // line and the engine commits it via the deterministic, non-LLM
        // DeliveryOverlay. This stub therefore no longer implements a
        // DeliverMessageAsync(DeliveryContext) surface — there is none to
        // implement. (Keystone for #1136: both downstream test projects compile
        // against this contract.)

        public virtual Task<DateeResponse> GetDateeResponseAsync(DateeContext context, CancellationToken ct = default)
        {
            _dateeContexts.Add(context);
            OnGetDateeResponse?.Invoke(context);

            return Task.FromResult(_dateeResponses.Count > 0
                ? _dateeResponses.Dequeue()
                : new DateeResponse("..."));
        }

        public virtual Task<string?> GetInterestChangeBeatAsync(InterestChangeContext context, CancellationToken ct = default)
            => Task.FromResult<string?>(null);

        public virtual Task<string> ApplyHorninessOverlayAsync(string message, string instruction, string? dateeContext = null, string? archetypeDirective = null, CancellationToken ct = default)
            => Task.FromResult(message);

        public virtual Task<string> ApplyShadowCorruptionAsync(string message, string instruction, ShadowStatType shadow, string? archetypeDirective = null, CancellationToken ct = default)
            => Task.FromResult(message);

        public virtual Task<string> ApplyTrapOverlayAsync(string message, string trapInstruction, string trapName, string? dateeContext = null, string? archetypeDirective = null, CancellationToken ct = default)
            => Task.FromResult(message);

        public virtual Task<string> ApplyFailureCorruptionAsync(string message, string instruction, StatType stat, Pinder.Core.Rolls.FailureTier tier, string? archetypeDirective = null, CancellationToken ct = default)
            => Task.FromResult(message);

        public static StatBlock MakeStatBlock(int allStats = 2, int allShadow = 0)
            => TestHelpers.MakeStatBlock(allStats, allShadow);
    }
}
