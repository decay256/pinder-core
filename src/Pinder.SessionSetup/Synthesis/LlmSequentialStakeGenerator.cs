using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text;
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
                "stake",
                "prompt-catalog: missing required key 'stake'. The yaml file is incomplete or missing.");
        }

        public async Task<List<string>> GenerateAsync(
            string characterName, 
            string genderIdentity, 
            string bio, 
            Dictionary<string, BackstoryFact> backstory, 
            CancellationToken cancellationToken = default)
        {
            var stakeGenerator = new LlmStakeGenerator(
                _transport,
                streamingTransport: null,
                options: null,
                catalog: _catalog);
            var llmResponse = await stakeGenerator.GenerateAsync(
                characterName,
                BuildBackstoryOnlyProfile(backstory),
                cancellationToken).ConfigureAwait(false);

            try
            {
                var list = LlmStakeGenerator.ParseCanonicalStakeBullets(llmResponse);
                if (list.Count != 15)
                {
                    throw new FormatException(
                        $"Expected exactly 15 psychological stake items, got {list.Count}.");
                }
                return list;
            }
            catch (FormatException ex)
            {
                // Fail-loud by propagating the failure with structural context
                throw new System.InvalidOperationException(
                    LlmDiagnosticFormatter.GeneratedTextFailure(
                        "Failed to parse canonical 15-item stake bullet list from LLM response.",
                        LlmPhase.Synthesis,
                        llmResponse),
                    ex);
            }
        }

        private static string BuildBackstoryOnlyProfile(Dictionary<string, BackstoryFact> backstory)
        {
            var sb = new StringBuilder();
            sb.AppendLine("BACKSTORY JSON:");
            sb.Append(JsonSerializer.Serialize(backstory));
            return sb.ToString();
        }
    }
}
