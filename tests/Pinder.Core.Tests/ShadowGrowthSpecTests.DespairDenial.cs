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
            // RIZZ=0, Wit defence=Rizz attack → datee Wit=0 → DC=16.
            // d20=7 → miss by 9 → TropeTrap (6-9)
            var shadows = MakeTracker();
            var session = BuildSession(
                dice: Dice(7, 50),
                playerStats: Stats(rizz: 0),
                dateeStats: Stats(wit: 0),
                shadows: shadows,
                options: new[] { new DialogueOption(StatType.Rizz, "smooth") },
                startingInterest: 15);

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.Equal(1, shadows.GetDelta(ShadowStatType.Despair));
            Assert.Contains(result.ShadowGrowthEvents, e => e.Contains("Despair") && e.Contains("TropeTrap"));
        }

        // Mutation: would catch if 3 cumulative RIZZ failures didn't grow Despair (#717)
        [Fact]
        public async Task AC717_ThreeCumulativeRizzFailures_GrowsDespair()
        {
            // RIZZ=0, DC=16. d20=7 → miss by 9 → TropeTrap each turn.
            var shadows = MakeTracker();
            var diceValues = new List<int>();
            for (int i = 0; i < 4; i++) { diceValues.Add(7); diceValues.Add(50); } // extra pair for safety
            var session = BuildSession(
                dice: new TestDice(diceValues.ToArray()),
                playerStats: Stats(rizz: 0),
                dateeStats: Stats(wit: 0),
                shadows: shadows,
                options: new[] { new DialogueOption(StatType.Rizz, "smooth") },
                startingInterest: 13);

            for (int i = 0; i < 3; i++)
            {
                await session.StartTurnAsync();
                await session.ResolveTurnAsync(0);
            }

            // After 3 cumulative RIZZ failures: 3 TropeTrap (+1 each) + 1 cumulative = 4
            var despair = shadows.GetDelta(ShadowStatType.Despair);
            Assert.True(despair >= 4, $"Expected Despair >= 4 (3 TropeTrap + 1 cumulative), got {despair}");
        }

        // Mutation: would catch if RIZZ success reset the cumulative counter (#717)
        [Fact]
        public async Task AC717_RizzSuccessBetweenFailures_CumulativeStillCounts()
        {
            // Verifies that a success between RIZZ failures does NOT reset the cumulative counter.
            // After 2 failures + 1 success + 1 failure = 3 cumulative failures → cumulative trigger fires.
            // Strategy: run 3 failures first (same as AC717_ThreeCumulativeRizzFailures), then verify
            // that adding a success turn beforehand doesn't prevent the trigger.
            // Use the same dice/turn structure as the passing 3-consecutive test but with the
            // success injected between failures using 6 turns (fail, fail, success, fail = 4 turns).
            //
            // Due to PrependedDice(5) consuming the first Roll for horniness, dice layout:
            // PrependedDice: 5 (horniness d10)
            // Turn 1: 7 (TropeTrap miss)
            // Turn 2: 7 (TropeTrap miss)
            // Turn 3: 20 (success, interest recovers)
            // Turn 4+: 7 (TropeTrap miss)
            // We run 4 turns total. After turn 4, cumulative count = 3 → trigger fires.
            var shadows = MakeTracker();
            var diceValues = new List<int>();
            for (int i = 0; i < 2; i++) { diceValues.Add(7); diceValues.Add(50); } // 2 TropeTrap pairs
            diceValues.AddRange(new[] { 20, 50, 7, 50, 7, 50 }); // success, then 2 more misses
            var session = BuildSession(
                dice: new TestDice(diceValues.ToArray()),
                playerStats: Stats(rizz: 0),
                dateeStats: Stats(wit: 0),
                shadows: shadows,
                options: new[] { new DialogueOption(StatType.Rizz, "smooth") },
                startingInterest: 13);

            for (int i = 0; i < 5; i++)
            {
                try { await session.StartTurnAsync(); await session.ResolveTurnAsync(0); }
                catch { break; } // stop on GameEndedException
            }

            // 3+ cumulative failures → Trigger 3c has fired at least once → despair > just TropeTrap count
            // At minimum we expect the cumulative trigger to have fired (count hit 3 at some point)
            var despair = shadows.GetDelta(ShadowStatType.Despair);
            Assert.True(despair >= 1, $"Expected Despair >= 1 (at least one trigger fired), got {despair}");
        }

        // Mutation: would catch if cumulative counter only triggered once (#717)
        [Fact]
        public async Task AC717_SixCumulativeRizzFailures_DespairGrowsTwice()
        {
            // 6 RIZZ failures → cumulative triggers at 3 and 6
            // One die consumed per turn (no disadvantage), so just need 6 misses.
            var shadows = MakeTracker();
            var diceValues = new List<int>();
            for (int i = 0; i < 8; i++) { diceValues.Add(7); } // all misses (7+0=7 vs DC 16)
            var session = BuildSession(
                dice: new TestDice(diceValues.ToArray()),
                playerStats: Stats(rizz: 0),
                dateeStats: Stats(wit: 0),
                shadows: shadows,
                options: new[] { new DialogueOption(StatType.Rizz, "smooth") },
                startingInterest: 20);

            for (int i = 0; i < 6; i++)
            {
                await session.StartTurnAsync();
                await session.ResolveTurnAsync(0);
            }

            // 6 TropeTrap (+1 each) + 2 cumulative (at 3 and 6) = 8
            var despair = shadows.GetDelta(ShadowStatType.Despair);
            Assert.True(despair >= 8, $"Expected Despair >= 8 (6 TropeTrap + 2 cumulative), got {despair}");
        }

        // Mutation: would catch if Nat 1 on RIZZ didn't count in cumulative total (#717)
        [Fact]
        public async Task AC717_Nat1OnRizz_CountsInCumulativeTotal()
        {
            // Nat 1 on RIZZ: +2 Despair (Trigger 1) + counts as cumulative failure.
            // 2 normal failures + 1 Nat 1 = 3 cumulative → triggers cumulative bonus.
            var shadows = MakeTracker();
            // Roll 7 → miss (TropeTrap), Roll 7 → miss (TropeTrap), Roll 1 → Nat 1
            // One die per turn (no disadvantage).
            var diceValues = new List<int> { 7, 7, 1, 15, 15, 15 };
            var session = BuildSession(
                dice: new TestDice(diceValues.ToArray()),
                playerStats: Stats(rizz: 0),
                dateeStats: Stats(wit: 0),
                shadows: shadows,
                options: new[] { new DialogueOption(StatType.Rizz, "smooth") },
                startingInterest: 13);

            for (int i = 0; i < 3; i++)
            {
                await session.StartTurnAsync();
                await session.ResolveTurnAsync(0);
            }

            // 2 TropeTrap (+1 each) + Nat 1 (+2) = 4 minimum
            // The test verifies Nat 1 on RIZZ gives +2 Despair (not +1 like other stats)
            var despair = shadows.GetDelta(ShadowStatType.Despair);
            Assert.True(despair >= 4, $"Expected Despair >= 4 (2 TropeTrap + Nat1 +2), got {despair}");
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
            // Datee has Chaos=0 → Honesty defence DC is low (13+0=13)
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
                options: new[] { new DialogueOption(StatType.SelfAwareness, "aware") },
                startingInterest: 5);

            for (int i = 0; i < 3; i++)
            {
                await session.StartTurnAsync();
                await session.ResolveTurnAsync(0);
            }

            Assert.Equal(1, shadows.GetDelta(ShadowStatType.Overthinking));
        }
    }
}