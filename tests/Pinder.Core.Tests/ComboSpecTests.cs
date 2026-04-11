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
    /// Spec-driven tests for Issue #46 — Combo System (§15).
    /// Tests are derived from docs/specs/issue-46-spec.md acceptance criteria and edge cases.
    /// </summary>
    [Trait("Category", "Core")]
    public class ComboSpecTests
    {
        // ============================================================
        // AC1: All 8 combos detected with correct names and bonuses
        // ============================================================

        // Mutation: would catch if The Reveal sequence was Charm→Chaos instead of Charm→Honesty
        [Fact]
        public void AC1_TheReveal_CharmHonesty_CorrectBonus()
        {
            var t = new ComboTracker();
            t.RecordTurn(StatType.Charm, true);
            t.RecordTurn(StatType.Honesty, true);
            var combo = t.CheckCombo();
            Assert.NotNull(combo);
            Assert.Equal("The Reveal", combo!.Name);
            Assert.Equal(1, combo.InterestBonus);
            Assert.False(combo.IsTriple);
        }

        // Mutation: would catch if The Read used Wit instead of SelfAwareness as first stat
        [Fact]
        public void AC1_TheRead_SAHonesty_CorrectBonus()
        {
            var t = new ComboTracker();
            t.RecordTurn(StatType.SelfAwareness, true);
            t.RecordTurn(StatType.Honesty, true);
            var combo = t.CheckCombo();
            Assert.NotNull(combo);
            Assert.Equal("The Read", combo!.Name);
            Assert.Equal(1, combo.InterestBonus);
            Assert.False(combo.IsTriple);
        }

        // Mutation: would catch if The Pivot used Chaos→Honesty instead of Honesty→Chaos
        [Fact]
        public void AC1_ThePivot_HonestyChaos_CorrectBonus()
        {
            var t = new ComboTracker();
            t.RecordTurn(StatType.Honesty, true);
            t.RecordTurn(StatType.Chaos, true);
            var combo = t.CheckCombo();
            Assert.NotNull(combo);
            Assert.Equal("The Pivot", combo!.Name);
            Assert.Equal(1, combo.InterestBonus);
        }

        // Mutation: would catch if The Escalation used Rizz→Chaos instead of Chaos→Rizz
        [Fact]
        public void AC1_TheEscalation_ChaosRizz_CorrectBonus()
        {
            var t = new ComboTracker();
            t.RecordTurn(StatType.Chaos, true);
            t.RecordTurn(StatType.Rizz, true);
            var combo = t.CheckCombo();
            Assert.NotNull(combo);
            Assert.Equal("The Escalation", combo!.Name);
            Assert.Equal(1, combo.InterestBonus);
        }

        // Mutation: would catch if The Disarm used Honesty→Wit instead of Wit→Honesty
        [Fact]
        public void AC1_TheDisarm_WitHonesty_CorrectBonus()
        {
            var t = new ComboTracker();
            t.RecordTurn(StatType.Wit, true);
            t.RecordTurn(StatType.Honesty, true);
            var combo = t.CheckCombo();
            Assert.NotNull(combo);
            Assert.Equal("The Disarm", combo!.Name);
            Assert.Equal(1, combo.InterestBonus);
        }

        // Mutation: would catch if Recovery InterestBonus was 1 instead of 2
        [Fact]
        public void AC1_TheRecovery_InterestBonusIs2()
        {
            var t = new ComboTracker();
            t.RecordTurn(StatType.Charm, false);
            t.RecordTurn(StatType.SelfAwareness, true);
            var combo = t.CheckCombo();
            Assert.NotNull(combo);
            Assert.Equal("The Recovery", combo!.Name);
            Assert.Equal(2, combo.InterestBonus);
            Assert.False(combo.IsTriple);
        }

        // Mutation: would catch if Triple had InterestBonus != 0 or IsTriple = false
        [Fact]
        public void AC1_TheTriple_ZeroInterestAndIsTripleTrue()
        {
            var t = new ComboTracker();
            t.RecordTurn(StatType.Rizz, true);
            t.RecordTurn(StatType.SelfAwareness, true);
            t.RecordTurn(StatType.Chaos, true);
            var combo = t.CheckCombo();
            Assert.NotNull(combo);
            Assert.Equal("The Triple", combo!.Name);
            Assert.Equal(0, combo.InterestBonus);
            Assert.True(combo.IsTriple);
        }

        // ============================================================
        // AC2: Interest bonus applied on success, NOT on failure
        // ============================================================

        // Mutation: would catch if combo bonus was applied even when completing roll fails
        [Theory]
        [InlineData(StatType.Wit, StatType.Charm)]
        [InlineData(StatType.Charm, StatType.Honesty)]
        [InlineData(StatType.SelfAwareness, StatType.Honesty)]
        [InlineData(StatType.Honesty, StatType.Chaos)]
        [InlineData(StatType.Chaos, StatType.Rizz)]
        [InlineData(StatType.Wit, StatType.Honesty)]
        public void AC2_TwoStatCombo_FailOnSecond_NoCombo(StatType first, StatType second)
        {
            var t = new ComboTracker();
            t.RecordTurn(first, true);
            t.RecordTurn(second, false); // fail on completing roll
            var combo = t.CheckCombo();
            Assert.Null(combo);
        }

        // ============================================================
        // AC3: The Recovery — any fail → SA success
        // ============================================================

        // Mutation: would catch if Recovery only worked with specific failing stat (e.g., Charm only)
        [Theory]
        [InlineData(StatType.Charm)]
        [InlineData(StatType.Wit)]
        [InlineData(StatType.Honesty)]
        [InlineData(StatType.Chaos)]
        [InlineData(StatType.Rizz)]
        [InlineData(StatType.SelfAwareness)]
        public void AC3_Recovery_AnyFailingStat_Triggers(StatType failingStat)
        {
            var t = new ComboTracker();
            t.RecordTurn(failingStat, false);
            t.RecordTurn(StatType.SelfAwareness, true);
            var combo = t.CheckCombo();
            Assert.NotNull(combo);
            Assert.Equal("The Recovery", combo!.Name);
            Assert.Equal(2, combo.InterestBonus);
        }

        // Mutation: would catch if Recovery triggered when previous turn succeeded
        [Fact]
        public void AC3_Recovery_PreviousSuccess_DoesNotTrigger()
        {
            var t = new ComboTracker();
            t.RecordTurn(StatType.Charm, true); // success, not fail
            t.RecordTurn(StatType.SelfAwareness, true);
            Assert.Null(t.CheckCombo());
        }

        // Mutation: would catch if Recovery triggered on SA fail (must succeed)
        [Fact]
        public void AC3_Recovery_SAFails_DoesNotTrigger()
        {
            var t = new ComboTracker();
            t.RecordTurn(StatType.Charm, false);
            t.RecordTurn(StatType.SelfAwareness, false); // SA fails
            Assert.Null(t.CheckCombo());
        }

        // Mutation: would catch if Recovery required specific stat instead of "any fail"
        // Edge case 3: Multiple consecutive failures before Recovery
        [Fact]
        public void AC3_Recovery_MultipleFailsThenSA_StillTriggers()
        {
            var t = new ComboTracker();
            t.RecordTurn(StatType.Wit, false);
            t.RecordTurn(StatType.Charm, false);
            t.RecordTurn(StatType.SelfAwareness, true);
            var combo = t.CheckCombo();
            Assert.NotNull(combo);
            Assert.Equal("The Recovery", combo!.Name);
        }

        // Mutation: would catch if Recovery triggered with non-SA stat after fail
        [Fact]
        public void AC3_Recovery_FailThenNonSA_DoesNotTrigger()
        {
            var t = new ComboTracker();
            t.RecordTurn(StatType.Charm, false);
            t.RecordTurn(StatType.Wit, true); // not SA
            // Wit after Charm is not a defined combo (Charm→Wit not in table)
            Assert.Null(t.CheckCombo());
        }

        // ============================================================
        // AC4: The Triple — roll bonus next turn
        // ============================================================

        // Mutation: would catch if HasTripleBonus was not set after Triple combo
        [Fact]
        public void AC4_Triple_SetsHasTripleBonus()
        {
            var t = new ComboTracker();
            Assert.False(t.HasTripleBonus);
            t.RecordTurn(StatType.Rizz, true);
            t.RecordTurn(StatType.SelfAwareness, true);
            t.RecordTurn(StatType.Chaos, true);
            Assert.True(t.HasTripleBonus);
        }

        // Mutation: would catch if HasTripleBonus persisted beyond one turn
        [Fact]
        public void AC4_Triple_BonusConsumedOnNextRecordTurn()
        {
            var t = new ComboTracker();
            t.RecordTurn(StatType.Rizz, true);
            t.RecordTurn(StatType.SelfAwareness, true);
            t.RecordTurn(StatType.Chaos, true);
            Assert.True(t.HasTripleBonus);

            // Next RecordTurn consumes it (use Chaos again so last 3 = SA,Chaos,Chaos = only 2 distinct, no new Triple)
            t.RecordTurn(StatType.Chaos, true);
            Assert.False(t.HasTripleBonus);
        }

        // Mutation: would catch if ConsumeTripleBonus didn't clear the flag
        [Fact]
        public void AC4_Triple_ConsumeTripleBonus_ClearsFlag()
        {
            var t = new ComboTracker();
            t.RecordTurn(StatType.Rizz, true);
            t.RecordTurn(StatType.SelfAwareness, true);
            t.RecordTurn(StatType.Chaos, true);
            Assert.True(t.HasTripleBonus);

            t.ConsumeTripleBonus();
            Assert.False(t.HasTripleBonus);
        }

        // Mutation: would catch if ConsumeTripleBonus threw when no bonus active
        [Fact]
        public void AC4_Triple_ConsumeTripleBonus_WhenNoBonusIsNoop()
        {
            var t = new ComboTracker();
            Assert.False(t.HasTripleBonus);
            t.ConsumeTripleBonus(); // should not throw
            Assert.False(t.HasTripleBonus);
        }

        // Mutation: would catch if Triple triggered even when 3rd roll fails
        [Fact]
        public void AC4_Triple_FailOn3rd_DoesNotTrigger()
        {
            var t = new ComboTracker();
            t.RecordTurn(StatType.Rizz, true);
            t.RecordTurn(StatType.SelfAwareness, true);
            t.RecordTurn(StatType.Chaos, false);
            Assert.Null(t.CheckCombo());
            Assert.False(t.HasTripleBonus);
        }

        // ============================================================
        // AC5: PeekCombo populates DialogueOption.ComboName
        // ============================================================

        // Mutation: would catch if PeekCombo mutated internal state
        [Fact]
        public void AC5_PeekCombo_IsIdempotent()
        {
            var t = new ComboTracker();
            t.RecordTurn(StatType.Wit, true);

            // Peek multiple times — same result, no state change
            Assert.Equal("The Setup", t.PeekCombo(StatType.Charm));
            Assert.Equal("The Setup", t.PeekCombo(StatType.Charm));
            Assert.Equal("The Disarm", t.PeekCombo(StatType.Honesty));

            // Verify peek didn't record any turn
            t.RecordTurn(StatType.Charm, true);
            var combo = t.CheckCombo();
            Assert.NotNull(combo);
            Assert.Equal("The Setup", combo!.Name); // still correct
        }

        // Mutation: would catch if PeekCombo returned non-null on turn 1
        [Fact]
        public void AC5_PeekCombo_Turn1_AlwaysNull()
        {
            var t = new ComboTracker();
            foreach (StatType stat in Enum.GetValues(typeof(StatType)))
            {
                Assert.Null(t.PeekCombo(stat));
            }
        }

        // Mutation: would catch if PeekCombo didn't show Recovery after fail
        [Fact]
        public void AC5_PeekCombo_Recovery_AfterFail_ShowsSA()
        {
            var t = new ComboTracker();
            t.RecordTurn(StatType.Wit, false);
            Assert.Equal("The Recovery", t.PeekCombo(StatType.SelfAwareness));
        }

        // Mutation: would catch if PeekCombo showed Recovery after success
        [Fact]
        public void AC5_PeekCombo_Recovery_AfterSuccess_NoSA()
        {
            var t = new ComboTracker();
            t.RecordTurn(StatType.Wit, true);
            // After success, PeekCombo(SA) should NOT show Recovery
            // (Wit→SA is not a defined 2-stat combo either)
            Assert.Null(t.PeekCombo(StatType.SelfAwareness));
        }

        // Mutation: would catch if PeekCombo didn't show 2-stat combos after a fail
        // Per spec: only the COMPLETING roll needs to succeed, prior turn success/fail doesn't matter
        // for 2-stat combos (only Recovery specifically requires a prior fail)
        [Fact]
        public void AC5_PeekCombo_AfterFail_TwoStatCombosStillShow()
        {
            var t = new ComboTracker();
            t.RecordTurn(StatType.Charm, false);
            // Charm→Honesty = The Reveal (Charm fail doesn't prevent it — only completing roll matters)
            Assert.Equal("The Reveal", t.PeekCombo(StatType.Honesty));
            // Also Recovery should show for SA
            Assert.Equal("The Recovery", t.PeekCombo(StatType.SelfAwareness));
            // Charm→Charm = no combo
            Assert.Null(t.PeekCombo(StatType.Charm));
        }

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

    // ============================================================
    // GameSession Integration Tests for Combo System
    // ============================================================

    [Trait("Category", "Core")]
    public class ComboGameSessionSpecTests
    {
        private static CharacterProfile MakeProfile(string name, int allStats = 2)
        {
            return new CharacterProfile(
                stats: TestHelpers.MakeStatBlock(allStats),
                assembledSystemPrompt: $"You are {name}.",
                displayName: name,
                timing: new TimingProfile(5, 0.0f, 0.0f, "neutral"),
                level: 1);
        }

        // Mutation: would catch if GameSession didn't apply combo interest bonus to total delta
        [Fact]
        public async Task AC2_Integration_ComboInterestBonusAddsToTotalDelta()
        {
            // Setup: Wit success → Charm success (The Setup, +1)
            // DC = 13 + 2 = 15. Roll 15: 15+2 = 17 >= 15 → success (beat by 2 → SuccessScale +1)
            var dice = new FixedDice(
                5,  // Constructor: horniness roll (1d10)
                15, 50, // Turn 1: Wit
                15, 50  // Turn 2: Charm
            );

            var llm = new ComboTestLlmAdapter();
            llm.EnqueueOptions(new DialogueOption(StatType.Wit, "Witty"));
            llm.EnqueueOptions(new DialogueOption(StatType.Charm, "Charming"));

            var session = new GameSession(MakeProfile("P"), MakeProfile("O", 0), llm, dice, new NullTrapRegistry(), new GameSessionConfig(clock: TestHelpers.MakeClock()));

            await session.StartTurnAsync();
            var r1 = await session.ResolveTurnAsync(0);
            Assert.True(r1.Roll.IsSuccess);

            await session.StartTurnAsync();
            var r2 = await session.ResolveTurnAsync(0);
            Assert.True(r2.Roll.IsSuccess);
            Assert.Equal("The Setup", r2.ComboTriggered);

            // Verify combo bonus (+1) is included in interest delta
            // SuccessScale(+1 for beat by 1) + RiskTierBonus(Hard:+3) + combo(+1) = 5
            Assert.Equal(5, r2.InterestDelta);
        }

        // Mutation: would catch if GameSession set ComboTriggered even on failed roll
        [Fact]
        public async Task AC2_Integration_NoComboOnFailedRoll()
        {
            var dice = new FixedDice(
                5,  // Constructor: horniness roll (1d10)
                15, 50, // Turn 1: Wit success
                5, 50   // Turn 2: Charm fail (5+2=7 < 15)
            );

            var llm = new ComboTestLlmAdapter();
            llm.EnqueueOptions(new DialogueOption(StatType.Wit, "Witty"));
            llm.EnqueueOptions(new DialogueOption(StatType.Charm, "Charming"));

            var session = new GameSession(MakeProfile("P"), MakeProfile("O", 0), llm, dice, new NullTrapRegistry(), new GameSessionConfig(clock: TestHelpers.MakeClock()));

            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);

            await session.StartTurnAsync();
            var r2 = await session.ResolveTurnAsync(0);
            Assert.False(r2.Roll.IsSuccess);
            Assert.Null(r2.ComboTriggered);
        }

        // Mutation: would catch if TurnResult.ComboTriggered wasn't populated
        [Fact]
        public async Task AC6_Integration_TurnResultComboTriggeredPopulated()
        {
            var dice = new FixedDice(
                5,  // Constructor: horniness roll (1d10)
                15, 50, // Turn 1
                15, 50  // Turn 2
            );

            var llm = new ComboTestLlmAdapter();
            llm.EnqueueOptions(new DialogueOption(StatType.Chaos, "Chaos"));
            llm.EnqueueOptions(new DialogueOption(StatType.Rizz, "Rizz"));

            var session = new GameSession(MakeProfile("P"), MakeProfile("O", 0), llm, dice, new NullTrapRegistry(), new GameSessionConfig(clock: TestHelpers.MakeClock()));

            await session.StartTurnAsync();
            var r1 = await session.ResolveTurnAsync(0);
            Assert.Null(r1.ComboTriggered); // first turn, no combo

            await session.StartTurnAsync();
            var r2 = await session.ResolveTurnAsync(0);
            Assert.Equal("The Escalation", r2.ComboTriggered);
        }

        // Mutation: would catch if PeekCombo wasn't called during StartTurnAsync
        [Fact]
        public async Task AC5_Integration_StartTurnPopulatesComboNames()
        {
            var dice = new FixedDice(
                5,  // Constructor: horniness roll (1d10)
                15, 50, // Turn 1
                15, 50  // Turn 2
            );

            var llm = new ComboTestLlmAdapter();
            llm.EnqueueOptions(new DialogueOption(StatType.Honesty, "Honest"));
            // Turn 2: Chaos would complete The Pivot, SA would not
            llm.EnqueueOptions(
                new DialogueOption(StatType.Chaos, "Chaos"),
                new DialogueOption(StatType.SelfAwareness, "SA")
            );

            var session = new GameSession(MakeProfile("P"), MakeProfile("O", 0), llm, dice, new NullTrapRegistry(), new GameSessionConfig(clock: TestHelpers.MakeClock()));

            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);

            var start2 = await session.StartTurnAsync();
            Assert.Equal("The Pivot", start2.Options[0].ComboName); // Honesty→Chaos
            Assert.Null(start2.Options[1].ComboName); // Honesty→SA is not a combo
        }

        // Mutation: would catch if GameSession didn't pass externalBonus for Triple
        [Fact]
        public async Task AC4_Integration_TripleBonusAppliedAsExternalBonus()
        {
            // 3 turns with distinct non-overlapping stats, then check external bonus on turn 4
            // Opponent allStats=0 → DC=16. Turn 2 reaches VeryIntoIt (interest 10+4+4=18) → advantage from turn 3.
            var dice = new FixedDice(
                5,  // Constructor: horniness roll (1d10)
                15, 50,      // Turn 1: Rizz (d20, d100)
                15, 50,      // Turn 2: SA (d20, d100)
                15, 15, 50,  // Turn 3: Chaos → Triple (VeryIntoIt advantage: d20, d20, d100)
                15, 15, 50   // Turn 4: advantage (d20, d20, d100)
            );

            var llm = new ComboTestLlmAdapter();
            llm.EnqueueOptions(new DialogueOption(StatType.Rizz, "R"));
            llm.EnqueueOptions(new DialogueOption(StatType.SelfAwareness, "SA"));
            llm.EnqueueOptions(new DialogueOption(StatType.Chaos, "C"));
            // Use SA again to avoid triggering a second Triple (SA,Chaos,SA = not 3 distinct)
            llm.EnqueueOptions(new DialogueOption(StatType.SelfAwareness, "SA2"));

            var session = new GameSession(MakeProfile("P"), MakeProfile("O", 0), llm, dice, new NullTrapRegistry(), new GameSessionConfig(clock: TestHelpers.MakeClock()));

            // Turns 1-2
            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);
            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);

            // Turn 3: Triple
            await session.StartTurnAsync();
            var r3 = await session.ResolveTurnAsync(0);
            Assert.Equal("The Triple", r3.ComboTriggered);
            Assert.True(r3.StateAfter.TripleBonusActive);

            // Turn 4: verify external bonus applied (triple +1 + momentum +2 from streak=3 at start, #268)
            var start4 = await session.StartTurnAsync();
            Assert.True(start4.State.TripleBonusActive);
            var r4 = await session.ResolveTurnAsync(0);
            Assert.Equal(3, r4.Roll.ExternalBonus);
            Assert.False(r4.StateAfter.TripleBonusActive); // consumed
        }

        // Mutation: would catch if Recovery combo in GameSession didn't add +2 to interest delta
        [Fact]
        public async Task AC3_Integration_RecoveryAdds2ToInterestDelta()
        {
            // Turn 1: fail, Turn 2: SA success → Recovery (+2)
            var dice = new FixedDice(
                5,  // Constructor: horniness roll (1d10)
                5, 50,  // Turn 1: fail (5+2=7 < 15)
                15, 50  // Turn 2: success (15+2=17 >= 15)
            );

            var llm = new ComboTestLlmAdapter();
            llm.EnqueueOptions(new DialogueOption(StatType.Wit, "Wit"));
            llm.EnqueueOptions(new DialogueOption(StatType.SelfAwareness, "SA"));

            var session = new GameSession(MakeProfile("P"), MakeProfile("O", 0), llm, dice, new NullTrapRegistry(), new GameSessionConfig(clock: TestHelpers.MakeClock()));

            await session.StartTurnAsync();
            var r1 = await session.ResolveTurnAsync(0);
            Assert.False(r1.Roll.IsSuccess);

            await session.StartTurnAsync();
            var r2 = await session.ResolveTurnAsync(0);
            Assert.True(r2.Roll.IsSuccess);
            Assert.Equal("The Recovery", r2.ComboTriggered);

            // Interest delta: SuccessScale(+1) + RiskTierBonus(Hard:+3) + combo(+2) = +6
            Assert.Equal(6, r2.InterestDelta);
        }
    }
}
