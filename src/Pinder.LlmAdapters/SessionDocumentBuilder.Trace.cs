using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Pinder.Core.Conversation;
using Pinder.Core.Prompts;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;
using Pinder.LlmAdapters.Anthropic;
using Pinder.Core.Text;

namespace Pinder.LlmAdapters
{
    public static partial class SessionDocumentBuilder
    {
        internal const string CharacterEmotionalDirectionRuntimeSource =
            PromptTraceDiagnosticContract.CharacterEmotionalDirectionRuntimeSource;

        private static string GetTemplateSource(PromptCatalog? promptCatalog, string key)
        {
            return PromptCatalog.ResolveCatalogOrThrow(promptCatalog).TryGet(key)?.SourceFile
                ?? "data/prompts/templates.yaml";
        }

        private static string GetTemplate(PromptCatalog? promptCatalog, string key)
        {
            return PromptTemplates.GetCatalogString(promptCatalog, key);
        }

        private static string RenderTemplate(string template, IReadOnlyDictionary<string, string> values)
        {
            return PromptCatalog.Substitute(template, values);
        }

        private static void AppendAnnotatedTemplate(
            AnnotatedStringBuilder sb,
            string template,
            string templateKey,
            IReadOnlyDictionary<string, (string Value, string SourceFile, string Key)> values,
            PromptCatalog? promptCatalog)
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
                    sb.Append(template.Substring(position), GetTemplateSource(promptCatalog, templateKey), templateKey);
                    break;
                }

