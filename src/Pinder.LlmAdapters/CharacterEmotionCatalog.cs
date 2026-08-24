using System;
using System.Collections.Generic;
using System.Linq;

namespace Pinder.LlmAdapters
{
    internal static class CharacterEmotionCatalog
    {
        public const string PromptKey = "character-emotional-primary-emotions";

        public static IReadOnlyList<string> Load(PromptCatalog catalog)
        {
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));

            PromptEntry? entry = catalog.TryGet(PromptKey);
            if (entry == null || string.IsNullOrWhiteSpace(entry.SystemPrompt))
            {
                throw new InvalidOperationException(
                    "prompt-catalog: missing required character emotion vocabulary '" + PromptKey + "'.");
            }

            string[] emotions = entry.SystemPrompt!
                .Split(new[] { ',', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(value => value.Trim())
                .Where(value => value.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (emotions.Length == 0)
                throw new InvalidOperationException(PromptKey + " must contain at least one emotion.");

            return emotions;
        }
    }
}
