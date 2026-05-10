using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Pinder.Core.Characters;
using Pinder.Core.Conversation;
using Pinder.Core.Data;
using Pinder.Core.Interfaces;
using Pinder.Core.Prompts;
using Pinder.Core.Stats;
using Pinder.Core.Traps;
using Pinder.SessionSetup;
using Xunit;

namespace Pinder.Core.Tests
{
    /// <summary>
    /// #836 placeholder texting-style aggregation:
    ///   - Anatomy texting-style fragments are excluded from the LLM-facing
    ///     join entirely (anatomy is silenced in the texting-style channel).
    ///   - Item texting-style fragments are sampled down to exactly 2 when
    ///     2+ items are equipped; with fewer items, all are kept.
    ///   - The pick is deterministic per (character id, configuration) so a
    ///     character does not get a fresh re-roll mid-conversation.
    ///   - Personality / backstory channels still receive anatomy
    ///     contributions (regression check).
    /// </summary>
    [Trait("Category", "Characters")]
    public class Issue836_TextingStylePlaceholderAggregationTests
    {
        // ----- repo helpers ---------------------------------------------------

        private static string LoadJson(string relativePath)
        {
            var knownRoots = new[]
            {
                "/root/.openclaw",
                "/home/openclaw/.openclaw",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".openclaw")
            };
            foreach (var root in knownRoots)
            {
                var candidate = Path.Combine(root, relativePath);
                if (File.Exists(candidate)) return File.ReadAllText(candidate);
            }
            var dir = AppDomain.CurrentDomain.BaseDirectory;
            for (int i = 0; i < 10; i++)
            {
                var candidate = Path.Combine(dir, relativePath);
                if (File.Exists(candidate)) return File.ReadAllText(candidate);
                var parent = Path.GetDirectoryName(dir);
                if (parent == null || parent == dir) break;
                dir = parent;
            }
            throw new FileNotFoundException($"Could not locate {relativePath}");
        }

        private static IItemRepository BuildItemRepo()
            => new JsonItemRepository(LoadJson("agents-extra/pinder/data/items/starter-items.json"));

        private static IAnatomyRepository BuildAnatomyRepo()
            => new JsonAnatomyRepository(LoadJson("agents-extra/pinder/data/anatomy/anatomy-parameters.json"));

        private static readonly IReadOnlyDictionary<StatType, int> ZeroBaseStats =
            new Dictionary<StatType, int>();
        private static readonly IReadOnlyDictionary<ShadowStatType, int> ZeroShadow =
            new Dictionary<ShadowStatType, int>();

        // Six items + two anatomy tiers — same kind of stack as a fully
        // built-out character. Item fragments here are all distinct enough
        // that a `" | "`-split count is unambiguous.
        private static readonly string[] SixItems =
        {
            "vintage-band-tee",
            "rubber-duck",
            "hiking-boots",
            "beanie-with-patches",
            "worn-paperback",
            "cargo-shorts",
        };

        private static readonly Dictionary<string, string> TwoAnatomyTiers =
            new Dictionary<string, string>
            {
                { "length", "short" },
                { "girth",  "slim"  },
            };

        // ----- direct aggregator tests ----------------------------------------

        [Fact]
        public void Aggregator_SixItems_PicksExactlyTwoFragments()
        {
            var assembler = new CharacterAssembler(BuildItemRepo(), BuildAnatomyRepo());
            var fragments = assembler.Assemble(
                SixItems, TwoAnatomyTiers, ZeroBaseStats, ZeroShadow);

            string joined = TextingStyleAggregator.Aggregate(
                fragments.TextingStyleSources, "seed-A");

            Assert.False(string.IsNullOrEmpty(joined));
            int parts = joined.Split(new[] { " | " }, StringSplitOptions.None).Length;
            Assert.Equal(2, parts);
        }

        [Fact]
        public void Aggregator_AnatomyFragmentsExcludedFromJoin()
        {
            var assembler = new CharacterAssembler(BuildItemRepo(), BuildAnatomyRepo());
            var fragments = assembler.Assemble(
                SixItems, TwoAnatomyTiers, ZeroBaseStats, ZeroShadow);

            // Pull the anatomy texting fragments straight from the source
            // breakdown (still present on FragmentCollection — only the
            // join is filtered).
            var anatomyFragments = fragments.TextingStyleSources
                .Where(s => s.Kind == "anatomy")
                .Select(s => s.Fragment)
                .ToList();

            // Sanity: the test data must actually contribute anatomy
            // texting fragments, otherwise the "exclusion" assertion below
            // is vacuous.
            Assert.NotEmpty(anatomyFragments);

            string joined = TextingStyleAggregator.Aggregate(
                fragments.TextingStyleSources, "seed-A");

            foreach (var anaFrag in anatomyFragments)
                Assert.DoesNotContain(anaFrag, joined);
        }

