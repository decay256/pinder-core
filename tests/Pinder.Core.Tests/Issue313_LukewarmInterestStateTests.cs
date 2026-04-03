using Pinder.Core.Conversation;
using Xunit;

namespace Pinder.Core.Tests
{
    /// <summary>
    /// Tests for issue #313: Lukewarm (5-9) as a distinct InterestState per rules §6.
    /// Verifies that the Interested range (previously 5-15) is properly split into
    /// Lukewarm (5-9) and Interested (10-15).
    /// </summary>
    public class Issue313_LukewarmInterestStateTests
    {
        // =====================================================================
        // AC1: InterestState.Lukewarm enum value exists
        // =====================================================================

        // Mutation: would catch if Lukewarm enum value is removed or renamed
        [Fact]
        public void InterestState_Lukewarm_ExistsAsEnumValue()
        {
            var lukewarm = InterestState.Lukewarm;
            Assert.Equal("Lukewarm", lukewarm.ToString());
        }

        // Mutation: would catch if Lukewarm is not positioned between Bored and Interested
        [Fact]
        public void InterestState_Lukewarm_OrdinalIsBetweenBoredAndInterested()
        {
            Assert.True((int)InterestState.Lukewarm > (int)InterestState.Bored,
                "Lukewarm should have a higher ordinal than Bored");
            Assert.True((int)InterestState.Lukewarm < (int)InterestState.Interested,
                "Lukewarm should have a lower ordinal than Interested");
        }

        // Mutation: would catch if enum has wrong number of values (missing Lukewarm or extra)
        [Fact]
        public void InterestState_Has7DistinctValues()
        {
            var values = Enum.GetValues(typeof(InterestState));
            Assert.Equal(7, values.Length);
        }

        // =====================================================================
        // AC2: GetState() returns Lukewarm for values 5-9
        // =====================================================================

        // Mutation: would catch if lower bound of Lukewarm is wrong (e.g., 4 or 6 instead of 5)
        [Fact]
        public void GetState_At5_ReturnsLukewarm()
        {
            var meter = CreateAtValue(5);
            Assert.Equal(InterestState.Lukewarm, meter.GetState());
        }

        // Mutation: would catch if upper bound of Lukewarm is wrong (e.g., 8 or 10 instead of 9)
        [Fact]
        public void GetState_At9_ReturnsLukewarm()
        {
            var meter = CreateAtValue(9);
            Assert.Equal(InterestState.Lukewarm, meter.GetState());
        }

        // Mutation: would catch if middle of Lukewarm range is not covered
        [Theory]
        [InlineData(5)]
        [InlineData(6)]
        [InlineData(7)]
        [InlineData(8)]
        [InlineData(9)]
        public void GetState_AllValuesInLukewarmRange_ReturnLukewarm(int value)
        {
            var meter = CreateAtValue(value);
            Assert.Equal(InterestState.Lukewarm, meter.GetState());
        }

        // =====================================================================
        // AC3: GetState() returns Interested for 10-15 (not 5-15)
        // =====================================================================

        // Mutation: would catch if Interested still starts at 5 instead of 10
        [Fact]
        public void GetState_At10_ReturnsInterested_NotLukewarm()
        {
            var meter = CreateAtValue(10);
            Assert.Equal(InterestState.Interested, meter.GetState());
        }

        // Mutation: would catch if Interested upper bound changed from 15
        [Fact]
        public void GetState_At15_ReturnsInterested()
        {
            var meter = CreateAtValue(15);
            Assert.Equal(InterestState.Interested, meter.GetState());
        }

        // Mutation: would catch if any value 10-15 returns wrong state
        [Theory]
        [InlineData(10)]
        [InlineData(11)]
        [InlineData(12)]
        [InlineData(13)]
        [InlineData(14)]
        [InlineData(15)]
        public void GetState_AllValuesInInterestedRange_ReturnInterested(int value)
        {
            var meter = CreateAtValue(value);
            Assert.Equal(InterestState.Interested, meter.GetState());
        }

