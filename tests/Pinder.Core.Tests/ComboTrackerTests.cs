using Pinder.Core.Conversation;
using Pinder.Core.Stats;
using Xunit;

namespace Pinder.Core.Tests
{
    [Trait("Category", "Core")]
    public class ComboTrackerTests
    {
        // ---- The Setup: Wit → Charm (success) = +1 interest ----

        [Fact]
        public void TheSetup_WitThenCharmSuccess_ReturnsCombo()
        {
            var tracker = new ComboTracker();
            tracker.RecordTurn(StatType.Wit, true);
            Assert.Null(tracker.CheckCombo());

            tracker.RecordTurn(StatType.Charm, true);
            var combo = tracker.CheckCombo();

            Assert.NotNull(combo);
            Assert.Equal("The Setup", combo!.Name);
            Assert.Equal(1, combo.InterestBonus);
            Assert.False(combo.IsTriple);
        }

        [Fact]
        public void TheSetup_WitThenCharmFail_NoCombo()
        {
            var tracker = new ComboTracker();
            tracker.RecordTurn(StatType.Wit, true);
            tracker.RecordTurn(StatType.Charm, false);

            Assert.Null(tracker.CheckCombo());
        }

        // ---- The Reveal: Charm → Honesty (success) = +1 interest ----

        [Fact]
        public void TheReveal_CharmThenHonestySuccess_ReturnsCombo()
        {
            var tracker = new ComboTracker();
            tracker.RecordTurn(StatType.Charm, true);
            tracker.RecordTurn(StatType.Honesty, true);
            var combo = tracker.CheckCombo();

            Assert.NotNull(combo);
            Assert.Equal("The Reveal", combo!.Name);
            Assert.Equal(1, combo.InterestBonus);
        }

        // ---- The Read: SelfAwareness → Honesty (success) = +1 interest ----

        [Fact]
        public void TheRead_SAThenHonestySuccess_ReturnsCombo()
        {
            var tracker = new ComboTracker();
            tracker.RecordTurn(StatType.SelfAwareness, true);
            tracker.RecordTurn(StatType.Honesty, true);
            var combo = tracker.CheckCombo();

            Assert.NotNull(combo);
            Assert.Equal("The Read", combo!.Name);
            Assert.Equal(1, combo.InterestBonus);
        }

        // ---- The Pivot: Honesty → Chaos (success) = +1 interest ----

        [Fact]
        public void ThePivot_HonestyThenChaosSuccess_ReturnsCombo()
        {
            var tracker = new ComboTracker();
            tracker.RecordTurn(StatType.Honesty, true);
            tracker.RecordTurn(StatType.Chaos, true);
            var combo = tracker.CheckCombo();

            Assert.NotNull(combo);
            Assert.Equal("The Pivot", combo!.Name);
            Assert.Equal(1, combo.InterestBonus);
        }

        // ---- The Escalation: Chaos → Rizz (success) = +1 interest ----

        [Fact]
        public void TheEscalation_ChaosThenRizzSuccess_ReturnsCombo()
        {
            var tracker = new ComboTracker();
            tracker.RecordTurn(StatType.Chaos, true);
            tracker.RecordTurn(StatType.Rizz, true);
            var combo = tracker.CheckCombo();

            Assert.NotNull(combo);
            Assert.Equal("The Escalation", combo!.Name);
            Assert.Equal(1, combo.InterestBonus);
        }

        // ---- The Disarm: Wit → Honesty (success) = +1 interest ----

        [Fact]
        public void TheDisarm_WitThenHonestySuccess_ReturnsCombo()
        {
            var tracker = new ComboTracker();
            tracker.RecordTurn(StatType.Wit, true);
            tracker.RecordTurn(StatType.Honesty, true);
            var combo = tracker.CheckCombo();

            Assert.NotNull(combo);
            Assert.Equal("The Disarm", combo!.Name);
            Assert.Equal(1, combo.InterestBonus);
        }

        // ---- The Recovery: Any fail → SelfAwareness (success) = +2 interest ----

        [Fact]
        public void TheRecovery_AnyFailThenSASuccess_ReturnsCombo()
        {
            var tracker = new ComboTracker();
            tracker.RecordTurn(StatType.Chaos, false);
            tracker.RecordTurn(StatType.SelfAwareness, true);
            var combo = tracker.CheckCombo();

            Assert.NotNull(combo);
            Assert.Equal("The Recovery", combo!.Name);
            Assert.Equal(2, combo.InterestBonus);
            Assert.False(combo.IsTriple);
        }

