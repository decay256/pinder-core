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
    public class LlmSequentialStakeGenerator : ISequentialStakeGenerator
    {
        private readonly ILlmTransport _transport;
        private readonly PromptCatalog _catalog;

        public LlmSequentialStakeGenerator(ILlmTransport transport, PromptCatalog catalog)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _catalog.RequireCompleteEntry(
                "stakes",
                "prompt-catalog: missing required key 'stakes'. The yaml file is incomplete or missing.");
        }

        public async Task<List<string>> GenerateAsync(
            string characterName, 
            string genderIdentity, 
            string bio, 
            Dictionary<string, BackstoryFact> backstory, 
            CancellationToken cancellationToken = default)
        {
            var entry = _catalog.Get("stakes");
            var systemPrompt = entry.SystemPrompt!;
            var userPromptTemplate = entry.UserTemplate!;
            var userPrompt = PromptCatalog.Substitute(userPromptTemplate, new Dictionary<string, string>
            {
                { "backstory", JsonSerializer.Serialize(backstory) }
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
                var list = JsonSerializer.Deserialize<List<string>>(llmResponse, options);
                if (list == null)
                {
                    throw new System.Text.Json.JsonException("Deserialized stakes list was null.");
                }
                return list;
            }
            catch (System.Text.Json.JsonException ex)
            {
                // Fail-loud by propagating the failure with structural context
                throw new System.InvalidOperationException(
                    LlmDiagnosticFormatter.GeneratedTextFailure(
                        "Failed to parse stakes JSON from LLM response.",
                        LlmPhase.Synthesis,
                        llmResponse),
                    ex);
            }
        }
    }
}