                if (nextIndex > position)
                {
                    sb.Append(
                        template.Substring(position, nextIndex - position),
                        GetTemplateSource(promptCatalog, templateKey),
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
            string heading,
            PromptCatalog? promptCatalog)
        {
            if (thresholds == null || thresholds.Count == 0) return false;

            var keys = GetActiveShadowTaintKeys(thresholds).ToList();
            if (keys.Count == 0) return false;

            sb.AppendLine(heading, GetTemplateSource(promptCatalog, headingKey), headingKey);
            foreach (var key in keys)
            {
                sb.AppendLine(
                    GetTemplate(promptCatalog, key),
                    GetTemplateSource(promptCatalog, key),
                    key);
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

        /// <summary>
        /// Builds the user-message content for GetDialogueOptionsAsync and returns the trace data.
        /// </summary>
        /// <remarks>
        /// <para><strong>#1208 immutable-first contract: DOCUMENTED EXCEPTION</strong></para>
        /// <para>This builder CANNOT be safely reordered to immutable-first. Its engine blocks interpolate volatile state,
        /// and trailing static instructions contain positional back-references. Changing this order breaks rendered semantics.
        /// See docs/prompt-cache-ordering.md and pinning tests for details.</para>
        /// </remarks>
        public static PromptTraceResult BuildDialogueOptionsPromptEx(
            DialogueContext context,
            PromptCatalog? promptCatalog = null)
            => BuildDialogueOptionsPromptCore(context, promptCatalog, includeConversationHistory: true);

        /// <summary>
        /// Returns the exact annotated ENGINE_STATE block embedded in the dialogue-options prompt.
        /// </summary>
        public static string BuildDialogueOptionsEngineStateInstruction(
            DialogueContext context,
            PromptCatalog? promptCatalog = null)
            => ExtractAnnotatedInstruction(
                BuildDialogueOptionsPromptEx(context, promptCatalog),
                "engine-options-block");

        /// <summary>
        /// Extracts one complete configured instruction from an annotated prompt without
        /// re-rendering it. Replacement spans between the template spans are retained.
        /// </summary>
        public static string ExtractAnnotatedInstruction(PromptTraceResult prompt, string templateKey)
        {
            if (prompt == null) throw new ArgumentNullException(nameof(prompt));
            if (string.IsNullOrWhiteSpace(templateKey))
                throw new ArgumentException("Template key must not be blank.", nameof(templateKey));

            var templateSpans = prompt.Spans
                .Where(span => string.Equals(span.Key, templateKey, StringComparison.Ordinal))
                .ToArray();
            if (templateSpans.Length == 0)
                throw new InvalidOperationException(
                    $"Compiled prompt does not contain annotated template '{templateKey}'.");

            int start = templateSpans.Min(span => span.Start);
            int literalEnd = templateSpans.Max(span => span.End);
            int end = prompt.Spans
                .Where(span => span.Start >= start && span.Start <= literalEnd)
                .Select(span => span.End)
                .DefaultIfEmpty(literalEnd)
                .Max();
            return prompt.Text.Substring(start, end - start).Trim();
        }

        internal static PromptTraceResult BuildDialogueOptionsSessionPromptEx(
            DialogueContext context,
            PromptCatalog? promptCatalog = null)
            => BuildDialogueOptionsPromptCore(context, promptCatalog, includeConversationHistory: false);

        private static PromptTraceResult BuildDialogueOptionsPromptCore(
            DialogueContext context,
            PromptCatalog? promptCatalog,
            bool includeConversationHistory)
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

            if (includeConversationHistory)
            {
                var historySb = new StringBuilder();
                HistoryFormatter.Format(historySb, context.ConversationHistory, playerName);
                sb.Append(historySb.ToString(), "conversation-history", "conversation-history");
                sb.AppendLine();
            }

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
                string runtimeFraming = GetTemplate(
                    promptCatalog,
                    PromptBuilder.TextingStyleRuntimeFramingKey);
                sb.AppendLine(
                    runtimeFraming,
                    GetTemplateSource(promptCatalog, PromptBuilder.TextingStyleRuntimeFramingKey),
                    PromptBuilder.TextingStyleRuntimeFramingKey);
                sb.AppendLine(context.PlayerTextingStyle, "runtime:dialogue-context", "DialogueContext.PlayerTextingStyle");
                sb.AppendLine();
            }

            if (AppendShadowTaintBlock(
                sb,
                context.ShadowThresholds,
                "shadow-state-heading",
                GetTemplate(promptCatalog, "shadow-state-heading"),
                promptCatalog))
            {
                sb.AppendLine();
            }

            // Cold-opener guard: fires only on the genuine first turn (nobody has spoken yet).
            // Keyed on empty history rather than a turn integer so it is robust to the
            // 0-based, end-of-turn-incremented counter (issue #1155).
            if (context.ConversationHistory.Count == 0)
            {
                sb.AppendLine(
                    GetTemplate(promptCatalog, "cold-opener-rule"),
                    GetTemplateSource(promptCatalog, "cold-opener-rule"),
                    "cold-opener-rule");
                sb.AppendLine();
            }

            // Turn 3+ pivot directive
            if (context.CurrentTurn >= 3)
            {
                sb.AppendLine(
                    GetTemplate(promptCatalog, "pivot-directive"),
                    GetTemplateSource(promptCatalog, "pivot-directive"),
                    "pivot-directive");
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
                    GetTemplate(promptCatalog, "stake-coverage-summary"),
                    new Dictionary<string, string>
                    {
                        { "referenced_count", referencedCount.ToString() },
                        { "untouched_count", untouchedIndices.Count.ToString() },
                    });
                sb.AppendLine(stakeCoverageSummary, GetTemplateSource(promptCatalog, "stake-coverage-summary"), "stake-coverage-summary");
                if (untouchedIndices.Count > 0)
                {
                    sb.AppendLine(
                        GetTemplate(promptCatalog, "stake-coverage-untouched-directive"),
                        GetTemplateSource(promptCatalog, "stake-coverage-untouched-directive"),
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
                        GetTemplate(promptCatalog, "stake-coverage-all-referenced-directive"),
                        GetTemplateSource(promptCatalog, "stake-coverage-all-referenced-directive"),
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
                    GetTemplate(promptCatalog, "engine-state-hfi-line"),
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
                    GetTemplate(promptCatalog, "engine-state-tor-line"),
                    new Dictionary<string, string>
                    {
                        { "player_tor", context.PlayerTerrorOfRejection.Value.ToString() },
                        { "datee_tor", context.DateeTerrorOfRejection.Value.ToString() },
                    });
            }

            string cognitiveSubtextLine = string.Empty;
            if (!string.IsNullOrWhiteSpace(context.CognitiveSubtextFact?.Text))
            {
                cognitiveSubtextLine = RenderTemplate(
                    GetTemplate(promptCatalog, "engine-state-cognitive-subtext-line"),
                    new Dictionary<string, string>
                    {
                        { "cognitive_subtext", context.CognitiveSubtextFact?.Text ?? string.Empty },
                    });
            }

            string avatarEmotionalDirection = string.Empty;
            if (context.AvatarEmotionalDirection != null)
            {
                CharacterEmotionalDirection direction = context.AvatarEmotionalDirection;
                avatarEmotionalDirection = RenderTemplate(
                    GetTemplate(promptCatalog, "avatar-emotional-performance-direction"),
                    new Dictionary<string, string>
                    {
                        { "primary_emotion", direction.PrimaryEmotion },
                        { "secondary_emotion", direction.SecondaryEmotion },
                        { "regulatory_state", direction.RegulatoryState },
                        { "activation", direction.Activation.ToString(System.Globalization.CultureInfo.InvariantCulture) },
                        { "trajectory", direction.Trajectory },
                        { "core_threat_or_desire", direction.CoreThreatOrDesire },
                        { "interpretation", direction.Interpretation },
                        { "impulse", direction.Impulse },
                        { "restraint", direction.Restraint },
                        { "response_posture", direction.ResponsePosture },
                    }) + "\n";
            }

            string transitionTargetLine = string.Empty;
            string transitionStyleLine = string.Empty;
            if (context.AvatarRevelationTarget != null)
            {
                ResolvedRevelationTarget target = context.AvatarRevelationTarget.ResolvedTarget;
                transitionTargetLine = RenderTemplate(
                    GetTemplate(promptCatalog, "engine-state-transition-target-line"),
                    new Dictionary<string, string>
                    {
                        { "transition_target", target.StemText ?? string.Empty },
                        { "transition_scope", "the final option" },
                    });
                transitionStyleLine = RenderTemplate(
                    GetTemplate(promptCatalog, "engine-state-transition-style-line"),
                    new Dictionary<string, string>
                    {
                        { "transition_style", target.TransitionStyle ?? string.Empty },
                        { "transition_scope", "the final option" },
                    });
            }

            // [ENGINE — Turn N] injection block
            string engineOptionsSource = GetTemplateSource(promptCatalog, "engine-options-block");
            AppendAnnotatedTemplate(
                sb,
                GetTemplate(promptCatalog, "engine-options-block"),
                "engine-options-block",
                new Dictionary<string, (string Value, string SourceFile, string Key)>
                {
                    { "{turn}", (context.CurrentTurn.ToString(), engineOptionsSource, "engine-options-block") },
                    { "{player_name}", (playerName, engineOptionsSource, "engine-options-block") },
                    { "{game_state}", (gameState.ToString().TrimEnd(), engineOptionsSource, "engine-options-block") },
                    { "{hfi_line}", (hfiLine, GetTemplateSource(promptCatalog, "engine-state-hfi-line"), "engine-state-hfi-line") },
                    { "{tor_line}", (torLine, GetTemplateSource(promptCatalog, "engine-state-tor-line"), "engine-state-tor-line") },
                    { "{cognitive_subtext_line}", (cognitiveSubtextLine, GetTemplateSource(promptCatalog, "engine-state-cognitive-subtext-line"), "engine-state-cognitive-subtext-line") },
                    { "{avatar_emotional_direction}", (avatarEmotionalDirection, GetTemplateSource(promptCatalog, "avatar-emotional-performance-direction"), "avatar-emotional-performance-direction") },
                    { "{transition_target_line}", (transitionTargetLine, GetTemplateSource(promptCatalog, "engine-state-transition-target-line"), "engine-state-transition-target-line") },
                    { "{transition_style_line}", (transitionStyleLine, GetTemplateSource(promptCatalog, "engine-state-transition-style-line"), "engine-state-transition-style-line") },
                    { "{options_count}", (optionsCountStr, engineOptionsSource, "engine-options-block") },
                    { "{options_format_list}", (optionsFormatListStr, engineOptionsSource, "engine-options-block") },
                },
                promptCatalog);

            sb.AppendLine();
            sb.AppendLine();

            // Output format instructions
            if (context.AvailableStats == null || context.AvailableStats.Length == 0)
                throw new InvalidOperationException("AvailableStats cannot be null or empty.");
            string availableStatsStr = string.Join(", ", Array.ConvertAll(context.AvailableStats, StatNameNormalizer.ToWireToken));

            string dialogueOptionsInstruction = GetTemplate(promptCatalog, "dialogue-options-instruction")
                .Replace("{player_name}", playerName)
                .Replace("{available_stats}", availableStatsStr)
                .Replace("{options_count}", optionsCountStr)
                .Replace("{options_list}", optionsListStr);
            sb.Append(dialogueOptionsInstruction, GetTemplateSource(promptCatalog, "dialogue-options-instruction"), "dialogue-options-instruction");
            sb.AppendLine();
            sb.AppendLine();
            string structuredJsonInstruction = GetTemplate(promptCatalog, "dialogue-options-structured-json-instruction")
                .Replace("{available_stats}", availableStatsStr)
                .Replace("{options_count}", optionsCountStr);
            sb.Append(structuredJsonInstruction, GetTemplateSource(promptCatalog, "dialogue-options-structured-json-instruction"), "dialogue-options-structured-json-instruction");

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
        public static PromptTraceResult BuildDateePromptEx(
            DateeContext context,
            PromptCatalog? promptCatalog = null)
        {
            throw new InvalidOperationException(
                "DATEE performance prompt compilation requires an accepted datee_response_plan.v1.");
        }

        internal static PromptTraceResult BuildDateePerformancePromptEx(
            DateeContext context,
            DateeResponsePlan responsePlan,
            PromptCatalog? promptCatalog = null,
            bool includeConversationHistory = true)
        {
            if (responsePlan == null) throw new ArgumentNullException(nameof(responsePlan));
            return BuildDateePromptCore(context, responsePlan, promptCatalog, includeConversationHistory);
        }

        internal static PromptTraceResult BuildDateePerformancePromptEx(
            DateeContext context,
            CharacterEmotionalDirection emotionalDirection,
            PromptCatalog? promptCatalog = null,
            bool includeConversationHistory = true)
        {
            if (emotionalDirection == null) throw new ArgumentNullException(nameof(emotionalDirection));

            PromptCatalog catalog = PromptCatalog.ResolveCatalogOrThrow(promptCatalog);
            return new EmotionalPromptCompiler(catalog).CompilePerformance(
                context,
                emotionalDirection,
                includeConversationHistory);
        }

        private static PromptTraceResult BuildDateePromptCore(
            DateeContext context,
            DateeResponsePlan responsePlan,
            PromptCatalog? promptCatalog,
            bool includeConversationHistory = true)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (string.IsNullOrEmpty(context.PlayerName)) throw new ArgumentException("PlayerName cannot be null or empty.");
            if (string.IsNullOrEmpty(context.DateeName)) throw new ArgumentException("DateeName cannot be null or empty.");

            var playerName = context.PlayerName;
            var dateeName = context.DateeName;

            var sb = new AnnotatedStringBuilder();

            string playerAvatarCard = context.PlayerAvatarCard.Render();
            if (!string.IsNullOrWhiteSpace(playerAvatarCard))
            {
                sb.AppendLine("PLAYER AVATAR PUBLIC DATING CARD");
                sb.AppendLine(
                    playerAvatarCard,
                    "runtime:datee-context",
                    "DateeContext.PlayerAvatarCard");
                sb.AppendLine();
            }

            if (includeConversationHistory)
            {
                var historySb = new StringBuilder();
                HistoryFormatter.Format(historySb, context.ConversationHistory, playerName);
                sb.Append(historySb.ToString(), "conversation-history", "conversation-history");
                sb.AppendLine();
            }

            // Player's last message with failure context
            if (context.DeliveryTier != FailureTier.Success)
            {
                string tierLabel = GetFailureTierName(context.DeliveryTier);
                sb.AppendLine($"PLAYER'S LAST MESSAGE (delivered after a {tierLabel}):");
                sb.AppendLine();
                sb.AppendLine("FAILURE CONTEXT");
                sb.AppendLine(GetDateeReactionGuidance(context.DeliveryTier, promptCatalog));
            }
            else
            {
                sb.AppendLine("PLAYER'S LAST MESSAGE");
            }

            sb.AppendLine();

            if (context.HorninessOverlayApplied)
            {
                string horninessGuidance = GetHorninessReactionGuidance(
                    context.InterestAfter,
                    context.HorninessOverlayApplied,
                    context.HorninessTier,
                    promptCatalog);
                string templateKey = context.InterestAfter < HorninessWarmthThreshold 
                    ? "datee-horniness-reaction-below-threshold" 
                    : "datee-horniness-reaction-high-interest";
                sb.AppendLine("HORNINESS REACTION GUIDANCE");
                sb.AppendLine(
                    horninessGuidance,
                    GetTemplateSource(promptCatalog, templateKey),
                    templateKey);
                sb.AppendLine();
            }

            // One accepted response plan is the sole private behavior block.
            InterestState dateeInterestState = context.InterestAfterState;
            string interestNarrativeKey = PromptTemplates.GetInterestNarrativeKey(dateeInterestState);
            string interestNarrative = GetTemplate(promptCatalog, interestNarrativeKey);
            string engineDateeSource = GetTemplateSource(promptCatalog, "engine-datee-block");
            AppendAnnotatedTemplate(
                sb,
                GetTemplate(promptCatalog, "engine-datee-block"),
                "engine-datee-block",
                new Dictionary<string, (string Value, string SourceFile, string Key)>
                {
                    { "{datee_name}", (dateeName, engineDateeSource, "engine-datee-block") },
                    { "{interest}", (context.InterestAfter.ToString(), engineDateeSource, "engine-datee-block") },
                    { "{interest_narrative}", (interestNarrative, GetTemplateSource(promptCatalog, interestNarrativeKey), interestNarrativeKey) },
                },
                promptCatalog);
            sb.AppendLine();

            string planJson = DateeResponsePlanJson.Serialize(responsePlan);
            AppendAnnotatedTemplate(
                sb,
                GetTemplate(promptCatalog, "datee-response-plan-performance"),
                "datee-response-plan-performance",
                new Dictionary<string, (string Value, string SourceFile, string Key)>
                {
                    { "{response_plan_json}", (planJson, "runtime:datee-response-plan", DateeResponsePlan.CurrentSchemaVersion) },
                },
                promptCatalog);
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

            if (AppendShadowTaintBlock(
                sb,
                context.ShadowThresholds,
                "datee-shadow-state-heading",
                GetTemplate(promptCatalog, "datee-shadow-state-heading"),
                promptCatalog))
            {
                sb.AppendLine();
            }

            // Inject active archetype directive for datee
            if (!string.IsNullOrEmpty(context.ActiveArchetypeDirective))
            {
                sb.AppendLine();
                sb.AppendLine(context.ActiveArchetypeDirective, "data/prompts/archetypes.yaml", "active-archetype-directive");
            }

            if (!string.IsNullOrWhiteSpace(context.DateeTextingStyle))
            {
                sb.AppendLine();
                string runtimeFraming = GetTemplate(
                    promptCatalog,
                    PromptBuilder.TextingStyleRuntimeFramingKey);
                sb.AppendLine(
                    runtimeFraming,
                    GetTemplateSource(promptCatalog, PromptBuilder.TextingStyleRuntimeFramingKey),
                    PromptBuilder.TextingStyleRuntimeFramingKey);
                sb.AppendLine(context.DateeTextingStyle, "runtime:datee-context", "DateeContext.DateeTextingStyle");
            }

            sb.AppendLine();

            string resistanceKey = PromptTemplates.GetResistanceKey(dateeInterestState);
            string resistanceBlock = GetResistanceBlock(
                context.InterestAfter,
                dateeInterestState,
                promptCatalog);

            const string lengthHint =
                "Keep it to a natural text-message length guided by your designated texting-style length axis.";

            AppendAnnotatedTemplate(
                sb,
                GetTemplate(promptCatalog, "datee-response-instruction"),
                "datee-response-instruction",
                new Dictionary<string, (string Value, string SourceFile, string Key)>
                {
                    { "{resistance_block}", (resistanceBlock, GetTemplateSource(promptCatalog, resistanceKey), resistanceKey) },
                    { "{length_hint}", (lengthHint, GetTemplateSource(promptCatalog, "datee-response-instruction"), "datee-response-instruction") },
                },
                promptCatalog);

            return new PromptTraceResult(sb.ToString(), sb.Spans);
        }
    }
}
