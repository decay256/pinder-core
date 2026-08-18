using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using Pinder.Core.Conversation;
using Pinder.Core.Diagnostics.AgentJournals;
using Pinder.Core.Stats;
using Pinder.Core.Text;
using Pinder.LlmAdapters.AgentJournals;

namespace Pinder.LlmAdapters
{
    public sealed class GameRunPromptDocumentPair
    {
        public GameRunPromptDocumentPair(
            AnnotatedInvocationDocument system,
            AnnotatedInvocationDocument user)
        {
            System = system ?? throw new ArgumentNullException(nameof(system));
            User = user ?? throw new ArgumentNullException(nameof(user));
        }

        public AnnotatedInvocationDocument System { get; }

        public AnnotatedInvocationDocument User { get; }
    }

    public static class GameRunPromptDocumentBuilder
    {
        private static readonly IPromptTraceSourceIdentityResolver TraceSourceResolver =
            GameRunPromptSourceIdentityResolver.Instance;

        public static AnnotatedInvocationDocument BuildPlayerAvatarSystemDocument(
            string playerAvatarPrompt,
            GameDefinition gameDefinition)
        {
            PromptTraceResult trace = SessionSystemPromptBuilder.BuildPlayerAvatarEx(
                playerAvatarPrompt,
                gameDefinition);
            return FromTrace(
                trace,
                "dialogue-options.system",
                AgentJournalInputRole.System,
                "session-system-prompt");
        }

        public static AnnotatedInvocationDocument BuildDateeSystemDocument(
            string dateePrompt,
            GameDefinition gameDefinition)
        {
            PromptTraceResult trace = SessionSystemPromptBuilder.BuildDateeEx(
                dateePrompt,
                gameDefinition);
            return FromTrace(
                trace,
                "session.system",
                AgentJournalInputRole.System,
                "session-system-prompt");
        }

        public static AnnotatedInvocationDocument BuildDialogueOptionsUserDocument(
            DialogueContext context,
            PromptCatalog? promptCatalog)
        {
            PromptTraceResult trace = SessionDocumentBuilder.BuildDialogueOptionsPromptEx(
                context,
                promptCatalog);
            return FromTrace(
                trace,
                "dialogue-options.user",
                AgentJournalInputRole.User,
                "dialogue-options-user-prompt");
        }

        internal static AnnotatedInvocationDocument BuildDialogueOptionsSessionUserDocument(
            DialogueContext context,
            PromptCatalog? promptCatalog)
        {
            PromptTraceResult trace = SessionDocumentBuilder.BuildDialogueOptionsSessionPromptEx(
                context,
                promptCatalog);
            return FromTrace(
                trace,
                "dialogue-options.user",
                AgentJournalInputRole.User,
                "dialogue-options-user-prompt");
        }

        public static AnnotatedInvocationDocument BuildDateeUserDocument(
            DateeContext context,
            PromptCatalog? promptCatalog)
        {
            PromptTraceResult trace = SessionDocumentBuilder.BuildDateePromptEx(
                context,
                promptCatalog);
            return FromTrace(
                trace,
                "session.user",
                AgentJournalInputRole.User,
                "datee-session-user-prompt");
        }

        public static AnnotatedInvocationDocument BuildEmotionalDirectorSystemDocument(
            PromptTraceResult trace)
            => FromTrace(
                trace,
                "datee.emotional-director.system",
                AgentJournalInputRole.System,
                "emotional-director-system-prompt");

        public static AnnotatedInvocationDocument BuildEmotionalDirectorUserDocument(
            PromptTraceResult trace)
            => FromTrace(
                trace,
                "datee.emotional-director.user",
                AgentJournalInputRole.User,
                "emotional-director-user-prompt");

        public static AnnotatedInvocationDocument BuildDateePerformanceDocument(
            PromptTraceResult trace)
            => FromTrace(
                trace,
                "datee.performance",
                AgentJournalInputRole.User,
                "datee-performance-prompt");

