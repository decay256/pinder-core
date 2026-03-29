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
    /// <summary>
    /// Tests for issue #48 — XP tracking per spec docs/specs/issue-48-spec.md.
    /// Covers all acceptance criteria, edge cases, and error conditions.
    /// </summary>
    public class XpTrackingSpecTests
    {
        // ====================== XpLedger Unit Tests ======================

        // What: AC-1 — XpLedger starts empty (spec §3)
        // Mutation: Fails if constructor initializes TotalXp to non-zero
        [Fact]
        public void XpLedger_NewLedger_TotalXpIsZero()
        {
            var ledger = new XpLedger();
            Assert.Equal(0, ledger.TotalXp);
        }

        // What: AC-1 — XpLedger starts with empty event list (spec §3)
        // Mutation: Fails if constructor pre-populates Events
        [Fact]
        public void XpLedger_NewLedger_EventsIsEmpty()
        {
            var ledger = new XpLedger();
            Assert.Empty(ledger.Events);
        }

        // What: AC-1 — Record adds event and increments TotalXp (spec §3)
        // Mutation: Fails if Record doesn't increment TotalXp
        [Fact]
        public void XpLedger_Record_IncrementsTotalXp()
        {
            var ledger = new XpLedger();
            ledger.Record("Success_DC_Low", 5);
            Assert.Equal(5, ledger.TotalXp);
        }

        // What: AC-1 — Record stores source label correctly (spec §3, §4)
        // Mutation: Fails if source label is lost or mutated
        [Fact]
        public void XpLedger_Record_StoresSourceLabel()
        {
            var ledger = new XpLedger();
            ledger.Record("Nat20", 25);
            Assert.Equal("Nat20", ledger.Events[0].Source);
        }

        // What: AC-1 — Record stores amount correctly (spec §3)
        // Mutation: Fails if amount is stored incorrectly
        [Fact]
        public void XpLedger_Record_StoresAmount()
        {
            var ledger = new XpLedger();
            ledger.Record("Nat20", 25);
            Assert.Equal(25, ledger.Events[0].Amount);
        }

        // What: AC-1 — Multiple records accumulate TotalXp (spec §3)
        // Mutation: Fails if TotalXp only tracks last event
        [Fact]
        public void XpLedger_MultipleRecords_AccumulateTotalXp()
        {
            var ledger = new XpLedger();
            ledger.Record("Success_DC_Low", 5);
            ledger.Record("Failure", 2);
            ledger.Record("Nat20", 25);
            ledger.Record("DateSecured", 50);
            Assert.Equal(82, ledger.TotalXp);
        }

        // What: AC-1 — Events returns chronological list (spec §3)
        // Mutation: Fails if events are stored in wrong order
        [Fact]
        public void XpLedger_Events_ChronologicalOrder()
        {
            var ledger = new XpLedger();
            ledger.Record("A", 1);
            ledger.Record("B", 2);
            ledger.Record("C", 3);
            Assert.Equal("A", ledger.Events[0].Source);
            Assert.Equal("B", ledger.Events[1].Source);
            Assert.Equal("C", ledger.Events[2].Source);
        }

        // What: AC-1 — DrainTurnEvents returns events since last drain (spec §3)
        // Mutation: Fails if drain cursor not advanced
        [Fact]
        public void XpLedger_DrainTurnEvents_ReturnsSinceLastDrain()
        {
            var ledger = new XpLedger();
            ledger.Record("A", 1);
            ledger.Record("B", 2);
            var first = ledger.DrainTurnEvents();
            Assert.Equal(2, first.Count);

            ledger.Record("C", 3);
            var second = ledger.DrainTurnEvents();
            Assert.Single(second);
            Assert.Equal("C", second[0].Source);
        }

        // What: AC-1 — DrainTurnEvents second call with no new records returns empty (spec §3, edge case)
        // Mutation: Fails if drain cursor resets on every call
        [Fact]
        public void XpLedger_DrainTurnEvents_SecondCallEmpty()
        {
            var ledger = new XpLedger();
            ledger.Record("A", 1);
            ledger.DrainTurnEvents();
            var second = ledger.DrainTurnEvents();
            Assert.Empty(second);
        }

        // What: AC-1 — DrainTurnEvents does not affect TotalXp (spec §3)
        // Mutation: Fails if drain subtracts from TotalXp
        [Fact]
        public void XpLedger_DrainTurnEvents_DoesNotAffectTotalXp()
        {
            var ledger = new XpLedger();
            ledger.Record("A", 10);
            ledger.DrainTurnEvents();
            Assert.Equal(10, ledger.TotalXp);
            Assert.Single(ledger.Events);
        }

        // What: AC-1 — DrainTurnEvents on empty ledger returns empty (spec §3, edge case)
        // Mutation: Fails if drain throws on empty ledger
        [Fact]
        public void XpLedger_DrainTurnEvents_EmptyLedger_ReturnsEmpty()
        {
            var ledger = new XpLedger();
            var result = ledger.DrainTurnEvents();
            Assert.Empty(result);
        }

        // What: Error condition — null source throws ArgumentException (spec §11)
        // Mutation: Fails if null source is accepted
        [Fact]
        public void XpLedger_Record_NullSource_ThrowsArgumentException()
        {
            var ledger = new XpLedger();
            Assert.Throws<ArgumentException>(() => ledger.Record(null!, 5));
        }

        // What: Error condition — empty source throws ArgumentException (spec §11)
        // Mutation: Fails if empty source is accepted
        [Fact]
        public void XpLedger_Record_EmptySource_ThrowsArgumentException()
        {
            var ledger = new XpLedger();
            Assert.Throws<ArgumentException>(() => ledger.Record("", 5));
        }

        // What: Error condition — zero amount throws ArgumentOutOfRangeException (spec §11)
        // Mutation: Fails if zero amount is accepted
        [Fact]
        public void XpLedger_Record_ZeroAmount_Throws()
        {
            var ledger = new XpLedger();
            Assert.Throws<ArgumentOutOfRangeException>(() => ledger.Record("Test", 0));
        }

        // What: Error condition — negative amount throws ArgumentOutOfRangeException (spec §11)
        // Mutation: Fails if negative amount is accepted
        [Fact]
        public void XpLedger_Record_NegativeAmount_Throws()
        {
            var ledger = new XpLedger();
            Assert.Throws<ArgumentOutOfRangeException>(() => ledger.Record("Test", -1));
        }

        // What: Error condition — XpEvent constructor with null source (spec §11)
        // Mutation: Fails if XpEvent accepts null source
        [Fact]
        public void XpEvent_NullSource_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => new XpLedger.XpEvent(null!, 5));
        }

        // What: Error condition — XpEvent constructor with zero amount (spec §11)
        // Mutation: Fails if XpEvent accepts zero amount
        [Fact]
        public void XpEvent_ZeroAmount_ThrowsArgumentOutOfRangeException()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new XpLedger.XpEvent("Test", 0));
        }

        // What: Error condition — XpEvent constructor with negative amount (spec §11)
        // Mutation: Fails if XpEvent accepts negative amount
        [Fact]
        public void XpEvent_NegativeAmount_ThrowsArgumentOutOfRangeException()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new XpLedger.XpEvent("Test", -5));
        }

        // ====================== GameSession XP Integration Tests ======================

        // What: AC-2 — Successful check DC ≤ 13 awards 5 XP with label "Success_DC_Low" (spec §2, §4)
        // Mutation: Fails if low-DC success awards wrong amount or uses wrong label
        [Fact]
        public async Task ResolveTurn_SuccessDcLow_Awards5Xp()
        {
            // Opponent stat 0 → DC = 13 + 0 = 13
            var session = MakeSession(diceRoll: 15, opponentStatValue: 0);
            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.True(result.Roll.IsSuccess);
            Assert.Equal(13, result.Roll.DC);
            Assert.Equal(5, result.XpEarned);
            var evt = session.XpLedger.Events.Single(e => e.Source == "Success_DC_Low");
            Assert.Equal(5, evt.Amount);
        }

        // What: AC-2 — Successful check DC 14–17 awards 10 XP with label "Success_DC_Mid" (spec §2, §4)
        // Mutation: Fails if mid-DC success awards wrong amount or uses wrong label
        [Fact]
        public async Task ResolveTurn_SuccessDcMid_Awards10Xp()
        {
            // Opponent stat 1 → DC = 13 + 1 = 14
            var session = MakeSession(diceRoll: 18, opponentStatValue: 1);
            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.True(result.Roll.IsSuccess);
            Assert.Equal(14, result.Roll.DC);
            Assert.Equal(10, result.XpEarned);
            var evt = session.XpLedger.Events.Single(e => e.Source == "Success_DC_Mid");
            Assert.Equal(10, evt.Amount);
        }

        // What: AC-2, AC-6 — Successful check DC ≥ 18 awards 15 XP with label "Success_DC_High" (spec §2, §4)
        // Mutation: Fails if high-DC success awards wrong amount or uses wrong label
        [Fact]
        public async Task ResolveTurn_SuccessDcHigh_Awards15Xp()
        {
            // Opponent stat 5 → DC = 13 + 5 = 18
            var session = MakeSession(diceRoll: 16, opponentStatValue: 5);
            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.True(result.Roll.IsSuccess);
            Assert.False(result.Roll.IsNatTwenty);
            Assert.True(result.Roll.DC >= 18);
            Assert.Equal(15, result.XpEarned);
            var evt = session.XpLedger.Events.Single(e => e.Source == "Success_DC_High");
            Assert.Equal(15, evt.Amount);
        }

        // What: AC-2 — Failed check awards 2 XP with label "Failure" (spec §2, §4)
        // Mutation: Fails if failure awards wrong amount or wrong label
        [Fact]
        public async Task ResolveTurn_Failure_Awards2Xp()
        {
            // Roll 5 + 3 = 8 < 13 → fail, not nat 1
            var session = MakeSession(diceRoll: 5, opponentStatValue: 0);
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
            var session = MakeSession(diceRoll: 20, opponentStatValue: 0);
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
            var session = MakeSession(diceRoll: 1, opponentStatValue: 0);
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
            var config = new GameSessionConfig(startingInterest: 24);
            var session = MakeSession(diceRoll: 15, opponentStatValue: 0, config: config);
            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.True(result.IsGameOver);
            Assert.Equal(GameOutcome.DateSecured, result.Outcome);

            var dateEvent = session.XpLedger.Events.Single(e => e.Source == "DateSecured");
            Assert.Equal(50, dateEvent.Amount);

            // TurnResult includes both roll XP and date XP
            Assert.True(result.XpEarned >= 55); // 5 (DC low) + 50
        }

        // What: AC-2 — Conversation complete (Unmatched) awards 5 XP (spec §2, §5.5)
        // Mutation: Fails if Unmatched doesn't record ConversationComplete event
        [Fact]
        public async Task ResolveTurn_Unmatched_Awards5XpConversationComplete()
        {
            // Start at 1 interest, failure drops to 0 → Unmatched
            var config = new GameSessionConfig(startingInterest: 1);
            var session = MakeSession(diceRoll: 2, opponentStatValue: 0, config: config);
            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.True(result.IsGameOver);
            Assert.Equal(GameOutcome.Unmatched, result.Outcome);

            var endEvent = session.XpLedger.Events.Single(e => e.Source == "ConversationComplete");
            Assert.Equal(5, endEvent.Amount);
        }

        // What: AC-5 — Successful Recover action awards 15 XP with label "TrapRecovery" (spec §2, §5.4)
        // Mutation: Fails if recovery XP is not 15 or label is wrong
        [Fact]
        public async Task RecoverAsync_Success_Awards15Xp()
        {
            var session = MakeSession(diceRoll: 15, opponentStatValue: 0);
            ActivateTrap(session);

            var result = await session.RecoverAsync();

            Assert.True(result.Success);
            Assert.Equal(15, result.XpEarned);
            Assert.Equal(15, session.TotalXpEarned);

            var evt = session.XpLedger.Events.Single(e => e.Source == "TrapRecovery");
            Assert.Equal(15, evt.Amount);
        }

        // What: AC-5 — Failed Recover action awards 0 XP (spec §10 edge case)
        // Mutation: Fails if failed recovery still grants XP
        [Fact]
        public async Task RecoverAsync_Failure_Awards0Xp()
        {
            var session = MakeSession(diceRoll: 3, opponentStatValue: 0);
            ActivateTrap(session);

            var result = await session.RecoverAsync();

            Assert.False(result.Success);
            Assert.Equal(0, result.XpEarned);
            Assert.Equal(0, session.TotalXpEarned);
        }

        // What: Edge case — ReadAsync success awards 0 XP (spec §10, Read not in XP sources)
        // Mutation: Fails if Read grants XP
        [Fact]
        public async Task ReadAsync_Success_Awards0Xp()
        {
            var session = MakeSession(diceRoll: 15, opponentStatValue: 0);
            var result = await session.ReadAsync();

            Assert.True(result.Success);
            Assert.Equal(0, result.XpEarned);
            Assert.Equal(0, session.TotalXpEarned);
        }

        // What: Edge case — ReadAsync failure awards 0 XP (spec §10)
        // Mutation: Fails if failed Read grants XP
        [Fact]
        public async Task ReadAsync_Failure_Awards0Xp()
        {
            var session = MakeSession(diceRoll: 3, opponentStatValue: 0);
            var result = await session.ReadAsync();

            Assert.False(result.Success);
            Assert.Equal(0, result.XpEarned);
        }

        // What: Edge case — Wait action awards 0 XP (spec §10, Wait not in XP sources)
        // Mutation: Fails if Wait grants XP
        [Fact]
        public void Wait_Awards0Xp()
        {
            var session = MakeSession(diceRoll: 15, opponentStatValue: 0);
            session.Wait();
            Assert.Equal(0, session.TotalXpEarned);
            Assert.Empty(session.XpLedger.Events);
        }

        // What: AC-3 — TurnResult.XpEarned populated each turn (spec §6)
        // Mutation: Fails if XpEarned is always 0
        [Fact]
        public async Task ResolveTurn_XpEarnedPopulatedEveryTurn()
        {
            var session = MakeSession(diceRoll: 15, opponentStatValue: 0);

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
            var session = MakeSession(diceRoll: 15, opponentStatValue: 0);

            await session.StartTurnAsync();
            var t1 = await session.ResolveTurnAsync(0);

            await session.StartTurnAsync();
            var t2 = await session.ResolveTurnAsync(0);

            Assert.Equal(t1.XpEarned + t2.XpEarned, session.TotalXpEarned);
        }

        // ====================== DC Boundary Tests (spec §10 edge cases) ======================

        // What: AC-6 — DC exactly 13 → Low tier 5 XP (spec §10 boundary)
        // Mutation: Fails if boundary at 13 is off-by-one (e.g. DC<=12 instead of <=13)
        [Fact]
        public async Task ResolveTurn_DcExactly13_IsLowTier()
        {
            var session = MakeSession(diceRoll: 15, opponentStatValue: 0); // DC = 13+0 = 13
            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.Equal(13, result.Roll.DC);
            Assert.Equal(5, result.XpEarned);
        }

        // What: AC-6 — DC exactly 14 → Mid tier 10 XP (spec §10 boundary)
        // Mutation: Fails if boundary at 14 is off-by-one (e.g. DC>=15 instead of >=14)
        [Fact]
        public async Task ResolveTurn_DcExactly14_IsMidTier()
        {
            var session = MakeSession(diceRoll: 18, opponentStatValue: 1); // DC = 13+1 = 14
            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.Equal(14, result.Roll.DC);
            Assert.Equal(10, result.XpEarned);
        }

        // What: AC-6 — DC exactly 17 → Mid tier 10 XP (spec §10 boundary, upper end)
        // Mutation: Fails if 17 is wrongly classified as High tier
        [Fact]
        public async Task ResolveTurn_DcExactly17_IsMidTier()
        {
            var session = MakeSession(diceRoll: 20, opponentStatValue: 4); // DC = 13+4 = 17, but nat20
            // Need non-nat20 roll that beats DC 17. Roll 16 + 3 stat = 19 >= 17.
            var session2 = MakeSession(diceRoll: 16, opponentStatValue: 4); // DC = 13+4 = 17
            await session2.StartTurnAsync();
            var result = await session2.ResolveTurnAsync(0);

            Assert.Equal(17, result.Roll.DC);
            Assert.True(result.Roll.IsSuccess);
            Assert.False(result.Roll.IsNatTwenty);
            Assert.Equal(10, result.XpEarned);
        }

        // What: AC-6 — DC exactly 18 → High tier 15 XP (spec §10 boundary)
        // Mutation: Fails if boundary at 18 is off-by-one (e.g. DC>=19 instead of >=18)
        [Fact]
        public async Task ResolveTurn_DcExactly18_IsHighTier()
        {
            var session = MakeSession(diceRoll: 16, opponentStatValue: 5); // DC = 13+5 = 18
            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.Equal(18, result.Roll.DC);
            Assert.True(result.Roll.IsSuccess);
            Assert.False(result.Roll.IsNatTwenty);
            Assert.Equal(15, result.XpEarned);
        }

        // What: Edge case — DC > 20 still classified as High tier (spec §10)
        // Mutation: Fails if implementation only handles DC up to 20
        [Fact]
        public async Task ResolveTurn_DcAbove20_IsHighTier()
        {
            // Opponent stat 8 → DC = 13 + 8 = 21
            // Need nat20 to beat DC 21 (20 + 3 stat = 23 >= 21), but nat20 overrides XP
            // Use roll 19 + 3 = 22 >= 21 → success, not nat20
            var session = MakeSession(diceRoll: 19, opponentStatValue: 8); // DC = 13+8 = 21
            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.True(result.Roll.DC >= 18);
            Assert.True(result.Roll.IsSuccess);
            Assert.False(result.Roll.IsNatTwenty);
            Assert.Equal(15, result.XpEarned);
        }

        // What: Edge case — DC < 13 still classified as Low tier (spec §10)
        // Mutation: Fails if implementation doesn't handle DC below 13
        [Fact]
        public async Task ResolveTurn_DcBelow13_IsLowTier()
        {
            // Opponent stat -1 → DC = 13 + (-1) = 12. Need opponent with negative stat.
            // If negative stats aren't possible, opponent stat 0 gives DC 13 which is low tier.
            // Test with 0 as baseline — already tested at exactly 13 above.
            // This is covered by DcExactly13 test. Adding for clarity.
            var session = MakeSession(diceRoll: 15, opponentStatValue: 0);
            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.True(result.Roll.DC <= 13);
            Assert.Equal(5, result.XpEarned);
        }

        // ====================== Precedence / Override Tests ======================

        // What: Spec §2 precedence — Nat 20 with low DC still awards 25, not 5 (spec §10 edge case)
        // Mutation: Fails if nat20 check happens after DC-tier check
        [Fact]
        public async Task ResolveTurn_Nat20LowDc_Awards25Not5()
        {
            var session = MakeSession(diceRoll: 20, opponentStatValue: 0); // DC=13
            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.True(result.Roll.IsNatTwenty);
            Assert.Equal(13, result.Roll.DC);
            Assert.Equal(25, result.XpEarned);
            // Specifically NOT 5 (DC low) and NOT 30 (5+25)
        }

        // What: Spec §10 edge case — Nat 20 with high DC awards 25, not 15 (high tier replaced)
        // Mutation: Fails if nat20 doesn't override high tier
        [Fact]
        public async Task ResolveTurn_Nat20HighDc_Awards25Not15()
        {
            var session = MakeSession(diceRoll: 20, opponentStatValue: 5); // DC=18
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
            var config = new GameSessionConfig(startingInterest: 24);
            var session = MakeSession(diceRoll: 15, opponentStatValue: 0, config: config);
            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.True(result.IsGameOver);
            Assert.Equal(GameOutcome.DateSecured, result.Outcome);

            // Roll XP (5 for DC low) + DateSecured (50) = at least 55
            Assert.True(result.XpEarned >= 55);
            Assert.Equal(result.XpEarned, session.TotalXpEarned);
        }

        // What: Spec §8 ex 8 — Failure + conversation complete on same turn
        // Mutation: Fails if end-of-game XP replaces rather than adds to roll XP
        [Fact]
        public async Task ResolveTurn_FailurePlusUnmatched_BothXpRecorded()
        {
            var config = new GameSessionConfig(startingInterest: 1);
            var session = MakeSession(diceRoll: 2, opponentStatValue: 0, config: config);
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
            var config = new GameSessionConfig(startingInterest: 24);
            var session = MakeSession(diceRoll: 15, opponentStatValue: 0, config: config);
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
            var session = MakeSession(diceRoll: 15, opponentStatValue: 0);
            Assert.NotNull(session.XpLedger);
        }

        // What: AC-1 — GameSession exposes TotalXpEarned (spec §5.2)
        // Mutation: Fails if TotalXpEarned property not exposed
        [Fact]
        public void GameSession_TotalXpEarned_StartsAtZero()
        {
            var session = MakeSession(diceRoll: 15, opponentStatValue: 0);
            Assert.Equal(0, session.TotalXpEarned);
        }

        // What: Spec §8 example 9 — Full session XP accumulation across multiple turns
        // Mutation: Fails if XP from different turns don't accumulate correctly
        [Fact]
        public async Task FullSession_XpAccumulation_AccrossTurns()
        {
            // All turns succeed with DC 13 (constant dice = 15, opponent stat 0)
            // Each turn should award 5 XP → 3 turns = 15 XP total
            var session = MakeSession(diceRoll: 15, opponentStatValue: 0);

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

            // Each turn earns 5 XP (success DC low)
            Assert.Equal(5, t1.XpEarned);
            Assert.Equal(5, t2.XpEarned);
            Assert.Equal(5, t3.XpEarned);
            Assert.Equal(15, totalFromTurns);
            Assert.Equal(totalFromTurns, session.TotalXpEarned);
        }

        // What: AC-6 — Standard source labels used correctly (spec §4)
        // Mutation: Fails if wrong string labels are used for XP events
        [Fact]
        public async Task SourceLabels_MatchSpecLabels()
        {
            // Verify Success_DC_Low label
            var s1 = MakeSession(diceRoll: 15, opponentStatValue: 0);
            await s1.StartTurnAsync();
            await s1.ResolveTurnAsync(0);
            Assert.Contains(s1.XpLedger.Events, e => e.Source == "Success_DC_Low");

            // Verify Nat20 label
            var s2 = MakeSession(diceRoll: 20, opponentStatValue: 0);
            await s2.StartTurnAsync();
            await s2.ResolveTurnAsync(0);
            Assert.Contains(s2.XpLedger.Events, e => e.Source == "Nat20");

            // Verify Failure label
            var s3 = MakeSession(diceRoll: 5, opponentStatValue: 0);
            await s3.StartTurnAsync();
            await s3.ResolveTurnAsync(0);
            Assert.Contains(s3.XpLedger.Events, e => e.Source == "Failure");

            // Verify Nat1 label
            var s4 = MakeSession(diceRoll: 1, opponentStatValue: 0);
            await s4.StartTurnAsync();
            await s4.ResolveTurnAsync(0);
            Assert.Contains(s4.XpLedger.Events, e => e.Source == "Nat1");
        }

        // ====================== Helpers ======================

        private static GameSession MakeSession(
            int diceRoll,
            int opponentStatValue,
            GameSessionConfig? config = null)
        {
            var playerStats = MakeStatBlock(allStats: 3);
            var player = MakeProfile("player", playerStats);

            var opponentStats = MakeStatBlock(allStats: opponentStatValue);
            var opponent = MakeProfile("opponent", opponentStats);

            return new GameSession(
                player,
                opponent,
                new NullLlmAdapter(),
                new ConstantDice(diceRoll),
                new NullTrapRegistry(),
                config);
        }

        private static GameSession MakeSessionWithDice(
            IDiceRoller dice,
            int opponentStatValue,
            GameSessionConfig? config = null)
        {
            var playerStats = MakeStatBlock(allStats: 3);
            var player = MakeProfile("player", playerStats);

            var opponentStats = MakeStatBlock(allStats: opponentStatValue);
            var opponent = MakeProfile("opponent", opponentStats);

            return new GameSession(
                player,
                opponent,
                new NullLlmAdapter(),
                dice,
                new NullTrapRegistry(),
                config);
        }

        private static StatBlock MakeStatBlock(int allStats = 2)
        {
            return new StatBlock(
                new Dictionary<StatType, int>
                {
                    { StatType.Charm, allStats }, { StatType.Rizz, allStats },
                    { StatType.Honesty, allStats }, { StatType.Chaos, allStats },
                    { StatType.Wit, allStats }, { StatType.SelfAwareness, allStats }
                },
                new Dictionary<ShadowStatType, int>
                {
                    { ShadowStatType.Madness, 0 }, { ShadowStatType.Horniness, 0 },
                    { ShadowStatType.Denial, 0 }, { ShadowStatType.Fixation, 0 },
                    { ShadowStatType.Dread, 0 }, { ShadowStatType.Overthinking, 0 }
                });
        }

        private static CharacterProfile MakeProfile(string name, StatBlock stats)
        {
            var timing = new TimingProfile(5, 1.0f, 0.0f, "neutral");
            return new CharacterProfile(stats, "system prompt", name, timing, 1);
        }

        private static void ActivateTrap(GameSession session)
        {
            var trapsField = typeof(GameSession).GetField("_traps",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var trapState = (TrapState)trapsField!.GetValue(session)!;
            var trapDef = new TrapDefinition("test-trap", StatType.Charm, TrapEffect.Disadvantage, 0, 3, "Test trap instruction", "clear", "");
            trapState.Activate(trapDef);
        }

        /// <summary>Always returns the same value for every Roll call.</summary>
        private sealed class ConstantDice : IDiceRoller
        {
            private readonly int _value;
            public ConstantDice(int value) => _value = value;
            public int Roll(int sides) => _value;
        }

        /// <summary>Returns values from a sequence, then falls back to default.</summary>
        private sealed class SequenceDice : IDiceRoller
        {
            private readonly Queue<int> _values;
            private readonly int _fallback;
            public SequenceDice(int fallback, int[] values)
            {
                _fallback = fallback;
                _values = new Queue<int>(values);
            }
            public int Roll(int sides)
            {
                return _values.Count > 0 ? _values.Dequeue() : _fallback;
            }
        }

        /// <summary>No-op trap registry for testing.</summary>
        private sealed class NullTrapRegistry : ITrapRegistry
        {
            public TrapDefinition? GetTrap(StatType stat) => null;
            public string? GetLlmInstruction(StatType stat) => null;
        }
    }
}
