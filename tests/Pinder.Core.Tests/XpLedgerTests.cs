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
    // ====================== XpLedger Unit Tests ======================

    [Trait("Category", "Core")]
    public class XpLedgerTests
    {
        [Fact]
        public void NewLedger_HasZeroTotalAndNoEvents()
        {
            var ledger = new XpLedger();
            Assert.Equal(0, ledger.TotalXp);
            Assert.Empty(ledger.Events);
        }

        [Fact]
        public void Record_AddsEventAndUpdatesTotalXp()
        {
            var ledger = new XpLedger();
            ledger.Record("Success_DC_Low", 5);
            Assert.Equal(5, ledger.TotalXp);
            Assert.Single(ledger.Events);
            Assert.Equal("Success_DC_Low", ledger.Events[0].Source);
            Assert.Equal(5, ledger.Events[0].Amount);
        }

        [Fact]
        public void Record_MultipleEvents_AccumulatesTotalXp()
        {
            var ledger = new XpLedger();
            ledger.Record("Success_DC_Low", 5);
            ledger.Record("Failure", 2);
            ledger.Record("Nat20", 25);
            Assert.Equal(32, ledger.TotalXp);
            Assert.Equal(3, ledger.Events.Count);
        }

        [Fact]
        public void Record_NullSource_ThrowsArgumentException()
        {
            var ledger = new XpLedger();
            Assert.Throws<ArgumentException>(() => ledger.Record(null!, 5));
        }

        [Fact]
        public void Record_EmptySource_ThrowsArgumentException()
        {
            var ledger = new XpLedger();
            Assert.Throws<ArgumentException>(() => ledger.Record("", 5));
        }

        [Fact]
        public void Record_ZeroAmount_ThrowsArgumentOutOfRangeException()
        {
            var ledger = new XpLedger();
            Assert.Throws<ArgumentOutOfRangeException>(() => ledger.Record("Nat20", 0));
        }

        [Fact]
        public void Record_NegativeAmount_ThrowsArgumentOutOfRangeException()
        {
            var ledger = new XpLedger();
            Assert.Throws<ArgumentOutOfRangeException>(() => ledger.Record("Nat20", -1));
        }

        [Fact]
        public void DrainTurnEvents_ReturnsEventsAndAdvancesCursor()
        {
            var ledger = new XpLedger();
            ledger.Record("Success_DC_Low", 5);
            ledger.Record("Failure", 2);

            var drained = ledger.DrainTurnEvents();
            Assert.Equal(2, drained.Count);
            Assert.Equal("Success_DC_Low", drained[0].Source);
            Assert.Equal("Failure", drained[1].Source);

            // Second drain returns empty
            var empty = ledger.DrainTurnEvents();
            Assert.Empty(empty);

            // TotalXp unchanged
            Assert.Equal(7, ledger.TotalXp);
            Assert.Equal(2, ledger.Events.Count);
        }

        [Fact]
        public void DrainTurnEvents_AfterNewRecord_ReturnsOnlyNew()
        {
            var ledger = new XpLedger();
            ledger.Record("Success_DC_Low", 5);
            ledger.DrainTurnEvents();

            ledger.Record("Nat20", 25);
            var drained = ledger.DrainTurnEvents();
            Assert.Single(drained);
            Assert.Equal("Nat20", drained[0].Source);
        }

        [Fact]
        public void DrainTurnEvents_NeverRecorded_ReturnsEmpty()
        {
            var ledger = new XpLedger();
            var drained = ledger.DrainTurnEvents();
            Assert.Empty(drained);
        }

        [Fact]
        public void XpEvent_NullSource_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => new XpLedger.XpEvent(null!, 5));
        }

        [Fact]
        public void XpEvent_ZeroAmount_ThrowsArgumentOutOfRangeException()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new XpLedger.XpEvent("Test", 0));
        }
    }

    // ====================== GameSession XP Integration Tests ======================

    [Trait("Category", "Core")]
    public class XpTrackingGameSessionTests
    {
        // AC-2: Normal success DC ≤ 13, Medium risk → 5*1.5=8 XP (Success_DC_Low)
        [Fact]
        public async Task ResolveTurnAsync_SuccessDcLow_Awards5Xp()
        {
            // Datee has 0 → DC = 16, need=16-3=13 → Hard (2x), base 5 → 10
            var session = MakeSession(diceRoll: 15, dateeStatValue: 0);
            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.True(result.Roll.IsSuccess);
            Assert.Equal(16, result.Roll.DC);
            Assert.Equal(10, result.XpEarned); // 5 * 2.0 = 10
            Assert.Equal(10, session.TotalXpEarned);
        }

        // AC-2: Normal success DC 14-17, Hard risk → 10*2=20 XP (Success_DC_Mid)
        [Fact]
        public async Task ResolveTurnAsync_SuccessDcMid_Awards10Xp()
        {
            // Datee has +1 → DC = 14, need=14-3=11 → Hard (2x), base 10 → 20
            var session = MakeSession(diceRoll: 18, dateeStatValue: 1);
            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.True(result.Roll.IsSuccess);
            Assert.Equal(17, result.Roll.DC);
            Assert.Equal(20, result.XpEarned); // 10 * 2.0 = 20
        }

        // AC-2: Normal success DC ≥ 18, Hard risk → 15*2=30 XP (Success_DC_High)
        [Fact]
        public async Task ResolveTurnAsync_SuccessDcHigh_Awards15Xp()
        {
            // Datee has +5 → DC = 21, need=21-3=18 → Bold (3x for XP), base 15 → 45
            var session = MakeSession(diceRoll: 19, dateeStatValue: 5);
            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.True(result.Roll.IsSuccess);
            Assert.False(result.Roll.IsNatTwenty);
            Assert.True(result.Roll.DC >= 18);
            Assert.Equal(45, result.XpEarned); // 15 * 3.0 = 45
        }

        // AC-2: Normal failure → 2 XP
        [Fact]
        public async Task ResolveTurnAsync_Failure_Awards2Xp()
        {
            // Roll 5 vs DC 13 → fail (not nat 1)
            var session = MakeSession(diceRoll: 5, dateeStatValue: 0);
            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.False(result.Roll.IsSuccess);
            Assert.False(result.Roll.IsNatOne);
            Assert.Equal(2, result.XpEarned);
        }

        // AC-2: Nat 20 → 25 XP (overrides DC-tier XP)
        [Fact]
        public async Task ResolveTurnAsync_Nat20_Awards25Xp_NotDcTierXp()
        {
            var session = MakeSession(diceRoll: 20, dateeStatValue: 0);
            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.True(result.Roll.IsNatTwenty);
            Assert.Equal(25, result.XpEarned);

            // Verify only one XP event (not nat20 + success)
            Assert.Single(session.XpLedger.Events.Where(e => e.Source == "Nat20"));
            Assert.Empty(session.XpLedger.Events.Where(e => e.Source.StartsWith("Success_DC_")));
        }

        // AC-2: Nat 1 → 10 XP (overrides failure XP)
        [Fact]
        public async Task ResolveTurnAsync_Nat1_Awards10Xp_NotFailureXp()
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

        // AC-4: Date secured grants 50 XP on final turn
        [Fact]
        public async Task ResolveTurnAsync_DateSecured_Multiplies3xPlusRollXp()
        {
            // Start at interest 24 (AlmostThere), roll success → +1 or more → 25 → DateSecured
            var config = new GameSessionConfig(clock: TestHelpers.MakeClock(), startingInterest: 24);
            var session = MakeSession(diceRoll: 15, dateeStatValue: 0, config: config);
            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.True(result.IsGameOver);
            Assert.Equal(GameOutcome.DateSecured, result.Outcome);

            // Roll XP is 10 (5 base * 2x Hard), 3x multiplier means delta is 20
            var dateEvent = session.XpLedger.Events.FirstOrDefault(e => e.Source == "OutcomeBonus_DateSecured");
            Assert.NotNull(dateEvent);
            Assert.Equal(20, dateEvent!.Amount);

            // TurnResult.XpEarned includes both roll XP and bonus
            Assert.Equal(30, result.XpEarned);
        }

        // AC-2: Conversation complete (Unmatched) → 1x XP multiplier
        [Fact]
        public async Task ResolveTurnAsync_Unmatched_Multiplies1xConversationComplete()
        {
            // Start at interest 1, failure drops to 0 → Unmatched
            var config = new GameSessionConfig(clock: TestHelpers.MakeClock(), startingInterest: 1);
            var session = MakeSession(diceRoll: 2, dateeStatValue: 0, config: config);
            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.True(result.IsGameOver);
            Assert.Equal(GameOutcome.Unmatched, result.Outcome);

            Assert.DoesNotContain(session.XpLedger.Events, e => e.Source.StartsWith("OutcomeBonus_"));
            Assert.DoesNotContain(session.XpLedger.Events, e => e.Source == "ConversationComplete");
        }

        // AC-6: DC boundary test — DC exactly 13, Medium risk → 5*1.5=8 XP
        [Fact]
        public async Task ResolveTurnAsync_DcExactly13_AwardsLowTierXp()
        {
            // need=16-3=13 → Hard (2x), base 5 → 10
            var session = MakeSession(diceRoll: 15, dateeStatValue: 0);
            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.Equal(16, result.Roll.DC);
            Assert.Equal(10, result.XpEarned); // 5 * 2.0 = 10
        }

        // AC-6: DC boundary test — DC exactly 14, Hard risk → 10*2=20 XP
        [Fact]
        public async Task ResolveTurnAsync_DcExactly14_AwardsMidTierXp()
        {
            // need=14-3=11 → Hard (2x), base 10 → 20
            var session = MakeSession(diceRoll: 18, dateeStatValue: 1);
            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.Equal(17, result.Roll.DC);
            Assert.Equal(20, result.XpEarned); // 10 * 2.0 = 20
        }

        // AC-6: Multi-turn accumulation — TotalXpEarned matches sum
        [Fact]
        public async Task MultiTurn_TotalXpEarned_MatchesSumOfTurnXp()
        {
            // Two turns: both successes with DC 13 (constant dice returns 15)
            var session = MakeSession(diceRoll: 15, dateeStatValue: 0);

            await session.StartTurnAsync();
            var t1 = await session.ResolveTurnAsync(0);

            await session.StartTurnAsync();
            var t2 = await session.ResolveTurnAsync(0);

            Assert.Equal(t1.XpEarned + t2.XpEarned, session.TotalXpEarned);
        }

        // AC-3: TurnResult.XpEarned populated each turn
        [Fact]
        public async Task ResolveTurnAsync_XpEarnedPopulated()
        {
            var session = MakeSession(diceRoll: 15, dateeStatValue: 0);
            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.True(result.XpEarned > 0);
        }

        // Edge: Nat 20 with DC ≤ 13 still awards 25, not 5+25
        [Fact]
        public async Task ResolveTurnAsync_Nat20LowDc_Awards25Not30()
        {
            var session = MakeSession(diceRoll: 20, dateeStatValue: 0);
            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.True(result.Roll.IsNatTwenty);
            Assert.Equal(16, result.Roll.DC);
            Assert.Equal(25, result.XpEarned);
        }

        // Edge: Game ends on first turn with both roll XP and end-game XP
        [Fact]
        public async Task ResolveTurnAsync_GameEndsFirstTurn_BothXpRecorded()
        {
            var config = new GameSessionConfig(clock: TestHelpers.MakeClock(), startingInterest: 24);
            var session = MakeSession(diceRoll: 15, dateeStatValue: 0, config: config);
            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.True(result.IsGameOver);
            // Should contain both roll XP and end-game XP
            Assert.True(session.XpLedger.Events.Count >= 2);
        }

        // ====================== Helpers ======================

        private static GameSession MakeSession(
            int diceRoll,
            int dateeStatValue,
            GameSessionConfig? config = null)
        {
            var playerStats = MakeStatBlock(allStats: 3);
            var player = MakeProfile("player", playerStats);

            var dateeStats = MakeStatBlock(allStats: dateeStatValue);
            var datee = MakeProfile("datee", dateeStats);

            config = config ?? new GameSessionConfig(clock: TestHelpers.MakeClock());
            return new GameSession(
                player,
                datee,
                new NullLlmAdapter(),
                new ConstantDice(diceRoll),
                new NullTrapRegistry(),
                config);
        }

        private static GameSession MakeSessionWithDice(
            IDiceRoller dice,
            int dateeStatValue,
            GameSessionConfig? config = null)
        {
            var playerStats = MakeStatBlock(allStats: 3);
            var player = MakeProfile("player", playerStats);

            var dateeStats = MakeStatBlock(allStats: dateeStatValue);
            var datee = MakeProfile("datee", dateeStats);

            config = config ?? new GameSessionConfig(clock: TestHelpers.MakeClock());
            return new GameSession(
                player,
                datee,
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
                    { ShadowStatType.Madness, 0 }, { ShadowStatType.Despair, 0 },
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
            var trapDef = new TrapDefinition("test-trap", StatType.Charm, TrapEffect.Disadvantage, 0, 3, "Test trap instruction", "clear", "");
            session.State.Traps.Activate(trapDef);
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
            public SequenceDice(params int[] values) : this(10, values) { }
            public SequenceDice(int fallback, params int[] values)
            {
                _fallback = fallback;
                _values = new Queue<int>(values);
            }
            public int Roll(int sides)
            {
                return _values.Count > 0 ? _values.Dequeue() : _fallback;
            }
        }
    }
}
