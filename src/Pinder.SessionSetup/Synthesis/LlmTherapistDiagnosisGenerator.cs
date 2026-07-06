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
            _transport = transport;
            _catalog = catalog;
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
            var systemPrompt = entry.SystemPrompt;
            
            var userPromptTemplate = entry.UserTemplate ?? "{backstory}\n{stakes}";
            var userPrompt = PromptCatalog.Substitute(userPromptTemplate, new Dictionary<string, string>
            {
                { "backstory", JsonSerializer.Serialize(backstory) },
                { "stakes", JsonSerializer.Serialize(stakeLines) }
            });

            var llmResponse = await _transport.SendAsync(
                systemPrompt,
                userPrompt,
                0.7,
                1024,
                LlmPhase.Synthesis,
                cancellationToken);

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            try
            {
                var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(llmResponse, options);
                if (dict == null || dict.Count == 0)
                {
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
            catch (JsonException)
            {
                // Suppress exception or log internally, returning a safe default dictionary
                return new Dictionary<string, string>();
            }
        }
    }
}
