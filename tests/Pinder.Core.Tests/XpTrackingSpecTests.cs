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
    [Trait("Category", "Core")]
    public partial class XpTrackingSpecTests
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

        // ====================== Helpers ======================

        private static GameSession MakeSession(
            int diceRoll,
            int dateeStatValue,
            GameSessionConfig? config = null,
            int playerStatValue = 3)
        {
            var playerStats = MakeStatBlock(allStats: playerStatValue);
            var player = MakeProfile("player", playerStats);

            var dateeStats = MakeStatBlock(allStats: dateeStatValue);
            var datee = MakeProfile("datee", dateeStats);

            // Clock is required; if caller did not supply a config, provide one with a zero-modifier clock.
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

            // Clock is required; if caller did not supply a config, provide one with a zero-modifier clock.
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
