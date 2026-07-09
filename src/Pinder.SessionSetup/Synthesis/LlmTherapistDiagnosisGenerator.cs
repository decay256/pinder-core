using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Pinder.Core.Characters;
using Pinder.Core.Interfaces;
using Pinder.LlmAdapters;

namespace Pinder.SessionSetup
{
    public class LlmTherapistDiagnosisGenerator : ITherapistDiagnosisGenerator
    {
        private readonly ILlmTransport _transport;
        private readonly PromptCatalog _catalog;

        public LlmTherapistDiagnosisGenerator(ILlmTransport transport, PromptCatalog catalog)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
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

            var llmResponse = await _transport.SendAsync(
                systemPrompt,
                userPrompt,
                entry.Temperature!.Value,
                entry.MaxTokens!.Value,
                LlmPhase.Synthesis,
                cancellationToken);

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            try
            {
                var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(llmResponse, options);
                if (dict == null)
                {
                    // The LLM returned the JSON literal `null` (or something that
                    // deserializes to it) rather than an object. That is not the
                    // same as a legitimate "no notable psychiatric traits" answer
                    // (which the model expresses as `{}`), so treat it as a
                    // malformed/contract-violating response and fail loud.
                    throw new JsonException("Deserialized diagnosis was null.");
                }

                if (dict.Count == 0)
                {
                    // A valid, empty JSON object is a legitimate answer: the
                    // character genuinely has no notable psychiatric diagnosis.
                    // This is success, not failure.
                    return new Dictionary<string, string>();
                }

                // Validate that keys and values are meaningful and non-empty
                var validatedDict = new Dictionary<string, string>();
                foreach (var kvp in dict)
                {
                    if (!string.IsNullOrWhiteSpace(kvp.Key) && !string.IsNullOrWhiteSpace(kvp.Value))
                    {
                        validatedDict[kvp.Key] = kvp.Value.Trim();
                    }
                }
                return validatedDict;
            }
            catch (JsonException ex)
            {
                // Fail-loud by propagating the failure with structural context,
                // mirroring LlmSequentialStakeGenerator: a malformed/unparseable
                // diagnosis response is a genuine generation failure, not a
                // valid empty diagnosis, and must not be silently swallowed.
                throw new InvalidOperationException(
                    LlmDiagnosticFormatter.GeneratedTextFailure(
                        "Failed to parse diagnosis JSON from LLM response.",
                        LlmPhase.Synthesis,
                        llmResponse),
                    ex);
            }
        }
    }
}
