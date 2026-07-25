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
                    string llmResponse = await LlmOptionalTextGeneration.SendRequiredAsync(
                        "diagnosis",
                        _transport,
                        systemPrompt,
                        userPrompt,
                        entry.Temperature!.Value,
                        entry.MaxTokens!.Value,
                        LlmPhase.Synthesis,
                        _onDiagnostic,
                        attemptCancellationToken).ConfigureAwait(false);

                    try
                    {
                        var dict = ParseDiagnosisJson(llmResponse);
                        if (dict == null)
                        {
                            // The LLM returned the JSON literal `null` (or something that
                            // deserializes to it) rather than an object. That is not the
                            // same as a diagnosis object satisfying the two required
                            // cognitive-subtext fields, so fail loudly.
                            throw new JsonException("Deserialized diagnosis was null.");
                        }

                        return SemanticOutputRecoveryAttemptResult<Dictionary<string, string>, DiagnosisRejection>.Accepted(
                            ValidateDiagnosis(dict));
                    }
                    catch (JsonException ex)
                    {
                        return SemanticOutputRecoveryAttemptResult<Dictionary<string, string>, DiagnosisRejection>.Rejected(
                            new DiagnosisRejection(llmResponse, ex));
                    }
                },
                cancellationToken: cancellationToken).ConfigureAwait(false);

            if (recovery.IsAccepted)
            {
                return recovery.AcceptedValue;
            }

            var finalRejection = recovery.Exhaustion.FinalRejection;
            // Fail-loud by propagating the failure with structural context,
            // mirroring LlmSequentialStakeGenerator: a malformed/unparseable
            // diagnosis response is a genuine generation failure, not a
            // valid empty diagnosis, and must not be silently swallowed.
            throw new InvalidOperationException(
                LlmDiagnosticFormatter.GeneratedTextFailure(
                    "Failed to parse diagnosis JSON from LLM response.",
                    LlmPhase.Synthesis,
                    finalRejection.GeneratedText),
                finalRejection.Failure);
        }

        internal static Dictionary<string, string>? ParseDiagnosisJson(string llmResponse)
        {
            var json = ExtractJsonObject(llmResponse);
            if (json == null)
                throw new JsonException("Diagnosis response did not contain a JSON object.");

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json, options);
        }

        private static Dictionary<string, string> ValidateDiagnosis(
            Dictionary<string, string> diagnosis)
        {
            var normalized = NormalizeGeneratedDiagnosis(diagnosis);
            var validation = TherapistDiagnosisContract.ValidateRequiredFields(normalized);
            if (!validation.IsValid)
            {
                var violation = validation.Violation!;
                throw new JsonException(
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

        internal static string? ExtractJsonObject(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;

            for (int start = text.IndexOf('{'); start >= 0 && start < text.Length; start = text.IndexOf('{', start + 1))
            {
                int depth = 0;
                bool inString = false;
                bool escaped = false;

                for (int i = start; i < text.Length; i++)
                {
                    char c = text[i];
                    if (inString)
                    {
                        if (escaped)
                        {
                            escaped = false;
                        }
                        else if (c == '\\')
                        {
                            escaped = true;
                        }
                        else if (c == '"')
                        {
                            inString = false;
                        }
                        continue;
                    }

                    if (c == '"')
                    {
                        inString = true;
                        continue;
                    }

                    if (c == '{')
                    {
                        depth++;
                    }
                    else if (c == '}')
                    {
                        depth--;
                        if (depth == 0)
                        {
                            var candidate = text.Substring(start, i - start + 1);
                            try
                            {
                                using var doc = JsonDocument.Parse(candidate);
                                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                                    return candidate;
                            }
                            catch (JsonException)
                            {
                                break;
                            }
                        }
                    }
                }
            }

            return null;
        }

        private sealed class DiagnosisRejection
        {
            public DiagnosisRejection(string generatedText, JsonException failure)
            {
                GeneratedText = generatedText;
                Failure = failure;
            }

            public string GeneratedText { get; }

            public JsonException Failure { get; }
        }
    }
}
