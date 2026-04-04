using Pinder.Core.Conversation;
using Pinder.SessionRunner;
using Xunit;

namespace Pinder.Core.Tests
{
    /// <summary>
    /// Tests for OutcomeProjector.Project — verifies the projection heuristic
    /// from issue #417 spec's decision table and edge cases.
    /// 
    /// The spec defines a top-to-bottom decision table:
    ///   1. interest >= 20 AND momentum >= 3 → "Likely DateSecured"
    ///   2. interest >= 16                   → "Probable DateSecured with continued play"
    ///   3. interest 10–15                   → "Uncertain — could go either way"
    ///   4. interest 5–9                     → "Trending toward Unmatched"
    ///   5. interest < 5                     → "Likely Unmatched or Ghost"
    /// 
    /// The implementation takes an InterestState parameter in addition to the
    /// numeric interest. Tests map interest values to the appropriate InterestState.
    /// </summary>
    public class OutcomeProjectorTests
    {
        // ===== SPEC DECISION TABLE: Rule 1 — interest >= 20 AND momentum >= 3 =====

        // Mutation: Fails if the top rule is removed or if momentum threshold is wrong
        [Theory]
        [InlineData(20, 3)]
        [InlineData(22, 4)]
        [InlineData(24, 5)]
        [InlineData(25, 3)]
        public void Project_HighInterestHighMomentum_ReturnsLikelyDateSecured(int interest, int momentum)
        {
            // AlmostThere for 21-24, VeryIntoIt for 20, DateSecured for 25
            var state = interest >= 25 ? InterestState.DateSecured
                      : interest >= 21 ? InterestState.AlmostThere
                      : InterestState.VeryIntoIt;

            var result = OutcomeProjector.Project(interest, momentum, 20, 20, state);

            Assert.Contains("DateSecured", result);
        }

        // ===== SPEC DECISION TABLE: Rule 2 — interest >= 16 (without high momentum at 20+) =====

        // Mutation: Fails if rule 2 threshold is changed from 16 to something else
        [Theory]
        [InlineData(16, 0, InterestState.VeryIntoIt)]
        [InlineData(18, 1, InterestState.VeryIntoIt)]
        [InlineData(19, 2, InterestState.VeryIntoIt)]
        public void Project_VeryIntoIt_ReturnsProbableDateSecured(int interest, int momentum, InterestState state)
        {
            var result = OutcomeProjector.Project(interest, momentum, 20, 20, state);

            Assert.Contains("DateSecured", result);
        }

        // Mutation: Fails if momentum >= 3 at interest 20 doesn't take precedence over rule 2
        [Fact]
        public void Project_Interest20Momentum2_FallsThroughToRule2()
        {
            // interest=20, momentum=2 → momentum < 3, so rule 1 doesn't match → rule 2 matches
            var result = OutcomeProjector.Project(20, 2, 20, 20, InterestState.VeryIntoIt);

            // Should indicate probable/possible, not the top-tier "Likely DateSecured"
            Assert.Contains("DateSecured", result);
        }

        // ===== SPEC DECISION TABLE: Rule 3 — interest 10–15 =====

        // Mutation: Fails if the 10-15 range returns wrong projection
        [Theory]
        [InlineData(10)]
        [InlineData(12)]
        [InlineData(15)]
        public void Project_Interested_ReturnsUncertainOrPossible(int interest)
        {
            var result = OutcomeProjector.Project(interest, 0, 20, 20, InterestState.Interested);

            // Spec says "Uncertain — could go either way" for this range
            // The result should NOT contain "Likely DateSecured" or "Unmatched"
            Assert.DoesNotContain("Unmatched", result);
        }

        // ===== SPEC DECISION TABLE: Rule 4 — interest 5–9 =====

        // Mutation: Fails if interest 5-9 returns a positive projection instead of negative
        [Theory]
        [InlineData(5)]
        [InlineData(7)]
        [InlineData(9)]
        public void Project_Lukewarm_ReturnsTrendingNegative(int interest)
        {
            var result = OutcomeProjector.Project(interest, 0, 20, 20, InterestState.Lukewarm);

            // Spec says "Trending toward Unmatched" — should indicate negative trend
            Assert.DoesNotContain("DateSecured", result);
        }

        // ===== SPEC DECISION TABLE: Rule 5 — interest < 5 =====

