using Pinder.Core.Conversation;
using Xunit;

namespace Pinder.Core.Tests
{
    public class InterestMeterTests
    {
        private static InterestMeter MeterAt(int value)
        {
            var meter = new InterestMeter();
            // Apply delta from starting value (10) to reach desired value
            meter.Apply(value - InterestMeter.StartingValue);
            return meter;
        }

        // --- GetState boundary tests ---

        [Fact]
        public void Value0_IsUnmatched()
        {
            var meter = MeterAt(0);
            Assert.Equal(InterestState.Unmatched, meter.GetState());
        }

        [Theory]
        [InlineData(1)]
        [InlineData(4)]
        public void Value1To4_IsBored(int value)
        {
            var meter = MeterAt(value);
            Assert.Equal(InterestState.Bored, meter.GetState());
        }

        [Theory]
        [InlineData(5)]
        [InlineData(10)]
        [InlineData(15)]
        public void Value5To15_IsInterested(int value)
        {
            var meter = MeterAt(value);
            Assert.Equal(InterestState.Interested, meter.GetState());
        }

        [Theory]
        [InlineData(16)]
        [InlineData(20)]
        public void Value16To20_IsVeryIntoIt(int value)
        {
            var meter = MeterAt(value);
            Assert.Equal(InterestState.VeryIntoIt, meter.GetState());
        }

        [Theory]
        [InlineData(21)]
        [InlineData(24)]
        public void Value21To24_IsAlmostThere(int value)
        {
            var meter = MeterAt(value);
            Assert.Equal(InterestState.AlmostThere, meter.GetState());
        }

        [Fact]
        public void Value25_IsDateSecured()
        {
            var meter = MeterAt(25);
            Assert.Equal(InterestState.DateSecured, meter.GetState());
        }

        // --- GrantsAdvantage ---

        [Theory]
        [InlineData(16, true)]
        [InlineData(20, true)]
        [InlineData(21, true)]
        [InlineData(24, true)]
        [InlineData(15, false)]
        [InlineData(25, false)]
        [InlineData(0, false)]
        [InlineData(3, false)]
        public void GrantsAdvantage_CorrectForState(int value, bool expected)
        {
            var meter = MeterAt(value);
            Assert.Equal(expected, meter.GrantsAdvantage);
        }

        // --- GrantsDisadvantage ---

        [Theory]
        [InlineData(1, true)]
        [InlineData(4, true)]
        [InlineData(0, false)]   // Unmatched, not Bored
        [InlineData(5, false)]
        [InlineData(16, false)]
        public void GrantsDisadvantage_CorrectForState(int value, bool expected)
        {
            var meter = MeterAt(value);
            Assert.Equal(expected, meter.GrantsDisadvantage);
        }

        // --- Boundary transitions ---

        [Fact]
        public void BoundaryTransition_BoredToInterested()
        {
            var meter = MeterAt(4);
            Assert.Equal(InterestState.Bored, meter.GetState());
            meter.Apply(1);
            Assert.Equal(InterestState.Interested, meter.GetState());
        }

        [Fact]
        public void BoundaryTransition_InterestedToVeryIntoIt()
        {
            var meter = MeterAt(15);
            Assert.Equal(InterestState.Interested, meter.GetState());
            meter.Apply(1);
            Assert.Equal(InterestState.VeryIntoIt, meter.GetState());
        }

        [Fact]
        public void BoundaryTransition_VeryIntoItToAlmostThere()
        {
            var meter = MeterAt(20);
            Assert.Equal(InterestState.VeryIntoIt, meter.GetState());
            meter.Apply(1);
            Assert.Equal(InterestState.AlmostThere, meter.GetState());
        }

        [Fact]
        public void BoundaryTransition_AlmostThereToDateSecured()
        {
            var meter = MeterAt(24);
            Assert.Equal(InterestState.AlmostThere, meter.GetState());
            meter.Apply(1);
            Assert.Equal(InterestState.DateSecured, meter.GetState());
        }

        [Fact]
        public void ClampedAt25_StillDateSecured()
        {
            var meter = MeterAt(25);
            meter.Apply(10); // should stay at 25
            Assert.Equal(25, meter.Current);
            Assert.Equal(InterestState.DateSecured, meter.GetState());
        }
    }
}
