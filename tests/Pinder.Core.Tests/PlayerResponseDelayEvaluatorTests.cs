using System;
using System.Collections.Generic;
using Pinder.Core.Conversation;
using Pinder.Core.Stats;
using Xunit;

namespace Pinder.Core.Tests
{
    public class PlayerResponseDelayEvaluatorTests
    {
        #region Helpers

        private static StatBlock MakeStats(int chaos = 2, int fixation = 0, int overthinking = 0)
        {
            return new StatBlock(
                new Dictionary<StatType, int>
                {
                    { StatType.Charm, 2 },
                    { StatType.Rizz, 2 },
                    { StatType.Honesty, 2 },
                    { StatType.Chaos, chaos },
                    { StatType.Wit, 2 },
                    { StatType.SelfAwareness, 2 }
                },
                new Dictionary<ShadowStatType, int>
                {
                    { ShadowStatType.Madness, 0 },
                    { ShadowStatType.Horniness, 0 },
                    { ShadowStatType.Denial, 0 },
                    { ShadowStatType.Fixation, fixation },
                    { ShadowStatType.Dread, 0 },
                    { ShadowStatType.Overthinking, overthinking }
                });
        }

        #endregion

        #region Delay Bucket — Base Penalties

        [Theory]
        [InlineData(0)]       // zero
        [InlineData(30)]      // 30 seconds
        [InlineData(59)]      // 59 seconds
        public void LessThanOneMinute_NoPenalty(int seconds)
        {
            var result = PlayerResponseDelayEvaluator.Evaluate(
                TimeSpan.FromSeconds(seconds), MakeStats(), InterestState.Interested);

            Assert.Equal(0, result.InterestDelta);
            Assert.False(result.TriggerTest);
            Assert.Null(result.TestPrompt);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(5)]
        [InlineData(14)]
        public void OneToFifteenMinutes_NoPenalty(double minutes)
        {
            var result = PlayerResponseDelayEvaluator.Evaluate(
                TimeSpan.FromMinutes(minutes), MakeStats(), InterestState.Interested);

            Assert.Equal(0, result.InterestDelta);
            Assert.False(result.TriggerTest);
        }

        [Theory]
        [InlineData(InterestState.Unmatched)]
        [InlineData(InterestState.Bored)]
        [InlineData(InterestState.Interested)]
        public void FifteenToSixtyMinutes_LowInterest_NoPenalty(InterestState state)
        {
            var result = PlayerResponseDelayEvaluator.Evaluate(
                TimeSpan.FromMinutes(30), MakeStats(), state);

            Assert.Equal(0, result.InterestDelta);
        }

        [Theory]
        [InlineData(InterestState.VeryIntoIt)]
        [InlineData(InterestState.AlmostThere)]
        [InlineData(InterestState.DateSecured)]
        public void FifteenToSixtyMinutes_HighInterest_MinusOne(InterestState state)
        {
            var result = PlayerResponseDelayEvaluator.Evaluate(
                TimeSpan.FromMinutes(30), MakeStats(), state);

            Assert.Equal(-1, result.InterestDelta);
            Assert.False(result.TriggerTest);  // not in 1-6h bucket
        }

        [Fact]
        public void OneToSixHours_MinusTwo()
        {
            var result = PlayerResponseDelayEvaluator.Evaluate(
                TimeSpan.FromHours(3), MakeStats(), InterestState.Interested);

            Assert.Equal(-2, result.InterestDelta);
            Assert.True(result.TriggerTest);
            Assert.NotNull(result.TestPrompt);
        }

        [Fact]
        public void SixToTwentyFourHours_MinusThree()
        {
            var result = PlayerResponseDelayEvaluator.Evaluate(
                TimeSpan.FromHours(12), MakeStats(), InterestState.Interested);

            Assert.Equal(-3, result.InterestDelta);
            Assert.False(result.TriggerTest);  // not 1-6h bucket
        }

        [Fact]
        public void TwentyFourPlusHours_MinusFive()
        {
            var result = PlayerResponseDelayEvaluator.Evaluate(
                TimeSpan.FromHours(48), MakeStats(), InterestState.Bored);

            Assert.Equal(-5, result.InterestDelta);
            Assert.False(result.TriggerTest);
        }

        #endregion

        #region Boundary Precision

        [Fact]
        public void ExactlyOneMinute_InOneToFifteenBucket_NoPenalty()
        {
            var result = PlayerResponseDelayEvaluator.Evaluate(
                TimeSpan.FromMinutes(1), MakeStats(), InterestState.VeryIntoIt);

            Assert.Equal(0, result.InterestDelta);
        }

        [Fact]
        public void ExactlyFifteenMinutes_InFifteenToSixtyBucket()
        {
            var result = PlayerResponseDelayEvaluator.Evaluate(
                TimeSpan.FromMinutes(15), MakeStats(), InterestState.VeryIntoIt);

            Assert.Equal(-1, result.InterestDelta);
        }

