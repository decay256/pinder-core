using System.Threading.Tasks;
using Pinder.Core.Conversation;

namespace Pinder.Core.Interfaces
{
    /// <summary>
    /// Abstraction layer for all LLM interactions during a Pinder conversation.
    /// The actual provider (EigenCore, OpenAI, Anthropic, etc.) is injected at runtime.
    /// </summary>
    public interface ILlmAdapter
    {
        /// <summary>
        /// Generate 4 dialogue options for the player's turn.
        /// </summary>
        Task<DialogueOption[]> GetDialogueOptionsAsync(DialogueContext context);

        /// <summary>
        /// Deliver the chosen option with outcome degradation applied.
        /// Returns the player's message text (post-degradation).
        /// </summary>
        Task<string> DeliverMessageAsync(DeliveryContext context);

        /// <summary>
        /// Generate the opponent's response to the player's delivered message.
        /// Returns an OpponentResponse containing the message text and optional gameplay signals.
        /// </summary>
        Task<OpponentResponse> GetOpponentResponseAsync(OpponentContext context);

        /// <summary>
        /// Generate a narrative beat when interest crosses a threshold.
        /// Return null to skip the beat.
        /// </summary>
        Task<string?> GetInterestChangeBeatAsync(InterestChangeContext context);

        /// <summary>
        /// Apply a horniness overlay to a delivered message.
        /// The instruction describes how to rewrite the message with involuntary heat.
        /// Returns the modified message text.
        /// </summary>
        Task<string> ApplyHorninessOverlayAsync(string message, string instruction);
    }
}