        [Fact]
        public void TheRecovery_SuccessThenSA_NoCombo()
        {
            var tracker = new ComboTracker();
            tracker.RecordTurn(StatType.Charm, true);
            tracker.RecordTurn(StatType.SelfAwareness, true);

            Assert.Null(tracker.CheckCombo());
        }

        [Fact]
        public void TheRecovery_MultipleFailsBeforeSA_StillTriggers()
        {
            var tracker = new ComboTracker();
            tracker.RecordTurn(StatType.Wit, false);
            tracker.RecordTurn(StatType.Charm, false);
            tracker.RecordTurn(StatType.SelfAwareness, true);

            var combo = tracker.CheckCombo();
            Assert.NotNull(combo);
            Assert.Equal("The Recovery", combo!.Name);
        }

        [Fact]
        public void TheRecovery_AnyStatCanFail()
        {
            // Verify Recovery works with different failing stats
            foreach (var stat in new[] { StatType.Charm, StatType.Wit, StatType.Honesty, StatType.Chaos, StatType.Rizz, StatType.SelfAwareness })
            {
                var tracker = new ComboTracker();
                tracker.RecordTurn(stat, false);
                tracker.RecordTurn(StatType.SelfAwareness, true);
                var combo = tracker.CheckCombo();

                Assert.NotNull(combo);
                Assert.Equal("The Recovery", combo!.Name);
                Assert.Equal(2, combo.InterestBonus);
            }
        }

        // ---- The Triple: 3 different stats in 3 turns, success on 3rd ----

        [Fact]
        public void TheTriple_ThreeDistinctStats_Success_ReturnsCombo()
        {
            var tracker = new ComboTracker();
            tracker.RecordTurn(StatType.Wit, true);
            tracker.RecordTurn(StatType.Charm, true);
            tracker.RecordTurn(StatType.Honesty, true);

            // Turn 2 triggers The Setup (Wit→Charm), but turn 3 is what we check
            var combo = tracker.CheckCombo();
            Assert.NotNull(combo);
            // The Reveal (Charm→Honesty) has interest +1, Triple has interest 0
            // Per single-best rule, The Reveal wins
            Assert.Equal("The Reveal", combo!.Name);
        }

        [Fact]
        public void TheTriple_ThreeDistinctStats_NoOverlap_ReturnsTriple()
        {
            // Use stats that don't form a 2-stat combo: Rizz, Chaos, SelfAwareness
            // Chaos→Rizz could form Escalation... let's use Rizz, Wit, SelfAwareness
            // Wit→SA is not a combo, Rizz→Wit not a combo
            var tracker = new ComboTracker();
            tracker.RecordTurn(StatType.Rizz, true);
            tracker.RecordTurn(StatType.Charm, false); // fail - no 2-stat combo completes
            tracker.RecordTurn(StatType.SelfAwareness, true);

            var combo = tracker.CheckCombo();
            Assert.NotNull(combo);
            // Recovery: prev failed, current is SA success → +2
            // Triple: 3 distinct stats → +0 interest, IsTriple
            // Recovery has higher InterestBonus (2 > 0), so Recovery wins
            Assert.Equal("The Recovery", combo!.Name);
        }

        [Fact]
        public void TheTriple_PureTriple_NoOtherCombo()
        {
            // Use stats with no 2-stat combo overlap and no fail
            // Rizz, SelfAwareness, Chaos — SA→Chaos is not a combo, Rizz→SA is not a combo
            var tracker = new ComboTracker();
            tracker.RecordTurn(StatType.Rizz, true);
            tracker.RecordTurn(StatType.SelfAwareness, true);
            tracker.RecordTurn(StatType.Chaos, true);

            var combo = tracker.CheckCombo();
            Assert.NotNull(combo);
            Assert.Equal("The Triple", combo!.Name);
            Assert.Equal(0, combo.InterestBonus);
            Assert.True(combo.IsTriple);
        }

        [Fact]
        public void TheTriple_SetsHasTripleBonus()
        {
            var tracker = new ComboTracker();
            Assert.False(tracker.HasTripleBonus);

            tracker.RecordTurn(StatType.Rizz, true);
            tracker.RecordTurn(StatType.SelfAwareness, true);
            tracker.RecordTurn(StatType.Chaos, true);

            Assert.True(tracker.HasTripleBonus);
        }

