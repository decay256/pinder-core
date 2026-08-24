using System;
using System.Collections.Generic;
using System.Linq;
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
        public async Task<AvatarEmotionalDirection> GetAvatarEmotionalDirectionAsync(
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

        private async Task<AvatarEmotionalDirection> GenerateAvatarEmotionalDirectionAsync(
            DialogueContext context,
            IReadOnlyList<ConversationMessage> priorMessages,
            PiConversationBranch privateBranch,
            CancellationToken cancellationToken)
        {
            PromptCatalog catalog = PromptCatalog.ResolveCatalogOrThrow(_options.PromptCatalog);
            PromptEntry vocabularyEntry = RequireAvatarPrompt(catalog, "avatar-emotional-primary-emotions");
            IReadOnlyList<string> allowedEmotions = vocabularyEntry.SystemPrompt!
                .Split(new[] { ',', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(value => value.Trim())
                .Where(value => value.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (allowedEmotions.Count == 0)
                throw new InvalidOperationException("avatar-emotional-primary-emotions must contain at least one emotion.");

            PromptEntry director = catalog.RequireCompleteEntry(
                "avatar-emotional-director",
                "prompt-catalog: missing avatar-emotional-director.");
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
                "avatar-emotional-director",
                new Dictionary<string, PromptTraceResult>
                {
                    ["{emotion_vocabulary}"] = TraceAvatarPrompt(
                        string.Join(", ", allowedEmotions),
                        vocabularyEntry,
                        "avatar-emotional-primary-emotions"),
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
                "avatar-emotional-director",
                new Dictionary<string, PromptTraceResult>
                {
                    ["{compiled_avatar_emotional_input}"] = inputTrace,
                });
            AnnotatedInvocationDocument systemDocument =
                GameRunPromptDocumentBuilder.BuildEmotionalDirectorSystemDocument(systemTrace);
            AnnotatedInvocationDocument userDocument =
                GameRunPromptDocumentBuilder.BuildEmotionalDirectorUserDocument(userTrace);
            double temperature = director.Temperature ?? LlmPhaseTemperatures.EmotionalDirector;
            int? maxTokens = director.MaxTokens ?? _options.MaxTokens;
            var metadata = new Dictionary<string, string>
            {
                ["schema_version"] = AvatarEmotionalDirectionContract.SchemaVersion,
                ["emotion_count"] = allowedEmotions.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
            };

            int maxAttempts = GetContractViolationAttemptLimit();
            var recovery = await SemanticOutputRecoveryExecutor.ExecuteAsync<AvatarEmotionalDirection, LlmContractException>(
                maxAttempts,
                async (attempt, attemptCancellationToken) =>
                {
                    AgentJournalCallScope? journal = null;
                    try
                    {
                        if (attempt > 1)
                        {
                            PromptEntry repair = RequireAvatarPrompt(catalog, "avatar-emotional-director-repair");
                            PromptTraceResult retryTrace = AppendAvatarTraces(
                                systemTrace,
                                TraceAvatarPrompt(repair.SystemPrompt!, repair, "avatar-emotional-director-repair"));
                            systemDocument = GameRunPromptDocumentBuilder.BuildEmotionalDirectorSystemDocument(retryTrace);
                        }

                        journal = await StartConversationJournalAttemptAsync(
                                GameRunConversationJournalInventory.AvatarEmotionalDirector,
                                LlmPhase.AvatarEmotionalDirector,
                                context.CurrentTurn,
                                attempt,
                                maxAttempts,
                                "avatar-private-analysis",
                                systemDocument,
                                userDocument,
                                branch: privateBranch,
                                branchKind: "avatar-private-analysis",
                                correlationContext: context.AgentJournalContext)
                            .ConfigureAwait(false);

                        AvatarEmotionalDirection direction;
                        string responseText;
                        bool canUseStructured = _transport is IStructuredLlmTransport
                            && (priorMessages.Count == 0
                                || (_transport is IStructuredConversationLlmTransport contextual
                                    && contextual.SupportsStructuredConversationMessages));
                        if (canUseStructured)
                        {
                            var request = AvatarEmotionalDirectionContract.CreateRequest(
                                systemDocument.Text,
                                userDocument.Text,
                                temperature,
                                maxTokens,
                                metadata,
                                allowedEmotions);
                            var response = await SendStructuredWithDiagnosticsAsync(
                                    (IStructuredLlmTransport)_transport,
                                    request,
                                    LlmPhase.AvatarEmotionalDirector,
                                    context.CurrentTurn,
                                    attemptCancellationToken,
                                    priorMessages: priorMessages,
                                    callId: journal.CallId)
                                .ConfigureAwait(false);
                            responseText = response.JsonText;
                            try
                            {
                                direction = ParseAvatarDirectionOrThrow(
                                    responseText,
                                    response.UsedNativeStructuredOutput,
                                    allowedEmotions,
                                    response.Provider,
                                    response.Model,
                                    context.CurrentTurn);
                                response.ReportValidation("accepted");
                            }
                            catch (LlmContractException ex)
                            {
                                response.ReportValidation("rejected", ex.Reason);
                                throw;
                            }
                        }
                        else
                        {
                            responseText = await SendWithDiagnosticsAsync(
                                    _transport,
                                    systemDocument.Text,
                                    userDocument.Text,
                                    temperature,
                                    maxTokens,
                                    LlmPhase.AvatarEmotionalDirector,
                                    context.CurrentTurn,
                                    attemptCancellationToken,
                                    priorMessages: priorMessages,
                                    callId: journal.CallId)
                                .ConfigureAwait(false);
                            direction = ParseAvatarDirectionOrThrow(
                                responseText,
                                false,
                                allowedEmotions,
                                null,
                                null,
                                context.CurrentTurn);
                        }

                        PiAcceptedExchangeEntryIds entryIds = await privateBranch.AppendAcceptedExchangeAsync(
                            userDocument.Text,
                            responseText).ConfigureAwait(false);
                        await journal.CompleteAcceptedAsync(responseText, entryIds.AssistantEntryId).ConfigureAwait(false);
                        return SemanticOutputRecoveryAttemptResult<AvatarEmotionalDirection, LlmContractException>.Accepted(direction);
                    }
                    catch (LlmContractException ex)
                    {
                        if (journal != null)
                            await journal.CompleteValidationRejectedAsync(ex.Reason).ConfigureAwait(false);
                        return SemanticOutputRecoveryAttemptResult<AvatarEmotionalDirection, LlmContractException>.Rejected(ex);
                    }
                    catch (OperationCanceledException)
                    {
                        if (journal != null)
                            await journal.CompleteCancelledAsync(AgentJournalTerminalCodes.Cancelled).ConfigureAwait(false);
                        throw;
                    }
                    catch (Exception ex)
                    {
                        if (journal != null)
                            await journal.CompleteProviderFailedAsync(ex.GetType().Name).ConfigureAwait(false);
                        throw;
                    }
                },
                delayAfterRejectedAttempt: attempt => TimeSpan.FromMilliseconds(
                    GetContractViolationBackoffDelayMs(_options.ContractViolationBackoffMs, attempt)),
                onRejected: rejection => NotifyContractViolation(rejection, "avatar-director"),
                cancellationToken: cancellationToken).ConfigureAwait(false);

            if (recovery.IsAccepted)
                return recovery.AcceptedValue;

            ExceptionDispatchInfo.Capture(recovery.Exhaustion.FinalRejection).Throw();
            throw recovery.Exhaustion.FinalRejection;
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

        private static PromptTraceResult AppendAvatarTraces(PromptTraceResult first, PromptTraceResult second)
        {
            var builder = new AnnotatedStringBuilder();
            builder.Append(first);
            builder.Append("\n\n", "runtime:AvatarEmotionalDirectorInput", "AvatarEmotionalDirector.RepairSeparator");
            builder.Append(second);
            return new PromptTraceResult(builder.ToString(), builder.Spans);
        }

        private static AvatarEmotionalDirection ParseAvatarDirectionOrThrow(
            string? jsonText,
            bool requireCompleteJsonObject,
            IReadOnlyList<string> allowedEmotions,
            string? provider,
            string? model,
            int? turnId)
        {
            if (AvatarEmotionalDirectionContract.TryParse(
                jsonText,
                requireCompleteJsonObject,
                allowedEmotions,
                out AvatarEmotionalDirection? direction,
                out string errorCode))
            {
                return direction!;
            }

            throw new LlmContractException(
                LlmPhase.AvatarEmotionalDirector,
                errorCode,
                "LLM avatar emotional direction output failed its private contract.",
                provider,
                model,
                AvatarEmotionalDirectionContract.ParserName,
                null,
                null,
                null,
                null,
                null,
                turnId);
        }
    }
}
