using System;
using System.Collections.Generic;
using System.Linq;
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
            var entry = _catalog.Get("stake");
            var systemPrompt = entry.SystemPrompt!;
            var userPromptTemplate = entry.UserTemplate!;
            var userPrompt = PromptCatalog.Substitute(userPromptTemplate, new Dictionary<string, string>
            {
                { "character_profile", BuildBackstoryOnlyProfile(backstory) }
            });

            var llmResponse = await _transport.SendAsync(
                systemPrompt,
                userPrompt,
                entry.Temperature!.Value,
                entry.MaxTokens!.Value,
                LlmPhase.Synthesis,
                cancellationToken);

            try
            {
                var list = ParseCanonicalStakeBullets(llmResponse);
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

        internal static List<string> ParseCanonicalStakeBullets(string llmResponse)
        {
            if (string.IsNullOrWhiteSpace(llmResponse))
                throw new FormatException("Stake response was empty.");

            var lines = llmResponse
                .Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
                .Select(line => line.Trim())
                .Where(line => line.Length > 0)
                .ToList();

            if (lines.Count == 0)
                throw new FormatException("Stake response contained no non-empty lines.");

            var result = new List<string>(capacity: lines.Count);
            foreach (var line in lines)
            {
                if (!line.StartsWith("- ", StringComparison.Ordinal))
                    throw new FormatException("Canonical stake response must contain only '- ' markdown bullet lines.");

                var body = line.Substring(2).Trim();
                if (body.Length == 0)
                    throw new FormatException("Canonical stake response contained an empty bullet.");

                result.Add(body);
            }

            return result;
        }
    }
}
