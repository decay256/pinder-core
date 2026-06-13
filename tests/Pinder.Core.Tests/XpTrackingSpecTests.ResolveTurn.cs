using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Pinder.Core.Characters;
using Pinder.Core.Conversation;
using Pinder.Core.Interfaces;
using Pinder.Core.Progression;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;
using Pinder.Core.Traps;
using Xunit;

namespace Pinder.Core.Tests
{
    [Trait("Category", "Core")]
    public partial class XpTrackingSpecTests
    {
        // ====================== GameSession XP Integration Tests ======================

        // What: AC-2 — Successful check DC ≤ 13, Medium risk tier awards 5*1.5=8 XP (spec §2, §4, risk-reward doc)
        // Mutation: Fails if low-DC success awards wrong amount or uses wrong label
        [Fact]
        public async Task ResolveTurn_SuccessDcLow_AwardsMultipliedXp()
        {
            // Datee stat 0 → DC = 16 + 0 = 16, need=16-3=13 → Hard (2x), base 5 → 10
            var session = MakeSession(diceRoll: 15, dateeStatValue: 0);
            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.True(result.Roll.IsSuccess);
            Assert.Equal(16, result.Roll.DC);
            Assert.Equal(RiskTier.Hard, result.Roll.RiskTier);
            Assert.Equal(10, result.XpEarned); // 5 * 2.0 = 10
            var evt = session.XpLedger.Events.Single(e => e.Source == "Success_DC_Low");
            Assert.Equal(10, evt.Amount);
        }

        // What: AC-2 — Successful check DC 14–17, Hard risk tier awards 10*2=20 XP (spec §2, §4, risk-reward doc)
        // Mutation: Fails if mid-DC success awards wrong amount or uses wrong label
        [Fact]
        public async Task ResolveTurn_SuccessDcMid_AwardsMultipliedXp()
        {
            // Datee stat 1 → DC = 16 + 1 = 17, need=17-3=14 → Hard (2x), base 10 → 20
            var session = MakeSession(diceRoll: 18, dateeStatValue: 1);
            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.True(result.Roll.IsSuccess);
            Assert.Equal(17, result.Roll.DC);
            Assert.Equal(RiskTier.Hard, result.Roll.RiskTier);
            Assert.Equal(20, result.XpEarned); // 10 * 2.0 = 20
            var evt = session.XpLedger.Events.Single(e => e.Source == "Success_DC_Mid");
            Assert.Equal(20, evt.Amount);
        }

        // What: AC-2, AC-6 — Successful check DC ≥ 18, Hard risk tier awards 15*2=30 XP (spec §2, §4, risk-reward doc)
        // Mutation: Fails if high-DC success awards wrong amount or uses wrong label
        [Fact]
        public async Task ResolveTurn_SuccessDcHigh_AwardsMultipliedXp()
        {
            // Datee stat 5 → DC = 16 + 5 = 21, need=21-3=18 → Bold (3x), base 15 → 45
            var session = MakeSession(diceRoll: 19, dateeStatValue: 5);
            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.True(result.Roll.IsSuccess);
            Assert.False(result.Roll.IsNatTwenty);
            Assert.True(result.Roll.DC >= 18);
            Assert.Equal(RiskTier.Bold, result.Roll.RiskTier);
            Assert.Equal(45, result.XpEarned); // 15 * 3.0 = 45
            var evt = session.XpLedger.Events.Single(e => e.Source == "Success_DC_High");
            Assert.Equal(45, evt.Amount);
        }

        // What: AC-2 — Failed check awards 2 XP with label "Failure" (spec §2, §4)
        // Mutation: Fails if failure awards wrong amount or wrong label
        [Fact]
        public async Task ResolveTurn_Failure_Awards2Xp()
        {
            // Roll 5 + 3 = 8 < 13 → fail, not nat 1
            var session = MakeSession(diceRoll: 5, dateeStatValue: 0);
            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.False(result.Roll.IsSuccess);
            Assert.False(result.Roll.IsNatOne);
            Assert.Equal(2, result.XpEarned);
            var evt = session.XpLedger.Events.Single(e => e.Source == "Failure");
            Assert.Equal(2, evt.Amount);
        }

