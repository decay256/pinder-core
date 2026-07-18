using System;
using System.Collections.Generic;
using System.Linq;
using Pinder.Core.Characters;
using Pinder.Core.Conversation;
using Pinder.Core.Prompts;
using Pinder.Core.Traps;
using Pinder.SessionSetup;
using Xunit;

namespace Pinder.Core.Tests
{
    public partial class Issue836_TextingStyleAggregationRuleTests
    {
        // ----- anatomy group voting ------------------------------------------

        [Fact]
        public void AnatomyGroup_MajorityVoteWins()
        {
            // Construct two anatomy entries from the stance group with the
            // same stance value, and one with a different stance value —
            // the majority (2) must win.
            // #1175: use new Unity param ids (trunkLengthBase, trunkGirth, trunkCurvature)
            var sources = new List<TextingStyleFragmentSource>
            {
                MakeAnatomyToneFragment("trunkLengthBase", "band0",
                    stance: "<dry-stance-line>", register: "x", pacing: "y"),
                MakeAnatomyToneFragment("trunkGirth", "band0",
                    stance: "<dry-stance-line>", register: "x", pacing: "y"),
                MakeAnatomyToneFragment("trunkCurvature", "band0",
                    stance: "<other-stance-line>", register: "x", pacing: "y"),
            };

            var lines = TextingStyleAggregator.AggregateAsList(sources, null);
            var stanceLine = lines.Single(l => l.StartsWith("stance:"));
            Assert.Equal("stance: <dry-stance-line>", stanceLine);
        }

        [Fact]
        public void AnatomyGroup_TieBreakByGroupOrder()
        {
            // trunkLengthBase and trunkGirth tie on stance — trunkLengthBase wins
            // (earliest in the StanceGroup order).
            var sources = new List<TextingStyleFragmentSource>
            {
                MakeAnatomyToneFragment("trunkLengthBase", "band0",
                    stance: "<line-from-trunkLengthBase>", register: "x", pacing: "y"),
                MakeAnatomyToneFragment("trunkGirth", "band0",
                    stance: "<line-from-trunkGirth>", register: "x", pacing: "y"),
            };

            var lines = TextingStyleAggregator.AggregateAsList(sources, null);
            var stanceLine = lines.Single(l => l.StartsWith("stance:"));
            Assert.Equal("stance: <line-from-trunkLengthBase>", stanceLine);
        }

        [Fact]
        public void AnatomyGroup_EmptyGroup_DropsAxis()
        {
            // Only stance-group entries — register and pacing axes must drop
            // because their groups have no contributors.
            var sources = new List<TextingStyleFragmentSource>
            {
                // Only stance-group entries.
                MakeAnatomyToneFragment("trunkLengthBase", "band0",
                    stance: "<a>", register: "<r>", pacing: "<p>"),
            };

            var lines = TextingStyleAggregator.AggregateAsList(sources, null);
            // stance must appear (trunkLengthBase's stance), register and pacing
            // must NOT appear because their groups have no contributors.
            Assert.Contains(lines, l => l.StartsWith("stance:"));
            Assert.DoesNotContain(lines, l => l.StartsWith("register:"));
            Assert.DoesNotContain(lines, l => l.StartsWith("pacing:"));
        }

        [Fact]
        public void AnatomyGroup_UngroupedParameter_DoesNotContribute()
        {
            // A texting-style fragment from a fictional anatomy parameter
            // not in any of the three groups must contribute nothing.
            var sources = new List<TextingStyleFragmentSource>
            {
                MakeAnatomyToneFragment("nonsense_param", "band-x",
                    stance: "<should-not-appear>",
                    register: "<should-not-appear>",
                    pacing: "<should-not-appear>"),
            };

            var lines = TextingStyleAggregator.AggregateAsList(sources, null);
            Assert.Empty(lines);
        }

        // ----- PromptBuilder integration --------------------------------------

        [Fact]
        public void PromptBuilder_EmitsNewAxisPrefixedBulletList()
        {
            var assembler = new CharacterAssembler(BuildItemRepo(), BuildAnatomyRepo());
            var fragments = assembler.Assemble(
                OneItemPerSlot, AnatomyStack, ZeroBaseStats, ZeroShadow);

            var prompt = PromptBuilder.BuildSystemPrompt(
                "TestChar", "she/her", "bio", fragments, new TrapState(),
                characterIdSeed: "stable-seed",
                archetypesEnabled: true);

            // Slice out the TEXTING STYLE section.
            string section = ExtractSection(prompt, "TEXTING STYLE", "ACTIVE ARCHETYPE");

            // Each emitted bullet must be axis-prefixed (e.g. "- emoji: ...").
            var bulletLines = section.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim())
                .Where(l => l.StartsWith("- "))
                .ToList();

