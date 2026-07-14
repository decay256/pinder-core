using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Pinder.Core.Conversation;
using Pinder.Core.Interfaces;
using Pinder.LlmAdapters;

namespace Pinder.SessionSetup
{
    internal static class LlmOptionalTextGeneration
    {
        internal enum CancellationBehavior
        {
            ReturnEmpty,
            Throw
        }

        public static async Task<string> RunAsync(
            string generatorName,
            ILlmTransport transport,
            string systemPrompt,
            string userMessage,
            PromptEntry entry,
            string phase,
            double configuredTemperature,
            double defaultTemperature,
            int configuredMaxTokens,
            int defaultMaxTokens,
            Action<SetupGenerationResult>? onDegraded,
            Action<OperationalDiagnosticEvent>? onDiagnostic,
            CancellationBehavior cancellationBehavior,
            CancellationToken cancellationToken = default,
            bool passCancellationTokenToTransport = false)
        {
            if (generatorName == null) throw new ArgumentNullException(nameof(generatorName));
            if (transport == null) throw new ArgumentNullException(nameof(transport));
            if (systemPrompt == null) throw new ArgumentNullException(nameof(systemPrompt));
            if (userMessage == null) throw new ArgumentNullException(nameof(userMessage));
            if (entry == null) throw new ArgumentNullException(nameof(entry));
            if (phase == null) throw new ArgumentNullException(nameof(phase));

            string callId = EmitSetupSynthesisStarted(onDiagnostic, generatorName, phase);

            bool terminalEmitted = false;
            try
            {
                double temperature = configuredTemperature != defaultTemperature
                    ? configuredTemperature
                    : entry.Temperature!.Value;
                int maxTokens = configuredMaxTokens != defaultMaxTokens
                    ? configuredMaxTokens
                    : entry.MaxTokens!.Value;

                CancellationToken sendCancellationToken = passCancellationTokenToTransport
                    ? cancellationToken
                    : default;

                string response = await transport
                    .SendAsync(
                        systemPrompt,
                        userMessage,
                        temperature,
                        maxTokens,
                        phase: phase,
                        ct: sendCancellationToken)
                    .ConfigureAwait(false);

                string trimmed = (response ?? string.Empty).Trim();
                if (string.IsNullOrEmpty(trimmed))
                {
                    onDegraded?.Invoke(
                        SetupGenerationResult.DegradedFailure(generatorName, "empty_output"));
                    EmitSetupSynthesisDegraded(
                        onDiagnostic,
                        generatorName,
                        phase,
                        callId,
                        reason: "empty_output",
                        exception: null,
                        failureClassification: OperationalDiagnosticFailureClassification.Degraded);
                    terminalEmitted = true;
                }
                else
                {
                    EmitSetupSynthesisSucceeded(onDiagnostic, generatorName, phase, callId);
                    terminalEmitted = true;
                }

                return trimmed;
            }
            catch (OperationCanceledException ex)
            {
                if (!terminalEmitted)
                {
                    EmitSetupSynthesisCancelled(onDiagnostic, generatorName, phase, callId, ex);
                }

                if (cancellationBehavior == CancellationBehavior.ReturnEmpty)
                {
                    return string.Empty;
                }

                throw;
            }
            catch (Exception ex)
            {
                if (onDegraded != null)
                {
                    onDegraded.Invoke(
                        SetupGenerationResult.DegradedFailure(generatorName, "transport_error"));
                    EmitSetupSynthesisDegraded(
                        onDiagnostic,
                        generatorName,
                        phase,
                        callId,
                        reason: "transport_error",
                        exception: ex,
                        failureClassification: OperationalDiagnostics.ClassifyException(ex));
                    return string.Empty;
                }

                EmitSetupSynthesisFailed(onDiagnostic, generatorName, phase, callId, ex);
                throw;
            }
        }

        private static string EmitSetupSynthesisStarted(
            Action<OperationalDiagnosticEvent>? onDiagnostic,
            string generatorName,
            string phase)
        {
            string callId = OperationalDiagnostics.CreateCallId();
            OperationalDiagnostics.Emit(
                onDiagnostic,
                new OperationalDiagnosticEvent(
                    "LlmOptionalTextGeneration",
                    "SetupSynthesisStarted",
                    OperationalDiagnosticSeverity.Info,
                    "Setup synthesis LLM operation started.",
                    operationKind: OperationalDiagnosticOperationKind.SetupSynthesis,
                    phaseCode: phase,
                    lifecycle: OperationalDiagnosticLifecycle.Start,
                    callId: callId,
                    correlationHints: GeneratorHints(generatorName)));
            return callId;
        }

