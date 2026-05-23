using System;
using Pinder.Core.Conversation;
using Pinder.Core.Interfaces;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;
using Xunit;

namespace Pinder.Core.Tests.Conversation
{
    [Collection("GameSession")]
    [Trait("Category", "Core")]
    public class GameSessionRulesEvaluatorTests
    {
        // Simple stub dice for predictable ghost rolls
        private sealed class PredictableDice : IDiceRoller
        {
            private readonly int _value;
            public PredictableDice(int value) => _value = value;
            public int Roll(int sides) => _value;
        }

        [Fact]
        public void CheckInterestEndConditions_InterestZero_ThrowsUnmatched()
        {
            var interest = new InterestMeter(0); // IsZero = true
            var shadows = new SessionShadowTracker(TestHelpers.MakeStatBlock(2, 0));

            var ex = Assert.Throws<GameEndedException>(() =>
                GameSessionRulesEvaluator.CheckInterestEndConditions(interest, shadows)
            );

            Assert.Equal(GameOutcome.Unmatched, ex.Outcome);
            Assert.NotEmpty(ex.ShadowGrowthEvents);
            Assert.Contains("Dread +1 (Conversation ended without date)", ex.ShadowGrowthEvents[0]);
        }

        [Fact]
        public void CheckInterestEndConditions_InterestMaxed_ThrowsDateSecured()
        {
            var interest = new InterestMeter(25); // IsMaxed = true

            var ex = Assert.Throws<GameEndedException>(() =>
                GameSessionRulesEvaluator.CheckInterestEndConditions(interest, null)
            );

            Assert.Equal(GameOutcome.DateSecured, ex.Outcome);
        }

        [Fact]
        public void CheckGhostTrigger_BoredAndRollOne_ThrowsGhosted()
        {
            var interest = new InterestMeter(2); // Bored state (interest 1-4)
            var shadows = new SessionShadowTracker(TestHelpers.MakeStatBlock(2, 0));
            var dice = new PredictableDice(1); // will trigger ghost

            var ex = Assert.Throws<GameEndedException>(() =>
                GameSessionRulesEvaluator.CheckGhostTrigger(interest, shadows, dice, null)
            );

            Assert.Equal(GameOutcome.Ghosted, ex.Outcome);
            Assert.NotEmpty(ex.ShadowGrowthEvents);
            Assert.Contains("Dread +1 (Ghosted)", ex.ShadowGrowthEvents[0]);
        }

        [Fact]
        public void CheckGhostTrigger_BoredAndRollTwo_DoesNotThrow()
        {
            var interest = new InterestMeter(2); // Bored state
            var shadows = new SessionShadowTracker(TestHelpers.MakeStatBlock(2, 0));
            var dice = new PredictableDice(2); // will not trigger ghost

            // Should complete successfully without throwing
            GameSessionRulesEvaluator.CheckGhostTrigger(interest, shadows, dice, null);
        }
    }
}
