using System;
using System.Collections.Generic;
using Pinder.Core.Conversation;
using Pinder.Core.Stats;
using Xunit;

namespace Pinder.Core.Tests
{
    /// <summary>
    /// Spec-driven tests for PlayerResponseDelayEvaluator (issue #55).
    /// Written from docs/specs/issue-55-spec.md — context-isolated from implementation.
    /// </summary>
    [Trait("Category", "Core")]
    public partial class PlayerResponseDelaySpecTests
    {
        #region Test Helpers

        /// <summary>
        /// Creates a StatBlock with configurable Chaos base and shadow stats.
        /// All other stats default to 2 / 0.
        /// </summary>
        private static StatBlock MakeDatee(
            int chaosBase = 2,
            int fixation = 0,
            int overthinking = 0,
            int denial = 0)
        {
            return new StatBlock(
                new Dictionary<StatType, int>
                {
                    { StatType.Charm, 2 },
                    { StatType.Rizz, 2 },
                    { StatType.Honesty, 2 },
                    { StatType.Chaos, chaosBase },
                    { StatType.Wit, 2 },
                    { StatType.SelfAwareness, 2 }
                },
                new Dictionary<ShadowStatType, int>
                {
                    { ShadowStatType.Madness, 0 },
                    { ShadowStatType.Despair, 0 },
                    { ShadowStatType.Denial, denial },
                    { ShadowStatType.Fixation, fixation },
                    { ShadowStatType.Dread, 0 },
                    { ShadowStatType.Overthinking, overthinking }
                });
        }

        private static StatBlock DefaultDatee => MakeDatee();

        #endregion

        #region AC1: Method exists and returns DelayPenalty

        // Mutation: would catch if Evaluate doesn't exist or returns wrong type
        [Fact]
        public void Evaluate_ReturnsDelayPenaltyInstance()
        {
            var result = PlayerResponseDelayEvaluator.Evaluate(
                TimeSpan.FromMinutes(5), DefaultDatee, InterestState.Interested);

            Assert.NotNull(result);
            Assert.IsType<DelayPenalty>(result);
        }

        #endregion

        #region AC2: Correct penalty per delay bucket

        // Mutation: would catch if < 1 min bucket returns non-zero penalty
        [Fact]
        public void Bucket_LessThan1Min_ZeroPenalty()
        {
            var result = PlayerResponseDelayEvaluator.Evaluate(
                TimeSpan.FromSeconds(30), DefaultDatee, InterestState.Interested);

            Assert.Equal(0, result.InterestDelta);
        }

        // Mutation: would catch if TimeSpan.Zero is not handled as < 1 min
        [Fact]
        public void Bucket_Zero_ZeroPenalty()
        {
            var result = PlayerResponseDelayEvaluator.Evaluate(
                TimeSpan.Zero, DefaultDatee, InterestState.Interested);

            Assert.Equal(0, result.InterestDelta);
        }

        // Mutation: would catch if 59 seconds is misclassified into 1-15 min bucket
        [Fact]
        public void Bucket_59Seconds_ZeroPenalty()
        {
            var result = PlayerResponseDelayEvaluator.Evaluate(
                TimeSpan.FromSeconds(59), DefaultDatee, InterestState.Interested);

            Assert.Equal(0, result.InterestDelta);
        }

        // Mutation: would catch if 1-15 min bucket returns non-zero
        [Fact]
        public void Bucket_1To15Min_ZeroPenalty()
        {
            var result = PlayerResponseDelayEvaluator.Evaluate(
                TimeSpan.FromMinutes(1), DefaultDatee, InterestState.Interested);

            Assert.Equal(0, result.InterestDelta);
        }

        // Mutation: would catch if boundary at exactly 1 min is mishandled
        [Fact]
        public void Bucket_Exactly1Min_ZeroPenalty()
        {
            var result = PlayerResponseDelayEvaluator.Evaluate(
                TimeSpan.FromMinutes(1), DefaultDatee, InterestState.VeryIntoIt);

            Assert.Equal(0, result.InterestDelta);
        }

        // Mutation: would catch if 14.999 minutes overflows into 15-60 min bucket
        [Fact]
        public void Bucket_Just_Under15Min_ZeroPenalty()
        {
            var result = PlayerResponseDelayEvaluator.Evaluate(
                TimeSpan.FromMinutes(14.999), DefaultDatee, InterestState.VeryIntoIt);

            Assert.Equal(0, result.InterestDelta);
        }

