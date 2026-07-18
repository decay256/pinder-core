using System;
using System.Linq;
using Pinder.Core.Characters;
using Pinder.Core.TestCommon;
using Pinder.LlmAdapters;
using Xunit;

namespace Pinder.Core.Tests
{
    /// <summary>
    /// Issue #873 Phase 4 + #875 Phase 5: archetype behaviors sourced
    /// exclusively from <c>data/prompts/archetypes.yaml</c>.
    ///
    /// What this file pins:
    /// - The loader parses <c>data/prompts/archetypes.yaml</c> via a
    ///   <see cref="PromptCatalog"/> and detects all 20 archetype entries.
    /// - <see cref="ArchetypeCatalog.BehaviorResolver"/> delegate is
    ///   wired via <see cref="ArchetypeYamlLoader.LoadFromPromptCatalog"/>.
    /// - After Phase 5 of #871, there are no embedded const fallbacks —
    ///   <see cref="ArchetypeCatalog.GetBehavior"/> throws when neither
    ///   the resolver nor the in-memory dictionary can satisfy a lookup.
    /// </summary>
    [Trait("Category", "PromptCatalog")]
    [Collection("StaticWiring")]
    public class Issue873_ArchetypeCatalogPhase4Tests
    {
        private static string PromptsRoot
            => TestRepoLocator.FindRepoSubdir("data", "prompts");

        // ----- loader: entry count -------------------------------------------

        [Fact]
        public void ArchetypesYaml_LoadsAll20Behaviors()
        {
            var catalog = PromptCatalog.LoadFromDirectory(PromptsRoot);

            // Verify the archetypes.yaml entries are present.
            var names = catalog.Names.ToList();

            // All 20 archetype names must be present in the catalog.
            Assert.Contains("The Hey Opener", names);
            Assert.Contains("The DTF Opener", names);
            Assert.Contains("The One-Word Replier", names);
            Assert.Contains("The Wall of Text", names);
            Assert.Contains("The Copy-Paste Machine", names);
            Assert.Contains("The Pickup Line Spammer", names);
            Assert.Contains("The Exploding Nice Guy", names);
            Assert.Contains("The Oversharer", names);
            Assert.Contains("The Philosopher", names);
            Assert.Contains("The Instagram Recruiter", names);
            Assert.Contains("The Bot / Scammer", names);
            Assert.Contains("The Zombie", names);
            Assert.Contains("The Breadcrumber", names);
            Assert.Contains("The Love Bomber", names);
            Assert.Contains("The Peacock", names);
            Assert.Contains("The Slow Fader", names);
            Assert.Contains("The Ghost", names);
            Assert.Contains("The Player", names);
            Assert.Contains("The Sniper", names);
            Assert.Contains("The Bio Responder", names);
        }

        // ----- BehaviorResolver delegate (assembly-boundary crossing) --------

        [Fact]
        public void BehaviorResolver_ReturnsYamlSourcedContent()
        {
            var prior = ArchetypeCatalog.BehaviorResolver;
            try
            {
                var catalog = PromptCatalog.LoadFromDirectory(PromptsRoot);
                ArchetypeYamlLoader.LoadFromPromptCatalog(catalog);

                // After loading, the resolver should be wired.
                Assert.NotNull(ArchetypeCatalog.BehaviorResolver);

                // The resolver delegate should return the yaml-sourced content.
                string? resolved = ArchetypeCatalog.BehaviorResolver("The Ghost");
                Assert.NotNull(resolved);
                Assert.Contains("disappears without explanation", resolved);

                // Unknown archetype names should return null from the resolver
                // (after Phase 5, GetBehavior throws when no result is found).
                Assert.Null(ArchetypeCatalog.BehaviorResolver("NonexistentArchetype"));
            }
            finally
            {
                ArchetypeCatalog.BehaviorResolver = prior;
            }
        }
    }
}
