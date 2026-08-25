using System;
using System.Collections.Generic;
using Pinder.Core.Conversation;
using Pinder.Core.Stats;

namespace Pinder.LlmAdapters
{
    /// <summary>
    /// Stable key derivation and validation for DATEE emotional-reaction
    /// direction prompts loaded through <see cref="PromptCatalog"/>.
    /// </summary>
    public static class EmotionalReactionPromptCatalog
    {
        public static IReadOnlyList<string> OutcomeKeys =>
            StatDeliveryInstructions.OutcomeTierKeys;

        public static readonly IReadOnlyList<string> RelationshipTransitionKeys = Array.AsReadOnly(new[]
        {
            "strengthened",
            "preserved",
            "damaged",
            "transformed",
        });

        private static readonly HashSet<string> OutcomeKeySet =
            new HashSet<string>(OutcomeKeys, StringComparer.Ordinal);

        private static readonly HashSet<string> RelationshipTransitionKeySet =
            new HashSet<string>(RelationshipTransitionKeys, StringComparer.Ordinal);

        public static string GetInterestStateMeaningKey(InterestState state)
        {
            switch (state)
            {
                case InterestState.Unmatched:
                    return "emotional-reaction-interest-unmatched";
                case InterestState.Bored:
                    return "emotional-reaction-interest-bored";
                case InterestState.Lukewarm:
                    return "emotional-reaction-interest-lukewarm";
                case InterestState.Interested:
                    return "emotional-reaction-interest-interested";
                case InterestState.VeryIntoIt:
                    return "emotional-reaction-interest-very-into-it";
                case InterestState.AlmostThere:
                    return "emotional-reaction-interest-almost-there";
                case InterestState.DateSecured:
                    return "emotional-reaction-interest-date-secured";
                default:
                    throw new InvalidOperationException($"Unknown interest state '{state}'.");
            }
        }

        public static string GetRelationshipTransitionInstructionKey(string transitionKey)
        {
            if (transitionKey is null) throw new ArgumentNullException(nameof(transitionKey));
            if (!RelationshipTransitionKeySet.Contains(transitionKey))
            {
                throw new ArgumentException(
                    $"Unknown relationship transition key '{transitionKey}'.",
                    nameof(transitionKey));
            }

            return "emotional-reaction-transition-" + transitionKey;
        }

        public static string GetRelationshipTransitionKey(
            InterestState before,
            InterestState after)
        {
            if (before == after)
                return "preserved";

            if (after == InterestState.Unmatched || after == InterestState.DateSecured)
                return "transformed";

            return after > before ? "strengthened" : "damaged";
        }

        public static string GetEventMeaningKey(StatType stat, string outcomeKey)
        {
            if (outcomeKey is null) throw new ArgumentNullException(nameof(outcomeKey));
            if (!OutcomeKeySet.Contains(outcomeKey))
            {
                throw new ArgumentException(
                    $"Unknown emotional reaction outcome key '{outcomeKey}'.",
                    nameof(outcomeKey));
            }

            return "emotional-reaction-event-" + StatKey(stat) + "-" + outcomeKey;
        }

        public static string GetInterestStateMeaning(PromptCatalog catalog, InterestState state)
        {
            if (catalog is null) throw new ArgumentNullException(nameof(catalog));
            return RequireSystemPrompt(catalog, GetInterestStateMeaningKey(state));
        }

        public static string GetRelationshipTransitionInstruction(PromptCatalog catalog, string transitionKey)
        {
            if (catalog is null) throw new ArgumentNullException(nameof(catalog));
            return RequireSystemPrompt(catalog, GetRelationshipTransitionInstructionKey(transitionKey));
        }

        public static string GetEventMeaning(PromptCatalog catalog, StatType stat, string outcomeKey)
        {
            if (catalog is null) throw new ArgumentNullException(nameof(catalog));
            return RequireSystemPrompt(catalog, GetEventMeaningKey(stat, outcomeKey));
        }