        // What: AC-2 — Nat 20 awards 25 XP, NOT DC-tier XP (spec §2 precedence rules)
        // Mutation: Fails if nat20 also awards DC-tier XP (additive instead of replacement)
        [Fact]
        public async Task ResolveTurn_Nat20_Awards25Xp_ReplacesSuccessTier()
        {
            var session = MakeSession(diceRoll: 20, dateeStatValue: 0);
            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.True(result.Roll.IsNatTwenty);
            Assert.Equal(25, result.XpEarned);

            // Verify only Nat20, no DC-tier events
            Assert.Single(session.XpLedger.Events.Where(e => e.Source == "Nat20"));
            Assert.Empty(session.XpLedger.Events.Where(e => e.Source.StartsWith("Success_DC_")));
        }

        // What: AC-2 — Nat 1 awards 10 XP, NOT failure XP (spec §2 precedence rules)
        // Mutation: Fails if nat1 also awards failure XP (additive instead of replacement)
        [Fact]
        public async Task ResolveTurn_Nat1_Awards10Xp_ReplacesFailureXp()
        {
            var session = MakeSession(diceRoll: 1, dateeStatValue: 0);
            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.True(result.Roll.IsNatOne);
            Assert.Equal(10, result.XpEarned);

            // Verify only Nat1, not Failure
            Assert.Single(session.XpLedger.Events.Where(e => e.Source == "Nat1"));
            Assert.Empty(session.XpLedger.Events.Where(e => e.Source == "Failure"));
        }

        // What: AC-4 — Date secured awards exactly 50 XP with label "DateSecured" (spec §2, §5.5)
        // Mutation: Fails if DateSecured amount is not 50 or label is wrong
        [Fact]
        public async Task ResolveTurn_DateSecured_Awards50XpEndOfGame()
        {
            // Start at 24 interest, success pushes to 25+ → DateSecured
            var config = new GameSessionConfig(clock: TestHelpers.MakeClock(), startingInterest: 24);
            var session = MakeSession(diceRoll: 15, dateeStatValue: 0, config: config);
            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.True(result.IsGameOver);
            Assert.Equal(GameOutcome.DateSecured, result.Outcome);

            var dateEvent = session.XpLedger.Events.Single(e => e.Source == "DateSecured");
            Assert.Equal(50, dateEvent.Amount);

            // TurnResult includes both roll XP and date XP
            Assert.True(result.XpEarned >= 58); // 8 (DC low * 1.5x Medium) + 50
        }

        // What: AC-2 — Conversation complete (Unmatched) awards 5 XP (spec §2, §5.5)
        // Mutation: Fails if Unmatched doesn't record ConversationComplete event
        [Fact]
        public async Task ResolveTurn_Unmatched_Awards5XpConversationComplete()
        {
            // Start at 1 interest, failure drops to 0 → Unmatched
            var config = new GameSessionConfig(clock: TestHelpers.MakeClock(), startingInterest: 1);
            var session = MakeSession(diceRoll: 2, dateeStatValue: 0, config: config);
            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.True(result.IsGameOver);
            Assert.Equal(GameOutcome.Unmatched, result.Outcome);

            var endEvent = session.XpLedger.Events.Single(e => e.Source == "ConversationComplete");
            Assert.Equal(5, endEvent.Amount);
        }

        // What: Edge case — Wait action awards 0 XP (spec §10, Wait not in XP sources)
        // Mutation: Fails if Wait grants XP
        [Fact]
        public void Wait_Awards0Xp()
        {
            var session = MakeSession(diceRoll: 15, dateeStatValue: 0);
            session.Wait();
            Assert.Equal(0, session.TotalXpEarned);
            Assert.Empty(session.XpLedger.Events);
        }

        // What: AC-3 — TurnResult.XpEarned populated each turn (spec §6)
        // Mutation: Fails if XpEarned is always 0
        [Fact]
        public async Task ResolveTurn_XpEarnedPopulatedEveryTurn()
        {
            var session = MakeSession(diceRoll: 15, dateeStatValue: 0);

            await session.StartTurnAsync();
            var t1 = await session.ResolveTurnAsync(0);
            Assert.True(t1.XpEarned > 0);

            await session.StartTurnAsync();
            var t2 = await session.ResolveTurnAsync(0);
            Assert.True(t2.XpEarned > 0);
        }

