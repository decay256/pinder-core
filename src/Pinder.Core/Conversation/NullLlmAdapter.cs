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
        /// <inheritdoc />
        public void StartOpponentSession(string opponentSystemPrompt)
        {
            // No-op: NullLlmAdapter does not maintain stateful sessions.
        }

        /// <inheritdoc />
        public bool HasOpponentSession => false;

        /// <summary>
        /// Returns 4 generic dialogue options, one per stat family
        /// (Charm, Honesty, Wit, Chaos).
        /// </summary>
        public Task<DialogueOption[]> GetDialogueOptionsAsync(DialogueContext context)
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

        /// <summary>
        /// Echoes the intended text with a failure tier prefix.
        /// Format: "[{tier}] {intendedText}" for failures, or the intended text as-is for success.
        /// </summary>
        public Task<string> DeliverMessageAsync(DeliveryContext context)
        {
            string message = context.Outcome == FailureTier.None
                ? context.ChosenOption.IntendedText
                : $"[{context.Outcome}] {context.ChosenOption.IntendedText}";
            return Task.FromResult(message);
        }

        /// <summary>
        /// Returns a minimal placeholder OpponentResponse with "..." text and no signals.
        /// </summary>
        public Task<OpponentResponse> GetOpponentResponseAsync(OpponentContext context)
        {
            return Task.FromResult(new OpponentResponse("..."));
        }

        /// <summary>
        /// Always returns null (no narrative beat).
        /// </summary>
        public Task<string?> GetInterestChangeBeatAsync(InterestChangeContext context)
        {
            return Task.FromResult<string?>(null);
        }

        /// <summary>
        /// Returns a placeholder steering question.
        /// </summary>
        public Task<string> GetSteeringQuestionAsync(SteeringContext context)
        {
            return Task.FromResult("so... when are we actually doing this?");
        }

        /// <summary>
        /// Returns the message unchanged (no LLM overlay in test mode).
        /// </summary>
        public Task<string> ApplyHorninessOverlayAsync(string message, string instruction, string? opponentContext = null)
        {
            return Task.FromResult(message);
        }

        /// <summary>
        /// Returns the message unchanged (no shadow corruption in test mode).
        /// </summary>
        public Task<string> ApplyShadowCorruptionAsync(string message, string instruction, ShadowStatType shadow)
        {
            return Task.FromResult(message);
        }

        /// <summary>
        /// Returns the message unchanged (no trap overlay in test mode).
        /// Used by the deterministic test harness so engine flow can be exercised
        /// without an actual LLM round-trip.
        /// </summary>
        public Task<string> ApplyTrapOverlayAsync(string message, string trapInstruction, string trapName, string? opponentContext = null)
        {
            return Task.FromResult(message);
        }
    }
}
