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
    public sealed class LlmBioGenerator : IBioGenerator
    {
        private readonly ILlmTransport _transport;
        private readonly PromptCatalog _catalog;

        public LlmBioGenerator(ILlmTransport transport, PromptCatalog catalog)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _catalog.RequireCompleteEntry(
                "bio",
                "prompt-catalog: missing required key 'bio'. The yaml file is incomplete or missing.");
        }

        public async Task<string> GenerateAsync(
            string characterName,
            string genderIdentity,
            Dictionary<string, BackstoryFact> backstory,
            List<string> stakeLines,
            Dictionary<string, string> diagnosis,
            CancellationToken cancellationToken = default)
        {
            var entry = _catalog.Get("bio");
            var userPrompt = PromptCatalog.Substitute(entry.UserTemplate!, new Dictionary<string, string>
            {
                { "characterName", characterName },
                { "genderIdentity", genderIdentity },
                { "backstory", JsonSerializer.Serialize(backstory) },
                { "stakes", JsonSerializer.Serialize(stakeLines) },
                { "diagnosis", JsonSerializer.Serialize(diagnosis) }
            });

            var llmResponse = await _transport.SendAsync(
                entry.SystemPrompt!,
                userPrompt,
                entry.Temperature!.Value,
                entry.MaxTokens!.Value,
                LlmPhase.Synthesis,
                cancellationToken);

            var bio = ExtractBio(llmResponse);
            if (string.IsNullOrWhiteSpace(bio))
            {
                throw new InvalidOperationException(
                    LlmDiagnosticFormatter.GeneratedTextFailure(
                        "Bio generation returned empty output.",
                        LlmPhase.Synthesis,
                        llmResponse));
            }

            return bio.Trim();
        }

        private static string ExtractBio(string llmResponse)
        {
            if (string.IsNullOrWhiteSpace(llmResponse)) return string.Empty;
            var trimmed = llmResponse.Trim();

            try
            {
                using var doc = JsonDocument.Parse(trimmed);
                if (doc.RootElement.ValueKind == JsonValueKind.Object
                    && doc.RootElement.TryGetProperty("bio", out var bio)
                    && bio.ValueKind == JsonValueKind.String)
                {
                    return bio.GetString() ?? string.Empty;
                }
            }
            catch (JsonException)
            {
                // Plain prose is also accepted by the prompt contract.
            }

            if (trimmed.StartsWith("\"", StringComparison.Ordinal)
                && trimmed.EndsWith("\"", StringComparison.Ordinal))
            {
                try
                {
                    return JsonSerializer.Deserialize<string>(trimmed) ?? string.Empty;
                }
                catch (JsonException)
                {
                    return trimmed.Trim('"');
                }
            }

            return trimmed;
        }
    }
}
