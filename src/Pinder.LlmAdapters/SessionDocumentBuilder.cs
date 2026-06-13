using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Pinder.Core.Conversation;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;

namespace Pinder.LlmAdapters
{
    /// <summary>
    /// Builds the user-message content for each of the 4 ILlmAdapter method calls.
    /// Pure static utility — no I/O, no state, no async.
    ///
    /// Sprint 12+: Uses compact [ENGINE] injection blocks that translate game
    /// mechanics into narrative for the LLM. Each block type provides exactly
    /// the information the LLM needs for that call.
    /// </summary>
    public static partial class SessionDocumentBuilder
    {
        /// <summary>
        /// Builds the user-message content for GetDialogueOptionsAsync.
        /// Uses [ENGINE — Turn N] injection block format.
        /// </summary>
        public static string BuildDialogueOptionsPrompt(DialogueContext context)
        {
            var result = BuildDialogueOptionsPromptEx(context);
            Pinder.Core.Text.InMemoryPromptTraceService.Instance.RecordTrace("dialogue-options", result);
            return result.Text;
        }

        /// <summary>
        /// Builds the [ENGINE — DELIVERY] injection-block prompt text.
        ///
        /// <para>
        /// #1125 — the creative "delivery" LLM call was collapsed into a
        /// deterministic, non-LLM commit/overlay step
        /// (<see cref="Pinder.Core.Conversation.DeliveryOverlay"/>), so this is
        /// NO LONGER a prompt that is compiled and sent for a live turn. The
        /// builder is retained only as a pure formatter consumed by the legacy
        /// SessionDocumentBuilder prompt-shape tests; it is NOT wired into the
        /// engine turn loop and — critically — it no longer registers a
        /// <c>delivery</c> <c>prompt_type</c> with the prompt-trace service.
        /// The active per-turn prompt_type set is therefore
        /// dialogue-options(-system), opponent(-system), and the steering/overlay
        /// calls only; there is no delivery creative compilation. (See AGENTS.md
        /// prompt-tracer list.)
        /// </para>
        /// </summary>
        /// <param name="context">The delivery context.</param>
        /// <param name="rollContextBuilder">
        /// Optional RollContextBuilder for YAML-sourced flavor text.
        /// When null, uses hardcoded roll context defaults.
        /// </param>
        public static string BuildDeliveryPrompt(
            DeliveryContext context,
            RollContextBuilder? rollContextBuilder = null,
            DeliveryRules? deliveryRules = null,
            StatDeliveryInstructions? statDeliveryInstructions = null)
        {
            // #1125: deliberately NO RecordTrace("delivery", ...) — the delivery
            // creative call is gone, so no "delivery" prompt_type may be emitted.
            var result = BuildDeliveryPromptEx(context, rollContextBuilder, deliveryRules, statDeliveryInstructions);
            return result.Text;
        }

        /// <summary>
        /// Builds the user-message content for GetDateeResponseAsync.
        /// Uses [ENGINE — DATEE] injection block format.
        /// </summary>
        public static string BuildDateePrompt(DateeContext context)
        {
            var result = BuildDateePromptEx(context);
            Pinder.Core.Text.InMemoryPromptTraceService.Instance.RecordTrace("datee", result);
            return result.Text;
        }



        /// <summary>
        /// Builds the user-message content for GetInterestChangeBeatAsync (§3.8).
        /// </summary>
        public static string BuildInterestChangeBeatPrompt(
            string dateeName,
            int interestBefore,
            int interestAfter,
            InterestState newState,
            IReadOnlyList<(string Sender, string Text)>? conversationHistory = null,
            string? playerName = null)
        {
            if (dateeName == null) throw new ArgumentNullException(nameof(dateeName));

            string thresholdInstruction = GetThresholdInstruction(interestBefore, interestAfter, newState, dateeName);

            var sb = new StringBuilder();

            // Include recent conversation history so the LLM can reference specific details
            if (conversationHistory != null && conversationHistory.Count > 0)
            {
                sb.AppendLine("RECENT CONVERSATION (for context — reference specific details in your response):");
                HistoryFormatter.FormatRecent(sb, conversationHistory, playerName);
                sb.AppendLine();
            }

            sb.Append(PromptTemplates.InterestBeatInstruction
                .Replace("{datee_name}", dateeName)
                .Replace("{interest_before}", interestBefore.ToString())
                .Replace("{interest_after}", interestAfter.ToString())
                .Replace("{threshold_instruction}", thresholdInstruction));

            return sb.ToString();
        }




    }
}
