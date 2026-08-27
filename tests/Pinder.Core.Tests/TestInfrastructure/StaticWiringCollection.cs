using Xunit;

namespace Pinder.Core.Tests
{
    /// <summary>
    /// Serializes tests that mutate process-global prompt and character wiring.
    /// </summary>
    [CollectionDefinition("StaticWiring", DisableParallelization = true)]
    public sealed class StaticWiringCollection
    {
    }
}
