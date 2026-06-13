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
    /// Issue #832: the assembled system prompt's archetype section is now
    /// "ACTIVE ARCHETYPE" (the level-eligible top-ranked archetype with
    /// its full behavior text), not "ARCHETYPES (tendency order — ...)"
    /// (a numbered list of every archetype name with vote counts).
    /// </summary>
    [Trait("Category", "Characters")]
    [Collection("StaticWiring")]
    public class Issue832_ActiveArchetypePromptTests
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

        // ── Section structure ────────────────────────────────────────────

        [Fact]
        public void BuildSystemPrompt_EmitsActiveArchetypeSection()
        {
            var fragments = BuildFragmentsWithActiveArchetype(
                name: "The Peacock",
                behavior: "Loud, expensive flex. Sample lines: \"check my watch\" \u00b7 \"weekend trip booked\"",
                count: 4, totalCount: 5);
            string prompt = PromptBuilder.BuildSystemPrompt(
                "TestChar", "she/her", "bio", fragments, new TrapState());

            Assert.Contains("ACTIVE ARCHETYPE", prompt);
        }

        [Fact]
        public void BuildSystemPrompt_DoesNotEmitRankedTendencyList()
        {
            // #832: the old ranked list is gone. A new system prompt MUST
            // NOT contain the old header ("ARCHETYPES (tendency order ...")
            // and MUST NOT contain the numbered-list pattern (e.g. "1.").
            var fragments = BuildFragmentsWithActiveArchetype(
                name: "The Peacock", behavior: "behavior", count: 4, totalCount: 5,
                rankedNonActives: new[] { ("The Wall of Text", 2), ("The Hey Opener", 1) });
            string prompt = PromptBuilder.BuildSystemPrompt(
                "TestChar", "she/her", "bio", fragments, new TrapState());

            Assert.DoesNotContain("ARCHETYPES (tendency order", prompt);
            // The non-active archetypes must NOT appear in the prompt.
            Assert.DoesNotContain("The Wall of Text", prompt);
            Assert.DoesNotContain("The Hey Opener", prompt);
        }

        // ── Active-archetype content ─────────────────────────────────────

        [Fact]
        public void BuildSystemPrompt_IncludesActiveArchetypeNameInterferenceAndBehavior()
        {
            // 4 of 5 votes \u2192 ratio 0.80 \u2192 "dominant" interference level.
            var fragments = BuildFragmentsWithActiveArchetype(
                name: "The Peacock",
                behavior: "Loud, expensive flex.\n*Sample lines:* \"check my watch\" \u00b7 \"weekend trip booked\"",
                count: 4, totalCount: 5);

            string prompt = PromptBuilder.BuildSystemPrompt(
                "TestChar", "she/her", "bio", fragments, new TrapState());

            // Name + interference level on a labelled bullet so a parser
            // can split them out cleanly.
            Assert.Contains("- The Peacock (dominant)", prompt);
            // Full behavior text (including sample-lines fragment).
            Assert.Contains("Loud, expensive flex.", prompt);
            Assert.Contains("*Sample lines:*", prompt);
        }

        [Fact]
        public void BuildSystemPrompt_RendersClearInterferenceLevel_ForSplitVotes()
        {
            // 2 of 4 = 0.5 \u2192 "clear" (per ActiveArchetype.InterferenceLevel).
            var fragments = BuildFragmentsWithActiveArchetype(
                name: "The Peacock", behavior: "b", count: 2, totalCount: 4);

            string prompt = PromptBuilder.BuildSystemPrompt(
                "TestChar", "she/her", null, fragments, new TrapState());

            Assert.Contains("- The Peacock (clear)", prompt);
        }

        [Fact]
        public void BuildSystemPrompt_RendersSlightInterferenceLevel_ForMinorityVotes()
        {
            // 1 of 5 = 0.2 \u2192 "slight".
            var fragments = BuildFragmentsWithActiveArchetype(
                name: "The Peacock", behavior: "b", count: 1, totalCount: 5);

            string prompt = PromptBuilder.BuildSystemPrompt(
                "TestChar", "she/her", null, fragments, new TrapState());

            Assert.Contains("- The Peacock (slight)", prompt);
        }

        // ── Fallback path ────────────────────────────────────────────────

        [Fact]
        public void BuildSystemPrompt_NullActiveArchetype_EmitsNoneResolvedFallback()
        {
            // No active archetype (legacy / under-leveled / no votes).
            var fragments = new FragmentCollection(
                personalityFragments: new List<string> { "x" },
                backstoryFragments:   new List<string> { "y" },
                textingStyleFragments: new List<string> { "z" },
                rankedArchetypes:     new List<(string, int)>(),
                timing: new TimingProfile(5, 1.0f, 0.0f, "neutral"),
                stats: new StatBlock(ZeroBaseStats, ZeroShadow),
                activeArchetype: null);

            string prompt = PromptBuilder.BuildSystemPrompt(
                "TestChar", "she/her", null, fragments, new TrapState());

            Assert.Contains("ACTIVE ARCHETYPE", prompt);
            Assert.Contains("(none resolved)", prompt);
        }

        // ── Helpers ──────────────────────────────────────────────────────

        private static FragmentCollection BuildFragmentsWithActiveArchetype(
            string name,
            string behavior,
            int count,
            int totalCount,
            IEnumerable<(string Name, int Count)>? rankedNonActives = null)
        {
            var ranked = new List<(string, int)> { (name, count) };
            if (rankedNonActives != null) ranked.AddRange(rankedNonActives);

            var active = new ActiveArchetype(name, behavior, count, totalCount);

            return new FragmentCollection(
                personalityFragments:  new List<string> { "p" },
                backstoryFragments:    new List<string> { "b" },
                textingStyleFragments: new List<string> { "t" },
                rankedArchetypes:      ranked,
                timing: new TimingProfile(5, 1.0f, 0.0f, "neutral"),
                stats: new StatBlock(ZeroBaseStats, ZeroShadow),
                activeArchetype: active);
        }
    }
}
