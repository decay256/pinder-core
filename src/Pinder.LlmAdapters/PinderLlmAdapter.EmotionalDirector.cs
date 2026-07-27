using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Pinder.Core.Conversation;
using Pinder.Core.Interfaces;
using Pinder.Core.Text;

namespace Pinder.LlmAdapters
{
    public sealed partial class PinderLlmAdapter
    {
        internal Task<EmotionalDirectorDirection> GenerateEmotionalDirectionAsync(
            DateeContext context,
            CancellationToken cancellationToken = default)
        {
            var promptCompiler = new EmotionalPromptCompiler(
                PromptCatalog.ResolveCatalogOrThrow(_options.PromptCatalog));
            return GenerateEmotionalDirectionAsync(context, promptCompiler, cancellationToken);
        }

        internal async Task<EmotionalDirectorDirection> GenerateEmotionalDirectionAsync(
            DateeContext context,
            EmotionalPromptCompiler promptCompiler,
            CancellationToken cancellationToken = default)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (promptCompiler == null) throw new ArgumentNullException(nameof(promptCompiler));

            CompiledEmotionalDirectorPrompt prompt =
                promptCompiler.CompileDirector(context);
            string userMessage = prompt.UserPrompt.Text;
            PromptTraceResult systemPrompt = prompt.SystemPrompt;
            PromptTraceResult attemptSystemPrompt = systemPrompt;
            double temperature = prompt.Temperature ?? LlmPhaseTemperatures.EmotionalDirector;
            int maxTokens = prompt.MaxTokens ?? _options.MaxTokens;
            var metadata = prompt.Metadata;

            int maxAttempts = GetContractViolationAttemptLimit();
            var recovery = await SemanticOutputRecoveryExecutor.ExecuteAsync<EmotionalDirectorDirection, LlmContractException>(
                maxAttempts,
                async (attempt, attemptCancellationToken) =>
                {
                    try
                    {
                        var attemptMetadata = BuildEmotionalDirectorMetadata(
                            metadata,
                            attemptSystemPrompt);
                        EmotionalDirectorDirection direction;
                        if (_transport is IStructuredLlmTransport structuredTransport)
                        {
                            var request = EmotionalDirectorContract.CreateRequest(
                                attemptSystemPrompt.Text,
                                userMessage,
                                temperature,
                                maxTokens,
                                attemptMetadata);
                            var structuredResponse = await SendStructuredWithDiagnosticsAsync(
                                    structuredTransport,
                                    request,
                                    LlmPhase.EmotionalDirector,
                                    context.CurrentTurn,
                                    attemptCancellationToken,
                                    attempt,
                                    maxAttempts,
                                    DateePrivatePhaseDirector,
                                    attemptMetadata)
                                .ConfigureAwait(false);

                            try
                            {
                                direction = ParseEmotionalDirectorOrThrow(
                                    structuredResponse.JsonText,
                                    requireCompleteJsonObject: structuredResponse.UsedNativeStructuredOutput,
                                    structuredResponse.Provider,
                                    structuredResponse.Model,
                                    context.CurrentTurn);
                                structuredResponse.ReportValidation("accepted");
                            }
                            catch (LlmContractException ex)
                            {
                                structuredResponse.ReportValidation("rejected", ex.Reason);
                                throw;
                            }
                        }
                        else
                        {
                            string responseText = await SendWithDiagnosticsAsync(
                                    _transport,
                                    attemptSystemPrompt.Text,
                                    userMessage,
                                    temperature,
                                    maxTokens,
                                    LlmPhase.EmotionalDirector,
                                    context.CurrentTurn,
                                    attemptCancellationToken,
                                    attempt,
                                    maxAttempts,
                                    DateePrivatePhaseDirector,
                                    attemptMetadata)
                                .ConfigureAwait(false);
                            direction = ParseEmotionalDirectorOrThrow(
                                responseText,
                                requireCompleteJsonObject: false,
                                provider: null,
                                model: null,
                                turnId: context.CurrentTurn);
                        }

                        return SemanticOutputRecoveryAttemptResult<EmotionalDirectorDirection, LlmContractException>.Accepted(direction);
                    }
                    catch (LlmContractException ex)
                    {
                        if (attempt < maxAttempts)
                        {
                            attemptSystemPrompt = promptCompiler.CompileDirectorRetrySystemPrompt(
                                systemPrompt,
                                ex.Reason);
                        }
                        return SemanticOutputRecoveryAttemptResult<EmotionalDirectorDirection, LlmContractException>.Rejected(ex);
                    }
                },
                delayAfterRejectedAttempt: attempt => TimeSpan.FromMilliseconds(
                    GetContractViolationBackoffDelayMs(_options.ContractViolationBackoffMs, attempt)),
                onRejected: rejection => NotifyContractViolation(rejection, DateePrivatePhaseDirector),
                cancellationToken: cancellationToken).ConfigureAwait(false);

            if (recovery.IsAccepted)
            {
                return recovery.AcceptedValue;
            }

            EmitEmotionalDirectorExhaustedDiagnostic(
                recovery.Exhaustion.FinalRejection,
                recovery.Exhaustion.AttemptCount,
                context.CurrentTurn);
            ExceptionDispatchInfo.Capture(recovery.Exhaustion.FinalRejection).Throw();
            throw recovery.Exhaustion.FinalRejection;
        }

        private void EmitEmotionalDirectorExhaustedDiagnostic(
            LlmContractException finalRejection,
            int attemptCount,
            int turn)
        {
            OperationalDiagnostics.Emit(
                GetDiagnosticSink(),
                new OperationalDiagnosticEvent(
                    source: "PinderLlmAdapter",
                    eventName: "EmotionalDirectorContractExhausted",
                    severity: OperationalDiagnosticSeverity.Error,
                    message: "Private emotional director contract recovery was exhausted.",
                    exception: null,
                    operationKind: OperationalDiagnosticOperationKind.DateeEmotionalDirector,
                    phaseCode: LlmPhase.EmotionalDirector,
                    lifecycle: OperationalDiagnosticLifecycle.Terminal,
                    outcome: OperationalDiagnosticOutcome.Failed,
                    failureClassification: OperationalDiagnosticFailureClassification.Permanent,
                    callId: OperationalDiagnostics.CreateCallId(),
                    correlationHints: new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["phase"] = LlmPhase.EmotionalDirector,
                        ["datee_private_phase"] = DateePrivatePhaseDirector,
                        ["exception_type"] = finalRejection.GetType().Name,
                        ["failure_kind"] = "contract_violation",
                        ["reason"] = finalRejection.Reason,
                        ["attempt_count"] = attemptCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        ["turn"] = turn.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    }));
        }

        private static EmotionalDirectorDirection ParseEmotionalDirectorOrThrow(
            string? jsonText,
            bool requireCompleteJsonObject,
            string? provider,
            string? model,
            int? turnId)
        {
            if (EmotionalDirectorContract.TryParse(
                jsonText,
                requireCompleteJsonObject,
                out var direction,
                out string errorCode))
            {
                return direction!;
            }

            throw new LlmContractException(
                phase: LlmPhase.EmotionalDirector,
                reason: errorCode,
                message: "LLM emotional_director output failed the private direction contract.",
                provider: provider,
                model: model,
                parserName: EmotionalDirectorContract.ParserName,
                expectedOptionCount: null,
                parsedOptionCount: null,
                optionCount: null,
                signalCount: null,
                sessionId: null,
                turnId: turnId);
        }
    }
}
