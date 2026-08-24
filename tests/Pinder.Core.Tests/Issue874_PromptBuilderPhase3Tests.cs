using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Pinder.Core.Characters;
using Pinder.Core.Prompts;
using Pinder.Core.Stats;
using Pinder.Core.TestCommon;
using Pinder.Core.Traps;
using Pinder.LlmAdapters;
using Xunit;

namespace Pinder.Core.Tests
{
    /// <summary>
    /// Issue #874 Phase 3 + #875 Phase 5 (updated by #1154): <see cref="PromptBuilder"/>
    /// structural strings sourced exclusively from
    /// <c>data/prompts/structural.yaml</c>.
    ///
    /// What this file pins (updated after #1154):
    /// - The loader parses <c>data/prompts/structural.yaml</c> into a
    ///   <see cref="PromptCatalog"/> with the single collapsed
    ///   <c>character_card_framing</c> entry (was 7 <c>structural-*</c> keys).
    /// - <see cref="PromptBuilder.StructuralFragmentLookup"/> MUST be wired
    ///   before calling <see cref="PromptBuilder.BuildSystemPrompt"/> —
    ///   a null or missing-key lookup throws <see cref="InvalidOperationException"/>.
    /// - When the lookup is wired, <see cref="PromptBuilder.BuildSystemPrompt"/>
    ///   splits the collapsed framing back into the yaml-sourced section
    ///   headers and emits them into the assembled prompt.
    /// </summary>
    [Trait("Category", "PromptCatalog")]
    [Collection("StaticWiring")]
    public class Issue874_PromptBuilderPhase3Tests
    {
        private static string PromptsRoot
            => TestRepoLocator.FindRepoSubdir("data", "prompts");

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
        public void StructuralYaml_LoadsCollapsedFramingEntry()
        {
            var catalog = LoadCatalog();

            // #1154: the 7 structural-* keys collapsed into one field.
            var names = catalog.Names.ToList();
            Assert.Contains("character_card_framing", names);
            Assert.Contains("texting_style_soft_framing", names);
            Assert.Contains("texting_style_runtime_framing", names);

            // The old per-section keys are gone.
            Assert.DoesNotContain("structural-lead-in", names);
            Assert.DoesNotContain("structural-identity", names);
            Assert.DoesNotContain("structural-personality", names);
            Assert.DoesNotContain("structural-backstory", names);
            Assert.DoesNotContain("structural-texting-style", names);
            Assert.DoesNotContain("structural-active-archetype", names);
            Assert.DoesNotContain("structural-active-trap-instructions", names);
        }

        // ----- byte-identity contract: collapsed framing carries the 7 labels --

        [Fact]
        public void Yaml_CharacterCardFraming_CarriesSevenLabelsInOrder()
        {
            // #1154 contract: the collapsed field is the 7 section labels,
            // one per line, in emission order. Splitting it back must
            // recover the exact legacy label strings byte-for-byte.
            var catalog = LoadCatalog();
            var entry = catalog.Get("character_card_framing");

            var labels = entry.SystemPrompt!
                .Replace("\r\n", "\n")
                .TrimEnd('\n')
                .Split('\n');

            Assert.Equal(
                new[]
                {
                    "RULES",
                    "IDENTITY",
                    "PERSONALITY",
                    "BACKSTORY",
                    "TEXTING STYLE",
                    "ACTIVE ARCHETYPE",
                    "ACTIVE TRAP INSTRUCTIONS",
                },
                labels);
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
        public void BuildSystemPrompt_EmitsConfiguredHeaders_WhenLookupIsSet()
        {
            var prior = PromptBuilder.StructuralFragmentLookup;
            var catalog = LoadCatalog();
            PromptBuilder.StructuralFragmentLookup = key => catalog.TryGet(key)?.SystemPrompt;

            try
            {
                string prompt = PromptBuilder.BuildSystemPrompt(
                    "TestChar", "they/them", null, EmptyFragments, new TrapState(),
                    archetypesEnabled: true);

                // Headers from yaml must appear.
                Assert.Contains("IDENTITY", prompt);
                Assert.Contains("PERSONALITY", prompt);
                Assert.Contains("BACKSTORY", prompt);
                Assert.Contains("TEXTING STYLE", prompt);
                Assert.Contains("ACTIVE ARCHETYPE", prompt);

                Assert.StartsWith("=== CHARACTER DATA ===", prompt);
            }
            finally
            {
                PromptBuilder.StructuralFragmentLookup = prior;
            }
        }

        [Fact]
        public void SoftTextingStyleFraming_IsCatalogBackedAndDoesNotChangeSectionOrder()
        {
            var catalog = LoadCatalog();
            string prompt = PromptBuilder.BuildSystemPrompt(
                "TestChar",
                "they/them",
                null,
                new FragmentCollection(
                    personalityFragments: Array.Empty<string>(),
                    backstoryFragments: Array.Empty<string>(),
                    textingStyleFragments: Array.Empty<string>(),
                    rankedArchetypes: Array.Empty<(string, int)>(),
                    timing: null,
                    stats: EmptyStats,
                    textingStyleSources: new[]
                    {
                        new TextingStyleFragmentSource(
                            "item",
                            "shoes",
                            "SYNTAX:\n- emoji: uses one tiny signal only when it fits\n",
                            slotOrParameter: "shoes"),
                    }),
                new TrapState(),
                archetypesEnabled: true,
                structuralFragmentLookup: key => catalog.TryGet(key)?.SystemPrompt);

            int identity = prompt.IndexOf("IDENTITY", StringComparison.Ordinal);
            int personality = prompt.IndexOf("PERSONALITY", StringComparison.Ordinal);
            int backstory = prompt.IndexOf("BACKSTORY", StringComparison.Ordinal);
            int textingStyle = prompt.IndexOf("TEXTING STYLE", StringComparison.Ordinal);
            int archetype = prompt.IndexOf("ACTIVE ARCHETYPE", StringComparison.Ordinal);
            Assert.True(identity < personality);
            Assert.True(personality < backstory);
            Assert.True(backstory < textingStyle);
            Assert.True(textingStyle < archetype);

            string section = prompt.Substring(textingStyle, archetype - textingStyle);
            Assert.Contains(catalog.Get("texting_style_soft_framing").SystemPrompt!, section);
            Assert.Contains("- emoji: uses one tiny signal only when it fits", section);
            Assert.DoesNotContain("follow this exactly", section, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void RuntimeTextingStyleFraming_OwnsHeadingAndSoftInstructionInOneCatalogFragment()
        {
            var catalog = LoadCatalog();
            string configured = catalog.Get("texting_style_runtime_framing").SystemPrompt!;
            string resolved = PromptBuilder.GetTextingStyleRuntimeFraming(
                key => catalog.TryGet(key)?.SystemPrompt);

            Assert.Equal(configured, resolved);
            Assert.StartsWith("YOUR TEXTING STYLE\n", resolved, StringComparison.Ordinal);
            Assert.Contains("loose expressive influences", resolved);
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
