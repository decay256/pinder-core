using System;
using System.Collections.Generic;
using Pinder.Core.Conversation;
using Pinder.Core.Stats;
using Xunit;

namespace Pinder.Core.Tests
{
    public partial class PlayerResponseDelaySpecTests
    {
        #region Spec Examples (Section 3)

        // Mutation: would catch if Example 1 doesn't match spec output
        [Fact]
        public void SpecExample1_ShortDelay_NoPenalty()
        {
            var result = PlayerResponseDelayEvaluator.Evaluate(
                TimeSpan.FromMinutes(5), MakeDatee(chaosBase: 2), InterestState.Interested);

            Assert.Equal(0, result.InterestDelta);
            Assert.False(result.TriggerTest);
            Assert.Null(result.TestPrompt);
        }

        // Mutation: would catch if Example 2 interest gating is broken
        [Fact]
        public void SpecExample2_30Min_InterestBelowThreshold()
        {
            var result = PlayerResponseDelayEvaluator.Evaluate(
                TimeSpan.FromMinutes(30), MakeDatee(chaosBase: 2), InterestState.Interested);

            Assert.Equal(0, result.InterestDelta);
            Assert.False(result.TriggerTest);
            Assert.Null(result.TestPrompt);
        }

        // Mutation: would catch if Example 3 VeryIntoIt gate is broken
        [Fact]
        public void SpecExample3_30Min_VeryIntoIt()
        {
            var result = PlayerResponseDelayEvaluator.Evaluate(
                TimeSpan.FromMinutes(30), MakeDatee(chaosBase: 2), InterestState.VeryIntoIt);

            Assert.Equal(-1, result.InterestDelta);
            Assert.False(result.TriggerTest);
            Assert.Null(result.TestPrompt);
        }

        // Mutation: would catch if Example 4 trigger test is missing
        [Fact]
        public void SpecExample4_3Hours_TriggersTest()
        {
            var result = PlayerResponseDelayEvaluator.Evaluate(
                TimeSpan.FromHours(3), MakeDatee(chaosBase: 2), InterestState.Interested);

            Assert.Equal(-2, result.InterestDelta);
            Assert.True(result.TriggerTest);
            Assert.NotNull(result.TestPrompt);
        }

        // Mutation: would catch if Example 5 Chaos override is broken
        [Fact]
        public void SpecExample5_3Hours_HighChaos_NoPenalty()
        {
            var result = PlayerResponseDelayEvaluator.Evaluate(
                TimeSpan.FromHours(3), MakeDatee(chaosBase: 4), InterestState.Interested);

            Assert.Equal(0, result.InterestDelta);
            Assert.False(result.TriggerTest);
            Assert.Null(result.TestPrompt);
        }

        // Mutation: would catch if Example 6 Fixation doubling is broken
        [Fact]
        public void SpecExample6_12Hours_FixationDoubles()
        {
            var result = PlayerResponseDelayEvaluator.Evaluate(
                TimeSpan.FromHours(12), MakeDatee(chaosBase: 2, fixation: 6), InterestState.Interested);

            Assert.Equal(-6, result.InterestDelta);
            Assert.False(result.TriggerTest);
        }

        // Mutation: would catch if Example 7 Overthinking +1 is not applied
        [Fact]
        public void SpecExample7_2Hours_Overthinking()
        {
            var result = PlayerResponseDelayEvaluator.Evaluate(
                TimeSpan.FromHours(2), MakeDatee(chaosBase: 2, overthinking: 6), InterestState.Interested);

            Assert.Equal(-3, result.InterestDelta);
            Assert.True(result.TriggerTest);
            Assert.NotNull(result.TestPrompt);
        }

        // Mutation: would catch if Example 8 modifier application order is wrong
        [Fact]
        public void SpecExample8_2Hours_FixationAndOverthinking()
        {
            var result = PlayerResponseDelayEvaluator.Evaluate(
                TimeSpan.FromHours(2),
                MakeDatee(chaosBase: 2, fixation: 7, overthinking: 8),
                InterestState.Interested);

            // Base -2, Fixation doubles to -4, Overthinking adds -1 = -5
            Assert.Equal(-5, result.InterestDelta);
            Assert.True(result.TriggerTest);
            Assert.NotNull(result.TestPrompt);
        }

        // Mutation: would catch if Example 9 24+ bucket value is wrong
        [Fact]
        public void SpecExample9_48Hours()
        {
            var result = PlayerResponseDelayEvaluator.Evaluate(
                TimeSpan.FromHours(48), MakeDatee(chaosBase: 2), InterestState.Bored);

            Assert.Equal(-5, result.InterestDelta);
            Assert.False(result.TriggerTest);
        }

        #endregion

        #region Error Conditions (Section 6)

        // Mutation: would catch if negative delay throws instead of returning 0
        [Fact]
        public void NegativeDelay_ReturnsZeroPenalty()
        {
            var result = PlayerResponseDelayEvaluator.Evaluate(
                TimeSpan.FromMinutes(-10), DefaultDatee, InterestState.Interested);

            Assert.Equal(0, result.InterestDelta);
            Assert.False(result.TriggerTest);
            Assert.Null(result.TestPrompt);
        }

        // Mutation: would catch if null dateeStats doesn't throw ArgumentNullException
        [Fact]
        public void NullDateeStats_ThrowsArgumentNullException()
        {
            var ex = Assert.Throws<ArgumentNullException>(() =>
                PlayerResponseDelayEvaluator.Evaluate(
                    TimeSpan.FromHours(1), null!, InterestState.Interested));

            Assert.Equal("dateeStats", ex.ParamName);
        }

