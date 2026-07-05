using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Pinder.Core.Interfaces;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;

namespace Pinder.Core.Conversation
{
    /// <summary>
    /// A no-op LLM adapter that returns hardcoded placeholder responses.
    /// Used for unit testing and standalone runs without an actual LLM provider.
    /// </summary>
    public sealed class NullLlmAdapter : ILlmAdapter, IStatefulLlmAdapter
    {
        /// <summary>
        /// Returns 4 generic dialogue options, one per stat family
        /// (Charm, Honesty, Wit, Chaos).
        /// </summary>
        public Task<DialogueOption[]> GetDialogueOptionsAsync(DialogueContext context, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            var options = new[]
            {
                new DialogueOption(StatType.Charm, "Hey, you come here often?"),
                new DialogueOption(StatType.Honesty, "I have to be real with you..."),
                new DialogueOption(StatType.Wit, "Did you know that penguins propose with pebbles?"),
                new DialogueOption(StatType.Chaos, "I once ate a whole pizza in a bouncy castle.")
            };
            return Task.FromResult(options);
        }

        /// <summary>
        /// Returns a minimal placeholder DateeResponse with "..." text and no signals.
        /// </summary>
        public Task<DateeResponse> GetDateeResponseAsync(DateeContext context, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(new DateeResponse("..."));
        }

        /// <inheritdoc />
        public Task<StatefulDateeResult> GetDateeResponseAsync(
            DateeContext context,
            IReadOnlyList<ConversationMessage> history,
            CancellationToken cancellationToken = default)
        {
            var resp = new DateeResponse("...");
            // NullLlmAdapter still records placeholder history entries so engine
            // round-trips (snapshot/restore) and Phase 0 invariants behave the
            // same as a real adapter.
            var entries = new ConversationMessage[]
            {
                ConversationMessage.User(string.Empty),
                ConversationMessage.Assistant("..."),
            };
            return Task.FromResult(new StatefulDateeResult(resp, entries));
        }

        /// <summary>
        /// Always returns null (no narrative beat).
        /// </summary>
        public Task<string?> GetInterestChangeBeatAsync(InterestChangeContext context, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult<string?>(null);
        }

        /// <summary>
        /// Returns a placeholder steering question.
        /// </summary>
        public Task<string> GetSteeringQuestionAsync(SteeringContext context, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult("so... when are we actually doing this?");
        }

        public Task<string> GetHorninessQuestionAsync(HorninessQuestionContext context, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult("so... your place or mine?");
        }

        public Task<string> GetSuccessImprovementAsync(SuccessImprovementContext context, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(context.DeliveredMessage);
        }

        /// <summary>
        /// Returns the message unchanged (no LLM overlay in test mode).
        /// </summary>
        public Task<string> ApplyHorninessOverlayAsync(string message, string instruction, string? dateeContext = null, string? archetypeDirective = null, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(message);
        }

        /// <summary>
        /// Returns the message unchanged (no shadow corruption in test mode).
        /// </summary>
        public Task<string> ApplyShadowCorruptionAsync(string message, string instruction, ShadowStatType shadow, string? archetypeDirective = null, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(message);
        }

        /// <summary>
        /// Returns the message unchanged (no trap overlay in test mode).
        /// Used by the deterministic test harness so engine flow can be exercised
        /// without an actual LLM round-trip.
        /// </summary>
        public Task<string> ApplyTrapOverlayAsync(string message, string trapInstruction, string trapName, string? dateeContext = null, string? archetypeDirective = null, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(message);
        }

        /// <inheritdoc />
        public Task<string> ApplyFailureCorruptionAsync(string message, string instruction, StatType stat, FailureTier tier, string? archetypeDirective = null, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(message);
        }
    }
}
