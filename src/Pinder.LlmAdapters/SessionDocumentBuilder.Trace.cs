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
        /// <remarks>
        /// <para><strong>#1208 immutable-first contract: DOCUMENTED EXCEPTION</strong></para>
        /// <para>This builder CANNOT be safely reordered to immutable-first. Its engine blocks interpolate volatile state,
        /// and trailing static instructions contain positional back-references. Changing this order breaks rendered semantics.
        /// See docs/prompt-cache-ordering.md and pinning tests for details.</para>
        /// </remarks>
        public static PromptTraceResult BuildDialogueOptionsPromptEx(DialogueContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (string.IsNullOrEmpty(context.PlayerName)) throw new ArgumentException("PlayerName cannot be null or empty.");
            if (string.IsNullOrEmpty(context.DateeName)) throw new ArgumentException("DateeName cannot be null or empty.");

            var playerName = context.PlayerName;
            var dateeName = context.DateeName;

            var sb = new AnnotatedStringBuilder();

            // Datee bio
            if (!string.IsNullOrWhiteSpace(context.DateePrompt))
            {
                sb.Append("YOU ARE TALKING TO: ");
                sb.AppendLine(context.DateePrompt, "data/prompts/structural.yaml", "datee-prompt");
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
                gameState.AppendLine($"Horniness: {context.HorninessLevel} — Rizz options more prominent, slightly too forward.");
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
                gameState.AppendLine($"📡 TELL DETECTED: The datee revealed a vulnerability around {context.ActiveTell.Stat}.");
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

            // Cold-opener guard: fires only on the genuine first turn (nobody has spoken yet).
            // Keyed on empty history rather than a turn integer so it is robust to the
            // 0-based, end-of-turn-incremented counter (issue #1155).
            if (context.ConversationHistory.Count == 0)
            {
                sb.AppendLine("COLD OPENER RULE: This is the very first message. You have never spoken to this person before.");
                sb.AppendLine("Since you are initiating the contact and sending the very first message, you MUST NOT say \"interesting that you mention\", \"since you said\", \"you mentioned\", or use any other phrasing that assumes the datee has already spoken or sent a message in this conversation. The datee has sent ZERO messages.");
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
                    sb.AppendLine("Untouched stake lines (the final OPTION must reference one):");
                    foreach (int idx in untouchedIndices)
                    {
                        string preview = context.StakeLines[idx];
                        if (preview.Length > 80) preview = preview.Substring(0, 80) + "…";
                        sb.AppendLine($"  Line {idx + 1}: \"{preview}\"");
                    }
                }
                else
                {
                    sb.AppendLine("All stake lines referenced — the final OPTION may continue the most recent stake thread.");
                }
                sb.AppendLine();
            }

            if (context.ResolvedTarget != null)
            {
                var target = context.ResolvedTarget.Value;
                sb.AppendLine($"TRANSITION DIRECTIVE: {playerName} should deliver {target.Registry} #{target.Index} (\"{target.StemText}\") via OPTION_C as {target.TransitionStyle}.");
                if (!string.IsNullOrEmpty(context.CognitiveSubtext))
                {
                    sb.AppendLine($"COGNITIVE SUBTEXT: {context.CognitiveSubtext}");
                }
                sb.AppendLine();
            }

            int optionCount = context.AvailableStats != null
                ? context.AvailableStats.Length
                : context.MaxDialogueOptions;

            string optionsCountStr = optionCount.ToString();
            string optionsListStr = string.Join(", ", Enumerable.Range(1, optionCount).Select(i => $"OPTION_{i}"));
            string optionsFormatListStr = string.Join(" ", Enumerable.Range(0, optionCount).Select(i => $"OPTION_{i + 1}: [message]"));

            // [ENGINE — Turn N] injection block
            string engineBlock = PromptTemplates.EngineOptionsBlock
                .Replace("{turn}", context.CurrentTurn.ToString())
                .Replace("{player_name}", playerName)
                .Replace("{game_state}", gameState.ToString().TrimEnd())
                .Replace("{options_count}", optionsCountStr)
                .Replace("{options_format_list}", optionsFormatListStr);
            sb.Append(engineBlock, GetTemplateSource("engine-options-block"), "engine-options-block");

            sb.AppendLine();
            sb.AppendLine();

            // Output format instructions
            if (context.AvailableStats == null || context.AvailableStats.Length == 0)
                throw new InvalidOperationException("AvailableStats cannot be null or empty.");
            string availableStatsStr = string.Join(", ", Array.ConvertAll(context.AvailableStats, s => s.ToString().ToUpperInvariant()));

            string dialogueOptionsInstruction = PromptTemplates.DialogueOptionsInstruction
                .Replace("{player_name}", playerName)
                .Replace("{available_stats}", availableStatsStr)
                .Replace("{options_count}", optionsCountStr)
                .Replace("{options_list}", optionsListStr);
            sb.Append(dialogueOptionsInstruction, GetTemplateSource("dialogue-options-instruction"), "dialogue-options-instruction");

            return new PromptTraceResult(sb.ToString(), sb.Spans);
        }

        // #1125 (final, #1138): the creative "delivery" LLM call was collapsed
        // into a deterministic, non-LLM commit/overlay step
        // (Pinder.Core.Conversation.DeliveryOverlay). The delivery prompt
        // builders (BuildDeliveryPrompt / BuildDeliveryPromptEx) and their
        // DeliveryContext input have been removed — there is no longer any
        // delivery prompt compiled or sent on a live turn. Overlay/commit
        // parity is pinned by Issue1125_CollapseDeliveryTests in
        // Pinder.Core.Tests.
        /// <summary>
        /// Builds the user-message content for GetDateeResponseAsync and returns the trace data.
        /// </summary>
        /// <remarks>
        /// <para><strong>#1208 immutable-first contract: DOCUMENTED EXCEPTION</strong></para>
        /// <para>This builder CANNOT be safely reordered to immutable-first. Its engine blocks interpolate volatile state,
        /// and trailing static instructions contain positional back-references. Changing this order breaks rendered semantics.
        /// See docs/prompt-cache-ordering.md and pinning tests for details.</para>
        /// </remarks>
        public static PromptTraceResult BuildDateePromptEx(DateeContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (string.IsNullOrEmpty(context.PlayerName)) throw new ArgumentException("PlayerName cannot be null or empty.");
            if (string.IsNullOrEmpty(context.DateeName)) throw new ArgumentException("DateeName cannot be null or empty.");

            var playerName = context.PlayerName;
            var dateeName = context.DateeName;

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
                sb.AppendLine(GetDateeReactionGuidance(context.DeliveryTier));
            }
            else
            {
                sb.AppendLine("PLAYER'S LAST MESSAGE");
                sb.AppendLine($"\"{context.PlayerDeliveredMessage}\"");
            }

            sb.AppendLine();

            if (context.HorninessOverlayApplied)
            {
                string horninessGuidance = GetHorninessReactionGuidance(context.InterestAfter, context.HorninessOverlayApplied, context.HorninessTier);
                string templateKey = context.InterestAfter < HorninessWarmthThreshold 
                    ? "datee-horniness-reaction-below-threshold" 
                    : "datee-horniness-reaction-high-interest";
                sb.AppendLine("HORNINESS REACTION GUIDANCE");
                sb.AppendLine(horninessGuidance, GetTemplateSource(templateKey), "datee-horniness-reaction");
                sb.AppendLine();
            }

            // [ENGINE — DATEE] injection block with interest narrative
            string interestNarrative = PromptTemplates.GetInterestNarrative(context.InterestAfter);
            string dateeBlock = PromptTemplates.EngineDateeBlock
                .Replace("{datee_name}", dateeName)
                .Replace("{interest}", context.InterestAfter.ToString())
                .Replace("{interest_narrative}", interestNarrative);
            sb.AppendLine(dateeBlock, GetTemplateSource("engine-datee-block"), "engine-datee-block");

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

            string dateeTaint = BuildShadowTaintBlock(context.ShadowThresholds);
            if (!string.IsNullOrEmpty(dateeTaint))
            {
                sb.AppendLine();
                sb.AppendLine("SHADOW STATE (corrupting forces on your communication)");
                sb.AppendLine(dateeTaint);
            }

            // Inject active archetype directive for datee
            if (!string.IsNullOrEmpty(context.ActiveArchetypeDirective))
            {
                sb.AppendLine();
                sb.AppendLine(context.ActiveArchetypeDirective, "data/prompts/archetypes.yaml", "active-archetype-directive");
            }

            sb.AppendLine();

            if (context.ResolvedTarget != null)
            {
                var target = context.ResolvedTarget.Value;
                sb.AppendLine($"TRANSITION DIRECTIVE: React to {target.Registry} #{target.Index} (\"{target.StemText}\") using {target.TransitionStyle}.");
                if (!string.IsNullOrEmpty(context.CognitiveSubtext))
                {
                    sb.AppendLine($"COGNITIVE SUBTEXT: {context.CognitiveSubtext}");
                }
                sb.AppendLine();
            }

            string resistanceBlock = GetResistanceBlock(context.InterestAfter);

            int ceiling = ComputeResponseCeiling(context.PlayerDeliveredMessage.Length);
            string lengthHint =
                $"Keep it to a natural text-message length. " +
                $"Do not exceed {ceiling} characters regardless of your texting style. " +
                $"The texting-style length axis in your system prompt is a stylistic guideline, NOT a hard engine cap — " +
                $"the engine-specified ceiling above takes precedence over any style axis that would run longer.";

            string dateeResponseInstruction = PromptTemplates.DateeResponseInstruction
                .Replace("{resistance_block}", resistanceBlock)
                .Replace("{length_hint}", lengthHint);
            sb.Append(dateeResponseInstruction, GetTemplateSource("datee-response-instruction"), "datee-response-instruction");

            return new PromptTraceResult(sb.ToString(), sb.Spans);
        }
    }
}
