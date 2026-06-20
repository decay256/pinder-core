using System.Collections.Generic;
using Pinder.Core.Interfaces;
using Pinder.Core.Rolls;
using Xunit;

namespace Pinder.Core.Tests
{
    /// <summary>
    /// Tests for <see cref="RollEngine.ResolveCheck"/> — the single entry point for all check kinds.
    /// #901.
    /// </summary>
    public class RollEngineCheckTests
    {
        private sealed class FixedDice : IDiceRoller
        {
            private readonly Queue<int> _values;
            public FixedDice(params int[] values) => _values = new Queue<int>(values);
            public int Roll(int sides) => _values.Count > 0 ? _values.Dequeue() : 10;
        }

        // ---- OptionRoll ----

        [Fact]
        public void OptionRoll_WithModifierBag_ComputesCorrectTotalAndSuccess()
        {
            var modifiers = new[] { new NamedModifier("stat", 3), new NamedModifier("level", 2) };
            // Roll 10 + 3 + 2 = 15 vs DC 15 → success
            var check = RollEngine.ResolveCheck(RollCheckKind.OptionRoll, new FixedDice(10), modifiers, dc: 15);

            Assert.Equal(RollCheckKind.OptionRoll, check.Kind);
            Assert.Equal(10, check.DieRoll);
            Assert.Equal(10, check.UsedDieRoll);
            Assert.Null(check.SecondDieRoll);
            Assert.Equal(5, check.ModifierSum);
            Assert.Equal(15, check.Total);
            Assert.Equal(15, check.Dc);
            Assert.True(check.IsSuccess);
            Assert.Equal(FailureTier.Success, check.Tier);
            Assert.Equal(0, check.MissMargin);
        }

        [Fact]
        public void OptionRoll_Miss_ReturnsCorrectTierAndMissMargin()
        {
            var modifiers = new[] { new NamedModifier("stat", 0) };
            // Roll 8 + 0 = 8 vs DC 15 → miss by 7 → TropeTrap
            var check = RollEngine.ResolveCheck(RollCheckKind.OptionRoll, new FixedDice(8), modifiers, dc: 15);

            Assert.False(check.IsSuccess);
            Assert.Equal(7, check.MissMargin);
            Assert.Equal(FailureTier.TropeTrap, check.Tier);
        }

        // ---- Steering ----

        [Fact]
        public void Steering_WithDisadvantage_UsesLowerRoll()
        {
            var modifiers = new[] { new NamedModifier("steering", 0) };
            // Two rolls: 15 and 5 — disadvantage picks lower (5)
            var check = RollEngine.ResolveCheck(RollCheckKind.Steering, new FixedDice(15, 5), modifiers,
                dc: 16, hasDisadvantage: true);

            Assert.Equal(RollCheckKind.Steering, check.Kind);
            Assert.Equal(15, check.DieRoll);        // first roll stored
            Assert.Equal(5, check.SecondDieRoll);    // second roll stored
            Assert.Equal(5, check.UsedDieRoll);      // lower taken (disadvantage)
            Assert.False(check.IsSuccess);           // 5 < 16
        }

        [Fact]
        public void Steering_EmptyModifierBag_ModifierSumIsZero()
        {
            var check = RollEngine.ResolveCheck(RollCheckKind.Steering, new FixedDice(12),
                new NamedModifier[0], dc: 16);

            Assert.Equal(0, check.ModifierSum);
            Assert.Equal(12, check.Total);
        }

        // ---- Horniness ----

        [Fact]
        public void Horniness_EmptyModifierBag_ModifierSumIsZero()
        {
            // Roll 5 vs supplied horniness DC 15: miss by 10 → Catastrophe
            var check = RollEngine.ResolveCheck(RollCheckKind.Horniness, new FixedDice(5),
                new NamedModifier[0], dc: 15);

            Assert.Equal(RollCheckKind.Horniness, check.Kind);
            Assert.Equal(0, check.ModifierSum);
            Assert.False(check.IsSuccess);
            Assert.Equal(10, check.MissMargin);
            Assert.Equal(FailureTier.Catastrophe, check.Tier);
        }

        // ---- Shadow ----

        [Fact]
        public void Shadow_Nat20_IsNatTwentyTrue()
        {
            var check = RollEngine.ResolveCheck(RollCheckKind.Shadow, new FixedDice(20),
                new NamedModifier[0], dc: 10);

            Assert.Equal(RollCheckKind.Shadow, check.Kind);
            Assert.True(check.IsNatTwenty);
            Assert.True(check.IsSuccess);
        }

        [Fact]
        public void Shadow_WithAdvantage_UsesHigherRoll()
        {
            // Two rolls: 5 and 17 — advantage picks higher (17)
            var check = RollEngine.ResolveCheck(RollCheckKind.Shadow, new FixedDice(5, 17),
                new NamedModifier[0], dc: 10, hasAdvantage: true);

            Assert.Equal(5, check.DieRoll);
            Assert.Equal(17, check.SecondDieRoll);
            Assert.Equal(17, check.UsedDieRoll);
            Assert.True(check.IsSuccess);
        }

        // ---- ShadowGrowth ----

        [Fact]
        public void ShadowGrowth_Nat1_IsNatOneTrue()
        {
            var check = RollEngine.ResolveCheck(RollCheckKind.ShadowGrowth, new FixedDice(1),
                new NamedModifier[0], dc: 10);

            Assert.Equal(RollCheckKind.ShadowGrowth, check.Kind);
            Assert.True(check.IsNatOne);
            Assert.False(check.IsSuccess); // 1 < 10
            // Tier from FailureTierLadder: miss = 9 → TropeTrap
            Assert.Equal(FailureTier.TropeTrap, check.Tier);
        }
    }
}
