using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Pinder.Core.Characters;
using Pinder.Core.Conversation;
using Pinder.Core.Interfaces;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;
using Pinder.Core.Traps;
using Xunit;

namespace Pinder.Core.Tests
{
    /// <summary>
    /// Tests for shadow threshold effects on gameplay (#45).
    /// Covers: Dread T3 starting interest, T2 disadvantage, Denial T3 option removal,
    /// Fixation T3 forced stat, ShadowThresholds in DialogueContext, backward compatibility.
    /// Maturity: Prototype (happy-path tests).
    /// </summary>
    [Trait("Category", "Core")]
    public partial class ShadowThresholdGameSessionTests
    {
        // ============== AC5: Dread T3 → Starting Interest 8 ==============

        [Fact]
        public async Task DreadT3_StartsAt8Interest()
        {
            var shadows = TestHelpers.MakeShadowTracker(dread: 18);
            var session = MakeSession(
                diceValues: new[] { 15, 50 },
                shadows: shadows);

            var turn = await session.StartTurnAsync();
            Assert.Equal(8, turn.State.Interest);
        }

        [Fact]
        public async Task DreadT2_StartsAt10Interest()
        {
            // Dread at 12 (T2) should NOT change starting interest
            var shadows = TestHelpers.MakeShadowTracker(dread: 12);
            var session = MakeSession(
                diceValues: new[] { 15, 50 },
                shadows: shadows);

            var turn = await session.StartTurnAsync();
            Assert.Equal(10, turn.State.Interest);
        }

        [Fact]
        public async Task ExplicitStartingInterest_OverridesDreadT3()
        {
            // If config has explicit StartingInterest, that takes priority
            var shadows = TestHelpers.MakeShadowTracker(dread: 20);
            var session = MakeSession(
                diceValues: new[] { 15, 50 },
                shadows: shadows,
                startingInterest: 5);

            var turn = await session.StartTurnAsync();
            Assert.Equal(5, turn.State.Interest);
        }

        // ============== AC2: T2 shadow check (not disadvantage) — #755 ==============
        // T2 generic disadvantage is removed. Shadow check IS the mechanic.

        [Fact]
        public async Task DenialT2_HonestyNoLongerRollsWithDisadvantage()
        {
            // Denial at 12 → shadow check fires, but NO roll disadvantage per #755
            var shadows = TestHelpers.MakeShadowTracker(denial: 12);
            // Single d20 roll (no disadvantage)
            var session = MakeSession(
                diceValues: new[] { 18, 50 },
                shadows: shadows,
                llmOptions: new[] { new DialogueOption(StatType.Honesty, "Let me be honest...") });

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            // Single roll used (not the lower of two)
            Assert.Equal(18, result.Roll.UsedDieRoll);
        }

        [Fact]
        public async Task DreadT2_WitNoLongerRollsWithDisadvantage()
        {
            var shadows = TestHelpers.MakeShadowTracker(dread: 14);
            // Single d20 roll per #755
            var session = MakeSession(
                diceValues: new[] { 19, 50 },
                shadows: shadows,
                llmOptions: new[] { new DialogueOption(StatType.Wit, "Witty remark") });

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.Equal(19, result.Roll.UsedDieRoll);
        }

        [Fact]
        public async Task MadnessT2_CharmNoLongerRollsWithDisadvantage()
        {
            var shadows = TestHelpers.MakeShadowTracker(madness: 15);
            // Single d20 roll per #755
            var session = MakeSession(
                diceValues: new[] { 17, 50 },
                shadows: shadows,
                llmOptions: new[] { new DialogueOption(StatType.Charm, "Hey there") });

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.Equal(17, result.Roll.UsedDieRoll);
        }

        [Fact]
        public async Task NoShadowDisadvantage_WhenBelowT2()
        {
            // Denial at 11 (T1) should NOT cause disadvantage
            var shadows = TestHelpers.MakeShadowTracker(denial: 11);
            // Only one d20 roll needed (no disadvantage = single roll)
            var session = MakeSession(
                diceValues: new[] { 15, 50 },
                shadows: shadows,
                llmOptions: new[] { new DialogueOption(StatType.Honesty, "Honestly...") });

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            // Should use the single roll value
            Assert.Equal(15, result.Roll.UsedDieRoll);
        }

        [Fact]
        public async Task UnpairedStat_NoDisadvantage_WhenOtherShadowAtT2()
        {
            // Denial at 12 affects Honesty, NOT Charm
            var shadows = TestHelpers.MakeShadowTracker(denial: 12);
            var session = MakeSession(
                diceValues: new[] { 15, 50 },
                shadows: shadows,
                llmOptions: new[] { new DialogueOption(StatType.Charm, "Charming!") });

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            // Charm is not affected by Denial → single roll
            Assert.Equal(15, result.Roll.UsedDieRoll);
        }

        // ============== AC3: Denial T3 → Honesty options removed ==============

        [Fact]
        public async Task DenialT3_RemovesHonestyOptions()
        {
            var shadows = TestHelpers.MakeShadowTracker(denial: 18);
            var session = MakeSession(
                diceValues: new[] { 15, 50 },
                shadows: shadows,
                llmOptions: new[]
                {
                    new DialogueOption(StatType.Charm, "Hi"),
                    new DialogueOption(StatType.Honesty, "Truth"),
                    new DialogueOption(StatType.Wit, "Clever")
                });

            var turn = await session.StartTurnAsync();

            // Honesty option should be removed
            Assert.Equal(2, turn.Options.Length);
            Assert.DoesNotContain(turn.Options, o => o.Stat == StatType.Honesty);
            Assert.Contains(turn.Options, o => o.Stat == StatType.Charm);
            Assert.Contains(turn.Options, o => o.Stat == StatType.Wit);
        }

