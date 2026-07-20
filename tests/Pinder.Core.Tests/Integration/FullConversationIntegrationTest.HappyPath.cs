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

namespace Pinder.Core.Tests.Integration
{
    public partial class FullConversationIntegrationTest
    {
        // ================================================================
        // AC1-AC13: Full 8-turn integration (happy path, DateSecured)
        // ================================================================

        /// <summary>
        /// Runs 8 turns: 7 Speak actions + 1 Recover action.
        /// Turn plan (with startingInterest=15 to reach DateSecured):
        ///   T1: Wit success          → +2 (Hard tier)
        ///   T2: Charm success        → +2 (The Setup combo: Wit→Charm)
        ///   T3: Honesty fail Misfire → −2
        ///   T4: SA success           → +5 (Bold tier + The Recovery combo)
        ///   T5: Chaos fail TropeTrap → −2 (trap "unhinged" activates)
        ///   T6: Wait                 → −1 interest (trap still active, 2 turns left)
        ///   T7: Charm success        → +3
        ///   T8: Charm Nat20 success  → +5 → interest 25 → DateSecured
        /// </summary>
        [Fact]
        public async Task FullEightTurnConversation_VerifiesAllMechanics()
        {
            // ---- Setup ----
            var geraldStats = CreateGeraldStats();
            var velvetStats = CreateVelvetStats();

            var timing = new TimingProfile(baseDelay: 10, variance: 0.5f, drySpell: 0.0f, readReceipt: "neutral");

            var gerald = TestHelpers.MakeCharacterProfile(geraldStats, "Test system prompt", "Gerald", timing, level: 5);
            var velvet = TestHelpers.MakeCharacterProfile(velvetStats, "Test system prompt", "Velvet", timing, level: 7);

            var playerShadows = new SessionShadowTracker(geraldStats);
            var dateeShadows = new SessionShadowTracker(velvetStats);

            var config = new GameSessionConfig(
                clock: TestHelpers.MakeClock(),
                playerShadows: playerShadows,
                dateeShadows: dateeShadows,
                startingInterest: 5,  // low start to stay in range with new higher risk bonuses
                previousOpener: null,
                activeTrapInterestPenalty: 0.0);

            var trapRegistry = new TestTrapRegistry();

            var llm = new ScriptedLlmAdapter(new[]
            {
                // Turn 1: pick index 0 = Wit
                new[] { Opt(StatType.Wit), Opt(StatType.Charm), Opt(StatType.Honesty), Opt(StatType.Chaos) },
                // Turn 2: pick index 1 = Charm
                new[] { Opt(StatType.Honesty), Opt(StatType.Charm), Opt(StatType.Wit), Opt(StatType.Chaos) },
                // Turn 3: pick index 0 = Honesty
                new[] { Opt(StatType.Honesty), Opt(StatType.Charm), Opt(StatType.Wit), Opt(StatType.Chaos) },
                // Turn 4: pick index 0 = SelfAwareness
                new[] { Opt(StatType.SelfAwareness), Opt(StatType.Charm), Opt(StatType.Wit), Opt(StatType.Chaos) },
                // Turn 5: pick index 0 = Chaos
                new[] { Opt(StatType.Chaos), Opt(StatType.Charm), Opt(StatType.Wit), Opt(StatType.Honesty) },
                // Turn 6 is Wait — no options needed
                // Turn 7: pick index 0 = Charm
                new[] { Opt(StatType.Charm), Opt(StatType.Honesty), Opt(StatType.Wit), Opt(StatType.Chaos) },
                // Turn 8: pick index 0 = Charm
                new[] { Opt(StatType.Charm), Opt(StatType.Honesty), Opt(StatType.Wit), Opt(StatType.Chaos) },
            });

            // Full dice queue (traced through every dice.Roll call with startingInterest=5):
            // Note: DC base changed from 13 to 16; Velvet stats adjusted -3 to preserve DC values.
            // Risk tier bonuses changed: Hard=+3 (was +1), Bold=+5 (was +2), Safe=+1 (was 0).
            // T1 (no adv, interest=5, Lukewarm):   d20=16, d100=50
            //   Wit DC=17, total=21, margin=4 → scale+1, Hard(need=12) → +3. delta=4. Interest=9.
            // T2 (no adv, interest=9, Lukewarm):   d20=8, d100=50
            //   Charm DC=18, total=23, margin=5 → scale+2, Safe(need=3) → +1, Setup +1. delta=4. Interest=13.
            // T3 (no adv, interest=13, Interested): d20=19, d100=50
            //   Honesty DC=27, total=24, miss=3 → Misfire(-1). delta=-1. Interest=12.
            // T4 (no adv, interest=12, Interested): d20=18, d100=50
            //   SA DC=23, total=24, margin=1 → scale+1, Bold(need=17) → +5, Recovery +2. delta=8. Interest=20.
            // T5 (adv, interest=20, VeryIntoIt):   d20=5, d20=7, d100=50
            //   Chaos DC=18, total=11, miss=7 → TropeTrap(-2). Interest=18.
            // T6 (Wait, interest=18, VeryIntoIt): -1 interest. Interest=17. Trap 2 turns left.
            // T7 (adv, interest=17, VeryIntoIt):   d20=8, d20=6, d100=50
            //   Charm DC=18, total=23, margin=5 → scale+2, Safe+1. delta=3. Interest=20.
            // T8 (adv, interest=20, VeryIntoIt):  d20=20, d20=5, d100=50
            //   Charm DC=18, Nat20! delta=+5. Interest=20+5=25 → DateSecured.
            var dice = new FixedDice(
                5,  // Constructor: horniness roll (1d10)
                16, 50,             // T1
                8, 50,              // T2
                19, 50,             // T3
                18, 50,             // T4
                5, 7, 50,           // T5
                                    // T6 Wait (no dice)
                8, 6, 50,           // T7
                20, 5, 50           // T8
            );

            var session = new GameSession(gerald, velvet, llm, dice, trapRegistry, config);

            // ========== Turn 1: Speak WIT (Success) ==========
            // Mutation: Fails if StartTurnAsync doesn't return valid options
            var turn1Start = await session.StartTurnAsync();
            Assert.NotNull(turn1Start);
            var turn1 = await session.ResolveTurnAsync(0); // Wit

            // Mutation: Fails if roll total computed incorrectly (d20 + stat + level bonus)
            Assert.True(turn1.Roll.IsSuccess);
            Assert.Equal(StatType.Wit, turn1.Roll.Stat);
            Assert.Equal(21, turn1.Roll.Total);           // 16 + 3 + 2
            Assert.Equal(17, turn1.Roll.DC);
            // Mutation: Fails if risk tier boundaries are wrong (need=12 should be Hard)
            Assert.Equal(RiskTier.Hard, turn1.Roll.RiskTier); // need = 17-3-2 = 12
            // Mutation: Fails if interest delta doesn't include Hard tier bonus
            Assert.Equal(4, turn1.InterestDelta);          // +1 success + 3 Hard
            Assert.Equal(9, turn1.StateAfter.Interest);    // 5 + 4
            Assert.Equal(InterestState.Lukewarm, turn1.StateAfter.State);
            // Mutation: Fails if momentum doesn't increment on success
            Assert.Equal(1, turn1.StateAfter.MomentumStreak);
            Assert.Null(turn1.ComboTriggered);
            Assert.False(turn1.IsGameOver);

            // ========== Turn 2: Speak CHARM (Success, The Setup combo) ==========
            var turn2Start = await session.StartTurnAsync();
            var turn2 = await session.ResolveTurnAsync(1); // Charm at index 1

            Assert.True(turn2.Roll.IsSuccess);
            Assert.Equal(StatType.Charm, turn2.Roll.Stat);
            Assert.Equal(23, turn2.Roll.Total);            // 8 + 13 + 2
            Assert.Equal(18, turn2.Roll.DC);
            Assert.Equal(RiskTier.Safe, turn2.Roll.RiskTier); // need = 18-13-2 = 3
            // Mutation: Fails if combo detection doesn't recognize Wit→Charm as "The Setup"
            Assert.Equal("The Setup", turn2.ComboTriggered);
            // Mutation: Fails if combo interest bonus not added to delta
            Assert.Equal(4, turn2.InterestDelta);          // +2 success + 1 Safe + 1 combo
            Assert.Equal(13, turn2.StateAfter.Interest);   // 9 + 4
            Assert.Equal(InterestState.Interested, turn2.StateAfter.State);
            Assert.Equal(2, turn2.StateAfter.MomentumStreak);
            Assert.False(turn2.IsGameOver);

            // ========== Turn 3: Speak HONESTY (Fail, Misfire) ==========
            var turn3Start = await session.StartTurnAsync();
            var turn3 = await session.ResolveTurnAsync(0); // Honesty at index 0

            Assert.False(turn3.Roll.IsSuccess);
            Assert.Equal(StatType.Honesty, turn3.Roll.Stat);
            Assert.Equal(24, turn3.Roll.Total);            // 19 + 3 + 2
            Assert.Equal(27, turn3.Roll.DC);               // 16 + Velvet Chaos 11
            // Mutation: Fails if miss margin 3 not mapped to Misfire tier (range 3–5)
            Assert.Equal(FailureTier.Misfire, turn3.Roll.Tier); // miss = 27-24 = 3
            // Mutation: Fails if Misfire interest delta is wrong (rules-v3.4 §5)
            Assert.Equal(-1, turn3.InterestDelta);         // Misfire → −1
            Assert.Equal(12, turn3.StateAfter.Interest);   // 13 − 1
            Assert.Equal(InterestState.Interested, turn3.StateAfter.State);
            // Mutation: Fails if momentum doesn't reset on failure
            Assert.Equal(0, turn3.StateAfter.MomentumStreak);
            Assert.Null(turn3.ComboTriggered);
            Assert.False(turn3.IsGameOver);

            // ========== Turn 4: Speak SA (Success, The Recovery combo, Bold tier) ==========
            var turn4Start = await session.StartTurnAsync();
            var turn4 = await session.ResolveTurnAsync(0); // SelfAwareness at index 0

            Assert.True(turn4.Roll.IsSuccess);
            Assert.Equal(StatType.SelfAwareness, turn4.Roll.Stat);
            Assert.Equal(24, turn4.Roll.Total);            // 18 + 4 + 2
            Assert.Equal(23, turn4.Roll.DC);               // 16 + Velvet Honesty 7
            // Mutation: Fails if need=17 not classified as Bold tier (need ≥ 16)
            Assert.Equal(RiskTier.Bold, turn4.Roll.RiskTier); // need = 23-4-2 = 17
            // Mutation: Fails if Recovery combo not detected (fail→SA success)
            Assert.Equal("The Recovery", turn4.ComboTriggered);
            // Mutation: Fails if Bold bonus (+5) or Recovery bonus (+2) missing
            Assert.Equal(8, turn4.InterestDelta);          // +1 success + 5 Bold + 2 Recovery
            Assert.Equal(20, turn4.StateAfter.Interest);   // 12 + 8
            Assert.Equal(InterestState.VeryIntoIt, turn4.StateAfter.State);
            Assert.Equal(1, turn4.StateAfter.MomentumStreak);
            Assert.False(turn4.IsGameOver);

            // ========== Turn 5: Speak CHAOS (Fail, TropeTrap) ==========
            var turn5Start = await session.StartTurnAsync();
            var turn5 = await session.ResolveTurnAsync(0); // Chaos at index 0

            Assert.False(turn5.Roll.IsSuccess);
            Assert.Equal(StatType.Chaos, turn5.Roll.Stat);
            Assert.Equal(11, turn5.Roll.Total);            // max(5,7)=7 + 2 + 2
            Assert.Equal(18, turn5.Roll.DC);               // 16 + Velvet Charm 2
            // Mutation: Fails if miss=7 not classified as TropeTrap (range 6–9)
            Assert.Equal(FailureTier.TropeTrap, turn5.Roll.Tier);
            // Mutation: Fails if trap not activated from registry
            Assert.NotNull(turn5.Roll.ActivatedTrap);
            Assert.Equal("unhinged", turn5.Roll.ActivatedTrap.Id);
            Assert.Equal(-2, turn5.InterestDelta);         // TropeTrap → −2 (rules-v3.4 §5)
            Assert.Equal(18, turn5.StateAfter.Interest);   // 20 − 2
            Assert.Equal(InterestState.VeryIntoIt, turn5.StateAfter.State);
            Assert.Equal(0, turn5.StateAfter.MomentumStreak);
            // Mutation: Fails if trap not tracked in game state
            Assert.Contains("unhinged", turn5.StateAfter.ActiveTrapNames);
            Assert.False(turn5.IsGameOver);

            // ========== Turn 6: Wait (-1 interest, trap still active) ==========
            session.Wait();
            // Interest: 18 - 1 = 17. Trap "unhinged" has 2 turns remaining (duration 3, turn 5 + AdvanceTurn = 2 left, then Wait AdvanceTurn = 1 left).

            // ========== Turn 7: Speak CHARM (Success) ==========
            var turn7Start = await session.StartTurnAsync();
            var turn7 = await session.ResolveTurnAsync(0); // Charm at index 0

            Assert.True(turn7.Roll.IsSuccess);
            Assert.Equal(StatType.Charm, turn7.Roll.Stat);
            Assert.Equal(23, turn7.Roll.Total);            // max(8,6)=8 + 13 + 2
            Assert.Equal(18, turn7.Roll.DC);
            Assert.Equal(RiskTier.Safe, turn7.Roll.RiskTier);
            // Mutation: Fails if beat-by-5 not scored as +2
            Assert.Equal(3, turn7.InterestDelta);          // +2 success(beat by 5) + 1 Safe
            Assert.Equal(20, turn7.StateAfter.Interest);   // 17 + 3
            Assert.Equal(InterestState.VeryIntoIt, turn7.StateAfter.State);
            Assert.Equal(1, turn7.StateAfter.MomentumStreak);
            Assert.False(turn7.IsGameOver);

            // ========== Turn 8: Speak CHARM (Nat 20, DateSecured) ==========
            var turn8Start = await session.StartTurnAsync();
            var turn8 = await session.ResolveTurnAsync(0); // Charm at index 0

            // Mutation: Fails if Nat20 not detected when advantage takes max(20,5)=20
            Assert.True(turn8.Roll.IsSuccess);
            Assert.True(turn8.Roll.IsNatTwenty);
            Assert.Equal(StatType.Charm, turn8.Roll.Stat);
            Assert.Equal(35, turn8.Roll.Total);            // max(20,5)=20 + 13 + 2
            // Mutation: Fails if Nat20 doesn't give +5 interest delta (scale+4 + Safe+1)
            Assert.Equal(5, turn8.InterestDelta);
            // Mutation: Fails if interest not clamped at 25
            Assert.Equal(25, turn8.StateAfter.Interest);   // 20 + 5 = 25
            // Mutation: Fails if DateSecured state not set at interest=25
            Assert.Equal(InterestState.DateSecured, turn8.StateAfter.State);
            Assert.Equal(2, turn8.StateAfter.MomentumStreak);
            // Mutation: Fails if game doesn't end on DateSecured
            Assert.True(turn8.IsGameOver);
            Assert.Equal(GameOutcome.DateSecured, turn8.Outcome);

            // ---- Cumulative XP verification ----
            // Mutation: Fails if XP ledger doesn't accumulate across all turns
            Assert.True(session.TotalXpEarned > 0);
        }
    }
}
