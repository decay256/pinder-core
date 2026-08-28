using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Pinder.Core.Conversation;
using Pinder.Core.Diagnostics.AgentJournals;
using Pinder.Core.Interfaces;
using Pinder.Core.Text;
using Pinder.LlmAdapters.AgentJournals;

namespace Pinder.LlmAdapters
{
    public sealed partial class PinderLlmAdapter
    {
        private sealed class CharacterEmotionalDirectorInvocation
        {
            public string Phase { get; set; } = string.Empty;
            public string JournalOperation { get; set; } = string.Empty;
            public string PrivatePhase { get; set; } = string.Empty;
            public string BranchKind { get; set; } = string.Empty;
            public PromptContractRoleScope RecipientRole { get; set; } = PromptContractRoleScope.Datee;
            public int Turn { get; set; }
            public PromptTraceResult SystemPrompt { get; set; } = null!;
            public PromptTraceResult UserPrompt { get; set; } = null!;
            public double Temperature { get; set; }
            public int? MaxTokens { get; set; }
            public IReadOnlyList<string> AllowedEmotions { get; set; } = Array.Empty<string>();
            public Func<PromptTraceResult, IReadOnlyDictionary<string, string>> BuildMetadata { get; set; } = null!;
            public Func<string, PromptTraceResult> CompileRetrySystemPrompt { get; set; } = null!;
            public Action<LlmContractException, int> OnExhausted { get; set; } = null!;
            public IReadOnlyList<ConversationMessage>? PriorMessages { get; set; }
            public PiConversationBranch? PrivateBranch { get; set; }
            public GameRunAgentJournalContext? JournalContext { get; set; }
            public IReadOnlyList<RoleFactAccessDecision>? RoleFactAccessDecisions { get; set; }
        }

