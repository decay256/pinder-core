using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Pinder.Core.Characters;
using Pinder.Core.Conversation;
using Pinder.Core.Interfaces;
using Pinder.LlmAdapters;

namespace Pinder.SessionSetup
{
    public class LlmTherapistDiagnosisGenerator : ITherapistDiagnosisGenerator
    {
        private const int MaxAttempts = 3;

        private readonly ILlmTransport _transport;
        private readonly PromptCatalog _catalog;
        private readonly Action<OperationalDiagnosticEvent>? _onDiagnostic;

        public LlmTherapistDiagnosisGenerator(
            ILlmTransport transport,
            PromptCatalog catalog,
            Action<OperationalDiagnosticEvent>? onDiagnostic = null)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _onDiagnostic = onDiagnostic;
            _catalog.RequireCompleteEntry(
                "diagnosis",
                "prompt-catalog: missing required key 'diagnosis'. The yaml file is incomplete or missing.");
        }

        public async Task<Dictionary<string, string>> GenerateAsync(
            string characterName, 
            string genderIdentity, 
            string bio, 
            Dictionary<string, BackstoryFact> backstory, 
            List<string> stakeLines, 
            CancellationToken cancellationToken = default)
        {
            var entry = _catalog.Get("diagnosis");
            var systemPrompt = entry.SystemPrompt!;
            var attemptSystemPrompt = systemPrompt;

            var userPromptTemplate = entry.UserTemplate!;
            var userPrompt = PromptCatalog.Substitute(userPromptTemplate, new Dictionary<string, string>
            {
                { "backstory", JsonSerializer.Serialize(backstory) },
                { "stakes", JsonSerializer.Serialize(stakeLines) }
            });

            var recovery = await SemanticOutputRecoveryExecutor.ExecuteAsync<Dictionary<string, string>, DiagnosisRejection>(
                MaxAttempts,
                async (attempt, attemptCancellationToken) =>
                {
                    string llmResponse;
                    StructuredLlmResponse? structuredResponse = null;
                    if (_transport is IStructuredLlmTransport structuredTransport)
                    {
                        structuredResponse = await structuredTransport.SendStructuredAsync(
                            TherapistDiagnosisStructuredContract.CreateRequest(
                                attemptSystemPrompt,
                                userPrompt,
                                entry.Temperature ?? 0.45,
                                entry.MaxTokens),
                            attemptCancellationToken).ConfigureAwait(false);
                        llmResponse = structuredResponse.JsonText;
                    }
                    else
                    {
                        llmResponse = await LlmOptionalTextGeneration.SendRequiredAsync(
                            "diagnosis",
                            _transport,
                            attemptSystemPrompt,
                            userPrompt,
                            entry.Temperature ?? 0.45,
                            entry.MaxTokens,
                            LlmPhase.Synthesis,
                            _onDiagnostic,
                            attemptCancellationToken).ConfigureAwait(false);
                    }

                    try
                    {
                        var dict = ParseDiagnosisJson(llmResponse);
                        if (dict == null)
                        {
                            // The LLM returned the JSON literal `null` (or something that
                            // deserializes to it) rather than an object. That is not the
                            // same as a diagnosis object satisfying the two required
                            // cognitive-subtext fields, so fail loudly.
                            throw new DiagnosisContractException(
                                "root_nonobject",
                                null,
                                "Deserialized diagnosis was null.");
                        }

                        var accepted = ValidateDiagnosis(dict);
                        structuredResponse?.ReportValidation("accepted");
                        return SemanticOutputRecoveryAttemptResult<Dictionary<string, string>, DiagnosisRejection>
                            .Accepted(accepted);
                    }
                    catch (Exception ex) when (ex is JsonException || ex is DiagnosisContractException)
                    {
                        var rejection = DiagnosisRejection.From(ex);
                        structuredResponse?.ReportValidation("rejected", rejection.Reason);
                        if (attempt < MaxAttempts)
                        {
                            attemptSystemPrompt = BuildRetrySystemPrompt(systemPrompt, rejection);
                        }

                        return SemanticOutputRecoveryAttemptResult<Dictionary<string, string>, DiagnosisRejection>
                            .Rejected(rejection);
                    }
                },
                onRejected: EmitRejectedDiagnostic,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            if (recovery.IsAccepted)
            {
                return recovery.AcceptedValue;
            }

            var finalRejection = recovery.Exhaustion.FinalRejection;
            throw new InvalidOperationException(
                $"Failed to parse diagnosis JSON from LLM response. " +
                $"Reason={finalRejection.Reason}; Field={finalRejection.Field ?? "none"}.",
                new JsonException(
                    finalRejection.Failure.Message,
                    finalRejection.Failure));
        }

        internal static Dictionary<string, string>? ParseDiagnosisJson(string llmResponse)
        {
            var extraction = GeneratedJsonObjectExtractor.TryExtractFirstValidObject(llmResponse);
            if (!extraction.Success)
                throw new DiagnosisContractException(
                    "invalid_json",
                    null,
                    $"Diagnosis response did not contain a valid JSON object. FailureCode={extraction.FailureCode}.");

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            try
            {
                return JsonSerializer.Deserialize<Dictionary<string, string>>(extraction.Json!, options);
            }
            catch (JsonException ex)
            {
                throw new DiagnosisContractException(
                    "invalid_json",
                    null,
                    "Diagnosis JSON could not be deserialized into string fields.",
                    ex);
            }
        }

        private static Dictionary<string, string> ValidateDiagnosis(
            Dictionary<string, string> diagnosis)
        {
            var normalized = NormalizeGeneratedDiagnosis(diagnosis);
            var validation = TherapistDiagnosisContract.ValidateRequiredFields(normalized);
            if (!validation.IsValid)
            {
                var violation = validation.Violation!;
                throw new DiagnosisContractException(
                    violation.Code,
                    violation.Field,
                    $"Diagnosis response violates contract: code={violation.Code}; field='{violation.Field}'. {violation.Message}");
            }

            return normalized;
        }

        private static Dictionary<string, string> NormalizeGeneratedDiagnosis(
            Dictionary<string, string> diagnosis)
        {
            var generatedFields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in diagnosis)
            {
                if (!string.IsNullOrWhiteSpace(pair.Key))
                    generatedFields[pair.Key.Trim()] = (pair.Value ?? string.Empty).Trim();
            }

            var selected = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string requiredField in TherapistDiagnosisContract.RequiredFields)
            {
                if (generatedFields.TryGetValue(requiredField, out var value))
                    selected[requiredField] = value;
            }

