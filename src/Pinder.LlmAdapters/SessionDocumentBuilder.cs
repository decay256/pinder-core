using System;
using System.Collections.Generic;
using System.Text;
using Pinder.Core.Conversation;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;

namespace Pinder.LlmAdapters
{
    /// <summary>
    /// Builds the user-message content for each of the 4 ILlmAdapter method calls.
    /// Pure static utility — no I/O, no state, no async.
    /// </summary>
    public static class SessionDocumentBuilder
    {
        /// <summary>
        /// Builds the user-message content for GetDialogueOptionsAsync (§3.2).
        /// </summary>
        public static string BuildDialogueOptionsPrompt(
            IReadOnlyList<(string Sender, string Text)> conversationHistory,
            string opponentLastMessage,
            string[] activeTraps,
            int currentInterest,
            int currentTurn,
            string playerName,
            string opponentName)
        {
            if (conversationHistory == null) throw new ArgumentNullException(nameof(conversationHistory));
            if (opponentLastMessage == null) throw new ArgumentNullException(nameof(opponentLastMessage));
            if (activeTraps == null) throw new ArgumentNullException(nameof(activeTraps));
            if (string.IsNullOrEmpty(playerName)) throw new ArgumentNullException(nameof(playerName));
            if (string.IsNullOrEmpty(opponentName)) throw new ArgumentNullException(nameof(opponentName));

            var sb = new StringBuilder();

            sb.AppendLine("CONVERSATION HISTORY");
            AppendConversationHistory(sb, conversationHistory, playerName);

            sb.AppendLine();
            sb.AppendLine("OPPONENT'S LAST MESSAGE");
            sb.AppendLine($"\"{opponentLastMessage}\"");

            sb.AppendLine();
            sb.AppendLine("GAME STATE");
            if (activeTraps.Length == 0)
            {
                sb.AppendLine("- Active traps: none");
            }
            else
            {
                sb.AppendLine($"- Active traps: {string.Join(", ", activeTraps)}");
            }

            sb.AppendLine();
            sb.AppendLine("YOUR TASK");
            sb.Append(PromptTemplates.DialogueOptionsInstruction.Replace("{player_name}", playerName));

            return sb.ToString();
        }

        /// <summary>
        /// Builds the user-message content for DeliverMessageAsync (§3.3 success / §3.4 failure).
        /// </summary>
        public static string BuildDeliveryPrompt(
            IReadOnlyList<(string Sender, string Text)> conversationHistory,
            DialogueOption chosenOption,
            FailureTier outcome,
            int beatDcBy,
            string[]? activeTrapInstructions,
            string playerName,
            string opponentName)
        {
            if (conversationHistory == null) throw new ArgumentNullException(nameof(conversationHistory));
            if (chosenOption == null) throw new ArgumentNullException(nameof(chosenOption));
            if (string.IsNullOrEmpty(playerName)) throw new ArgumentNullException(nameof(playerName));
            if (string.IsNullOrEmpty(opponentName)) throw new ArgumentNullException(nameof(opponentName));

            var sb = new StringBuilder();

            sb.AppendLine("CONVERSATION HISTORY");
            AppendConversationHistory(sb, conversationHistory, playerName);
            sb.AppendLine();

            if (outcome == FailureTier.None)
            {
                // Success path (§3.3)
                sb.AppendLine($"The player chose option: \"{chosenOption.IntendedText}\"");
                sb.AppendLine($"Stat used: {chosenOption.Stat.ToString().ToUpperInvariant()}");
                sb.AppendLine($"They rolled SUCCESS — beat DC by {beatDcBy}.");
                sb.AppendLine();
                sb.Append(PromptTemplates.SuccessDeliveryInstruction
                    .Replace("{player_name}", playerName));
            }
            else
            {
                // Failure path (§3.4)
                int missMargin = Math.Abs(beatDcBy);
                string tierName = GetFailureTierName(outcome);
                string tierInstruction = GetTierInstruction(outcome);

                sb.AppendLine($"The player chose option: \"{chosenOption.IntendedText}\"");
                sb.AppendLine($"Stat used: {chosenOption.Stat.ToString().ToUpperInvariant()}");
                sb.AppendLine($"They rolled FAILED — missed DC by {missMargin}.");
                sb.AppendLine($"Failure tier: {tierName}");
                sb.AppendLine();

                string failureText = PromptTemplates.FailureDeliveryInstruction
                    .Replace("{player_name}", playerName)
                    .Replace("{intended_message}", chosenOption.IntendedText)
                    .Replace("{stat}", chosenOption.Stat.ToString().ToUpperInvariant())
                    .Replace("{miss_margin}", missMargin.ToString())
                    .Replace("{tier}", tierName)
                    .Replace("{tier_instruction}", tierInstruction);

                if (activeTrapInstructions != null && activeTrapInstructions.Length > 0)
                {
                    failureText = failureText.Replace("{active_trap_llm_instructions}",
                        "Active trap instructions:\n" + string.Join("\n", activeTrapInstructions));
                }
                else
                {
                    failureText = failureText.Replace("{active_trap_llm_instructions}", "");
                }

                sb.Append(failureText);
            }

            return sb.ToString();
        }