        // Mutation: would catch if 15 min boundary is exclusive instead of inclusive
        [Fact]
        public void Bucket_Exactly15Min_VeryIntoIt_MinusOne()
        {
            var result = PlayerResponseDelayEvaluator.Evaluate(
                TimeSpan.FromMinutes(15), DefaultDatee, InterestState.VeryIntoIt);

            Assert.Equal(-1, result.InterestDelta);
        }

        // Mutation: would catch if 15-60 min bucket doesn't gate on interest state
        [Fact]
        public void Bucket_15Min_Interested_ZeroPenalty()
        {
            var result = PlayerResponseDelayEvaluator.Evaluate(
                TimeSpan.FromMinutes(15), DefaultDatee, InterestState.Interested);

            Assert.Equal(0, result.InterestDelta);
        }

        // Mutation: would catch if AlmostThere is not included in the interest gate
        [Fact]
        public void Bucket_30Min_AlmostThere_MinusOne()
        {
            var result = PlayerResponseDelayEvaluator.Evaluate(
                TimeSpan.FromMinutes(30), DefaultDatee, InterestState.AlmostThere);

            Assert.Equal(-1, result.InterestDelta);
        }

        // Mutation: would catch if 59.999 minutes overflows into 1-6h bucket
        [Fact]
        public void Bucket_JustUnder60Min_VeryIntoIt_MinusOne()
        {
            var result = PlayerResponseDelayEvaluator.Evaluate(
                TimeSpan.FromMinutes(59.999), DefaultDatee, InterestState.VeryIntoIt);

            Assert.Equal(-1, result.InterestDelta);
        }

        // Mutation: would catch if 60 min boundary is exclusive instead of inclusive
        [Fact]
        public void Bucket_Exactly60Min_MinusTwo()
        {
            var result = PlayerResponseDelayEvaluator.Evaluate(
                TimeSpan.FromMinutes(60), DefaultDatee, InterestState.Interested);

            Assert.Equal(-2, result.InterestDelta);
        }

        // Mutation: would catch if 1-6h base penalty is wrong value (e.g. -1 instead of -2)
        [Fact]
        public void Bucket_3Hours_MinusTwo()
        {
            var result = PlayerResponseDelayEvaluator.Evaluate(
                TimeSpan.FromHours(3), DefaultDatee, InterestState.Interested);

            Assert.Equal(-2, result.InterestDelta);
        }

        // Mutation: would catch if 5.999h overflows into 6-24h bucket
        [Fact]
        public void Bucket_JustUnder6Hours_MinusTwo()
        {
            var result = PlayerResponseDelayEvaluator.Evaluate(
                TimeSpan.FromHours(5.999), DefaultDatee, InterestState.Interested);

            Assert.Equal(-2, result.InterestDelta);
        }

        // Mutation: would catch if 6h boundary is exclusive instead of inclusive
        [Fact]
        public void Bucket_Exactly6Hours_MinusThree()
        {
            var result = PlayerResponseDelayEvaluator.Evaluate(
                TimeSpan.FromHours(6), DefaultDatee, InterestState.Interested);

            Assert.Equal(-3, result.InterestDelta);
        }

        // Mutation: would catch if 23.999h overflows into 24+ bucket
        [Fact]
        public void Bucket_JustUnder24Hours_MinusThree()
        {
            var result = PlayerResponseDelayEvaluator.Evaluate(
                TimeSpan.FromHours(23.999), DefaultDatee, InterestState.Interested);

            Assert.Equal(-3, result.InterestDelta);
        }

        // Mutation: would catch if 24h boundary is exclusive instead of inclusive
        [Fact]
        public void Bucket_Exactly24Hours_MinusFive()
        {
            var result = PlayerResponseDelayEvaluator.Evaluate(
                TimeSpan.FromHours(24), DefaultDatee, InterestState.Interested);

            Assert.Equal(-5, result.InterestDelta);
        }

        // Mutation: would catch if 24+ bucket has wrong value (e.g. -4 instead of -5)
        [Fact]
        public void Bucket_48Hours_MinusFive()
        {
            var result = PlayerResponseDelayEvaluator.Evaluate(
                TimeSpan.FromHours(48), DefaultDatee, InterestState.Bored);

            Assert.Equal(-5, result.InterestDelta);
        }

        // Mutation: would catch if very large delays aren't handled
        [Fact]
        public void Bucket_30Days_MinusFive()
        {
            var result = PlayerResponseDelayEvaluator.Evaluate(
                TimeSpan.FromDays(30), DefaultDatee, InterestState.Interested);

            Assert.Equal(-5, result.InterestDelta);
        }

        #endregion
    }
}
