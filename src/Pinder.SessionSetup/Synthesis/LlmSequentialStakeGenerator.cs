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
        private const int MaxAttempts = 3;

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
            => await GenerateFromBackstoryAndPersonalityAsync(characterName, genderIdentity, bio,
                backstory, string.Empty, cancellationToken).ConfigureAwait(false);

        public async Task<List<string>> GenerateFromBackstoryAndPersonalityAsync(
            string characterName,
            string genderIdentity,
            string bio,
            Dictionary<string, BackstoryFact> backstory,
            string consolidatedPersonality,
            CancellationToken cancellationToken = default)
        {
            var profile = BuildProfile(backstory, consolidatedPersonality);
            var recovery = await SemanticOutputRecoveryExecutor.ExecuteAsync<List<string>, StakeRejection>(
                MaxAttempts,
                async (attempt, attemptCancellationToken) =>
                {
                    var stakeGenerator = new LlmStakeGenerator(
                        _transport,
                        streamingTransport: null,
                        options: null,
                        catalog: _catalog);
                    string llmResponse = await stakeGenerator.GenerateAsync(
                        characterName,
                        profile,
                        attemptCancellationToken).ConfigureAwait(false);

                    try
                    {
                        var list = LlmStakeGenerator.ParseCanonicalStakeBullets(llmResponse);
                        if (list.Count != 15)
                        {
                            throw new FormatException(
                                $"Expected exactly 15 psychological stake items, got {list.Count}.");
                        }
                        return SemanticOutputRecoveryAttemptResult<List<string>, StakeRejection>.Accepted(list);
                    }
                    catch (FormatException ex)
                    {
                        return SemanticOutputRecoveryAttemptResult<List<string>, StakeRejection>.Rejected(
                            new StakeRejection(llmResponse, ex));
                    }
                },
                cancellationToken: cancellationToken).ConfigureAwait(false);

            if (recovery.IsAccepted)
            {
                return recovery.AcceptedValue;
            }

            var finalRejection = recovery.Exhaustion.FinalRejection;
            try
            {
                throw finalRejection.Failure;
            }
            catch (FormatException ex)
            {
                // Fail-loud by propagating the failure with structural context
                throw new System.InvalidOperationException(
                    LlmDiagnosticFormatter.GeneratedTextFailure(
                        "Failed to parse canonical 15-item stake bullet list from LLM response.",
                        LlmPhase.Synthesis,
                        finalRejection.GeneratedText),
                    ex);
            }
        }

        private static string BuildProfile(Dictionary<string, BackstoryFact> backstory, string consolidatedPersonality)
        {
            var sb = new StringBuilder();
            sb.AppendLine("BACKSTORY JSON:");
            sb.Append(JsonSerializer.Serialize(backstory));
            sb.AppendLine();
            sb.AppendLine("CONSOLIDATED PERSONALITY:");
            sb.Append(consolidatedPersonality);
            return sb.ToString();
        }

        private sealed class StakeRejection
        {
            public StakeRejection(string generatedText, FormatException failure)
            {
                GeneratedText = generatedText;
                Failure = failure;
            }

            public string GeneratedText { get; }

            public FormatException Failure { get; }
        }
    }
}