            Assert.NotEmpty(bulletLines);
            foreach (var bullet in bulletLines)
            {
                var body = bullet.Substring(2);
                int colon = body.IndexOf(':');
                Assert.True(colon > 0, $"Bullet missing axis prefix: '{bullet}'");
                string axis = body.Substring(0, colon);
                Assert.Contains(axis, new[]
                {
                    "emoji", "shorthand", "grammar", "structure", "length", "tics",
                    "stance", "register", "pacing",
                });
            }

            // No bullet should be a raw item / anatomy fragment (those are
            // a multi-line block; bullets are single axis lines).
            foreach (var bullet in bulletLines)
                Assert.False(bullet.Contains("\n"), $"Bullet must be a single line: '{bullet}'");
        }

        [Fact]
        public void PromptBuilder_PersonalityAndBackstory_StillIncludeAnatomyContributions()
        {
            // Regression: anatomy is silenced ONLY in the texting-style
            // channel. Its personality / backstory fragments must still
            // flow into the prompt unchanged.
            var assembler = new CharacterAssembler(BuildItemRepo(), BuildAnatomyRepo());
            var fragments = assembler.Assemble(
                OneItemPerSlot, AnatomyStack, ZeroBaseStats, ZeroShadow);

            var prompt = PromptBuilder.BuildSystemPrompt(
                "TestChar", "she/her", "bio", fragments, new TrapState(),
                characterIdSeed: "stable-seed");

            string personalitySection = ExtractSection(prompt, "PERSONALITY", "BACKSTORY");
            string backstorySection   = ExtractSection(prompt, "BACKSTORY",   "TEXTING STYLE");

            // #1175: Band-based check — resolve bands for each AnatomyStack entry
            // and verify personality/backstory fragments land in the prompt.
            var anatomyRepo = BuildAnatomyRepo();
            var anatomyBands = AnatomyStack
                .Select(kv =>
                {
                    var param = anatomyRepo.GetParameter(kv.Key);
                    return param?.ResolveBand(kv.Value);
                })
                .Where(b => b != null)
                .ToList();
            Assert.NotEmpty(anatomyBands);

            bool sawAnyAnatomyPersonality = false;
            bool sawAnyAnatomyBackstory   = false;
            foreach (var band in anatomyBands)
            {
                if (!string.IsNullOrEmpty(band!.PersonalityFragment))
                {
                    Assert.Contains(band.PersonalityFragment, personalitySection);
                    sawAnyAnatomyPersonality = true;
                }
                if (!string.IsNullOrEmpty(band.BackstoryFragment))
                {
                    Assert.Contains(band.BackstoryFragment, backstorySection);
                    sawAnyAnatomyBackstory = true;
                }
            }

            Assert.True(sawAnyAnatomyPersonality);
            // #1184: load-bearing anatomy bands now carry backstory fragments.
            Assert.True(sawAnyAnatomyBackstory,
                "At least one resolved anatomy band should contribute a backstory fragment");
        }