            return selected;
        }

        private string BuildRetrySystemPrompt(string basePrompt, DiagnosisRejection rejection)
        {
            string key = rejection.Field == null
                ? "diagnosis-repair-json"
                : "diagnosis-repair-field";
            var repairEntry = _catalog.TryGet(key);
            string? repairTemplate = repairEntry?.SystemPrompt;
            if (string.IsNullOrWhiteSpace(repairTemplate))
            {
                return basePrompt;
            }

            string repairPrompt = PromptCatalog.Substitute(
                repairTemplate!,
                new Dictionary<string, string>
                {
                    ["field"] = rejection.Field ?? string.Empty,
                });
            return basePrompt + Environment.NewLine + Environment.NewLine + repairPrompt;
        }

        private void EmitRejectedDiagnostic(
            SemanticOutputRecoveryRejection<DiagnosisRejection> rejection)
        {
            var hints = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["generator"] = "diagnosis",
                ["reason"] = rejection.Rejection.Reason,
                ["attempt"] = rejection.Attempt.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["total_attempts"] = rejection.TotalAttempts.ToString(System.Globalization.CultureInfo.InvariantCulture),
            };
            if (rejection.Rejection.Field != null)
            {
                hints["field"] = rejection.Rejection.Field;
            }

            OperationalDiagnostics.Emit(
                _onDiagnostic,
                new OperationalDiagnosticEvent(
                    "LlmTherapistDiagnosisGenerator",
                    "DiagnosisContractRejected",
                    rejection.IsFinalAttempt
                        ? OperationalDiagnosticSeverity.Error
                        : OperationalDiagnosticSeverity.Warning,
                    "Therapist diagnosis output failed structured validation.",
                    operationKind: OperationalDiagnosticOperationKind.SetupSynthesis,
                    phaseCode: LlmPhase.Synthesis,
                    lifecycle: rejection.IsFinalAttempt
                        ? OperationalDiagnosticLifecycle.Terminal
                        : OperationalDiagnosticLifecycle.Phase,
                    outcome: rejection.IsFinalAttempt
                        ? OperationalDiagnosticOutcome.Failed
                        : OperationalDiagnosticOutcome.Degraded,
                    failureClassification: OperationalDiagnosticFailureClassification.Permanent,
                    callId: OperationalDiagnostics.CreateCallId(),
                    correlationHints: hints));
        }

        private sealed class DiagnosisRejection
        {
            private DiagnosisRejection(string reason, string? field, Exception failure)
            {
                Reason = reason;
                Field = field;
                Failure = failure;
            }

            public string Reason { get; }

            public string? Field { get; }

            public Exception Failure { get; }

            public static DiagnosisRejection From(Exception failure)
            {
                if (failure is DiagnosisContractException contract)
                {
                    return new DiagnosisRejection(contract.Reason, contract.Field, contract);
                }

                return new DiagnosisRejection("invalid_json", null, failure);
            }
        }

        private sealed class DiagnosisContractException : JsonException
        {
            public DiagnosisContractException(
                string reason,
                string? field,
                string message,
                Exception? innerException = null)
                : base(message, innerException)
            {
                Reason = reason;
                Field = field;
            }

            public string Reason { get; }

            public string? Field { get; }
        }
    }
}
