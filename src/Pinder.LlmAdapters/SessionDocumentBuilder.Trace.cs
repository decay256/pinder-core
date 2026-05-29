using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Pinder.Core.Conversation;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;
using Pinder.Core.Text;

namespace Pinder.LlmAdapters
{
    public static partial class SessionDocumentBuilder
    {
        private static string GetTemplateSource(string key)
        {
            return PromptTemplates.Catalog?.TryGet(key)?.SourceFile ?? "data/prompts/templates.yaml";
        }

        /// <summary>
        /// Builds the user-message content for GetDialogueOptionsAsync and returns the trace data.
        /// </summary>
        public static PromptTraceResult BuildDialogueOptionsPromptEx(DialogueContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            var playerName = FallbackName(context.PlayerName, "Player");
            var opponentName = FallbackName(context.OpponentName, "Opponent");

            var sb = new AnnotatedStringBuilder();

            // Opponent bio
            if (!string.IsNullOrWhiteSpace(context.OpponentPrompt))
            {
                sb.Append("YOU ARE TALKING TO: ");
                sb.AppendLine(context.OpponentPrompt, "data/prompts/structural.yaml", "opponent-prompt");
                sb.AppendLine();
            }

            // Conversation history
            var historySb = new StringBuilder();
            HistoryFormatter.Format(historySb, context.ConversationHistory, playerName);
            sb.Append(historySb.ToString(), "conversation-history", "conversation-history");
            sb.AppendLine();

            // Game state summary
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

            // Inject active archetype directive
            if (!string.IsNullOrEmpty(context.ActiveArchetypeDirective))
            {
                sb.AppendLine(context.ActiveArchetypeDirective, "data/prompts/archetypes.yaml", "active-archetype-directive");
                sb.AppendLine();
            }

            // Inject texting style
            if (!string.IsNullOrEmpty(context.PlayerTextingStyle))
            {
                sb.AppendLine("YOUR TEXTING STYLE — follow this exactly, no deviations:");
                sb.AppendLine(context.PlayerTextingStyle, "data/prompts/structural.yaml", "player-texting-style");
                sb.AppendLine();
            }

            // Turn 1 cold-opener guard
            if (context.CurrentTurn == 1)
            {
                sb.AppendLine("COLD OPENER RULE: This is Turn 1. You have never spoken to this person before.");
                sb.AppendLine("Since you are initiating the contact and sending the very first message, you MUST NOT say \"interesting that you mention\", \"since you said\", \"you mentioned\", or use any other phrasing that assumes the opponent has already spoken or sent a message in this conversation. The opponent has sent ZERO messages.");
                sb.AppendLine("Your only knowledge of them is their dating profile: bio text AND visible appearance (items listed after 'Wearing:' in the profile above).");
                sb.AppendLine("Do NOT reference anything you would only know from inside knowledge of the character — only what is visible on their public profile.");
                sb.AppendLine("A strong opener can react to their bio, their look, or both. Something specific beats something generic.");
                sb.AppendLine("Examples: their outfit, a specific item they're wearing, the energy their style projects, something the bio implies about them.");
                sb.AppendLine();
            }

            // Turn 3+ pivot directive
            if (context.CurrentTurn >= 3)
            {
                sb.AppendLine(PromptTemplates.PivotDirective, GetTemplateSource("pivot-directive"), "pivot-directive");
                sb.AppendLine();
            }

            // Per-turn stake-coverage block
            if (context.StakeLines != null && context.StakeLines.Length > 0)
            {
                var referenced = context.StakeLinesReferenced;
                var untouchedIndices = new List<int>();
                for (int i = 0; i < context.StakeLines.Length; i++)
                {
                    if (referenced == null || !referenced.Contains(i))
                        untouchedIndices.Add(i);
                }

                int referencedCount = context.StakeLines.Length - untouchedIndices.Count;
                sb.AppendLine($"STAKE COVERAGE — {referencedCount} line(s) referenced this session, {untouchedIndices.Count} untouched.");
                if (untouchedIndices.Count > 0)
                {
                    sb.AppendLine("Untouched stake lines (OPTION_C must reference one):");
                    foreach (int idx in untouchedIndices)
                    {
                        string preview = context.StakeLines[idx];
                        if (preview.Length > 80) preview = preview.Substring(0, 80) + "…";
                        sb.AppendLine($"  Line {idx + 1}: \"{preview}\"");
                    }
                }
                else
                {
                    sb.AppendLine("All stake lines referenced — OPTION_C may continue the most recent stake thread.");
                }
                sb.AppendLine();
            }

            int optionCount = context.AvailableStats != null
                ? context.AvailableStats.Length
                : 4;

            // [ENGINE — Turn N] injection block
            string engineBlock = PromptTemplates.EngineOptionsBlock
                .Replace("{turn}", context.CurrentTurn.ToString())
                .Replace("{player_name}", playerName)
                .Replace("{game_state}", gameState.ToString().TrimEnd())
                .Replace("Generate 4 options", $"Generate {optionCount} options");
            sb.Append(engineBlock, GetTemplateSource("engine-options-block"), "engine-options-block");

            sb.AppendLine();
            sb.AppendLine();

            // Output format instructions
            string availableStatsStr = context.AvailableStats != null && context.AvailableStats.Length > 0
                ? string.Join(", ", Array.ConvertAll(context.AvailableStats, s => s.ToString().ToUpperInvariant()))
                : "CHARM, RIZZ, HONESTY, CHAOS, WIT, SELF_AWARENESS";
            string dialogueOptionsInstruction = PromptTemplates.DialogueOptionsInstruction
                .Replace("{player_name}", playerName)
                .Replace("{available_stats}", availableStatsStr)
                .Replace("Generate exactly 4 dialogue options", $"Generate exactly {optionCount} dialogue options")
                .Replace("only OPTION_1, OPTION_2, OPTION_3, OPTION_4", "only " + string.Join(", ", Enumerable.Range(1, optionCount).Select(i => $"OPTION_{i}")))
                .Replace("OPTION_4", $"OPTION_{optionCount}");
            sb.Append(dialogueOptionsInstruction, GetTemplateSource("dialogue-options-instruction"), "dialogue-options-instruction");

            return new PromptTraceResult(sb.ToString(), sb.Spans);
        }

