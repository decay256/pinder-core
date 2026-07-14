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
    public class LlmBackstoryGenerator : IBackstoryGenerator
    {
        public sealed class Options
        {
            public double Temperature { get; set; } = GeneratorDefaultConfigs.Backstory.Temperature;
            public int MaxTokens { get; set; } = GeneratorDefaultConfigs.Backstory.MaxTokens;
            public Action<OperationalDiagnosticEvent>? OnDiagnostic { get; set; }
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
            _catalog = PromptCatalog.ResolveCatalogOrThrow(catalog);
            _catalog.RequireCompleteEntry(
                "backstory",
                "prompt-catalog: missing required key 'backstory'.");
        }

        public async Task<Dictionary<string, BackstoryFact>> GenerateAsync(
            string characterName,
            string genderIdentity,
            string bio,
            IReadOnlyList<string> backstoryFragments,
            CancellationToken cancellationToken = default)
            => await GenerateFromConsolidatedAsync(characterName, genderIdentity, bio,
                string.Join("\n", backstoryFragments), string.Empty, cancellationToken).ConfigureAwait(false);

        public async Task<Dictionary<string, BackstoryFact>> GenerateFromConsolidatedAsync(
            string characterName,
            string genderIdentity,
            string bio,
            string consolidatedBackstory,
            string consolidatedPersonality,
            CancellationToken cancellationToken = default)
        {
            var entry = _catalog.TryGet("backstory")
                ?? throw new InvalidOperationException("prompt-catalog: missing required key 'backstory'.");

            string systemPromptTemplate = entry.SystemPrompt
                ?? throw new InvalidOperationException("prompt-catalog: key 'backstory' has no system_prompt.");
            string userTemplate = entry.UserTemplate
                ?? throw new InvalidOperationException("prompt-catalog: key 'backstory' has no user_template.");

            var values = new Dictionary<string, string>
            {
                { "characterName", characterName },
                { "genderIdentity", genderIdentity },
                { "bio", bio },
                { "consolidated_backstory", consolidatedBackstory },
                { "consolidated_personality", consolidatedPersonality }
            };

            // Use PromptCatalog.Substitute for both system prompt (in case of variables) and user message
            string systemPrompt = PromptCatalog.Substitute(systemPromptTemplate, values);
            string userMessage = PromptCatalog.Substitute(userTemplate, values);

            double temp = _options.Temperature != GeneratorDefaultConfigs.Backstory.Temperature
                ? _options.Temperature
                : entry.Temperature!.Value;
            int maxTok = _options.MaxTokens != GeneratorDefaultConfigs.Backstory.MaxTokens
                ? _options.MaxTokens
                : entry.MaxTokens!.Value;

            var responseJson = await LlmOptionalTextGeneration.SendRequiredAsync(
                "backstory",
                _transport,
                systemPrompt,
                userMessage,
                temp,
                maxTok,
                "backstory",
                _options.OnDiagnostic,
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
