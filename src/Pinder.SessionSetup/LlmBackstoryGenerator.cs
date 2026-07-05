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
    public class LlmBackstoryGenerator : IBackstoryGenerator
    {
        public sealed class Options
        {
            public double Temperature { get; set; } = 0.7;
            public int MaxTokens { get; set; } = 2000;
        }

        private readonly ILlmTransport _transport;
        private readonly Options _options;
        private readonly PromptCatalog _catalog;

        public LlmBackstoryGenerator(ILlmTransport transport, Options? options = null)
            : this(transport, options, catalog: null)
        {
        }

        public LlmBackstoryGenerator(
            ILlmTransport transport,
            Options? options,
            PromptCatalog? catalog)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _options = options ?? new Options();
            _catalog = catalog ?? PromptTemplates.Catalog
                ?? throw new InvalidOperationException("PromptTemplates.Catalog is not wired. Call PromptWiring.Wire() at startup.");

            // Enforce that the catalog contains the required key
            var entry = _catalog.TryGet("backstory")
                ?? throw new InvalidOperationException("prompt-catalog: missing required key 'backstory'.");
            if (string.IsNullOrWhiteSpace(entry.SystemPrompt))
                throw new InvalidOperationException("prompt-catalog: key 'backstory' has no system_prompt. Check the yaml file.");
            if (string.IsNullOrWhiteSpace(entry.UserTemplate))
                throw new InvalidOperationException("prompt-catalog: key 'backstory' has no user_template. Check the yaml file.");
        }

        public async Task<Dictionary<string, BackstoryFact>> GenerateAsync(
            string characterName,
            string genderIdentity,
            string bio,
            IReadOnlyList<string> backstoryFragments,
            CancellationToken cancellationToken = default)
        {
            var entry = _catalog.TryGet("backstory")
                ?? throw new InvalidOperationException("prompt-catalog: missing required key 'backstory'.");

            string systemPromptTemplate = entry.SystemPrompt
                ?? throw new InvalidOperationException("prompt-catalog: key 'backstory' has no system_prompt.");
            string userTemplate = entry.UserTemplate
                ?? throw new InvalidOperationException("prompt-catalog: key 'backstory' has no user_template.");

            string fragments = string.Join("\n", backstoryFragments);

            var values = new Dictionary<string, string>
            {
                { "characterName", characterName },
                { "genderIdentity", genderIdentity },
                { "bio", bio },
                { "fragments", fragments }
            };

            // Use PromptCatalog.Substitute for both system prompt (in case of variables) and user message
            string systemPrompt = PromptCatalog.Substitute(systemPromptTemplate, values);
            string userMessage = PromptCatalog.Substitute(userTemplate, values);

            var responseJson = await _transport.SendAsync(
                systemPrompt,
                userMessage,
                _options.Temperature,
                _options.MaxTokens,
                "backstory",
                cancellationToken);

            var result = new Dictionary<string, BackstoryFact>(StringComparer.OrdinalIgnoreCase);

            try
            {
                // Find start and end of JSON to strip out markdown backticks if any
                int startIdx = responseJson.IndexOf('{');
                int endIdx = responseJson.LastIndexOf('}');
                if (startIdx >= 0 && endIdx >= startIdx)
                {
                    responseJson = responseJson.Substring(startIdx, endIdx - startIdx + 1);
                }

                using var doc = JsonDocument.Parse(responseJson);
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    string cat = prop.Name;
                    string bioLie = GetValue(prop.Value, "BioLie", "bio_lie") ?? string.Empty;
                    string tragic = GetValue(prop.Value, "TragicReality", "tragic_reality") ?? string.Empty;

                    result[cat] = new BackstoryFact
                    {
                        BioLie = bioLie,
                        TragicReality = tragic
                    };
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to parse backstory JSON: {ex.Message}", ex);
            }

            return result;
        }

        private static string? GetValue(JsonElement element, string pascalCase, string snakeCase)
        {
            if (element.ValueKind != JsonValueKind.Object) return null;

            foreach (var prop in element.EnumerateObject())
            {
                if (string.Equals(prop.Name, pascalCase, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(prop.Name, snakeCase, StringComparison.OrdinalIgnoreCase))
                {
                    if (prop.Value.ValueKind == JsonValueKind.String)
                    {
                        return prop.Value.GetString();
                    }
                    return null;
                }
            }
            return null;
        }
    }
}