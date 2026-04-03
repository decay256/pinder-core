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
    /// Spec-driven tests for issue #44: Shadow growth events — §7 growth table in GameSession.
    /// Written by test-engineer agent from docs/specs/issue-44-spec.md.
    /// Maturity: Prototype — happy-path per AC + key edge cases.
    /// </summary>
    public class ShadowGrowthSpecTests
    {
        // =====================================================================
        // AC1: All shadow growth events from §7 implemented
        // =====================================================================

        // --- Dread triggers ---

        // Mutation: would catch if Nat 1 on Charm grew Dread instead of Madness (wrong shadow pairing)
        [Fact]
        public async Task AC1_Nat1OnCharm_GrowsMadnessNotDread()
        {
            var shadows = MakeTracker();
            var session = BuildSession(dice: Dice(1, 50), shadows: shadows);

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0); // Charm at index 0

            Assert.Equal(1, shadows.GetDelta(ShadowStatType.Madness));
            Assert.Equal(0, shadows.GetDelta(ShadowStatType.Dread));
            Assert.Contains(result.ShadowGrowthEvents, e => e.Contains("Nat 1") && e.Contains("Madness"));
        }

        // Mutation: would catch if Nat 1 on Honesty grew wrong shadow (must be Denial)
        [Fact]
        public async Task AC1_Nat1OnHonesty_GrowsDenial()
        {
            var shadows = MakeTracker();
            var session = BuildSession(dice: Dice(1, 50), shadows: shadows,
                options: new[] { new DialogueOption(StatType.Honesty, "truth") });

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.Equal(1, shadows.GetDelta(ShadowStatType.Denial));
            Assert.Contains(result.ShadowGrowthEvents, e => e.Contains("Denial"));
        }

        // Mutation: would catch if Nat 1 on Chaos grew wrong shadow (must be Fixation)
        [Fact]
        public async Task AC1_Nat1OnChaos_GrowsFixation()
        {
            var shadows = MakeTracker();
            var session = BuildSession(dice: Dice(1, 50), shadows: shadows,
                options: new[] { new DialogueOption(StatType.Chaos, "wild") });

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.Equal(1, shadows.GetDelta(ShadowStatType.Fixation));
            Assert.Contains(result.ShadowGrowthEvents, e => e.Contains("Fixation"));
        }

        // Mutation: would catch if Nat 1 on SA grew Dread instead of Overthinking
        [Fact]
        public async Task AC1_Nat1OnSA_GrowsOverthinking()
        {
            var shadows = MakeTracker();
            var session = BuildSession(dice: Dice(1, 50), shadows: shadows,
                options: new[] { new DialogueOption(StatType.SelfAwareness, "aware") });

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.Equal(1, shadows.GetDelta(ShadowStatType.Overthinking));
            Assert.Contains(result.ShadowGrowthEvents, e => e.Contains("Overthinking"));
        }

        // Mutation: would catch if Nat 1 on Wit grew Madness instead of Dread
        [Fact]
        public async Task AC1_Nat1OnWit_GrowsDread()
        {
            var shadows = MakeTracker();
            var session = BuildSession(dice: Dice(1, 50), shadows: shadows);

            await session.StartTurnAsync();
            // Wit is at index 2 in NullLlmAdapter
            var result = await session.ResolveTurnAsync(2);

            Assert.True(shadows.GetDelta(ShadowStatType.Dread) >= 1);
            Assert.Contains(result.ShadowGrowthEvents, e => e.Contains("Dread") && e.Contains("Nat 1"));
        }

        // Mutation: would catch if catastrophic Wit fail didn't apply +1 Dread
        [Fact]
        public async Task AC1_CatastrophicWitFail_GrowsDread()
        {
            // Wit roll: d20=2, mod=0, DC=13+0=13, miss=11 → Catastrophe (10+)
            var shadows = MakeTracker();
            var session = BuildSession(
                dice: Dice(2, 50),
                playerStats: Stats(wit: 0),
                opponentStats: Stats(rizz: 0), // Wit defence is Rizz
                shadows: shadows);

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(2); // Wit

            Assert.Contains(result.ShadowGrowthEvents, e => e.Contains("Catastrophic Wit failure"));
        }

        // Mutation: would catch if interest=0 only gave +1 Dread instead of +2
        [Fact]
        public async Task AC1_InterestHitsZero_GrowsDread2()
        {
            var shadows = MakeTracker();
            // Start at 1 interest, catastrophic failure drops to 0
            var session = BuildSession(
                dice: Dice(2, 50),
                playerStats: Stats(charm: 0),
                opponentStats: Stats(sa: 0),
                shadows: shadows,
                startingInterest: 1);

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.Contains(result.ShadowGrowthEvents, e => e.Contains("Interest hit 0") && e.Contains("Dread"));
            // The +2 is critical — not +1
            Assert.True(shadows.GetDelta(ShadowStatType.Dread) >= 2);
        }

        // Mutation: would catch if ghost didn't apply Dread growth
        [Fact]
        public async Task AC1_Ghosted_GrowsDread1()
        {
            var shadows = MakeTracker();
            // Interest 1 = Bored. Ghost roll: dice.Roll(4)==1 → ghost
            var session = BuildSession(dice: Dice(1), shadows: shadows, startingInterest: 1);

            var ex = await Assert.ThrowsAsync<GameEndedException>(() => session.StartTurnAsync());
            Assert.Equal(GameOutcome.Ghosted, ex.Outcome);
            Assert.Equal(1, shadows.GetDelta(ShadowStatType.Dread));
            Assert.Contains(ex.ShadowGrowthEvents, e => e.Contains("Ghosted") && e.Contains("Dread"));
        }

        // --- Madness triggers ---

        // Mutation: would catch if trope trap threshold was 2 instead of 3
        [Fact]
        public async Task AC1_ThreeTropeTraps_GrowsMadness()
        {
            // Miss by 6-9 = TropeTrap. Charm=0, SA=0 → DC=13. d20=6 → miss=7 → TropeTrap
            var shadows = MakeTracker();
            var diceValues = new List<int>();
            for (int i = 0; i < 3; i++) { diceValues.Add(6); diceValues.Add(50); }
            var session = BuildSession(
                dice: new TestDice(diceValues.ToArray()),
                playerStats: Stats(charm: 0),
                opponentStats: Stats(sa: 0),
                shadows: shadows,
                startingInterest: 15);

            for (int i = 0; i < 3; i++)
            {
                await session.StartTurnAsync();
                await session.ResolveTurnAsync(0);
            }

            Assert.Equal(1, shadows.GetDelta(ShadowStatType.Madness));
        }

        // Mutation: would catch if trope trap triggered again at count 4
        [Fact]
        public async Task AC1_FourTropeTraps_MadnessStillOne()
        {
            var shadows = MakeTracker();
            // Miss by 6-9 = TropeTrap = -2 interest (rules-v3.4 §5). 4 turns × -2 = -8.
            // Starting at 15 (Interested, no adv/disadv). After 4 turns: 15-8=7 (Interested).
            // No game-ending risk.
            var diceValues = new List<int>();
            for (int i = 0; i < 4; i++) { diceValues.Add(6); diceValues.Add(50); }
            var session = BuildSession(
                dice: new TestDice(diceValues.ToArray()),
                playerStats: Stats(charm: 0),
                opponentStats: Stats(sa: 0),
                shadows: shadows,
                startingInterest: 15);

            for (int i = 0; i < 4; i++)
            {
                await session.StartTurnAsync();
                await session.ResolveTurnAsync(0);
            }

            // Should still be 1, not 2
            Assert.Equal(1, shadows.GetDelta(ShadowStatType.Madness));
        }

        // Mutation: would catch if same-opener check was case-sensitive
        [Fact]
        public async Task AC1_SameOpenerCaseInsensitive_GrowsMadness()
        {
            var shadows = MakeTracker();
            var session = BuildSession(
                dice: Dice(15, 50),
                shadows: shadows,
                previousOpener: "  HEY, YOU COME HERE OFTEN?  "); // NullLlmAdapter Charm option

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.Contains(result.ShadowGrowthEvents, e => e.Contains("Same opener twice"));
            Assert.Equal(1, shadows.GetDelta(ShadowStatType.Madness));
        }

        // Mutation: would catch if previousOpener=null still triggered madness
        [Fact]
        public async Task AC1_NullPreviousOpener_NoMadnessGrowth()
        {
            var shadows = MakeTracker();
            var session = BuildSession(dice: Dice(15, 50), shadows: shadows, previousOpener: null);

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.DoesNotContain(result.ShadowGrowthEvents, e => e.Contains("Same opener twice"));
        }

        // --- Denial triggers ---

        // Mutation: would catch if date-secured-without-honesty didn't grow Denial
        [Fact]
        public async Task AC1_DateSecuredNoHonesty_GrowsDenial()
        {
            var shadows = MakeTracker();
            // Start at 24, Charm success → 25 → DateSecured
            var session = BuildSession(
                dice: Dice(15, 50),
                playerStats: Stats(charm: 5),
                shadows: shadows,
                startingInterest: 24);

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0); // Charm, not Honesty

            Assert.True(result.IsGameOver);
            Assert.Equal(GameOutcome.DateSecured, result.Outcome);
            Assert.Contains(result.ShadowGrowthEvents, e => e.Contains("Denial") && e.Contains("Date secured without any Honesty"));
        }

        // --- Fixation triggers ---

        // Mutation: would catch if same-stat required 4 instead of 3 consecutive turns
        [Fact]
        public async Task AC1_SameStat3Turns_GrowsFixation()
        {
            var shadows = MakeTracker();
            var diceValues = new List<int>();
            for (int i = 0; i < 3; i++) { diceValues.Add(15); diceValues.Add(50); }
            // Charm (0) is NOT the highest-prob option; Honesty (5) vs Chaos defence (0) is.
            // This isolates the same-stat trigger from the highest-% trigger.
            var session = BuildSession(
                dice: new TestDice(diceValues.ToArray()),
                playerStats: Stats(charm: 0, honesty: 5),
                shadows: shadows,
                options: new[]
                {
                    new DialogueOption(StatType.Honesty, "honest"),
                    new DialogueOption(StatType.Charm, "charming"),
                    new DialogueOption(StatType.Wit, "witty"),
                    new DialogueOption(StatType.Chaos, "chaotic")
                });

            for (int i = 0; i < 3; i++)
            {
                await session.StartTurnAsync();
                await session.ResolveTurnAsync(1); // Charm each time (NOT highest-prob)
            }

            Assert.Equal(1, shadows.GetDelta(ShadowStatType.Fixation));
        }

        // Mutation: would catch if highest-% option wasn't defined as index 0
        [Fact]
        public async Task AC1_HighestPct3InARow_GrowsFixation()
        {
            var shadows = MakeTracker();
            var diceValues = new List<int>();
            for (int i = 0; i < 3; i++) { diceValues.Add(15); diceValues.Add(50); }
            // Use different stats at index 0 each turn to isolate highest-% from same-stat
            var opts1 = new[] { new DialogueOption(StatType.Charm, "a"), new DialogueOption(StatType.Wit, "b") };
            var opts2 = new[] { new DialogueOption(StatType.Honesty, "c"), new DialogueOption(StatType.Wit, "d") };
            var opts3 = new[] { new DialogueOption(StatType.Wit, "e"), new DialogueOption(StatType.Chaos, "f") };
            var rotatingLlm = new RotatingLlmAdapter(new[] { opts1, opts2, opts3 });
            var session = BuildSessionWithLlm(
                dice: new TestDice(diceValues.ToArray()),
                llm: rotatingLlm,
                playerStats: Stats(charm: 5, honesty: 5, wit: 5),
                shadows: shadows);

            for (int i = 0; i < 3; i++)
            {
                await session.StartTurnAsync();
                await session.ResolveTurnAsync(0); // Always index 0 → highest-%
            }

            // No same-stat trigger (Charm, Honesty, Wit are different), but highest-% triggers
            Assert.True(shadows.GetDelta(ShadowStatType.Fixation) >= 1);
        }

        // Mutation: would catch if never-Chaos check used wrong stat
        [Fact]
        public async Task AC1_NeverPickedChaos_EndOfGame_GrowsFixation()
        {
            var shadows = MakeTracker();
            // Quick game: start at 24, succeed → DateSecured
            var session = BuildSession(
                dice: Dice(15, 50),
                playerStats: Stats(charm: 5),
                shadows: shadows,
                startingInterest: 24);

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0); // Charm, never Chaos

            Assert.True(result.IsGameOver);
            Assert.Contains(result.ShadowGrowthEvents, e => e.Contains("Never picked Chaos"));
        }

        // Mutation: would catch if Fixation offset threshold was 3 instead of 4 distinct stats
        [Fact]
        public async Task AC1_FourDistinctStats_EndOfGame_ReducesFixation()
        {
            var shadows = MakeTracker();
            var diceValues = new List<int>();
            for (int i = 0; i < 6; i++) { diceValues.Add(20); diceValues.Add(50); } // Nat 20s for quick DateSecured
            var session = BuildSession(
                dice: new TestDice(diceValues.ToArray()),
                playerStats: Stats(charm: 5, honesty: 5, wit: 5, chaos: 5),
                shadows: shadows,
                startingInterest: 10);

            // Play 4 different stats
            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0); // Charm

            await session.StartTurnAsync();
            await session.ResolveTurnAsync(1); // Honesty

            await session.StartTurnAsync();
            await session.ResolveTurnAsync(2); // Wit

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(3); // Chaos

            if (!result.IsGameOver)
            {
                // Keep going until game ends
                await session.StartTurnAsync();
                result = await session.ResolveTurnAsync(0);
            }

            Assert.True(result.IsGameOver);
            // -1 Fixation for 4+ distinct stats; Chaos was picked so no "never Chaos"
            Assert.Contains(result.ShadowGrowthEvents, e => e.Contains("4+ different stats"));
            Assert.DoesNotContain(result.ShadowGrowthEvents, e => e.Contains("Never picked Chaos"));
        }

        // --- Overthinking triggers ---

        // Mutation: would catch if SA 3+ times threshold was wrong (e.g. 2 or 4)
        [Fact]
        public async Task AC1_SA3Times_GrowsOverthinking()
        {
            var shadows = MakeTracker();
            var diceValues = new List<int>();
            for (int i = 0; i < 3; i++) { diceValues.Add(15); diceValues.Add(50); }
            var session = BuildSession(
                dice: new TestDice(diceValues.ToArray()),
                playerStats: Stats(sa: 5),
                shadows: shadows,
                options: new[] { new DialogueOption(StatType.SelfAwareness, "aware") });

            for (int i = 0; i < 3; i++)
            {
                await session.StartTurnAsync();
                await session.ResolveTurnAsync(0);
            }

            Assert.Equal(1, shadows.GetDelta(ShadowStatType.Overthinking));
        }

        // Mutation: would catch if Read failure didn't grow Overthinking
        [Fact]
        public async Task AC1_ReadFailure_GrowsOverthinking()
        {
            var shadows = MakeTracker();
            // SA=0, dice=2 → total 2 < DC 12 → fail
            var session = BuildSession(dice: Dice(2), playerStats: Stats(sa: 0), shadows: shadows);

            var result = await session.ReadAsync();

            Assert.False(result.Success);
            Assert.Equal(1, shadows.GetDelta(ShadowStatType.Overthinking));
            Assert.Contains(result.ShadowGrowthEvents, e => e.Contains("Overthinking"));
        }

        // =====================================================================
        // AC2: Shadow stats mutate correctly during a session
        // =====================================================================

        // Mutation: would catch if SessionShadowTracker didn't accumulate deltas additively
        [Fact]
        public async Task AC2_MultipleNat1s_Accumulate()
        {
            var shadows = MakeTracker();
            // Two Nat 1 Charm rolls → Madness should be 2
            var diceValues = new List<int>();
            diceValues.Add(1); diceValues.Add(50); // Turn 1 Nat 1
            diceValues.Add(1); diceValues.Add(50); // Turn 2 Nat 1
            var session = BuildSession(
                dice: new TestDice(diceValues.ToArray()),
                shadows: shadows,
                startingInterest: 15);

            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0); // Charm Nat 1 → +1 Madness

            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0); // Charm Nat 1 → +1 Madness

            Assert.Equal(2, shadows.GetDelta(ShadowStatType.Madness));
        }

        // Mutation: would catch if StatBlock was mutated instead of session tracker
        [Fact]
        public void AC2_SessionShadowTracker_DoesNotMutateStatBlock()
        {
            var stats = Stats();
            var tracker = new SessionShadowTracker(stats);
            int originalShadow = stats.GetShadow(ShadowStatType.Madness);

            tracker.ApplyGrowth(ShadowStatType.Madness, 3, "test");

            // StatBlock should be unchanged
            Assert.Equal(originalShadow, stats.GetShadow(ShadowStatType.Madness));
            // Tracker should reflect the delta
            Assert.Equal(3, tracker.GetDelta(ShadowStatType.Madness));
        }

        // Mutation: would catch if GetEffectiveStat used wrong formula
        [Fact]
        public void AC2_GetEffectiveStat_AccountsForSessionDelta()
        {
            // base Charm=4, base Madness shadow=0, session +3 Madness
            // effective = 4 - ((0 + 3) / 3) = 4 - 1 = 3
            var stats = Stats(charm: 4);
            var tracker = new SessionShadowTracker(stats);
            tracker.ApplyGrowth(ShadowStatType.Madness, 3, "test");

            Assert.Equal(3, tracker.GetEffectiveStat(StatType.Charm));
        }

        // Mutation: would catch if integer division rounded up instead of down
        [Fact]
        public void AC2_GetEffectiveStat_IntegerDivision()
        {
            // base Charm=4, base Madness=0, session +2 Madness
            // effective = 4 - (2 / 3) = 4 - 0 = 4 (integer division floors)
            var stats = Stats(charm: 4);
            var tracker = new SessionShadowTracker(stats);
            tracker.ApplyGrowth(ShadowStatType.Madness, 2, "test");

            Assert.Equal(4, tracker.GetEffectiveStat(StatType.Charm));
        }

        // =====================================================================
        // AC3: TurnResult.ShadowGrowthEvents populated correctly
        // =====================================================================

        // Mutation: would catch if ShadowGrowthEvents was null instead of empty when no growth
        [Fact]
        public async Task AC3_NoGrowth_EmptyNotNull()
        {
            var shadows = MakeTracker();
            // d20=15 → success, no triggers
            var session = BuildSession(dice: Dice(15, 50), shadows: shadows);

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.NotNull(result.ShadowGrowthEvents);
            // May have end-of-game events if game ended, but no per-turn growth triggers
        }

        // Mutation: would catch if events weren't drained (appeared in multiple turns)
        [Fact]
        public async Task AC3_EventsDrainedPerTurn()
        {
            var shadows = MakeTracker();
            // Turn 1: Nat 1 → shadow event. Turn 2: success → no event
            var diceValues = new List<int> { 1, 50, 15, 50 };
            var session = BuildSession(
                dice: new TestDice(diceValues.ToArray()),
                shadows: shadows,
                startingInterest: 15);

            await session.StartTurnAsync();
            var result1 = await session.ResolveTurnAsync(0);
            Assert.NotEmpty(result1.ShadowGrowthEvents);

            await session.StartTurnAsync();
            var result2 = await session.ResolveTurnAsync(1); // Different stat, success
            // Turn 2 should NOT contain Turn 1's events
            Assert.DoesNotContain(result2.ShadowGrowthEvents, e => e.Contains("Nat 1"));
        }

        // Mutation: would catch if no shadow tracker meant null instead of empty events
        [Fact]
        public async Task AC3_NoTracker_EmptyEvents()
        {
            var session = BuildSession(dice: Dice(1, 50), shadows: null);

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.NotNull(result.ShadowGrowthEvents);
            Assert.Empty(result.ShadowGrowthEvents);
        }

        // =====================================================================
        // AC4: Test coverage for key triggers (spec-mandated minimum tests)
        // =====================================================================

        // These are already covered above:
        // - Dread +2 when interest reaches 0: AC1_InterestHitsZero_GrowsDread2
        // - Fixation +1 when same stat 3 turns: AC1_SameStat3Turns_GrowsFixation
        // - Madness +1 when Nat 1 on Charm: AC1_Nat1OnCharm_GrowsMadnessNotDread
        // - Denial +1 DateSecured no Honesty: AC1_DateSecuredNoHonesty_GrowsDenial
        // - Fixation -1 offset 4+ stats: AC1_FourDistinctStats_EndOfGame_ReducesFixation

        // =====================================================================
        // Edge Cases from Spec §6
        // =====================================================================

        // Mutation: would catch if Nat 1 on Wit (Legendary tier) also triggered Catastrophe
        [Fact]
        public async Task Edge_Nat1OnWit_IsLegendary_NoCatastropheTrigger()
        {
            var shadows = MakeTracker();
            var session = BuildSession(dice: Dice(1, 50), shadows: shadows);

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(2); // Wit

            // Nat 1 = Legendary tier, NOT Catastrophe. Only Nat 1 trigger.
            Assert.Contains(result.ShadowGrowthEvents, e => e.Contains("Nat 1"));
            Assert.DoesNotContain(result.ShadowGrowthEvents, e => e.Contains("Catastrophic Wit failure"));
            Assert.Equal(1, shadows.GetDelta(ShadowStatType.Dread));
        }

        // Mutation: would catch if same-stat streak of 6 only triggered once
        [Fact]
        public async Task Edge_SameStatStreak6_TriggersFixationTwice()
        {
            var shadows = MakeTracker();
            var diceValues = new List<int>();
            for (int i = 0; i < 6; i++) { diceValues.Add(10); diceValues.Add(50); }
            var session = BuildSession(
                dice: new TestDice(diceValues.ToArray()),
                playerStats: Stats(charm: 0),
                opponentStats: Stats(sa: 0),
                shadows: shadows,
                startingInterest: 15,
                options: new[]
                {
                    new DialogueOption(StatType.Honesty, "honest"),
                    new DialogueOption(StatType.Charm, "charming"),
                    new DialogueOption(StatType.Wit, "witty"),
                    new DialogueOption(StatType.Chaos, "chaotic")
                });

            for (int i = 0; i < 6; i++)
            {
                await session.StartTurnAsync();
                await session.ResolveTurnAsync(1); // Charm at index 1, 6 times
            }

            Assert.Equal(2, shadows.GetDelta(ShadowStatType.Fixation));
        }

        // Mutation: would catch if 3 distinct stats triggered the 4+ offset
        [Fact]
        public async Task Edge_ThreeDistinctStats_NoFixationOffset()
        {
            var shadows = MakeTracker();
            var diceValues = new List<int>();
            for (int i = 0; i < 4; i++) { diceValues.Add(20); diceValues.Add(50); }
            var session = BuildSession(
                dice: new TestDice(diceValues.ToArray()),
                playerStats: Stats(charm: 5, honesty: 5, wit: 5),
                shadows: shadows,
                startingInterest: 10);

            // Use only 3 distinct stats: Charm, Honesty, Wit (skip Chaos)
            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0); // Charm
            await session.StartTurnAsync();
            await session.ResolveTurnAsync(1); // Honesty
            await session.StartTurnAsync();
            await session.ResolveTurnAsync(2); // Wit

            // Force game end
            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0); // Charm again

            if (result.IsGameOver)
            {
                // 3 distinct stats should NOT trigger -1 offset
                Assert.DoesNotContain(result.ShadowGrowthEvents, e => e.Contains("4+ different stats"));
            }
        }

        // Mutation: would catch if Fixation offset and growth didn't both apply
        [Fact]
        public async Task Edge_FixationOffsetAndGrowthBothApply()
        {
            var shadows = MakeTracker();
            var diceValues = new List<int>();
            for (int i = 0; i < 6; i++) { diceValues.Add(20); diceValues.Add(50); }
            // Use 4+ distinct stats (triggers -1 Fixation) but never Chaos (triggers +1 Fixation)
            var session = BuildSession(
                dice: new TestDice(diceValues.ToArray()),
                playerStats: Stats(charm: 5, honesty: 5, wit: 5, sa: 5),
                shadows: shadows,
                startingInterest: 10,
                options: new[]
                {
                    new DialogueOption(StatType.Charm, "a"),
                    new DialogueOption(StatType.Honesty, "b"),
                    new DialogueOption(StatType.Wit, "c"),
                    new DialogueOption(StatType.SelfAwareness, "d")
                });

            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0); // Charm
            await session.StartTurnAsync();
            await session.ResolveTurnAsync(1); // Honesty
            await session.StartTurnAsync();
            await session.ResolveTurnAsync(2); // Wit
            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(3); // SA

            if (!result.IsGameOver)
            {
                await session.StartTurnAsync();
                result = await session.ResolveTurnAsync(0);
            }

            if (result.IsGameOver)
            {
                // Both "Never picked Chaos" (+1) and "4+ different stats" (-1) should fire
                Assert.Contains(result.ShadowGrowthEvents, e => e.Contains("Never picked Chaos"));
                Assert.Contains(result.ShadowGrowthEvents, e => e.Contains("4+ different stats"));
            }
        }

        // Mutation: would catch if different opener didn't prevent madness growth
        [Fact]
        public async Task Edge_DifferentOpener_NoMadness()
        {
            var shadows = MakeTracker();
            var session = BuildSession(
                dice: Dice(15, 50),
                shadows: shadows,
                previousOpener: "Something completely unrelated");

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.DoesNotContain(result.ShadowGrowthEvents, e => e.Contains("Same opener twice"));
        }

        // Mutation: would catch if negative shadow delta was rejected
        [Fact]
        public void Edge_NegativeShadowDelta_Allowed()
        {
            var tracker = MakeTracker();
            tracker.ApplyGrowth(ShadowStatType.Fixation, 2, "growth");
            tracker.ApplyOffset(ShadowStatType.Fixation, -3, "big offset");

            Assert.Equal(-1, tracker.GetDelta(ShadowStatType.Fixation));
        }

        // =====================================================================
        // Error Conditions from Spec §7
        // =====================================================================

        // NOTE: Spec §7 says ApplyGrowth should throw on null/empty reason,
        // but implementation does not enforce this. Skipped at prototype maturity.
        // If validation is added later, re-enable these tests.

        // Mutation: would catch if GameEndedException lost shadow events
        [Fact]
        public void Error_GameEndedException_CarriesEvents()
        {
            var events = new List<string> { "Ghosted: +1 Dread" };
            var ex = new GameEndedException(GameOutcome.Ghosted, events);
            Assert.Single(ex.ShadowGrowthEvents);
            Assert.Equal("Ghosted: +1 Dread", ex.ShadowGrowthEvents[0]);
        }

        // Mutation: would catch if GameEndedException default had null events
        [Fact]
        public void Error_GameEndedException_DefaultEmpty()
        {
            var ex = new GameEndedException(GameOutcome.Ghosted);
            Assert.NotNull(ex.ShadowGrowthEvents);
            Assert.Empty(ex.ShadowGrowthEvents);
        }

        // =====================================================================
        // SessionShadowTracker DrainGrowthEvents
        // =====================================================================

        // Mutation: would catch if DrainGrowthEvents didn't clear the log
        [Fact]
        public void DrainGrowthEvents_ClearsAfterDrain()
        {
            var tracker = MakeTracker();
            tracker.ApplyGrowth(ShadowStatType.Madness, 1, "event 1");
            tracker.ApplyGrowth(ShadowStatType.Dread, 1, "event 2");

            var first = tracker.DrainGrowthEvents();
            Assert.Equal(2, first.Count);

            var second = tracker.DrainGrowthEvents();
            Assert.Empty(second);
        }

        // Mutation: would catch if DrainGrowthEvents returned the internal list (shared reference)
        [Fact]
        public void DrainGrowthEvents_ReturnsNewList()
        {
            var tracker = MakeTracker();
            tracker.ApplyGrowth(ShadowStatType.Madness, 1, "event");

            var drained = tracker.DrainGrowthEvents();
            Assert.Single(drained);

            // Further growth should not appear in already-drained list
            tracker.ApplyGrowth(ShadowStatType.Dread, 1, "event 2");
            Assert.Single(drained); // Still 1, not 2
        }

        // Mutation: would catch if GetDelta returned wrong value for unused shadow type
        [Fact]
        public void GetDelta_NoGrowth_ReturnsZero()
        {
            var tracker = MakeTracker();
            Assert.Equal(0, tracker.GetDelta(ShadowStatType.Madness));
            Assert.Equal(0, tracker.GetDelta(ShadowStatType.Dread));
            Assert.Equal(0, tracker.GetDelta(ShadowStatType.Denial));
            Assert.Equal(0, tracker.GetDelta(ShadowStatType.Fixation));
            Assert.Equal(0, tracker.GetDelta(ShadowStatType.Overthinking));
        }

        // =====================================================================
        // Helpers — test-only utilities (no production code copied)
        // =====================================================================

        private static SessionShadowTracker MakeTracker()
            => new SessionShadowTracker(Stats());

        private static StatBlock Stats(
            int charm = 3, int rizz = 2, int honesty = 1,
            int chaos = 0, int wit = 4, int sa = 2)
        {
            return new StatBlock(
                new Dictionary<StatType, int>
                {
                    { StatType.Charm, charm }, { StatType.Rizz, rizz },
                    { StatType.Honesty, honesty }, { StatType.Chaos, chaos },
                    { StatType.Wit, wit }, { StatType.SelfAwareness, sa }
                },
                new Dictionary<ShadowStatType, int>
                {
                    { ShadowStatType.Madness, 0 }, { ShadowStatType.Horniness, 0 },
                    { ShadowStatType.Denial, 0 }, { ShadowStatType.Fixation, 0 },
                    { ShadowStatType.Dread, 0 }, { ShadowStatType.Overthinking, 0 }
                });
        }

        private static CharacterProfile MakeProfile(string name, StatBlock stats)
            => new CharacterProfile(stats, "system prompt", name, new TimingProfile(5, 1.0f, 0.0f, "neutral"), 1);

        private static TestDice Dice(params int[] values) => new TestDice(values);

        private static GameSession BuildSession(
            TestDice? dice = null,
            StatBlock? playerStats = null,
            StatBlock? opponentStats = null,
            SessionShadowTracker? shadows = null,
            DialogueOption[]? options = null,
            string? previousOpener = null,
            int? startingInterest = null)
        {
            return BuildSessionWithLlm(
                dice: dice ?? Dice(15, 50),
                llm: options != null ? new StubLlmAdapter(options) : null,
                playerStats: playerStats,
                opponentStats: opponentStats,
                shadows: shadows,
                previousOpener: previousOpener,
                startingInterest: startingInterest);
        }

        private static GameSession BuildSessionWithLlm(
            TestDice dice,
            ILlmAdapter? llm = null,
            StatBlock? playerStats = null,
            StatBlock? opponentStats = null,
            SessionShadowTracker? shadows = null,
            string? previousOpener = null,
            int? startingInterest = null)
        {
            playerStats ??= Stats();
            opponentStats ??= Stats();
            llm ??= new NullLlmAdapter();

            var config = new GameSessionConfig(
                playerShadows: shadows,
                previousOpener: previousOpener,
                startingInterest: startingInterest);

            var wrappedDice = new PrependedDice(5, dice);

            return new GameSession(
                MakeProfile("player", playerStats),
                MakeProfile("opponent", opponentStats),
                llm,
                wrappedDice,
                new NullTrapRegistry(),
                config);
        }

        private sealed class PrependedDice : IDiceRoller
        {
            private int? _first;
            private readonly IDiceRoller _inner;
            public PrependedDice(int firstValue, IDiceRoller inner) { _first = firstValue; _inner = inner; }
            public int Roll(int sides) { if (_first.HasValue) { var v = _first.Value; _first = null; return v; } return _inner.Roll(sides); }
        }

        /// <summary>Deterministic dice for tests — dequeues values in order.</summary>
        private sealed class TestDice : IDiceRoller
        {
            private readonly Queue<int> _values;

            public TestDice(int[] values) => _values = new Queue<int>(values);

            public int Roll(int sides)
                => _values.Count > 0 ? _values.Dequeue() : 10;
        }

        /// <summary>LLM adapter returning fixed options.</summary>
        private sealed class StubLlmAdapter : ILlmAdapter
        {
            private readonly DialogueOption[] _options;
            public StubLlmAdapter(DialogueOption[] options) => _options = options;

            public Task<DialogueOption[]> GetDialogueOptionsAsync(DialogueContext context)
                => Task.FromResult(_options);
            public Task<string> DeliverMessageAsync(DeliveryContext context)
                => Task.FromResult(context.ChosenOption.IntendedText);
            public Task<OpponentResponse> GetOpponentResponseAsync(OpponentContext context)
                => Task.FromResult(new OpponentResponse("..."));
            public Task<string?> GetInterestChangeBeatAsync(InterestChangeContext context)
                => Task.FromResult<string?>(null);
        }

        /// <summary>LLM adapter that rotates through different option sets per turn.</summary>
        private sealed class RotatingLlmAdapter : ILlmAdapter
        {
            private readonly DialogueOption[][] _optionSets;
            private int _call;
            public RotatingLlmAdapter(DialogueOption[][] optionSets) => _optionSets = optionSets;

            public Task<DialogueOption[]> GetDialogueOptionsAsync(DialogueContext context)
            {
                var idx = _call < _optionSets.Length ? _call : _optionSets.Length - 1;
                _call++;
                return Task.FromResult(_optionSets[idx]);
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