        // Mutation: would catch if undefined enum value throws instead of being handled
        [Fact]
        public void UndefinedInterestState_DoesNotThrow()
        {
            var result = PlayerResponseDelayEvaluator.Evaluate(
                TimeSpan.FromMinutes(30), DefaultDatee, (InterestState)999);

            // Undefined enum should be treated as "not >= 16", so no penalty in 15-60 min bucket
            Assert.Equal(0, result.InterestDelta);
        }

        // Mutation: would catch if method ever returns null
        [Fact]
        public void Evaluate_NeverReturnsNull()
        {
            var result = PlayerResponseDelayEvaluator.Evaluate(
                TimeSpan.Zero, DefaultDatee, InterestState.Interested);

            Assert.NotNull(result);
        }

        #endregion

        #region Edge Cases: Zero-stat datee

        // Mutation: would catch if zero stats cause exceptions or wrong behavior
        [Fact]
        public void ZeroStatDatee_PureBasePenaltyApplies()
        {
            var zeroStats = new StatBlock(
                new Dictionary<StatType, int>
                {
                    { StatType.Charm, 0 },
                    { StatType.Rizz, 0 },
                    { StatType.Honesty, 0 },
                    { StatType.Chaos, 0 },
                    { StatType.Wit, 0 },
                    { StatType.SelfAwareness, 0 }
                },
                new Dictionary<ShadowStatType, int>
                {
                    { ShadowStatType.Madness, 0 },
                    { ShadowStatType.Despair, 0 },
                    { ShadowStatType.Denial, 0 },
                    { ShadowStatType.Fixation, 0 },
                    { ShadowStatType.Dread, 0 },
                    { ShadowStatType.Overthinking, 0 }
                });

            var result = PlayerResponseDelayEvaluator.Evaluate(
                TimeSpan.FromHours(3), zeroStats, InterestState.Interested);

            Assert.Equal(-2, result.InterestDelta);
        }

        #endregion

        #region Edge Cases: Denial shadow (no mechanical effect per spec)

        // Mutation: would catch if Denial shadow incorrectly modifies InterestDelta
        [Fact]
        public void Denial6_DoesNotModifyPenalty()
        {
            var stats = MakeDatee(denial: 10);
            var result = PlayerResponseDelayEvaluator.Evaluate(
                TimeSpan.FromHours(3), stats, InterestState.Interested);

            // Denial has no mechanical effect per spec — base penalty unchanged
            Assert.Equal(-2, result.InterestDelta);
        }

        #endregion

        #region Edge Cases: Fixation/Overthinking with zero base penalty

        // Mutation: would catch if Fixation doubling on 0 produces non-zero result
        [Fact]
        public void Fixation6_WithZeroBasePenalty_StillZero()
        {
            var stats = MakeDatee(fixation: 10);
            var result = PlayerResponseDelayEvaluator.Evaluate(
                TimeSpan.FromMinutes(5), stats, InterestState.Interested);

            Assert.Equal(0, result.InterestDelta);
        }

        // Mutation: would catch if Overthinking adds -1 even when base is 0
        [Fact]
        public void Overthinking6_WithZeroBasePenalty_StillZero()
        {
            // Spec Section 8: "If the base penalty from step 1–2 is already 0, skip steps 4–5"
            var stats = MakeDatee(overthinking: 10);
            var result = PlayerResponseDelayEvaluator.Evaluate(
                TimeSpan.FromMinutes(5), stats, InterestState.Interested);

            Assert.Equal(0, result.InterestDelta);
        }

        #endregion

        #region Edge Cases: 15-60 min bucket with modifiers

        // Mutation: would catch if Fixation doubling on -1 (15-60 min, high interest) is broken
        [Fact]
        public void Fixation6_15To60MinBucket_VeryIntoIt_DoublesMinusOne()
        {
            var stats = MakeDatee(fixation: 6);
            var result = PlayerResponseDelayEvaluator.Evaluate(
                TimeSpan.FromMinutes(30), stats, InterestState.VeryIntoIt);

            // Base -1 doubled to -2
            Assert.Equal(-2, result.InterestDelta);
        }

        // Mutation: would catch if Overthinking on -1 (15-60 min, high interest) is broken
        [Fact]
        public void Overthinking6_15To60MinBucket_VeryIntoIt()
        {
            var stats = MakeDatee(overthinking: 6);
            var result = PlayerResponseDelayEvaluator.Evaluate(
                TimeSpan.FromMinutes(30), stats, InterestState.VeryIntoIt);

            // Base -1, Overthinking adds -1 = -2
            Assert.Equal(-2, result.InterestDelta);
        }

        #endregion

        #region Edge Cases: 24+ bucket with modifiers

        // Mutation: would catch if Fixation doubling on -5 is wrong
        [Fact]
        public void Fixation6_24PlusBucket_DoublesMinusFive()
        {
            var stats = MakeDatee(fixation: 6);
            var result = PlayerResponseDelayEvaluator.Evaluate(
                TimeSpan.FromHours(48), stats, InterestState.Interested);

            // Base -5 doubled to -10
            Assert.Equal(-10, result.InterestDelta);
        }

        // Mutation: would catch if both modifiers on 24+ bucket produce wrong result
        [Fact]
        public void BothModifiers_24PlusBucket()
        {
            var stats = MakeDatee(fixation: 6, overthinking: 6);
            var result = PlayerResponseDelayEvaluator.Evaluate(
                TimeSpan.FromHours(48), stats, InterestState.Interested);

            // Base -5, Fixation doubles to -10, Overthinking -1 = -11
            Assert.Equal(-11, result.InterestDelta);
        }

        #endregion
    }
}