        [Fact]
        public async Task DenialT3_AllHonesty_FallbackToChaos()
        {
            var shadows = TestHelpers.MakeShadowTracker(denial: 19);
            var session = MakeSession(
                diceValues: new[] { 15, 50 },
                shadows: shadows,
                llmOptions: new[]
                {
                    new DialogueOption(StatType.Honesty, "Truth 1"),
                    new DialogueOption(StatType.Chaos, "Wild"),
                    new DialogueOption(StatType.Honesty, "Truth 2")
                });

            var turn = await session.StartTurnAsync();

            // Only the Chaos option should remain
            Assert.Single(turn.Options);
            Assert.Equal(StatType.Chaos, turn.Options[0].Stat);
        }

        [Fact]
        public async Task DenialT3_AllHonestyNoChaos_KeepsFirst()
        {
            var shadows = TestHelpers.MakeShadowTracker(denial: 20);
            var session = MakeSession(
                diceValues: new[] { 15, 50 },
                shadows: shadows,
                llmOptions: new[]
                {
                    new DialogueOption(StatType.Honesty, "Truth 1"),
                    new DialogueOption(StatType.Honesty, "Truth 2"),
                    new DialogueOption(StatType.Honesty, "Truth 3")
                });

            var turn = await session.StartTurnAsync();

            // Fallback: keep first option (no Chaos available)
            Assert.Single(turn.Options);
            Assert.Equal("Truth 1", turn.Options[0].IntendedText);
        }

        // ============== AC4: Fixation T3 → Forced stat ==============

        [Fact]
        public async Task FixationT3_ForcesSameStatAsLastTurn()
        {
            var shadows = TestHelpers.MakeShadowTracker(fixation: 18);
            // Turn 1: pick Charm (index 0). Turn 2: all options forced to Charm.
            // Dice: turn1 d20=15, delay=50, turn2 d20=15, delay=50
            var session = MakeSession(
                diceValues: new[] { 15, 50, 15, 50 },
                shadows: shadows,
                llmOptions: new[]
                {
                    new DialogueOption(StatType.Charm, "Hey"),
                    new DialogueOption(StatType.Wit, "Clever"),
                    new DialogueOption(StatType.Honesty, "Truth")
                });

            // Turn 1
            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0); // picks Charm

            // Turn 2: all options should be forced to Charm
            var turn2 = await session.StartTurnAsync();
            Assert.All(turn2.Options, o => Assert.Equal(StatType.Charm, o.Stat));
        }

        [Fact]
        public async Task FixationT3_FirstTurn_NoForce()
        {
            var shadows = TestHelpers.MakeShadowTracker(fixation: 20);
            var session = MakeSession(
                diceValues: new[] { 15, 50 },
                shadows: shadows,
                llmOptions: new[]
                {
                    new DialogueOption(StatType.Charm, "Hey"),
                    new DialogueOption(StatType.Wit, "Clever"),
                    new DialogueOption(StatType.Honesty, "Truth")
                });

            var turn = await session.StartTurnAsync();

            // First turn: no forced stat
            Assert.Equal(StatType.Charm, turn.Options[0].Stat);
            Assert.Equal(StatType.Wit, turn.Options[1].Stat);
            Assert.Equal(StatType.Honesty, turn.Options[2].Stat);
        }

        // ============== AC7: ShadowThresholds populated in DialogueContext ==============

        [Fact]
        public async Task ShadowThresholds_PopulatedInDialogueContext()
        {
            Dictionary<ShadowStatType, int>? capturedThresholds = null;
            var shadows = TestHelpers.MakeShadowTracker(dread: 14, denial: 6, fixation: 0);

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
            // #307: shadowThresholds now carries raw shadow values, not tier (0-3)
            Assert.Equal(14, capturedThresholds![ShadowStatType.Dread]);     // raw value 14
            Assert.Equal(6, capturedThresholds[ShadowStatType.Denial]);      // raw value 6
            Assert.Equal(0, capturedThresholds[ShadowStatType.Fixation]);    // raw value 0
        }

        // ============== AC9: Omitted tracker is synthesized from player stats ==============

        [Fact]
        public async Task OmittedShadowTracker_UsesPlayerStatsWithDefaultBehavior()
        {
            var session = MakeSession(
                diceValues: new[] { 15, 50 },
                shadows: null);

            var turn = await session.StartTurnAsync();

            Assert.Equal(10, turn.State.Interest);
            Assert.Equal(4, turn.Options.Length);
            Assert.NotNull(session.State.PlayerShadows);
            Assert.Equal(0, session.State.PlayerShadows!.GetEffectiveShadow(ShadowStatType.Dread));
        }

        [Fact]
        public async Task OmittedShadowTracker_DialogueContextHasPlayerThresholds()
        {
            Dictionary<ShadowStatType, int>? capturedThresholds = null;
            bool contextChecked = false;

            var llm = new CapturingLlmAdapter(ctx =>
            {
                capturedThresholds = ctx.ShadowThresholds;
                contextChecked = true;
            });

            var session = MakeSessionWithLlm(
                diceValues: new[] { 15, 50 },
                shadows: null,
                llm: llm);

            await session.StartTurnAsync();

            Assert.True(contextChecked);
            Assert.NotNull(capturedThresholds);
            Assert.Equal(0, capturedThresholds![ShadowStatType.Dread]);
            Assert.Equal(0, capturedThresholds[ShadowStatType.Denial]);
        }
    }
}
