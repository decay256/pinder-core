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
    [Trait("Category", "Core")]
    public partial class ComboSpecTests
    {
        // ============================================================
        // Edge case 1: First turn — no combo possible
        // ============================================================

        // Mutation: would catch if CheckCombo returned non-null on first turn
        [Fact]
        public void EdgeCase1_FirstTurn_CheckComboNull()
        {
            var t = new ComboTracker();
            t.RecordTurn(StatType.Wit, true);
            Assert.Null(t.CheckCombo());
        }

        // ============================================================
        // Edge case 2: Same stat twice — no combo
        // ============================================================

        // Mutation: would catch if tracker matched same-stat sequences
        [Fact]
        public void EdgeCase2_SameStatTwice_NoCombo()
        {
            var t = new ComboTracker();
            t.RecordTurn(StatType.Charm, true);
            t.RecordTurn(StatType.Charm, true);
            Assert.Null(t.CheckCombo());
        }

        // ============================================================
        // Edge case 4: Triple with repeated stats — does NOT trigger
        // ============================================================

        // Mutation: would catch if Triple counted total stats rather than distinct stats in last 3
        [Fact]
        public void EdgeCase4_Triple_RepeatedStat_DoesNotTrigger()
        {
            var t = new ComboTracker();
            t.RecordTurn(StatType.Wit, true);
            t.RecordTurn(StatType.Charm, true);
            // Turn 2 triggers The Setup
            t.RecordTurn(StatType.Wit, true); // Only 2 distinct in last 3
            // Charm→Wit is not a 2-stat combo
            Assert.Null(t.CheckCombo());
            Assert.False(t.HasTripleBonus);
        }

        // ============================================================
        // Edge case 5: Triple overlap with 2-stat combo — highest wins
        // ============================================================

        // Mutation: would catch if both combos fired or Triple won over higher-interest combo
        [Fact]
        public void EdgeCase5_TripleOverlapWith2StatCombo_HighestInterestWins()
        {
            var t = new ComboTracker();
            t.RecordTurn(StatType.Wit, true);
            t.RecordTurn(StatType.Charm, true);
            // Turn 2: The Setup fires (tested elsewhere)
            t.RecordTurn(StatType.Honesty, true);
            // Turn 3: The Reveal (Charm→Honesty, +1) AND The Triple (3 distinct, +0)
            // The Reveal has higher InterestBonus, so it wins
            var combo = t.CheckCombo();
            Assert.NotNull(combo);
            Assert.Equal("The Reveal", combo!.Name);
            Assert.Equal(1, combo.InterestBonus);
            Assert.False(combo.IsTriple);
            // Triple should NOT have set HasTripleBonus since it didn't win
            Assert.False(t.HasTripleBonus);
        }

        // ============================================================
        // Edge case 6: Triple bonus expires on fail
        // ============================================================

        // Mutation: would catch if HasTripleBonus only reset on success
        [Fact]
        public void EdgeCase6_Triple_BonusConsumedEvenOnFailingTurn()
        {
            var t = new ComboTracker();
            t.RecordTurn(StatType.Rizz, true);
            t.RecordTurn(StatType.SelfAwareness, true);
            t.RecordTurn(StatType.Chaos, true);
            Assert.True(t.HasTripleBonus);

            // Next turn fails — bonus still consumed
            t.RecordTurn(StatType.Charm, false);
            Assert.False(t.HasTripleBonus);
        }

        // ============================================================
        // Edge case 7: Triple bonus consumed by non-Speak actions
        // (via ConsumeTripleBonus, tested at tracker level)
        // ============================================================

        // Mutation: would catch if ConsumeTripleBonus didn't work independent of RecordTurn
        [Fact]
        public void EdgeCase7_ConsumeTripleBonus_WithoutRecordTurn()
        {
            var t = new ComboTracker();
            t.RecordTurn(StatType.Rizz, true);
            t.RecordTurn(StatType.SelfAwareness, true);
            t.RecordTurn(StatType.Chaos, true);
            Assert.True(t.HasTripleBonus);

            t.ConsumeTripleBonus();
            Assert.False(t.HasTripleBonus);

            // Subsequent turns should work normally (use Chaos again so last 3 = SA,Chaos,Chaos = 2 distinct, no new Triple)
            t.RecordTurn(StatType.Chaos, true);
            Assert.Null(t.CheckCombo()); // no combo (SA→Chaos is not a 2-stat combo)
        }

        // ============================================================
        // Combo chaining (Example 6 from spec)
        // ============================================================

        // Mutation: would catch if completing stat didn't count as opening stat for next combo
        [Fact]
        public void ComboChaining_CompletingStatStartsNextCombo()
        {
            var t = new ComboTracker();
            // Wit → Honesty = "The Disarm"
            t.RecordTurn(StatType.Wit, true);
            t.RecordTurn(StatType.Honesty, true);
            Assert.Equal("The Disarm", t.CheckCombo()!.Name);

            // Honesty → Chaos = "The Pivot"
            t.RecordTurn(StatType.Chaos, true);
            Assert.Equal("The Pivot", t.CheckCombo()!.Name);

            // Chaos → Rizz = "The Escalation"
            t.RecordTurn(StatType.Rizz, true);
            Assert.Equal("The Escalation", t.CheckCombo()!.Name);
        }

        // ============================================================
        // Error conditions
        // ============================================================

        // Mutation: would catch if CheckCombo threw without prior RecordTurn
        [Fact]
        public void ErrorCondition_CheckCombo_NoPriorRecordTurn_ReturnsNull()
        {
            var t = new ComboTracker();
            Assert.Null(t.CheckCombo());
        }

        // Mutation: would catch if HasTripleBonus threw on fresh tracker
        [Fact]
        public void ErrorCondition_HasTripleBonus_DefaultIsFalse()
        {
            var t = new ComboTracker();
            Assert.False(t.HasTripleBonus);
        }

        // Mutation: would catch if PeekCombo threw on non-combo stat
        [Fact]
        public void ErrorCondition_PeekCombo_StatNotInAnyCombo_ReturnsNull()
        {
            var t = new ComboTracker();
            t.RecordTurn(StatType.Rizz, true);
            // Rizz→Charm is not a defined combo
            Assert.Null(t.PeekCombo(StatType.Charm));
        }

        // ============================================================
        // ComboResult construction validation
        // ============================================================

        // Mutation: would catch if ComboResult constructor didn't preserve values
        [Fact]
        public void ComboResult_ConstructorPreservesValues()
        {
            var result = new ComboResult("Test", 5, true);
            Assert.Equal("Test", result.Name);
            Assert.Equal(5, result.InterestBonus);
            Assert.True(result.IsTriple);

            var result2 = new ComboResult("Other", 0, false);
            Assert.Equal("Other", result2.Name);
            Assert.Equal(0, result2.InterestBonus);
            Assert.False(result2.IsTriple);
        }

        // ============================================================
        // GameStateSnapshot TripleBonusActive backward compatibility
        // ============================================================

        // Mutation: would catch if default TripleBonusActive was true instead of false
        [Fact]
        public void GameStateSnapshot_TripleBonusActive_DefaultsFalse()
        {
            var snap = new GameStateSnapshot(
                interest: 10,
                state: InterestState.Interested,
                momentumStreak: 0,
                activeTrapNames: Array.Empty<string>(),
                turnNumber: 1);
            Assert.False(snap.TripleBonusActive);
        }

        // Mutation: would catch if TripleBonusActive was always false
        [Fact]
        public void GameStateSnapshot_TripleBonusActive_CanBeTrue()
        {
            var snap = new GameStateSnapshot(
                interest: 10,
                state: InterestState.Interested,
                momentumStreak: 0,
                activeTrapNames: Array.Empty<string>(),
                turnNumber: 1,
                tripleBonusActive: true);
            Assert.True(snap.TripleBonusActive);
        }

        // ============================================================
        // Reverse-order sequences should NOT trigger combos
        // ============================================================

        // Mutation: would catch if combo matching was bidirectional
        [Fact]
        public void ReverseSequence_CharmThenWit_DoesNotTriggerSetup()
        {
            var t = new ComboTracker();
            t.RecordTurn(StatType.Charm, true); // reverse of Setup (Wit→Charm)
            t.RecordTurn(StatType.Wit, true);
            Assert.Null(t.CheckCombo());
        }

        // Mutation: would catch if Honesty→Charm triggered The Reveal (should be Charm→Honesty)
        [Fact]
        public void ReverseSequence_HonestyThenCharm_DoesNotTriggerReveal()
        {
            var t = new ComboTracker();
            t.RecordTurn(StatType.Honesty, true);
            t.RecordTurn(StatType.Charm, true);
            // Honesty→Charm is not a defined combo
            Assert.Null(t.CheckCombo());
        }

        // Mutation: would catch if Rizz→Chaos triggered The Escalation (should be Chaos→Rizz)
        [Fact]
        public void ReverseSequence_RizzThenChaos_DoesNotTriggerEscalation()
        {
            var t = new ComboTracker();
            t.RecordTurn(StatType.Rizz, true);
            t.RecordTurn(StatType.Chaos, true);
            Assert.Null(t.CheckCombo());
        }

        // ============================================================
        // The Triple requires exactly 3 distinct stats in last 3 turns
        // ============================================================

        // Mutation: would catch if Triple checked all history instead of last 3 turns
        [Fact]
        public void Triple_Only3MostRecentTurnsMatter()
        {
            var t = new ComboTracker();
            // 4 turns: Rizz, SA, Rizz, Charm — last 3 are SA, Rizz, Charm = 3 distinct
            t.RecordTurn(StatType.Rizz, true);
            t.RecordTurn(StatType.SelfAwareness, true);
            t.RecordTurn(StatType.Rizz, true); // not a combo
            t.RecordTurn(StatType.Charm, true);
            // Last 3: SA, Rizz, Charm = 3 distinct → Triple
            // But also check if SA→Rizz or Rizz→Charm form 2-stat combos (they don't)
            var combo = t.CheckCombo();
            Assert.NotNull(combo);
            Assert.Equal("The Triple", combo!.Name);
        }

        // Mutation: would catch if Triple only required 2 distinct stats
        [Fact]
        public void Triple_TwoDistinctIn3Turns_DoesNotTrigger()
        {
            var t = new ComboTracker();
            t.RecordTurn(StatType.Rizz, true);
            t.RecordTurn(StatType.Charm, true);
            t.RecordTurn(StatType.Rizz, true); // only 2 distinct: Rizz, Charm
            Assert.Null(t.CheckCombo());
            Assert.False(t.HasTripleBonus);
        }

        // ============================================================
        // The Triple with failures on turns 1 and 2 (Example 7)
        // ============================================================

        // Mutation: would catch if Triple required success on all 3 turns
        [Fact]
        public void Triple_FailOnTurns1And2_SuccessOnTurn3_Triggers()
        {
            var t = new ComboTracker();
            t.RecordTurn(StatType.Wit, false);
            t.RecordTurn(StatType.Charm, false);
            t.RecordTurn(StatType.Honesty, true);
            // 3 distinct stats, success on 3rd
            // But also: Charm(fail)→Honesty(success) could be The Reveal
            // AND Triple (3 distinct). The Reveal has higher interest, wins.
            var combo = t.CheckCombo();
            Assert.NotNull(combo);
            // The Reveal: Charm→Honesty, +1 interest. Triple: +0 interest. Reveal wins.
            Assert.Equal("The Reveal", combo!.Name);
        }

        // Mutation: would catch if Triple fired when only fail on turn 1 but not a separate triple
        [Fact]
        public void Triple_FailOnTurn1Only_SuccessOnTurn3_WithNon2StatComboStats()
        {
            var t = new ComboTracker();
            t.RecordTurn(StatType.Rizz, false);   // fail
            t.RecordTurn(StatType.SelfAwareness, false); // fail
            t.RecordTurn(StatType.Chaos, true);    // success
            // 3 distinct. SA→Chaos not a combo. Rizz→SA not a combo.
            // Recovery? prev turn SA failed, current is Chaos (not SA) → no
            var combo = t.CheckCombo();
            Assert.NotNull(combo);
            Assert.Equal("The Triple", combo!.Name);
            Assert.True(combo.IsTriple);
        }

        // ============================================================
        // Long sequence: combos reset properly between sequences
        // ============================================================

        // Mutation: would catch if old history polluted new combo detection
        [Fact]
        public void LongSequence_CombosResetProperly()
        {
            var t = new ComboTracker();

            // Turns 1-2: Wit→Charm = The Setup
            t.RecordTurn(StatType.Wit, true);
            t.RecordTurn(StatType.Charm, true);
            Assert.Equal("The Setup", t.CheckCombo()!.Name);

            // Turn 3: Honesty (Charm→Honesty = The Reveal)
            t.RecordTurn(StatType.Honesty, true);
            // Charm→Honesty = The Reveal (+1) AND Wit,Charm,Honesty = Triple (+0)
            // The Reveal has higher interest bonus, wins per single-best rule
            var combo3 = t.CheckCombo();
            Assert.NotNull(combo3);
            Assert.Equal("The Reveal", combo3!.Name);

            // Turn 4: Chaos (Honesty→Chaos = The Pivot)
            t.RecordTurn(StatType.Chaos, true);
            var combo4 = t.CheckCombo();
            Assert.NotNull(combo4);
            Assert.Equal("The Pivot", combo4!.Name);
        }
    }
}
