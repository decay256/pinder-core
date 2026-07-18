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
using Xunit;

namespace Pinder.Core.Tests
{
    [Trait("Category", "Characters")]
    [Collection("StaticWiring")]
    public class CharacterSystemTests
    {
        // -----------------------------------------------------------------------
        // Helpers
        // -----------------------------------------------------------------------

        private static IItemRepository BuildItemRepo()
        {
            var json = TestRepoLocator.ReadDataFile("items/starter-items.json");
            return new JsonItemRepository(json);
        }

        private static IAnatomyRepository BuildAnatomyRepo()
        {
            var json = TestRepoLocator.ReadDataFile("anatomy/anatomy-parameters.json");
            return new JsonAnatomyRepository(json);
        }

        private static readonly IReadOnlyDictionary<StatType, int> ZeroBaseStats =
            new Dictionary<StatType, int>();

        private static readonly IReadOnlyDictionary<ShadowStatType, int> ZeroShadow =
            new Dictionary<ShadowStatType, int>();

        private class FixedDice : IDiceRoller
        {
            private readonly int _value;
            public FixedDice(int value) => _value = value;
            public int Roll(int sides) => Math.Min(_value, sides);
        }

        // -----------------------------------------------------------------------
        // CharacterAssembler tests
        // -----------------------------------------------------------------------

        [Fact]
        public void Assemble_TwoItems_SumsStats()
        {
            var assembler = new CharacterAssembler(BuildItemRepo(), BuildAnatomyRepo());

            // head_cheff: Charm +1
            // head_tophat: Charm +1, Rizz +1
            var result = assembler.Assemble(
                new[] { "head_cheff", "head_tophat" },
                new Dictionary<string, float>(),
                ZeroBaseStats, ZeroShadow);

            Assert.Equal(2, result.Stats.GetEffective(StatType.Charm));
            Assert.Equal(1, result.Stats.GetEffective(StatType.Rizz));
        }

        [Fact]
        public void Assemble_WithAnatomy_AddsAnatomyStats()
        {
            var assembler = new CharacterAssembler(BuildItemRepo(), BuildAnatomyRepo());

            // head_cheff: Charm +1
            // anatomy: isCircumcised=0.0 → uncircumcised band → Honesty+1
            //          scrotumScale=0.00 → band 0 (0.00-0.05) → Wit+1, SA+1
            var result = assembler.Assemble(
                new[] { "head_cheff" },
                new Dictionary<string, float>
                {
                    { "isCircumcised", 0.0f },   // uncircumcised → Honesty+1
                    { "scrotumScale",  0.02f },   // band 0 → Wit+1, SA+1
                },
                ZeroBaseStats, ZeroShadow);

            Assert.Equal(1, result.Stats.GetEffective(StatType.Charm));
            Assert.Equal(1, result.Stats.GetEffective(StatType.SelfAwareness));
            Assert.Equal(1, result.Stats.GetEffective(StatType.Honesty));
        }

        [Fact]
        public void Assemble_FragmentsNonEmpty()
        {
            var assembler = new CharacterAssembler(BuildItemRepo(), BuildAnatomyRepo());

            var result = assembler.Assemble(
                new[] { "head_cheff", "head_tophat" },
                new Dictionary<string, float> { { "trunkLengthBase", 0.18f }, { "trunkGirth", 0.08f } },
                ZeroBaseStats, ZeroShadow);

            Assert.NotEmpty(result.PersonalityFragments);
            Assert.NotEmpty(result.BackstoryFragments);
            Assert.NotEmpty(result.TextingStyleFragments);
        }

        [Fact]
        public void Assemble_ArchetypesRankedDescending()
        {
            var assembler = new CharacterAssembler(BuildItemRepo(), BuildAnatomyRepo());

            var result = assembler.Assemble(
                new[] { "head_tophat", "face_monocle" },
                new Dictionary<string, float> { { "trunkLengthBase", 0.18f } },
                ZeroBaseStats, ZeroShadow);

            Assert.NotEmpty(result.RankedArchetypes);

            // Counts must be descending
            for (int i = 1; i < result.RankedArchetypes.Count; i++)
                Assert.True(result.RankedArchetypes[i - 1].Count >= result.RankedArchetypes[i].Count);
        }

        [Fact]
        public void Assemble_UnknownItem_ThrowsFormatException()
        {
            var assembler = new CharacterAssembler(BuildItemRepo(), BuildAnatomyRepo());

            var ex = Assert.Throws<FormatException>(() =>
                assembler.Assemble(
                    new[] { "head_cheff", "THIS_DOES_NOT_EXIST" },
                    new Dictionary<string, float>(),
                    ZeroBaseStats, ZeroShadow));

            Assert.Contains("THIS_DOES_NOT_EXIST", ex.Message);
        }

        // -----------------------------------------------------------------------
        // PromptBuilder tests
        // -----------------------------------------------------------------------

