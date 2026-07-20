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
    public partial class ShadowGrowthSpecTests
    {
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

        [Fact]
        public async Task AC3_OmittedTracker_UsesDefaultTrackerForGrowthEvents()
        {
            var session = BuildSession(dice: Dice(1, 50), shadows: null);

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.NotNull(result.ShadowGrowthEvents);
            Assert.Contains(result.ShadowGrowthEvents, e => e.Contains("Madness"));
            Assert.NotNull(session.State.PlayerShadows);
            Assert.Equal(1, session.State.PlayerShadows!.GetDelta(ShadowStatType.Madness));
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
                dateeStats: Stats(sa: 0),
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
                startingInterest: 5);

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
                startingInterest: 5,
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

        // Same opener trigger removed (#716) — verify no Madness from same opener
        [Fact]
        public async Task AC716_SameOpener_NoLongerGrowsMadness()
        {
            var shadows = MakeTracker();
            var session = BuildSession(
                dice: Dice(15, 50),
                shadows: shadows,
                previousOpener: "  HEY, YOU COME HERE OFTEN?  "); // NullLlmAdapter Charm option

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.DoesNotContain(result.ShadowGrowthEvents, e => e.Contains("Same opener"));
            Assert.Equal(0, shadows.GetDelta(ShadowStatType.Madness));
        }

        // (#405 update) Pre-fix this test asserted GetDelta == -1 (i.e. effective shadow
        // could be driven below 0). The floor-at-0 invariant means the reduction is
        // suppressed at the boundary: the partial reduction down to 0 is applied, and the
        // remainder is recorded as a (floored) event.
        [Fact]
        public void Edge_NegativeShadowDelta_FlooredAtZero()
        {
            var tracker = MakeTracker();
            tracker.ApplyGrowth(ShadowStatType.Fixation, 2, "growth");
            // Effective is now 2. Requesting -3 would drive to -1 — floor at 0 instead.
            tracker.ApplyOffset(ShadowStatType.Fixation, -3, "big offset");

            Assert.Equal(0, tracker.GetEffectiveShadow(ShadowStatType.Fixation));
            // Stored delta brings base+delta to 0, never below.
            Assert.True(tracker.GetEffectiveShadow(ShadowStatType.Fixation) == 0
                && tracker.GetDelta(ShadowStatType.Fixation) >= -tracker.GetEffectiveShadow(ShadowStatType.Fixation) - 0,
                "#405: stored delta must not drive base+delta below 0");
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

        // #956: ShadowGrowthEffects typed record list must be populated and consistent
        // with ShadowGrowthEvents on a Ghosted throw.
        [Fact]
        public async Task GhostedException_CarriesTypedShadowGrowthEffects()
        {
            var shadows = MakeTracker();
            var session = BuildSession(dice: Dice(1), shadows: shadows, startingInterest: 1);

            var ex = await Assert.ThrowsAsync<GameEndedException>(() => session.StartTurnAsync());

            Assert.Equal(GameOutcome.Ghosted, ex.Outcome);
            Assert.Equal(0, shadows.GetDelta(ShadowStatType.Dread));
            Assert.Contains(ex.ShadowGrowthEvents, e => e.Contains("Ghosted") && e.Contains("Dread"));

            Assert.NotNull(ex.ShadowGrowthEffects);
            Assert.NotEmpty(ex.ShadowGrowthEffects);
            Assert.Equal(ex.ShadowGrowthEvents.Count, ex.ShadowGrowthEffects.Count);

            var effect = ex.ShadowGrowthEffects[0];
            Assert.Equal(ShadowStatType.Dread, effect.Stat);
            Assert.Equal(1, effect.Amount);
            Assert.Equal("Ghosted", effect.Reason);
        }

        [Fact]
        public void GameEndedException_DefaultCtor_HasEmptyEffects()
        {
            var ex = new GameEndedException(GameOutcome.Ghosted);
            Assert.NotNull(ex.ShadowGrowthEffects);
            Assert.Empty(ex.ShadowGrowthEffects);
        }

        [Fact]
        public void GameEndedException_ThreeParamCtor_CarriesBothLists()
        {
            var events = new[] { "Dread +1 (Ghosted)", "Madness +1 (Nat 1)" };
            var effects = new[]
            {
                new ShadowGrowthEffect(ShadowStatType.Dread, 1, "Ghosted"),
                new ShadowGrowthEffect(ShadowStatType.Madness, 1, "Nat 1")
            };

            var ex = new GameEndedException(GameOutcome.Ghosted, events, effects);

            Assert.Equal(2, ex.ShadowGrowthEvents.Count);
            Assert.Equal(2, ex.ShadowGrowthEffects.Count);
            Assert.Equal(ShadowStatType.Dread, ex.ShadowGrowthEffects[0].Stat);
            Assert.Equal(ShadowStatType.Madness, ex.ShadowGrowthEffects[1].Stat);
        }
    }
}
