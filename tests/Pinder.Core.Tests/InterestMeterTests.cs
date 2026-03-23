using Pinder.Core.Conversation;
using Xunit;

namespace Pinder.Core.Tests
{
    public class InterestMeterTests
    {
        // --- GetState boundary tests ---

        [Fact]
        public void GetState_AtZero_ReturnsUnmatched()
        {
            var meter = new InterestMeter();
            meter.Apply(-InterestMeter.StartingValue); // set to 0
            Assert.Equal(InterestState.Unmatched, meter.GetState());
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        public void GetState_Between1And4_ReturnsBored(int value)
        {
            var meter = CreateAtValue(value);
            Assert.Equal(InterestState.Bored, meter.GetState());
        }

        [Theory]
        [InlineData(5)]
        [InlineData(10)]
        [InlineData(15)]
        public void GetState_Between5And15_ReturnsInterested(int value)
        {
            var meter = CreateAtValue(value);
            Assert.Equal(InterestState.Interested, meter.GetState());
        }

        [Theory]
        [InlineData(16)]
        [InlineData(18)]
        [InlineData(20)]
        public void GetState_Between16And20_ReturnsVeryIntoIt(int value)
        {
            var meter = CreateAtValue(value);
            Assert.Equal(InterestState.VeryIntoIt, meter.GetState());
        }

        [Theory]
        [InlineData(21)]
        [InlineData(22)]
        [InlineData(24)]
        public void GetState_Between21And24_ReturnsAlmostThere(int value)
        {
            var meter = CreateAtValue(value);
            Assert.Equal(InterestState.AlmostThere, meter.GetState());
        }

        [Fact]
        public void GetState_At25_ReturnsDateSecured()
        {
            var meter = CreateAtValue(25);
            Assert.Equal(InterestState.DateSecured, meter.GetState());
        }

        // --- GrantsAdvantage tests ---

        [Theory]
        [InlineData(16, true)]
        [InlineData(20, true)]
        [InlineData(21, true)]
        [InlineData(24, true)]
        [InlineData(0, false)]
        [InlineData(4, false)]
        [InlineData(10, false)]
        [InlineData(25, false)]
        public void GrantsAdvantage_CorrectForState(int value, bool expected)
        {
            var meter = CreateAtValue(value);
            Assert.Equal(expected, meter.GrantsAdvantage);
        }

        // --- GrantsDisadvantage tests ---

        [Theory]
        [InlineData(1, true)]
        [InlineData(4, true)]
        [InlineData(0, false)]   // Unmatched, not Bored
        [InlineData(5, false)]
        [InlineData(16, false)]
        public void GrantsDisadvantage_CorrectForState(int value, bool expected)
        {
            var meter = CreateAtValue(value);
            Assert.Equal(expected, meter.GrantsDisadvantage);
        }

        // --- Boundary transitions ---

        [Fact]
        public void Boundary_4To5_BoredToInterested()
        {
            var meter = CreateAtValue(4);
            Assert.Equal(InterestState.Bored, meter.GetState());
            meter.Apply(1);
            Assert.Equal(InterestState.Interested, meter.GetState());
        }

        [Fact]
        public void Boundary_15To16_InterestedToVeryIntoIt()
        {
            var meter = CreateAtValue(15);
            Assert.Equal(InterestState.Interested, meter.GetState());
            meter.Apply(1);
            Assert.Equal(InterestState.VeryIntoIt, meter.GetState());
        }

        [Fact]
        public void Boundary_20To21_VeryIntoItToAlmostThere()
        {
            var meter = CreateAtValue(20);
            Assert.Equal(InterestState.VeryIntoIt, meter.GetState());
            meter.Apply(1);
            Assert.Equal(InterestState.AlmostThere, meter.GetState());
        }

        [Fact]
        public void Boundary_24To25_AlmostThereToDateSecured()
        {
            var meter = CreateAtValue(24);
            Assert.Equal(InterestState.AlmostThere, meter.GetState());
            meter.Apply(1);
            Assert.Equal(InterestState.DateSecured, meter.GetState());
        }

        // --- Helper ---

        private static InterestMeter CreateAtValue(int target)
        {
            var meter = new InterestMeter();
            meter.Apply(target - InterestMeter.StartingValue);
            return meter;
        }
    }
}