        [Fact]
        public void BuildSystemPrompt_ContainsRequiredSections()
        {
            var assembler = new CharacterAssembler(BuildItemRepo(), BuildAnatomyRepo());
            var fragments = assembler.Assemble(
                new[] { "head_cheff" },
                new Dictionary<string, float> { { "trunkLengthBase", 0.18f } },
                ZeroBaseStats, ZeroShadow);

            var prompt = PromptBuilder.BuildSystemPrompt(
                "TestChar", "she/her", "A test bio.", fragments, new TrapState(),
                archetypesEnabled: true);

            Assert.Contains("PERSONALITY", prompt);
            Assert.Contains("BACKSTORY", prompt);
            Assert.Contains("TEXTING STYLE", prompt);
            // #832: ARCHETYPES (tendency-order ranked list) replaced by
            // ACTIVE ARCHETYPE (the level-eligible top-ranked archetype).
            Assert.Contains("ACTIVE ARCHETYPE", prompt);
            Assert.DoesNotContain("ARCHETYPES (tendency order", prompt);
            Assert.Contains("EFFECTIVE STATS", prompt);
            Assert.Contains("she/her", prompt);
            Assert.Contains("A test bio.", prompt);
        }

        [Fact]
        public void BuildSystemPrompt_NoTraps_NoTrapSection()
        {
            var assembler = new CharacterAssembler(BuildItemRepo(), BuildAnatomyRepo());
            var fragments = assembler.Assemble(
                new[] { "head_cheff" },
                new Dictionary<string, float>(),
                ZeroBaseStats, ZeroShadow);

            var prompt = PromptBuilder.BuildSystemPrompt(
                "TestChar", "she/her", null, fragments, new TrapState());

            int dataStart = prompt.IndexOf("=== CHARACTER DATA ===", System.StringComparison.Ordinal);
            Assert.DoesNotContain("ACTIVE TRAP INSTRUCTIONS", prompt.Substring(dataStart));
        }

        [Fact]
        public void BuildSystemPrompt_ActiveTrap_TrapSectionPresent()
        {
            var assembler = new CharacterAssembler(BuildItemRepo(), BuildAnatomyRepo());
            var fragments = assembler.Assemble(
                new[] { "head_cheff" },
                new Dictionary<string, float>(),
                ZeroBaseStats, ZeroShadow);

            var trapDef = new TrapDefinition(
                "test-trap", StatType.Charm,
                TrapEffect.Disadvantage, 0, 1,
                "Inject try-hard energy into every message.",
                "Self-Awareness DC 12", "");

            var trapState = new TrapState();
            trapState.Activate(trapDef);

            var prompt = PromptBuilder.BuildSystemPrompt(
                "TestChar", "she/her", null, fragments, trapState);

            Assert.Contains("ACTIVE TRAP INSTRUCTIONS", prompt);
            Assert.Contains("Inject try-hard energy", prompt);
        }

        [Fact]
        public void BuildSystemPrompt_NullBio_ShowsNone()
        {
            var assembler = new CharacterAssembler(BuildItemRepo(), BuildAnatomyRepo());
            var fragments = assembler.Assemble(
                new[] { "head_cheff" },
                new Dictionary<string, float>(),
                ZeroBaseStats, ZeroShadow);

            var prompt = PromptBuilder.BuildSystemPrompt(
                "X", "he/him", null, fragments, new TrapState());

            Assert.Contains("Bio: none", prompt);
        }

        // -----------------------------------------------------------------------
        // InterestMeter tests
        // -----------------------------------------------------------------------

        [Fact]
        public void InterestMeter_MaxIs25()
        {
            Assert.Equal(25, InterestMeter.Max);
        }

        [Fact]
        public void InterestMeter_StartingValueIs10()
        {
            Assert.Equal(10, InterestMeter.StartingValue);
        }

        [Fact]
        public void InterestMeter_StartsAt10()
        {
            var meter = new InterestMeter();
            Assert.Equal(10, meter.Current);
        }

        [Fact]
        public void InterestMeter_ApplyPositive()
        {
            var meter = new InterestMeter();
            meter.Apply(5);
            Assert.Equal(15, meter.Current);
        }

        [Fact]
        public void InterestMeter_ClampsAtMin()
        {
            var meter = new InterestMeter();
            meter.Apply(-20);
            Assert.Equal(InterestMeter.Min, meter.Current);
            Assert.True(meter.IsZero);
        }

        [Fact]
        public void InterestMeter_ClampsAtMax()
        {
            var meter = new InterestMeter();
            meter.Apply(20);
            Assert.Equal(InterestMeter.Max, meter.Current);
            Assert.True(meter.IsMaxed);
        }

        // -----------------------------------------------------------------------
        // TimingProfile tests
        // -----------------------------------------------------------------------

        [Fact]
        public void TimingProfile_HighInterest_FasterThanLow()
        {
            var profile = new TimingProfile(60, 0f, 0f, "neutral");  // zero variance
            var dice = new FixedDice(50); // neutral roll

            int delayLow  = profile.ComputeDelay(0,  dice);
            int delayHigh = profile.ComputeDelay(InterestMeter.Max, dice);

            Assert.True(delayHigh < delayLow,
                $"High interest ({delayHigh}) should produce shorter delay than low ({delayLow})");
        }

        [Fact]
        public void TimingProfile_ResultFlooredAtOne()
        {
            var profile = new TimingProfile(0, 0f, 0f, "neutral");
            var dice = new FixedDice(1);

            int delay = profile.ComputeDelay(InterestMeter.Max, dice);
            Assert.True(delay >= 1);
        }
    }
}