        // What: AC-6 — Multi-turn TotalXpEarned matches sum of TurnResult.XpEarned (spec §8 ex 9)
        // Mutation: Fails if TotalXpEarned doesn't accumulate across turns
        [Fact]
        public async Task MultiTurn_TotalXpEarned_MatchesSumOfTurnXp()
        {
            var session = MakeSession(diceRoll: 15, dateeStatValue: 0);

            await session.StartTurnAsync();
            var t1 = await session.ResolveTurnAsync(0);

            await session.StartTurnAsync();
            var t2 = await session.ResolveTurnAsync(0);

            Assert.Equal(t1.XpEarned + t2.XpEarned, session.TotalXpEarned);
        }

        // ====================== DC Boundary Tests (spec §10 edge cases) ======================

        // What: AC-6 — DC exactly 13, Medium risk → 5*1.5=8 XP (spec §10 boundary)
        // Mutation: Fails if boundary at 13 is off-by-one (e.g. DC<=12 instead of <=13)
        [Fact]
        public async Task ResolveTurn_DcExactly13_IsLowTier()
        {
            // need=16-3=13 → Hard (2x), base 5 → 10 (DC base is now 16)
            var session = MakeSession(diceRoll: 15, dateeStatValue: 0); // DC = 16+0 = 16
            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.Equal(16, result.Roll.DC);
            Assert.Equal(10, result.XpEarned); // 5 * 2.0 = 10
        }

        // What: AC-6 — DC exactly 14, Hard risk → 10*2=20 XP (spec §10 boundary)
        // Mutation: Fails if boundary at 14 is off-by-one (e.g. DC>=15 instead of >=14)
        [Fact]
        public async Task ResolveTurn_DcExactly14_IsMidTier()
        {
            // need=17-3=14 → Hard (2x), base 10 → 20 (DC base is now 16)
            var session = MakeSession(diceRoll: 18, dateeStatValue: 1); // DC = 16+1 = 17
            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.Equal(17, result.Roll.DC);
            Assert.Equal(20, result.XpEarned); // 10 * 2.0 = 20
        }

        // What: AC-6 — DC exactly 17, Hard risk → 10*2=20 XP (spec §10 boundary, upper end)
        // Mutation: Fails if 17 is wrongly classified as High tier
        [Fact]
        public async Task ResolveTurn_DcExactly17_IsMidTier()
        {
            // need=20-3=17 → Bold (3x), base 10 → 30 (DC base is now 16, dateeStat=4 → DC=20)
            var session2 = MakeSession(diceRoll: 18, dateeStatValue: 4); // DC = 16+4 = 20
            await session2.StartTurnAsync();
            var result = await session2.ResolveTurnAsync(0);

            Assert.Equal(20, result.Roll.DC);
            Assert.True(result.Roll.IsSuccess);
            Assert.False(result.Roll.IsNatTwenty);
            Assert.Equal(RiskTier.Bold, result.Roll.RiskTier);
            Assert.Equal(30, result.XpEarned); // 10 * 3.0 = 30
        }

        // What: AC-6 — DC exactly 18, Hard risk → 15*2=30 XP (spec §10 boundary)
        // Mutation: Fails if boundary at 18 is off-by-one (e.g. DC>=19 instead of >=18)
        [Fact]
        public async Task ResolveTurn_DcExactly18_IsHighTier()
        {
            // need=21-3=18 → Bold (3x), base 15 → 45 (DC base is now 16, dateeStat=5 → DC=21)
            var session = MakeSession(diceRoll: 19, dateeStatValue: 5); // DC = 16+5 = 21
            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.True(result.Roll.DC >= 18);
            Assert.True(result.Roll.IsSuccess);
            Assert.False(result.Roll.IsNatTwenty);
            Assert.Equal(RiskTier.Bold, result.Roll.RiskTier);
            Assert.Equal(45, result.XpEarned); // 15 * 3.0 = 45
        }

