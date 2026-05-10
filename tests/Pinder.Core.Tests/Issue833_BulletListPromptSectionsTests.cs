using System.Collections.Generic;
using System.Linq;
using Pinder.Core.Characters;
using Pinder.Core.Conversation;
using Pinder.Core.Prompts;
using Pinder.Core.Stats;
using Pinder.Core.Traps;
using Xunit;

namespace Pinder.Core.Tests
{
    /// <summary>
    /// Issue #833: PromptBuilder emits PERSONALITY, BACKSTORY, and TEXTING
    /// STYLE as bullet lists (one fragment per line, leading "- ") instead
    /// of pipe-joined / newline-joined prose blobs. Easier for the LLM to
    /// scan, easier to provenance back to the originating item / anatomy
    /// when a fragment surfaces verbatim in delivered text.
    /// </summary>
    [Trait("Category", "Characters")]
    public class Issue833_BulletListPromptSectionsTests
    {
        private static readonly Dictionary<StatType, int> ZeroBaseStats =
            new Dictionary<StatType, int>
            {
                { StatType.Charm, 0 }, { StatType.Rizz, 0 },
                { StatType.Honesty, 0 }, { StatType.Chaos, 0 },
                { StatType.Wit, 0 }, { StatType.SelfAwareness, 0 },
            };

        private static readonly Dictionary<ShadowStatType, int> ZeroShadow =
            new Dictionary<ShadowStatType, int>
            {
                { ShadowStatType.Madness, 0 }, { ShadowStatType.Despair, 0 },
                { ShadowStatType.Denial, 0 }, { ShadowStatType.Fixation, 0 },
                { ShadowStatType.Dread, 0 }, { ShadowStatType.Overthinking, 0 },
            };

        // ── PERSONALITY ──────────────────────────────────────────────────

        [Fact]
        public void PersonalitySection_EmitsBulletList_NotPipeJoined()
        {
            var fragments = BuildFragments(
                personality: new[] { "fragment_a", "fragment_b", "fragment_c" });

            string prompt = PromptBuilder.BuildSystemPrompt(
                "TestChar", "she/her", "bio", fragments, new TrapState());
            string section = ExtractSection(prompt, "PERSONALITY", "BACKSTORY");

            // Each fragment lives on its own line with a "- " marker.
            Assert.Contains("- fragment_a", section);
            Assert.Contains("- fragment_b", section);
            Assert.Contains("- fragment_c", section);
            // Old pipe-joined form must NOT appear.
            Assert.DoesNotContain("fragment_a | fragment_b", section);
        }

        // ── BACKSTORY ────────────────────────────────────────────────────

        [Fact]
        public void BackstorySection_EmitsBulletList()
        {
            var fragments = BuildFragments(
                backstory: new[] { "born somewhere", "moved cities", "had a moment" });

            string prompt = PromptBuilder.BuildSystemPrompt(
                "TestChar", "she/her", "bio", fragments, new TrapState());
            string section = ExtractSection(prompt, "BACKSTORY", "TEXTING STYLE");

            Assert.Contains("- born somewhere", section);
            Assert.Contains("- moved cities", section);
            Assert.Contains("- had a moment", section);
        }

        // ── TEXTING STYLE ────────────────────────────────────────────────

        [Fact]
        public void TextingStyleSection_EmitsBulletList_FromAggregatedItems()
        {
            // Two item-kind sources only \u2014 anatomy is silenced in this
            // channel by #836's aggregator.
            var sources = new List<TextingStyleFragmentSource>
            {
                new TextingStyleFragmentSource("item", "rolex",  "uses lots of \u2026"),
                new TextingStyleFragmentSource("item", "blazer", "lowercase texts only"),
            };
            var fragments = BuildFragments(textingStyleSources: sources);

            string prompt = PromptBuilder.BuildSystemPrompt(
                "TestChar", "she/her", "bio", fragments, new TrapState());
            string section = ExtractSection(prompt, "TEXTING STYLE", "ACTIVE ARCHETYPE");

            Assert.Contains("- uses lots of \u2026", section);
            Assert.Contains("- lowercase texts only", section);
            // Old pipe-joined form must NOT appear.
            Assert.DoesNotContain("uses lots of \u2026 | lowercase", section);
        }

        // ── Empty section handling ───────────────────────────────────────

