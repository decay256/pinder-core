using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Pinder.Core.Conversation;
using Pinder.Core.Stats;
using Xunit;

namespace Pinder.Core.Tests
{
    public partial class ShadowThresholdGameSessionTests
    {
        // ============== Edge: Multiple T2 — #755: no longer causes disadvantage ==============

        [Fact]
        public async Task MultipleT2_NoLongerBothStatsGetDisadvantage()
        {
            // #755: T2 shadow fires shadow check, not roll disadvantage
            var shadows = TestHelpers.MakeShadowTracker(dread: 12, denial: 12);

            // Single d20 roll (no disadvantage)
            var session = MakeSession(
                diceValues: new[] { 19, 50 },
                shadows: shadows,
                llmOptions: new[] { new DialogueOption(StatType.Honesty, "Truth") });

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);
            Assert.Equal(19, result.Roll.UsedDieRoll);
        }

        // ============== Edge: Advantage from VeryIntoIt still applies ==============

        [Fact]
        public async Task AdvantageAndShadowDisadvantage_Cancel()
        {
            // #755: T2 no longer causes disadvantage, so advantage from VeryIntoIt applies cleanly.
            var shadows = TestHelpers.MakeShadowTracker(denial: 14);
            var session = MakeSession(
                diceValues: new[] { 8, 14, 50 },
                shadows: shadows,
                startingInterest: 18, // VeryIntoIt → advantage
                llmOptions: new[] { new DialogueOption(StatType.Honesty, "Truth") });

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            // Only advantage active → takes higher of 8 and 14 = 14
            Assert.Equal(14, result.Roll.UsedDieRoll);
        }

        // ============== #307: Shadow taint uses raw values ==============

        [Fact]
        public async Task ShadowThresholds_StoresRawValues_NotTiers()
        {
            // Madness=8 is T1 (tier=1). Context should get raw value 8, not tier 1.
            Dictionary<ShadowStatType, int>? capturedThresholds = null;
            var shadows = TestHelpers.MakeShadowTracker(madness: 8);

            var llm = new CapturingLlmAdapter(ctx =>
            {
                capturedThresholds = ctx.ShadowThresholds;
            });

            var session = MakeSessionWithLlm(
                diceValues: new[] { 15, 50 },
                shadows: shadows,
                llm: llm);

            await session.StartTurnAsync();

            Assert.NotNull(capturedThresholds);
            // Raw value must be passed, not tier
            Assert.Equal(8, capturedThresholds![ShadowStatType.Madness]);
        }

        [Fact]
        public async Task ShadowThresholds_T0Value_PassedAsRawZero()
        {
            // Madness=3 is T0. Context should get raw value 3.
            Dictionary<ShadowStatType, int>? capturedThresholds = null;
            var shadows = TestHelpers.MakeShadowTracker(madness: 3);

            var llm = new CapturingLlmAdapter(ctx =>
            {
                capturedThresholds = ctx.ShadowThresholds;
            });

            var session = MakeSessionWithLlm(
                diceValues: new[] { 15, 50 },
                shadows: shadows,
                llm: llm);

            await session.StartTurnAsync();

            Assert.NotNull(capturedThresholds);
            Assert.Equal(3, capturedThresholds![ShadowStatType.Madness]);
        }

        [Fact]
        public async Task FixationT3_StillTriggersAt18RawValue()
        {
            // Fixation=18 (T3) should still force all options to last stat used
            var shadows = TestHelpers.MakeShadowTracker(fixation: 18);
            var options = new[]
            {
                new DialogueOption(StatType.Charm, "Hey"),
                new DialogueOption(StatType.Wit, "Clever"),
                new DialogueOption(StatType.Honesty, "Truth")
            };

            var session = MakeSession(
                diceValues: new[] { 15, 15, 50, 15, 50 },
                shadows: shadows,
                llmOptions: options);

            // First turn: no last stat used, so no fixation filtering
            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0); // plays Charm