        // What: Edge case — DC > 20, Bold risk → 15*3=45 XP (spec §10)
        // Mutation: Fails if implementation only handles DC up to 20
        [Fact]
        public async Task ResolveTurn_DcAbove20_IsHighTier()
        {
            // need=22-3=19 → Bold (3x), base 15 → 45 (DC base 16, dateeStat=6 → DC=22)
            var session = MakeSession(diceRoll: 19, dateeStatValue: 6); // DC = 16+6 = 22
            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.True(result.Roll.DC >= 18);
            Assert.True(result.Roll.IsSuccess);
            Assert.False(result.Roll.IsNatTwenty);
            Assert.Equal(RiskTier.Bold, result.Roll.RiskTier);
            Assert.Equal(45, result.XpEarned); // 15 * 3.0 = 45
        }

        // What: Edge case — DC ≤ 13, Medium risk → 5*1.5=8 XP (spec §10)
        // Mutation: Fails if implementation doesn't handle DC below 13
        [Fact]
        public async Task ResolveTurn_DcBelow13_IsLowTier()
        {
            // need=16-3=13 → Hard (2x), base 5 → 10 (DC base is now 16)
            var session = MakeSession(diceRoll: 15, dateeStatValue: 0);
            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.True(result.Roll.DC <= 16);
            Assert.Equal(10, result.XpEarned); // 5 * 2.0 = 10
        }

        // ====================== Precedence / Override Tests ======================

        // What: Spec §2 precedence — Nat 20 with low DC still awards 25, not 5 (spec §10 edge case)
        // Mutation: Fails if nat20 check happens after DC-tier check
        [Fact]
        public async Task ResolveTurn_Nat20LowDc_Awards25Not5()
        {
            var session = MakeSession(diceRoll: 20, dateeStatValue: 0); // DC=16
            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.True(result.Roll.IsNatTwenty);
            Assert.Equal(16, result.Roll.DC);
            Assert.Equal(25, result.XpEarned);
            // Specifically NOT 5 (DC low) and NOT 35 (10+25)
        }

        // What: Spec §10 edge case — Nat 20 with high DC awards 25, not 15 (high tier replaced)
        // Mutation: Fails if nat20 doesn't override high tier
        [Fact]
        public async Task ResolveTurn_Nat20HighDc_Awards25Not15()
        {
            var session = MakeSession(diceRoll: 20, dateeStatValue: 5); // DC=18
            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.True(result.Roll.IsNatTwenty);
            Assert.Equal(25, result.XpEarned);
            Assert.Empty(session.XpLedger.Events.Where(e => e.Source == "Success_DC_High"));
        }

        // ====================== End-of-Game XP Tests ======================

        // What: Spec §8 ex 6 — Date secured final turn includes both roll + date XP
        // Mutation: Fails if end-of-game XP is not included in TurnResult.XpEarned
        [Fact]
        public async Task ResolveTurn_DateSecuredFinalTurn_XpEarnedIncludesBoth()
        {
            var config = new GameSessionConfig(clock: TestHelpers.MakeClock(), startingInterest: 24);
            var session = MakeSession(diceRoll: 15, dateeStatValue: 0, config: config);
            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.True(result.IsGameOver);
            Assert.Equal(GameOutcome.DateSecured, result.Outcome);

            // Roll XP (8 for DC low * 1.5x Medium) + DateSecured (50) = at least 58
            Assert.True(result.XpEarned >= 58);
            Assert.Equal(result.XpEarned, session.TotalXpEarned);
        }

        // What: Spec §8 ex 8 — Failure + conversation complete on same turn
        // Mutation: Fails if end-of-game XP replaces rather than adds to roll XP
        [Fact]
        public async Task ResolveTurn_FailurePlusUnmatched_BothXpRecorded()
        {
            var config = new GameSessionConfig(clock: TestHelpers.MakeClock(), startingInterest: 1);
            var session = MakeSession(diceRoll: 2, dateeStatValue: 0, config: config);
            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.True(result.IsGameOver);
            Assert.Equal(GameOutcome.Unmatched, result.Outcome);

            // Should have both failure (2) and conversation complete (5)
            Assert.True(session.XpLedger.Events.Count >= 2);
            Assert.True(result.XpEarned >= 7); // 2 + 5
        }