        // =====================================================================
        // AC4: Boundary transitions — Bored/Lukewarm and Lukewarm/Interested
        // =====================================================================

        // Mutation: would catch if boundary between Bored and Lukewarm is at wrong value
        [Fact]
        public void Boundary_4IsBored_5IsLukewarm()
        {
            var meter4 = CreateAtValue(4);
            var meter5 = CreateAtValue(5);
            Assert.Equal(InterestState.Bored, meter4.GetState());
            Assert.Equal(InterestState.Lukewarm, meter5.GetState());
        }

        // Mutation: would catch if boundary between Lukewarm and Interested is at wrong value
        [Fact]
        public void Boundary_9IsLukewarm_10IsInterested()
        {
            var meter9 = CreateAtValue(9);
            var meter10 = CreateAtValue(10);
            Assert.Equal(InterestState.Lukewarm, meter9.GetState());
            Assert.Equal(InterestState.Interested, meter10.GetState());
        }

        // Mutation: would catch if Apply() across boundary doesn't transition correctly
        [Fact]
        public void Transition_BoredToLukewarm_ViaApply()
        {
            var meter = CreateAtValue(4);
            Assert.Equal(InterestState.Bored, meter.GetState());
            meter.Apply(1);
            Assert.Equal(InterestState.Lukewarm, meter.GetState());
        }

        // Mutation: would catch if Apply() across Lukewarm/Interested boundary fails
        [Fact]
        public void Transition_LukewarmToInterested_ViaApply()
        {
            var meter = CreateAtValue(9);
            Assert.Equal(InterestState.Lukewarm, meter.GetState());
            meter.Apply(1);
            Assert.Equal(InterestState.Interested, meter.GetState());
        }

        // Mutation: would catch if negative transitions from Interested skip Lukewarm
        [Fact]
        public void Transition_InterestedToLukewarm_ViaApply()
        {
            var meter = CreateAtValue(10);
            Assert.Equal(InterestState.Interested, meter.GetState());
            meter.Apply(-1);
            Assert.Equal(InterestState.Lukewarm, meter.GetState());
        }

        // Mutation: would catch if negative transitions from Lukewarm skip to wrong state
        [Fact]
        public void Transition_LukewarmToBored_ViaApply()
        {
            var meter = CreateAtValue(5);
            Assert.Equal(InterestState.Lukewarm, meter.GetState());
            meter.Apply(-1);
            Assert.Equal(InterestState.Bored, meter.GetState());
        }

        // =====================================================================
        // AC5: GrantsAdvantage unaffected — Lukewarm does NOT grant advantage
        // =====================================================================

        // Mutation: would catch if Lukewarm incorrectly grants advantage
        [Theory]
        [InlineData(5)]
        [InlineData(7)]
        [InlineData(9)]
        public void GrantsAdvantage_Lukewarm_ReturnsFalse(int value)
        {
            var meter = CreateAtValue(value);
            Assert.False(meter.GrantsAdvantage,
                $"Lukewarm (value={value}) should NOT grant advantage");
        }

        // Mutation: would catch if Lukewarm incorrectly grants disadvantage
        [Theory]
        [InlineData(5)]
        [InlineData(7)]
        [InlineData(9)]
        public void GrantsDisadvantage_Lukewarm_ReturnsFalse(int value)
        {
            var meter = CreateAtValue(value);
            Assert.False(meter.GrantsDisadvantage,
                $"Lukewarm (value={value}) should NOT grant disadvantage");
        }

        // =====================================================================
        // AC6: Other states unaffected by Lukewarm addition
        // =====================================================================

        // Mutation: would catch if Unmatched range broke after enum insertion
        [Fact]
        public void GetState_At0_StillReturnsUnmatched()
        {
            var meter = CreateAtValue(0);
            Assert.Equal(InterestState.Unmatched, meter.GetState());
        }

        // Mutation: would catch if Bored range broke after enum insertion
        [Theory]
        [InlineData(1)]
        [InlineData(4)]
        public void GetState_BoredRange_StillReturnsBored(int value)
        {
            var meter = CreateAtValue(value);
            Assert.Equal(InterestState.Bored, meter.GetState());
        }

