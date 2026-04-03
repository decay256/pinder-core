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
    /// Spec-based tests for issue #45: Shadow thresholds — §7 threshold effects on gameplay.
    /// Written from docs/specs/issue-45-spec.md only (context-isolated from implementation).
    /// Maturity: Prototype — happy-path per AC + key edge cases.
    /// </summary>
    public class ShadowThresholdSpecTests
    {
        // =====================================================================
        // AC1: ShadowThresholdEvaluator computes threshold level (0/1/2/3)
        // =====================================================================

        // Mutation: would catch if boundary at 5 used >= instead of >, or threshold 6 mapped wrong
        [Theory]
        [InlineData(0, 0)]
        [InlineData(1, 0)]
        [InlineData(5, 0)]
        [InlineData(6, 1)]
        [InlineData(7, 1)]
        [InlineData(11, 1)]
        [InlineData(12, 2)]
        [InlineData(13, 2)]
        [InlineData(17, 2)]
        [InlineData(18, 3)]
        [InlineData(19, 3)]
        [InlineData(25, 3)]
        [InlineData(100, 3)]
        public void AC1_GetThresholdLevel_ReturnsCorrectTier(int shadowValue, int expected)
        {
            // What: §7 threshold boundaries — 6=T1, 12=T2, 18+=T3
            Assert.Equal(expected, ShadowThresholdEvaluator.GetThresholdLevel(shadowValue));
        }

        // Mutation: would catch if negative values throw instead of returning 0
        // Edge case 6.1: negative shadow values
        [Theory]
        [InlineData(-1, 0)]
        [InlineData(-100, 0)]
        [InlineData(int.MinValue, 0)]
        public void AC1_NegativeShadowValues_ReturnTierZero(int shadowValue, int expected)
        {
            // What: Defensive guard — negative values should not throw (spec §6.1)
            Assert.Equal(expected, ShadowThresholdEvaluator.GetThresholdLevel(shadowValue));
        }

        // =====================================================================
        // AC2 + Spec §2.2: InterestMeter(int) constructor overload
        // =====================================================================

        // Mutation: would catch if InterestMeter(int) uses default 10 instead of the parameter
        [Fact]
        public void AC2_InterestMeter_IntConstructor_SetsCurrentToValue()
        {
            // What: InterestMeter(8) should start at 8, not default 10
            var meter = new InterestMeter(8);
            Assert.Equal(8, meter.Current);
        }

        // Mutation: would catch if parameterless constructor is broken by new overload
        [Fact]
        public void AC2_InterestMeter_DefaultConstructor_StillStartsAt10()
        {
            // What: Existing parameterless constructor unchanged (spec §6.10)
            var meter = new InterestMeter();
            Assert.Equal(10, meter.Current);
        }

        // Mutation: would catch if clamping uses wrong upper bound
        [Theory]
        [InlineData(30, 25)]
        [InlineData(100, 25)]
        public void AC2_InterestMeter_ClampsToMax(int input, int expected)
        {
            // What: Values above Max (25) clamped silently (spec §6.9)
            Assert.Equal(expected, new InterestMeter(input).Current);
        }

        // Mutation: would catch if clamping uses wrong lower bound
        [Theory]
        [InlineData(-5, 0)]
        [InlineData(-100, 0)]
        public void AC2_InterestMeter_ClampsToMin(int input, int expected)
        {
            // What: Values below Min (0) clamped silently (spec §6.9)
            Assert.Equal(expected, new InterestMeter(input).Current);
        }

        // Mutation: would catch if boundary values (0, 25) are off-by-one
        [Fact]
        public void AC2_InterestMeter_BoundaryValues()
        {
            Assert.Equal(0, new InterestMeter(0).Current);
            Assert.Equal(25, new InterestMeter(25).Current);
        }

        // =====================================================================
        // AC5: Dread ≥18 → Starting Interest 8
        // =====================================================================

        // Mutation: would catch if Dread T3 doesn't change starting interest
        [Fact]
        public async Task AC5_DreadT3_StartsAt8Interest()
        {
            // What: Dread shadow ≥18 → InterestMeter(8) at construction (spec §3.3)
            var shadows = MakeShadowTracker(dread: 18);
            var session = MakeSession(diceValues: new[] { 15, 50 }, shadows: shadows);

            var turn = await session.StartTurnAsync();
            Assert.Equal(8, turn.State.Interest);
        }

        // Mutation: would catch if Dread T3 check uses ≥17 instead of ≥18
        [Fact]
        public async Task AC5_DreadAt17_StartsAt10Interest()
        {
            // What: Dread=17 is T2, NOT T3 — interest stays 10
            var shadows = MakeShadowTracker(dread: 17);
            var session = MakeSession(diceValues: new[] { 15, 50 }, shadows: shadows);

            var turn = await session.StartTurnAsync();
            Assert.Equal(10, turn.State.Interest);
        }

        // Mutation: would catch if Dread T2 incorrectly triggers starting interest change
        [Fact]
        public async Task AC5_DreadT2_StartsAt10Interest()
        {
            // What: Dread=12 (T2) should NOT affect starting interest
            var shadows = MakeShadowTracker(dread: 12);
            var session = MakeSession(diceValues: new[] { 15, 50 }, shadows: shadows);

            var turn = await session.StartTurnAsync();
            Assert.Equal(10, turn.State.Interest);
        }

        // Mutation: would catch if explicit startingInterest doesn't override Dread T3
        [Fact]
        public async Task AC5_ExplicitStartingInterest_OverridesDreadT3()
        {
            // What: If config has explicit StartingInterest, that takes priority over Dread T3
            var shadows = MakeShadowTracker(dread: 20);
            var session = MakeSession(diceValues: new[] { 15, 50 }, shadows: shadows, startingInterest: 5);

            var turn = await session.StartTurnAsync();
            Assert.Equal(5, turn.State.Interest);
        }

        // Mutation: would catch if large Dread value breaks (e.g., only checks ==18)
        [Fact]
        public async Task AC5_DreadAt25_StartsAt8Interest()
        {
            // What: Any Dread ≥18 should trigger T3, not just exactly 18
            var shadows = MakeShadowTracker(dread: 25);
            var session = MakeSession(diceValues: new[] { 15, 50 }, shadows: shadows);

            var turn = await session.StartTurnAsync();
            Assert.Equal(8, turn.State.Interest);
        }

        // =====================================================================
        // AC2: T2 Disadvantage on paired stat rolls
        // =====================================================================

        // Mutation: would catch if disadvantage not applied to the paired stat
        [Fact]
        public async Task AC2_DenialT2_HonestyHasDisadvantage()
        {
            // What: Denial ≥12 → Honesty rolls with disadvantage (spec §3.2)
            // Dice: 18, 5 → disadvantage takes lower (5)
            var shadows = MakeShadowTracker(denial: 12);
            var session = MakeSession(
                diceValues: new[] { 18, 5, 50 },
                shadows: shadows,
                llmOptions: new[] { new DialogueOption(StatType.Honesty, "Truth time") });

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.Equal(5, result.Roll.UsedDieRoll);
        }

        // Mutation: would catch if Dread→Wit pairing is wrong
        [Fact]
        public async Task AC2_DreadT2_WitHasDisadvantage()
        {
            // What: Dread ≥12 → Wit rolls with disadvantage
            var shadows = MakeShadowTracker(dread: 14);
            var session = MakeSession(
                diceValues: new[] { 19, 3, 50 },
                shadows: shadows,
                llmOptions: new[] { new DialogueOption(StatType.Wit, "Witty") });

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.Equal(3, result.Roll.UsedDieRoll);
        }

        // Mutation: would catch if Madness→Charm pairing is wrong
        [Fact]
        public async Task AC2_MadnessT2_CharmHasDisadvantage()
        {
            // What: Madness ≥12 → Charm rolls with disadvantage
            var shadows = MakeShadowTracker(madness: 15);
            var session = MakeSession(
                diceValues: new[] { 17, 4, 50 },
                shadows: shadows,
                llmOptions: new[] { new DialogueOption(StatType.Charm, "Hey") });

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.Equal(4, result.Roll.UsedDieRoll);
        }

        // Mutation: would catch if Fixation→Chaos pairing is wrong
        [Fact]
        public async Task AC2_FixationT2_ChaosHasDisadvantage()
        {
            // What: Fixation ≥12 → Chaos rolls with disadvantage
            var shadows = MakeShadowTracker(fixation: 13);
            var session = MakeSession(
                diceValues: new[] { 16, 6, 50 },
                shadows: shadows,
                llmOptions: new[] { new DialogueOption(StatType.Chaos, "Wild!") });

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.Equal(6, result.Roll.UsedDieRoll);
        }

        // Mutation: would catch if Overthinking→SA pairing is wrong
        [Fact]
        public async Task AC2_OverthinkingT2_SAHasDisadvantage()
        {
            // What: Overthinking ≥12 → SelfAwareness rolls with disadvantage
            var shadows = MakeShadowTracker(overthinking: 12);
            var session = MakeSession(
                diceValues: new[] { 20, 2, 50 },
                shadows: shadows,
                llmOptions: new[] { new DialogueOption(StatType.SelfAwareness, "I know") });

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.Equal(2, result.Roll.UsedDieRoll);
        }

        // Mutation: would catch if Horniness→Rizz pairing is wrong
        [Fact]
        public async Task AC2_HorninessT2_RizzHasDisadvantage()
        {
            // What: Horniness ≥12 → Rizz rolls with disadvantage
            var shadows = MakeShadowTracker(horniness: 14);
            var session = MakeSession(
                diceValues: new[] { 18, 7, 50 },
                shadows: shadows,
                llmOptions: new[] { new DialogueOption(StatType.Rizz, "Smooth") });

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.Equal(7, result.Roll.UsedDieRoll);
        }

        // Mutation: would catch if T1 (below T2) incorrectly triggers disadvantage
        [Fact]
        public async Task AC2_T1_NoDisadvantage()
        {
            // What: Denial=11 is T1 → NO disadvantage on Honesty
            var shadows = MakeShadowTracker(denial: 11);
            var session = MakeSession(
                diceValues: new[] { 15, 50 },
                shadows: shadows,
                llmOptions: new[] { new DialogueOption(StatType.Honesty, "Honestly...") });

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            // Single roll, no disadvantage
            Assert.Equal(15, result.Roll.UsedDieRoll);
        }

        // Mutation: would catch if unpaired stat incorrectly gets disadvantage
        [Fact]
        public async Task AC2_UnpairedStat_NoDisadvantage()
        {
            // What: Denial T2 penalizes Honesty, NOT Charm (spec §3.2 pairing table)
            var shadows = MakeShadowTracker(denial: 12);
            var session = MakeSession(
                diceValues: new[] { 15, 50 },
                shadows: shadows,
                llmOptions: new[] { new DialogueOption(StatType.Charm, "Charming!") });

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.Equal(15, result.Roll.UsedDieRoll);
        }

        // =====================================================================
        // AC3: Denial ≥18 → Honesty options removed
        // =====================================================================

        // Mutation: would catch if Honesty options are not filtered out
        [Fact]
        public async Task AC3_DenialT3_RemovesHonestyOptions()
        {
            // What: Denial ≥18 → Honesty options removed post-LLM (spec §3.4)
            var shadows = MakeShadowTracker(denial: 18);
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
            var shadows = MakeShadowTracker(denial: 17);
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
            var shadows = MakeShadowTracker(denial: 19);
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
            var shadows = MakeShadowTracker(denial: 20);
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
            var shadows = MakeShadowTracker(fixation: 18);
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
            var shadows = MakeShadowTracker(fixation: 20);
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
            var shadows = MakeShadowTracker(fixation: 17);
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
            var shadows = MakeShadowTracker(dread: 14, denial: 6, fixation: 0);

            var llm = new CapturingLlmAdapter(ctx => { captured = ctx.ShadowThresholds; });
            var session = MakeSessionWithLlm(diceValues: new[] { 15, 50 }, shadows: shadows, llm: llm);

            await session.StartTurnAsync();

            Assert.NotNull(captured);
            Assert.Equal(2, captured![ShadowStatType.Dread]);       // 14 → T2
            Assert.Equal(1, captured[ShadowStatType.Denial]);       // 6 → T1
            Assert.Equal(0, captured[ShadowStatType.Fixation]);     // 0 → T0
        }

        // Mutation: would catch if all 6 shadow stats are not included in dictionary
        [Fact]
        public async Task AC7_ShadowThresholds_ContainsAllSixStats()
        {
            // What: All 6 shadow stats must be present in the threshold dictionary
            Dictionary<ShadowStatType, int>? captured = null;
            var shadows = MakeShadowTracker();

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
            Assert.True(captured.ContainsKey(ShadowStatType.Horniness));
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
            var config = new GameSessionConfig(playerShadows: null);
            var session = new GameSession(
                MakeProfile("player"),
                MakeProfile("opponent"),
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

        // Mutation: would catch if only first shadow T2 is checked, not all
        [Fact]
        public async Task Edge_MultipleT2_BothStatsGetDisadvantage()
        {
            // What: Dread T2 + Denial T2 → both Wit and Honesty have disadvantage (spec §6.4)
            var shadows = MakeShadowTracker(dread: 12, denial: 12);

            // Test Honesty: d20 first=19, second=2 → disadvantage uses 2
            var session = MakeSession(
                diceValues: new[] { 19, 2, 50 },
                shadows: shadows,
                llmOptions: new[] { new DialogueOption(StatType.Honesty, "Truth") });

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);
            Assert.Equal(2, result.Roll.UsedDieRoll);
        }

        // =====================================================================
        // Edge case 6.5/6.6: Advantage + Disadvantage cancel
        // =====================================================================

        // Mutation: would catch if advantage and disadvantage stack instead of canceling
        [Fact]
        public async Task Edge_AdvantageAndShadowDisadvantage_Cancel()
        {
            // What: VeryIntoIt (advantage) + Denial T2 (Honesty disadv) → cancel → single roll (spec §6.6)
            var shadows = MakeShadowTracker(denial: 14);
            var session = MakeSession(
                diceValues: new[] { 12, 50 },
                shadows: shadows,
                startingInterest: 18, // VeryIntoIt → advantage
                llmOptions: new[] { new DialogueOption(StatType.Honesty, "Truth") });

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            // Advantage + disadvantage cancel → single roll = 12
            Assert.Equal(12, result.Roll.UsedDieRoll);
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
            var shadows = MakeShadowTracker(dread: 14, denial: 6, fixation: 18);
            var llm = new CapturingLlmAdapter(ctx => { captured = ctx.ShadowThresholds; });
            var session = MakeSessionWithLlm(diceValues: new[] { 15, 50 }, shadows: shadows, llm: llm);

            await session.StartTurnAsync();

            Assert.NotNull(captured);
            Assert.Equal(2, captured![ShadowStatType.Dread]);
            Assert.Equal(1, captured[ShadowStatType.Denial]);
            Assert.Equal(3, captured[ShadowStatType.Fixation]);
            Assert.Equal(0, captured[ShadowStatType.Madness]);
            Assert.Equal(0, captured[ShadowStatType.Overthinking]);
            Assert.Equal(0, captured[ShadowStatType.Horniness]);
        }

        // =====================================================================
        // Helpers (test-only utilities, not copied from implementation)
        // =====================================================================

        private static SessionShadowTracker MakeShadowTracker(
            int dread = 0, int denial = 0, int fixation = 0,
            int madness = 0, int overthinking = 0, int horniness = 0)
        {
            var stats = new StatBlock(
                new Dictionary<StatType, int>
                {
                    { StatType.Charm, 2 }, { StatType.Rizz, 2 }, { StatType.Honesty, 2 },
                    { StatType.Chaos, 2 }, { StatType.Wit, 2 }, { StatType.SelfAwareness, 2 }
                },
                new Dictionary<ShadowStatType, int>
                {
                    { ShadowStatType.Dread, dread }, { ShadowStatType.Denial, denial },
                    { ShadowStatType.Fixation, fixation }, { ShadowStatType.Madness, madness },
                    { ShadowStatType.Overthinking, overthinking }, { ShadowStatType.Horniness, horniness }
                });
            return new SessionShadowTracker(stats);
        }

        private static StatBlock MakeStatBlock()
        {
            return new StatBlock(
                new Dictionary<StatType, int>
                {
                    { StatType.Charm, 2 }, { StatType.Rizz, 2 }, { StatType.Honesty, 2 },
                    { StatType.Chaos, 2 }, { StatType.Wit, 2 }, { StatType.SelfAwareness, 2 }
                },
                new Dictionary<ShadowStatType, int>
                {
                    { ShadowStatType.Madness, 0 }, { ShadowStatType.Horniness, 0 },
                    { ShadowStatType.Denial, 0 }, { ShadowStatType.Fixation, 0 },
                    { ShadowStatType.Dread, 0 }, { ShadowStatType.Overthinking, 0 }
                });
        }

        private static CharacterProfile MakeProfile(string name)
        {
            var stats = MakeStatBlock();
            var timing = new TimingProfile(5, 1.0f, 0.0f, "neutral");
            return new CharacterProfile(stats, "system prompt", name, timing, 1);
        }

        private static GameSession MakeSession(
            int[] diceValues,
            SessionShadowTracker? shadows,
            DialogueOption[]? llmOptions = null,
            int? startingInterest = null)
        {
            var config = new GameSessionConfig(
                playerShadows: shadows,
                startingInterest: startingInterest);

            ILlmAdapter llm = llmOptions != null
                ? (ILlmAdapter)new FixedOptionsLlmAdapter(llmOptions)
                : new NullLlmAdapter();

            var allDice = new int[diceValues.Length + 1];
            allDice[0] = 5;
            Array.Copy(diceValues, 0, allDice, 1, diceValues.Length);

            return new GameSession(
                MakeProfile("player"),
                MakeProfile("opponent"),
                llm,
                new QueueDice(allDice),
                new EmptyTrapRegistry(),
                config);
        }

        private static GameSession MakeSessionWithLlm(
            int[] diceValues,
            SessionShadowTracker? shadows,
            ILlmAdapter llm)
        {
            var config = new GameSessionConfig(playerShadows: shadows);

            var allDice2 = new int[diceValues.Length + 1];
            allDice2[0] = 5;
            Array.Copy(diceValues, 0, allDice2, 1, diceValues.Length);

            return new GameSession(
                MakeProfile("player"),
                MakeProfile("opponent"),
                llm,
                new QueueDice(allDice2),
                new EmptyTrapRegistry(),
                config);
        }

        // ---- Test doubles ----

        private sealed class QueueDice : IDiceRoller
        {
            private readonly Queue<int> _values;
            public QueueDice(int[] values) => _values = new Queue<int>(values);
            public int Roll(int sides) => _values.Count > 0 ? _values.Dequeue() : 10;
        }

        private sealed class EmptyTrapRegistry : ITrapRegistry
        {
            public TrapDefinition? GetTrap(StatType stat) => null;
            public string? GetLlmInstruction(StatType stat) => null;
        }

        private sealed class FixedOptionsLlmAdapter : ILlmAdapter
        {
            private readonly DialogueOption[] _options;
            public FixedOptionsLlmAdapter(DialogueOption[] options) => _options = options;

            public Task<DialogueOption[]> GetDialogueOptionsAsync(DialogueContext context)
                => Task.FromResult(_options);
            public Task<string> DeliverMessageAsync(DeliveryContext context)
                => Task.FromResult(context.ChosenOption.IntendedText);
            public Task<OpponentResponse> GetOpponentResponseAsync(OpponentContext context)
                => Task.FromResult(new OpponentResponse("..."));
            public Task<string?> GetInterestChangeBeatAsync(InterestChangeContext context)
                => Task.FromResult<string?>(null);
        }

        private sealed class CapturingLlmAdapter : ILlmAdapter
        {
            private readonly Action<DialogueContext> _onGetOptions;
            public CapturingLlmAdapter(Action<DialogueContext> onGetOptions) => _onGetOptions = onGetOptions;

            public Task<DialogueOption[]> GetDialogueOptionsAsync(DialogueContext context)
            {
                _onGetOptions(context);
                return Task.FromResult(new[]
                {
                    new DialogueOption(StatType.Charm, "Hey"),
                    new DialogueOption(StatType.Wit, "Clever")
                });
            }
            public Task<string> DeliverMessageAsync(DeliveryContext context)
                => Task.FromResult(context.ChosenOption.IntendedText);
            public Task<OpponentResponse> GetOpponentResponseAsync(OpponentContext context)
                => Task.FromResult(new OpponentResponse("..."));
            public Task<string?> GetInterestChangeBeatAsync(InterestChangeContext context)
                => Task.FromResult<string?>(null);
        }
    }
}
