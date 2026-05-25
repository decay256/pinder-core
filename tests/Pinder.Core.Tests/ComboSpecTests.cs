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
    public partial class ComboSpecTests
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
    }
}
