using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Pinder.Core.Conversation;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;
using Pinder.LlmAdapters.Anthropic;
using Pinder.Core.Text;

namespace Pinder.LlmAdapters
{
    public static partial class SessionDocumentBuilder
    {
        private static string GetTemplateSource(string key)
        {
            return PromptTemplates.Catalog?.TryGet(key)?.SourceFile ?? "data/prompts/templates.yaml";
        }

        private static string RenderTemplate(string template, IReadOnlyDictionary<string, string> values)
        {
            return PromptCatalog.Substitute(template, values);
        }

        private static void AppendAnnotatedTemplate(
            AnnotatedStringBuilder sb,
            string template,
            string templateKey,
            IReadOnlyDictionary<string, (string Value, string SourceFile, string Key)> values)
        {
            int position = 0;
            while (position < template.Length)
            {
                string? nextPlaceholder = null;
                int nextIndex = template.Length;
                foreach (string placeholder in values.Keys)
                {
                    int candidate = template.IndexOf(placeholder, position, StringComparison.Ordinal);
                    if (candidate >= 0 && candidate < nextIndex)
                    {
                        nextPlaceholder = placeholder;
                        nextIndex = candidate;
                    }
                }

                if (nextPlaceholder == null)
                {
                    sb.Append(template.Substring(position), GetTemplateSource(templateKey), templateKey);
                    break;
                }

                if (nextIndex > position)
                {
                    sb.Append(
                        template.Substring(position, nextIndex - position),
                        GetTemplateSource(templateKey),
                        templateKey);
                }

                var replacement = values[nextPlaceholder];
                sb.Append(replacement.Value, replacement.SourceFile, replacement.Key);
                position = nextIndex + nextPlaceholder.Length;
            }
        }

        private static bool AppendShadowTaintBlock(
            AnnotatedStringBuilder sb,
            Dictionary<ShadowStatType, int>? thresholds,
            string headingKey,
            string heading)
        {
            if (thresholds == null || thresholds.Count == 0) return false;

            var keys = GetActiveShadowTaintKeys(thresholds).ToList();
            if (keys.Count == 0) return false;

            sb.AppendLine(heading, GetTemplateSource(headingKey), headingKey);
            foreach (var key in keys)
            {
                sb.AppendLine(GetShadowTaintTemplate(key), GetTemplateSource(key), key);
            }

            return true;
        }

        private static IEnumerable<string> GetActiveShadowTaintKeys(Dictionary<ShadowStatType, int> thresholds)
        {
            if (thresholds.TryGetValue(ShadowStatType.Madness, out int madness) && madness > 5)
                yield return "shadow-taint-madness";
            if (thresholds.TryGetValue(ShadowStatType.Despair, out int despair) && despair > 6)
                yield return "shadow-taint-despair";
            if (thresholds.TryGetValue(ShadowStatType.Denial, out int denial) && denial > 5)
                yield return "shadow-taint-denial";
            if (thresholds.TryGetValue(ShadowStatType.Fixation, out int fixation) && fixation > 5)
                yield return "shadow-taint-fixation";
            if (thresholds.TryGetValue(ShadowStatType.Dread, out int dread) && dread > 5)
                yield return "shadow-taint-dread";
            if (thresholds.TryGetValue(ShadowStatType.Overthinking, out int overthinking) && overthinking > 5)
                yield return "shadow-taint-overthinking";
        }

