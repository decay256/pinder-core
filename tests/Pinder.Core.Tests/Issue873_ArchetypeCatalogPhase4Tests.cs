using System;
using System.IO;
using System.Linq;
using Pinder.Core.Characters;
using Pinder.LlmAdapters;
using Xunit;

namespace Pinder.Core.Tests
{
    /// <summary>
    /// Issue #873 Phase 4: <see cref="ArchetypeCatalog._behaviors"/> const
    /// strings migrated to <c>data/prompts/archetypes.yaml</c>.
    ///
    /// What this file pins:
    /// - The loader parses <c>data/prompts/archetypes.yaml</c> via a
    ///   <see cref="PromptCatalog"/> and registers all 20 archetype behaviors.
    /// - <see cref="ArchetypeCatalog.GetBehavior"/> returns the yaml-sourced
    ///   content byte-for-byte identical to the const fallback.
    /// - The <see cref="ArchetypeCatalog.BehaviorResolver"/> delegate is
    ///   wired so <see cref="ArchetypeCatalog.GetBehavior"/> prefers the
    ///   yaml catalog (assembly-boundary crossing via delegate — Phase 3
    ///   pattern, since <c>ArchetypeCatalog</c> lives in <c>Pinder.Core</c>
    ///   and cannot reference <c>PromptCatalog</c> in <c>Pinder.LlmAdapters</c>).
    ///
    /// Consolidation note: the existing <see cref="ArchetypeYamlLoader.LoadFromYaml"/>
    /// parses <c>archetypes-enriched.yaml</c> (a sequence-based format);
    /// <see cref="ArchetypeYamlLoader.LoadFromPromptCatalog"/> is the new
    /// entrypoint for the consolidated <c>PromptCatalog</c>-format yaml under
    /// <c>data/prompts/</c>. Both paths call <see cref="ArchetypeCatalog.RegisterBehavior"/>
    /// which overwrites the static-initialiser entries. No duplicate path remains.
    /// </summary>
    [Trait("Category", "PromptCatalog")]
    public class Issue873_ArchetypeCatalogPhase4Tests
    {
        // ----- repo helpers ---------------------------------------------------

        private static string FindRepoSubdir(string subdir)
        {
            var dir = AppDomain.CurrentDomain.BaseDirectory;
            for (int i = 0; i < 10; i++)
            {
                var candidate = Path.Combine(dir, subdir);
                if (Directory.Exists(candidate)) return candidate;
                var parent = Path.GetDirectoryName(dir);
                if (parent == null || parent == dir) break;
                dir = parent;
            }
            throw new DirectoryNotFoundException(
                $"Could not locate {subdir} in any ancestor of the test binary.");
        }

        private static string PromptsRoot
            => FindRepoSubdir(Path.Combine("data", "prompts"));

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

        // ----- byte-identity contract: representative entry ------------------

        [Fact]
        public void ArchetypeCatalog_GetBehavior_LoadsFromYaml_ByteForByte()
        {
            // #873 Phase 4 contract: this is a pure relocation. The yaml
            // must produce the same text the legacy const does. If a
            // future PR tunes the behavior content, it MUST update both
            // the yaml AND the const (or, post-Phase-5, just the yaml).

            // Save const version before loading.
            string constBehavior = ArchetypeCatalog.GetBehavior("The Hey Opener");
            Assert.NotNull(constBehavior);
            Assert.NotEmpty(constBehavior);

            // Load from yaml via PromptCatalog.
            var catalog = PromptCatalog.LoadFromDirectory(PromptsRoot);
            int registered = ArchetypeYamlLoader.LoadFromPromptCatalog(catalog);

            Assert.True(registered >= 20,
                $"expected >=20 registered behaviors, got {registered}");

            // Read back — should be byte-identical to const fallback.
            string fromYaml = ArchetypeCatalog.GetBehavior("The Hey Opener");

            // Exact byte-for-byte comparison — no whitespace normalisation.
            Assert.Equal(constBehavior, fromYaml);
        }

        // ----- BehaviorResolver delegate (assembly-boundary crossing) --------

        [Fact]
        public void BehaviorResolver_ReturnsYamlSourcedContent()
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
            // (so GetBehavior falls through to the placeholder).
            Assert.Null(ArchetypeCatalog.BehaviorResolver("NonexistentArchetype"));
        }
    }
}