        [Fact]
        public void Aggregator_OneItem_KeepsThatOneFragment()
        {
            var assembler = new CharacterAssembler(BuildItemRepo(), BuildAnatomyRepo());
            var fragments = assembler.Assemble(
                new[] { "vintage-band-tee" }, TwoAnatomyTiers,
                ZeroBaseStats, ZeroShadow);

            string joined = TextingStyleAggregator.Aggregate(
                fragments.TextingStyleSources, "seed-A");

            // The single item fragment should be the whole output, with no
            // " | " separator at all.
            var itemFragment = fragments.TextingStyleSources
                .Single(s => s.Kind == "item").Fragment;
            Assert.Equal(itemFragment, joined);
        }

        [Fact]
        public void Aggregator_AnatomyOnly_NoItems_ReturnsEmpty()
        {
            var assembler = new CharacterAssembler(BuildItemRepo(), BuildAnatomyRepo());
            var fragments = assembler.Assemble(
                Array.Empty<string>(), TwoAnatomyTiers,
                ZeroBaseStats, ZeroShadow);

            string joined = TextingStyleAggregator.Aggregate(
                fragments.TextingStyleSources, "seed-A");

            Assert.Equal(string.Empty, joined);
        }

        [Fact]
        public void Aggregator_DeterministicForSameSeed()
        {
            var assembler = new CharacterAssembler(BuildItemRepo(), BuildAnatomyRepo());
            var fragments = assembler.Assemble(
                SixItems, TwoAnatomyTiers, ZeroBaseStats, ZeroShadow);

            string a = TextingStyleAggregator.Aggregate(
                fragments.TextingStyleSources, "char-uuid-1234");
            string b = TextingStyleAggregator.Aggregate(
                fragments.TextingStyleSources, "char-uuid-1234");
            string c = TextingStyleAggregator.Aggregate(
                fragments.TextingStyleSources, "char-uuid-1234");

            Assert.Equal(a, b);
            Assert.Equal(b, c);
        }

        [Fact]
        public void Aggregator_DifferentSeeds_CanProduceDifferentPicks()
        {
            // Not strictly required by the spec, but a useful sanity check
            // that the seed actually feeds into the RNG. With 6 items and
            // C(6,2)=15 possible pairs, two arbitrary seeds are very
            // unlikely to collide. We try a small set and assert at least
            // one pair differs — i.e. picks are not seed-invariant.
            var assembler = new CharacterAssembler(BuildItemRepo(), BuildAnatomyRepo());
            var fragments = assembler.Assemble(
                SixItems, TwoAnatomyTiers, ZeroBaseStats, ZeroShadow);

            var picks = new[] { "seed-A", "seed-B", "seed-C", "seed-D", "seed-E" }
                .Select(s => TextingStyleAggregator.Aggregate(
                    fragments.TextingStyleSources, s))
                .Distinct()
                .ToList();

            Assert.True(picks.Count >= 2,
                "Expected the aggregator to produce different picks for at least " +
                "two different seeds; got identical output across all seeds.");
        }

        [Fact]
        public void Aggregator_NullSeed_FallsBackToContentHash_StillDeterministic()
        {
            var assembler = new CharacterAssembler(BuildItemRepo(), BuildAnatomyRepo());
            var fragments = assembler.Assemble(
                SixItems, TwoAnatomyTiers, ZeroBaseStats, ZeroShadow);

            string a = TextingStyleAggregator.Aggregate(fragments.TextingStyleSources, null);
            string b = TextingStyleAggregator.Aggregate(fragments.TextingStyleSources, null);
            string c = TextingStyleAggregator.Aggregate(fragments.TextingStyleSources, "");

            Assert.Equal(a, b);
            Assert.Equal(a, c); // empty-string seed treated same as null
            Assert.False(string.IsNullOrEmpty(a));
        }

        [Fact]
        public void Aggregator_PickedFragmentsExistInSourceList()
        {
            // Whatever the aggregator returns must be a concatenation of
            // fragments that actually appeared on the item-source list —
            // the placeholder doesn't fabricate or splice strings.
            var assembler = new CharacterAssembler(BuildItemRepo(), BuildAnatomyRepo());
            var fragments = assembler.Assemble(
                SixItems, TwoAnatomyTiers, ZeroBaseStats, ZeroShadow);

            string joined = TextingStyleAggregator.Aggregate(
                fragments.TextingStyleSources, "seed-A");

            var parts = joined.Split(new[] { " | " }, StringSplitOptions.None);
            Assert.Equal(2, parts.Length);

            var itemFragmentSet = fragments.TextingStyleSources
                .Where(s => s.Kind == "item")
                .Select(s => s.Fragment)
                .ToHashSet();

            foreach (var part in parts)
                Assert.Contains(part, itemFragmentSet);
        }

