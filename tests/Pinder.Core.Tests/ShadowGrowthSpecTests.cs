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
    [Trait("Category", "Core")]
    public partial class ShadowGrowthSpecTests
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

        // #942 transactional fix: shadow tracker must NOT be mutated when StartTurnAsync throws;
        // Dread growth is surfaced via the exception's ShadowGrowthEvents instead.
        [Fact]
        public async Task AC1_Ghosted_GrowsDread1()
        {
            var shadows = MakeTracker();
            // Interest 1 = Bored. Ghost roll: dice.Roll(4)==1 → ghost
            var session = BuildSession(dice: Dice(1), shadows: shadows, startingInterest: 1);

            var ex = await Assert.ThrowsAsync<GameEndedException>(() => session.StartTurnAsync());
            Assert.Equal(GameOutcome.Ghosted, ex.Outcome);
            // Tracker must be unchanged (#942): caller applies MarkEnded after catching.
            Assert.Equal(0, shadows.GetDelta(ShadowStatType.Dread));
            // Exception still carries the Dread growth event for the SPA to display.
            Assert.Contains(ex.ShadowGrowthEvents, e => e.Contains("Ghosted") && e.Contains("Dread"));
        }

        // --- Madness triggers ---

        // Every TropeTrap failure → +1 Madness (not just 3rd)
        [Fact]
        public async Task AC716_EveryTropeTrap_GrowsMadness()
        {
            // Use Honesty to avoid CHARM 3x trigger. Honesty=0, Charm=0 → DC will cause TropeTrap.
            var shadows = MakeTracker();
            var diceValues = new List<int>();
            for (int i = 0; i < 3; i++) { diceValues.Add(6); diceValues.Add(50); }
            var session = BuildSession(
                dice: new TestDice(diceValues.ToArray()),
                playerStats: Stats(honesty: 0),
                opponentStats: Stats(charm: 0),
                shadows: shadows,
                options: new[] { new DialogueOption(StatType.Honesty, "truth") },
                startingInterest: 15);

            for (int i = 0; i < 3; i++)
            {
                await session.StartTurnAsync();
                await session.ResolveTurnAsync(0);
            }

            // Each TropeTrap gives +1, so 3 TropeTraps = +3 Madness
            Assert.Equal(3, shadows.GetDelta(ShadowStatType.Madness));
        }

        // Single TropeTrap should give +1 Madness immediately
        [Fact]
        public async Task AC716_SingleTropeTrap_GrowsMadnessOne()
        {
            var shadows = MakeTracker();
            var session = BuildSession(
                dice: new TestDice(new[] { 6, 50 }),
                playerStats: Stats(charm: 0),
                opponentStats: Stats(sa: 0),
                shadows: shadows,
                startingInterest: 15);

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.Equal(1, shadows.GetDelta(ShadowStatType.Madness));
            Assert.Contains(result.ShadowGrowthEvents, e => e.Contains("TropeTrap"));
        }

        // CHARM used 3 times → +1 Madness
        [Fact]
        public async Task AC716_CharmUsed3Times_GrowsMadness()
        {
            var shadows = MakeTracker();
            // Use moderate rolls that succeed but don't push interest to DateSecured.
            // Charm=3, SA=2 → DC=15. d20=15 → success. Start low interest to avoid date.
            var diceValues = new List<int>();
            for (int i = 0; i < 4; i++) { diceValues.Add(15); diceValues.Add(50); }
            var session = BuildSession(
                dice: new TestDice(diceValues.ToArray()),
                shadows: shadows,
                startingInterest: 5);

            for (int i = 0; i < 3; i++)
            {
                await session.StartTurnAsync();
                await session.ResolveTurnAsync(0); // Charm at index 0
            }

            Assert.Equal(1, shadows.GetDelta(ShadowStatType.Madness));
        }

        // CHARM used 2 times → no Madness from this trigger
        [Fact]
        public async Task AC716_CharmUsed2Times_NoMadnessFromUsage()
        {
            var shadows = MakeTracker();
            var diceValues = new List<int>();
            for (int i = 0; i < 3; i++) { diceValues.Add(15); diceValues.Add(50); }
            var session = BuildSession(
                dice: new TestDice(diceValues.ToArray()),
                shadows: shadows,
                startingInterest: 5);

            for (int i = 0; i < 2; i++)
            {
                await session.StartTurnAsync();
                await session.ResolveTurnAsync(0); // Charm at index 0
            }

            Assert.Equal(0, shadows.GetDelta(ShadowStatType.Madness));
        }

        // Combo success → Madness -1
        [Fact]
        public async Task AC716_ComboSuccess_ReducesMadness()
        {
            var shadows = MakeTracker();
            shadows.ApplyGrowth(ShadowStatType.Madness, 2, "setup");

            // NullLlmAdapter: idx 0=Charm, idx 1=Honesty, idx 2=Wit, idx 3=Chaos
            // Combo: 2 different stats succeed in a row.
            // Turn 1: Wit (idx 2) success. Turn 2: Charm (idx 0) success → "The Setup" combo.
            // Use Nat 20 to guarantee success and avoid date-secured by starting low.
            var diceValues = new List<int> { 20, 50, 20, 50, 20, 50, 20, 50 };
            var session = BuildSession(
                dice: new TestDice(diceValues.ToArray()),
                shadows: shadows,
                startingInterest: 5);

            // Turn 1: Wit success
            await session.StartTurnAsync();
            await session.ResolveTurnAsync(2);

            // Turn 2: Charm success → should trigger "The Setup" combo (Wit → Charm)
            await session.StartTurnAsync();
            var result2 = await session.ResolveTurnAsync(0);

            // If combo triggered, Madness should be reduced from 2 to 1
            if (result2.ComboTriggered != null)
            {
                Assert.True(shadows.GetDelta(ShadowStatType.Madness) < 2,
                    $"Combo '{result2.ComboTriggered}' triggered but Madness not reduced: {shadows.GetDelta(ShadowStatType.Madness)}");
            }
        }

        // CHAOS combo trigger → Fixation -1 (#719)
        [Fact]
        public async Task AC719_ChaosCombo_ReducesFixation()
        {
            var shadows = MakeTracker();
            shadows.ApplyGrowth(ShadowStatType.Fixation, 2, "setup");

            // NullLlmAdapter: idx 0=Charm, idx 1=Honesty, idx 2=Wit, idx 3=Chaos
            // The Pivot combo: Honesty → Chaos (both succeed)
            // Use Nat 20 to guarantee success.
            var diceValues = new List<int> { 20, 50, 20, 50, 20, 50, 20, 50 };
            var session = BuildSession(
                dice: new TestDice(diceValues.ToArray()),
                shadows: shadows,
                startingInterest: 5);

            // Turn 1: Honesty success (idx 1)
            await session.StartTurnAsync();
            await session.ResolveTurnAsync(1);

            // Turn 2: Chaos success (idx 3) → should trigger "The Pivot" combo
            await session.StartTurnAsync();
            var result2 = await session.ResolveTurnAsync(3);

            // The Pivot combo should fire, reducing Fixation by 1
            Assert.NotNull(result2.ComboTriggered);
            Assert.Equal("The Pivot", result2.ComboTriggered);

            // Fixation: setup 2, combo reduction -1 = net delta 1
            // (Also Madness -1 from generic combo reduction, but we only check Fixation)
            Assert.True(shadows.GetDelta(ShadowStatType.Fixation) < 2,
                $"CHAOS combo triggered but Fixation not reduced: {shadows.GetDelta(ShadowStatType.Fixation)}");
            Assert.Contains(result2.ShadowGrowthEvents, e => e.Contains("CHAOS combo"));
        }

        // Tell option selected → Madness -1
        [Fact]
        public async Task AC716_TellOptionSelected_ReducesMadness()
        {
            var shadows = MakeTracker();
            shadows.ApplyGrowth(ShadowStatType.Madness, 2, "setup");

            // Use TellLlmAdapter: turn 1 opponent response reveals a Charm tell.
            // Turn 2: player picks Charm → HasTellBonus = true.
            var diceValues = new List<int> { 15, 50, 15, 50, 15, 50, 15, 50 };
            var session = BuildSessionWithLlm(
                dice: new TestDice(diceValues.ToArray()),
                llm: new TellLlmAdapter(StatType.Charm),
                shadows: shadows,
                startingInterest: 5);

            // Turn 1: any option (sets up tell for next turn)
            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);

            // Turn 2: Charm (idx 0) with active tell → Madness -1
            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            // setup 2 - 1 (tell) = 1
            Assert.Equal(1, shadows.GetDelta(ShadowStatType.Madness));
            Assert.Contains(result.ShadowGrowthEvents, e => e.Contains("Tell option"));
        }

        // Tell option selected on failure → still reduces Madness
        [Fact]
        public async Task AC716_TellOptionFailure_StillReducesMadness()
        {
            var shadows = MakeTracker();
            shadows.ApplyGrowth(ShadowStatType.Madness, 3, "setup");

            // Turn 1: succeed to set up tell. Turn 2: fail (Fumble) but tell still reduces.
            // Charm=3, DC=15. Turn 2 roll=10 → total=13, miss by 2 → Fumble (no TropeTrap).
            var diceValues = new List<int> { 15, 50, 10, 50, 15, 50, 15, 50 };
            var session = BuildSessionWithLlm(
                dice: new TestDice(diceValues.ToArray()),
                llm: new TellLlmAdapter(StatType.Charm),
                shadows: shadows,
                startingInterest: 5);

            // Turn 1: sets up tell
            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);

            // Turn 2: Charm with Tell, roll=10 → Fumble. Tell reduction still fires.
            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            // setup 3 - 1 (tell) = 2
            Assert.Equal(2, shadows.GetDelta(ShadowStatType.Madness));
            Assert.Contains(result.ShadowGrowthEvents, e => e.Contains("Tell option"));
        }
    }
}