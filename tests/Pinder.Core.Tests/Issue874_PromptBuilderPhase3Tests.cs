using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Pinder.Core.Characters;
using Pinder.Core.Prompts;
using Pinder.Core.Stats;
using Pinder.Core.Traps;
using Pinder.LlmAdapters;
using Xunit;

namespace Pinder.Core.Tests
{
    /// <summary>
    /// Issue #874 Phase 3 + #875 Phase 5: <see cref="PromptBuilder"/>
    /// structural strings sourced exclusively from
    /// <c>data/prompts/structural.yaml</c>.
    ///
    /// What this file pins (updated after Phase 5):
    /// - The loader parses <c>data/prompts/structural.yaml</c> into a
    ///   <see cref="PromptCatalog"/> with 7 expected entries.
    /// - <see cref="PromptBuilder.StructuralFragmentLookup"/> MUST be wired
    ///   before calling <see cref="PromptBuilder.BuildSystemPrompt"/> —
    ///   a null or missing-key lookup throws <see cref="InvalidOperationException"/>.
    /// - When the lookup is wired, <see cref="PromptBuilder.BuildSystemPrompt"/>
    ///   emits the yaml-sourced section headers into the assembled prompt.
    /// </summary>
    [Trait("Category", "PromptCatalog")]
    [Collection("StaticWiring")]
    public class Issue874_PromptBuilderPhase3Tests
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

        private static PromptCatalog LoadCatalog()
            => PromptCatalog.LoadFromDirectory(PromptsRoot);

        private static StatBlock EmptyStats
            => new StatBlock(
                new Dictionary<StatType, int>(),
                new Dictionary<ShadowStatType, int>());

        private static FragmentCollection EmptyFragments
            => new FragmentCollection(
                personalityFragments: new string[0],
                backstoryFragments: new string[0],
                textingStyleFragments: new string[0],
                rankedArchetypes: new (string, int)[0],
                timing: null,
                stats: EmptyStats);

        // ----- loader: entry count -------------------------------------------

        [Fact]
        public void StructuralYaml_LoadsAll7Entries()
        {
            var catalog = LoadCatalog();

            // The 7 structural entries must be present.
            var names = catalog.Names.ToList();
            Assert.Contains("structural-lead-in", names);
            Assert.Contains("structural-identity", names);
            Assert.Contains("structural-personality", names);
            Assert.Contains("structural-backstory", names);
            Assert.Contains("structural-texting-style", names);
            Assert.Contains("structural-active-archetype", names);
            Assert.Contains("structural-active-trap-instructions", names);
        }

        // ----- byte-identity contract: representative entry ------------------

        [Fact]
        public void Yaml_StructuralIdentity_MatchesConst_ByteForByte()
        {
            // #874 Phase 3 contract: this is a pure relocation. The yaml
            // must produce the same text the legacy const does. If a
            // future PR renames the section header, it MUST update both
            // the yaml AND the const (or, post-Phase 5, just the yaml).
            var catalog = LoadCatalog();
            var entry = catalog.Get("structural-identity");

            string fromYaml = entry.SystemPrompt!;
            const string fromConst = "IDENTITY";

            Assert.Equal(fromConst, fromYaml);
        }

        [Fact]
        public void Yaml_StructuralLeadIn_MatchesConst_ByteForByte()
        {
            var catalog = LoadCatalog();
            var entry = catalog.Get("structural-lead-in");

            string fromYaml = entry.SystemPrompt!;
            const string fromConst = "RULES";

            Assert.Equal(fromConst, fromYaml);
        }

        // ----- StructuralFragmentLookup: must be wired (Phase 5) ------------

        [Fact]
        public void BuildSystemPrompt_Throws_WhenLookupIsNull()
        {
            var prior = PromptBuilder.StructuralFragmentLookup;
            PromptBuilder.StructuralFragmentLookup = null;
            try
            {
                Assert.Throws<InvalidOperationException>(() =>
                    PromptBuilder.BuildSystemPrompt(
                        "TestChar", "they/them", null, EmptyFragments, new TrapState()));
            }
            finally
            {
                PromptBuilder.StructuralFragmentLookup = prior;
            }
        }

        // ----- StructuralFragmentLookup: catalog-sourced ---------------------

        [Fact]
        public void BuildSystemPrompt_EmitsYamlHeader_WhenLookupIsSet()
        {
            var prior = PromptBuilder.StructuralFragmentLookup;
            var catalog = LoadCatalog();
            PromptBuilder.StructuralFragmentLookup = key => catalog.TryGet(key)?.SystemPrompt;

            try
            {
                string prompt = PromptBuilder.BuildSystemPrompt(
                    "TestChar", "they/them", null, EmptyFragments, new TrapState());

                // Headers from yaml must appear.
                Assert.Contains("IDENTITY", prompt);
                Assert.Contains("PERSONALITY", prompt);
                Assert.Contains("BACKSTORY", prompt);
                Assert.Contains("TEXTING STYLE", prompt);
                Assert.Contains("ACTIVE ARCHETYPE", prompt);

                // Lead-in is now the RULES token (structural.yaml structural-lead-in).
                Assert.Contains("RULES", prompt);
            }
            finally
            {
                PromptBuilder.StructuralFragmentLookup = prior;
            }
        }

        [Fact]
        public void BuildSystemPrompt_Throws_WhenKeyNotFound()
        {
            var prior = PromptBuilder.StructuralFragmentLookup;
            // Simulate a catalog that doesn't have the structural keys.
            PromptBuilder.StructuralFragmentLookup = _ => null;

            try
            {
                Assert.Throws<InvalidOperationException>(() =>
                    PromptBuilder.BuildSystemPrompt(
                        "TestChar", "they/them", null, EmptyFragments, new TrapState()));
            }
            finally
            {
                PromptBuilder.StructuralFragmentLookup = prior;
            }
        }
    }
}
