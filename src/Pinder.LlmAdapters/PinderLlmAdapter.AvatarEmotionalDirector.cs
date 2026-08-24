using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Pinder.Core.Conversation;
using Pinder.Core.Diagnostics.AgentJournals;
using Pinder.Core.Interfaces;
using Pinder.Core.Text;
using Pinder.LlmAdapters.AgentJournals;

namespace Pinder.LlmAdapters
{
    public sealed partial class PinderLlmAdapter
    {
        public async Task<CharacterEmotionalDirection> GetAvatarEmotionalDirectionAsync(
            DialogueContext context,
            IReadOnlyList<ConversationMessage> avatarHistory,
            LlmConversationSessionSnapshot? avatarSession,
            CancellationToken cancellationToken = default)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (avatarHistory == null) throw new ArgumentNullException(nameof(avatarHistory));

            await using PiConversationSession session = await PiConversationSession.RestoreOrImportAsync(
                avatarSession,
                avatarHistory,
                "avatar").ConfigureAwait(false);
            PiConversationBranch branch = await session.ForkAsync("avatar-private-analysis").ConfigureAwait(false);
            AgentJournalCallScope? disposalJournal = null;
            try
            {
                disposalJournal = await StartBranchDisposalJournalAsync(
                        session,
                        branch,
                        context.CurrentTurn,
                        context.AgentJournalContext)
                    .ConfigureAwait(false);
                IReadOnlyList<ConversationMessage> priorMessages = await branch.BuildSemanticHistoryAsync()
                    .ConfigureAwait(false);
                return await GenerateAvatarEmotionalDirectionAsync(
                        context,
                        priorMessages,
                        branch,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                await branch.DisposeAsync().ConfigureAwait(false);
                if (disposalJournal != null)
                    await disposalJournal.CompleteAcceptedAsync("disposed").ConfigureAwait(false);
            }
        }

        private async Task<CharacterEmotionalDirection> GenerateAvatarEmotionalDirectionAsync(
            DialogueContext context,
            IReadOnlyList<ConversationMessage> priorMessages,
            PiConversationBranch privateBranch,
            CancellationToken cancellationToken)
        {
            PromptCatalog catalog = PromptCatalog.ResolveCatalogOrThrow(_options.PromptCatalog);
            PromptEntry vocabularyEntry = RequireAvatarPrompt(catalog, CharacterEmotionCatalog.PromptKey);
            IReadOnlyList<string> allowedEmotions = CharacterEmotionCatalog.Load(catalog);

            PromptEntry director = catalog.RequireCompleteEntry(
                EmotionalPromptCompiler.DirectorPromptKey,
                "prompt-catalog: missing shared character emotional director.");
            PromptEntry wrapper = RequireAvatarPrompt(catalog, "avatar-emotional-director-system-wrapper");
            PromptEntry input = RequireAvatarPrompt(catalog, "avatar-emotional-director-input");
            PromptEntry relationship = RequireAvatarPrompt(
                catalog,
                EmotionalReactionPromptCatalog.GetInterestStateMeaningKey(context.CurrentInterestState));

            PromptTraceResult avatarSystemTrace = SessionSystemPromptBuilder.BuildPlayerAvatarEx(
                context.PlayerAvatarPrompt,
                RequireGameDefinition());
            PromptTraceResult directorRulesTrace = RenderAvatarTemplate(
                director.SystemPrompt!,
                director,
                EmotionalPromptCompiler.DirectorPromptKey,
                new Dictionary<string, PromptTraceResult>
                {
                    ["{emotion_vocabulary}"] = TraceAvatarPrompt(
                        string.Join(", ", allowedEmotions),
                        vocabularyEntry,
                        CharacterEmotionCatalog.PromptKey),
                });
            PromptTraceResult systemTrace = RenderAvatarTemplate(
                wrapper.SystemPrompt!,
                wrapper,
                "avatar-emotional-director-system-wrapper",
                new Dictionary<string, PromptTraceResult>
                {
                    ["{avatar_system_prompt}"] = avatarSystemTrace,
                    ["{director_system_prompt}"] = directorRulesTrace,
                });
            PromptTraceResult inputTrace = RenderAvatarTemplate(
                input.SystemPrompt!,
                input,
                "avatar-emotional-director-input",
                new Dictionary<string, PromptTraceResult>
                {
                    ["{relationship_meaning}"] = TraceAvatarPrompt(relationship.SystemPrompt!, relationship, EmotionalReactionPromptCatalog.GetInterestStateMeaningKey(context.CurrentInterestState)),
                    ["{datee_profile}"] = TraceAvatarRuntime(JsonConvert.ToString(context.DateePrompt), "AvatarEmotionalDirector.DateeProfile"),
                    ["{datee_last_message}"] = TraceAvatarRuntime(
                        JsonConvert.ToString(string.IsNullOrWhiteSpace(context.DateeLastMessage)
                            ? "No DATEE message has been received yet."
                            : context.DateeLastMessage),
                        "AvatarEmotionalDirector.DateeLastMessage"),
                    ["{cognitive_subtext}"] = TraceAvatarRuntime(context.CognitiveSubtext ?? "No persistent pressure is available.", "AvatarEmotionalDirector.CognitiveSubtext"),
                    ["{transition_target}"] = TraceAvatarRuntime(context.ResolvedTarget?.StemText ?? "No specific revelation target is active.", "AvatarEmotionalDirector.TransitionTarget"),
                    ["{transition_style}"] = TraceAvatarRuntime(context.ResolvedTarget?.TransitionStyle ?? "Stay open to the immediate exchange.", "AvatarEmotionalDirector.TransitionStyle"),
                });
            PromptTraceResult userTrace = RenderAvatarTemplate(
                director.UserTemplate!,
                director,
                EmotionalPromptCompiler.DirectorPromptKey,
                new Dictionary<string, PromptTraceResult>
                {
                    ["{compiled_reaction_input}"] = inputTrace,
                });
            double temperature = director.Temperature ?? LlmPhaseTemperatures.EmotionalDirector;
            int? maxTokens = director.MaxTokens ?? _options.MaxTokens;
            var metadata = new Dictionary<string, string>
            {
                ["schema_version"] = CharacterEmotionalDirectionContract.SchemaVersion,
                ["emotion_count"] = allowedEmotions.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
            };
            var sharedCompiler = new EmotionalPromptCompiler(catalog);
            return await ExecuteCharacterEmotionalDirectorAsync(
                    new CharacterEmotionalDirectorInvocation
                    {
                        Phase = LlmPhase.AvatarEmotionalDirector,
                        JournalOperation = GameRunConversationJournalInventory.AvatarEmotionalDirector,
                        PrivatePhase = "avatar-private-analysis",
                        BranchKind = "avatar-private-analysis",
                        Turn = context.CurrentTurn,
                        SystemPrompt = systemTrace,
                        UserPrompt = userTrace,
                        Temperature = temperature,
                        MaxTokens = maxTokens,
                        AllowedEmotions = allowedEmotions,
                        PriorMessages = priorMessages,
                        PrivateBranch = privateBranch,
                        JournalContext = context.AgentJournalContext,
                        BuildMetadata = _ => metadata,
                        CompileRetrySystemPrompt = reason => sharedCompiler.CompileDirectorRetrySystemPrompt(
                            systemTrace,
                            reason),
                        OnExhausted = (exception, attempts) => { },
                    },
                    cancellationToken)
                .ConfigureAwait(false);
        }

