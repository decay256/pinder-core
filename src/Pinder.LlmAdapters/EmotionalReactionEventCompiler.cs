using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Pinder.Core.Characters;
using Pinder.Core.Conversation;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;
using Pinder.Core.Text;

namespace Pinder.LlmAdapters
{
    /// <summary>
    /// Compiles runtime turn facts and catalog prose into a private emotional
    /// reaction input artifact. It performs no LLM call and is not wired into
    /// the current visible DATEE response prompt.
    /// </summary>
    public sealed class EmotionalReactionEventCompiler
    {
        public const string RuntimeSource = "runtime:DateeContext";
        private const string CharacterDiagnosisSource = "character:psychiatric_diagnosis";
        private static readonly Regex PlaceholderRegex =
            new Regex(@"\{(?<token>[a-zA-Z_][a-zA-Z0-9_]*)\}", RegexOptions.Compiled);

        private readonly PromptCatalog? _promptCatalog;

        public EmotionalReactionEventCompiler(PromptCatalog? promptCatalog = null)
        {
            _promptCatalog = promptCatalog;
        }

        public PromptTraceResult Compile(DateeContext context, PromptCatalog? promptCatalog = null)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            PromptCatalog catalog = PromptCatalog.ResolveCatalogOrThrow(promptCatalog ?? _promptCatalog);
            var turnEvent = context.EmotionalTurnEvent
                ?? throw new InvalidOperationException(
                    "emotional-reaction-compiler: missing DateeContext.EmotionalTurnEvent.");

            if (string.IsNullOrWhiteSpace(context.PlayerDeliveredMessage))
            {
                throw new InvalidOperationException(
                    "emotional-reaction-compiler: PlayerDeliveredMessage is required.");
            }

            var diagnosisValidation = TherapistDiagnosisContract.ValidateRequiredFields(turnEvent.TherapistDiagnosis);
            if (!diagnosisValidation.IsValid)
            {
                throw new InvalidOperationException(
                    "emotional-reaction-compiler: invalid therapist diagnosis: " +
                    diagnosisValidation.Violation!.Message);
            }

            string transitionKey = EmotionalReactionPromptCatalog.GetRelationshipTransitionKey(
                context.InterestBeforeState,
                context.InterestAfterState);
            string outcomeKey = RollOutcomeIntensityContract.ToKey(turnEvent.OutcomeIntensity);

            PromptTraceResult priorRelationship = EntryTrace(
                catalog,
                EmotionalReactionPromptCatalog.GetInterestStateMeaningKey(context.InterestBeforeState));
            PromptTraceResult resultingRelationship = EntryTrace(
                catalog,
                EmotionalReactionPromptCatalog.GetInterestStateMeaningKey(context.InterestAfterState));
            PromptTraceResult transitionMeaning = RenderTemplate(
                RequireEntry(catalog, EmotionalReactionPromptCatalog.GetRelationshipTransitionInstructionKey(transitionKey)),
                EmotionalReactionPromptCatalog.GetRelationshipTransitionInstructionKey(transitionKey),
                new Dictionary<string, PromptTraceResult>
                {
                    { "prior_relationship", priorRelationship },
                    { "resulting_relationship", resultingRelationship },
                },
                "prior_relationship",
                "resulting_relationship");
            PromptTraceResult eventMeaning = EntryTrace(
                catalog,
                EmotionalReactionPromptCatalog.GetEventMeaningKey(turnEvent.SelectedStat, outcomeKey));
            PromptTraceResult deliveredMessage = StructuredLiteralTrace(
                context.PlayerDeliveredMessage.Trim(),
                "PlayerDeliveredMessage");
            PromptTraceResult history = CompileHistory(context.ConversationHistory, catalog);
            PromptTraceResult character = CompileCharacterFormulation(turnEvent, catalog);