        /// <summary>
        /// Builds the user-message content for DeliverMessageAsync and returns the trace data.
        /// </summary>
        public static PromptTraceResult BuildDeliveryPromptEx(
            DeliveryContext context,
            RollContextBuilder? rollContextBuilder = null,
            DeliveryRules? deliveryRules = null,
            StatDeliveryInstructions? statDeliveryInstructions = null)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            var playerName = FallbackName(context.PlayerName, "Player");
            var opponentName = FallbackName(context.OpponentName, "Opponent");
            var builder = rollContextBuilder ?? new RollContextBuilder();

            var sb = new AnnotatedStringBuilder();

            // Conversation history
            var historySb = new StringBuilder();
            HistoryFormatter.Format(historySb, context.ConversationHistory, playerName);
            sb.Append(historySb.ToString(), "conversation-history", "conversation-history");

            string deliveryTaint = BuildShadowTaintBlock(context.ShadowThresholds);
            if (!string.IsNullOrEmpty(deliveryTaint))
            {
                sb.AppendLine();
                sb.AppendLine("SHADOW STATE (corrupting forces on your communication)");
                sb.AppendLine(deliveryTaint);
            }

            if (!string.IsNullOrEmpty(context.ActiveArchetypeDirective))
            {
                sb.AppendLine();
                sb.AppendLine(context.ActiveArchetypeDirective, "data/prompts/archetypes.yaml", "active-archetype-directive");
            }

            sb.AppendLine();

            // Build roll context narrative
            string rollContext = context.Outcome == FailureTier.Success
                ? builder.GetSuccessContext(context.BeatDcBy, false)
                : builder.GetFailureContext(context.Outcome);

            // [ENGINE — DELIVERY] injection block
            string deliveryBlock = PromptTemplates.EngineDeliveryBlock
                .Replace("{chosen_option}", context.ChosenOption.IntendedText)
                .Replace("{roll_context}", rollContext)
                .Replace("{player_name}", playerName);
            sb.AppendLine(deliveryBlock, GetTemplateSource("engine-delivery-block"), "engine-delivery-block");

            sb.AppendLine();

