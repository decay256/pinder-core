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
        private readonly PromptCatalog? _catalog;

        private const string DefaultSystemPrompt = 
            "You are a backstory generator for a character. " +
            "You must generate exactly 20 categories of backstory facts. " +
            "Here are the 20 exact category keys you must use: " +
            "age_and_demographics, birthplace_and_origin, childhood_milieu, parental_dynamics, early_education_scars, " +
            "higher_education, formative_intimacies, career_debut, current_profession, financial_hygiene, " +
            "domestic_milieu, social_circle, recent_ex, career_low, delusional_plan, " +
            "hyperfixations, ideological_posture, digital_footprint, physical_dysmorphia, dependencies. " +
            "For each category, provide a 'BioLie' and a 'TragicReality'. " +
            "Return ONLY valid JSON where keys are the 20 category names and values are objects containing 'BioLie' and 'TragicReality' strings.";

        private const string DefaultUserTemplate = 
            "Character: {characterName}\n" +
            "Gender: {genderIdentity}\n" +
            "Bio: {bio}\n" +
            "Fragments:\n{fragments}";

        public LlmBackstoryGenerator(ILlmTransport transport, Options? options = null, PromptCatalog? catalog = null)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _options = options ?? new Options();
            _catalog = catalog;
        }

        public async Task<Dictionary<string, BackstoryFact>> GenerateAsync(
            string characterName,
            string genderIdentity,
            string bio,
            IReadOnlyList<string> backstoryFragments,
            CancellationToken cancellationToken = default)
        {
            string systemPrompt = DefaultSystemPrompt;
            string userTemplate = DefaultUserTemplate;

            var entry = _catalog?.TryGet("backstory");
            if (entry != null)
            {
                if (entry.SystemPrompt != null) systemPrompt = entry.SystemPrompt;
                if (entry.UserTemplate != null) userTemplate = entry.UserTemplate;
            }

            string fragments = string.Join("\n", backstoryFragments);

            var values = new Dictionary<string, string>
            {
                { "characterName", characterName },
                { "genderIdentity", genderIdentity },
                { "bio", bio },
                { "fragments", fragments }
            };

            // Using PromptCatalog.Substitute if possible, else manual fallback for default template
            string userMessage = _catalog != null && entry != null 
                ? PromptCatalog.Substitute(userTemplate, values)
                : userTemplate
                    .Replace("{characterName}", characterName)
                    .Replace("{genderIdentity}", genderIdentity)
                    .Replace("{bio}", bio)
                    .Replace("{fragments}", fragments);

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