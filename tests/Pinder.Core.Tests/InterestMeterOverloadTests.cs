using Pinder.Core.Conversation;
using Xunit;

namespace Pinder.Core.Tests
{
    public class InterestMeterOverloadTests
    {
        [Fact]
        public void DefaultConstructor_StartsAt10()
        {
            var meter = new InterestMeter();
            Assert.Equal(10, meter.Current);
        }

        [Theory]
        [InlineData(0, 0)]
        [InlineData(8, 8)]
        [InlineData(25, 25)]
        [InlineData(-5, 0)]     // clamped to Min
        [InlineData(-100, 0)]   // clamped to Min
        [InlineData(30, 25)]    // clamped to Max
        [InlineData(100, 25)]   // clamped to Max
        [InlineData(3, 3)]
        public void IntConstructor_ClampsCorrectly(int input, int expected)
        {
            var meter = new InterestMeter(input);
            Assert.Equal(expected, meter.Current);
        }

        [Fact]
        public void IntConstructor_Zero_Unmatched()
        {
            var meter = new InterestMeter(0);
            Assert.Equal(InterestState.Unmatched, meter.GetState());
        }

        [Fact]
        public void IntConstructor_25_DateSecured()
        {
            var meter = new InterestMeter(25);
            Assert.Equal(InterestState.DateSecured, meter.GetState());
        }

        [Fact]
        public void IntConstructor_3_Bored()
        {
            var meter = new InterestMeter(3);
            Assert.Equal(InterestState.Bored, meter.GetState());
        }

        [Fact]
        public void IntConstructor_ApplyStillWorks()
        {
            var meter = new InterestMeter(3);
            meter.Apply(-5);
            Assert.Equal(0, meter.Current);
            Assert.Equal(InterestState.Unmatched, meter.GetState());
        }

        [Fact]
        public void IntConstructor_PropertiesWork()
        {
            var meter = new InterestMeter(16);
            Assert.True(meter.GrantsAdvantage);
            Assert.False(meter.GrantsDisadvantage);
        }
    }
}
