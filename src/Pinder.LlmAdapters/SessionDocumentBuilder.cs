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
    ///
    /// Sprint 12+: Uses compact [ENGINE] injection blocks that translate game
    /// mechanics into narrative for the LLM. Each block type provides exactly
    /// the information the LLM needs for that call.
    /// </summary>
    public static class SessionDocumentBuilder
    {
        /// <summary>
        /// Builds the user-message content for GetDialogueOptionsAsync.
        /// Uses [ENGINE — Turn N] injection block format.
        /// </summary>
        public static string BuildDialogueOptionsPrompt(DialogueContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            var playerName = FallbackName(context.PlayerName, "Player");
            var opponentName = FallbackName(context.OpponentName, "Opponent");

            var sb = new StringBuilder();

            // Opponent profile as informational context (not system identity)
            if (!string.IsNullOrWhiteSpace(context.OpponentPrompt))
            {
                sb.AppendLine($"OPPONENT PROFILE (for context — this is who you are talking to, NOT who you are):");
                sb.AppendLine(context.OpponentPrompt);
                sb.AppendLine();
            }

            // Conversation history
            AppendConversationHistory(sb, context.ConversationHistory, playerName);
            sb.AppendLine();

            // Game state summary for the ENGINE block
            var gameState = new StringBuilder();
            gameState.AppendLine($"Interest: {context.CurrentInterest}/25 — {GetInterestLabel(context.CurrentInterest)}");

            if (context.ActiveTraps.Count > 0)
            {
                gameState.AppendLine($"Active traps: {string.Join(", ", context.ActiveTraps)}");
            }

            if (context.ActiveTrapInstructions != null && context.ActiveTrapInstructions.Length > 0)
            {
                gameState.AppendLine("ACTIVE TRAP INSTRUCTIONS (taint ALL generated options regardless of stat):");
                foreach (var instruction in context.ActiveTrapInstructions)
                    gameState.AppendLine(instruction);
            }

            if (context.HorninessLevel >= 6)
            {
                gameState.AppendLine($"Horniness: {context.HorninessLevel}/10 — Rizz options more prominent, slightly too forward.");
            }
            if (context.RequiresRizzOption)
            {
                gameState.AppendLine("\U0001f525 REQUIRED: Include at least one Rizz option.");
            }

            string shadowTaint = BuildShadowTaintBlock(context.ShadowThresholds);
            if (!string.IsNullOrEmpty(shadowTaint))
            {
                gameState.AppendLine($"Shadow state: {shadowTaint}");
            }

            if (context.CallbackOpportunities != null && context.CallbackOpportunities.Count > 0)
            {
                gameState.AppendLine("Callback opportunities:");
                foreach (var cb in context.CallbackOpportunities)
                {
                    int turnsAgo = context.CurrentTurn - cb.TurnIntroduced;
                    string bonus = turnsAgo >= 4 ? "+2 hidden" : turnsAgo >= 2 ? "+1 hidden" : "+3 hidden (opener)";
                    gameState.AppendLine($"  \"{cb.TopicKey}\" (T{cb.TurnIntroduced}, {turnsAgo} turns ago, {bonus})");
                }
            }

            if (context.ActiveTell != null)
            {
                gameState.AppendLine($"📡 TELL DETECTED: The opponent revealed a vulnerability around {context.ActiveTell.Stat}.");
                gameState.AppendLine($"One option using {context.ActiveTell.Stat} should explicitly capitalize on this moment —");
                gameState.AppendLine("it landed differently than intended. The player read the room.");
            }

            // Inject active archetype directive (#649)
            if (!string.IsNullOrEmpty(context.ActiveArchetypeDirective))
            {
                sb.AppendLine(context.ActiveArchetypeDirective);
                sb.AppendLine();
            }

            // Inject texting style immediately before the ENGINE block (#489)
            if (!string.IsNullOrEmpty(context.PlayerTextingStyle))
            {
                sb.AppendLine("YOUR TEXTING STYLE — follow this exactly, no deviations:");
                sb.AppendLine(context.PlayerTextingStyle);
                sb.AppendLine();
            }

            // [ENGINE — Turn N] injection block
            sb.Append(PromptTemplates.EngineOptionsBlock
                .Replace("{turn}", context.CurrentTurn.ToString())
                .Replace("{player_name}", playerName)
                .Replace("{game_state}", gameState.ToString().TrimEnd()));

            sb.AppendLine();
            sb.AppendLine();

            // Output format instructions
// Build available stats string for this turn
            string availableStatsStr = context.AvailableStats != null && context.AvailableStats.Length > 0
                ? string.Join(", ", System.Array.ConvertAll(context.AvailableStats, s => s.ToString().ToUpperInvariant()))
                : "CHARM, RIZZ, HONESTY, CHAOS, WIT, SELF_AWARENESS";
            sb.Append(PromptTemplates.DialogueOptionsInstruction
                .Replace("{player_name}", playerName)
                .Replace("{available_stats}", availableStatsStr));

            return sb.ToString();
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
            DeliveryRules? deliveryRules = null)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            var playerName = FallbackName(context.PlayerName, "Player");
            var opponentName = FallbackName(context.OpponentName, "Opponent");
            var builder = rollContextBuilder ?? new RollContextBuilder();

            var sb = new StringBuilder();

            // Conversation history
            AppendConversationHistory(sb, context.ConversationHistory, playerName);

            string deliveryTaint = BuildShadowTaintBlock(context.ShadowThresholds);
            if (!string.IsNullOrEmpty(deliveryTaint))
            {
                sb.AppendLine();
                sb.AppendLine("SHADOW STATE (corrupting forces on your communication)");
                sb.AppendLine(deliveryTaint);
            }

            sb.AppendLine();

            // Build roll context narrative from YAML or fallback
            string rollContext;
            if (context.Outcome == FailureTier.None)
            {
                rollContext = builder.GetSuccessContext(context.BeatDcBy, false);
            }
            else
            {
                rollContext = builder.GetFailureContext(context.Outcome);
            }

            // [ENGINE — DELIVERY] injection block
            sb.AppendLine(PromptTemplates.EngineDeliveryBlock
                .Replace("{chosen_option}", context.ChosenOption.IntendedText)
                .Replace("{roll_context}", rollContext)
                .Replace("{player_name}", playerName));

            sb.AppendLine();

            // Additional context for the LLM based on outcome
            if (context.Outcome == FailureTier.None)
            {
                string nat20Str = context.IsNat20 ? " (NAT 20)" : "";
                string beatDcByStr = $"{context.BeatDcBy}{nat20Str}";
                string tierLabel = context.IsNat20 ? "Nat 20 — legendary. One sentence can be more effective than a paragraph if it's exactly right."
                    : context.BeatDcBy >= 15 ? "Exceptional (margin 15+) — the best version of this message that could exist. It arrives at exactly the right moment with exactly the right weight."
                    : context.BeatDcBy >= 10 ? "Critical success (margin 10-14) — deliver at peak. The message arrives perfectly. Something resonates."
                    : context.BeatDcBy >= 5  ? "Strong success (margin 5-9) — improve the phrasing, timing, or rhythm. Sharpen word choice. You may add ONE word or phrase that makes the existing sentiment more precise. Do NOT add new sentences or new ideas."
                    : "Clean success (margin 1-4) — deliver essentially as written. Small word choice improvements only.";
                sb.AppendLine($"Stat: {context.ChosenOption.Stat.ToString().ToUpperInvariant()} | Beat DC by {beatDcByStr}");
                sb.Append(PromptTemplates.BuildSuccessDeliveryInstruction(deliveryRules)
                    .Replace("{player_name}", playerName)
                    .Replace("{beat_dc_by}", beatDcByStr)
                    .Replace("{tier_instruction}", tierLabel));
            }
            else
            {
                int missMargin = Math.Abs(context.BeatDcBy);
                string tierName = GetFailureTierName(context.Outcome);
                string tierInstruction = GetTierInstruction(context.Outcome);

                sb.AppendLine($"Stat: {context.ChosenOption.Stat.ToString().ToUpperInvariant()} | Missed DC by {missMargin} | Tier: {tierName}");

                string failureText = PromptTemplates.FailureDeliveryInstruction
                    .Replace("{player_name}", playerName)
                    .Replace("{intended_message}", context.ChosenOption.IntendedText)
                    .Replace("{stat}", context.ChosenOption.Stat.ToString().ToUpperInvariant())
                    .Replace("{miss_margin}", missMargin.ToString())
                    .Replace("{tier}", tierName)
                    .Replace("{tier_instruction}", tierInstruction);

                if (context.ActiveTrapInstructions != null && context.ActiveTrapInstructions.Length > 0)
                {
                    failureText = failureText.Replace("{active_trap_llm_instructions}",
                        "Active trap instructions:\n" + string.Join("\n", context.ActiveTrapInstructions));
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
        /// Builds the user-message content for GetOpponentResponseAsync.
        /// Uses [ENGINE — OPPONENT] injection block format.
        /// </summary>
        public static string BuildOpponentPrompt(OpponentContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            var playerName = FallbackName(context.PlayerName, "Player");
            var opponentName = FallbackName(context.OpponentName, "Opponent");

            var sb = new StringBuilder();

            // Conversation history
            AppendConversationHistory(sb, context.ConversationHistory, playerName);
            sb.AppendLine();

            // Player's last message with failure context if applicable
            if (context.DeliveryTier != FailureTier.None)
            {
                string tierLabel = GetFailureTierName(context.DeliveryTier);
                sb.AppendLine($"PLAYER'S LAST MESSAGE (delivered after a {tierLabel}):");
                sb.AppendLine($"\"{context.PlayerDeliveredMessage}\"");
                sb.AppendLine();
                sb.AppendLine("FAILURE CONTEXT");
                sb.AppendLine(GetOpponentReactionGuidance(context.DeliveryTier));
            }
            else
            {
                sb.AppendLine("PLAYER'S LAST MESSAGE");
                sb.AppendLine($"\"{context.PlayerDeliveredMessage}\"");
            }

            sb.AppendLine();

            // [ENGINE — OPPONENT] injection block with interest narrative
            string interestNarrative = PromptTemplates.GetInterestNarrative(context.InterestAfter);
            sb.AppendLine(PromptTemplates.EngineOpponentBlock
                .Replace("{opponent_name}", opponentName)
                .Replace("{interest}", context.InterestAfter.ToString())
                .Replace("{interest_narrative}", interestNarrative));

            sb.AppendLine();

            // Interest change delta
            int delta = context.InterestAfter - context.InterestBefore;
            string deltaStr = delta >= 0 ? $"+{delta}" : delta.ToString();
            sb.AppendLine($"Interest moved from {context.InterestBefore} to {context.InterestAfter} ({deltaStr}).");

            // Response timing
            sb.AppendLine();
            sb.AppendLine("RESPONSE TIMING");
            if (context.ResponseDelayMinutes < 1.0)
            {
                sb.AppendLine("Your reply arrives in less than 1 minute.");
            }
            else
            {
                sb.AppendLine($"Your reply arrives in approximately {context.ResponseDelayMinutes:F1} minutes.");
            }

            if (context.ActiveTrapInstructions != null && context.ActiveTrapInstructions.Length > 0)
            {
                sb.AppendLine();
                sb.AppendLine("ACTIVE TRAP INSTRUCTIONS");
                foreach (var instruction in context.ActiveTrapInstructions)
                {
                    sb.AppendLine(instruction);
                }
            }

            string opponentTaint = BuildShadowTaintBlock(context.ShadowThresholds);
            if (!string.IsNullOrEmpty(opponentTaint))
            {
                sb.AppendLine();
                sb.AppendLine("SHADOW STATE (corrupting forces on your communication)");
                sb.AppendLine(opponentTaint);
            }

            // Inject active archetype directive for opponent (#649)
            if (!string.IsNullOrEmpty(context.ActiveArchetypeDirective))
            {
                sb.AppendLine();
                sb.AppendLine(context.ActiveArchetypeDirective);
            }

            sb.AppendLine();

            string resistanceBlock = GetResistanceBlock(context.InterestAfter);
            sb.Append(PromptTemplates.OpponentResponseInstruction
                .Replace("{resistance_block}", resistanceBlock));

            return sb.ToString();
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
                int start = Math.Max(0, conversationHistory.Count - 6);
                for (int i = start; i < conversationHistory.Count; i++)
                {
                    var entry = conversationHistory[i];
                    string role = (!string.IsNullOrEmpty(playerName) && entry.Sender == playerName) ? "PLAYER" : "OPPONENT";
                    sb.AppendLine($"[{role}] \"{entry.Text}\"");
                }
                sb.AppendLine();
            }

            sb.Append(PromptTemplates.InterestBeatInstruction
                .Replace("{opponent_name}", opponentName)
                .Replace("{interest_before}", interestBefore.ToString())
                .Replace("{interest_after}", interestAfter.ToString())
                .Replace("{threshold_instruction}", thresholdInstruction));

            return sb.ToString();
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

        /// <summary>
        /// Returns a compact interest state label for the game state summary.
        /// </summary>
        private static string GetInterestLabel(int interest)
        {
            if (interest >= 21) return "Almost There \U0001f525";
            if (interest >= 16) return "Very Into It \U0001f60d (advantage)";
            if (interest >= 10) return "Interested \U0001f60a";
            if (interest >= 5) return "Lukewarm \U0001f914";
            if (interest >= 1) return "Bored \U0001f610 (disadvantage)";
            return "Unmatched \U0001f480";
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

            return PromptTemplates.InterestBeatGeneric.Replace("{opponent_name}", opponentName);
        }

        private static string BuildShadowTaintBlock(Dictionary<ShadowStatType, int>? thresholds)
        {
            if (thresholds == null || thresholds.Count == 0) return string.Empty;
            var sb = new StringBuilder();
            if (thresholds.TryGetValue(ShadowStatType.Madness, out int madness) && madness > 5)
                sb.AppendLine("Your Madness is elevated. Your charm has an uncanny quality — warmth that somehow feels slightly off. Smooth words that land wrong. People can't identify why they feel uneasy. You don't notice.");
            if (thresholds.TryGetValue(ShadowStatType.Horniness, out int horniness) && horniness > 6)
                sb.AppendLine("Your Horniness is elevated. You are reading subtext that may not be there. Rizz options surface more often and more forward. You are slightly too aware of the tension.");
            if (thresholds.TryGetValue(ShadowStatType.Denial, out int denial) && denial > 5)
                sb.AppendLine("Your Denial is elevated. Your honest options sound rehearsed. You tell truths that are technically true but curated. Emotional availability is performed rather than felt.");
            if (thresholds.TryGetValue(ShadowStatType.Fixation, out int fixation) && fixation > 5)
                sb.AppendLine("Your Fixation is elevated. Chaos options feel forced — spontaneity that sounds calculated. You're trying to seem unpredictable. It shows.");
            if (thresholds.TryGetValue(ShadowStatType.Dread, out int dread) && dread > 5)
                sb.AppendLine("Your Dread is elevated. Even successful Wit options have melancholy undertones. Jokes land but leave a slightly hollow aftertaste. You're funny because it's easier than being vulnerable.");
            if (thresholds.TryGetValue(ShadowStatType.Overthinking, out int overthinking) && overthinking > 5)
                sb.AppendLine("Your Overthinking is elevated. Self-Awareness options are too clinical. You explain your feelings rather than expressing them. You know this. You're doing it anyway.");
            return sb.ToString().Trim();
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

        /// <summary>
        /// Returns a resistance descriptor block based on current interest level.
        /// Below 25, the opponent always maintains some form of resistance.
        /// </summary>
        internal static string GetResistanceBlock(int interest)
        {
            string descriptor;
            if (interest >= 25)
                descriptor = PromptTemplates.ResistanceDissolved;
            else if (interest >= 21)
                descriptor = PromptTemplates.ResistanceAlmostConvinced;
            else if (interest >= 15)
                descriptor = PromptTemplates.ResistanceDeliberateApproach;
            else if (interest >= 10)
                descriptor = PromptTemplates.ResistanceUnstableAgreement;
            else if (interest >= 5)
                descriptor = PromptTemplates.ResistanceSkepticalInterest;
            else if (interest >= 1)
                descriptor = PromptTemplates.ResistanceActiveDisengagement;
            else
                descriptor = PromptTemplates.ResistanceActiveDisengagement;

            return $"Current interest: {interest}/25. Resistance level: {descriptor}";
        }

        /// <summary>
        /// Returns per-tier opponent reaction guidance for failure degradation (#493).
        /// </summary>
        internal static string GetOpponentReactionGuidance(FailureTier tier)
        {
            switch (tier)
            {
                case FailureTier.Fumble: return PromptTemplates.OpponentReactionFumble;
                case FailureTier.Misfire: return PromptTemplates.OpponentReactionMisfire;
                case FailureTier.TropeTrap: return PromptTemplates.OpponentReactionTropeTrap;
                case FailureTier.Catastrophe: return PromptTemplates.OpponentReactionCatastrophe;
                case FailureTier.Legendary: return PromptTemplates.OpponentReactionLegendary;
                default: return string.Empty;
            }
        }

        private static string FallbackName(string name, string fallback)
        {
            return string.IsNullOrEmpty(name) ? fallback : name;
        }
    }
}