            // Second turn: Fixation T3 should force all options to Charm
            var turn2 = await session.StartTurnAsync();
            Assert.All(turn2.Options, o => Assert.Equal(StatType.Charm, o.Stat));
        }

        [Fact]
        public async Task DenialT3_StillTriggersAt18RawValue()
        {
            // Denial=18 (T3) should still remove Honesty options
            var shadows = TestHelpers.MakeShadowTracker(denial: 18);
            var options = new[]
            {
                new DialogueOption(StatType.Charm, "Hey"),
                new DialogueOption(StatType.Honesty, "Truth"),
                new DialogueOption(StatType.Wit, "Clever")
            };

            var session = MakeSession(
                diceValues: new[] { 15, 50 },
                shadows: shadows,
                llmOptions: options);

            var turn = await session.StartTurnAsync();

            // Honesty options should be filtered out
            Assert.DoesNotContain(turn.Options, o => o.Stat == StatType.Honesty);
        }

        // ============== #310: Madness T3 → One option marked unhinged ==============

        [Fact]
        public async Task MadnessT3_MarksExactlyOneOptionAsUnhinged()
        {
            // Madness=18 (T3) → exactly one option IsUnhingedReplacement = true
            var shadows = TestHelpers.MakeShadowTracker(madness: 18);
            var options = new[]
            {
                new DialogueOption(StatType.Charm, "Hey"),
                new DialogueOption(StatType.Wit, "Clever"),
                new DialogueOption(StatType.Honesty, "Truth")
            };

            // Dice queue: [5(horniness), 2(Madness Roll(3)→idx 1)]
            var session = MakeSession(
                diceValues: new[] { 2 },
                shadows: shadows,
                llmOptions: options);

            var turn = await session.StartTurnAsync();

            int unhingedCount = turn.Options.Count(o => o.IsUnhingedReplacement);
            Assert.Equal(1, unhingedCount);
        }

        [Fact]
        public async Task MadnessT2_NoOptionsMarkedUnhinged()
        {
            // Madness=12 (T2) → no options should be unhinged
            var shadows = TestHelpers.MakeShadowTracker(madness: 12);
            var options = new[]
            {
                new DialogueOption(StatType.Charm, "Hey"),
                new DialogueOption(StatType.Wit, "Clever"),
                new DialogueOption(StatType.Honesty, "Truth")
            };

            var session = MakeSession(
                diceValues: new[] { 10 },
                shadows: shadows,
                llmOptions: options);

            var turn = await session.StartTurnAsync();

            Assert.All(turn.Options, o => Assert.False(o.IsUnhingedReplacement));
        }

        [Fact]
        public async Task MadnessT3_PreservesOriginalStatAndText()
        {
            // The unhinged option should keep its original stat and text
            var shadows = TestHelpers.MakeShadowTracker(madness: 20);
            var options = new[]
            {
                new DialogueOption(StatType.Charm, "Hey babe"),
                new DialogueOption(StatType.Wit, "Clever line")
            };

            // Dice queue: [5(horniness), 1(Madness Roll(2)→idx 0)]
            var session = MakeSession(
                diceValues: new[] { 1 },
                shadows: shadows,
                llmOptions: options);

            var turn = await session.StartTurnAsync();

            var unhinged = turn.Options.Single(o => o.IsUnhingedReplacement);
            Assert.Equal(StatType.Charm, unhinged.Stat);
            Assert.Equal("Hey babe", unhinged.IntendedText);
        }

        [Fact]
        public async Task MadnessT3_WithHorninessT3_PreservesUnhingedFlag()
        {
            // Both Madness T3 and Despair T3 active (session Horniness converts to Rizz)
            // but should preserve the IsUnhingedReplacement flag.
            var shadows = TestHelpers.MakeShadowTracker(madness: 18);
            var options = new[]
            {
                new DialogueOption(StatType.Charm, "Hey"),
                new DialogueOption(StatType.Wit, "Clever")
            };

            // Dice: [5(horniness→5, no T3), 1(Madness Roll(2)→idx 0)]
            var session = MakeSession(
                diceValues: new[] { 1 },
                shadows: shadows,
                llmOptions: options);

            var turn = await session.StartTurnAsync();

            // One option should be unhinged and keep original stat
            var unhinged = turn.Options.Single(o => o.IsUnhingedReplacement);
            Assert.Equal(StatType.Charm, unhinged.Stat);
        }

        [Fact]
        public async Task MadnessBelow18_NoUnhingedOptions()
        {
            // Madness=17 (just below T3 threshold) → no unhinged
            var shadows = TestHelpers.MakeShadowTracker(madness: 17);
            var options = new[]
            {
                new DialogueOption(StatType.Charm, "Hey"),
                new DialogueOption(StatType.Wit, "Clever")
            };

            var session = MakeSession(
                diceValues: new[] { 10 },
                shadows: shadows,
                llmOptions: options);

            var turn = await session.StartTurnAsync();

            Assert.All(turn.Options, o => Assert.False(o.IsUnhingedReplacement));
        }

        [Fact]
        public void DialogueOption_IsUnhingedReplacement_DefaultsFalse()
        {
            var opt = new DialogueOption(StatType.Charm, "Hey");
            Assert.False(opt.IsUnhingedReplacement);
        }
    }
}
