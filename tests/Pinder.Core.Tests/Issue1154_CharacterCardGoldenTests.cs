using System;
using System.Collections.Generic;
using System.IO;
using Pinder.Core.Characters;
using Pinder.Core.Conversation;
using Pinder.Core.Prompts;
using Pinder.Core.Stats;
using Pinder.Core.Text;
using Pinder.Core.Traps;
using Xunit;

namespace Pinder.Core.Tests
{
    /// <summary>
    /// Issue #1154 — golden byte-identity oracle for the inner character-card
    /// builder <see cref="PromptBuilder.BuildSystemPromptEx"/>.
    ///
    /// The #1154 refactor collapsed the 7 <c>structural-*</c> section-header
    /// keys in <c>data/prompts/structural.yaml</c> into a single
    /// <c>character_card_framing</c> field; the builder splits that one field
    /// back into the 7 labels and emits them in the EXACT same byte positions
    /// as before. This is a BEHAVIOR-PRESERVING change: the compiled character
    /// card must stay byte-for-byte identical to what the old per-key builder
    /// produced.
    ///
    /// The golden fixtures were captured from the UNMODIFIED pre-refactor code
    /// and checked in verbatim. Both the trap-INACTIVE and trap-ACTIVE cases
    /// are pinned (the trap block is gated on <c>activeTraps.AllActive</c>).
    ///
    /// NOTE (orchestrator decision): #1154's other half — reordering sections
    /// so variable per-character data forms a trailing block — was DEFERRED
    /// because it is byte-CHANGING and conflicts with this non-negotiable
    /// byte-identity contract. Only the SSOT collapse landed here.
    /// </summary>
    [Trait("Category", "PromptCatalog")]
    [Collection("StaticWiring")]
    public class Issue1154_CharacterCardGoldenTests
    {
        private static string FixturesDir =>
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "Issue1154");

        private static string ReadFixture(string name) =>
            File.ReadAllText(Path.Combine(FixturesDir, name));

        private static readonly Dictionary<StatType, int> BaseStats =
            new Dictionary<StatType, int>
            {
                { StatType.Charm, 3 }, { StatType.Rizz, 2 },
                { StatType.Honesty, 5 }, { StatType.Chaos, 1 },
                { StatType.Wit, 4 }, { StatType.SelfAwareness, 0 },
            };

        private static readonly Dictionary<ShadowStatType, int> Shadow =
            new Dictionary<ShadowStatType, int>
            {
                { ShadowStatType.Madness, 0 }, { ShadowStatType.Despair, 0 },
                { ShadowStatType.Denial, 0 }, { ShadowStatType.Fixation, 0 },
                { ShadowStatType.Dread, 0 }, { ShadowStatType.Overthinking, 0 },
            };

        private static FragmentCollection BuildDeterministicFragments()
        {
            return new FragmentCollection(
                personalityFragments: new List<string> { "warm but guarded", "dry humour", "secretly competitive" },
                backstoryFragments:   new List<string> { "grew up by the sea", "dropped out of art school", "adopted a three-legged cat" },
                textingStyleFragments: new List<string>(),
                rankedArchetypes:     new List<(string, int)> { ("The Peacock", 4) },
                timing: new TimingProfile(5, 1.0f, 0.0f, "neutral"),
                stats: new StatBlock(BaseStats, Shadow),
                activeArchetype: new ActiveArchetype(
                    "The Peacock",
                    string.Join(Environment.NewLine,
                        "Loud, expensive flex.",
                        "*Sample lines:* \"check my watch\" \u00b7 \"weekend trip booked\""),
                    4, 5),
                textingStyleSources: new List<TextingStyleFragmentSource>
                {
                    new TextingStyleFragmentSource(
                        "item", "hiking-boots",
                        "SYNTAX:\n- emoji: never uses emoji at all\n- shorthand: \n- grammar: \n- structure: \n- length: \n- tics: \nTONE:",
                        slotOrParameter: "shoes"),
                });
        }