        [Fact]
        public void EmptyFragmentList_EmitsNoBullets_HeaderStillPresent()
        {
            var fragments = BuildFragments(personality: new string[0]);

            string prompt = PromptBuilder.BuildSystemPrompt(
                "TestChar", "she/her", "bio", fragments, new TrapState());

            // Header is always emitted (downstream parsers depend on it)
            // but no bullet body lines for an empty list.
            Assert.Contains("PERSONALITY", prompt);
            string section = ExtractSection(prompt, "PERSONALITY", "BACKSTORY");
            // The section between PERSONALITY and BACKSTORY should be just
            // whitespace \u2014 no bullet lines to extract.
            var bullets = section.Split('\n')
                .Select(l => l.Trim())
                .Where(l => l.StartsWith("- "))
                .ToList();
            Assert.Empty(bullets);
        }

        [Fact]
        public void NullOrWhitespaceFragments_AreSkipped_NotEmittedAsEmptyBullets()
        {
            var fragments = BuildFragments(
                personality: new[] { "real-fragment", "", "   ", null! });

            string prompt = PromptBuilder.BuildSystemPrompt(
                "TestChar", "she/her", "bio", fragments, new TrapState());
            string section = ExtractSection(prompt, "PERSONALITY", "BACKSTORY");

            var bullets = section.Split('\n')
                .Select(l => l.Trim())
                .Where(l => l.StartsWith("- "))
                .ToList();
            Assert.Single(bullets);
            Assert.Equal("- real-fragment", bullets[0]);
        }

        // ── TextingStyleAggregator.AggregateAsList ───────────────────────

        [Fact]
        public void TextingStyleAggregator_AggregateAsList_ReturnsItemFragmentsInOriginalOrder()
        {
            var sources = new List<TextingStyleFragmentSource>
            {
                new TextingStyleFragmentSource("anatomy", "anatomy-x", "anatomy-frag"),
                new TextingStyleFragmentSource("item",    "item-1",    "item-fragment-1"),
                new TextingStyleFragmentSource("anatomy", "anatomy-y", "anatomy-frag-2"),
                new TextingStyleFragmentSource("item",    "item-2",    "item-fragment-2"),
            };

            var picked = TextingStyleAggregator.AggregateAsList(sources, seedKey: null);

            // Anatomy is filtered out; only the two item fragments survive,
            // in their original order.
            Assert.Equal(new[] { "item-fragment-1", "item-fragment-2" }, picked);
        }

        [Fact]
        public void TextingStyleAggregator_Aggregate_StillReturnsJoinedString_ForLegacyCallers()
        {
            // Round-trip the legacy joined form so CharacterDefinitionLoader
            // (which uses the joined string for delivery context) is unaffected.
            var sources = new List<TextingStyleFragmentSource>
            {
                new TextingStyleFragmentSource("item", "i1", "frag-one"),
                new TextingStyleFragmentSource("item", "i2", "frag-two"),
            };

            string joined = TextingStyleAggregator.Aggregate(sources, seedKey: null);

            Assert.Equal("frag-one | frag-two", joined);
        }

        // ── Helpers ──────────────────────────────────────────────────────

        private static FragmentCollection BuildFragments(
            IReadOnlyList<string>? personality = null,
            IReadOnlyList<string>? backstory = null,
            IReadOnlyList<TextingStyleFragmentSource>? textingStyleSources = null)
        {
            return new FragmentCollection(
                personalityFragments:  personality ?? new List<string> { "p" },
                backstoryFragments:    backstory   ?? new List<string> { "b" },
                textingStyleFragments: new List<string>(),
                rankedArchetypes:      new List<(string, int)>(),
                timing: new TimingProfile(5, 1.0f, 0.0f, "neutral"),
                stats: new StatBlock(ZeroBaseStats, ZeroShadow),
                activeArchetype: null,
                textingStyleSources: textingStyleSources ?? new List<TextingStyleFragmentSource>());
        }

        private static string ExtractSection(string prompt, string header, string nextHeader)
        {
            int start = prompt.IndexOf(header, System.StringComparison.Ordinal);
            Assert.True(start >= 0, $"Section '{header}' not found in prompt.");
            int afterHeader = start + header.Length;
            int end = prompt.IndexOf(nextHeader, afterHeader, System.StringComparison.Ordinal);
            return end >= 0 ? prompt.Substring(afterHeader, end - afterHeader) : prompt.Substring(afterHeader);
        }
    }
}