        internal static void ValidateRuntimeCatalog(PromptCatalog catalog)
        {
            if (catalog is null) throw new ArgumentNullException(nameof(catalog));

            foreach (InterestState state in Enum.GetValues(typeof(InterestState)))
            {
                RequireSystemPrompt(catalog, GetInterestStateMeaningKey(state));
            }

            foreach (string transitionKey in RelationshipTransitionKeys)
            {
                string key = GetRelationshipTransitionInstructionKey(transitionKey);
                string prompt = RequireSystemPrompt(catalog, key);
                RequirePlaceholder(key, prompt, "prior_relationship");
                RequirePlaceholder(key, prompt, "resulting_relationship");
            }

            foreach (StatType stat in Enum.GetValues(typeof(StatType)))
            {
                foreach (string outcomeKey in OutcomeKeys)
                {
                    RequireSystemPrompt(catalog, GetEventMeaningKey(stat, outcomeKey));
                }
            }

            RequireSystemPromptWithPlaceholders(
                catalog,
                "emotional-reaction-compiled-wrapper",
                "prior_relationship",
                "resulting_relationship",
                "transition_meaning",
                "recipient_event_meaning",
                "delivered_message",
                "recent_visible_history",
                "character_formulation");
            RequireSystemPromptWithPlaceholders(
                catalog,
                "emotional-reaction-compiled-session-wrapper",
                "prior_relationship",
                "resulting_relationship",
                "transition_meaning",
                "recipient_event_meaning",
                "delivered_message",
                "character_formulation");
            RequireSystemPromptWithPlaceholders(
                catalog,
                "emotional-reaction-character-success-wrapper",
                "derived_feeling",
                "defense_reaction",
                "stat_reaction",
                "safe_connection");
            RequireSystemPromptWithPlaceholders(
                catalog,
                "emotional-reaction-character-failure-wrapper",
                "derived_feeling",
                "defense_reaction",
                "stat_reaction",
                "hurt_protection",
                "repair_requirement");
            RequireSystemPromptWithPlaceholders(
                catalog,
                "emotional-reaction-history-line",
                "sender",
                "message");
            RequireSystemPrompt(catalog, "emotional-reaction-history-empty");
            RequireSystemPromptWithPlaceholders(
                catalog,
                "emotional-reaction-performance-direction",
                "primary_emotion",
                "intensity",
                "underlying_feeling",
                "interpretation",
                "impulse",
                "restraint",
                "response_posture");
            EmotionalDirectionLeakGuard.ValidatePerformanceTemplate(
                RequireSystemPrompt(catalog, "emotional-reaction-performance-direction"),
                "emotional-reaction-performance-direction");
            RequireCompletePromptWithPlaceholders(
                catalog,
                "emotional-reaction-director",
                new[] { "emotion_vocabulary" },
                new[] { "compiled_reaction_input" });
            RequireSystemPromptWithPlaceholders(
                catalog,
                EmotionalPromptCompiler.DirectorSystemWrapperPromptKey,
                "datee_system_prompt",
                "director_system_prompt");
            RequireSystemPrompt(
                catalog,
                EmotionalPromptCompiler.DirectorContractRepairPromptKey);
            RequireSystemPrompt(
                catalog,
                EmotionalPromptCompiler.DirectorDraftedChatReplyRepairPromptKey);
            RequireSystemPrompt(
                catalog,
                EmotionalPromptCompiler.DirectorResponsePostureOmitsPrimaryEmotionRepairPromptKey);
            RequireSystemPromptWithPlaceholders(
                catalog,
                EmotionalPromptCompiler.DirectorUnsupportedPrimaryEmotionRepairPromptKey,
                "emotion_vocabulary");
            RequireSystemPrompt(catalog, CharacterEmotionCatalog.PromptKey);
            RequireSystemPromptWithPlaceholders(
                catalog,
                "avatar-emotional-director-system-wrapper",
                "avatar_system_prompt",
                "director_system_prompt");
            RequireSystemPromptWithPlaceholders(
                catalog,
                "avatar-emotional-director-input",
                "relationship_meaning",
                "datee_profile",
                "datee_last_message",
                "cognitive_subtext",
                "transition_target",
                "transition_style");
            RequireSystemPromptWithPlaceholders(
                catalog,
                "avatar-emotional-performance-direction",
                "primary_emotion",
                "intensity",
                "underlying_feeling",
                "interpretation",
                "impulse",
                "restraint",
                "response_posture");
        }

        private static string RequireSystemPrompt(PromptCatalog catalog, string key)
        {
            var entry = catalog.TryGet(key)
                ?? throw new InvalidOperationException(
                    $"prompt-catalog: missing required runtime prompt key '{key}'. The yaml file is incomplete or missing.");

            if (string.IsNullOrWhiteSpace(entry.SystemPrompt))
            {
                throw new InvalidOperationException(
                    $"prompt-catalog: runtime prompt key '{key}' has no system_prompt. Check the yaml file.");
            }

            return entry.SystemPrompt!;
        }

        private static void RequirePlaceholder(string key, string prompt, string token)
        {
            string placeholder = "{" + token + "}";
            if (prompt.IndexOf(placeholder, StringComparison.Ordinal) < 0)
            {
                throw new InvalidOperationException(
                    $"prompt-catalog: key '{key}' system_prompt must include required token '{placeholder}'.");
            }
        }

        private static void RequireSystemPromptWithPlaceholders(
            PromptCatalog catalog,
            string key,
            params string[] tokens)
        {
            string prompt = RequireSystemPrompt(catalog, key);
            foreach (string token in tokens)
                RequirePlaceholder(key, prompt, token);
        }

        private static void RequireCompletePromptWithPlaceholders(
            PromptCatalog catalog,
            string key,
            IReadOnlyList<string> systemTokens,
            IReadOnlyList<string> userTokens)
        {
            var entry = catalog.RequireCompleteEntry(
                key,
                $"prompt-catalog: missing required runtime prompt key '{key}'. The yaml file is incomplete or missing.");
            foreach (string token in systemTokens)
                RequirePlaceholderInTemplate(key, entry.SystemPrompt!, "system_prompt", token);
            foreach (string token in userTokens)
                RequirePlaceholderInTemplate(key, entry.UserTemplate!, "user_template", token);
        }

        private static void RequirePlaceholderInTemplate(
            string key,
            string template,
            string field,
            string token)
        {
            string placeholder = "{" + token + "}";
            if (template.IndexOf(placeholder, StringComparison.Ordinal) < 0)
            {
                throw new InvalidOperationException(
                    $"prompt-catalog: key '{key}' {field} must include required token '{placeholder}'.");
            }
        }

        private static string StatKey(StatType stat)
        {
            switch (stat)
            {
                case StatType.Charm:
                    return "charm";
                case StatType.Rizz:
                    return "rizz";
                case StatType.Honesty:
                    return "honesty";
                case StatType.Chaos:
                    return "chaos";
                case StatType.Wit:
                    return "wit";
                case StatType.SelfAwareness:
                    return "self-awareness";
                default:
                    throw new InvalidOperationException($"Unknown stat '{stat}'.");
            }
        }
    }
}
