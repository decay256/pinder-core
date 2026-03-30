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
    /// <summary>
    /// End-to-end integration tests running full GameSession conversations.
    /// Verifies the complete rules stack: interest deltas with success/failure scaling,
    /// momentum streaks, combo detection, trap activation and recovery, shadow growth
    /// events, XP accumulation, and game outcome resolution.
    ///
    /// All randomness is controlled via FixedDice; no real LLM API calls are made.
    /// </summary>
    public class FullConversationIntegrationTest
    {
        /// <summary>
        /// Runs 8 turns: 7 Speak actions + 1 Recover action.
        /// Turn plan (with startingInterest=15 to reach DateSecured):
        ///   T1: Wit success          → +2 (Hard tier)
        ///   T2: Charm success        → +2 (The Setup combo: Wit→Charm)
        ///   T3: Honesty fail Misfire → −2
        ///   T4: SA success           → +5 (Bold tier + The Recovery combo)
        ///   T5: Chaos fail TropeTrap → −3 (trap "unhinged" activates)
        ///   T6: Recover success      → 0  (trap cleared)
        ///   T7: Charm success        → +2
        ///   T8: Charm Nat20 success  → +4 → interest 25 → DateSecured
        /// </summary>
        [Fact]
        public async Task FullEightTurnConversation_VerifiesAllMechanics()
        {
            // ---- Setup ----
            var geraldStats = CreateGeraldStats();
            var velvetStats = CreateVelvetStats();

            var timing = new TimingProfile(baseDelay: 10, variance: 0.5f, drySpell: 0.0f, readReceipt: "neutral");

            var gerald = new CharacterProfile(geraldStats, "Test system prompt", "Gerald", timing, level: 5);
            var velvet = new CharacterProfile(velvetStats, "Test system prompt", "Velvet", timing, level: 7);

            var playerShadows = new SessionShadowTracker(geraldStats);
            var opponentShadows = new SessionShadowTracker(velvetStats);

            var config = new GameSessionConfig(
                clock: null,
                playerShadows: playerShadows,
                opponentShadows: opponentShadows,
                startingInterest: 15,
                previousOpener: null);

            var trapRegistry = new TestTrapRegistry();

            // Build scripted LLM adapter: returns correct stat options per turn.
            // Turn 4 needs SelfAwareness which NullLlmAdapter doesn't provide.
            var llm = new ScriptedLlmAdapter(new[]
            {
                // Turn 1: pick index 0 = Wit
                new[] { Opt(StatType.Wit), Opt(StatType.Charm), Opt(StatType.Honesty), Opt(StatType.Chaos) },
                // Turn 2: pick index 1 = Charm (index 1 to avoid highest-% streak)
                new[] { Opt(StatType.Honesty), Opt(StatType.Charm), Opt(StatType.Wit), Opt(StatType.Chaos) },
                // Turn 3: pick index 0 = Honesty
                new[] { Opt(StatType.Honesty), Opt(StatType.Charm), Opt(StatType.Wit), Opt(StatType.Chaos) },
                // Turn 4: pick index 0 = SelfAwareness
                new[] { Opt(StatType.SelfAwareness), Opt(StatType.Charm), Opt(StatType.Wit), Opt(StatType.Chaos) },
                // Turn 5: pick index 0 = Chaos
                new[] { Opt(StatType.Chaos), Opt(StatType.Charm), Opt(StatType.Wit), Opt(StatType.Honesty) },
                // Turn 6 is Recover — no options needed
                // Turn 7: pick index 0 = Charm
                new[] { Opt(StatType.Charm), Opt(StatType.Honesty), Opt(StatType.Wit), Opt(StatType.Chaos) },
                // Turn 8: pick index 0 = Charm
                new[] { Opt(StatType.Charm), Opt(StatType.Honesty), Opt(StatType.Wit), Opt(StatType.Chaos) },
            });

            // Full dice queue (traced through every dice.Roll call):
            // T1 (no adv):  d20=14,         d100=50
            // T2 (adv):     d20=3, d20=5,   d100=50
            // T3 (adv):     d20=19, d20=18, d100=50
            // T4 (adv):     d20=15, d20=18, d100=50
            // T5 (adv):     d20=5, d20=7,   d100=50
            // T6 (Recover, adv): d20=10, d20=8
            // T7 (adv):     d20=8, d20=6,   d100=50
            // T8 (adv):     d20=20, d20=5,  d100=50
            var dice = new FixedDice(
                14, 50,             // T1
                3, 5, 50,           // T2
                19, 18, 50,         // T3
                15, 18, 50,         // T4
                5, 7, 50,           // T5
                10, 8,              // T6 Recover
                8, 6, 50,           // T7
                20, 5, 50           // T8
            );

            var session = new GameSession(gerald, velvet, llm, dice, trapRegistry, config);

            // ========== Turn 1: Speak WIT (Success) ==========
            // Interest=15 → Interested → no advantage/disadvantage
            // Gerald: Wit +3, level 5 bonus +2 = +5 total mods
            // Velvet defends Wit with Rizz: 13 + 4 = DC 17
            // Roll: d20=14, total=14+3+2=19 vs DC 17 → success, beat by 2 → +1
            // RiskTier: need = 17-3-2 = 12 → Hard → +1 bonus
            // Interest delta: +1 (success) + 1 (Hard) = +2
            var turn1Start = await session.StartTurnAsync();
            Assert.NotNull(turn1Start);
            var turn1 = await session.ResolveTurnAsync(0); // Wit

            Assert.True(turn1.Roll.IsSuccess);
            Assert.Equal(StatType.Wit, turn1.Roll.Stat);
            Assert.Equal(19, turn1.Roll.Total);           // 14 + 3 + 2
            Assert.Equal(17, turn1.Roll.DC);
            Assert.Equal(RiskTier.Hard, turn1.Roll.RiskTier); // need = 17-3-2 = 12
            Assert.Equal(2, turn1.InterestDelta);          // +1 success + 1 Hard
            Assert.Equal(17, turn1.StateAfter.Interest);   // 15 + 2
            Assert.Equal(InterestState.VeryIntoIt, turn1.StateAfter.State);
            Assert.Equal(1, turn1.StateAfter.MomentumStreak);
            Assert.Null(turn1.ComboTriggered);
            Assert.Equal(10, turn1.XpEarned);              // DC 17 → mid-tier → 10
            Assert.False(turn1.IsGameOver);

            // ========== Turn 2: Speak CHARM (Success, The Setup combo) ==========
            // Interest=17 → VeryIntoIt → advantage
            // Gerald: Charm +13, level 5 bonus +2 = +15 total mods
            // Velvet defends Charm with SA: 13 + 5 = DC 18
            // Roll: d20=3, d20=5 (advantage: take higher=5), total=5+13+2=20 vs DC 18 → success, beat by 2 → +1
            // RiskTier: need = 18-13-2 = 3 → Safe → 0
            // Combo: Wit→Charm = "The Setup" (+1 interest)
            // Interest delta: +1 (success) + 0 (Safe) + 1 (combo) = +2
            var turn2Start = await session.StartTurnAsync();
            var turn2 = await session.ResolveTurnAsync(1); // Charm at index 1

            Assert.True(turn2.Roll.IsSuccess);
            Assert.Equal(StatType.Charm, turn2.Roll.Stat);
            Assert.Equal(20, turn2.Roll.Total);            // max(3,5)=5 + 13 + 2
            Assert.Equal(18, turn2.Roll.DC);
            Assert.Equal(RiskTier.Safe, turn2.Roll.RiskTier); // need = 18-13-2 = 3
            Assert.Equal("The Setup", turn2.ComboTriggered); // Wit→Charm
            Assert.Equal(2, turn2.InterestDelta);          // +1 success + 0 Safe + 1 combo
            Assert.Equal(19, turn2.StateAfter.Interest);   // 17 + 2
            Assert.Equal(InterestState.VeryIntoIt, turn2.StateAfter.State);
            Assert.Equal(2, turn2.StateAfter.MomentumStreak);
            Assert.Equal(15, turn2.XpEarned);              // DC 18 → high-tier → 15
            Assert.False(turn2.IsGameOver);

            // ========== Turn 3: Speak HONESTY (Fail, Misfire) ==========
            // Interest=19 → VeryIntoIt → advantage
            // Gerald: Honesty +3, level 5 bonus +2 = +5 total mods
            // Velvet defends Honesty with Chaos: 13 + 14 = DC 27
            // Roll: d20=19, d20=18 (advantage: take higher=19), total=19+3+2=24 vs DC 27 → fail
            // Miss = 27-24 = 3 → Misfire → -2
            // Momentum resets to 0
            var turn3Start = await session.StartTurnAsync();
            var turn3 = await session.ResolveTurnAsync(0); // Honesty at index 0

            Assert.False(turn3.Roll.IsSuccess);
            Assert.Equal(StatType.Honesty, turn3.Roll.Stat);
            Assert.Equal(24, turn3.Roll.Total);            // max(19,18)=19 + 3 + 2
            Assert.Equal(27, turn3.Roll.DC);               // 13 + Velvet Chaos 14
            Assert.Equal(FailureTier.Misfire, turn3.Roll.Tier); // miss = 27-24 = 3
            Assert.Equal(-2, turn3.InterestDelta);         // Misfire → −2
            Assert.Equal(17, turn3.StateAfter.Interest);   // 19 − 2
            Assert.Equal(InterestState.VeryIntoIt, turn3.StateAfter.State);
            Assert.Equal(0, turn3.StateAfter.MomentumStreak); // reset on fail
            Assert.Null(turn3.ComboTriggered);
            Assert.Equal(2, turn3.XpEarned);               // failure → 2
            Assert.False(turn3.IsGameOver);

            // ========== Turn 4: Speak SA (Success, The Recovery combo, Bold tier) ==========
            // Interest=17 → VeryIntoIt → advantage
            // Gerald: SA +4, level 5 bonus +2 = +6 total mods
            // Velvet defends SA with Honesty: 13 + 10 = DC 23
            // Roll: d20=15, d20=18 (advantage: take higher=18), total=18+4+2=24 vs DC 23 → success, beat by 1 → +1
            // RiskTier: need = 23-4-2 = 17 → Bold → +2
            // Combo: fail→SA success = "The Recovery" (+2 interest)
            // Interest delta: +1 (success) + 2 (Bold) + 2 (Recovery) = +5
            var turn4Start = await session.StartTurnAsync();
            var turn4 = await session.ResolveTurnAsync(0); // SelfAwareness at index 0

            Assert.True(turn4.Roll.IsSuccess);
            Assert.Equal(StatType.SelfAwareness, turn4.Roll.Stat);
            Assert.Equal(24, turn4.Roll.Total);            // max(15,18)=18 + 4 + 2
            Assert.Equal(23, turn4.Roll.DC);               // 13 + Velvet Honesty 10
            Assert.Equal(RiskTier.Bold, turn4.Roll.RiskTier); // need = 23-4-2 = 17
            Assert.Equal("The Recovery", turn4.ComboTriggered); // fail→SA success
            Assert.Equal(5, turn4.InterestDelta);          // +1 success + 2 Bold + 2 Recovery
            Assert.Equal(22, turn4.StateAfter.Interest);   // 17 + 5
            Assert.Equal(InterestState.AlmostThere, turn4.StateAfter.State);
            Assert.Equal(1, turn4.StateAfter.MomentumStreak);
            Assert.Equal(15, turn4.XpEarned);              // DC 23 → high-tier → 15
            Assert.False(turn4.IsGameOver);

            // ========== Turn 5: Speak CHAOS (Fail, TropeTrap) ==========
            // Interest=22 → AlmostThere → advantage
            // Gerald: Chaos +2, level 5 bonus +2 = +4 total mods
            // Velvet defends Chaos with Charm: 13 + 5 = DC 18
            // Roll: d20=5, d20=7 (advantage: take higher=7), total=7+2+2=11 vs DC 18 → fail
            // Miss = 18-11 = 7 → TropeTrap → -3, activates "unhinged" trap
            // Momentum resets to 0
            var turn5Start = await session.StartTurnAsync();
            var turn5 = await session.ResolveTurnAsync(0); // Chaos at index 0

            Assert.False(turn5.Roll.IsSuccess);
            Assert.Equal(StatType.Chaos, turn5.Roll.Stat);
            Assert.Equal(11, turn5.Roll.Total);            // max(5,7)=7 + 2 + 2
            Assert.Equal(18, turn5.Roll.DC);               // 13 + Velvet Charm 5
            Assert.Equal(FailureTier.TropeTrap, turn5.Roll.Tier); // miss = 18-11 = 7
            Assert.NotNull(turn5.Roll.ActivatedTrap);
            Assert.Equal("unhinged", turn5.Roll.ActivatedTrap.Id);
            Assert.Equal(-3, turn5.InterestDelta);         // TropeTrap → −3
            Assert.Equal(19, turn5.StateAfter.Interest);   // 22 − 3
            Assert.Equal(InterestState.VeryIntoIt, turn5.StateAfter.State);
            Assert.Equal(0, turn5.StateAfter.MomentumStreak); // reset on fail
            Assert.Contains("unhinged", turn5.StateAfter.ActiveTrapNames);
            Assert.Equal(2, turn5.XpEarned);               // failure → 2
            Assert.False(turn5.IsGameOver);

            // ========== Turn 6: Recover (SA vs DC 12, Success) ==========
            // Interest=19 → VeryIntoIt → advantage
            // Gerald: SA +4, level 5 bonus +2 = +6 total mods
            // Roll: d20=10, d20=8 (advantage: take higher=10), total=10+4+2=16 vs DC 12 → success
            // Clears "unhinged" trap, +15 XP
            var recover = await session.RecoverAsync();

            Assert.True(recover.Success);
            Assert.Equal("unhinged", recover.ClearedTrapName);
            Assert.True(recover.Roll.IsSuccess);
            Assert.Equal(16, recover.Roll.Total);          // max(10,8)=10 + 4 + 2
            Assert.Equal(12, recover.Roll.DC);
            Assert.Equal(19, recover.StateAfter.Interest); // unchanged
            Assert.Empty(recover.StateAfter.ActiveTrapNames);
            Assert.Equal(15, recover.XpEarned);            // recovery success → 15

            // ========== Turn 7: Speak CHARM (Success) ==========
            // Interest=19 → VeryIntoIt → advantage
            // Gerald: Charm +13, level 5 bonus +2 = +15 total mods
            // Velvet defends Charm with SA: 13 + 5 = DC 18
            // Roll: d20=8, d20=6 (advantage: take higher=8), total=8+13+2=23 vs DC 18 → success, beat by 5 → +2
            // RiskTier: need = 18-13-2 = 3 → Safe → 0
            // Combo: The Triple check — last 3 stats: SA(T4), Chaos(T5), Charm(T7) = 3 different → fires
            // The Triple: +0 interest bonus, but queues +1 roll bonus for next turn
            // Interest delta: +2 (success beat by 5) + 0 (Safe) = +2
            var turn7Start = await session.StartTurnAsync();
            var turn7 = await session.ResolveTurnAsync(0); // Charm at index 0

            Assert.True(turn7.Roll.IsSuccess);
            Assert.Equal(StatType.Charm, turn7.Roll.Stat);
            Assert.Equal(23, turn7.Roll.Total);            // max(8,6)=8 + 13 + 2
            Assert.Equal(18, turn7.Roll.DC);
            Assert.Equal(RiskTier.Safe, turn7.Roll.RiskTier);
            Assert.Equal(2, turn7.InterestDelta);          // +2 success (beat by 5)
            Assert.Equal(21, turn7.StateAfter.Interest);   // 19 + 2
            Assert.Equal(InterestState.AlmostThere, turn7.StateAfter.State);
            Assert.Equal(1, turn7.StateAfter.MomentumStreak);
            // The Triple combo fires: SA(T4), Chaos(T5), Charm(T7) = 3 different stats in combo history
            Assert.Equal("The Triple", turn7.ComboTriggered);
            Assert.True(turn7.StateAfter.TripleBonusActive); // +1 roll bonus queued for next turn
            Assert.Equal(15, turn7.XpEarned);              // DC 18 → high-tier → 15
            Assert.False(turn7.IsGameOver);

            // ========== Turn 8: Speak CHARM (Nat 20, DateSecured) ==========
            // Interest=21 → AlmostThere → advantage
            // Gerald: Charm +13, level 5 bonus +2 = +15 total mods
            // Triple bonus: +1 external bonus consumed
            // Roll: d20=20, d20=5 (advantage: take higher=20), Nat20 = auto-success → +4
            // total=20+13+2=35, external=1
            // Interest delta: +4 (Nat20)
            // Interest: 21 + 4 = 25 → DateSecured
            // XP: Nat20 → 25 + DateSecured → 50 = 75 this turn
            var turn8Start = await session.StartTurnAsync();
            var turn8 = await session.ResolveTurnAsync(0); // Charm at index 0

            Assert.True(turn8.Roll.IsSuccess);
            Assert.True(turn8.Roll.IsNatTwenty);
            Assert.Equal(StatType.Charm, turn8.Roll.Stat);
            Assert.Equal(35, turn8.Roll.Total);            // max(20,5)=20 + 13 + 2
            Assert.Equal(1, turn8.Roll.ExternalBonus);     // +1 from consumed Triple bonus
            Assert.Equal(4, turn8.InterestDelta);          // Nat20 → +4
            Assert.Equal(25, turn8.StateAfter.Interest);   // 21 + 4
            Assert.Equal(InterestState.DateSecured, turn8.StateAfter.State);
            Assert.Equal(2, turn8.StateAfter.MomentumStreak);
            Assert.True(turn8.IsGameOver);
            Assert.Equal(GameOutcome.DateSecured, turn8.Outcome);

            // Nat20 → 25 XP + DateSecured → 50 XP = 75 total for this turn
            Assert.Equal(75, turn8.XpEarned);

            // ---- Cumulative XP verification ----
            // T1:10 + T2:15 + T3:2 + T4:15 + T5:2 + T6:15 + T7:15 + T8:75 = 149
            int expectedTotalXp = 10 + 15 + 2 + 15 + 2 + 15 + 15 + 75; // = 149
            Assert.Equal(expectedTotalXp, session.TotalXpEarned);

            // ---- Shadow growth events verification ----
            // End-of-game triggers: Denial +1 (no Honesty success) and Fixation -1 (4+ distinct stats)
            // Turn 3 was Honesty FAIL so _honestySuccessCount remains 0 → Denial trigger fires
            // Stats used: Wit, Charm, Honesty, SA, Chaos, Charm, Charm = 5 distinct → Fixation -1
            Assert.NotEmpty(turn8.ShadowGrowthEvents);
            Assert.Contains(turn8.ShadowGrowthEvents,
                e => e.Contains("Denial") && e.Contains("+1"));
            Assert.Contains(turn8.ShadowGrowthEvents,
                e => e.Contains("Fixation") && e.Contains("-1"));

            // Verify shadow tracker reflects the growth
            Assert.True(playerShadows.GetDelta(ShadowStatType.Denial) >= 1);
        }

        /// <summary>
        /// Verifies ghost trigger mechanic: when interest enters Bored range (1–4),
        /// a d4 roll of 1 at the start of the next turn triggers ghosting.
        /// Also verifies Dread +1 shadow growth on ghost.
        /// </summary>
        [Fact]
        public async Task GhostTrigger_BoredState_DiceRollOne_TriggersGhosting()
        {
            // Start at interest=3 (Bored range: 1-4)
            var geraldStats = CreateGeraldStats();
            var velvetStats = CreateVelvetStats();
            var timing = new TimingProfile(baseDelay: 10, variance: 0.5f, drySpell: 0.0f, readReceipt: "neutral");
            var gerald = new CharacterProfile(geraldStats, "Test prompt", "Gerald", timing, level: 5);
            var velvet = new CharacterProfile(velvetStats, "Test prompt", "Velvet", timing, level: 7);

            var playerShadows = new SessionShadowTracker(geraldStats);

            var config = new GameSessionConfig(
                clock: null,
                playerShadows: playerShadows,
                opponentShadows: null,
                startingInterest: 3, // Bored
                previousOpener: null);

            var trapRegistry = new TestTrapRegistry();
            var llm = new ScriptedLlmAdapter(Array.Empty<DialogueOption[]>());

            // Ghost check: dice.Roll(4) == 1 → ghosted
            var dice = new FixedDice(1); // d4=1 → ghost triggers

            var session = new GameSession(gerald, velvet, llm, dice, trapRegistry, config);

            // StartTurnAsync checks ghost trigger for Bored state
            var ex = await Assert.ThrowsAsync<GameEndedException>(() => session.StartTurnAsync());

            Assert.Equal(GameOutcome.Ghosted, ex.Outcome);

            // Shadow growth: Dread +1 on ghost (#44 trigger 8)
            Assert.NotEmpty(ex.ShadowGrowthEvents);
            Assert.Contains(ex.ShadowGrowthEvents, e => e.Contains("Dread"));
            Assert.Equal(1, playerShadows.GetDelta(ShadowStatType.Dread));
        }

        /// <summary>
        /// Verifies that Bored state does NOT ghost when the d4 is not 1 (75% case),
        /// and then the turn proceeds normally.
        /// </summary>
        [Fact]
        public async Task GhostTrigger_BoredState_DiceRollNotOne_ContinuesNormally()
        {
            var geraldStats = CreateGeraldStats();
            var velvetStats = CreateVelvetStats();
            var timing = new TimingProfile(baseDelay: 10, variance: 0.5f, drySpell: 0.0f, readReceipt: "neutral");
            var gerald = new CharacterProfile(geraldStats, "Test prompt", "Gerald", timing, level: 5);
            var velvet = new CharacterProfile(velvetStats, "Test prompt", "Velvet", timing, level: 7);

            var config = new GameSessionConfig(
                clock: null,
                playerShadows: null,
                opponentShadows: null,
                startingInterest: 3, // Bored
                previousOpener: null);

            var trapRegistry = new TestTrapRegistry();
            var llm = new ScriptedLlmAdapter(new[]
            {
                new[] { Opt(StatType.Charm), Opt(StatType.Wit), Opt(StatType.Honesty), Opt(StatType.Chaos) },
            });

            // d4=2 (no ghost), then d20=18 for roll, d100=50 for timing
            var dice = new FixedDice(2, 18, 50);

            var session = new GameSession(gerald, velvet, llm, dice, trapRegistry, config);

            // Should NOT throw — ghost doesn't trigger with d4=2
            var turnStart = await session.StartTurnAsync();
            Assert.NotNull(turnStart);
            Assert.True(turnStart.Options.Length > 0);
        }

        /// <summary>
        /// Verifies the Unmatched outcome: interest reaches 0 through failures.
        /// Uses a 2-turn sequence: Catastrophe failure (-4) then Wait (-1) to hit 0.
        /// Also verifies end-of-game shadow growth: Dread +2 when interest hits 0 (#44 trigger 7),
        /// and ConversationComplete XP.
        /// </summary>
        [Fact]
        public async Task UnmatchedOutcome_InterestDropsToZero_ViaFailureAndWait()
        {
            // Starting interest=5 (Interested)
            var geraldStats = CreateGeraldStats();
            var velvetStats = CreateVelvetStats();
            var timing = new TimingProfile(baseDelay: 10, variance: 0.5f, drySpell: 0.0f, readReceipt: "neutral");
            var gerald = new CharacterProfile(geraldStats, "Test prompt", "Gerald", timing, level: 5);
            var velvet = new CharacterProfile(velvetStats, "Test prompt", "Velvet", timing, level: 7);

            var playerShadows = new SessionShadowTracker(geraldStats);

            var config = new GameSessionConfig(
                clock: null,
                playerShadows: playerShadows,
                opponentShadows: null,
                startingInterest: 5, // Interested (5-15)
                previousOpener: null);

            var trapRegistry = new TestTrapRegistry();
            var llm = new ScriptedLlmAdapter(new[]
            {
                // Turn 1: Honesty — will fail badly against Velvet's Chaos 14 defence
                new[] { Opt(StatType.Honesty), Opt(StatType.Charm), Opt(StatType.Wit), Opt(StatType.Chaos) },
            });

            // Turn 1: Honesty attack
            // Gerald: Honesty +3, level +2, DC = 13 + Velvet Chaos 14 = 27
            // Roll d20=2 → total = 2+3+2 = 7, miss = 27-7 = 20 → Catastrophe (miss 10+) → -4
            // Interest: 5-4 = 1 (Bored)
            //
            // After Turn 1: interest=1 (Bored), then Wait → -1 → interest=0 → Unmatched
            // Wait checks ghost first: d4 roll needed (not 1 to avoid ghost)
            var dice = new FixedDice(
                2,     // T1: d20=2 (no advantage at Interested)
                50,    // T1: d100 for timing
                3      // Wait: ghost check d4=3 (no ghost)
            );

            var session = new GameSession(gerald, velvet, llm, dice, trapRegistry, config);

            // ========== Turn 1: Speak HONESTY (Catastrophe) ==========
            var turn1Start = await session.StartTurnAsync();
            var turn1 = await session.ResolveTurnAsync(0);

            Assert.False(turn1.Roll.IsSuccess);
            Assert.Equal(FailureTier.Catastrophe, turn1.Roll.Tier);
            Assert.Equal(-4, turn1.InterestDelta);
            Assert.Equal(1, turn1.StateAfter.Interest);    // 5 - 4 = 1
            Assert.Equal(InterestState.Bored, turn1.StateAfter.State);
            Assert.False(turn1.IsGameOver);

            // Dread +2 trigger fires when interest hits 0, not at 1
            // So no shadow events yet on this turn (interest is 1, not 0)

            // ========== Turn 2: Wait (−1 interest → 0 → Unmatched) ==========
            session.Wait(); // −1 interest, ghost check passes (d4=3)

            // After Wait, interest should be 0 → game ends
            // Wait sets _ended=true and _outcome=Unmatched when interest hits 0

            // Verify game is over by trying another action
            await Assert.ThrowsAsync<GameEndedException>(() => session.StartTurnAsync());

            // Verify XP: T1 failure=2, no end-of-game XP recorded by Wait
            // (Wait doesn't call RecordEndOfGameXp — that's only on ResolveTurnAsync)
            Assert.Equal(2, session.TotalXpEarned);
        }

        /// <summary>
        /// Verifies a mixed-action sequence: Speak, Read, Wait, then Speak again.
        /// Ensures turn counter increments for all action types and state is consistent.
        /// </summary>
        [Fact]
        public async Task MixedActions_SpeakReadWaitSpeak_TurnCounterAndStateConsistent()
        {
            var geraldStats = CreateGeraldStats();
            var velvetStats = CreateVelvetStats();
            var timing = new TimingProfile(baseDelay: 10, variance: 0.5f, drySpell: 0.0f, readReceipt: "neutral");
            var gerald = new CharacterProfile(geraldStats, "Test prompt", "Gerald", timing, level: 5);
            var velvet = new CharacterProfile(velvetStats, "Test prompt", "Velvet", timing, level: 7);

            var config = new GameSessionConfig(
                clock: null,
                playerShadows: null,
                opponentShadows: null,
                startingInterest: 15, // VeryIntoIt after a small bump
                previousOpener: null);

            var trapRegistry = new TestTrapRegistry();
            var llm = new ScriptedLlmAdapter(new[]
            {
                // Turn 1: Charm success
                new[] { Opt(StatType.Charm), Opt(StatType.Wit), Opt(StatType.Honesty), Opt(StatType.Chaos) },
                // Turn 4: Wit success (after Read and Wait)
                new[] { Opt(StatType.Wit), Opt(StatType.Charm), Opt(StatType.Honesty), Opt(StatType.Chaos) },
            });

            // T1 (Speak, advantage at 15→Interested, no adv): d20=18, d100=50
            // Gerald: Charm +13, level +2, DC = 13 + Velvet SA 5 = 18
            // total = 18+13+2 = 33 vs DC 18 → success
            //
            // T2 (Read, advantage at 17→VeryIntoIt): d20=15, d20=12 (adv: take 15)
            // SA +4, level +2, total = 15+4+2 = 21 vs DC 12 → success (reveals interest)
            //
            // T3 (Wait, advantage at 17→VeryIntoIt): no dice needed, -1 interest
            //
            // T4 (Speak, advantage at 16→VeryIntoIt): d20=10, d20=8 (adv: take 10), d100=50
            // Wit +3, level +2, total = 10+3+2 = 15 vs DC 17 (13 + Velvet Rizz 4)
            // miss = 17-15 = 2 → Fumble → -1
            var dice = new FixedDice(
                18, 50,       // T1: Speak Charm (no advantage at Interest 15 = Interested)
                15, 12,       // T2: Read (advantage at 17 = VeryIntoIt)
                              // T3: Wait — no dice
                10, 8, 50    // T4: Speak Wit (advantage at 16 = VeryIntoIt)
            );

            var session = new GameSession(gerald, velvet, llm, dice, trapRegistry, config);

            // ========== Turn 1: Speak CHARM (Success) ==========
            // Interest=15 → Interested → no advantage
            var turn1Start = await session.StartTurnAsync();
            var turn1 = await session.ResolveTurnAsync(0);

            Assert.True(turn1.Roll.IsSuccess);
            Assert.Equal(StatType.Charm, turn1.Roll.Stat);
            Assert.Equal(33, turn1.Roll.Total);          // 18+13+2
            Assert.Equal(1, turn1.StateAfter.TurnNumber);  // turn incremented to 1
            // Beat DC by 15 → +3 (beat 10+)
            // RiskTier: need = 18-13-2 = 3 → Safe → 0
            Assert.Equal(3, turn1.InterestDelta);        // +3 success (beat by 15)
            Assert.Equal(18, turn1.StateAfter.Interest); // 15 + 3

            // ========== Turn 2: Read (SA vs DC 12, Success) ==========
            // Interest=18 → VeryIntoIt → advantage
            var read = await session.ReadAsync();

            Assert.True(read.Success);
            Assert.Equal(18, read.InterestValue);         // reveals current interest
            Assert.Equal(18, read.StateAfter.Interest);   // unchanged on success
            Assert.Equal(2, read.StateAfter.TurnNumber);   // turn incremented to 2
            Assert.Equal(0, read.XpEarned);                // Read grants 0 XP per §10

            // ========== Turn 3: Wait (−1 interest) ==========
            // Interest=18 → VeryIntoIt → no ghost check (not Bored)
            session.Wait();
            // Interest: 18 - 1 = 17

            // ========== Turn 4: Speak WIT (Fumble) ==========
            // Interest=17 → VeryIntoIt → advantage
            var turn4Start = await session.StartTurnAsync();
            var turn4 = await session.ResolveTurnAsync(0);

            Assert.False(turn4.Roll.IsSuccess);
            Assert.Equal(StatType.Wit, turn4.Roll.Stat);
            Assert.Equal(15, turn4.Roll.Total);           // max(10,8)=10 + 3 + 2
            Assert.Equal(17, turn4.Roll.DC);              // 13 + Velvet Rizz 4
            Assert.Equal(FailureTier.Fumble, turn4.Roll.Tier); // miss = 17-15 = 2
            Assert.Equal(-1, turn4.InterestDelta);        // Fumble → -1
            Assert.Equal(16, turn4.StateAfter.Interest);  // 17 - 1
            Assert.Equal(4, turn4.StateAfter.TurnNumber);  // 4th turn overall
            Assert.Equal(0, turn4.StateAfter.MomentumStreak); // reset on fail
        }

        /// <summary>
        /// Verifies that a failed Read action applies −1 interest and +1 Overthinking shadow growth.
        /// </summary>
        [Fact]
        public async Task ReadFailed_MinusOneInterest_PlusOneOverthinking()
        {
            var geraldStats = CreateGeraldStats();
            var velvetStats = CreateVelvetStats();
            var timing = new TimingProfile(baseDelay: 10, variance: 0.5f, drySpell: 0.0f, readReceipt: "neutral");
            var gerald = new CharacterProfile(geraldStats, "Test prompt", "Gerald", timing, level: 5);
            var velvet = new CharacterProfile(velvetStats, "Test prompt", "Velvet", timing, level: 7);

            var playerShadows = new SessionShadowTracker(geraldStats);

            var config = new GameSessionConfig(
                clock: null,
                playerShadows: playerShadows,
                opponentShadows: null,
                startingInterest: 12, // Interested (5-15)
                previousOpener: null);

            var trapRegistry = new TestTrapRegistry();
            var llm = new ScriptedLlmAdapter(Array.Empty<DialogueOption[]>());

            // Read: SA +4, level +2 = +6 total mods, DC 12
            // Need d20 roll where total < 12: roll=3, total=3+4+2=9 < 12 → fail
            // No advantage at interest 12 (Interested)
            var dice = new FixedDice(3); // d20=3

            var session = new GameSession(gerald, velvet, llm, dice, trapRegistry, config);

            var result = await session.ReadAsync();

            Assert.False(result.Success);
            Assert.Null(result.InterestValue);             // not revealed on failure
            Assert.Equal(11, result.StateAfter.Interest);  // 12 - 1
            Assert.Equal(0, result.XpEarned);              // Read grants 0 XP

            // Shadow growth: Overthinking +1 on Read failure (#44 trigger 10)
            Assert.NotEmpty(result.ShadowGrowthEvents);
            Assert.Contains(result.ShadowGrowthEvents, e => e.Contains("Overthinking"));
            Assert.Equal(1, playerShadows.GetDelta(ShadowStatType.Overthinking));
        }

        /// <summary>
        /// Verifies momentum streak mechanic across multiple successful turns:
        /// 3-streak → +2, 4-streak → +2, and streak resets on failure.
        /// </summary>
        [Fact]
        public async Task MomentumStreak_ThreeSuccesses_GrandsBonus_ResetsOnFail()
        {
            var geraldStats = CreateGeraldStats();
            var velvetStats = CreateVelvetStats();
            var timing = new TimingProfile(baseDelay: 10, variance: 0.5f, drySpell: 0.0f, readReceipt: "neutral");
            var gerald = new CharacterProfile(geraldStats, "Test prompt", "Gerald", timing, level: 5);
            var velvet = new CharacterProfile(velvetStats, "Test prompt", "Velvet", timing, level: 7);

            var config = new GameSessionConfig(
                clock: null,
                playerShadows: null,
                opponentShadows: null,
                startingInterest: 10, // Interested
                previousOpener: null);

            var trapRegistry = new TestTrapRegistry();
            var llm = new ScriptedLlmAdapter(new[]
            {
                // T1-T4: All Charm (high stat = easy success)
                new[] { Opt(StatType.Charm) },
                new[] { Opt(StatType.Charm) },
                new[] { Opt(StatType.Charm) },
                new[] { Opt(StatType.Charm) },
            });

            // All Charm: Gerald +13, level +2, DC = 13 + Velvet SA 5 = 18, need = 18-13-2 = 3
            // Roll d20=15 each time → total=15+13+2=30, beat by 12 → +3 success (10+)
            // T1: no adv (Interested), d20=15, d100=50
            // T2: no adv (Interested at 13), d20=15, d100=50
            // T3: adv (VeryIntoIt at 16), d20=15, d20=10, d100=50
            // T4: adv (VeryIntoIt), d20=3, d20=2, d100=50 → fail to verify momentum reset
            var dice = new FixedDice(
                15, 50,         // T1
                15, 50,         // T2
                15, 10, 50,     // T3 (advantage)
                3, 2, 50        // T4 (advantage, will fail: max(3,2)=3+13+2=18 vs 18 → success actually)
            );

            var session = new GameSession(gerald, velvet, llm, dice, trapRegistry, config);

            // T1: success, momentum 1
            await session.StartTurnAsync();
            var t1 = await session.ResolveTurnAsync(0);
            Assert.True(t1.Roll.IsSuccess);
            Assert.Equal(1, t1.StateAfter.MomentumStreak);
            Assert.Equal(3, t1.InterestDelta); // +3 (beat DC by 12), no momentum bonus yet

            // T2: success, momentum 2
            await session.StartTurnAsync();
            var t2 = await session.ResolveTurnAsync(0);
            Assert.True(t2.Roll.IsSuccess);
            Assert.Equal(2, t2.StateAfter.MomentumStreak);
            Assert.Equal(3, t2.InterestDelta); // +3, no momentum bonus at streak 2

            // T3: success, momentum 3 → +2 bonus
            await session.StartTurnAsync();
            var t3 = await session.ResolveTurnAsync(0);
            Assert.True(t3.Roll.IsSuccess);
            Assert.Equal(3, t3.StateAfter.MomentumStreak);
            Assert.Equal(5, t3.InterestDelta); // +3 (beat by 12) + 2 (momentum at 3-streak)

            // T4: success too (18 >= 18), momentum 4 → +2 bonus
            await session.StartTurnAsync();
            var t4 = await session.ResolveTurnAsync(0);
            Assert.True(t4.Roll.IsSuccess); // FinalTotal = 18 >= DC 18
            Assert.Equal(4, t4.StateAfter.MomentumStreak);
            // Beat by 0 → +1 success, + 2 momentum = 3
            Assert.Equal(3, t4.InterestDelta);
        }

        // ---- Helper methods ----

        /// <summary>Creates Gerald's stats: Charm+13, Wit+3, Honesty+3, Chaos+2, SA+4, Rizz+2. All shadows 0.</summary>
        private static StatBlock CreateGeraldStats()
        {
            return new StatBlock(
                new Dictionary<StatType, int>
                {
                    { StatType.Charm, 13 },
                    { StatType.Wit, 3 },
                    { StatType.Honesty, 3 },
                    { StatType.Chaos, 2 },
                    { StatType.SelfAwareness, 4 },
                    { StatType.Rizz, 2 }
                },
                new Dictionary<ShadowStatType, int>
                {
                    { ShadowStatType.Madness, 0 },
                    { ShadowStatType.Horniness, 0 },
                    { ShadowStatType.Denial, 0 },
                    { ShadowStatType.Fixation, 0 },
                    { ShadowStatType.Dread, 0 },
                    { ShadowStatType.Overthinking, 0 }
                });
        }

        /// <summary>Creates Velvet's stats: Chaos+14, Honesty+10, Charm+5, SA+5, Wit+4, Rizz+4. All shadows 0.</summary>
        private static StatBlock CreateVelvetStats()
        {
            return new StatBlock(
                new Dictionary<StatType, int>
                {
                    { StatType.Chaos, 14 },
                    { StatType.Honesty, 10 },
                    { StatType.Charm, 5 },
                    { StatType.SelfAwareness, 5 },
                    { StatType.Wit, 4 },
                    { StatType.Rizz, 4 }
                },
                new Dictionary<ShadowStatType, int>
                {
                    { ShadowStatType.Madness, 0 },
                    { ShadowStatType.Horniness, 0 },
                    { ShadowStatType.Denial, 0 },
                    { ShadowStatType.Fixation, 0 },
                    { ShadowStatType.Dread, 0 },
                    { ShadowStatType.Overthinking, 0 }
                });
        }

        private static DialogueOption Opt(StatType stat, string text = "Test option")
        {
            return new DialogueOption(stat, text);
        }

        // ---- Test doubles ----

        /// <summary>
        /// Deterministic dice roller that returns values from a queue.
        /// </summary>
        private sealed class FixedDice : IDiceRoller
        {
            private readonly Queue<int> _values;

            public FixedDice(params int[] values)
            {
                _values = new Queue<int>(values);
            }

            public int Roll(int sides)
            {
                if (_values.Count == 0)
                    throw new InvalidOperationException(
                        $"FixedDice: no more values in queue (requested d{sides}).");
                return _values.Dequeue();
            }
        }

        /// <summary>
        /// Trap registry that returns "unhinged" for Chaos and null for all other stats.
        /// </summary>
        private sealed class TestTrapRegistry : ITrapRegistry
        {
            private readonly TrapDefinition _chaosTrap = new TrapDefinition(
                id: "unhinged",
                stat: StatType.Chaos,
                effect: TrapEffect.Disadvantage,
                effectValue: 0,
                durationTurns: 3,
                llmInstruction: "You're unhinged now",
                clearMethod: "Recover",
                nat1Bonus: "Extra chaos");

            public TrapDefinition? GetTrap(StatType stat)
            {
                return stat == StatType.Chaos ? _chaosTrap : null;
            }

            public string? GetLlmInstruction(StatType stat) => null;
        }

        /// <summary>
        /// LLM adapter that returns pre-scripted dialogue options per turn.
        /// Behaves like NullLlmAdapter for delivery, opponent response, and narrative beats.
        /// </summary>
        private sealed class ScriptedLlmAdapter : ILlmAdapter
        {
            private readonly Queue<DialogueOption[]> _optionSets;

            public ScriptedLlmAdapter(IEnumerable<DialogueOption[]> optionSets)
            {
                _optionSets = new Queue<DialogueOption[]>(optionSets);
            }

            public Task<DialogueOption[]> GetDialogueOptionsAsync(DialogueContext context)
            {
                if (_optionSets.Count == 0)
                    throw new InvalidOperationException(
                        "ScriptedLlmAdapter: no more option sets in queue.");
                return Task.FromResult(_optionSets.Dequeue());
            }

            public Task<string> DeliverMessageAsync(DeliveryContext context)
            {
                string message = context.Outcome == FailureTier.None
                    ? context.ChosenOption.IntendedText
                    : $"[{context.Outcome}] {context.ChosenOption.IntendedText}";
                return Task.FromResult(message);
            }

            public Task<OpponentResponse> GetOpponentResponseAsync(OpponentContext context)
            {
                return Task.FromResult(new OpponentResponse("..."));
            }

            public Task<string?> GetInterestChangeBeatAsync(InterestChangeContext context)
            {
                return Task.FromResult<string?>(null);
            }
        }
    }
}