        /// <summary>
        /// Builds the user-message content for GetOpponentResponseAsync (§3.5).
        /// </summary>
        public static string BuildOpponentPrompt(
            IReadOnlyList<(string Sender, string Text)> conversationHistory,
            string playerDeliveredMessage,
            int interestBefore,
            int interestAfter,
            double responseDelayMinutes,
            string[]? activeTrapInstructions,
            string playerName,
            string opponentName)
        {
            if (conversationHistory == null) throw new ArgumentNullException(nameof(conversationHistory));
            if (playerDeliveredMessage == null) throw new ArgumentNullException(nameof(playerDeliveredMessage));
            if (string.IsNullOrEmpty(playerName)) throw new ArgumentNullException(nameof(playerName));
            if (string.IsNullOrEmpty(opponentName)) throw new ArgumentNullException(nameof(opponentName));

            var sb = new StringBuilder();

            sb.AppendLine("CONVERSATION HISTORY");
            AppendConversationHistory(sb, conversationHistory, playerName);

            sb.AppendLine();
            sb.AppendLine("PLAYER'S LAST MESSAGE");
            sb.AppendLine($"\"{playerDeliveredMessage}\"");

            sb.AppendLine();
            sb.AppendLine("INTEREST CHANGE");
            int delta = interestAfter - interestBefore;
            string deltaStr = delta >= 0 ? $"+{delta}" : delta.ToString();
            sb.AppendLine($"Interest moved from {interestBefore} to {interestAfter} ({deltaStr}).");
            sb.AppendLine($"Current Interest: {interestAfter}/25");

            sb.AppendLine();
            sb.AppendLine("RESPONSE TIMING");
            if (responseDelayMinutes < 1.0)
            {
                sb.AppendLine("Your reply arrives in less than 1 minute.");
            }
            else
            {
                sb.AppendLine($"Your reply arrives in approximately {responseDelayMinutes:F1} minutes.");
            }
            sb.AppendLine("Write in a register consistent with that timing — a 3-minute reply feels different from a 3-hour reply.");

            sb.AppendLine();
            sb.AppendLine("CURRENT INTEREST STATE");
            sb.AppendLine(GetInterestBehaviourBlock(interestAfter));

            if (activeTrapInstructions != null && activeTrapInstructions.Length > 0)
            {
                sb.AppendLine();
                sb.AppendLine("ACTIVE TRAP INSTRUCTIONS");
                foreach (var instruction in activeTrapInstructions)
                {
                    sb.AppendLine(instruction);
                }
            }

            sb.AppendLine();
            sb.Append(PromptTemplates.OpponentResponseInstruction);

            return sb.ToString();
        }

        /// <summary>
        /// Builds the user-message content for GetInterestChangeBeatAsync (§3.8).
        /// </summary>
        public static string BuildInterestChangeBeatPrompt(
            string opponentName,
            int interestBefore,
            int interestAfter,
            InterestState newState)
        {
            if (opponentName == null) throw new ArgumentNullException(nameof(opponentName));

            string thresholdInstruction = GetThresholdInstruction(interestBefore, interestAfter, newState, opponentName);

            return PromptTemplates.InterestBeatInstruction
                .Replace("{opponent_name}", opponentName)
                .Replace("{interest_before}", interestBefore.ToString())
                .Replace("{interest_after}", interestAfter.ToString())
                .Replace("{threshold_instruction}", thresholdInstruction);
        }