        // ── trap-INACTIVE: no ACTIVE TRAP block ───────────────────────────
        [Fact]
        public void BuildSystemPromptEx_TrapInactive_MatchesGoldenByteForByte()
        {
            var fragments = BuildDeterministicFragments();

            var result = PromptBuilder.BuildSystemPromptEx(
                "Velvet", "she/her", "just here for the vibes",
                fragments, new TrapState(), characterIdSeed: "fixed-seed-1154",
                archetypesEnabled: true);

            Assert.Equal(ReadFixture("golden_inactive.txt"), result.Text);
        }

        // ── trap-ACTIVE: the gated ACTIVE TRAP INSTRUCTIONS block appears ──
        [Fact]
        public void BuildSystemPromptEx_TrapActive_MatchesGoldenByteForByte()
        {
            var fragments = BuildDeterministicFragments();

            var trapState = new TrapState();
            trapState.Activate(new TrapDefinition(
                id: "cringe",
                stat: StatType.Charm,
                effect: TrapEffect.StatPenalty,
                effectValue: 2,
                durationTurns: 3,
                llmInstruction: "You just said something deeply cringe. Overcompensate awkwardly for the next few messages.",
                clearMethod: "land a genuine joke",
                nat1Bonus: "double cringe",
                displayName: "Cringe"));

            var result = PromptBuilder.BuildSystemPromptEx(
                "Velvet", "she/her", "just here for the vibes",
                fragments, trapState, characterIdSeed: "fixed-seed-1154",
                archetypesEnabled: true);

            Assert.Equal(ReadFixture("golden_active.txt"), result.Text);
        }

        [Fact]
        public void BuildSystemPromptEx_ConstantPrefix_IdenticalAcrossCharacters()
        {
            var f1 = BuildDeterministicFragments();
            var t1 = new TrapState();
            var r1 = PromptBuilder.BuildSystemPromptEx("Alice", "she/her", "bio 1", f1, t1, "seed1");
            
            var f2 = new FragmentCollection(
                new List<string> { "different personality" }, new List<string>(), new List<string>(), 
                new List<(string, int)>(), new TimingProfile(1, 1, 1, "f"), new StatBlock(BaseStats, Shadow), null, new List<TextingStyleFragmentSource>());
            var t2 = new TrapState();
            var r2 = PromptBuilder.BuildSystemPromptEx("Bob", "he/him", "bio 2", f2, t2, "seed2");

            int split1 = r1.Text.IndexOf("=== CHARACTER DATA ===", StringComparison.Ordinal);
            int split2 = r2.Text.IndexOf("=== CHARACTER DATA ===", StringComparison.Ordinal);
            
            string prefix1 = r1.Text.Substring(0, split1 + "=== CHARACTER DATA ===".Length);
            string prefix2 = r2.Text.Substring(0, split2 + "=== CHARACTER DATA ===".Length);
            
            Assert.Equal(prefix1, prefix2);
        }

        // ── provenance: every framing span is now sourced from the single
        //    collapsed key (was the 7 structural-* keys) ────────────────────
        [Fact]
        public void BuildSystemPromptEx_FramingSpans_AttributeToCollapsedKey()
        {
            var fragments = BuildDeterministicFragments();

            var trapState = new TrapState();
            trapState.Activate(new TrapDefinition(
                "cringe", StatType.Charm, TrapEffect.StatPenalty, 2, 3,
                "trap instr", "clear", "nat1", "Cringe"));

            var result = PromptBuilder.BuildSystemPromptEx(
                "Velvet", "she/her", "bio", fragments, trapState,
                characterIdSeed: "fixed-seed-1154");

            // No span carries a legacy structural-* key anymore.
            Assert.DoesNotContain(result.Spans, s =>
                s.Key != null && s.Key.StartsWith("structural-", StringComparison.Ordinal));
            // The framing headers are now attributed to the collapsed key.
            Assert.Contains(result.Spans, s => s.Key == PromptBuilder.CharacterCardFramingKey);
        }