        private static void EmitSetupSynthesisSucceeded(
            Action<OperationalDiagnosticEvent>? onDiagnostic,
            string generatorName,
            string phase,
            string callId)
        {
            OperationalDiagnostics.Emit(
                onDiagnostic,
                new OperationalDiagnosticEvent(
                    "LlmOptionalTextGeneration",
                    "SetupSynthesisSucceeded",
                    OperationalDiagnosticSeverity.Info,
                    "Setup synthesis LLM operation succeeded.",
                    operationKind: OperationalDiagnosticOperationKind.SetupSynthesis,
                    phaseCode: phase,
                    lifecycle: OperationalDiagnosticLifecycle.Terminal,
                    outcome: OperationalDiagnosticOutcome.Succeeded,
                    callId: callId,
                    correlationHints: GeneratorHints(generatorName)));
        }

        private static void EmitSetupSynthesisCancelled(
            Action<OperationalDiagnosticEvent>? onDiagnostic,
            string generatorName,
            string phase,
            string callId,
            OperationCanceledException exception)
        {
            OperationalDiagnostics.Emit(
                onDiagnostic,
                new OperationalDiagnosticEvent(
                    "LlmOptionalTextGeneration",
                    "SetupSynthesisCancelled",
                    OperationalDiagnosticSeverity.Warning,
                    "Setup synthesis LLM operation was cancelled.",
                    exception,
                    OperationalDiagnosticOperationKind.SetupSynthesis,
                    phase,
                    OperationalDiagnosticLifecycle.Terminal,
                    OperationalDiagnosticOutcome.Cancelled,
                    OperationalDiagnosticFailureClassification.Cancelled,
                    callId: callId,
                    correlationHints: GeneratorHints(generatorName)));
        }

        private static void EmitSetupSynthesisFailed(
            Action<OperationalDiagnosticEvent>? onDiagnostic,
            string generatorName,
            string phase,
            string callId,
            Exception exception)
        {
            OperationalDiagnostics.Emit(
                onDiagnostic,
                new OperationalDiagnosticEvent(
                    "LlmOptionalTextGeneration",
                    "SetupSynthesisFailed",
                    OperationalDiagnosticSeverity.Error,
                    "Setup synthesis LLM operation failed.",
                    exception,
                    OperationalDiagnosticOperationKind.SetupSynthesis,
                    phase,
                    OperationalDiagnosticLifecycle.Terminal,
                    OperationalDiagnosticOutcome.Failed,
                    OperationalDiagnostics.ClassifyException(exception),
                    callId: callId,
                    correlationHints: GeneratorHints(generatorName)));
        }

        private static void EmitSetupSynthesisDegraded(
            Action<OperationalDiagnosticEvent>? onDiagnostic,
            string generatorName,
            string phase,
            string callId,
            string reason,
            Exception? exception,
            OperationalDiagnosticFailureClassification failureClassification)
        {
            OperationalDiagnostics.Emit(
                onDiagnostic,
                new OperationalDiagnosticEvent(
                    "LlmOptionalTextGeneration",
                    "SetupSynthesisDegraded",
                    OperationalDiagnosticSeverity.Warning,
                    "Setup synthesis LLM operation degraded.",
                    exception,
                    OperationalDiagnosticOperationKind.SetupSynthesis,
                    phase,
                    OperationalDiagnosticLifecycle.Terminal,
                    OperationalDiagnosticOutcome.Degraded,
                    failureClassification,
                    callId: callId,
                    correlationHints: GeneratorHints(generatorName, reason)));
        }

        private static Dictionary<string, string> GeneratorHints(
            string generatorName,
            string? reason = null)
        {
            var hints = new Dictionary<string, string>
            {
                ["generator"] = generatorName,
            };
            if (reason != null)
            {
                hints["reason"] = reason;
            }

            return hints;
        }

        public static async Task<string> SendRequiredAsync(
            string generatorName,
            ILlmTransport transport,
            string systemPrompt,
            string userMessage,
            double temperature,
            int maxTokens,
            string phase,
            Action<OperationalDiagnosticEvent>? onDiagnostic,
            CancellationToken cancellationToken = default)
        {
            if (generatorName == null) throw new ArgumentNullException(nameof(generatorName));
            if (transport == null) throw new ArgumentNullException(nameof(transport));
            if (systemPrompt == null) throw new ArgumentNullException(nameof(systemPrompt));
            if (userMessage == null) throw new ArgumentNullException(nameof(userMessage));
            if (phase == null) throw new ArgumentNullException(nameof(phase));

            string callId = EmitSetupSynthesisStarted(onDiagnostic, generatorName, phase);

            try
            {
                string result = await transport
                    .SendAsync(systemPrompt, userMessage, temperature, maxTokens, phase, cancellationToken)
                    .ConfigureAwait(false);

                EmitSetupSynthesisSucceeded(onDiagnostic, generatorName, phase, callId);

                return result;
            }
            catch (OperationCanceledException ex)
            {
                EmitSetupSynthesisCancelled(onDiagnostic, generatorName, phase, callId, ex);
                throw;
            }
            catch (Exception ex)
            {
                EmitSetupSynthesisFailed(onDiagnostic, generatorName, phase, callId, ex);
                throw;
            }
        }
    }
}
