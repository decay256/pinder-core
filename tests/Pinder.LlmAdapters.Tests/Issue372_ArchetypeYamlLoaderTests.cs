using Pinder.Core.Characters;
using Pinder.LlmAdapters;
using Xunit;

namespace Pinder.LlmAdapters.Tests
{
    /// <summary>
    /// #372 \u2014 ArchetypeYamlLoader must parse the canonical
    /// <c>archetypes-enriched.yaml</c> structure and register every block of
    /// <c>type: archetype_definition</c> into <see cref="ArchetypeCatalog"/>.
    /// </summary>
    [Trait("Category", "LlmAdapters")]
    public class Issue372_ArchetypeYamlLoaderTests
    {
        private const string SampleYaml = @"- id: header
  type: definition
  title: Header
- id: archetype.fake-troll-9999
  type: archetype_definition
  title: The Fake Troll 9999
  behavior: |
    Loves wordplay. Cannot let a sentence land without a pun retrofitted.
    Self-applauding.
- id: archetype.empty-9999
  type: archetype_definition
  title: The Empty 9999
  behavior: ''
- id: not-an-archetype-9999
  type: definition
  title: Other
";

        [Fact]
        public void LoadFromYaml_RegistersBehaviorForEachArchetypeDefinition()
        {
            var result = ArchetypeYamlLoader.LoadFromYaml(SampleYaml);

            // Sanity: parsed without error
            Assert.Null(result.Error);
            Assert.Equal(1, result.Registered);
            Assert.Single(result.SkippedMissingBehavior);
            Assert.Equal("The Empty 9999", result.SkippedMissingBehavior[0]);

            // The behaviour must now be retrievable from the catalog.
            string behavior = ArchetypeCatalog.GetBehavior("The Fake Troll 9999");
            Assert.Contains("Loves wordplay", behavior);

            // The non-archetype block must NOT have been registered.
            string headerBehavior = ArchetypeCatalog.GetBehavior("Header");
            Assert.Contains("behavioral pattern", headerBehavior); // bare placeholder
        }

        [Fact]
        public void LoadFromYaml_EmptyInput_ReturnsErrorAndDoesNotMutateCatalog()
        {
            var result = ArchetypeYamlLoader.LoadFromYaml("");
            Assert.NotNull(result.Error);
            Assert.Equal(0, result.Registered);
        }

        [Fact]
        public void LoadFromYaml_MalformedYaml_ReturnsErrorWithoutThrowing()
        {
            var result = ArchetypeYamlLoader.LoadFromYaml("- id: incomplete\n  type: archetype_definition\n  title: [this is wrong\n  behavior: x");
            Assert.NotNull(result.Error);
            Assert.Equal(0, result.Registered);
        }
    }
}
