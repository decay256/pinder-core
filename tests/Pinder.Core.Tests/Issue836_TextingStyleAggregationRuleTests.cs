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
    [Collection("StaticWiring")]
    public partial class Issue836_TextingStyleAggregationRuleTests
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

        // Six items — one in each slot — so all 6 syntax axes are
        // exercised by the tests. The starter-items.json fixture has at
        // least one item per slot; the assembler maps slot from
        // ItemDefinition.Slot.
        // Issue #1176: updated to use real Unity item ids / Unity slot names.
        // Slot → syntax axis mapping (Unity slots): Special→emoji, Head→shorthand,
        // Body→grammar, Hair→structure, Arms→length, Face→tics.
        private static readonly string[] OneItemPerSlot =
        {
            "special_shoe3",   // Special slot → emoji axis (has SYNTAX block)
            "head_hat",        // Head slot → shorthand axis
            "vest8",           // Body slot → grammar axis (has personality fragment)
            "hair1",           // Hair slot → structure axis
            "arms0",           // Arms slot → length axis
            "face_eyes1",      // Face slot → tics axis
        };

        // A small anatomy stack covering at least one tier per tone group
        // so each of the three tone axes has a contributing source.
        // #1175: now uses Unity param ids with float values [0..1].
        // Stance group: trunkLengthBase, trunkLengthMid, trunkLengthTip, trunkGirth, trunkCurvature
        // Register group: skinHue, skinSat, skinVal, freckles, blemishes, veins
        // Pacing group: glansScale, glansWidth, scrotumScale, leftTesticleScale, rightTesticleScale, scrotumDrop, isCircumcised
        private static readonly Dictionary<string, float> AnatomyStack =
            new Dictionary<string, float>
            {
                { "trunkLengthBase",  0.18f },  // stance group (band 1 – compact)
                { "trunkGirth",       0.08f },  // stance group (band 0 – slim)
                { "veins",            0.08f },  // register group (band 0 – subtle)
                { "blemishes",        0.05f },  // register group (band 0 – smooth)
                { "glansScale",       0.50f },  // pacing group (mid band)
                { "isCircumcised",    0.0f  },  // pacing group (uncircumcised band)
            };

        // ----- direct aggregator: parsing -------------------------------------

        [Fact]
        public void ParseSyntaxAxes_ExtractsAllSixAxes()
        {
            var repo = BuildItemRepo();
            // Issue #1176: use real Unity item id (special_shoe3 has a full SYNTAX/TONE block)
            var item = repo.GetItem("special_shoe3");
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
            // #1175: Use the trunkCurvature parameter from the bundled file,
            // which has SYNTAX/TONE blocks and parens in tone axis keys.
            var repo = BuildAnatomyRepo();
            var param = repo.GetParameter("trunkCurvature");
            Assert.NotNull(param);
            // Resolve a band that has a texting_style_fragment (band 1 = neutral-ish)
            var band = param!.ResolveBand(0.5f); // neutral straight → band 3
            Assert.NotNull(band);
            Assert.NotNull(band!.TextingStyleFragment);

            var axes = TextingStyleAggregator.ParseToneAxes(band.TextingStyleFragment!);
            Assert.Contains("stance", axes.Keys);
            // The parenthesised sub-key (e.g. "stance (neutral)") must not
            // appear in the extracted text key.
            Assert.False(axes.ContainsKey("stance (neutral)"));
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
                new Dictionary<string, float>(),
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
                OneItemPerSlot, new Dictionary<string, float>(),
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

            // Find two items in slot "Special" (Unity footwear slot) with different emoji axes.
            var repo = BuildItemRepo();
            var allItems = repo.GetAll()
                .Where(i => string.Equals(i.Slot, "Special", StringComparison.OrdinalIgnoreCase))
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
            // Issue #1176: use special_shoe3 which has a full SYNTAX/TONE block
            var shoesItem = repo.GetItem("special_shoe3");
            Assert.NotNull(shoesItem);

            var shoesAxes = TextingStyleAggregator.ParseSyntaxAxes(shoesItem!.TextingStyleFragment);
            var nonEmojiLines = shoesAxes
                .Where(kv => !string.Equals(kv.Key, "emoji", StringComparison.OrdinalIgnoreCase))
                .Select(kv => kv.Value)
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .ToList();

            var assembler = new CharacterAssembler(BuildItemRepo(), BuildAnatomyRepo());
            // Equip ONLY the shoes item; no other items, no anatomy.
            var fragments = assembler.Assemble(
                new[] { shoesItem!.ItemId },
                new Dictionary<string, float>(),
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

    }
}