        [Fact]
        public void ExactlySixtyMinutes_InOneToSixHourBucket()
        {
            var result = PlayerResponseDelayEvaluator.Evaluate(
                TimeSpan.FromMinutes(60), MakeStats(), InterestState.Interested);

            Assert.Equal(-2, result.InterestDelta);
            Assert.True(result.TriggerTest);
        }

        [Fact]
        public void ExactlySixHours_InSixToTwentyFourBucket()
        {
            var result = PlayerResponseDelayEvaluator.Evaluate(
                TimeSpan.FromHours(6), MakeStats(), InterestState.Interested);

            Assert.Equal(-3, result.InterestDelta);
        }

        [Fact]
        public void ExactlyTwentyFourHours_InTwentyFourPlusBucket()
        {
            var result = PlayerResponseDelayEvaluator.Evaluate(
                TimeSpan.FromHours(24), MakeStats(), InterestState.Interested);

            Assert.Equal(-5, result.InterestDelta);
        }

        [Fact]
        public void VeryLargeDelay_ThirtyDays_MinusFive()
        {
            var result = PlayerResponseDelayEvaluator.Evaluate(
                TimeSpan.FromDays(30), MakeStats(), InterestState.Interested);

            Assert.Equal(-5, result.InterestDelta);
        }

        #endregion

        #region Chaos Override

        [Fact]
        public void ChaosBaseFour_ZeroesPenalty()
        {
            var result = PlayerResponseDelayEvaluator.Evaluate(
                TimeSpan.FromHours(3), MakeStats(chaos: 4), InterestState.Interested);

            Assert.Equal(0, result.InterestDelta);
            Assert.False(result.TriggerTest);
            Assert.Null(result.TestPrompt);
        }

        [Fact]
        public void ChaosBaseFive_ZeroesPenalty()
        {
            var result = PlayerResponseDelayEvaluator.Evaluate(
                TimeSpan.FromHours(3), MakeStats(chaos: 5), InterestState.Interested);

            Assert.Equal(0, result.InterestDelta);
        }

        [Fact]
        public void ChaosBaseThree_PenaltyApplies()
        {
            var result = PlayerResponseDelayEvaluator.Evaluate(
                TimeSpan.FromHours(3), MakeStats(chaos: 3), InterestState.Interested);

            Assert.Equal(-2, result.InterestDelta);
        }

        [Fact]
        public void ChaosOverridesFixationAndOverthinking()
        {
            var result = PlayerResponseDelayEvaluator.Evaluate(
                TimeSpan.FromHours(3), MakeStats(chaos: 4, fixation: 10, overthinking: 10), InterestState.Interested);

            Assert.Equal(0, result.InterestDelta);
            Assert.False(result.TriggerTest);
        }

        #endregion

        #region Fixation Doubling

        [Fact]
        public void FixationSix_DoublesPenalty()
        {
            var result = PlayerResponseDelayEvaluator.Evaluate(
                TimeSpan.FromHours(12), MakeStats(fixation: 6), InterestState.Interested);

            // base -3, doubled to -6
            Assert.Equal(-6, result.InterestDelta);
        }

        [Fact]
        public void FixationFive_NoDoubling()
        {
            var result = PlayerResponseDelayEvaluator.Evaluate(
                TimeSpan.FromHours(12), MakeStats(fixation: 5), InterestState.Interested);

            Assert.Equal(-3, result.InterestDelta);
        }

        #endregion

        #region Overthinking Addition

        [Fact]
        public void OverthinkingSix_AddsOne()
        {
            var result = PlayerResponseDelayEvaluator.Evaluate(
                TimeSpan.FromHours(2), MakeStats(overthinking: 6), InterestState.Interested);

            // base -2, overthinking -1 = -3
            Assert.Equal(-3, result.InterestDelta);
        }

        [Fact]
        public void OverthinkingFive_NoAddition()
        {
            var result = PlayerResponseDelayEvaluator.Evaluate(
                TimeSpan.FromHours(2), MakeStats(overthinking: 5), InterestState.Interested);

            Assert.Equal(-2, result.InterestDelta);
        }

        #endregion

        #region Combined Modifiers

        [Fact]
        public void FixationAndOverthinking_DoubleFirst_ThenAddOne()
        {
            // Example 8 from spec: base -2, fixation doubles -> -4, overthinking +1 -> -5
            var result = PlayerResponseDelayEvaluator.Evaluate(
                TimeSpan.FromHours(2), MakeStats(fixation: 7, overthinking: 8), InterestState.Interested);

            Assert.Equal(-5, result.InterestDelta);
            Assert.True(result.TriggerTest);
        }

        #endregion

        #region Test Trigger

        [Fact]
        public void OneToSixHourBucket_TriggerTest_True()
        {
            var result = PlayerResponseDelayEvaluator.Evaluate(
                TimeSpan.FromHours(3), MakeStats(), InterestState.Interested);

            Assert.True(result.TriggerTest);
            Assert.NotNull(result.TestPrompt);
        }