            return RenderTemplate(
                RequireEntry(catalog, "emotional-reaction-compiled-wrapper"),
                "emotional-reaction-compiled-wrapper",
                new Dictionary<string, PromptTraceResult>
                {
                    { "prior_relationship", priorRelationship },
                    { "resulting_relationship", resultingRelationship },
                    { "transition_meaning", transitionMeaning },
                    { "recipient_event_meaning", eventMeaning },
                    { "delivered_message", deliveredMessage },
                    { "recent_visible_history", history },
                    { "character_formulation", character },
                },
                "prior_relationship",
                "resulting_relationship",
                "transition_meaning",
                "recipient_event_meaning",
                "delivered_message",
                "recent_visible_history",
                "character_formulation");
        }

        private static PromptTraceResult CompileCharacterFormulation(
            DateeEmotionalTurnEvent turnEvent,
            PromptCatalog catalog)
        {
            string statReactionKey = GetDiagnosisStatReactionKey(turnEvent.SelectedStat);
            var diagnosis = turnEvent.TherapistDiagnosis!;

            var values = new Dictionary<string, PromptTraceResult>
            {
                {
                    "derived_feeling",
                    CharacterTrace(diagnosis[TherapistDiagnosisContract.DerivedFeelingKey],
                        TherapistDiagnosisContract.DerivedFeelingKey)
                },
                {
                    "defense_reaction",
                    CharacterTrace(diagnosis[TherapistDiagnosisContract.DefenseReactionKey],
                        TherapistDiagnosisContract.DefenseReactionKey)
                },
                {
                    "stat_reaction",
                    CharacterTrace(diagnosis[statReactionKey], statReactionKey)
                },
            };

            string templateKey;
            string[] requiredTokens;
            if (RollOutcomeIntensityContract.IsSuccess(turnEvent.OutcomeIntensity))
            {
                templateKey = "emotional-reaction-character-success-wrapper";
                values["safe_connection"] = CharacterTrace(
                    diagnosis[TherapistDiagnosisContract.SafeConnectionKey],
                    TherapistDiagnosisContract.SafeConnectionKey);
                requiredTokens = new[]
                {
                    "derived_feeling",
                    "defense_reaction",
                    "stat_reaction",
                    "safe_connection",
                };
            }
            else
            {
                templateKey = "emotional-reaction-character-failure-wrapper";
                values["hurt_protection"] = CharacterTrace(
                    diagnosis[TherapistDiagnosisContract.HurtProtectionKey],
                    TherapistDiagnosisContract.HurtProtectionKey);
                values["repair_requirement"] = CharacterTrace(
                    diagnosis[TherapistDiagnosisContract.RepairRequirementKey],
                    TherapistDiagnosisContract.RepairRequirementKey);
                requiredTokens = new[]
                {
                    "derived_feeling",
                    "defense_reaction",
                    "stat_reaction",
                    "hurt_protection",
                    "repair_requirement",
                };
            }

            return RenderTemplate(RequireEntry(catalog, templateKey), templateKey, values, requiredTokens);
        }

        private static PromptTraceResult CompileHistory(
            IReadOnlyList<(string Sender, string Text)> history,
            PromptCatalog catalog)
        {
            if (history == null || history.Count == 0)
                return EntryTrace(catalog, "emotional-reaction-history-empty");

            var sb = new AnnotatedStringBuilder();
            var recent = history.Skip(Math.Max(0, history.Count - 6));
            bool first = true;
            foreach (var item in recent)
            {
                if (!first)
                    sb.AppendLine();
                first = false;

                sb.Append(RenderTemplate(
                    RequireEntry(catalog, "emotional-reaction-history-line"),
                    "emotional-reaction-history-line",
                    new Dictionary<string, PromptTraceResult>
                    {
                        { "sender", StructuredLiteralTrace(item.Sender, "ConversationHistory.Sender") },
                        { "message", StructuredLiteralTrace(item.Text, "ConversationHistory.Text") },
                    },
                    "sender",
                    "message"));
            }

            return new PromptTraceResult(sb.ToString(), sb.Spans);
        }

