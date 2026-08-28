using Xunit;

namespace Pinder.LlmAdapters.Tests
{
    /// <summary>
    /// Serializes tests that mutate process-global prompt wiring.
    /// </summary>
    [CollectionDefinition(Name, DisableParallelization = true)]
    public sealed class StaticWiringCollection
    {
        public const string Name = "StaticWiring";

        private StaticWiringCollection()
        {
        }
    }
}