        private static string GetShadowTaintTemplate(string key)
        {
            switch (key)
            {
                case "shadow-taint-madness": return PromptTemplates.ShadowTaintMadness;
                case "shadow-taint-despair": return PromptTemplates.ShadowTaintDespair;
                case "shadow-taint-denial": return PromptTemplates.ShadowTaintDenial;
                case "shadow-taint-fixation": return PromptTemplates.ShadowTaintFixation;
                case "shadow-taint-dread": return PromptTemplates.ShadowTaintDread;
                case "shadow-taint-overthinking": return PromptTemplates.ShadowTaintOverthinking;
                default:
                    throw new InvalidOperationException($"Unknown shadow taint prompt key '{key}'.");
            }
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

            if (AppendShadowTaintBlock(sb, context.ShadowThresholds, "shadow-state-heading", PromptTemplates.ShadowStateHeading))
            {
                sb.AppendLine();
            }

            // Cold-opener guard: fires only on the genuine first turn (nobody has spoken yet).
            // Keyed on empty history rather than a turn integer so it is robust to the
            // 0-based, end-of-turn-incremented counter (issue #1155).
            if (context.ConversationHistory.Count == 0)
            {
                sb.AppendLine(PromptTemplates.ColdOpenerRule, GetTemplateSource("cold-opener-rule"), "cold-opener-rule");
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
                string stakeCoverageSummary = RenderTemplate(
                    PromptTemplates.StakeCoverageSummary,
                    new Dictionary<string, string>
                    {
                        { "referenced_count", referencedCount.ToString() },
                        { "untouched_count", untouchedIndices.Count.ToString() },
                    });
                sb.AppendLine(stakeCoverageSummary, GetTemplateSource("stake-coverage-summary"), "stake-coverage-summary");
                if (untouchedIndices.Count > 0)
                {
                    sb.AppendLine(
                        PromptTemplates.StakeCoverageUntouchedDirective,
                        GetTemplateSource("stake-coverage-untouched-directive"),
                        "stake-coverage-untouched-directive");
                    foreach (int idx in untouchedIndices)
                    {
                        string preview = context.StakeLines[idx];
                        if (preview.Length > 80) preview = preview.Substring(0, 80) + "…";
                        sb.AppendLine($"  Line {idx + 1}: \"{preview}\"");
                    }
                }
                else
                {
                    sb.AppendLine(
                        PromptTemplates.StakeCoverageAllReferencedDirective,
                        GetTemplateSource("stake-coverage-all-referenced-directive"),
                        "stake-coverage-all-referenced-directive");
                }
                sb.AppendLine();
            }

            int optionCount = context.AvailableStats != null
                ? context.AvailableStats.Length
                : context.MaxDialogueOptions;

            string optionsCountStr = optionCount.ToString();
            string optionsListStr = string.Join(", ", Enumerable.Range(1, optionCount).Select(i => $"OPTION_{i}"));
            string optionsFormatListStr = string.Join(" ", Enumerable.Range(0, optionCount).Select(i => $"OPTION_{i + 1}: [message]"));
            string hfiLine = string.Empty;
            if (context.PlayerHungerForIntimacy.HasValue && context.DateeHungerForIntimacy.HasValue)
            {
                hfiLine = RenderTemplate(
                    PromptTemplates.EngineStateHfiLine,
                    new Dictionary<string, string>
                    {
                        { "player_hfi", context.PlayerHungerForIntimacy.Value.ToString() },
                        { "datee_hfi", context.DateeHungerForIntimacy.Value.ToString() },
                    });
            }

            string torLine = string.Empty;
            if (context.PlayerTerrorOfRejection.HasValue && context.DateeTerrorOfRejection.HasValue)
            {
                torLine = RenderTemplate(
                    PromptTemplates.EngineStateTorLine,
                    new Dictionary<string, string>
                    {
                        { "player_tor", context.PlayerTerrorOfRejection.Value.ToString() },
                        { "datee_tor", context.DateeTerrorOfRejection.Value.ToString() },
                    });
            }

            string cognitiveSubtextLine = string.Empty;
            if (!string.IsNullOrWhiteSpace(context.CognitiveSubtext))
            {
                cognitiveSubtextLine = RenderTemplate(
                    PromptTemplates.EngineStateCognitiveSubtextLine,
                    new Dictionary<string, string>
                    {
                        { "cognitive_subtext", context.CognitiveSubtext ?? string.Empty },
                    });
            }

            string transitionTargetLine = string.Empty;
            string transitionStyleLine = string.Empty;
            if (context.ResolvedTarget != null)
            {
                var target = context.ResolvedTarget.Value;
                transitionTargetLine = RenderTemplate(
                    PromptTemplates.EngineStateTransitionTargetLine,
                    new Dictionary<string, string>
                    {
                        { "transition_target", target.StemText ?? string.Empty },
                        { "transition_scope", "the final option" },
                    });
                transitionStyleLine = RenderTemplate(
                    PromptTemplates.EngineStateTransitionStyleLine,
                    new Dictionary<string, string>
                    {
                        { "transition_style", target.TransitionStyle ?? string.Empty },
                        { "transition_scope", "the final option" },
                    });
            }

            // [ENGINE — Turn N] injection block
            string engineOptionsSource = GetTemplateSource("engine-options-block");
            AppendAnnotatedTemplate(
                sb,
                PromptTemplates.EngineOptionsBlock,
                "engine-options-block",
                new Dictionary<string, (string Value, string SourceFile, string Key)>
                {
                    { "{turn}", (context.CurrentTurn.ToString(), engineOptionsSource, "engine-options-block") },
                    { "{player_name}", (playerName, engineOptionsSource, "engine-options-block") },
                    { "{game_state}", (gameState.ToString().TrimEnd(), engineOptionsSource, "engine-options-block") },
                    { "{hfi_line}", (hfiLine, GetTemplateSource("engine-state-hfi-line"), "engine-state-hfi-line") },
                    { "{tor_line}", (torLine, GetTemplateSource("engine-state-tor-line"), "engine-state-tor-line") },
                    { "{cognitive_subtext_line}", (cognitiveSubtextLine, GetTemplateSource("engine-state-cognitive-subtext-line"), "engine-state-cognitive-subtext-line") },
                    { "{transition_target_line}", (transitionTargetLine, GetTemplateSource("engine-state-transition-target-line"), "engine-state-transition-target-line") },
                    { "{transition_style_line}", (transitionStyleLine, GetTemplateSource("engine-state-transition-style-line"), "engine-state-transition-style-line") },
                    { "{options_count}", (optionsCountStr, engineOptionsSource, "engine-options-block") },
                    { "{options_format_list}", (optionsFormatListStr, engineOptionsSource, "engine-options-block") },
                });

            sb.AppendLine();
            sb.AppendLine();

            // Output format instructions
            if (context.AvailableStats == null || context.AvailableStats.Length == 0)
                throw new InvalidOperationException("AvailableStats cannot be null or empty.");
            string availableStatsStr = string.Join(", ", Array.ConvertAll(context.AvailableStats, StatNameNormalizer.ToWireToken));

            string dialogueOptionsInstruction = PromptTemplates.DialogueOptionsInstruction
                .Replace("{player_name}", playerName)
                .Replace("{available_stats}", availableStatsStr)
                .Replace("{options_count}", optionsCountStr)
                .Replace("{options_list}", optionsListStr);
            sb.Append(dialogueOptionsInstruction, GetTemplateSource("dialogue-options-instruction"), "dialogue-options-instruction");
            sb.AppendLine();
            sb.AppendLine();
            string structuredJsonInstruction = PromptTemplates.DialogueOptionsStructuredJsonInstruction
                .Replace("{available_stats}", availableStatsStr)
                .Replace("{options_count}", optionsCountStr);
            sb.Append(structuredJsonInstruction, GetTemplateSource("dialogue-options-structured-json-instruction"), "dialogue-options-structured-json-instruction");

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
            string dateeCognitiveSubtextLine = string.Empty;
            if (!string.IsNullOrWhiteSpace(context.CognitiveSubtext))
            {
                dateeCognitiveSubtextLine = RenderTemplate(
                    PromptTemplates.EngineStateCognitiveSubtextLine,
                    new Dictionary<string, string>
                    {
                        { "cognitive_subtext", context.CognitiveSubtext ?? string.Empty },
                    });
            }

            string dateeTransitionTargetLine = string.Empty;
            string dateeTransitionStyleLine = string.Empty;
            if (context.ResolvedTarget != null)
            {
                var target = context.ResolvedTarget.Value;
                dateeTransitionTargetLine = RenderTemplate(
                    PromptTemplates.EngineStateTransitionTargetLine,
                    new Dictionary<string, string>
                    {
                        { "transition_target", target.StemText ?? string.Empty },
                        { "transition_scope", "the datee response" },
                    });
                dateeTransitionStyleLine = RenderTemplate(
                    PromptTemplates.EngineStateTransitionStyleLine,
                    new Dictionary<string, string>
                    {
                        { "transition_style", target.TransitionStyle ?? string.Empty },
                        { "transition_scope", "the datee response" },
                    });
            }

            string interestNarrative = PromptTemplates.GetInterestNarrative(context.InterestAfter);
            string engineDateeSource = GetTemplateSource("engine-datee-block");
            AppendAnnotatedTemplate(
                sb,
                PromptTemplates.EngineDateeBlock,
                "engine-datee-block",
                new Dictionary<string, (string Value, string SourceFile, string Key)>
                {
                    { "{datee_name}", (dateeName, engineDateeSource, "engine-datee-block") },
                    { "{interest}", (context.InterestAfter.ToString(), engineDateeSource, "engine-datee-block") },
                    { "{interest_narrative}", (interestNarrative, engineDateeSource, "engine-datee-block") },
                    { "{cognitive_subtext_line}", (dateeCognitiveSubtextLine, GetTemplateSource("engine-state-cognitive-subtext-line"), "engine-state-cognitive-subtext-line") },
                    { "{transition_target_line}", (dateeTransitionTargetLine, GetTemplateSource("engine-state-transition-target-line"), "engine-state-transition-target-line") },
                    { "{transition_style_line}", (dateeTransitionStyleLine, GetTemplateSource("engine-state-transition-style-line"), "engine-state-transition-style-line") },
                });
            sb.AppendLine();

            sb.AppendLine();

            // Interest change delta
            int delta = context.InterestAfter - context.InterestBefore;
            string deltaStr = delta >= 0 ? $"+{delta}" : delta.ToString();
            sb.AppendLine($"Interest moved from {context.InterestBefore} to {context.InterestAfter} ({deltaStr}).");

            if (context.ActiveTrapInstructions != null && context.ActiveTrapInstructions.Length > 0)
            {
                sb.AppendLine();
                sb.AppendLine("ACTIVE TRAP INSTRUCTIONS");
                foreach (var instruction in context.ActiveTrapInstructions)
                {
                    sb.AppendLine(instruction);
                }
            }

            if (AppendShadowTaintBlock(sb, context.ShadowThresholds, "datee-shadow-state-heading", PromptTemplates.DateeShadowStateHeading))
            {
                sb.AppendLine();
            }

            // Inject active archetype directive for datee
            if (!string.IsNullOrEmpty(context.ActiveArchetypeDirective))
            {
                sb.AppendLine();
                sb.AppendLine(context.ActiveArchetypeDirective, "data/prompts/archetypes.yaml", "active-archetype-directive");
            }

            sb.AppendLine();

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
