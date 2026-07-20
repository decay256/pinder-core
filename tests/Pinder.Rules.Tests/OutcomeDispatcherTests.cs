using System.Collections.Generic;
using Xunit;
using Pinder.Rules;

namespace Pinder.Rules.Tests
{
    public class OutcomeDispatcherTests
    {
        private readonly TestEffectHandler _handler = new TestEffectHandler();
        private readonly GameState _state = new GameState();

        [Fact]
        public void Dispatch_NullOutcome_DoesNothing()
        {
            OutcomeDispatcher.Dispatch(null, _state, _handler);
            Assert.Empty(_handler.InterestDeltas);
        }

        [Fact]
        public void InterestDelta_DispatchesCorrectValue()
        {
            var outcome = new Dictionary<string, object> { ["interest_delta"] = -2 };
            OutcomeDispatcher.Dispatch(outcome, _state, _handler);
            Assert.Equal(new[] { -2 }, _handler.InterestDeltas);
        }

        [Fact]
        public void Trap_True_ActivatesEmptyTrap()
        {
            var outcome = new Dictionary<string, object> { ["trap"] = true };
            OutcomeDispatcher.Dispatch(outcome, _state, _handler);
            Assert.Equal(new[] { "" }, _handler.ActivatedTraps);
        }

        [Fact]
        public void Trap_False_DoesNotActivate()
        {
            var outcome = new Dictionary<string, object> { ["trap"] = false };
            OutcomeDispatcher.Dispatch(outcome, _state, _handler);
            Assert.Empty(_handler.ActivatedTraps);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("yes")]
        [InlineData("1")]
        [InlineData(1)]
        public void Trap_MalformedBoolean_ThrowsBeforeApplyingAnyEffects(object? value)
        {
            var outcome = new Dictionary<string, object>
            {
                ["interest_delta"] = -1,
                ["trap"] = value!
            };

            var ex = Assert.Throws<System.FormatException>(() =>
                OutcomeDispatcher.Dispatch(outcome, _state, _handler));

            Assert.Contains("trap", ex.Message);
            Assert.Contains("boolean", ex.Message);
            Assert.Empty(_handler.InterestDeltas);
            Assert.Empty(_handler.ActivatedTraps);
        }

        [Fact]
        public void TrapName_ActivatesNamedTrap()
        {
            var outcome = new Dictionary<string, object> { ["trap_name"] = "Overthinking" };
            OutcomeDispatcher.Dispatch(outcome, _state, _handler);
            Assert.Equal(new[] { "Overthinking" }, _handler.ActivatedTraps);
        }

        [Fact]
        public void RollBonus_FormatsAsPositiveModifier()
        {
            var outcome = new Dictionary<string, object> { ["roll_bonus"] = 2 };
            OutcomeDispatcher.Dispatch(outcome, _state, _handler);
            Assert.Equal(new[] { "+2" }, _handler.RollModifiers);
        }

        [Fact]
        public void Effect_DispatchesStringModifier()
        {
            var outcome = new Dictionary<string, object> { ["effect"] = "advantage" };
            OutcomeDispatcher.Dispatch(outcome, _state, _handler);
            Assert.Equal(new[] { "advantage" }, _handler.RollModifiers);
        }

        [Fact]
        public void RiskTier_DispatchesTierString()
        {
            var outcome = new Dictionary<string, object> { ["risk_tier"] = "Bold" };
            OutcomeDispatcher.Dispatch(outcome, _state, _handler);
            Assert.Equal(new[] { "Bold" }, _handler.RiskTiers);
        }

        [Fact]
        public void XpMultiplier_DispatchesDoubleValue()
        {
            var outcome = new Dictionary<string, object> { ["xp_multiplier"] = 1.5 };
            OutcomeDispatcher.Dispatch(outcome, _state, _handler);
            Assert.Equal(new[] { 1.5 }, _handler.XpMultipliers);
        }

        [Fact]
        public void ShadowEffect_DispatchesShadowGrowth()
        {
            var outcome = new Dictionary<string, object>
            {
                ["shadow_effect"] = new Dictionary<string, object>
                {
                    ["shadow"] = "Madness",
                    ["delta"] = 1
                }
            };
            OutcomeDispatcher.Dispatch(outcome, _state, _handler);
            Assert.Single(_handler.ShadowGrowths);
            Assert.Equal("Madness", _handler.ShadowGrowths[0].Shadow);
            Assert.Equal(1, _handler.ShadowGrowths[0].Delta);
        }

