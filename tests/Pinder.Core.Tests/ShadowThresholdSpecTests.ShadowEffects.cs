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
    public partial class ShadowThresholdSpecTests
    {
        // =====================================================================
        // AC3: Denial ≥18 → Honesty options removed
        // =====================================================================

        // Mutation: would catch if Honesty options are not filtered out
        [Fact]
        public async Task AC3_DenialT3_RemovesHonestyOptions()
        {
            // What: Denial ≥18 → Honesty options removed post-LLM (spec §3.4)
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

            Assert.Equal(2, turn.Options.Length);
            Assert.DoesNotContain(turn.Options, o => o.Stat == StatType.Honesty);
        }

        // Mutation: would catch if Denial T2 incorrectly removes Honesty options (only T3 should)
        [Fact]
        public async Task AC3_DenialT2_DoesNotRemoveHonestyOptions()
        {
            // What: Denial=17 (T2) should NOT remove Honesty options, only disadvantage
            var shadows = TestHelpers.MakeShadowTracker(denial: 17);
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

            Assert.Equal(3, turn.Options.Length);
            Assert.Contains(turn.Options, o => o.Stat == StatType.Honesty);
        }

        // Edge case 6.2: all options are Honesty → fallback to Chaos
        // Mutation: would catch if empty option set is returned instead of fallback
        [Fact]
        public async Task AC3_DenialT3_AllHonesty_FallsBackToChaos()
        {
            // What: All Honesty options + Denial T3 → keep Chaos fallback (spec §6.2)
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

            Assert.Single(turn.Options);
            Assert.Equal(StatType.Chaos, turn.Options[0].Stat);
        }

        // Edge case 6.2: all Honesty, no Chaos → keep first option
        // Mutation: would catch if implementation throws or returns empty
        [Fact]
        public async Task AC3_DenialT3_AllHonestyNoChaos_KeepsFirst()
        {
            // What: All Honesty, no Chaos available → keep first option as fallback (spec §6.2)
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

            Assert.Single(turn.Options);
        }

        // =====================================================================
        // AC4: Fixation ≥18 → Forced stat (same as last turn)
        // =====================================================================

        // Mutation: would catch if Fixation T3 doesn't override option stats
        [Fact]
        public async Task AC4_FixationT3_ForcesLastUsedStat()
        {
            // What: Fixation ≥18 → all options forced to same stat as last turn (spec §3.4)
            var shadows = TestHelpers.MakeShadowTracker(fixation: 18);
            // Turn 1 dice: d20=15, delay=50; Turn 2 dice: d20=15, delay=50
            var session = MakeSession(
                diceValues: new[] { 15, 50, 15, 50 },
                shadows: shadows,
                llmOptions: new[]
                {
                    new DialogueOption(StatType.Charm, "Hey"),
                    new DialogueOption(StatType.Wit, "Clever"),
                    new DialogueOption(StatType.Honesty, "Truth")
                });

            // Turn 1: pick Charm
            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);

            // Turn 2: all options should be forced to Charm
            var turn2 = await session.StartTurnAsync();
            Assert.All(turn2.Options, o => Assert.Equal(StatType.Charm, o.Stat));
        }

        // Edge case 6.3: first turn with Fixation T3 → no forced stat
        // Mutation: would catch if implementation forces stat on first turn (null lastStatUsed)
        [Fact]
        public async Task AC4_FixationT3_FirstTurn_NoForce()
        {
            // What: First turn, no previous stat → options returned as-is (spec §6.3)
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

            // First turn: options unchanged
            Assert.Equal(StatType.Charm, turn.Options[0].Stat);
            Assert.Equal(StatType.Wit, turn.Options[1].Stat);
            Assert.Equal(StatType.Honesty, turn.Options[2].Stat);
        }

        // Mutation: would catch if Fixation T2 incorrectly forces stat (only T3 should)
        [Fact]
        public async Task AC4_FixationT2_DoesNotForceStat()
        {
            // What: Fixation=17 (T2) should NOT force stat, only T3 does
            var shadows = TestHelpers.MakeShadowTracker(fixation: 17);
            var session = MakeSession(
                diceValues: new[] { 15, 50, 15, 50 },
                shadows: shadows,
                llmOptions: new[]
                {
                    new DialogueOption(StatType.Charm, "Hey"),
                    new DialogueOption(StatType.Wit, "Clever"),
                    new DialogueOption(StatType.Honesty, "Truth")
                });

            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0); // pick Charm

            var turn2 = await session.StartTurnAsync();

            // T2 should NOT force stat — Wit and Honesty should remain
            Assert.Contains(turn2.Options, o => o.Stat == StatType.Wit);
        }

        // =====================================================================
        // AC7: ShadowThresholds populated in DialogueContext
        // =====================================================================

        // Mutation: would catch if ShadowThresholds not populated or wrong values
        [Fact]
        public async Task AC7_ShadowThresholds_PopulatedInDialogueContext()
        {
            // What: StartTurnAsync populates DialogueContext.ShadowThresholds (spec §2.3)
            Dictionary<ShadowStatType, int>? captured = null;
            var shadows = TestHelpers.MakeShadowTracker(dread: 14, denial: 6, fixation: 0);

            var llm = new CapturingLlmAdapter(ctx => { captured = ctx.ShadowThresholds; });
            var session = MakeSessionWithLlm(diceValues: new[] { 15, 50 }, shadows: shadows, llm: llm);

            await session.StartTurnAsync();

            Assert.NotNull(captured);
            // #307: shadowThresholds now carries raw shadow values, not tier (0-3)
            Assert.Equal(14, captured![ShadowStatType.Dread]);      // raw value 14
            Assert.Equal(6, captured[ShadowStatType.Denial]);       // raw value 6
            Assert.Equal(0, captured[ShadowStatType.Fixation]);     // raw value 0
        }

        // Mutation: would catch if all 6 shadow stats are not included in dictionary
        [Fact]
        public async Task AC7_ShadowThresholds_ContainsAllSixStats()
        {
            // What: All 6 shadow stats must be present in the threshold dictionary
            Dictionary<ShadowStatType, int>? captured = null;
            var shadows = TestHelpers.MakeShadowTracker();

            var llm = new CapturingLlmAdapter(ctx => { captured = ctx.ShadowThresholds; });
            var session = MakeSessionWithLlm(diceValues: new[] { 15, 50 }, shadows: shadows, llm: llm);

            await session.StartTurnAsync();

            Assert.NotNull(captured);
            Assert.Equal(6, captured!.Count);
            Assert.True(captured.ContainsKey(ShadowStatType.Dread));
            Assert.True(captured.ContainsKey(ShadowStatType.Madness));
            Assert.True(captured.ContainsKey(ShadowStatType.Denial));
            Assert.True(captured.ContainsKey(ShadowStatType.Fixation));
            Assert.True(captured.ContainsKey(ShadowStatType.Overthinking));
            Assert.True(captured.ContainsKey(ShadowStatType.Despair));
        }

        // =====================================================================
        // AC6/AC9: No effects when SessionShadowTracker is null (backward compat)
        // =====================================================================

        // Mutation: would catch if null tracker causes crash or changes interest
        [Fact]
        public async Task AC9_NoShadowTracker_InterestStartsAt10()
        {
            // What: No tracker → default interest 10 (spec §7.1, Example 9)
            var session = MakeSession(diceValues: new[] { 15, 50 }, shadows: null);

            var turn = await session.StartTurnAsync();
            Assert.Equal(10, turn.State.Interest);
        }

        // Mutation: would catch if null tracker doesn't set ShadowThresholds to null
        [Fact]
        public async Task AC9_NoShadowTracker_ThresholdsAreNull()
        {
            // What: No tracker → ShadowThresholds is null in DialogueContext (spec §7.1)
            Dictionary<ShadowStatType, int>? captured = null;
            bool checked_ = false;
            var llm = new CapturingLlmAdapter(ctx =>
            {
                captured = ctx.ShadowThresholds;
                checked_ = true;
            });
            var session = MakeSessionWithLlm(diceValues: new[] { 15, 50 }, shadows: null, llm: llm);

            await session.StartTurnAsync();

            Assert.True(checked_);
            Assert.Null(captured);
        }

        // Mutation: would catch if null tracker still triggers option filtering
        [Fact]
        public async Task AC9_NoShadowTracker_AllOptionsReturned()
        {
            // What: No tracker → no option filtering (spec §7.1)
            var session = MakeSession(diceValues: new[] { 15, 50 }, shadows: null);

            var turn = await session.StartTurnAsync();
            // NullLlmAdapter returns 4 options by default
            Assert.Equal(4, turn.Options.Length);
        }

        // Error condition 7.4: config is non-null but tracker is null
        // Mutation: would catch if config.PlayerShadows being null is not handled
        [Fact]
        public async Task AC9_ConfigWithNullTracker_BehavesLikeNoTracker()
        {
            // What: GameSessionConfig exists but PlayerShadows is null → skip all (spec §7.4)
            var config = new GameSessionConfig(clock: TestHelpers.MakeClock(), playerShadows: null);
            var session = new GameSession(
                MakeProfile("player"),
                MakeProfile("datee"),
                new NullLlmAdapter(),
                new QueueDice(new[] { 15, 50 }),
                new EmptyTrapRegistry(),
                config);

            var turn = await session.StartTurnAsync();
            Assert.Equal(10, turn.State.Interest);
        }

        // =====================================================================
        // Edge case 6.4: Multiple T2 shadows simultaneously
        // =====================================================================

        // #755: T2 generic disadvantage removed. Multiple T2 shadows fire shadow checks, not roll disadvantage.
        [Fact]
        public async Task Edge_MultipleT2_NoLongerCausesDisadvantage()
        {
            // What: Dread T2 + Denial T2 → shadow checks fire but NO roll disadvantage per #755
            var shadows = TestHelpers.MakeShadowTracker(dread: 12, denial: 12);

            // Test Honesty: single roll (no disadvantage)
            var session = MakeSession(
                diceValues: new[] { 19, 50 },
                shadows: shadows,
                llmOptions: new[] { new DialogueOption(StatType.Honesty, "Truth") });

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);
            Assert.Equal(19, result.Roll.UsedDieRoll);
        }

        // =====================================================================
        // Edge case 6.5/6.6: Advantage + Disadvantage cancel
        // =====================================================================

        // #755: T2 disadvantage removed. Advantage from VeryIntoIt still applies (no cancel needed).
        [Fact]
        public async Task Edge_AdvantageAndShadowDisadvantage_Cancel()
        {
            // What: VeryIntoIt (advantage) + Denial T2 → only advantage applies (T2 no longer causes disadvantage per #755)
            // With advantage, rolls twice and takes higher.
            var shadows = TestHelpers.MakeShadowTracker(denial: 14);
            var session = MakeSession(
                diceValues: new[] { 8, 14, 50 },
                shadows: shadows,
                startingInterest: 18, // VeryIntoIt → advantage
                llmOptions: new[] { new DialogueOption(StatType.Honesty, "Truth") });

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            // Advantage (no disadvantage from T2) → takes higher of 8 and 14 = 14
            Assert.Equal(14, result.Roll.UsedDieRoll);
        }

        // =====================================================================
        // Spec Example 8: Multiple shadow thresholds active simultaneously
        // =====================================================================

        // Mutation: would catch if threshold dictionary miscomputes mixed-tier values
        [Fact]
        public async Task Example8_MultipleThresholds_CorrectTiers()
        {
            // What: Dread=14(T2), Denial=6(T1), Fixation=18(T3) → correct tiers (spec Example 8)
            Dictionary<ShadowStatType, int>? captured = null;
            var shadows = TestHelpers.MakeShadowTracker(dread: 14, denial: 6, fixation: 18);
            var llm = new CapturingLlmAdapter(ctx => { captured = ctx.ShadowThresholds; });
            var session = MakeSessionWithLlm(diceValues: new[] { 15, 50 }, shadows: shadows, llm: llm);

            await session.StartTurnAsync();

            Assert.NotNull(captured);
            // #307: shadowThresholds now carries raw shadow values
            Assert.Equal(14, captured![ShadowStatType.Dread]);
            Assert.Equal(6, captured[ShadowStatType.Denial]);
            Assert.Equal(18, captured[ShadowStatType.Fixation]);
            Assert.Equal(0, captured[ShadowStatType.Madness]);
            Assert.Equal(0, captured[ShadowStatType.Overthinking]);
            Assert.Equal(0, captured[ShadowStatType.Despair]);
        }
    }
}