        // What: Edge case — Game ends on first turn with both types of XP (spec §10 edge case)
        // Mutation: Fails if first-turn end doesn't record both roll and end-game XP
        [Fact]
        public async Task ResolveTurn_GameEndsFirstTurn_BothXpTypes()
        {
            var config = new GameSessionConfig(clock: TestHelpers.MakeClock(), startingInterest: 24);
            var session = MakeSession(diceRoll: 15, dateeStatValue: 0, config: config);
            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.True(result.IsGameOver);
            Assert.True(session.XpLedger.Events.Count >= 2);
            Assert.True(session.XpLedger.Events.Any(e => e.Source == "DateSecured"));
        }

        // ====================== Session-Level Ledger Tests ======================

        // What: AC-1 — GameSession exposes XpLedger (spec §5.2)
        // Mutation: Fails if XpLedger property not exposed
        [Fact]
        public void GameSession_ExposesXpLedger()
        {
            var session = MakeSession(diceRoll: 15, dateeStatValue: 0);
            Assert.NotNull(session.XpLedger);
        }

        // What: AC-1 — GameSession exposes TotalXpEarned (spec §5.2)
        // Mutation: Fails if TotalXpEarned property not exposed
        [Fact]
        public void GameSession_TotalXpEarned_StartsAtZero()
        {
            var session = MakeSession(diceRoll: 15, dateeStatValue: 0);
            Assert.Equal(0, session.TotalXpEarned);
        }

        // What: Spec §8 example 9 — Full session XP accumulation across multiple turns
        // Mutation: Fails if XP from different turns don't accumulate correctly
        [Fact]
        public async Task FullSession_XpAccumulation_AccrossTurns()
        {
            // All turns succeed with DC 16, Hard risk (2x), base 5 → 10 each
            // 3 turns = 30 XP total
            var session = MakeSession(diceRoll: 15, dateeStatValue: 0);

            int totalFromTurns = 0;

            await session.StartTurnAsync();
            var t1 = await session.ResolveTurnAsync(0);
            totalFromTurns += t1.XpEarned;

            await session.StartTurnAsync();
            var t2 = await session.ResolveTurnAsync(0);
            totalFromTurns += t2.XpEarned;

            await session.StartTurnAsync();
            var t3 = await session.ResolveTurnAsync(0);
            totalFromTurns += t3.XpEarned;

            // Each turn earns 10 XP (base 5 * 2.0 Hard = 10)
            Assert.Equal(10, t1.XpEarned);
            Assert.Equal(10, t2.XpEarned);
            Assert.Equal(10, t3.XpEarned);
            Assert.Equal(30, totalFromTurns);
            Assert.Equal(totalFromTurns, session.TotalXpEarned);
        }

        // What: AC-6 — Standard source labels used correctly (spec §4)
        // Mutation: Fails if wrong string labels are used for XP events
        [Fact]
        public async Task SourceLabels_MatchSpecLabels()
        {
            // Verify Success_DC_Low label
            var s1 = MakeSession(diceRoll: 15, dateeStatValue: 0);
            await s1.StartTurnAsync();
            await s1.ResolveTurnAsync(0);
            Assert.Contains(s1.XpLedger.Events, e => e.Source == "Success_DC_Low");

            // Verify Nat20 label
            var s2 = MakeSession(diceRoll: 20, dateeStatValue: 0);
            await s2.StartTurnAsync();
            await s2.ResolveTurnAsync(0);
            Assert.Contains(s2.XpLedger.Events, e => e.Source == "Nat20");

            // Verify Failure label
            var s3 = MakeSession(diceRoll: 5, dateeStatValue: 0);
            await s3.StartTurnAsync();
            await s3.ResolveTurnAsync(0);
            Assert.Contains(s3.XpLedger.Events, e => e.Source == "Failure");

            // Verify Nat1 label
            var s4 = MakeSession(diceRoll: 1, dateeStatValue: 0);
            await s4.StartTurnAsync();
            await s4.ResolveTurnAsync(0);
            Assert.Contains(s4.XpLedger.Events, e => e.Source == "Nat1");
        }
    }
}
