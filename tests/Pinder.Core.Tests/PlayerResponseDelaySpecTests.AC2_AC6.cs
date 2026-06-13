using System;
using Pinder.Core.Conversation;
using Pinder.Core.Stats;
using Xunit;

namespace Pinder.Core.Tests
{
    public partial class PlayerResponseDelaySpecTests
    {
        #region AC2 continued: Interest state gating for 15-60 min bucket

        // Mutation: would catch if Unmatched is treated as >= 16
        [Fact]
        public void Gate_15Min_Unmatched_ZeroPenalty()
        {
            var result = PlayerResponseDelayEvaluator.Evaluate(
                TimeSpan.FromMinutes(30), DefaultDatee, InterestState.Unmatched);

            Assert.Equal(0, result.InterestDelta);
        }

        // Mutation: would catch if Bored is treated as >= 16
        [Fact]
        public void Gate_15Min_Bored_ZeroPenalty()
        {
            var result = PlayerResponseDelayEvaluator.Evaluate(
                TimeSpan.FromMinutes(30), DefaultDatee, InterestState.Bored);

            Assert.Equal(0, result.InterestDelta);
        }

        // Mutation: would catch if DateSecured is excluded from >= 16 set
        [Fact]
        public void Gate_15Min_DateSecured_MinusOne()
        {
            var result = PlayerResponseDelayEvaluator.Evaluate(
                TimeSpan.FromMinutes(30), DefaultDatee, InterestState.DateSecured);

            Assert.Equal(-1, result.InterestDelta);
        }

        #endregion

        #region AC3: Chaos base stat >= 4 reduces penalty to 0

        // Mutation: would catch if Chaos threshold is > 4 instead of >= 4
        [Fact]
        public void Chaos4_ZeroesPenalty()
        {
            var stats = MakeDatee(chaosBase: 4);
            var result = PlayerResponseDelayEvaluator.Evaluate(
                TimeSpan.FromHours(3), stats, InterestState.Interested);

            Assert.Equal(0, result.InterestDelta);
            Assert.False(result.TriggerTest);
            Assert.Null(result.TestPrompt);
        }

        // Mutation: would catch if Chaos 3 incorrectly zeroes penalty
        [Fact]
        public void Chaos3_PenaltyStillApplies()
        {
            var stats = MakeDatee(chaosBase: 3);
            var result = PlayerResponseDelayEvaluator.Evaluate(
                TimeSpan.FromHours(3), stats, InterestState.Interested);

            Assert.Equal(-2, result.InterestDelta);
        }

        // Mutation: would catch if Chaos >= 4 doesn't override Fixation+Overthinking
        [Fact]
        public void Chaos5_OverridesAllModifiers()
        {
            var stats = MakeDatee(chaosBase: 5, fixation: 10, overthinking: 10);
            var result = PlayerResponseDelayEvaluator.Evaluate(
                TimeSpan.FromHours(12), stats, InterestState.Interested);

            Assert.Equal(0, result.InterestDelta);
            Assert.False(result.TriggerTest);
            Assert.Null(result.TestPrompt);
        }

        // Mutation: would catch if Chaos >= 4 doesn't prevent test trigger
        [Fact]
        public void Chaos4_NoTriggerTest_Even1to6hBucket()
        {
            var stats = MakeDatee(chaosBase: 4);
            var result = PlayerResponseDelayEvaluator.Evaluate(
                TimeSpan.FromHours(2), stats, InterestState.Interested);

            Assert.Equal(0, result.InterestDelta);
            Assert.False(result.TriggerTest);
        }

        #endregion

        #region AC4: Fixation shadow >= 6 doubles penalty

        // Mutation: would catch if Fixation doubling is missing
        [Fact]
        public void Fixation6_DoublesPenalty()
        {
            var stats = MakeDatee(fixation: 6);
            var result = PlayerResponseDelayEvaluator.Evaluate(
                TimeSpan.FromHours(12), stats, InterestState.Interested);

            // Base -3 doubled to -6
            Assert.Equal(-6, result.InterestDelta);
        }

        // Mutation: would catch if Fixation threshold is > 6 instead of >= 6
        [Fact]
        public void Fixation5_NoDoubling()
        {
            var stats = MakeDatee(fixation: 5);
            var result = PlayerResponseDelayEvaluator.Evaluate(
                TimeSpan.FromHours(12), stats, InterestState.Interested);

            Assert.Equal(-3, result.InterestDelta);
        }

        // Mutation: would catch if Fixation doubling is additive instead of multiplicative
        [Fact]
        public void Fixation6_Doubles_1to6hBucket()
        {
            var stats = MakeDatee(fixation: 6);
            var result = PlayerResponseDelayEvaluator.Evaluate(
                TimeSpan.FromHours(2), stats, InterestState.Interested);

            // Base -2 doubled to -4
            Assert.Equal(-4, result.InterestDelta);
        }

        #endregion

        #region AC5: Overthinking shadow >= 6 adds +1 penalty

        // Mutation: would catch if Overthinking +1 is missing
        [Fact]
        public void Overthinking6_AddsOne()
        {
            var stats = MakeDatee(overthinking: 6);
            var result = PlayerResponseDelayEvaluator.Evaluate(
                TimeSpan.FromHours(2), stats, InterestState.Interested);

            // Base -2, Overthinking adds -1 = -3
            Assert.Equal(-3, result.InterestDelta);
        }

