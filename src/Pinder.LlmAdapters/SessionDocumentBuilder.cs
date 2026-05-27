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
        /// Builds the user-message content for DeliverMessageAsync.
        /// Uses [ENGINE — DELIVERY] injection block format.
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
            var result = BuildDeliveryPromptEx(context, rollContextBuilder, deliveryRules, statDeliveryInstructions);
            Pinder.Core.Text.InMemoryPromptTraceService.Instance.RecordTrace("delivery", result);
            return result.Text;
        }

        /// <summary>
        /// Builds the user-message content for GetOpponentResponseAsync.
        /// Uses [ENGINE — OPPONENT] injection block format.
        /// </summary>
        public static string BuildOpponentPrompt(OpponentContext context)
        {
            var result = BuildOpponentPromptEx(context);
            Pinder.Core.Text.InMemoryPromptTraceService.Instance.RecordTrace("opponent", result);
            return result.Text;
        }



        /// <summary>
        /// Builds the user-message content for GetInterestChangeBeatAsync (§3.8).
        /// </summary>
        public static string BuildInterestChangeBeatPrompt(
            string opponentName,
            int interestBefore,
            int interestAfter,
            InterestState newState,
            IReadOnlyList<(string Sender, string Text)>? conversationHistory = null,
            string? playerName = null)
        {
            if (opponentName == null) throw new ArgumentNullException(nameof(opponentName));

            string thresholdInstruction = GetThresholdInstruction(interestBefore, interestAfter, newState, opponentName);

            var sb = new StringBuilder();

            // Include recent conversation history so the LLM can reference specific details
            if (conversationHistory != null && conversationHistory.Count > 0)
            {
                sb.AppendLine("RECENT CONVERSATION (for context — reference specific details in your response):");
                HistoryFormatter.FormatRecent(sb, conversationHistory, playerName);
                sb.AppendLine();
            }

            sb.Append(PromptTemplates.InterestBeatInstruction
                .Replace("{opponent_name}", opponentName)
                .Replace("{interest_before}", interestBefore.ToString())
                .Replace("{interest_after}", interestAfter.ToString())
                .Replace("{threshold_instruction}", thresholdInstruction));

            return sb.ToString();
        }




    }
}