        [Fact]
        public void StartingInterest_DispatchesAsInterestDelta()
        {
            var outcome = new Dictionary<string, object> { ["starting_interest"] = 5 };
            OutcomeDispatcher.Dispatch(outcome, _state, _handler);
            Assert.Equal(new[] { 5 }, _handler.InterestDeltas);
        }

        [Fact]
        public void UnknownKeys_ThrowBeforeApplyingAnyEffects()
        {
            var outcome = new Dictionary<string, object>
            {
                ["unknown_key"] = "whatever",
                ["interest_delta"] = -1
            };
            var ex = Assert.Throws<System.FormatException>(() =>
                OutcomeDispatcher.Dispatch(outcome, _state, _handler));
            Assert.Contains("unknown_key", ex.Message);
            Assert.Empty(_handler.InterestDeltas);
        }

        [Fact]
        public void MalformedNumericValue_ThrowsBeforeApplyingAnyEffects()
        {
            var outcome = new Dictionary<string, object>
            {
                ["interest_delta"] = -1,
                ["roll_bonus"] = "high"
            };
            var ex = Assert.Throws<System.FormatException>(() =>
                OutcomeDispatcher.Dispatch(outcome, _state, _handler));
            Assert.Contains("roll_bonus", ex.Message);
            Assert.Contains("high", ex.Message);
            Assert.Empty(_handler.InterestDeltas);
            Assert.Empty(_handler.RollModifiers);
        }

        [Fact]
        public void MultipleOutcomes_AllDispatched()
        {
            var outcome = new Dictionary<string, object>
            {
                ["interest_delta"] = -3,
                ["trap"] = true
            };
            OutcomeDispatcher.Dispatch(outcome, _state, _handler);
            Assert.Equal(new[] { -3 }, _handler.InterestDeltas);
            Assert.Single(_handler.ActivatedTraps);
        }

        [Fact]
        public void NumericCoercion_AcceptsIntegerAndFloatStringsAcrossOutcomes()
        {
            var outcome = new Dictionary<string, object>
            {
                ["interest_delta"] = "-2",
                ["roll_bonus"] = "3",
                ["xp_multiplier"] = "1.5"
            };

            OutcomeDispatcher.Dispatch(outcome, _state, _handler);

            Assert.Equal(new[] { -2 }, _handler.InterestDeltas);
            Assert.Equal(new[] { "+3" }, _handler.RollModifiers);
            Assert.Equal(new[] { 1.5 }, _handler.XpMultipliers);
        }

        [Fact]
        public void NumericCoercion_RejectsFractionalIntegerOutcomeBeforeApplyingAnyEffects()
        {
            var outcome = new Dictionary<string, object>
            {
                ["interest_delta"] = 1,
                ["roll_bonus"] = 2.5
            };

            var ex = Assert.Throws<System.FormatException>(() =>
                OutcomeDispatcher.Dispatch(outcome, _state, _handler));

            Assert.Contains("roll_bonus", ex.Message);
            Assert.Contains("whole number", ex.Message);
            Assert.Empty(_handler.InterestDeltas);
            Assert.Empty(_handler.RollModifiers);
        }

        [Fact]
        public void NumericCoercion_RejectsIntegerOutcomeOverflowBeforeApplyingAnyEffects()
        {
            var outcome = new Dictionary<string, object>
            {
                ["interest_delta"] = 1,
                ["roll_bonus"] = long.MaxValue
            };

            var ex = Assert.Throws<System.FormatException>(() =>
                OutcomeDispatcher.Dispatch(outcome, _state, _handler));

            Assert.Contains("roll_bonus", ex.Message);
            Assert.Contains("Int32", ex.Message);
            Assert.Empty(_handler.InterestDeltas);
            Assert.Empty(_handler.RollModifiers);
        }

        [Theory]
        [InlineData(double.NaN)]
        [InlineData(double.PositiveInfinity)]
        [InlineData(double.NegativeInfinity)]
        [InlineData(float.NaN)]
        [InlineData(float.PositiveInfinity)]
        [InlineData(float.NegativeInfinity)]
        [InlineData("NaN")]
        [InlineData("Infinity")]
        [InlineData("-Infinity")]
        public void NumericCoercion_RejectsNonFiniteMultiplierBeforeApplyingAnyEffects(object value)
        {
            var outcome = new Dictionary<string, object>
            {
                ["interest_delta"] = 1,
                ["xp_multiplier"] = value
            };

            var ex = Assert.Throws<System.FormatException>(() =>
                OutcomeDispatcher.Dispatch(outcome, _state, _handler));

            Assert.Contains("xp_multiplier", ex.Message);
            Assert.Contains("finite", ex.Message);
            Assert.Empty(_handler.InterestDeltas);
            Assert.Empty(_handler.XpMultipliers);
        }
    }
}