        // ----- PromptBuilder integration --------------------------------------

        [Fact]
        public void PromptBuilder_TextingStyleSection_OnlyContainsItemFragments()
        {
            var assembler = new CharacterAssembler(BuildItemRepo(), BuildAnatomyRepo());
            var fragments = assembler.Assemble(
                SixItems, TwoAnatomyTiers, ZeroBaseStats, ZeroShadow);

            var prompt = PromptBuilder.BuildSystemPrompt(
                "TestChar", "she/her", "bio", fragments, new TrapState(),
                characterIdSeed: "stable-seed");

            // Slice out the TEXTING STYLE section so we don't accidentally
            // hit anatomy strings that legitimately appear in PERSONALITY
            // or BACKSTORY.
            // #832: ARCHETYPES (tendency-order ranked list) replaced by
            // ACTIVE ARCHETYPE; use the new header as the boundary.
            string section = ExtractSection(prompt, "TEXTING STYLE", "ACTIVE ARCHETYPE");

            // Anatomy texting-style fragments must NOT appear here.
            var anatomyTexting = fragments.TextingStyleSources
                .Where(s => s.Kind == "anatomy")
                .Select(s => s.Fragment)
                .ToList();
            Assert.NotEmpty(anatomyTexting); // prove the test data has anatomy contributions
            foreach (var anaFrag in anatomyTexting)
                Assert.DoesNotContain(anaFrag, section);

            // #833: section emitted as a bullet list (one fragment per
            // line, leading `- `) instead of `" | "`-joined prose. Count
            // the bullets to verify exactly 2 item fragments survive
            // the placeholder pick.
            var bulletLines = section.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim())
                .Where(l => l.StartsWith("- "))
                .ToList();
            Assert.Equal(2, bulletLines.Count);
        }

        [Fact]
        public void PromptBuilder_PersonalityAndBackstory_StillIncludeAnatomyContributions()
        {
            // Regression check: anatomy is silenced ONLY in the
            // texting-style channel. Its personality / backstory fragments
            // must still flow into the prompt unchanged.
            var assembler = new CharacterAssembler(BuildItemRepo(), BuildAnatomyRepo());
            var fragments = assembler.Assemble(
                SixItems, TwoAnatomyTiers, ZeroBaseStats, ZeroShadow);

            var prompt = PromptBuilder.BuildSystemPrompt(
                "TestChar", "she/her", "bio", fragments, new TrapState(),
                characterIdSeed: "stable-seed");

            string personalitySection = ExtractSection(prompt, "PERSONALITY", "BACKSTORY");
            string backstorySection   = ExtractSection(prompt, "BACKSTORY",   "TEXTING STYLE");

            // Pull the anatomy-tier definitions directly so we know what
            // their non-empty personality / backstory contributions look
            // like, then assert each one appears in the relevant section.
            var anatomyRepo = BuildAnatomyRepo();
            var anatomyTiers = TwoAnatomyTiers
                .Select(kv => anatomyRepo.GetParameter(kv.Key)?.GetTier(kv.Value))
                .Where(t => t != null)
                .ToList();
            Assert.NotEmpty(anatomyTiers);

            bool sawAnyAnatomyPersonality = false;
            bool sawAnyAnatomyBackstory   = false;
            foreach (var tier in anatomyTiers)
            {
                if (!string.IsNullOrEmpty(tier!.PersonalityFragment))
                {
                    Assert.Contains(tier.PersonalityFragment, personalitySection);
                    sawAnyAnatomyPersonality = true;
                }
                if (!string.IsNullOrEmpty(tier.BackstoryFragment))
                {
                    Assert.Contains(tier.BackstoryFragment, backstorySection);
                    sawAnyAnatomyBackstory = true;
                }
            }

            Assert.True(sawAnyAnatomyPersonality,
                "Test data did not include any anatomy personality fragment; cannot verify regression.");
            Assert.True(sawAnyAnatomyBackstory,
                "Test data did not include any anatomy backstory fragment; cannot verify regression.");
        }

        [Fact]
        public void PromptBuilder_Determinism_SameSeedProducesSamePrompt()
        {
            var assembler = new CharacterAssembler(BuildItemRepo(), BuildAnatomyRepo());
            var fragments = assembler.Assemble(
                SixItems, TwoAnatomyTiers, ZeroBaseStats, ZeroShadow);

            string p1 = PromptBuilder.BuildSystemPrompt(
                "TestChar", "she/her", "bio", fragments, new TrapState(),
                characterIdSeed: "char-uuid-1234");
            string p2 = PromptBuilder.BuildSystemPrompt(
                "TestChar", "she/her", "bio", fragments, new TrapState(),
                characterIdSeed: "char-uuid-1234");

            Assert.Equal(p1, p2);
        }

        // ----- CharacterDefinitionLoader integration --------------------------

        [Fact]
        public void Loader_AssemblesProfileWithFilteredTextingStyle()
        {
            // End-to-end: feed a v1 character JSON through the loader and
            // confirm the resulting CharacterProfile.TextingStyleFragment
            // contains exactly 2 item fragments and no anatomy ones.
            string json = BuildCharacterJson(
                characterId: "11111111-1111-4111-8111-111111111111",
                items: SixItems,
                anatomy: TwoAnatomyTiers);

            var profile = CharacterDefinitionLoader.Parse(json, BuildItemRepo(), BuildAnatomyRepo());

            Assert.False(string.IsNullOrEmpty(profile.TextingStyleFragment));
            Assert.Equal(2, profile.TextingStyleFragment
                .Split(new[] { " | " }, StringSplitOptions.None).Length);

            // Anatomy fragments still reachable on the per-source breakdown
            // (Character Sheet UI #404), but absent from the joined string.
            var anatomyFragments = profile.TextingStyleSources
                .Where(s => s.Kind == "anatomy")
                .Select(s => s.Fragment)
                .ToList();
            Assert.NotEmpty(anatomyFragments);
            foreach (var anaFrag in anatomyFragments)
                Assert.DoesNotContain(anaFrag, profile.TextingStyleFragment);
        }

        [Fact]
        public void Loader_SameCharacterId_LocksThePickAcrossLoads()
        {
            string json = BuildCharacterJson(
                characterId: "22222222-2222-4222-8222-222222222222",
                items: SixItems,
                anatomy: TwoAnatomyTiers);

            var p1 = CharacterDefinitionLoader.Parse(json, BuildItemRepo(), BuildAnatomyRepo());
            var p2 = CharacterDefinitionLoader.Parse(json, BuildItemRepo(), BuildAnatomyRepo());
            var p3 = CharacterDefinitionLoader.Parse(json, BuildItemRepo(), BuildAnatomyRepo());

            Assert.Equal(p1.TextingStyleFragment, p2.TextingStyleFragment);
            Assert.Equal(p2.TextingStyleFragment, p3.TextingStyleFragment);
            Assert.Equal(p1.AssembledSystemPrompt, p2.AssembledSystemPrompt);
        }

        [Fact]
        public void Loader_DifferentCharacterIds_CanYieldDifferentPicks()
        {
            // With 6 items, two arbitrary UUIDs are very unlikely to land
            // on the same pair. Asserting at least one differs is enough
            // to prove the seed flows through to the aggregator.
            var ids = new[]
            {
                "33333333-3333-4333-8333-333333333333",
                "44444444-4444-4444-8444-444444444444",
                "55555555-5555-4555-8555-555555555555",
                "66666666-6666-4666-8666-666666666666",
                "77777777-7777-4777-8777-777777777777",
            };

            var fragments = ids
                .Select(id => CharacterDefinitionLoader.Parse(
                    BuildCharacterJson(id, SixItems, TwoAnatomyTiers),
                    BuildItemRepo(), BuildAnatomyRepo())
                    .TextingStyleFragment)
                .Distinct()
                .ToList();

            Assert.True(fragments.Count >= 2,
                "Expected at least two distinct picks across five different character ids.");
        }

        // ----- helpers --------------------------------------------------------

        private static string ExtractSection(string prompt, string header, string nextHeader)
        {
            int start = prompt.IndexOf(header, StringComparison.Ordinal);
            Assert.True(start >= 0, $"Section '{header}' not found in prompt.");
            int afterHeader = start + header.Length;
            int end = prompt.IndexOf(nextHeader, afterHeader, StringComparison.Ordinal);
            if (end < 0) end = prompt.Length;
            return prompt.Substring(afterHeader, end - afterHeader);
        }

        private static string BuildCharacterJson(
            string characterId,
            IEnumerable<string> items,
            IReadOnlyDictionary<string, string> anatomy)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append('{');
            sb.Append("\"schema_version\":1,");
            sb.Append($"\"character_id\":\"{characterId}\",");
            sb.Append("\"name\":\"TestChar\",");
            sb.Append("\"gender_identity\":\"she/her\",");
            sb.Append("\"bio\":\"a bio\",");
            sb.Append("\"level\":3,");
            sb.Append("\"items\":[");
            sb.Append(string.Join(",", items.Select(i => $"\"{i}\"")));
            sb.Append("],");
            sb.Append("\"anatomy\":{");
            sb.Append(string.Join(",", anatomy.Select(kv => $"\"{kv.Key}\":\"{kv.Value}\"")));
            sb.Append("},");
            sb.Append("\"allocation\":{\"spent\":{},\"unspent_pool\":0,\"shadows\":{}}");
            sb.Append('}');
            return sb.ToString();
        }
    }
}