        private async Task<CharacterEmotionalDirection> ExecuteCharacterEmotionalDirectorAsync(
            CharacterEmotionalDirectorInvocation invocation,
            CancellationToken cancellationToken)
        {
            int maxAttempts = GetContractViolationAttemptLimit();
            PromptTraceResult attemptSystemPrompt = invocation.SystemPrompt;
            var userDocument = GameRunPromptDocumentBuilder.BuildEmotionalDirectorUserDocument(
                invocation.UserPrompt);

            var recovery = await SemanticOutputRecoveryExecutor.ExecuteAsync<CharacterEmotionalDirection, LlmContractException>(
                maxAttempts,
                async (attempt, attemptCancellationToken) =>
                {
                    AgentJournalCallScope? journal = null;
                    try
                    {
                        var systemDocument = GameRunPromptDocumentBuilder.BuildEmotionalDirectorSystemDocument(
                            attemptSystemPrompt);
                        IReadOnlyDictionary<string, string> metadata = invocation.BuildMetadata(
                            attemptSystemPrompt);
                        ValidatePromptContracts(invocation.Phase, invocation.RecipientRole, systemDocument, userDocument);
                        journal = await StartConversationJournalAttemptAsync(
                                invocation.JournalOperation,
                                invocation.Phase,
                                invocation.Turn,
                                attempt,
                                maxAttempts,
                                invocation.PrivatePhase,
                                systemDocument,
                                userDocument,
                                branch: invocation.PrivateBranch,
                                branchKind: invocation.BranchKind,
                                correlationContext: invocation.JournalContext,
                                roleFactAccessDecisions: invocation.RoleFactAccessDecisions)
                            .ConfigureAwait(false);

                        CharacterEmotionalDirection direction;
                        string responseText;
                        bool canUseStructured = _transport is IStructuredLlmTransport
                            && (invocation.PriorMessages == null
                                || invocation.PriorMessages.Count == 0
                                || (_transport is IStructuredConversationLlmTransport contextual
                                    && contextual.SupportsStructuredConversationMessages));
                        if (canUseStructured)
                        {
                            var request = CharacterEmotionalDirectionContract.CreateRequest(
                                systemDocument.Text,
                                userDocument.Text,
                                invocation.Temperature,
                                invocation.MaxTokens,
                                metadata,
                                invocation.Phase,
                                invocation.AllowedEmotions);
                            var response = await SendStructuredWithDiagnosticsAsync(
                                    (IStructuredLlmTransport)_transport,
                                    request,
                                    invocation.Phase,
                                    invocation.Turn,
                                    attemptCancellationToken,
                                    attempt,
                                    maxAttempts,
                                    invocation.PrivatePhase,
                                    metadata,
                                    invocation.PriorMessages,
                                    callId: journal.CallId,
                                    promptContract: new PromptProviderContract(PromptProviderOperation.EmotionalDirectorStructured, invocation.RecipientRole, new[] { systemDocument, userDocument }, invocation.RoleFactAccessDecisions))
                                .ConfigureAwait(false);
                            responseText = response.JsonText;
                            try
                            {
                                direction = ParseCharacterDirectionOrThrow(
                                    responseText,
                                    response.UsedNativeStructuredOutput,
                                    invocation.AllowedEmotions,
                                    invocation.Phase,
                                    response.Provider,
                                    response.Model,
                                    invocation.Turn);
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
                                    invocation.Temperature,
                                    invocation.MaxTokens,
                                    invocation.Phase,
                                    invocation.Turn,
                                    attemptCancellationToken,
                                    attempt,
                                    maxAttempts,
                                    invocation.PrivatePhase,
                                    metadata,
                                    invocation.PriorMessages,
                                    callId: journal.CallId,
                                    promptContract: new PromptProviderContract(PromptProviderOperation.EmotionalDirectorUnstructured, invocation.RecipientRole, new[] { systemDocument, userDocument }, invocation.RoleFactAccessDecisions))
                                .ConfigureAwait(false);
                            direction = ParseCharacterDirectionOrThrow(
                                responseText,
                                false,
                                invocation.AllowedEmotions,
                                invocation.Phase,
                                null,
                                null,
                                invocation.Turn);
                        }

                        string? semanticEntryId = null;
                        if (invocation.PrivateBranch != null)
                        {
                            PiAcceptedExchangeEntryIds entryIds = await invocation.PrivateBranch
                                .AppendAcceptedExchangeAsync(userDocument.Text, responseText)
                                .ConfigureAwait(false);
                            semanticEntryId = entryIds.AssistantEntryId;
                        }
                        await journal.CompleteAcceptedAsync(responseText, semanticEntryId).ConfigureAwait(false);
                        return SemanticOutputRecoveryAttemptResult<CharacterEmotionalDirection, LlmContractException>.Accepted(direction);
                    }
                    catch (LlmContractException ex)
                    {
                        if (journal != null)
                            await journal.CompleteValidationRejectedAsync(ex.Reason).ConfigureAwait(false);
                        if (attempt < maxAttempts)
                            attemptSystemPrompt = invocation.CompileRetrySystemPrompt(ex.Reason);
                        return SemanticOutputRecoveryAttemptResult<CharacterEmotionalDirection, LlmContractException>.Rejected(ex);
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
                onRejected: rejection => NotifyContractViolation(rejection, invocation.PrivatePhase),
                cancellationToken: cancellationToken).ConfigureAwait(false);

            if (recovery.IsAccepted)
                return recovery.AcceptedValue;

            invocation.OnExhausted(recovery.Exhaustion.FinalRejection, recovery.Exhaustion.AttemptCount);
            ExceptionDispatchInfo.Capture(recovery.Exhaustion.FinalRejection).Throw();
            throw recovery.Exhaustion.FinalRejection;
        }

        private static CharacterEmotionalDirection ParseCharacterDirectionOrThrow(
            string? jsonText,
            bool requireCompleteJsonObject,
            IReadOnlyList<string> allowedEmotions,
            string phase,
            string? provider,
            string? model,
            int? turnId)
        {
            if (CharacterEmotionalDirectionContract.TryParse(
                jsonText,
                requireCompleteJsonObject,
                allowedEmotions,
                out CharacterEmotionalDirection? direction,
                out string errorCode))
            {
                return direction!;
            }

            throw new LlmContractException(
                phase,
                errorCode,
                "LLM character emotional direction output failed its private contract.",
                provider,
                model,
                CharacterEmotionalDirectionContract.ParserName,
                null,
                null,
                null,
                null,
                null,
                turnId);
        }
    }
}
