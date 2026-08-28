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
        public static string BuildDialogueOptionsPrompt(
            DialogueContext context,
            PromptCatalog? promptCatalog = null)
        {
            var result = BuildDialogueOptionsPromptEx(context, promptCatalog);
            return result.Text;
        }

        /// <summary>
        /// Builds the user-message content for GetDateeResponseAsync.
        /// Uses [ENGINE — DATEE] injection block format.
        /// </summary>
        public static string BuildDateePrompt(
            DateeContext context,
            PromptCatalog? promptCatalog = null)
        {
            var result = BuildDateePromptEx(context, promptCatalog);
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
            string? playerName = null,
            PromptCatalog? promptCatalog = null)
        {
            return BuildInterestChangeBeatPromptEx(
                dateeName,
                interestBefore,
                interestAfter,
                newState,
                conversationHistory,
                playerName,
                promptCatalog).Text;
        }




    }
}