        public static GameRunPromptDocumentPair BuildDramaticArcDocuments(
            PromptEntry entry,
            IReadOnlyDictionary<string, string> values)
        {
            if (entry == null) throw new ArgumentNullException(nameof(entry));
            if (values == null) throw new ArgumentNullException(nameof(values));
            string systemTemplate = RequireConfiguredPrompt(
                entry.SystemPrompt,
                "dramatic_arc.system_prompt",
                nameof(BuildDramaticArcDocuments));
            string userTemplate = RequireConfiguredPrompt(
                entry.UserTemplate,
                "dramatic_arc.user_template",
                nameof(BuildDramaticArcDocuments));

            var substitutions = RuntimeSubstitutions(values);
            AnnotatedInvocationDocument system = new AnnotatedInvocationDocumentBuilder()
                .AppendTemplate(
                    systemTemplate,
                    substitutions,
                    CatalogSource(entry.SourceFile, "dramatic_arc.system_prompt", systemTemplate))
                .Build(
                    "game.setup.dramatic-arc.system",
                    AgentJournalInputRole.System,
                    "dramatic-arc-system-prompt");
            AnnotatedInvocationDocument user = new AnnotatedInvocationDocumentBuilder()
                .AppendTemplate(
                    userTemplate,
                    substitutions,
                    CatalogSource(entry.SourceFile, "dramatic_arc.user_template", userTemplate))
                .Build(
                    "game.setup.dramatic-arc.user",
                    AgentJournalInputRole.User,
                    "dramatic-arc-user-prompt");

            return new GameRunPromptDocumentPair(system, user);
        }

        public static GameRunPromptDocumentPair? BuildSuccessImprovementDocuments(
            SuccessImprovementContext context,
            StatDeliveryInstructions? instructions,
            GameDefinition gameDefinition,
            PromptCatalog? promptCatalog)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (gameDefinition == null) throw new ArgumentNullException(nameof(gameDefinition));

            string? template = instructions?.Get(context.Stat, context.TierKey);
            if (string.IsNullOrWhiteSpace(template))
            {
                return null;
            }

            var instructionValues = new Dictionary<string, AnnotatedInvocationDocument>(StringComparer.OrdinalIgnoreCase)
            {
                ["player_name"] = RuntimeFragment(context.PlayerName, "SuccessImprovementContext.PlayerName"),
                ["datee_name"] = RuntimeFragment(context.DateeName, "SuccessImprovementContext.DateeName"),
                ["delivered_message"] = RuntimeFragment(context.DeliveredMessage, "SuccessImprovementContext.DeliveredMessage"),
            };
            AnnotatedInvocationDocument instruction = BuildTemplateFragment(
                template,
                instructionValues,
                DeliverySource(
                    "delivery_instructions." + StatKey(context.Stat) + "." + context.TierKey,
                    template));

            string envelope = RequireConfiguredPrompt(
                instructions?.GetSuccessImprovementPromptTemplate(),
                "success_improvement_prompt_template",
                nameof(BuildSuccessImprovementDocuments));
            RequireTokens(
                envelope,
                "success_improvement_prompt_template",
                nameof(BuildSuccessImprovementDocuments),
                "tier",
                "stat",
                "delivered_message",
                "conversation_history",
                "instruction");

            var values = new Dictionary<string, AnnotatedInvocationDocument>(StringComparer.OrdinalIgnoreCase)
            {
                ["player_name"] = RuntimeFragment(context.PlayerName, "SuccessImprovementContext.PlayerName"),
                ["datee_name"] = RuntimeFragment(context.DateeName, "SuccessImprovementContext.DateeName"),
                ["delivered_message"] = RuntimeFragment(context.DeliveredMessage, "SuccessImprovementContext.DeliveredMessage"),
                ["tier"] = RuntimeFragment(context.TierKey ?? string.Empty, "SuccessImprovementContext.TierKey"),
                ["tier_upper"] = RuntimeFragment((context.TierKey ?? string.Empty).ToUpperInvariant(), "SuccessImprovementContext.TierKeyUpper"),
                ["stat"] = RuntimeFragment(context.Stat.ToString(), "SuccessImprovementContext.Stat"),
                ["conversation_history"] = FormatConversationHistoryDocument(context.ConversationHistory, promptCatalog),
                ["instruction"] = instruction,
            };

            AnnotatedInvocationDocument user = new AnnotatedInvocationDocumentBuilder()
                .AppendTemplate(
                    envelope,
                    values,
                    DeliverySource("success_improvement_prompt_template", envelope))
                .Trim()
                .Build(
                    "delivery.success-improvement.user",
                    AgentJournalInputRole.User,
                    "delivery-success-improvement-user-prompt");
            return new GameRunPromptDocumentPair(
                BuildPlayerAvatarSystemDocument(context.PlayerAvatarPrompt, gameDefinition),
                user);
        }