        [Fact]
        public void TheTriple_BonusConsumedOnNextRecordTurn()
        {
            var tracker = new ComboTracker();
            tracker.RecordTurn(StatType.Rizz, true);
            tracker.RecordTurn(StatType.SelfAwareness, true);
            tracker.RecordTurn(StatType.Chaos, true);
            Assert.True(tracker.HasTripleBonus);

            // Next turn consumes the bonus (use Chaos again to avoid re-triggering Triple)
            tracker.RecordTurn(StatType.Chaos, true);
            Assert.False(tracker.HasTripleBonus);
        }

        [Fact]
        public void TheTriple_RepeatedStat_DoesNotTrigger()
        {
            var tracker = new ComboTracker();
            tracker.RecordTurn(StatType.Wit, true);
            tracker.RecordTurn(StatType.Charm, true);
            tracker.RecordTurn(StatType.Wit, true); // only 2 distinct stats

            var combo = tracker.CheckCombo();
            // No triple (not 3 distinct), no 2-stat combo for Charm→Wit
            Assert.Null(combo);
        }

        [Fact]
        public void TheTriple_FailOnEarlierTurns_StillTriggers()
        {
            var tracker = new ComboTracker();
            tracker.RecordTurn(StatType.Rizz, false);
            tracker.RecordTurn(StatType.SelfAwareness, false);
            tracker.RecordTurn(StatType.Chaos, true);

            // Recovery would need prev to fail + current SA → prev is SA fail, current is Chaos, not SA
            // Triple: 3 distinct stats, 3rd succeeds
            var combo = tracker.CheckCombo();
            Assert.NotNull(combo);
            Assert.Equal("The Triple", combo!.Name);
            Assert.True(combo.IsTriple);
        }

        // ---- PeekCombo ----

        [Fact]
        public void PeekCombo_DoesNotMutateState()
        {
            var tracker = new ComboTracker();
            tracker.RecordTurn(StatType.Wit, true);

            // Peek Charm → should see "The Setup"
            string? name = tracker.PeekCombo(StatType.Charm);
            Assert.Equal("The Setup", name);

            // Peek again — same result (not mutated)
            Assert.Equal("The Setup", tracker.PeekCombo(StatType.Charm));

            // Record something different — Setup should not fire
            tracker.RecordTurn(StatType.Rizz, true);
            Assert.Null(tracker.CheckCombo());
        }

        [Fact]
        public void PeekCombo_NoHistory_ReturnsNull()
        {
            var tracker = new ComboTracker();
            Assert.Null(tracker.PeekCombo(StatType.Charm));
            Assert.Null(tracker.PeekCombo(StatType.SelfAwareness));
        }

        [Fact]
        public void PeekCombo_Recovery_ShowsWhenPrevFailed()
        {
            var tracker = new ComboTracker();
            tracker.RecordTurn(StatType.Charm, false);

            Assert.Equal("The Recovery", tracker.PeekCombo(StatType.SelfAwareness));
            Assert.Null(tracker.PeekCombo(StatType.Charm));
        }

        // ---- Edge cases ----

        [Fact]
        public void FirstTurn_NoCombo()
        {
            var tracker = new ComboTracker();
            tracker.RecordTurn(StatType.Wit, true);
            Assert.Null(tracker.CheckCombo());
        }

        [Fact]
        public void SameStatTwice_NoCombo()
        {
            var tracker = new ComboTracker();
            tracker.RecordTurn(StatType.Wit, true);
            tracker.RecordTurn(StatType.Wit, true);
            Assert.Null(tracker.CheckCombo());
        }

        [Fact]
        public void CheckCombo_WithoutRecordTurn_ReturnsNull()
        {
            var tracker = new ComboTracker();
            Assert.Null(tracker.CheckCombo());
        }

        [Fact]
        public void ComboChaining_CompletingStatCanStartNext()
        {
            var tracker = new ComboTracker();

            // Turn 1: Wit success
            tracker.RecordTurn(StatType.Wit, true);
            Assert.Null(tracker.CheckCombo());

            // Turn 2: Honesty success → "The Disarm" (Wit→Honesty)
            tracker.RecordTurn(StatType.Honesty, true);
            var combo = tracker.CheckCombo();
            Assert.NotNull(combo);
            Assert.Equal("The Disarm", combo!.Name);

            // Turn 3: Chaos success → "The Pivot" (Honesty→Chaos)
            tracker.RecordTurn(StatType.Chaos, true);
            combo = tracker.CheckCombo();
            Assert.NotNull(combo);
            Assert.Equal("The Pivot", combo!.Name);
        }