        [Fact]
        public void BuildSystemPromptEx_DataFramingSpans_AttributePromptLabelsToCatalog()
        {
            var fragments = BuildDeterministicFragments();

            var result = PromptBuilder.BuildSystemPromptEx(
                "Velvet", "she/her", "just here for the vibes",
                fragments, new TrapState(), characterIdSeed: "fixed-seed-1154",
                archetypesEnabled: true);

            AssertAllOccurrencesAttributed(result, "EFFECTIVE STATS", PromptBuilder.CharacterDataFramingKey);
            AssertAllOccurrencesAttributed(result, "=== CHARACTER DATA ===", PromptBuilder.CharacterDataFramingKey);
            AssertAllOccurrencesAttributed(result, "- Gender identity:", PromptBuilder.CharacterDataFramingKey);
            AssertAllOccurrencesAttributed(result, "- Bio:", PromptBuilder.CharacterDataFramingKey);
            AssertAllOccurrencesAttributed(result, "- Charm:", PromptBuilder.CharacterDataFramingKey);
            AssertAllOccurrencesAttributed(result, "- Self-Awareness:", PromptBuilder.CharacterDataFramingKey);
        }

        [Fact]
        public void BuildSystemPromptEx_MissingDataFraming_Throws()
        {
            var previousLookup = PromptBuilder.StructuralFragmentLookup;
            var previousLookupEx = PromptBuilder.StructuralFragmentLookupEx;
            try
            {
                PromptBuilder.StructuralFragmentLookup = key =>
                    key == PromptBuilder.CharacterCardFramingKey
                        ? "RULES\nIDENTITY\nPERSONALITY\nBACKSTORY\nTEXTING STYLE\nACTIVE ARCHETYPE\nACTIVE TRAP INSTRUCTIONS"
                        : null;
                PromptBuilder.StructuralFragmentLookupEx = null;

                var ex = Assert.Throws<InvalidOperationException>(() =>
                    PromptBuilder.BuildSystemPromptEx(
                        "Velvet", "she/her", "bio",
                        BuildDeterministicFragments(), new TrapState()));

                Assert.Contains(PromptBuilder.CharacterDataFramingKey, ex.Message, StringComparison.Ordinal);
            }
            finally
            {
                PromptBuilder.StructuralFragmentLookup = previousLookup;
                PromptBuilder.StructuralFragmentLookupEx = previousLookupEx;
            }
        }

        [Fact]
        public void BuildSystemPromptEx_MalformedDataFraming_Throws()
        {
            var previousLookup = PromptBuilder.StructuralFragmentLookup;
            var previousLookupEx = PromptBuilder.StructuralFragmentLookupEx;
            try
            {
                PromptBuilder.StructuralFragmentLookup = key =>
                    key == PromptBuilder.CharacterCardFramingKey
                        ? "RULES\nIDENTITY\nPERSONALITY\nBACKSTORY\nTEXTING STYLE\nACTIVE ARCHETYPE\nACTIVE TRAP INSTRUCTIONS"
                        : "EFFECTIVE STATS\n=== CHARACTER DATA ===";
                PromptBuilder.StructuralFragmentLookupEx = null;

                var ex = Assert.Throws<InvalidOperationException>(() =>
                    PromptBuilder.BuildSystemPromptEx(
                        "Velvet", "she/her", "bio",
                        BuildDeterministicFragments(), new TrapState()));

                Assert.Contains("12 labels/templates", ex.Message, StringComparison.Ordinal);
            }
            finally
            {
                PromptBuilder.StructuralFragmentLookup = previousLookup;
                PromptBuilder.StructuralFragmentLookupEx = previousLookupEx;
            }
        }

        private static void AssertAllOccurrencesAttributed(
            PromptTraceResult result,
            string text,
            string key)
        {
            int searchFrom = 0;
            int matches = 0;
            while (true)
            {
                int index = result.Text.IndexOf(text, searchFrom, StringComparison.Ordinal);
                if (index < 0) break;
                matches++;
                Assert.Contains(result.Spans, span =>
                    span.Key == key &&
                    span.Start <= index &&
                    span.End >= index + text.Length);
                searchFrom = index + text.Length;
            }

            Assert.True(matches > 0, $"Expected prompt to contain '{text}'.");
        }
    }
}
