using System.Threading.Tasks;
using Pinder.Core.Conversation;
using Pinder.Core.Stats;

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
        /// <param name="opponentContext">Optional compact opponent context (name, bio, items) to ground the overlay.</param>
        /// <param name="archetypeDirective">
        /// Optional active archetype directive for the speaking character
        /// (e.g. <c>"ACTIVE ARCHETYPE: The Peacock (clear)\n..."</c>) so the
        /// overlay rewrite respects the character's voice (#372).
        /// </param>
        Task<string> ApplyHorninessOverlayAsync(string message, string instruction, string? opponentContext = null, string? archetypeDirective = null);

        /// <summary>
        /// Apply a shadow corruption instruction to a delivered message.
        /// Called when a shadow check fails and the main roll was a success —
        /// the message is rewritten to show the corruption bleeding through.
        /// Returns the corrupted message text.
        /// </summary>
        /// <param name="archetypeDirective">
        /// Optional active archetype directive for the speaking character so
        /// the corrupted rewrite still sounds like the character (#372).
        /// </param>
        Task<string> ApplyShadowCorruptionAsync(string message, string instruction, ShadowStatType shadow, string? archetypeDirective = null);
    }
}