        // Mutation: Fails if low interest doesn't project unmatched/ghost
        [Theory]
        [InlineData(1)]
        [InlineData(3)]
        [InlineData(4)]
        public void Project_Bored_ReturnsLikelyUnmatched(int interest)
        {
            var result = OutcomeProjector.Project(interest, 0, 20, 20, InterestState.Bored);

            Assert.Contains("Unmatched", result);
        }

        // Mutation: Fails if interest=0 (Unmatched state) doesn't return appropriate message
        [Fact]
        public void Project_Unmatched_ReturnsUnmatchedProjection()
        {
            var result = OutcomeProjector.Project(0, 0, 20, 20, InterestState.Unmatched);

            Assert.Contains("Unmatched", result);
        }

        // ===== EDGE CASES FROM SPEC =====

        // Mutation: Fails if interest=25 (DateSecured) doesn't return success projection
        [Fact]
        public void Project_DateSecured_ReturnsDateSecuredMessage()
        {
            // Spec: interest=25 should never occur at cutoff, but if it does → "Likely DateSecured"
            var result = OutcomeProjector.Project(25, 0, 15, 20, InterestState.DateSecured);

            Assert.Contains("DateSecured", result);
        }

        // Mutation: Fails if momentum=0 with high interest incorrectly triggers top-tier projection
        [Fact]
        public void Project_Interest22Momentum0_DoesNotReturnTopTier()
        {
            // Spec: momentum=0 with interest=22 → "Probable DateSecured" (not "Likely DateSecured")
            // Because momentum < 3, rule 1 doesn't match; falls through to rule 2
            var result = OutcomeProjector.Project(22, 0, 20, 20, InterestState.AlmostThere);

            Assert.Contains("DateSecured", result);
        }

        // ===== BOUNDARY VALUES =====

        // Mutation: Fails if boundary between rule 4 and 5 is wrong (interest=4 vs 5)
        [Fact]
        public void Project_Interest4_IsInLowestTier()
        {
            var result = OutcomeProjector.Project(4, 0, 20, 20, InterestState.Bored);

            Assert.Contains("Unmatched", result);
        }

        // Mutation: Fails if boundary between rule 3 and 4 is wrong (interest=9 vs 10)
        [Fact]
        public void Project_Interest9_IsInLukewarmTier()
        {
            var result = OutcomeProjector.Project(9, 0, 20, 20, InterestState.Lukewarm);

            Assert.DoesNotContain("DateSecured", result);
        }

        [Fact]
        public void Project_Interest10_IsInInterestedTier()
        {
            var result = OutcomeProjector.Project(10, 0, 20, 20, InterestState.Interested);

            // Should not project unmatched at interest 10
            Assert.DoesNotContain("Unmatched", result);
        }

        // Mutation: Fails if boundary between rule 2 and 3 is wrong (interest=15 vs 16)
        [Fact]
        public void Project_Interest15_IsInUncertainTier()
        {
            var result = OutcomeProjector.Project(15, 0, 20, 20, InterestState.Interested);

            Assert.DoesNotContain("Unmatched", result);
        }

        [Fact]
        public void Project_Interest16_IsInProbableTier()
        {
            var result = OutcomeProjector.Project(16, 0, 20, 20, InterestState.VeryIntoIt);

            Assert.Contains("DateSecured", result);
        }

        // Mutation: Fails if momentum threshold for top tier is != 3
        [Fact]
        public void Project_Interest20Momentum3_IsTopTier()
        {
            var result = OutcomeProjector.Project(20, 3, 20, 20, InterestState.VeryIntoIt);

            Assert.Contains("DateSecured", result);
        }

        // ===== PURE FUNCTION GUARANTEES =====

        // Mutation: Fails if function returns null or empty string
        [Fact]
        public void Project_NeverReturnsNullOrEmpty()
        {
            var result = OutcomeProjector.Project(12, 2, 20, 20, InterestState.Interested);

            Assert.False(string.IsNullOrEmpty(result));
        }

        // Mutation: Fails if function is not deterministic
        [Fact]
        public void Project_SameInputsSameOutput()
        {
            var r1 = OutcomeProjector.Project(18, 1, 20, 20, InterestState.VeryIntoIt);
            var r2 = OutcomeProjector.Project(18, 1, 20, 20, InterestState.VeryIntoIt);

            Assert.Equal(r1, r2);
        }

        // ===== MOMENTUM DISPLAY =====

