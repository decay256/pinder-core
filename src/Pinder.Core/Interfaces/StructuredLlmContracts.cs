using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Pinder.Core.Interfaces
{
    /// <summary>
    /// Provider-neutral request for an LLM phase that expects structured JSON.
    /// Provider adapters may map this to native structured output, tools, or
    /// strict JSON mode while preserving the same schema metadata.
    /// </summary>
    public sealed class StructuredLlmRequest
    {
        public StructuredLlmRequest(
            string schemaName,
            string schemaVersion,
            string jsonSchema,
            string systemPrompt,
            string userMessage,
            double temperature,
            int maxTokens,
            string phase,
            IReadOnlyDictionary<string, string>? metadata = null)
        {
            SchemaName = schemaName ?? throw new ArgumentNullException(nameof(schemaName));
            SchemaVersion = schemaVersion ?? throw new ArgumentNullException(nameof(schemaVersion));
            JsonSchema = jsonSchema ?? throw new ArgumentNullException(nameof(jsonSchema));
            SystemPrompt = systemPrompt ?? throw new ArgumentNullException(nameof(systemPrompt));
            UserMessage = userMessage ?? throw new ArgumentNullException(nameof(userMessage));
            Temperature = temperature;
            MaxTokens = maxTokens;
            Phase = phase ?? throw new ArgumentNullException(nameof(phase));
            Metadata = metadata ?? new Dictionary<string, string>();
        }

        public string SchemaName { get; }
        public string SchemaVersion { get; }
        public string JsonSchema { get; }
        public string SystemPrompt { get; }
        public string UserMessage { get; }
        public double Temperature { get; }
        public int MaxTokens { get; }
        public string Phase { get; }
        public IReadOnlyDictionary<string, string> Metadata { get; }
    }

    /// <summary>
    /// Provider-neutral structured response with transport metadata kept outside
    /// the game parser so verifier/telemetry can distinguish native mode from
    /// local JSON fallback.
    /// </summary>
    public sealed class StructuredLlmResponse
    {
        private readonly Action<StructuredLlmValidationResult>? _validationObserver;
        private int _validationReported;

        public StructuredLlmResponse(
            string jsonText,
            string? provider = null,
            string? model = null,
            bool usedNativeStructuredOutput = false,
            IReadOnlyDictionary<string, string>? metadata = null,
            string? providerRequestJson = null,
            string? validationMode = null,
            Action<StructuredLlmValidationResult>? validationObserver = null)
        {
            JsonText = jsonText ?? string.Empty;
            Provider = provider;
            Model = model;
            UsedNativeStructuredOutput = usedNativeStructuredOutput;
            Metadata = metadata ?? new Dictionary<string, string>();
            ProviderRequestJson = providerRequestJson;
            ValidationMode = validationMode ?? (usedNativeStructuredOutput ? "native_structured_output" : "local_validation");
            _validationObserver = validationObserver;
        }

        public string JsonText { get; }
        public string? Provider { get; }
        public string? Model { get; }
        public bool UsedNativeStructuredOutput { get; }
        public IReadOnlyDictionary<string, string> Metadata { get; }
        public string? ProviderRequestJson { get; }
        public string ValidationMode { get; }

        public void ReportValidation(string outcome, string? rejectionReason = null)
        {
            if (System.Threading.Interlocked.Exchange(ref _validationReported, 1) != 0) return;
            try
            {
                _validationObserver?.Invoke(new StructuredLlmValidationResult(
                    ValidationMode,
                    outcome,
                    rejectionReason));
            }
            catch
            {
                // Trace observers are diagnostic and must never change game flow.
            }
        }

        public StructuredLlmResponse WithJsonText(string jsonText)
            => new StructuredLlmResponse(
                jsonText,
                Provider,
                Model,
                UsedNativeStructuredOutput,
                Metadata,
                ProviderRequestJson,
                ValidationMode,
                result => ReportValidation(result.Outcome, result.RejectionReason));

        public StructuredLlmResponse WithValidationObserver(Action<StructuredLlmValidationResult> observer)
        {
            if (observer == null) throw new ArgumentNullException(nameof(observer));
            return new StructuredLlmResponse(
                JsonText,
                Provider,
                Model,
                UsedNativeStructuredOutput,
                Metadata,
                ProviderRequestJson,
                ValidationMode,
                result =>
                {
                    ReportValidation(result.Outcome, result.RejectionReason);
                    observer(result);
                });
        }
    }

    public sealed class StructuredLlmValidationResult
    {
        public StructuredLlmValidationResult(string mode, string outcome, string? rejectionReason)
        {
            Mode = mode ?? throw new ArgumentNullException(nameof(mode));
            Outcome = outcome ?? throw new ArgumentNullException(nameof(outcome));
            RejectionReason = rejectionReason;
        }

        public string Mode { get; }
        public string Outcome { get; }
        public string? RejectionReason { get; }
    }

    /// <summary>
    /// Optional transport capability for phases that can request a structured
    /// JSON response without provider-specific game logic.
    /// </summary>
    public interface IStructuredLlmTransport
    {
        Task<StructuredLlmResponse> SendStructuredAsync(
            StructuredLlmRequest request,
            CancellationToken ct = default);
    }
}