        // Mutation: would catch if VeryIntoIt range broke after enum insertion
        [Theory]
        [InlineData(16)]
        [InlineData(20)]
        public void GetState_VeryIntoItRange_StillReturnsVeryIntoIt(int value)
        {
            var meter = CreateAtValue(value);
            Assert.Equal(InterestState.VeryIntoIt, meter.GetState());
        }

        // Mutation: would catch if AlmostThere range broke
        [Theory]
        [InlineData(21)]
        [InlineData(24)]
        public void GetState_AlmostThereRange_StillReturnsAlmostThere(int value)
        {
            var meter = CreateAtValue(value);
            Assert.Equal(InterestState.AlmostThere, meter.GetState());
        }

        // Mutation: would catch if DateSecured broke
        [Fact]
        public void GetState_At25_StillReturnsDateSecured()
        {
            var meter = CreateAtValue(25);
            Assert.Equal(InterestState.DateSecured, meter.GetState());
        }

        // =====================================================================
        // AC7: Advantage/disadvantage for non-Lukewarm states still correct
        // =====================================================================

        // Mutation: would catch if Bored disadvantage broke after enum change
        [Fact]
        public void GrantsDisadvantage_Bored_StillTrue()
        {
            var meter = CreateAtValue(3);
            Assert.True(meter.GrantsDisadvantage);
        }

        // Mutation: would catch if VeryIntoIt advantage broke after enum change
        [Fact]
        public void GrantsAdvantage_VeryIntoIt_StillTrue()
        {
            var meter = CreateAtValue(18);
            Assert.True(meter.GrantsAdvantage);
        }

        // Mutation: would catch if AlmostThere advantage broke after enum change
        [Fact]
        public void GrantsAdvantage_AlmostThere_StillTrue()
        {
            var meter = CreateAtValue(22);
            Assert.True(meter.GrantsAdvantage);
        }

        // Mutation: would catch if Interested incorrectly gains advantage/disadvantage
        [Fact]
        public void Interested_NoAdvantageOrDisadvantage()
        {
            var meter = CreateAtValue(12);
            Assert.False(meter.GrantsAdvantage);
            Assert.False(meter.GrantsDisadvantage);
        }

        // =====================================================================
        // Full sweep: every value 0-25 returns a valid state
        // =====================================================================

        // Mutation: would catch if any value in 0-25 throws or returns unexpected state
        [Theory]
        [InlineData(0, InterestState.Unmatched)]
        [InlineData(1, InterestState.Bored)]
        [InlineData(4, InterestState.Bored)]
        [InlineData(5, InterestState.Lukewarm)]
        [InlineData(9, InterestState.Lukewarm)]
        [InlineData(10, InterestState.Interested)]
        [InlineData(15, InterestState.Interested)]
        [InlineData(16, InterestState.VeryIntoIt)]
        [InlineData(20, InterestState.VeryIntoIt)]
        [InlineData(21, InterestState.AlmostThere)]
        [InlineData(24, InterestState.AlmostThere)]
        [InlineData(25, InterestState.DateSecured)]
        public void GetState_AllBoundaryValues_ReturnCorrectState(int value, InterestState expected)
        {
            var meter = CreateAtValue(value);
            Assert.Equal(expected, meter.GetState());
        }

        // =====================================================================
        // Starting value (10) should be Interested, not Lukewarm
        // =====================================================================

        // Mutation: would catch if default starting interest falls in Lukewarm instead of Interested
        [Fact]
        public void DefaultStartingValue_IsInterested_NotLukewarm()
        {
            var meter = new InterestMeter();
            Assert.Equal(InterestState.Interested, meter.GetState());
        }

        // =====================================================================
        // Helper
        // =====================================================================

        private static InterestMeter CreateAtValue(int target)
        {
            var meter = new InterestMeter();
            meter.Apply(target - InterestMeter.StartingValue);
            return meter;
        }
    }
}