        [Fact]
        public void PromptBuilder_Determinism_SameSeedProducesSamePrompt()
        {
            var assembler = new CharacterAssembler(BuildItemRepo(), BuildAnatomyRepo());
            var fragments = assembler.Assemble(
                OneItemPerSlot, AnatomyStack, ZeroBaseStats, ZeroShadow);

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
        public void Loader_AssemblesProfileWithNewAggregation()
        {
            string json = BuildCharacterJson(
                characterId: "11111111-1111-4111-8111-111111111111",
                items: OneItemPerSlot,
                anatomy: AnatomyStack);

            var profile = CharacterDefinitionLoader.Parse(json, BuildItemRepo(), BuildAnatomyRepo());

            Assert.False(string.IsNullOrEmpty(profile.TextingStyleFragment));

            // Every part of the joined string must be of the shape
            // "axis: rule" — no raw multi-line item / anatomy fragments.
            var parts = profile.TextingStyleFragment
                .Split(new[] { " | " }, StringSplitOptions.None);
            foreach (var part in parts)
            {
                int colon = part.IndexOf(':');
                Assert.True(colon > 0, $"Part missing axis prefix: '{part}'");
                string axis = part.Substring(0, colon);
                Assert.Contains(axis, new[]
                {
                    "emoji", "shorthand", "grammar", "structure", "length", "tics",
                    "stance", "register", "pacing",
                });
            }

            // Anatomy fragments still reachable on the per-source breakdown
            // (Character Sheet UI #404), with the new SlotOrParameter
            // field populated.
            var anatomySources = profile.TextingStyleSources
                .Where(s => s.Kind == "anatomy")
                .ToList();
            Assert.NotEmpty(anatomySources);
            foreach (var s in anatomySources)
                Assert.False(string.IsNullOrEmpty(s.SlotOrParameter),
                    $"Anatomy source '{s.Source}' missing SlotOrParameter (param id).");

            var itemSources = profile.TextingStyleSources
                .Where(s => s.Kind == "item")
                .ToList();
            foreach (var s in itemSources)
                Assert.False(string.IsNullOrEmpty(s.SlotOrParameter),
                    $"Item source '{s.Source}' missing SlotOrParameter (slot).");
        }

        [Fact]
        public void Loader_SameCharacterId_LocksThePickAcrossLoads()
        {
            string json = BuildCharacterJson(
                characterId: "22222222-2222-4222-8222-222222222222",
                items: OneItemPerSlot,
                anatomy: AnatomyStack);

            var p1 = CharacterDefinitionLoader.Parse(json, BuildItemRepo(), BuildAnatomyRepo());
            var p2 = CharacterDefinitionLoader.Parse(json, BuildItemRepo(), BuildAnatomyRepo());
            var p3 = CharacterDefinitionLoader.Parse(json, BuildItemRepo(), BuildAnatomyRepo());

            Assert.Equal(p1.TextingStyleFragment, p2.TextingStyleFragment);
            Assert.Equal(p2.TextingStyleFragment, p3.TextingStyleFragment);
            Assert.Equal(p1.AssembledSystemPrompt, p2.AssembledSystemPrompt);
        }

        [Fact]
        public void Loader_DifferentCharacterIds_SameItemsAndAnatomy_ProduceIdenticalAggregates()
        {
            // v1 rule is deterministic by configuration, NOT by character
            // id — the seed parameter is unused. So two characters with
            // identical equipment and anatomy MUST aggregate to the same
            // 9 axes regardless of UUID. This is the "build-craft is
            // preserved" property: same outfit → same texting style.
            string j1 = BuildCharacterJson(
                "33333333-3333-4333-8333-333333333333", OneItemPerSlot, AnatomyStack);
            string j2 = BuildCharacterJson(
                "44444444-4444-4444-8444-444444444444", OneItemPerSlot, AnatomyStack);

            var p1 = CharacterDefinitionLoader.Parse(j1, BuildItemRepo(), BuildAnatomyRepo());
            var p2 = CharacterDefinitionLoader.Parse(j2, BuildItemRepo(), BuildAnatomyRepo());

            Assert.Equal(p1.TextingStyleFragment, p2.TextingStyleFragment);
        }

        // ----- helpers --------------------------------------------------------

        /// <summary>
        /// Build a synthetic anatomy <see cref="TextingStyleFragmentSource"/>
        /// with the given tone-axis lines. Used by the group-voting tests
        /// to construct deterministic inputs without depending on the
        /// real anatomy fixture's tier shape.
        /// </summary>
        private static TextingStyleFragmentSource MakeAnatomyToneFragment(
            string parameterId, string tierName,
            string stance, string register, string pacing)
        {
            string fragment =
                "SYNTAX:\n" +
                "TONE:\n" +
                $"- stance ({tierName}): {stance}\n" +
                $"- register ({tierName}): {register}\n" +
                $"- pacing ({tierName}): {pacing}";
            return new TextingStyleFragmentSource(
                kind: "anatomy",
                source: tierName,
                fragment: fragment,
                slotOrParameter: parameterId);
        }

        private static string ExtractSection(string prompt, string header, string nextHeader)
        {
            int dataStart = prompt.IndexOf("=== CHARACTER DATA ===", StringComparison.Ordinal);
            int searchFrom = dataStart >= 0 ? dataStart : 0;
            int start = prompt.IndexOf(header, searchFrom, StringComparison.Ordinal);
            Assert.True(start >= 0, $"Section '{header}' not found in prompt.");
            int afterHeader = start + header.Length;
            int end = prompt.IndexOf(nextHeader, afterHeader, StringComparison.Ordinal);
            if (end < 0) end = prompt.Length;
            return prompt.Substring(afterHeader, end - afterHeader);
        }

        private static string BuildCharacterJson(
            string characterId,
            IEnumerable<string> items,
            IReadOnlyDictionary<string, float> anatomy)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append('{');
            sb.Append("\"schema_version\":2,");  // v2: float anatomy
            sb.Append($"\"character_id\":\"{characterId}\",");
            sb.Append("\"name\":\"TestChar\",");
            sb.Append("\"gender_identity\":\"she/her\",");
            sb.Append("\"bio\":\"a bio\",");
            sb.Append("\"level\":3,");
            sb.Append("\"items\":[");
            sb.Append(string.Join(",", items.Select(i => $"\"{i}\"")));
            sb.Append("],");
            sb.Append("\"anatomy\":{");
            // v2: write floats as JSON numbers (not strings)
            sb.Append(string.Join(",", anatomy.Select(kv =>
                $"\"{kv.Key}\":{kv.Value.ToString("G", System.Globalization.CultureInfo.InvariantCulture)}")));
            sb.Append("},");
            sb.Append("\"allocation\":{\"spent\":{},\"unspent_pool\":0,\"shadows\":{");
            sb.Append("\"madness\":0,\"despair\":0,\"denial\":0,\"fixation\":0,\"dread\":0,\"overthinking\":0");
            sb.Append("}}");
            sb.Append('}');
            return sb.ToString();
        }
    }
}