        [Fact]
        public void SixToTwentyFourBucket_TriggerTest_False()
        {
            var result = PlayerResponseDelayEvaluator.Evaluate(
                TimeSpan.FromHours(12), MakeStats(), InterestState.Interested);

            Assert.False(result.TriggerTest);
            Assert.Null(result.TestPrompt);
        }

        [Fact]
        public void OneToSixHourBucket_ChaosZeroesPenalty_TriggerTest_False()
        {
            var result = PlayerResponseDelayEvaluator.Evaluate(
                TimeSpan.FromHours(3), MakeStats(chaos: 4), InterestState.Interested);

            Assert.False(result.TriggerTest);
        }

        #endregion

        #region Error Conditions

        [Fact]
        public void NegativeDelay_NoPenalty()
        {
            var result = PlayerResponseDelayEvaluator.Evaluate(
                TimeSpan.FromMinutes(-10), MakeStats(), InterestState.Interested);

            Assert.Equal(0, result.InterestDelta);
            Assert.False(result.TriggerTest);
            Assert.Null(result.TestPrompt);
        }

        [Fact]
        public void NullOpponentStats_Throws()
        {
            Assert.Throws<ArgumentNullException>(() =>
                PlayerResponseDelayEvaluator.Evaluate(
                    TimeSpan.FromHours(1), null!, InterestState.Interested));
        }

        [Fact]
        public void UndefinedEnumValue_TreatedAsLowInterest()
        {
            // Cast an invalid value — should not throw, treated as "not >= 16"
            var result = PlayerResponseDelayEvaluator.Evaluate(
                TimeSpan.FromMinutes(30), MakeStats(), (InterestState)99);

            Assert.Equal(0, result.InterestDelta);
        }

        #endregion

        #region Spec Examples

        [Fact]
        public void SpecExample1_ShortDelay_NoPenalty()
        {
            var result = PlayerResponseDelayEvaluator.Evaluate(
                TimeSpan.FromMinutes(5), MakeStats(), InterestState.Interested);

            Assert.Equal(0, result.InterestDelta);
            Assert.False(result.TriggerTest);
            Assert.Null(result.TestPrompt);
        }

        [Fact]
        public void SpecExample2_ThirtyMin_InterestBelowThreshold()
        {
            var result = PlayerResponseDelayEvaluator.Evaluate(
                TimeSpan.FromMinutes(30), MakeStats(), InterestState.Interested);

            Assert.Equal(0, result.InterestDelta);
        }

        [Fact]
        public void SpecExample3_ThirtyMin_VeryIntoIt()
        {
            var result = PlayerResponseDelayEvaluator.Evaluate(
                TimeSpan.FromMinutes(30), MakeStats(), InterestState.VeryIntoIt);

            Assert.Equal(-1, result.InterestDelta);
        }

        [Fact]
        public void SpecExample4_ThreeHours_TriggersTest()
        {
            var result = PlayerResponseDelayEvaluator.Evaluate(
                TimeSpan.FromHours(3), MakeStats(), InterestState.Interested);

            Assert.Equal(-2, result.InterestDelta);
            Assert.True(result.TriggerTest);
            Assert.NotNull(result.TestPrompt);
        }

        [Fact]
        public void SpecExample5_ThreeHours_HighChaos_NoPenalty()
        {
            var result = PlayerResponseDelayEvaluator.Evaluate(
                TimeSpan.FromHours(3), MakeStats(chaos: 4), InterestState.Interested);

            Assert.Equal(0, result.InterestDelta);
            Assert.False(result.TriggerTest);
            Assert.Null(result.TestPrompt);
        }

        [Fact]
        public void SpecExample6_TwelveHours_FixationDoubling()
        {
            var result = PlayerResponseDelayEvaluator.Evaluate(
                TimeSpan.FromHours(12), MakeStats(fixation: 6), InterestState.Interested);

            Assert.Equal(-6, result.InterestDelta);
            Assert.False(result.TriggerTest);
        }

        [Fact]
        public void SpecExample7_TwoHours_OverthinkingAddsOne()
        {
            var result = PlayerResponseDelayEvaluator.Evaluate(
                TimeSpan.FromHours(2), MakeStats(overthinking: 6), InterestState.Interested);

            Assert.Equal(-3, result.InterestDelta);
            Assert.True(result.TriggerTest);
            Assert.NotNull(result.TestPrompt);
        }

        [Fact]
        public void SpecExample8_TwoHours_FixationAndOverthinking()
        {
            var result = PlayerResponseDelayEvaluator.Evaluate(
                TimeSpan.FromHours(2), MakeStats(fixation: 7, overthinking: 8), InterestState.Interested);

            Assert.Equal(-5, result.InterestDelta);
            Assert.True(result.TriggerTest);
            Assert.NotNull(result.TestPrompt);
        }

        [Fact]
        public void SpecExample9_FortyEightHours()
        {
            var result = PlayerResponseDelayEvaluator.Evaluate(
                TimeSpan.FromHours(48), MakeStats(), InterestState.Bored);

            Assert.Equal(-5, result.InterestDelta);
            Assert.False(result.TriggerTest);
        }

        #endregion
    }
}