        // Mutation: would catch if Overthinking threshold is > 6 instead of >= 6
        [Fact]
        public void Overthinking5_NoExtraPenalty()
        {
            var stats = MakeDatee(overthinking: 5);
            var result = PlayerResponseDelayEvaluator.Evaluate(
                TimeSpan.FromHours(2), stats, InterestState.Interested);

            Assert.Equal(-2, result.InterestDelta);
        }

        // Mutation: would catch if Overthinking is applied before Fixation (order matters)
        [Fact]
        public void FixationAndOverthinking_CorrectOrder()
        {
            // Spec: base=-2, Fixation doubles to -4, Overthinking adds -1 = -5
            // Wrong order: base=-2, Overthinking adds -1=-3, Fixation doubles=-6
            var stats = MakeDatee(fixation: 7, overthinking: 8);
            var result = PlayerResponseDelayEvaluator.Evaluate(
                TimeSpan.FromHours(2), stats, InterestState.Interested);

            Assert.Equal(-5, result.InterestDelta);
        }

        #endregion

        #region AC6: Test trigger fires at 1-6h delay

        // Mutation: would catch if TriggerTest is not set for 1-6h bucket
        [Fact]
        public void TriggerTest_1to6hBucket_True()
        {
            var result = PlayerResponseDelayEvaluator.Evaluate(
                TimeSpan.FromHours(3), DefaultDatee, InterestState.Interested);

            Assert.True(result.TriggerTest);
            Assert.NotNull(result.TestPrompt);
        }

        // Mutation: would catch if TestPrompt is empty string instead of meaningful
        [Fact]
        public void TriggerTest_1to6h_TestPromptNonEmpty()
        {
            var result = PlayerResponseDelayEvaluator.Evaluate(
                TimeSpan.FromHours(2), DefaultDatee, InterestState.Interested);

            Assert.True(result.TriggerTest);
            Assert.False(string.IsNullOrEmpty(result.TestPrompt));
        }

        // Mutation: would catch if TriggerTest fires for < 1h bucket
        [Fact]
        public void TriggerTest_Under1h_False()
        {
            var result = PlayerResponseDelayEvaluator.Evaluate(
                TimeSpan.FromMinutes(30), DefaultDatee, InterestState.VeryIntoIt);

            Assert.False(result.TriggerTest);
            Assert.Null(result.TestPrompt);
        }

        // Mutation: would catch if TriggerTest fires for 6-24h bucket
        [Fact]
        public void TriggerTest_6to24hBucket_False()
        {
            var result = PlayerResponseDelayEvaluator.Evaluate(
                TimeSpan.FromHours(12), DefaultDatee, InterestState.Interested);

            Assert.False(result.TriggerTest);
            Assert.Null(result.TestPrompt);
        }

        // Mutation: would catch if TriggerTest fires for 24+ bucket
        [Fact]
        public void TriggerTest_24PlusBucket_False()
        {
            var result = PlayerResponseDelayEvaluator.Evaluate(
                TimeSpan.FromHours(48), DefaultDatee, InterestState.Interested);

            Assert.False(result.TriggerTest);
            Assert.Null(result.TestPrompt);
        }

        // Mutation: would catch if TriggerTest fires for < 1 min bucket
        [Fact]
        public void TriggerTest_LessThan1Min_False()
        {
            var result = PlayerResponseDelayEvaluator.Evaluate(
                TimeSpan.FromSeconds(30), DefaultDatee, InterestState.Interested);

            Assert.False(result.TriggerTest);
            Assert.Null(result.TestPrompt);
        }

        // Mutation: would catch if TriggerTest fires when Chaos >= 4 zeroes penalty
        [Fact]
        public void TriggerTest_ChaosZeroesPenalty_NoTrigger()
        {
            var stats = MakeDatee(chaosBase: 4);
            var result = PlayerResponseDelayEvaluator.Evaluate(
                TimeSpan.FromHours(3), stats, InterestState.Interested);

            Assert.False(result.TriggerTest);
        }

        // Mutation: would catch if TriggerTest at exactly 1 hour boundary is wrong
        [Fact]
        public void TriggerTest_Exactly1Hour_True()
        {
            var result = PlayerResponseDelayEvaluator.Evaluate(
                TimeSpan.FromHours(1), DefaultDatee, InterestState.Interested);

            Assert.True(result.TriggerTest);
        }

        // Mutation: would catch if TriggerTest at 5.999h is not triggered
        [Fact]
        public void TriggerTest_JustUnder6Hours_True()
        {
            var result = PlayerResponseDelayEvaluator.Evaluate(
                TimeSpan.FromHours(5.999), DefaultDatee, InterestState.Interested);

            Assert.True(result.TriggerTest);
        }

        #endregion

        #region AC7: DelayPenalty is a sealed class

        // Mutation: would catch if DelayPenalty is a struct or record
        [Fact]
        public void DelayPenalty_IsClass()
        {
            Assert.True(typeof(DelayPenalty).IsClass);
        }

        // Mutation: would catch if DelayPenalty is not sealed
        [Fact]
        public void DelayPenalty_IsSealed()
        {
            Assert.True(typeof(DelayPenalty).IsSealed);
        }

        #endregion
    }
}