        private static PromptEntry RequireEntry(PromptCatalog catalog, string key)
        {
            var entry = catalog.TryGet(key)
                ?? throw new InvalidOperationException(
                    "emotional-reaction-compiler: missing prompt key '" + key + "'.");

            if (string.IsNullOrWhiteSpace(entry.SystemPrompt))
            {
                throw new InvalidOperationException(
                    "emotional-reaction-compiler: prompt key '" + key + "' must define system_prompt.");
            }

            return entry;
        }

        private static PromptTraceResult EntryTrace(PromptCatalog catalog, string key)
        {
            var entry = RequireEntry(catalog, key);
            return new PromptTraceResult(
                entry.SystemPrompt!,
                new[] { new AnnotatedSpan(0, entry.SystemPrompt!.Length, entry.SourceFile, key) });
        }

        private static PromptTraceResult RuntimeTrace(string text, string key)
        {
            return new PromptTraceResult(
                text ?? string.Empty,
                new[] { new AnnotatedSpan(0, (text ?? string.Empty).Length, RuntimeSource, key) });
        }

        private static PromptTraceResult StructuredLiteralTrace(string? text, string key)
        {
            return RuntimeTrace(JsonConvert.ToString(text ?? string.Empty), key);
        }

        private static PromptTraceResult CharacterTrace(string text, string key)
        {
            return new PromptTraceResult(
                text ?? string.Empty,
                new[] { new AnnotatedSpan(0, (text ?? string.Empty).Length, CharacterDiagnosisSource, key) });
        }

        private static PromptTraceResult RenderTemplate(
            PromptEntry entry,
            string key,
            IReadOnlyDictionary<string, PromptTraceResult> values,
            params string[] requiredTokens)
        {
            string template = entry.SystemPrompt!;
            string sourceFile = entry.SourceFile ?? string.Empty;

            foreach (string token in requiredTokens)
            {
                if (template.IndexOf("{" + token + "}", StringComparison.Ordinal) < 0)
                {
                    throw new InvalidOperationException(
                        "emotional-reaction-compiler: prompt key '" + key +
                        "' must include required token '{" + token + "}'.");
                }
            }

            var sb = new AnnotatedStringBuilder();
            int index = 0;
            foreach (Match match in PlaceholderRegex.Matches(template))
            {
                if (match.Index > index)
                {
                    sb.Append(
                        template.Substring(index, match.Index - index),
                        sourceFile,
                        key);
                }

                string token = match.Groups["token"].Value;
                if (!values.TryGetValue(token, out var value))
                {
                    throw new KeyNotFoundException(
                        "emotional-reaction-compiler: prompt key '" + key +
                        "' references token '{" + token + "}' without a supplied value.");
                }

                sb.Append(value);
                index = match.Index + match.Length;
            }

            if (index < template.Length)
                sb.Append(template.Substring(index), sourceFile, key);

            return new PromptTraceResult(sb.ToString(), sb.Spans);
        }

        private static string GetDiagnosisStatReactionKey(StatType stat)
        {
            switch (stat)
            {
                case StatType.Charm:
                    return TherapistDiagnosisContract.CharmReactionKey;
                case StatType.Rizz:
                    return TherapistDiagnosisContract.RizzReactionKey;
                case StatType.Honesty:
                    return TherapistDiagnosisContract.HonestyReactionKey;
                case StatType.Chaos:
                    return TherapistDiagnosisContract.ChaosReactionKey;
                case StatType.Wit:
                    return TherapistDiagnosisContract.WitReactionKey;
                case StatType.SelfAwareness:
                    return TherapistDiagnosisContract.SelfAwarenessReactionKey;
                default:
                    throw new InvalidOperationException("emotional-reaction-compiler: unknown selected stat.");
            }
        }
    }
}