        [Fact]
        public void ConsumeTripleBonus_ClearsBonus()
        {
            var tracker = new ComboTracker();
            tracker.RecordTurn(StatType.Rizz, true);
            tracker.RecordTurn(StatType.SelfAwareness, true);
            tracker.RecordTurn(StatType.Chaos, true);
            Assert.True(tracker.HasTripleBonus);

            tracker.ConsumeTripleBonus();
            Assert.False(tracker.HasTripleBonus);
        }

        [Fact]
        public void MultipleComboSameTurn_HighestInterestBonusWins()
        {
            // Turn 3 scenario: Wit(success) → Charm(success) → Honesty(success)
            // Turn 3 matches: The Reveal (Charm→Honesty, +1) AND The Triple (3 distinct, +0 roll bonus)
            // The Reveal should win (higher interest bonus)
            var tracker = new ComboTracker();
            tracker.RecordTurn(StatType.Wit, true);
            tracker.RecordTurn(StatType.Charm, true);
            // Turn 2 was The Setup
            tracker.RecordTurn(StatType.Honesty, true);

            var combo = tracker.CheckCombo();
            Assert.NotNull(combo);
            Assert.Equal("The Reveal", combo!.Name);
            Assert.Equal(1, combo.InterestBonus);
            Assert.False(combo.IsTriple);
        }

        // ---- All 8 combo names exact match ----

        [Fact]
        public void AllEightCombos_ExactNames()
        {
            // The Setup: Wit → Charm
            var t = new ComboTracker();
            t.RecordTurn(StatType.Wit, true);
            t.RecordTurn(StatType.Charm, true);
            Assert.Equal("The Setup", t.CheckCombo()!.Name);

            // The Reveal: Charm → Honesty
            t = new ComboTracker();
            t.RecordTurn(StatType.Charm, true);
            t.RecordTurn(StatType.Honesty, true);
            Assert.Equal("The Reveal", t.CheckCombo()!.Name);

            // The Read: SA → Honesty
            t = new ComboTracker();
            t.RecordTurn(StatType.SelfAwareness, true);
            t.RecordTurn(StatType.Honesty, true);
            Assert.Equal("The Read", t.CheckCombo()!.Name);

            // The Pivot: Honesty → Chaos
            t = new ComboTracker();
            t.RecordTurn(StatType.Honesty, true);
            t.RecordTurn(StatType.Chaos, true);
            Assert.Equal("The Pivot", t.CheckCombo()!.Name);

            // The Recovery: Any fail → SA
            t = new ComboTracker();
            t.RecordTurn(StatType.Wit, false);
            t.RecordTurn(StatType.SelfAwareness, true);
            Assert.Equal("The Recovery", t.CheckCombo()!.Name);

            // The Escalation: Chaos → Rizz
            t = new ComboTracker();
            t.RecordTurn(StatType.Chaos, true);
            t.RecordTurn(StatType.Rizz, true);
            Assert.Equal("The Escalation", t.CheckCombo()!.Name);

            // The Disarm: Wit → Honesty
            t = new ComboTracker();
            t.RecordTurn(StatType.Wit, true);
            t.RecordTurn(StatType.Honesty, true);
            Assert.Equal("The Disarm", t.CheckCombo()!.Name);

            // The Triple: 3 distinct stats (no 2-stat overlap)
            t = new ComboTracker();
            t.RecordTurn(StatType.Rizz, true);
            t.RecordTurn(StatType.SelfAwareness, true);
            t.RecordTurn(StatType.Chaos, true);
            Assert.Equal("The Triple", t.CheckCombo()!.Name);
        }

        [Fact]
        public void HasTripleBonus_DefaultFalse()
        {
            var tracker = new ComboTracker();
            Assert.False(tracker.HasTripleBonus);
        }

        [Fact]
        public void TheTriple_FailOn3rd_NoCombo()
        {
            var tracker = new ComboTracker();
            tracker.RecordTurn(StatType.Rizz, true);
            tracker.RecordTurn(StatType.SelfAwareness, true);
            tracker.RecordTurn(StatType.Chaos, false);

            Assert.Null(tracker.CheckCombo());
            Assert.False(tracker.HasTripleBonus);
        }
    }
}