        public static AnnotatedInvocationDocument BuildSuccessImprovementSkippedDocument(
            string validationCode)
        {
            if (string.IsNullOrWhiteSpace(validationCode))
                throw new ArgumentException("Validation code is required.", nameof(validationCode));

            return new AnnotatedInvocationDocumentBuilder()
                .AppendRuntimeGenerated(
                    validationCode,
                    "SuccessImprovement.SkipValidationCode")
                .Build(
                    "delivery.success-improvement.skipped",
                    AgentJournalInputRole.User,
                    "delivery-success-improvement-skipped");
        }

        public static GameRunPromptDocumentPair BuildSteeringQuestionDocuments(
            SteeringContext context,
            GameDefinition gameDefinition,
            PromptCatalog? promptCatalog)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (gameDefinition == null) throw new ArgumentNullException(nameof(gameDefinition));

            string template = RequireConfiguredPrompt(
                gameDefinition.SteeringPrompt,
                "steering_prompt",
                nameof(BuildSteeringQuestionDocuments));
            RequireTokens(
                template,
                "steering_prompt",
                nameof(BuildSteeringQuestionDocuments),
                "delivered_message",
                "conversation_history");

            var values = new Dictionary<string, AnnotatedInvocationDocument>(StringComparer.OrdinalIgnoreCase)
            {
                ["player_name"] = RuntimeFragment(context.PlayerName, "SteeringContext.PlayerName"),
                ["datee_name"] = RuntimeFragment(context.DateeName, "SteeringContext.DateeName"),
                ["delivered_message"] = RuntimeFragment(context.DeliveredMessage, "SteeringContext.DeliveredMessage"),
                ["conversation_history"] = FormatConversationHistoryDocument(context.ConversationHistory, promptCatalog),
            };
            AnnotatedInvocationDocument user = new AnnotatedInvocationDocumentBuilder()
                .AppendTemplate(
                    template,
                    values,
                    GameDefinitionSource("steering_prompt", template))
                .Trim()
                .Build(
                    "delivery.steering-question.user",
                    AgentJournalInputRole.User,
                    "delivery-steering-question-user-prompt");

