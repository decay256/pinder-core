using System;
using System.Collections.Generic;
using Xunit;
using Pinder.Rules;

namespace Pinder.Rules.Tests
{
    public partial class SpecComplianceTests
    {
        // ============================================================
        // OutcomeDispatcher — Edge Cases (AC6)
        // ============================================================

        // Mutation: would catch if empty outcome calls handler methods
        [Fact]
        public void OutcomeDispatcher_EmptyOutcome_DoesNothing()
        {
            var handler = new TestEffectHandler();
            OutcomeDispatcher.Dispatch(new Dictionary<string, object>(), new GameState(), handler);
            Assert.Empty(handler.InterestDeltas);
            Assert.Empty(handler.ActivatedTraps);
            Assert.Empty(handler.RollModifiers);
            Assert.Empty(handler.RiskTiers);
            Assert.Empty(handler.XpMultipliers);
            Assert.Empty(handler.ShadowGrowths);
        }

        // Mutation: would catch if roll_bonus positive formatting is wrong
        [Fact]
        public void OutcomeDispatcher_RollBonus_PositiveValue_FormatsWithPlus()
        {
            var handler = new TestEffectHandler();
            var outcome = new Dictionary<string, object> { ["roll_bonus"] = 2 };
            OutcomeDispatcher.Dispatch(outcome, new GameState(), handler);
            Assert.Single(handler.RollModifiers);
            Assert.Equal("+2", handler.RollModifiers[0]);
        }

        // Mutation: would catch if effect "disadvantage" is not dispatched
        [Fact]
        public void OutcomeDispatcher_Effect_Disadvantage_DispatchesAsModifier()
        {
            var handler = new TestEffectHandler();
            var outcome = new Dictionary<string, object> { ["effect"] = "disadvantage" };
            OutcomeDispatcher.Dispatch(outcome, new GameState(), handler);
            Assert.Equal(new[] { "disadvantage" }, handler.RollModifiers);
        }

        // Mutation: would catch if trap_name is dispatched via wrong handler method
        [Fact]
        public void OutcomeDispatcher_TrapName_CallsActivateTrapWithName()
        {
            var handler = new TestEffectHandler();
            var outcome = new Dictionary<string, object> { ["trap_name"] = "Overthinking" };
            OutcomeDispatcher.Dispatch(outcome, new GameState(), handler);
            Assert.Single(handler.ActivatedTraps);
            Assert.Equal("Overthinking", handler.ActivatedTraps[0]);
        }

        // Mutation: would catch if xp_multiplier fails with int instead of double
        [Fact]
        public void OutcomeDispatcher_XpMultiplier_IntegerValue_DispatchesAsDouble()
        {
            var handler = new TestEffectHandler();
            var outcome = new Dictionary<string, object> { ["xp_multiplier"] = 2 };
            OutcomeDispatcher.Dispatch(outcome, new GameState(), handler);
            Assert.Single(handler.XpMultipliers);
            Assert.Equal(2.0, handler.XpMultipliers[0]);
        }

        // Mutation: would catch if zero roll_bonus is skipped
        [Fact]
        public void OutcomeDispatcher_RollBonus_Zero_Dispatches()
        {
            var handler = new TestEffectHandler();
            var outcome = new Dictionary<string, object> { ["roll_bonus"] = 0 };
            OutcomeDispatcher.Dispatch(outcome, new GameState(), handler);
            Assert.Single(handler.RollModifiers);
        }

        // Mutation: would catch if starting_interest outcome key isn't handled
        [Fact]
        public void OutcomeDispatcher_StartingInterest_DispatchesAsInterestDelta()
        {
            var handler = new TestEffectHandler();
            var outcome = new Dictionary<string, object> { ["starting_interest"] = 5 };
            OutcomeDispatcher.Dispatch(outcome, new GameState(), handler);
            Assert.Equal(new[] { 5 }, handler.InterestDeltas);
        }

        // ============================================================
        // OutcomeDispatcher — shadow_effect complete (AC6)
        // ============================================================

        // Mutation: would catch if shadow_effect reason doesn't come from rule context
        [Fact]
        public void OutcomeDispatcher_ShadowEffect_DispatchesAllFields()
        {
            var handler = new TestEffectHandler();
            var outcome = new Dictionary<string, object>
            {
                ["shadow_effect"] = new Dictionary<string, object>
                {
                    ["shadow"] = "Despair",
                    ["delta"] = 3
                }
            };
            OutcomeDispatcher.Dispatch(outcome, new GameState(), handler);
            Assert.Single(handler.ShadowGrowths);
            Assert.Equal("Despair", handler.ShadowGrowths[0].Shadow);
            Assert.Equal(3, handler.ShadowGrowths[0].Delta);
        }

        // ============================================================
        // OutcomeDispatcher — risk_tier dispatches correctly (AC6)
        // ============================================================

        // Mutation: would catch if risk_tier dispatches via wrong handler method
        [Fact]
        public void OutcomeDispatcher_RiskTier_DispatchesViaSetRiskTier()
        {
            var handler = new TestEffectHandler();
            var outcome = new Dictionary<string, object> { ["risk_tier"] = "Hard" };
            OutcomeDispatcher.Dispatch(outcome, new GameState(), handler);
            Assert.Single(handler.RiskTiers);
            Assert.Equal("Hard", handler.RiskTiers[0]);
            // Should NOT be in roll modifiers
            Assert.Empty(handler.RollModifiers);
        }
    }
}