        private static PromptEntry RequireAvatarPrompt(PromptCatalog catalog, string key)
        {
            PromptEntry? entry = catalog.TryGet(key);
            if (entry == null || string.IsNullOrWhiteSpace(entry.SystemPrompt))
                throw new InvalidOperationException("prompt-catalog: missing required avatar emotional prompt '" + key + "'.");
            return entry;
        }

        private static PromptTraceResult RenderAvatarTemplate(
            string template,
            PromptEntry entry,
            string key,
            IReadOnlyDictionary<string, PromptTraceResult> values)
        {
            var builder = new AnnotatedStringBuilder();
            int cursor = 0;
            while (cursor < template.Length)
            {
                KeyValuePair<string, PromptTraceResult>? next = null;
                int nextIndex = template.Length;
                foreach (KeyValuePair<string, PromptTraceResult> value in values)
                {
                    int index = template.IndexOf(value.Key, cursor, StringComparison.Ordinal);
                    if (index >= 0 && index < nextIndex)
                    {
                        next = value;
                        nextIndex = index;
                    }
                }

                if (!next.HasValue)
                {
                    builder.Append(template.Substring(cursor), entry.SourceFile, key);
                    break;
                }

                builder.Append(template.Substring(cursor, nextIndex - cursor), entry.SourceFile, key);
                builder.Append(next.Value.Value);
                cursor = nextIndex + next.Value.Key.Length;
            }

            foreach (string placeholder in values.Keys)
            {
                if (template.IndexOf(placeholder, StringComparison.Ordinal) < 0)
                    throw new InvalidOperationException("Avatar emotional prompt is missing " + placeholder + ".");
            }
            return new PromptTraceResult(builder.ToString(), builder.Spans);
        }

        private static PromptTraceResult TraceAvatarPrompt(string text, PromptEntry entry, string key)
            => new PromptTraceResult(
                text,
                new[] { new AnnotatedSpan(0, text.Length, entry.SourceFile, key) });

        private static PromptTraceResult TraceAvatarRuntime(string text, string key)
            => new PromptTraceResult(
                text,
                new[] { new AnnotatedSpan(0, text.Length, "runtime:AvatarEmotionalDirectorInput", key) });

    }
}