            // Additional context for the LLM based on outcome
            if (context.Outcome == FailureTier.Success)
            {
                string nat20Str = context.IsNat20 ? " (NAT 20)" : "";
                string beatDcByStr = $"{context.BeatDcBy}{nat20Str}";

                string tierKey = StatDeliveryInstructions.SuccessTierKey(context.BeatDcBy, context.IsNat20);
                string configuredInstruction = statDeliveryInstructions != null
                    ? statDeliveryInstructions.Get(context.ChosenOption.Stat, tierKey)
                    : null;

                string tierLabel = !string.IsNullOrWhiteSpace(configuredInstruction)
                    ? configuredInstruction
                    : context.IsNat20
                    ? $"Nat 20 — legendary. One sentence can be more effective than a paragraph if it's exactly right. {GetStatSuccessVoice(context.ChosenOption.Stat)}"
                    : context.BeatDcBy >= 15
                    ? $"Exceptional (margin 15+) — this is the best version of this message that could exist. {GetStatSuccessVoice(context.ChosenOption.Stat)}"
                    : context.BeatDcBy >= 10
                    ? $"Critical success (margin 10-14) — deliver at peak. {GetStatSuccessVoice(context.ChosenOption.Stat)}"
                    : context.BeatDcBy >= 5
                    ? $"Strong success (margin 5-9) — the message lands better than planned. {GetStatSuccessVoice(context.ChosenOption.Stat)}"
                    : "Clean success (margin 1-4) — deliver essentially as written. Small word choice improvements only.";

                sb.AppendLine($"Stat: {context.ChosenOption.Stat.ToString().ToUpperInvariant()} | Beat DC by {beatDcByStr}");
                
                string successInstruction = PromptTemplates.BuildSuccessDeliveryInstruction(deliveryRules)
                    .Replace("{player_name}", playerName)
                    .Replace("{beat_dc_by}", beatDcByStr)
                    .Replace("{tier_instruction}", tierLabel);
                sb.Append(successInstruction, GetTemplateSource("default-clean"), "success-delivery-instruction");
            }
            else
            {
                int missMargin = Math.Abs(context.BeatDcBy);
                string tierName = GetFailureTierName(context.Outcome);

                string tierInstruction = !string.IsNullOrWhiteSpace(context.StatFailureInstruction)
                    ? context.StatFailureInstruction
                    : (statDeliveryInstructions != null && !string.IsNullOrWhiteSpace(statDeliveryInstructions.Get(context.ChosenOption.Stat, StatDeliveryInstructions.FailureTierKey(context.Outcome))))
                    ? statDeliveryInstructions.Get(context.ChosenOption.Stat, StatDeliveryInstructions.FailureTierKey(context.Outcome))
                    : GetTierInstruction(context.Outcome);

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

                sb.Append(failureText, GetTemplateSource("failure-delivery-instruction"), "failure-delivery-instruction");
            }

            return new PromptTraceResult(sb.ToString(), sb.Spans);
        }

        /// <summary>
        /// Builds the user-message content for GetOpponentResponseAsync and returns the trace data.
        /// </summary>
        public static PromptTraceResult BuildOpponentPromptEx(OpponentContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            var playerName = FallbackName(context.PlayerName, "Player");
            var opponentName = FallbackName(context.OpponentName, "Opponent");

            var sb = new AnnotatedStringBuilder();

            // Conversation history
            var historySb = new StringBuilder();
            HistoryFormatter.Format(historySb, context.ConversationHistory, playerName);
            sb.Append(historySb.ToString(), "conversation-history", "conversation-history");
            sb.AppendLine();

            // Player's last message with failure context
            if (context.DeliveryTier != FailureTier.Success)
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
            string opponentBlock = PromptTemplates.EngineOpponentBlock
                .Replace("{opponent_name}", opponentName)
                .Replace("{interest}", context.InterestAfter.ToString())
                .Replace("{interest_narrative}", interestNarrative);
            sb.AppendLine(opponentBlock, GetTemplateSource("engine-opponent-block"), "engine-opponent-block");

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

            // Inject active archetype directive for opponent
            if (!string.IsNullOrEmpty(context.ActiveArchetypeDirective))
            {
                sb.AppendLine();
                sb.AppendLine(context.ActiveArchetypeDirective, "data/prompts/archetypes.yaml", "active-archetype-directive");
            }

            sb.AppendLine();

            string resistanceBlock = GetResistanceBlock(context.InterestAfter);

            int playerLen = context.PlayerDeliveredMessage.Length;
            int ceiling = ComputeResponseCeiling(playerLen);
            string lengthHint =
                $"Aim for roughly {playerLen} characters (matching the player's message length). " +
                $"Do not exceed {ceiling} characters regardless of your texting style. " +
                $"The texting-style length axis in your system prompt is a stylistic guideline, NOT a hard engine cap \u2014 " +
                $"the engine-specified length above takes precedence. " +
                $"For this message, aim for ~{playerLen} characters as the engine specifies. " +
                $"Style-rule length axes apply ONLY when they are compatible with the engine-specified length.";

            string opponentResponseInstruction = PromptTemplates.OpponentResponseInstruction
                .Replace("{resistance_block}", resistanceBlock)
                .Replace("{length_hint}", lengthHint);
            sb.Append(opponentResponseInstruction, GetTemplateSource("opponent-response-instruction"), "opponent-response-instruction");

            return new PromptTraceResult(sb.ToString(), sb.Spans);
        }
    }
}
