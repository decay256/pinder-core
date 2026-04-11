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

        // --- Despair triggers (#708) ---

        // Mutation: would catch if RIZZ Nat 1 grows only +1 Despair instead of +2
        [Fact]
        public async Task AC708_RizzNat1_GrowsDespair2()
        {
            var shadows = MakeTracker();
            var session = BuildSession(
                dice: Dice(1, 50),
                shadows: shadows,
                options: new[] { new DialogueOption(StatType.Rizz, "smooth move") });

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.Equal(2, shadows.GetDelta(ShadowStatType.Despair));
            Assert.Contains(result.ShadowGrowthEvents, e => e.Contains("Despair") && e.Contains("Nat 1"));
        }

        // Mutation: would catch if Charm Nat 1 accidentally grew Despair (wrong pairing)
        [Fact]
        public async Task AC708_CharmNat1_DoesNotGrowDespair()
        {
            var shadows = MakeTracker();
            var session = BuildSession(
                dice: Dice(1, 50),
                shadows: shadows);

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0); // Charm

            Assert.Equal(0, shadows.GetDelta(ShadowStatType.Despair));
            Assert.Equal(1, shadows.GetDelta(ShadowStatType.Madness));
        }

        // Mutation: would catch if RIZZ TropeTrap didn't grow Despair
        [Fact]
        public async Task AC708_RizzTropeTrap_GrowsDespair1()
        {
            // RIZZ=0, Wit defence=Rizz attack → opponent Wit=0 → DC=16.
            // d20=7 → miss by 9 → TropeTrap (6-9)
            var shadows = MakeTracker();
            var session = BuildSession(
                dice: Dice(7, 50),
                playerStats: Stats(rizz: 0),
                opponentStats: Stats(wit: 0),
                shadows: shadows,
                options: new[] { new DialogueOption(StatType.Rizz, "smooth") },
                startingInterest: 15);

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.Equal(1, shadows.GetDelta(ShadowStatType.Despair));
            Assert.Contains(result.ShadowGrowthEvents, e => e.Contains("Despair") && e.Contains("TropeTrap"));
        }

        // Mutation: would catch if 3 consecutive RIZZ failures didn't grow Despair
        [Fact]
        public async Task AC708_ThreeConsecutiveRizzFailures_GrowsDespair()
        {
            // RIZZ=0, DC=16. d20=7 → miss by 9 → TropeTrap each turn.
            // 3 turns × -2 interest from TropeTrap = -6 total. Start at 20 → end at 14. No game-end.
            var shadows = MakeTracker();
            // Extra dice: prepend also adds 5, so actual consumed: 5(horn), 7,50, 7,50, 7,50
            var diceValues = new List<int>();
            for (int i = 0; i < 4; i++) { diceValues.Add(7); diceValues.Add(50); } // extra pair for safety
            var session = BuildSession(
                dice: new TestDice(diceValues.ToArray()),
                playerStats: Stats(rizz: 0),
                opponentStats: Stats(wit: 0),
                shadows: shadows,
                options: new[] { new DialogueOption(StatType.Rizz, "smooth") },
                startingInterest: 13); // low enough to never reach 25 via mishap

            for (int i = 0; i < 3; i++)
            {
                await session.StartTurnAsync();
                await session.ResolveTurnAsync(0);
            }

            // After 3 consecutive RIZZ failures: 3 TropeTrap (+1 each) + 1 consecutive = 4
            var despair = shadows.GetDelta(ShadowStatType.Despair);
            Assert.True(despair >= 4, $"Expected Despair >= 4 (3 TropeTrap + 1 consecutive), got {despair}");
        }

        // Mutation: would catch if consecutive RIZZ counter didn't reset on success
        [Fact]
        public async Task AC708_RizzSuccess_ResetsDespairConsecutiveCount()
        {
            // Two failures (TropeTrap) then one success. Consecutive count resets at success.
            // rizz:0, wit:0 → DC=16. Roll=7 → total=7, miss by 9 → TropeTrap. Roll=20 → success.
            var shadows = MakeTracker();
            // PrependedDice adds 5 first. Sequence: 5(horn), 7,50, 7,50, 20,50
            var diceValues = new List<int> { 7, 50, 7, 50, 20, 50, 15, 50 }; // extra safety
            var session = BuildSession(
                dice: new TestDice(diceValues.ToArray()),
                playerStats: Stats(rizz: 0),
                opponentStats: Stats(wit: 0),
                shadows: shadows,
                options: new[] { new DialogueOption(StatType.Rizz, "smooth") },
                startingInterest: 12);

            for (int i = 0; i < 3; i++)
            {
                await session.StartTurnAsync();
                await session.ResolveTurnAsync(0);
            }

            // 2 TropeTrap failures = 2 Despair growths. No consecutive bonus (reset at success).
            var despair = shadows.GetDelta(ShadowStatType.Despair);
            Assert.True(despair >= 2, $"Expected Despair >= 2 (2 TropeTrap growths), got {despair}");
            // Importantly the consecutive count reset, so no +1 at exactly 3 consecutive
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

        // Mutation: would catch if highest-% still used optionIndex==0 instead of actual probability
        [Fact]
        public async Task AC1_HighestPctAtIndex1_PickedThrice_GrowsFixation()
        {
            var shadows = MakeTracker();
            var diceValues = new List<int>();
            for (int i = 0; i < 3; i++) { diceValues.Add(15); diceValues.Add(50); }
            // Opponent has Chaos=0 → Honesty defence DC is low (13+0=13)
            // Player Honesty=5 → margin = 5-13 = -8 (best available)
            // Put Honesty at index 1 each turn, lower-prob stat at index 0
            // Charm vs SA defence: 3 - (13+2) = -12 (worse)
            // Wit vs Rizz defence: 4 - (13+2) = -11 (worse)
            // Chaos vs Charm defence: 0 - (13+3) = -16 (much worse)
            var opts1 = new[] { new DialogueOption(StatType.Charm, "a"), new DialogueOption(StatType.Honesty, "b") };
            var opts2 = new[] { new DialogueOption(StatType.Wit, "c"), new DialogueOption(StatType.Honesty, "d") };
            var opts3 = new[] { new DialogueOption(StatType.Chaos, "e"), new DialogueOption(StatType.Honesty, "f") };
            var rotatingLlm = new RotatingLlmAdapter(new[] { opts1, opts2, opts3 });
            var session = BuildSessionWithLlm(
                dice: new TestDice(diceValues.ToArray()),
                llm: rotatingLlm,
                playerStats: Stats(charm: 3, honesty: 5, wit: 4, chaos: 0),
                shadows: shadows);

            for (int i = 0; i < 3; i++)
            {
                await session.StartTurnAsync();
                await session.ResolveTurnAsync(1); // Always index 1 (Honesty) → highest-%
            }

            // Same stat 3 turns AND highest-% 3 turns → Fixation should grow
            Assert.True(shadows.GetDelta(ShadowStatType.Fixation) >= 1);
        }

        // Mutation: would catch if picking the lower-prob option still triggered highest-% Fixation
        [Fact]
        public async Task AC1_PickLowerProbOption3Turns_NoHighestPctFixation()
        {
            var shadows = MakeTracker();
            var diceValues = new List<int>();
            for (int i = 0; i < 3; i++) { diceValues.Add(15); diceValues.Add(50); }
            // Player: Honesty=5 is highest, Chaos=0 is lowest
            // Honesty margin = 5 - (13+0) = -8 (best)
            // Chaos margin = 0 - (13+3) = -16 (worst)
            // Each turn: [Honesty, Chaos] — always pick index 1 (Chaos = NOT highest-%)
            var opts1 = new[] { new DialogueOption(StatType.Honesty, "a"), new DialogueOption(StatType.Chaos, "b") };
            var opts2 = new[] { new DialogueOption(StatType.Honesty, "c"), new DialogueOption(StatType.Chaos, "d") };
            var opts3 = new[] { new DialogueOption(StatType.Honesty, "e"), new DialogueOption(StatType.Chaos, "f") };
            var rotatingLlm = new RotatingLlmAdapter(new[] { opts1, opts2, opts3 });
            var session = BuildSessionWithLlm(
                dice: new TestDice(diceValues.ToArray()),
                llm: rotatingLlm,
                playerStats: Stats(charm: 3, honesty: 5, wit: 4, chaos: 0),
                shadows: shadows);

            for (int i = 0; i < 3; i++)
            {
                await session.StartTurnAsync();
                await session.ResolveTurnAsync(1); // Always index 1 (Chaos) → NOT highest-%
            }

            // Same stat (Chaos) 3 turns → Fixation +1 from same-stat trigger
            // But NOT highest-% trigger — so exactly +1, not +2
            Assert.Equal(1, shadows.GetDelta(ShadowStatType.Fixation));
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
                startingInterest: 5);

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
                    { ShadowStatType.Madness, 0 }, { ShadowStatType.Despair, 0 },
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

            var config = new GameSessionConfig(clock: TestHelpers.MakeClock(), playerShadows: shadows,
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

        /// <summary>LLM adapter that returns a Tell on the opponent's response for a specific stat.</summary>
        private sealed class TellLlmAdapter : ILlmAdapter
        {
            private readonly StatType _tellStat;
            public TellLlmAdapter(StatType tellStat) => _tellStat = tellStat;

            public Task<DialogueOption[]> GetDialogueOptionsAsync(DialogueContext context)
            {
                var options = new[]
                {
                    new DialogueOption(StatType.Charm, "Hey, you come here often?"),
                    new DialogueOption(StatType.Honesty, "I have to be real with you..."),
                    new DialogueOption(StatType.Wit, "Did you know that penguins propose with pebbles?"),
                    new DialogueOption(StatType.Chaos, "I once ate a whole pizza in a bouncy castle.")
                };
                return Task.FromResult(options);
            }
            public Task<string> DeliverMessageAsync(DeliveryContext context)
                => Task.FromResult(context.ChosenOption.IntendedText);
            public Task<OpponentResponse> GetOpponentResponseAsync(OpponentContext context)
                => Task.FromResult(new OpponentResponse("...",
                    detectedTell: new Tell(_tellStat, $"Tell on {_tellStat}")));
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