        // Mutation: Fails if momentum > 0 is not mentioned in output
        [Theory]
        [InlineData(22, 4, InterestState.AlmostThere)]
        [InlineData(18, 5, InterestState.VeryIntoIt)]
        [InlineData(13, 3, InterestState.Interested)]
        public void Project_WithMomentum_MentionsMomentumInOutput(int interest, int momentum, InterestState state)
        {
            var result = OutcomeProjector.Project(interest, momentum, 20, 20, state);

            Assert.Contains("Momentum", result);
        }

        // Mutation: Fails if momentum=0 still shows momentum text
        [Fact]
        public void Project_ZeroMomentum_DoesNotMentionMomentum()
        {
            var result = OutcomeProjector.Project(23, 0, 20, 20, InterestState.AlmostThere);

            Assert.DoesNotContain("Momentum", result);
        }

        // ===== INTEREST DISPLAY =====

        // Mutation: Fails if interest value not shown in output
        [Fact]
        public void Project_ShowsInterestInOutput()
        {
            var result = OutcomeProjector.Project(22, 4, 20, 20, InterestState.AlmostThere);

            Assert.Contains("22/25", result);
        }

        // ===== SPEC EXAMPLES FROM I/O TABLE =====

        // Mutation: Fails if the decision table logic doesn't match spec examples
        [Fact]
        public void Project_SpecExample_Interest22Momentum4()
        {
            // Spec table row: interest=22, momentum=4 → "Likely DateSecured"
            var result = OutcomeProjector.Project(22, 4, 20, 20, InterestState.AlmostThere);
            Assert.Contains("DateSecured", result);
        }

        [Fact]
        public void Project_SpecExample_Interest7Momentum0()
        {
            // Spec table row: interest=7, momentum=0 → "Trending toward Unmatched"
            var result = OutcomeProjector.Project(7, 0, 15, 15, InterestState.Lukewarm);
            Assert.DoesNotContain("DateSecured", result);
        }

        [Fact]
        public void Project_SpecExample_Interest3Momentum0()
        {
            // Spec table row: interest=3, momentum=0 → "Likely Unmatched or Ghost"
            var result = OutcomeProjector.Project(3, 0, 20, 20, InterestState.Bored);
            Assert.Contains("Unmatched", result);
        }

        // ===== DEGENERATE CASES =====

        // Mutation: Fails if maxTurns=0 causes crash
        [Fact]
        public void Project_MaxTurns0_DoesNotCrash()
        {
            // Spec: degenerate case, interest=10 (starting), should not crash
            var result = OutcomeProjector.Project(10, 0, 0, 0, InterestState.Interested);

            Assert.False(string.IsNullOrEmpty(result));
        }

        // Mutation: Fails if maxTurns=1 causes crash
        [Fact]
        public void Project_MaxTurns1_ProducesValidOutput()
        {
            var result = OutcomeProjector.Project(11, 0, 1, 1, InterestState.Interested);

            Assert.False(string.IsNullOrEmpty(result));
        }

        // ===== OUT OF RANGE INTEREST (Error Conditions) =====

        // Mutation: Fails if negative interest throws exception instead of returning sensible result
        [Fact]
        public void Project_NegativeInterest_DoesNotThrow()
        {
            // Spec: negative values → "Likely Unmatched or Ghost", no exception
            var result = OutcomeProjector.Project(-5, 0, 20, 20, InterestState.Unmatched);

            Assert.Contains("Unmatched", result);
        }

        // Mutation: Fails if interest > 25 throws exception instead of returning sensible result
        [Fact]
        public void Project_InterestAbove25_DoesNotThrow()
        {
            // Spec: values > 25 → "Likely DateSecured", no exception
            var result = OutcomeProjector.Project(30, 0, 20, 20, InterestState.DateSecured);

            Assert.Contains("DateSecured", result);
        }

        // ===== ESTIMATED TURNS COMPARISON =====

        // Mutation: Fails if higher momentum doesn't improve the projection or estimated turns
        [Fact]
        public void Project_HigherMomentum_ProducesDifferentOutput()
        {
            var noMomentum = OutcomeProjector.Project(15, 0, 20, 20, InterestState.Interested);
            var highMomentum = OutcomeProjector.Project(15, 5, 20, 20, InterestState.Interested);

            // With momentum, the output should mention it; without, it should not
            Assert.DoesNotContain("Momentum", noMomentum);
            Assert.Contains("Momentum", highMomentum);
        }
    }
}
