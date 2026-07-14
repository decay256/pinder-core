using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Pinder.Core.Conversation;
using Pinder.Core.Interfaces;
using Pinder.LlmAdapters;

namespace Pinder.SessionSetup
{
    public sealed class LlmBackstoryConsolidator : IBackstoryConsolidator
    {
        private readonly ILlmTransport _transport;
        private readonly PromptCatalog _catalog;
        private readonly Action<OperationalDiagnosticEvent>? _onDiagnostic;

        public LlmBackstoryConsolidator(
            ILlmTransport transport,
            PromptCatalog catalog,
            Action<OperationalDiagnosticEvent>? onDiagnostic = null)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _onDiagnostic = onDiagnostic;
            _catalog.RequireCompleteEntry("backstory_consolidation", "prompt-catalog: missing required key 'backstory_consolidation'.");
        }

        public async Task<string> GenerateAsync(string characterName, string genderIdentity, string bio,
            string gameSystemPrompt, IReadOnlyList<string> backstoryFragments,
            IReadOnlyList<string> textingStyleSignals, string stats, CancellationToken cancellationToken = default)
        {
            var entry = _catalog.Get("backstory_consolidation");
            var userPrompt = PromptCatalog.Substitute(entry.UserTemplate!, new Dictionary<string, string>
            {
                { "characterName", characterName },
                { "genderIdentity", genderIdentity },
                { "bio", string.IsNullOrWhiteSpace(bio) ? "(none)" : bio },
                { "game_system_prompt", gameSystemPrompt },
                { "backstory_fragments", FormatList(backstoryFragments) },
                { "texting_style", FormatList(textingStyleSignals) },
                { "stats", stats }
            });
            var result = (await LlmOptionalTextGeneration.SendRequiredAsync(
                "backstory_consolidation",
                _transport,
                entry.SystemPrompt!,
                userPrompt,
                entry.Temperature!.Value,
                entry.MaxTokens!.Value,
                LlmPhase.Synthesis,
                _onDiagnostic,
                cancellationToken)
                .ConfigureAwait(false)).Trim();
            if (string.IsNullOrWhiteSpace(result))
                throw new InvalidOperationException("Backstory consolidation returned empty output.");
            return result;
        }

        private static string FormatList(IReadOnlyList<string> values)
        {
            var lines = new List<string>();
            if (values != null)
                foreach (var value in values)
                    if (!string.IsNullOrWhiteSpace(value)) lines.Add("- " + value.Trim());
            return lines.Count == 0 ? "- (none)" : string.Join("\n", lines);
        }
    }
}
