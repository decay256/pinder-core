using System.Runtime.CompilerServices;
using Pinder.Core.TestCommon;

namespace Pinder.LlmAdapters.Tests
{
    /// <summary>
    /// Wires the prompt catalog once at assembly load, before any test runs.
    /// Uses the shared PromptCatalogInitializer.
    /// </summary>
    internal static class LlmAdaptersTestWiring
    {
        [ModuleInitializer]
        internal static void Initialize()
        {
            PromptCatalogInitializer.Initialize();
        }
    }
}
