using System;
using System.Collections.Generic;
using System.Linq;
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
        private const string EmotionalDirectorPromptKey = "emotional-reaction-director";

        internal async Task<EmotionalDirectorDirection> GenerateEmotionalDirectionAsync(
            DateeContext context,
            CancellationToken cancellationToken = default)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            var catalog = PromptCatalog.ResolveCatalogOrThrow(_options.PromptCatalog);
            var prompt = catalog.RequireCompleteEntry(
                EmotionalDirectorPromptKey,
                "prompt-catalog: missing required runtime prompt key 'emotional-reaction-director'. The yaml file is incomplete or missing.");

            var compiled = new EmotionalReactionEventCompiler(catalog).Compile(context);
            string userMessage = PromptCatalog.Substitute(
                prompt.UserTemplate!,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    { "compiled_reaction_input", compiled.Text },
                }).Trim();
            string systemPrompt = prompt.SystemPrompt!.Trim();
            double temperature = prompt.Temperature ?? LlmPhaseTemperatures.EmotionalDirector;
            int maxTokens = prompt.MaxTokens ?? _options.MaxTokens;
            var metadata = BuildEmotionalDirectorMetadata(prompt, compiled, context);

            int maxAttempts = GetContractViolationAttemptLimit();
            var recovery = await SemanticOutputRecoveryExecutor.ExecuteAsync<EmotionalDirectorDirection, LlmContractException>(
                maxAttempts,
                async (attempt, attemptCancellationToken) =>
                {
                    try
                    {
                        EmotionalDirectorDirection direction;
                        if (_transport is IStructuredLlmTransport structuredTransport)
                        {
                            var request = EmotionalDirectorContract.CreateRequest(
                                systemPrompt,
                                userMessage,
                                temperature,
                                maxTokens,
                                metadata);
                            var structuredResponse = await SendStructuredWithDiagnosticsAsync(
                                    structuredTransport,
                                    request,
                                    LlmPhase.EmotionalDirector,
                                    context.CurrentTurn,
                                    attemptCancellationToken,
                                    attempt,
                                    maxAttempts,
                                    DateePrivatePhaseDirector,
                                    metadata)
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
                                    systemPrompt,
                                    userMessage,
                                    temperature,
                                    maxTokens,
                                    LlmPhase.EmotionalDirector,
                                    context.CurrentTurn,
                                    attemptCancellationToken,
                                    attempt,
                                    maxAttempts,
                                    DateePrivatePhaseDirector,
                                    metadata)
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

        private static Dictionary<string, string> BuildEmotionalDirectorMetadata(
            PromptEntry prompt,
            PromptTraceResult compiled,
            DateeContext context)
        {
            var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "phase", LlmPhase.EmotionalDirector },
                { "prompt_key", EmotionalDirectorPromptKey },
                { "system_prompt_source", prompt.SourceFile ?? string.Empty },
                { "user_template_source", prompt.SourceFile ?? string.Empty },
                { "turn", context.CurrentTurn.ToString(System.Globalization.CultureInfo.InvariantCulture) },
            };

            string sources = string.Join(
                ",",
                compiled.Spans
                    .Select(span => span.SourceFile ?? string.Empty)
                    .Where(source => !string.IsNullOrWhiteSpace(source))
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(source => source, StringComparer.Ordinal));
            if (sources.Length > 0)
            {
                metadata["compiled_input_sources"] = sources;
            }

            string keys = string.Join(
                ",",
                compiled.Spans
                    .Select(span => span.Key ?? string.Empty)
                    .Where(key => !string.IsNullOrWhiteSpace(key))
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(key => key, StringComparer.Ordinal));
            if (keys.Length > 0)
            {
                metadata["compiled_input_keys"] = keys;
            }

            return metadata;
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
