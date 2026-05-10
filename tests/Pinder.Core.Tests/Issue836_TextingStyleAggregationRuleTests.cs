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
    /// #836 v1 texting-style aggregation rule:
    ///   - 6 syntax axes are owned 1:1 by the 6 item slots
    ///     (shoes\u2192emoji, hat\u2192shorthand, shirt\u2192grammar, trousers\u2192structure,
    ///     frame\u2192length, accessory\u2192tics).
    ///   - 3 tone axes (stance, register, pacing) are decided by majority
    ///     vote across anatomy parameter groups.
    ///   - Output is up to 9 axis-prefixed lines in canonical order;
    ///     missing sources drop their axis rather than back-filling.
    ///   - Fully deterministic per (character_id, items, anatomy).
    ///   - Personality / backstory channels are unaffected.
    ///
    /// See <c>docs/persona/texting-style-aggregation.md</c> for the
    /// design rationale.
    /// </summary>
    [Trait("Category", "Characters")]
    public class Issue836_TextingStyleAggregationRuleTests
    {
        // ----- repo helpers ---------------------------------------------------

        /// <summary>
        /// Walk up from the test binary's directory looking for the
        /// canonical pinder-core data file at <c>data/&lt;relativePath&gt;</c>.
        /// The legacy <c>agents-extra/pinder/data</c> mirror was stale
        /// (pre-#834 single-line texting fragments) so we deliberately do
        /// NOT fall back to it here — the v1 rule needs the new
        /// SYNTAX/TONE block format.
        /// </summary>
        private static string LoadJson(string relativePath)
        {
            var dir = AppDomain.CurrentDomain.BaseDirectory;
            for (int i = 0; i < 10; i++)
            {
                var candidate = Path.Combine(dir, "data", relativePath);
                if (File.Exists(candidate)) return File.ReadAllText(candidate);
                var parent = Path.GetDirectoryName(dir);
                if (parent == null || parent == dir) break;
                dir = parent;
            }
            throw new FileNotFoundException(
                $"Could not locate data/{relativePath} in any ancestor of the test binary.");
        }

        private static IItemRepository BuildItemRepo()
            => new JsonItemRepository(LoadJson("items/starter-items.json"));

        private static IAnatomyRepository BuildAnatomyRepo()
            => new JsonAnatomyRepository(LoadJson("anatomy/anatomy-parameters.json"));

        private static readonly IReadOnlyDictionary<StatType, int> ZeroBaseStats =
            new Dictionary<StatType, int>();
        private static readonly IReadOnlyDictionary<ShadowStatType, int> ZeroShadow =
            new Dictionary<ShadowStatType, int>();

        // Six items \u2014 one in each slot \u2014 so all 6 syntax axes are
        // exercised by the tests. The starter-items.json fixture has at
        // least one item per slot; the assembler maps slot from
        // ItemDefinition.Slot.
        private static readonly string[] OneItemPerSlot =
        {
            "vintage-band-tee",         // shirt
            "rubber-duck",              // accessory
            "hiking-boots",             // shoes
            "beanie-with-patches",      // hat
            "worn-paperback",           // accessory? \u2014 will fall back to whichever slot
            "cargo-shorts",             // trousers
        };

        // A small anatomy stack covering at least one tier per tone group
        // so each of the three tone axes has a contributing source.
        private static readonly Dictionary<string, string> AnatomyStack =
            new Dictionary<string, string>
            {
                { "length",          "short" },        // stance group
                { "girth",           "slim"  },        // stance group
                { "vein_definition", "subtle" },       // register group
                { "skin_texture",    "smooth" },       // register group
                { "ball_size",       "petite" },       // pacing group
                { "tattoos",         "ink-free" },     // pacing group
            };

        // ----- direct aggregator: parsing -------------------------------------

        [Fact]
        public void ParseSyntaxAxes_ExtractsAllSixAxes()
        {
            var repo = BuildItemRepo();
            var item = repo.GetItem("vintage-band-tee");
            Assert.NotNull(item);

            var axes = TextingStyleAggregator.ParseSyntaxAxes(item!.TextingStyleFragment);
            Assert.Equal(
                new[] { "emoji", "shorthand", "grammar", "structure", "length", "tics" }
                    .OrderBy(s => s),
                axes.Keys.OrderBy(s => s));
            // Each axis must have a non-empty value (the canonical pool
            // structure per docs/persona/texting-style-pool.md).
            foreach (var kv in axes)
                Assert.False(string.IsNullOrWhiteSpace(kv.Value));
        }

        [Fact]
        public void ParseToneAxes_ExtractsStanceRegisterPacing_WithParenSubKeyStripped()
        {
            var repo = BuildAnatomyRepo();
            var lengthShort = repo.GetParameter("length")!.GetTier("short");
            Assert.NotNull(lengthShort);

            var axes = TextingStyleAggregator.ParseToneAxes(lengthShort!.TextingStyleFragment);
            Assert.Contains("stance", axes.Keys);
            Assert.Contains("register", axes.Keys);
            Assert.Contains("pacing", axes.Keys);
            // The parenthesised sub-key (e.g. "stance (dry)") must not
            // appear in the extracted text key.
            Assert.False(axes.ContainsKey("stance (dry)"));
            Assert.False(axes.ContainsKey("register (scientific)"));
        }

        [Fact]
        public void ParseToneAxes_EmptyFragment_ReturnsEmptyMap()
        {
            var axes = TextingStyleAggregator.ParseToneAxes("");
            Assert.Empty(axes);
        }

        [Fact]
        public void ParseToneAxes_NullFragment_ReturnsEmptyMap()
        {
            var axes = TextingStyleAggregator.ParseToneAxes(null!);
            Assert.Empty(axes);
        }

        [Fact]
        public void ParseSyntaxAxes_TonelessFragment_StillExtractsSyntax()
        {
            // SYNTAX-only block (no TONE section) should still parse.
            const string fragment = "SYNTAX:\n- emoji: foo\n- shorthand: bar\n- grammar: baz\n- structure: qux\n- length: aaa\n- tics: bbb";
            var axes = TextingStyleAggregator.ParseSyntaxAxes(fragment);
            Assert.Equal("foo", axes["emoji"]);
            Assert.Equal("bbb", axes["tics"]);
        }

        // ----- direct aggregator: full assemble -------------------------------

        [Fact]
        public void Aggregate_AllSlotsAndAnatomyGroups_EmitsUpToNineAxes()
        {
            var assembler = new CharacterAssembler(BuildItemRepo(), BuildAnatomyRepo());
            var fragments = assembler.Assemble(
                OneItemPerSlot, AnatomyStack, ZeroBaseStats, ZeroShadow);

            var lines = TextingStyleAggregator.AggregateAsList(
                fragments.TextingStyleSources, "char-1");

            // Must be at most 9 lines.
            Assert.True(lines.Count <= 9,
                $"Expected at most 9 lines; got {lines.Count}: [{string.Join(", ", lines)}]");

            // Each line must be of the shape "axis: rule".
            foreach (var line in lines)
            {
                int colon = line.IndexOf(':');
                Assert.True(colon > 0, $"Line missing axis prefix: '{line}'");
                Assert.True(line.Length > colon + 1, $"Line missing rule body: '{line}'");
            }

            // Canonical axis ordering: every axis listed must come from
            // {emoji, shorthand, grammar, structure, length, tics, stance,
            // register, pacing} and appear in that order.
            var canonical = new[]
            {
                "emoji", "shorthand", "grammar", "structure", "length", "tics",
                "stance", "register", "pacing",
            };
            int prevIdx = -1;
            foreach (var line in lines)
            {
                string axis = line.Substring(0, line.IndexOf(':'));
                int idx = Array.IndexOf(canonical, axis);
                Assert.True(idx >= 0, $"Unknown axis '{axis}' in line '{line}'");
                Assert.True(idx > prevIdx, $"Axes out of canonical order at '{line}'");
                prevIdx = idx;
            }
        }

        [Fact]
        public void Aggregate_NoItems_NoAnatomy_ReturnsEmpty()
        {
            var assembler = new CharacterAssembler(BuildItemRepo(), BuildAnatomyRepo());
            var fragments = assembler.Assemble(
                Array.Empty<string>(),
                new Dictionary<string, string>(),
                ZeroBaseStats, ZeroShadow);

            var lines = TextingStyleAggregator.AggregateAsList(
                fragments.TextingStyleSources, "char-empty");

            Assert.Empty(lines);
        }

        [Fact]
        public void Aggregate_OnlyAnatomy_EmitsToneAxesOnly()
        {
            var assembler = new CharacterAssembler(BuildItemRepo(), BuildAnatomyRepo());
            var fragments = assembler.Assemble(
                Array.Empty<string>(), AnatomyStack, ZeroBaseStats, ZeroShadow);

            var lines = TextingStyleAggregator.AggregateAsList(
                fragments.TextingStyleSources, "char-anatomy-only");

            // Every line must be a tone axis; no syntax axes can appear.
            foreach (var line in lines)
            {
                string axis = line.Substring(0, line.IndexOf(':'));
                Assert.Contains(axis, new[] { "stance", "register", "pacing" });
            }
        }

        [Fact]
        public void Aggregate_OnlyItems_EmitsSyntaxAxesOnly()
        {
            var assembler = new CharacterAssembler(BuildItemRepo(), BuildAnatomyRepo());
            var fragments = assembler.Assemble(
                OneItemPerSlot, new Dictionary<string, string>(),
                ZeroBaseStats, ZeroShadow);

            var lines = TextingStyleAggregator.AggregateAsList(
                fragments.TextingStyleSources, "char-items-only");

            // No tone axes should appear.
            foreach (var line in lines)
            {
                string axis = line.Substring(0, line.IndexOf(':'));
                Assert.Contains(axis, new[] {
                    "emoji", "shorthand", "grammar", "structure", "length", "tics",
                });
            }
        }

        [Fact]
        public void Aggregate_DeterministicAcrossCalls()
        {
            var assembler = new CharacterAssembler(BuildItemRepo(), BuildAnatomyRepo());
            var fragments = assembler.Assemble(
                OneItemPerSlot, AnatomyStack, ZeroBaseStats, ZeroShadow);

            var a = TextingStyleAggregator.AggregateAsList(fragments.TextingStyleSources, "uuid-1");
            var b = TextingStyleAggregator.AggregateAsList(fragments.TextingStyleSources, "uuid-1");
            var c = TextingStyleAggregator.AggregateAsList(fragments.TextingStyleSources, "different-seed");

            Assert.Equal(a, b);
            // v1 rule is deterministic by construction \u2014 the seed
            // parameter is unused; passing a different seed must not
            // change the output.
            Assert.Equal(a, c);
        }

        [Fact]
        public void Aggregate_NullSeed_ProducesSameOutputAsNonNullSeed()
        {
            var assembler = new CharacterAssembler(BuildItemRepo(), BuildAnatomyRepo());
            var fragments = assembler.Assemble(
                OneItemPerSlot, AnatomyStack, ZeroBaseStats, ZeroShadow);

            var seeded = TextingStyleAggregator.AggregateAsList(fragments.TextingStyleSources, "uuid-1");
            var nullSeed = TextingStyleAggregator.AggregateAsList(fragments.TextingStyleSources, null);
            Assert.Equal(seeded, nullSeed);
        }

        // ----- slot \u2192 axis fixed mapping ----------------------------------

        [Fact]
        public void SlotMapping_EquippingShoes_ChangesEmojiAxis()
        {
            // Two builds, identical anatomy + everything-but-shoes
            // identical, different shoes \u2014 the emoji axis must change
            // (assuming the two shoes carry different emoji rules; the
            // starter pool guarantees this for the candidates we pick).
            var assembler = new CharacterAssembler(BuildItemRepo(), BuildAnatomyRepo());

            // Find two items in slot "shoes" with different emoji axes.
            var repo = BuildItemRepo();
            var allItems = repo.GetAll()
                .Where(i => string.Equals(i.Slot, "shoes", StringComparison.OrdinalIgnoreCase))
                .ToList();
            Assert.True(allItems.Count >= 2,
                "Need at least 2 shoes items in starter-items.json to run this test.");

            ItemDefinition? a = null;
            ItemDefinition? b = null;
            for (int i = 0; i < allItems.Count && b == null; i++)
            {
                var ax = TextingStyleAggregator.ParseSyntaxAxes(allItems[i].TextingStyleFragment);
                for (int j = i + 1; j < allItems.Count; j++)
                {
                    var bx = TextingStyleAggregator.ParseSyntaxAxes(allItems[j].TextingStyleFragment);
                    if (ax.TryGetValue("emoji", out var ae) &&
                        bx.TryGetValue("emoji", out var be) &&
                        !string.Equals(ae, be, StringComparison.Ordinal))
                    {
                        a = allItems[i];
                        b = allItems[j];
                        break;
                    }
                }
            }
            Assert.NotNull(a);
            Assert.NotNull(b);

            var fA = assembler.Assemble(new[] { a!.ItemId }, AnatomyStack, ZeroBaseStats, ZeroShadow);
            var fB = assembler.Assemble(new[] { b!.ItemId }, AnatomyStack, ZeroBaseStats, ZeroShadow);

            var emojiA = TextingStyleAggregator.AggregateAsList(fA.TextingStyleSources, null)
                .FirstOrDefault(l => l.StartsWith("emoji:"));
            var emojiB = TextingStyleAggregator.AggregateAsList(fB.TextingStyleSources, null)
                .FirstOrDefault(l => l.StartsWith("emoji:"));

            Assert.NotNull(emojiA);
            Assert.NotNull(emojiB);
            Assert.NotEqual(emojiA, emojiB);
        }

        [Fact]
        public void SlotMapping_OnlyTheOwnedAxisIsRead_OtherSyntaxLinesIgnored()
        {
            // A shoes item carries lines for all 6 syntax axes in its
            // texting_style_fragment. The aggregator must read ONLY the
            // emoji axis from the shoes item \u2014 the shorthand/grammar/
            // etc. lines on the same item must NOT leak into the
            // aggregate (those slots' axes are filled by hat / shirt /
            // etc. items, or silenced if absent).
            var repo = BuildItemRepo();
            var shoesItem = repo.GetAll()
                .First(i => string.Equals(i.Slot, "shoes", StringComparison.OrdinalIgnoreCase));

            var shoesAxes = TextingStyleAggregator.ParseSyntaxAxes(shoesItem.TextingStyleFragment);
            var nonEmojiLines = shoesAxes
                .Where(kv => !string.Equals(kv.Key, "emoji", StringComparison.OrdinalIgnoreCase))
                .Select(kv => kv.Value)
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .ToList();

            var assembler = new CharacterAssembler(BuildItemRepo(), BuildAnatomyRepo());
            // Equip ONLY the shoes item; no other items, no anatomy.
            var fragments = assembler.Assemble(
                new[] { shoesItem.ItemId },
                new Dictionary<string, string>(),
                ZeroBaseStats, ZeroShadow);

            var lines = TextingStyleAggregator.AggregateAsList(fragments.TextingStyleSources, null);

            // Must produce exactly one line, on the emoji axis.
            Assert.Single(lines);
            Assert.StartsWith("emoji:", lines[0]);

            // None of the shoes' OTHER axis lines may appear in the
            // aggregate output (they're owned by other slots).
            foreach (var nonEmojiLine in nonEmojiLines)
            {
                Assert.DoesNotContain(nonEmojiLine, lines[0]);
            }
        }

        // ----- anatomy group voting ------------------------------------------

        [Fact]
        public void AnatomyGroup_MajorityVoteWins()
        {
            // Construct two anatomy entries from the stance group with the
            // same stance value, and one with a different stance value \u2014
            // the majority (2) must win.
            var sources = new List<TextingStyleFragmentSource>
            {
                MakeAnatomyToneFragment("length", "short",
                    stance: "<dry-stance-line>", register: "x", pacing: "y"),
                MakeAnatomyToneFragment("girth", "slim",
                    stance: "<dry-stance-line>", register: "x", pacing: "y"),
                MakeAnatomyToneFragment("circumcision", "uncircumcised",
                    stance: "<other-stance-line>", register: "x", pacing: "y"),
            };

            var lines = TextingStyleAggregator.AggregateAsList(sources, null);
            var stanceLine = lines.Single(l => l.StartsWith("stance:"));
            Assert.Equal("stance: <dry-stance-line>", stanceLine);
        }

        [Fact]
        public void AnatomyGroup_TieBreakByGroupOrder()
        {
            // length and girth tie on stance \u2014 length wins (earliest in
            // the StanceGroup order [length, girth, circumcision]).
            var sources = new List<TextingStyleFragmentSource>
            {
                MakeAnatomyToneFragment("length", "short",
                    stance: "<line-from-length>", register: "x", pacing: "y"),
                MakeAnatomyToneFragment("girth", "slim",
                    stance: "<line-from-girth>", register: "x", pacing: "y"),
            };

            var lines = TextingStyleAggregator.AggregateAsList(sources, null);
            var stanceLine = lines.Single(l => l.StartsWith("stance:"));
            Assert.Equal("stance: <line-from-length>", stanceLine);
        }

        [Fact]
        public void AnatomyGroup_EmptyGroup_DropsAxis()
        {
            // skin_tone group (register) has empty fragments in the real
            // fixture for every tier; if the OTHER register-group params
            // (vein_definition, skin_texture) are also missing, the
            // register axis must drop entirely.
            var sources = new List<TextingStyleFragmentSource>
            {
                // Only stance-group entries.
                MakeAnatomyToneFragment("length", "short",
                    stance: "<a>", register: "<r>", pacing: "<p>"),
            };

            var lines = TextingStyleAggregator.AggregateAsList(sources, null);
            // stance must appear (length's stance), register and pacing
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
                MakeAnatomyToneFragment("nonsense_param", "tier-x",
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
                characterIdSeed: "stable-seed");

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

            var anatomyRepo = BuildAnatomyRepo();
            var anatomyTiers = AnatomyStack
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

            Assert.True(sawAnyAnatomyPersonality);
            Assert.True(sawAnyAnatomyBackstory);
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
            // "axis: rule" \u2014 no raw multi-line item / anatomy fragments.
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
            // id \u2014 the seed parameter is unused. So two characters with
            // identical equipment and anatomy MUST aggregate to the same
            // 9 axes regardless of UUID. This is the "build-craft is
            // preserved" property: same outfit \u2192 same texting style.
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