            return new GameRunPromptDocumentPair(
                BuildPlayerAvatarSystemDocument(context.PlayerAvatarPrompt, gameDefinition),
                user);
        }

        public static GameRunPromptDocumentPair BuildHorninessQuestionDocuments(
            HorninessQuestionContext context,
            GameDefinition gameDefinition,
            PromptCatalog? promptCatalog)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (gameDefinition == null) throw new ArgumentNullException(nameof(gameDefinition));

            string template = RequireConfiguredPrompt(
                gameDefinition.HorninessPrompt,
                "horniness_prompt",
                nameof(BuildHorninessQuestionDocuments));
            RequireTokens(
                template,
                "horniness_prompt",
                nameof(BuildHorninessQuestionDocuments),
                "delivered_message",
                "conversation_history");

            var values = new Dictionary<string, AnnotatedInvocationDocument>(StringComparer.OrdinalIgnoreCase)
            {
                ["player_name"] = RuntimeFragment(context.PlayerName, "HorninessQuestionContext.PlayerName"),
                ["datee_name"] = RuntimeFragment(context.DateeName, "HorninessQuestionContext.DateeName"),
                ["delivered_message"] = RuntimeFragment(context.DeliveredMessage, "HorninessQuestionContext.DeliveredMessage"),
                ["conversation_history"] = FormatConversationHistoryDocument(context.ConversationHistory, promptCatalog),
            };
            AnnotatedInvocationDocument user = new AnnotatedInvocationDocumentBuilder()
                .AppendTemplate(
                    template,
                    values,
                    GameDefinitionSource("horniness_prompt", template))
                .Trim()
                .Build(
                    "delivery.horniness-question.user",
                    AgentJournalInputRole.User,
                    "delivery-horniness-question-user-prompt");

            return new GameRunPromptDocumentPair(
                BuildPlayerAvatarSystemDocument(context.PlayerAvatarPrompt, gameDefinition),
                user);
        }

        private static AnnotatedInvocationDocument FromTrace(
            PromptTraceResult trace,
            string documentId,
            AgentJournalInputRole role,
            string kind)
            => PromptProvenanceAdapter.FromPromptTraceResult(
                trace,
                documentId,
                role,
                kind,
                TraceSourceResolver);

        private static AnnotatedInvocationDocument BuildTemplateFragment(
            string template,
            IReadOnlyDictionary<string, AnnotatedInvocationDocument> values,
            AgentJournalSourceIdentity source)
            => new AnnotatedInvocationDocumentBuilder()
                .AppendTemplate(template, values, source)
                .Build("fragment." + source.KeyPath.Replace(':', '.').Replace('_', '-'), AgentJournalInputRole.User, "template-fragment");

        private static IReadOnlyDictionary<string, AnnotatedInvocationDocument> RuntimeSubstitutions(
            IReadOnlyDictionary<string, string> values)
        {
            var substitutions = new Dictionary<string, AnnotatedInvocationDocument>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, string> pair in values)
            {
                substitutions[pair.Key] = RuntimeFragment(pair.Value, pair.Key);
            }

            return substitutions;
        }

        private static AnnotatedInvocationDocument RuntimeFragment(string? value, string keyPath)
            => new AnnotatedInvocationDocumentBuilder()
                .AppendRuntimeGenerated(value ?? string.Empty, keyPath)
                .Build("fragment." + keyPath.Replace(':', '.').Replace('_', '-'), AgentJournalInputRole.User, "runtime-fragment");

        private static AnnotatedInvocationDocument FormatConversationHistoryDocument(
            IEnumerable<(string Sender, string Text)> history,
            PromptCatalog? promptCatalog)
        {
            if (history == null) throw new ArgumentNullException(nameof(history));
            var builder = new AnnotatedInvocationDocumentBuilder();
            bool hasEntries = false;
            foreach (var (sender, text) in history)
            {
                if (hasEntries)
                {
                    builder.AppendRuntimeGenerated(Environment.NewLine, "conversation_history.separator");
                }

                hasEntries = true;
                builder.AppendRuntimeGenerated(
                    sender + ": " + text,
                    "conversation_history.entry");
            }

            if (!hasEntries)
            {
                string empty = PromptTemplates.GetCatalogString(promptCatalog, "conversation-history-empty");
                builder.AppendConfigured(
                    empty,
                    CatalogSource(
                        GetTemplateSource(promptCatalog, "conversation-history-empty"),
                        "conversation-history-empty",
                        empty));
            }

            return builder.Build(
                "fragment.conversation-history",
                AgentJournalInputRole.User,
                "conversation-history-fragment");
        }

        private static string GetTemplateSource(PromptCatalog? promptCatalog, string key)
            => PromptCatalog.ResolveCatalogOrThrow(promptCatalog).TryGet(key)?.SourceFile
                ?? "data/prompts/templates.yaml";

        private static string RequireConfiguredPrompt(string? value, string key, string methodName)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException(
                    "Production path '" + methodName + "' is missing configured prompt '" + key + "'.");
            }

            return value;
        }

        private static void RequireTokens(
            string template,
            string key,
            string methodName,
            params string[] requiredTokens)
        {
            foreach (string token in requiredTokens)
            {
                if (template.IndexOf("{" + token + "}", StringComparison.Ordinal) < 0)
                {
                    throw new InvalidOperationException(
                        "Production path '" + methodName + "' has configured template '" + key +
                        "' without required placeholder '{" + token + "}'.");
                }
            }
        }

        private static AgentJournalSourceIdentity CatalogSource(string? sourceFile, string keyPath, string text)
            => new AgentJournalSourceIdentity(
                AgentJournalSourceKind.Configuration,
                GameRunPromptSourceIdentityResolver.Instance.ResolveRequired(sourceFile),
                keyPath,
                contentHash: ComputeSha256(text));

        private static AgentJournalSourceIdentity DeliverySource(string keyPath, string text)
            => new AgentJournalSourceIdentity(
                AgentJournalSourceKind.Configuration,
                "delivery.instructions",
                keyPath,
                contentHash: ComputeSha256(text));

        private static AgentJournalSourceIdentity GameDefinitionSource(string keyPath, string text)
            => new AgentJournalSourceIdentity(
                AgentJournalSourceKind.Configuration,
                "game.definition",
                keyPath,
                contentHash: ComputeSha256(text));

        private static string ComputeSha256(string value)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(value ?? string.Empty));
                var builder = new StringBuilder("sha256:", "sha256:".Length + hash.Length * 2);
                for (int i = 0; i < hash.Length; i++)
                {
                    builder.Append(hash[i].ToString("x2"));
                }

                return builder.ToString();
            }
        }

        private static string StatKey(StatType stat)
        {
            switch (stat)
            {
                case StatType.Charm: return "charm";
                case StatType.Rizz: return "rizz";
                case StatType.Honesty: return "honesty";
                case StatType.Chaos: return "chaos";
                case StatType.Wit: return "wit";
                case StatType.SelfAwareness: return "sa";
                default: return stat.ToString().ToLowerInvariant();
            }
        }
    }
}