        /// <summary>
        /// Formats conversation history with [T{n}|PLAYER|name] markers.
        /// Full history — never truncated.
        /// </summary>
        private static void AppendConversationHistory(
            StringBuilder sb,
            IReadOnlyList<(string Sender, string Text)> history,
            string playerName)
        {
            sb.AppendLine("[CONVERSATION_START]");

            for (int i = 0; i < history.Count; i++)
            {
                int turn = (i / 2) + 1;
                var entry = history[i];
                string role = entry.Sender == playerName ? "PLAYER" : "OPPONENT";
                sb.AppendLine($"[T{turn}|{role}|{entry.Sender}] \"{entry.Text}\"");
            }

            sb.AppendLine("[CURRENT_TURN]");
        }

        private static string GetInterestBehaviourBlock(int interest)
        {
            if (interest >= 21)
                return "You are extremely interested. You're looking for excuses to keep talking. The date is basically happening.";
            if (interest >= 17)
                return "You are very interested. Replies come quickly. Tone is warmer, more playful. You might volunteer personal information.";
            if (interest >= 13)
                return "You are engaged. Normal pacing. Responsive but not eager. You're seeing where this goes.";
            if (interest >= 9)
                return "You are lukewarm. Taking your time. Replies are functional. You might test them a little.";
            if (interest >= 5)
                return "You are cooling. Short replies. A little dry. You're not sold. One or two good messages could change that.";
            if (interest >= 1)
                return "You are disengaged. Minimal effort. You might send a closing signal or go quiet.";
            return "You have lost all interest. You are unmatching.";
        }

        private static string GetThresholdInstruction(int before, int after, InterestState newState, string opponentName)
        {
            if (newState == InterestState.Unmatched)
                return PromptTemplates.InterestBeatUnmatched.Replace("{opponent_name}", opponentName);
            if (newState == InterestState.DateSecured)
                return PromptTemplates.InterestBeatDateSecured.Replace("{opponent_name}", opponentName);
            if (after > before && after > 15 && before <= 15)
                return PromptTemplates.InterestBeatAbove15.Replace("{opponent_name}", opponentName);
            if (after < before && after < 8 && before >= 8)
                return PromptTemplates.InterestBeatBelow8.Replace("{opponent_name}", opponentName);

            // Generic fallback for other threshold crossings
            return PromptTemplates.InterestBeatGeneric.Replace("{opponent_name}", opponentName);
        }

        private static string GetFailureTierName(FailureTier tier)
        {
            switch (tier)
            {
                case FailureTier.Fumble: return "FUMBLE";
                case FailureTier.Misfire: return "MISFIRE";
                case FailureTier.TropeTrap: return "TROPE_TRAP";
                case FailureTier.Catastrophe: return "CATASTROPHE";
                case FailureTier.Legendary: return "LEGENDARY";
                default: return "UNKNOWN";
            }
        }

        private static string GetTierInstruction(FailureTier tier)
        {
            switch (tier)
            {
                case FailureTier.Fumble:
                    return "Slight fumble. The intended message mostly gets through but with one awkward word choice, an unnecessary hedge, or a small detail that undermines it. Still readable.";
                case FailureTier.Misfire:
                    return "The message goes sideways. Key information gets garbled, tone shifts unexpectedly, or a strange tangent appears mid-sentence. The intent is still guessable but the execution is off.";
                case FailureTier.TropeTrap:
                    return "A stat-specific social trope failure activates. The message transforms into a recognisable bad-texting archetype (oversharing, going unhinged, being pretentious, spiraling, etc.). The trap is now active.";
                case FailureTier.Catastrophe:
                    return "Spectacular disaster. The intended message has been completely hijacked by the character's worst impulse. What comes out is the thing they would NEVER want to send. Still sounds like them — their disaster is their own.";
                case FailureTier.Legendary:
                    return "Maximum humiliation. The character's deepest embarrassing quality surfaces fully. This should be funny, specific, and feel earned by the build.";
                default:
                    return "A failure has occurred. Degrade the message accordingly.";
            }
        }
    }
}
