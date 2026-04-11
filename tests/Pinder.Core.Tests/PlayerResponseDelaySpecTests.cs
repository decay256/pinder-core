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
    public class PlayerResponseDelaySpecTests
    {
        #region Test Helpers

        /// <summary>
        /// Creates a StatBlock with configurable Chaos base and shadow stats.
        /// All other stats default to 2 / 0.
        /// </summary>
        private static StatBlock MakeOpponent(
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

        private static StatBlock DefaultOpponent => MakeOpponent();

        #endregion

        #region AC1: Method exists and returns DelayPenalty

        // Mutation: would catch if Evaluate doesn't exist or returns wrong type
        [Fact]
        public void Evaluate_ReturnsDelayPenaltyInstance()
        {
            var result = PlayerResponseDelayEvaluator.Evaluate(
                TimeSpan.FromMinutes(5), DefaultOpponent, InterestState.Interested);

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
                TimeSpan.FromSeconds(30), DefaultOpponent, InterestState.Interested);

            Assert.Equal(0, result.InterestDelta);
        }

        // Mutation: would catch if TimeSpan.Zero is not handled as < 1 min
        [Fact]
        public void Bucket_Zero_ZeroPenalty()
        {
            var result = PlayerResponseDelayEvaluator.Evaluate(
                TimeSpan.Zero, DefaultOpponent, InterestState.Interested);

            Assert.Equal(0, result.InterestDelta);
        }

        // Mutation: would catch if 59 seconds is misclassified into 1-15 min bucket
        [Fact]
        public void Bucket_59Seconds_ZeroPenalty()
        {
            var result = PlayerResponseDelayEvaluator.Evaluate(
                TimeSpan.FromSeconds(59), DefaultOpponent, InterestState.Interested);

            Assert.Equal(0, result.InterestDelta);
        }

        // Mutation: would catch if 1-15 min bucket returns non-zero
        [Fact]
        public void Bucket_1To15Min_ZeroPenalty()
        {
            var result = PlayerResponseDelayEvaluator.Evaluate(
                TimeSpan.FromMinutes(1), DefaultOpponent, InterestState.Interested);

            Assert.Equal(0, result.InterestDelta);
        }

        // Mutation: would catch if boundary at exactly 1 min is mishandled
        [Fact]
        public void Bucket_Exactly1Min_ZeroPenalty()
        {
            var result = PlayerResponseDelayEvaluator.Evaluate(
                TimeSpan.FromMinutes(1), DefaultOpponent, InterestState.VeryIntoIt);

            Assert.Equal(0, result.InterestDelta);
        }

        // Mutation: would catch if 14.999 minutes overflows into 15-60 min bucket
        [Fact]
        public void Bucket_Just_Under15Min_ZeroPenalty()
        {
            var result = PlayerResponseDelayEvaluator.Evaluate(
                TimeSpan.FromMinutes(14.999), DefaultOpponent, InterestState.VeryIntoIt);

            Assert.Equal(0, result.InterestDelta);
        }

        // Mutation: would catch if 15 min boundary is exclusive instead of inclusive
        [Fact]
        public void Bucket_Exactly15Min_VeryIntoIt_MinusOne()
        {
            var result = PlayerResponseDelayEvaluator.Evaluate(
                TimeSpan.FromMinutes(15), DefaultOpponent, InterestState.VeryIntoIt);

            Assert.Equal(-1, result.InterestDelta);
        }

        // Mutation: would catch if 15-60 min bucket doesn't gate on interest state
        [Fact]
        public void Bucket_15Min_Interested_ZeroPenalty()
        {
            var result = PlayerResponseDelayEvaluator.Evaluate(
                TimeSpan.FromMinutes(15), DefaultOpponent, InterestState.Interested);

            Assert.Equal(0, result.InterestDelta);
        }

        // Mutation: would catch if AlmostThere is not included in the interest gate
        [Fact]
        public void Bucket_30Min_AlmostThere_MinusOne()
        {
            var result = PlayerResponseDelayEvaluator.Evaluate(
                TimeSpan.FromMinutes(30), DefaultOpponent, InterestState.AlmostThere);

            Assert.Equal(-1, result.InterestDelta);
        }

        // Mutation: would catch if 59.999 minutes overflows into 1-6h bucket
        [Fact]
        public void Bucket_JustUnder60Min_VeryIntoIt_MinusOne()
        {
            var result = PlayerResponseDelayEvaluator.Evaluate(
                TimeSpan.FromMinutes(59.999), DefaultOpponent, InterestState.VeryIntoIt);

            Assert.Equal(-1, result.InterestDelta);
        }

        // Mutation: would catch if 60 min boundary is exclusive instead of inclusive
        [Fact]
        public void Bucket_Exactly60Min_MinusTwo()
        {
            var result = PlayerResponseDelayEvaluator.Evaluate(
                TimeSpan.FromMinutes(60), DefaultOpponent, InterestState.Interested);

            Assert.Equal(-2, result.InterestDelta);
        }

        // Mutation: would catch if 1-6h base penalty is wrong value (e.g. -1 instead of -2)
        [Fact]
        public void Bucket_3Hours_MinusTwo()
        {
            var result = PlayerResponseDelayEvaluator.Evaluate(
                TimeSpan.FromHours(3), DefaultOpponent, InterestState.Interested);

            Assert.Equal(-2, result.InterestDelta);
        }

        // Mutation: would catch if 5.999h overflows into 6-24h bucket
        [Fact]
        public void Bucket_JustUnder6Hours_MinusTwo()
        {
            var result = PlayerResponseDelayEvaluator.Evaluate(
                TimeSpan.FromHours(5.999), DefaultOpponent, InterestState.Interested);

            Assert.Equal(-2, result.InterestDelta);
        }

        // Mutation: would catch if 6h boundary is exclusive instead of inclusive
        [Fact]
        public void Bucket_Exactly6Hours_MinusThree()
        {
            var result = PlayerResponseDelayEvaluator.Evaluate(
                TimeSpan.FromHours(6), DefaultOpponent, InterestState.Interested);

            Assert.Equal(-3, result.InterestDelta);
        }

        // Mutation: would catch if 23.999h overflows into 24+ bucket
        [Fact]
        public void Bucket_JustUnder24Hours_MinusThree()
        {
            var result = PlayerResponseDelayEvaluator.Evaluate(
                TimeSpan.FromHours(23.999), DefaultOpponent, InterestState.Interested);

            Assert.Equal(-3, result.InterestDelta);
        }

        // Mutation: would catch if 24h boundary is exclusive instead of inclusive
        [Fact]
        public void Bucket_Exactly24Hours_MinusFive()
        {
            var result = PlayerResponseDelayEvaluator.Evaluate(
                TimeSpan.FromHours(24), DefaultOpponent, InterestState.Interested);

            Assert.Equal(-5, result.InterestDelta);
        }

        // Mutation: would catch if 24+ bucket has wrong value (e.g. -4 instead of -5)
        [Fact]
        public void Bucket_48Hours_MinusFive()
        {
            var result = PlayerResponseDelayEvaluator.Evaluate(
                TimeSpan.FromHours(48), DefaultOpponent, InterestState.Bored);

            Assert.Equal(-5, result.InterestDelta);
        }

        // Mutation: would catch if very large delays aren't handled
        [Fact]
        public void Bucket_30Days_MinusFive()
        {
            var result = PlayerResponseDelayEvaluator.Evaluate(
                TimeSpan.FromDays(30), DefaultOpponent, InterestState.Interested);

            Assert.Equal(-5, result.InterestDelta);
        }

        #endregion

        #region AC2 continued: Interest state gating for 15-60 min bucket

        // Mutation: would catch if Unmatched is treated as >= 16
        [Fact]
        public void Gate_15Min_Unmatched_ZeroPenalty()
        {
            var result = PlayerResponseDelayEvaluator.Evaluate(
                TimeSpan.FromMinutes(30), DefaultOpponent, InterestState.Unmatched);

            Assert.Equal(0, result.InterestDelta);
        }

        // Mutation: would catch if Bored is treated as >= 16
        [Fact]
        public void Gate_15Min_Bored_ZeroPenalty()
        {
            var result = PlayerResponseDelayEvaluator.Evaluate(
                TimeSpan.FromMinutes(30), DefaultOpponent, InterestState.Bored);

            Assert.Equal(0, result.InterestDelta);
        }

        // Mutation: would catch if DateSecured is excluded from >= 16 set
        [Fact]
        public void Gate_15Min_DateSecured_MinusOne()
        {
            var result = PlayerResponseDelayEvaluator.Evaluate(
                TimeSpan.FromMinutes(30), DefaultOpponent, InterestState.DateSecured);

            Assert.Equal(-1, result.InterestDelta);
        }

        #endregion

        #region AC3: Chaos base stat >= 4 reduces penalty to 0

        // Mutation: would catch if Chaos threshold is > 4 instead of >= 4
        [Fact]
        public void Chaos4_ZeroesPenalty()
        {
            var stats = MakeOpponent(chaosBase: 4);
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
            var stats = MakeOpponent(chaosBase: 3);
            var result = PlayerResponseDelayEvaluator.Evaluate(
                TimeSpan.FromHours(3), stats, InterestState.Interested);

            Assert.Equal(-2, result.InterestDelta);
        }

        // Mutation: would catch if Chaos >= 4 doesn't override Fixation+Overthinking
        [Fact]
        public void Chaos5_OverridesAllModifiers()
        {
            var stats = MakeOpponent(chaosBase: 5, fixation: 10, overthinking: 10);
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
            var stats = MakeOpponent(chaosBase: 4);
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
            var stats = MakeOpponent(fixation: 6);
            var result = PlayerResponseDelayEvaluator.Evaluate(
                TimeSpan.FromHours(12), stats, InterestState.Interested);

            // Base -3 doubled to -6
            Assert.Equal(-6, result.InterestDelta);
        }

        // Mutation: would catch if Fixation threshold is > 6 instead of >= 6
        [Fact]
        public void Fixation5_NoDoubling()
        {
            var stats = MakeOpponent(fixation: 5);
            var result = PlayerResponseDelayEvaluator.Evaluate(
                TimeSpan.FromHours(12), stats, InterestState.Interested);

            Assert.Equal(-3, result.InterestDelta);
        }

        // Mutation: would catch if Fixation doubling is additive instead of multiplicative
        [Fact]
        public void Fixation6_Doubles_1to6hBucket()
        {
            var stats = MakeOpponent(fixation: 6);
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
            var stats = MakeOpponent(overthinking: 6);
            var result = PlayerResponseDelayEvaluator.Evaluate(
                TimeSpan.FromHours(2), stats, InterestState.Interested);

            // Base -2, Overthinking adds -1 = -3
            Assert.Equal(-3, result.InterestDelta);
        }

        // Mutation: would catch if Overthinking threshold is > 6 instead of >= 6
        [Fact]
        public void Overthinking5_NoExtraPenalty()
        {
            var stats = MakeOpponent(overthinking: 5);
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
            var stats = MakeOpponent(fixation: 7, overthinking: 8);
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
                TimeSpan.FromHours(3), DefaultOpponent, InterestState.Interested);

            Assert.True(result.TriggerTest);
            Assert.NotNull(result.TestPrompt);
        }

        // Mutation: would catch if TestPrompt is empty string instead of meaningful
        [Fact]
        public void TriggerTest_1to6h_TestPromptNonEmpty()
        {
            var result = PlayerResponseDelayEvaluator.Evaluate(
                TimeSpan.FromHours(2), DefaultOpponent, InterestState.Interested);

            Assert.True(result.TriggerTest);
            Assert.False(string.IsNullOrEmpty(result.TestPrompt));
        }

        // Mutation: would catch if TriggerTest fires for < 1h bucket
        [Fact]
        public void TriggerTest_Under1h_False()
        {
            var result = PlayerResponseDelayEvaluator.Evaluate(
                TimeSpan.FromMinutes(30), DefaultOpponent, InterestState.VeryIntoIt);

            Assert.False(result.TriggerTest);
            Assert.Null(result.TestPrompt);
        }

        // Mutation: would catch if TriggerTest fires for 6-24h bucket
        [Fact]
        public void TriggerTest_6to24hBucket_False()
        {
            var result = PlayerResponseDelayEvaluator.Evaluate(
                TimeSpan.FromHours(12), DefaultOpponent, InterestState.Interested);

            Assert.False(result.TriggerTest);
            Assert.Null(result.TestPrompt);
        }

        // Mutation: would catch if TriggerTest fires for 24+ bucket
        [Fact]
        public void TriggerTest_24PlusBucket_False()
        {
            var result = PlayerResponseDelayEvaluator.Evaluate(
                TimeSpan.FromHours(48), DefaultOpponent, InterestState.Interested);

            Assert.False(result.TriggerTest);
            Assert.Null(result.TestPrompt);
        }

        // Mutation: would catch if TriggerTest fires for < 1 min bucket
        [Fact]
        public void TriggerTest_LessThan1Min_False()
        {
            var result = PlayerResponseDelayEvaluator.Evaluate(
                TimeSpan.FromSeconds(30), DefaultOpponent, InterestState.Interested);

            Assert.False(result.TriggerTest);
            Assert.Null(result.TestPrompt);
        }

        // Mutation: would catch if TriggerTest fires when Chaos >= 4 zeroes penalty
        [Fact]
        public void TriggerTest_ChaosZeroesPenalty_NoTrigger()
        {
            var stats = MakeOpponent(chaosBase: 4);
            var result = PlayerResponseDelayEvaluator.Evaluate(
                TimeSpan.FromHours(3), stats, InterestState.Interested);

            Assert.False(result.TriggerTest);
        }

        // Mutation: would catch if TriggerTest at exactly 1 hour boundary is wrong
        [Fact]
        public void TriggerTest_Exactly1Hour_True()
        {
            var result = PlayerResponseDelayEvaluator.Evaluate(
                TimeSpan.FromHours(1), DefaultOpponent, InterestState.Interested);

            Assert.True(result.TriggerTest);
        }

        // Mutation: would catch if TriggerTest at 5.999h is not triggered
        [Fact]
        public void TriggerTest_JustUnder6Hours_True()
        {
            var result = PlayerResponseDelayEvaluator.Evaluate(
                TimeSpan.FromHours(5.999), DefaultOpponent, InterestState.Interested);

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

        #region Spec Examples (Section 3)

        // Mutation: would catch if Example 1 doesn't match spec output
        [Fact]
        public void SpecExample1_ShortDelay_NoPenalty()
        {
            var result = PlayerResponseDelayEvaluator.Evaluate(
                TimeSpan.FromMinutes(5), MakeOpponent(chaosBase: 2), InterestState.Interested);

            Assert.Equal(0, result.InterestDelta);
            Assert.False(result.TriggerTest);
            Assert.Null(result.TestPrompt);
        }

        // Mutation: would catch if Example 2 interest gating is broken
        [Fact]
        public void SpecExample2_30Min_InterestBelowThreshold()
        {
            var result = PlayerResponseDelayEvaluator.Evaluate(
                TimeSpan.FromMinutes(30), MakeOpponent(chaosBase: 2), InterestState.Interested);

            Assert.Equal(0, result.InterestDelta);
            Assert.False(result.TriggerTest);
            Assert.Null(result.TestPrompt);
        }

        // Mutation: would catch if Example 3 VeryIntoIt gate is broken
        [Fact]
        public void SpecExample3_30Min_VeryIntoIt()
        {
            var result = PlayerResponseDelayEvaluator.Evaluate(
                TimeSpan.FromMinutes(30), MakeOpponent(chaosBase: 2), InterestState.VeryIntoIt);

            Assert.Equal(-1, result.InterestDelta);
            Assert.False(result.TriggerTest);
            Assert.Null(result.TestPrompt);
        }

        // Mutation: would catch if Example 4 trigger test is missing
        [Fact]
        public void SpecExample4_3Hours_TriggersTest()
        {
            var result = PlayerResponseDelayEvaluator.Evaluate(
                TimeSpan.FromHours(3), MakeOpponent(chaosBase: 2), InterestState.Interested);

            Assert.Equal(-2, result.InterestDelta);
            Assert.True(result.TriggerTest);
            Assert.NotNull(result.TestPrompt);
        }

        // Mutation: would catch if Example 5 Chaos override is broken
        [Fact]
        public void SpecExample5_3Hours_HighChaos_NoPenalty()
        {
            var result = PlayerResponseDelayEvaluator.Evaluate(
                TimeSpan.FromHours(3), MakeOpponent(chaosBase: 4), InterestState.Interested);

            Assert.Equal(0, result.InterestDelta);
            Assert.False(result.TriggerTest);
            Assert.Null(result.TestPrompt);
        }

        // Mutation: would catch if Example 6 Fixation doubling is broken
        [Fact]
        public void SpecExample6_12Hours_FixationDoubles()
        {
            var result = PlayerResponseDelayEvaluator.Evaluate(
                TimeSpan.FromHours(12), MakeOpponent(chaosBase: 2, fixation: 6), InterestState.Interested);

            Assert.Equal(-6, result.InterestDelta);
            Assert.False(result.TriggerTest);
        }

        // Mutation: would catch if Example 7 Overthinking +1 is not applied
        [Fact]
        public void SpecExample7_2Hours_Overthinking()
        {
            var result = PlayerResponseDelayEvaluator.Evaluate(
                TimeSpan.FromHours(2), MakeOpponent(chaosBase: 2, overthinking: 6), InterestState.Interested);

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
                MakeOpponent(chaosBase: 2, fixation: 7, overthinking: 8),
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
                TimeSpan.FromHours(48), MakeOpponent(chaosBase: 2), InterestState.Bored);

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
                TimeSpan.FromMinutes(-10), DefaultOpponent, InterestState.Interested);

            Assert.Equal(0, result.InterestDelta);
            Assert.False(result.TriggerTest);
            Assert.Null(result.TestPrompt);
        }

        // Mutation: would catch if null opponentStats doesn't throw ArgumentNullException
        [Fact]
        public void NullOpponentStats_ThrowsArgumentNullException()
        {
            var ex = Assert.Throws<ArgumentNullException>(() =>
                PlayerResponseDelayEvaluator.Evaluate(
                    TimeSpan.FromHours(1), null!, InterestState.Interested));

            Assert.Equal("opponentStats", ex.ParamName);
        }

        // Mutation: would catch if undefined enum value throws instead of being handled
        [Fact]
        public void UndefinedInterestState_DoesNotThrow()
        {
            var result = PlayerResponseDelayEvaluator.Evaluate(
                TimeSpan.FromMinutes(30), DefaultOpponent, (InterestState)999);

            // Undefined enum should be treated as "not >= 16", so no penalty in 15-60 min bucket
            Assert.Equal(0, result.InterestDelta);
        }

        // Mutation: would catch if method ever returns null
        [Fact]
        public void Evaluate_NeverReturnsNull()
        {
            var result = PlayerResponseDelayEvaluator.Evaluate(
                TimeSpan.Zero, DefaultOpponent, InterestState.Interested);

            Assert.NotNull(result);
        }

        #endregion

        #region Edge Cases: Zero-stat opponent

        // Mutation: would catch if zero stats cause exceptions or wrong behavior
        [Fact]
        public void ZeroStatOpponent_PureBasePenaltyApplies()
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
            var stats = MakeOpponent(denial: 10);
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
            var stats = MakeOpponent(fixation: 10);
            var result = PlayerResponseDelayEvaluator.Evaluate(
                TimeSpan.FromMinutes(5), stats, InterestState.Interested);

            Assert.Equal(0, result.InterestDelta);
        }

        // Mutation: would catch if Overthinking adds -1 even when base is 0
        [Fact]
        public void Overthinking6_WithZeroBasePenalty_StillZero()
        {
            // Spec Section 8: "If the base penalty from step 1–2 is already 0, skip steps 4–5"
            var stats = MakeOpponent(overthinking: 10);
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
            var stats = MakeOpponent(fixation: 6);
            var result = PlayerResponseDelayEvaluator.Evaluate(
                TimeSpan.FromMinutes(30), stats, InterestState.VeryIntoIt);

            // Base -1 doubled to -2
            Assert.Equal(-2, result.InterestDelta);
        }

        // Mutation: would catch if Overthinking on -1 (15-60 min, high interest) is broken
        [Fact]
        public void Overthinking6_15To60MinBucket_VeryIntoIt()
        {
            var stats = MakeOpponent(overthinking: 6);
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
            var stats = MakeOpponent(fixation: 6);
            var result = PlayerResponseDelayEvaluator.Evaluate(
                TimeSpan.FromHours(48), stats, InterestState.Interested);

            // Base -5 doubled to -10
            Assert.Equal(-10, result.InterestDelta);
        }

        // Mutation: would catch if both modifiers on 24+ bucket produce wrong result
        [Fact]
        public void BothModifiers_24PlusBucket()
        {
            var stats = MakeOpponent(fixation: 6, overthinking: 6);
            var result = PlayerResponseDelayEvaluator.Evaluate(
                TimeSpan.FromHours(48), stats, InterestState.Interested);

            // Base -5, Fixation doubles to -10, Overthinking -1 = -11
            Assert.Equal(-11, result.InterestDelta);
        }

        #endregion
    }
}
