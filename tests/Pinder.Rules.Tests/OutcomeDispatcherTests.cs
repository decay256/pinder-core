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
        public void UnknownKeys_AreSilentlyIgnored()
        {
            var outcome = new Dictionary<string, object>
            {
                ["unknown_key"] = "whatever",
                ["interest_delta"] = -1
            };
            OutcomeDispatcher.Dispatch(outcome, _state, _handler);
            Assert.Equal(new[] { -1 }, _handler.InterestDeltas);
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
    }
}
