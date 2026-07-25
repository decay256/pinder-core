using System;
using System.IO;
using Pinder.LlmAdapters;

namespace Pinder.SessionSetup
{
    /// <summary>
    /// Bootstraps the prompt yaml infrastructure for compatibility callers.
    /// Game hosts that need operation-stable configuration should capture the
    /// side-effect-free <see cref="PromptRuntimeBinding"/> returned by
    /// <see cref="BuildBinding"/>.
    /// </summary>
    public static class PromptWiring
    {
        /// <summary>
        /// One-shot compatibility publication of the unified PromptCatalog and
        /// legacy static delegates. Safe to call more than once; subsequent
        /// calls are no-ops if the static catalog is already assembled.
        /// </summary>
        public static void Wire(string promptsRoot, TextWriter? errorSink = null)
        {
            if (promptsRoot is null)
                throw new ArgumentNullException(nameof(promptsRoot));

            if (PromptTemplates.Catalog != null)
                return;

            BuildBinding(promptsRoot, errorSink).PublishCompatibilityStatics(errorSink);
        }

        /// <summary>
        /// Build an immutable runtime binding from the existing yaml loaders
        /// without mutating PromptTemplates, PromptBuilder, ArchetypeCatalog, or
        /// TextingStyleAggregator globals.
        /// </summary>
        public static PromptRuntimeBinding BuildBinding(string promptsRoot, TextWriter? errorSink = null)
            => PromptRuntimeBinding.Build(promptsRoot, errorSink);
    }
}
