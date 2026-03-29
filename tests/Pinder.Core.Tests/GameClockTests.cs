using System;
using Pinder.Core.Conversation;
using Pinder.Core.Interfaces;
using Xunit;

namespace Pinder.Core.Tests
{
    public class GameClockTests
    {
        private static DateTimeOffset MakeTime(int hour) =>
            new DateTimeOffset(2024, 1, 15, hour, 0, 0, TimeSpan.Zero);

        // --- Constructor ---

        [Fact]
        public void Constructor_SetsNowAndEnergy()
        {
            var start = MakeTime(10);
            var clock = new GameClock(start, dailyEnergy: 15);

            Assert.Equal(start, clock.Now);
            Assert.Equal(15, clock.RemainingEnergy);
        }

        [Fact]
        public void Constructor_DefaultEnergy_Is10()
        {
            var clock = new GameClock(MakeTime(10));
            Assert.Equal(10, clock.RemainingEnergy);
        }

        [Fact]
        public void Constructor_ZeroEnergy_Allowed()
        {
            var clock = new GameClock(MakeTime(10), dailyEnergy: 0);
            Assert.Equal(0, clock.RemainingEnergy);
        }

        [Fact]
        public void Constructor_NegativeEnergy_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new GameClock(MakeTime(10), dailyEnergy: -1));
        }

        // --- GetTimeOfDay boundaries ---

        [Theory]
        [InlineData(0, TimeOfDay.LateNight)]
        [InlineData(1, TimeOfDay.LateNight)]
        [InlineData(2, TimeOfDay.AfterTwoAm)]
        [InlineData(5, TimeOfDay.AfterTwoAm)]
        [InlineData(6, TimeOfDay.Morning)]
        [InlineData(11, TimeOfDay.Morning)]
        [InlineData(12, TimeOfDay.Afternoon)]
        [InlineData(17, TimeOfDay.Afternoon)]
        [InlineData(18, TimeOfDay.Evening)]
        [InlineData(21, TimeOfDay.Evening)]
        [InlineData(22, TimeOfDay.LateNight)]
        [InlineData(23, TimeOfDay.LateNight)]
        public void GetTimeOfDay_AllHourBoundaries(int hour, TimeOfDay expected)
        {
            var clock = new GameClock(MakeTime(hour));
            Assert.Equal(expected, clock.GetTimeOfDay());
        }

        // --- GetHorninessModifier ---

        [Theory]
        [InlineData(8, -2)]   // Morning
        [InlineData(14, 0)]   // Afternoon
        [InlineData(19, 1)]   // Evening
        [InlineData(23, 3)]   // LateNight
        [InlineData(0, 3)]    // LateNight (hour 0)
        [InlineData(3, 5)]    // AfterTwoAm
        public void GetHorninessModifier_CorrectPerTimeOfDay(int hour, int expected)
        {
            var clock = new GameClock(MakeTime(hour));
            Assert.Equal(expected, clock.GetHorninessModifier());
        }

        // --- Advance ---

        [Fact]
        public void Advance_MovesTimeForward()
        {
            var clock = new GameClock(MakeTime(10));
            clock.Advance(TimeSpan.FromHours(4));
            Assert.Equal(MakeTime(14), clock.Now);
        }

        [Fact]
        public void Advance_Zero_Throws()
        {
            var clock = new GameClock(MakeTime(10));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => clock.Advance(TimeSpan.Zero));
        }

        [Fact]
        public void Advance_Negative_Throws()
        {
            var clock = new GameClock(MakeTime(10));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => clock.Advance(TimeSpan.FromHours(-1)));
        }

        [Fact]
        public void Advance_CrossingMidnight_ReplenishesEnergy()
        {
            var clock = new GameClock(MakeTime(23), dailyEnergy: 10);
            clock.ConsumeEnergy(7);
            Assert.Equal(3, clock.RemainingEnergy);

            clock.Advance(TimeSpan.FromHours(2)); // crosses midnight
            Assert.Equal(10, clock.RemainingEnergy);
        }

        [Fact]
        public void Advance_SameDay_DoesNotReplenishEnergy()
        {
            var clock = new GameClock(MakeTime(10), dailyEnergy: 10);
            clock.ConsumeEnergy(5);
            clock.Advance(TimeSpan.FromHours(2));
            Assert.Equal(5, clock.RemainingEnergy);
        }

        [Fact]
        public void Advance_MultipleMidnightCrossings_ReplenishesEnergy()
        {
            var clock = new GameClock(MakeTime(23), dailyEnergy: 10);
            clock.ConsumeEnergy(10);
            clock.Advance(TimeSpan.FromHours(50)); // crosses midnight twice
            Assert.Equal(10, clock.RemainingEnergy);
        }

        // --- AdvanceTo ---

        [Fact]
        public void AdvanceTo_SetsNow()
        {
            var clock = new GameClock(MakeTime(10));
            var target = MakeTime(14);
            clock.AdvanceTo(target);
            Assert.Equal(target, clock.Now);
        }

        [Fact]
        public void AdvanceTo_SameTime_Throws()
        {
            var start = MakeTime(10);
            var clock = new GameClock(start);
            Assert.Throws<ArgumentException>(() => clock.AdvanceTo(start));
        }

        [Fact]
        public void AdvanceTo_PastTime_Throws()
        {
            var clock = new GameClock(MakeTime(10));
            Assert.Throws<ArgumentException>(
                () => clock.AdvanceTo(MakeTime(8)));
        }

        [Fact]
        public void AdvanceTo_CrossingMidnight_ReplenishesEnergy()
        {
            var clock = new GameClock(MakeTime(23), dailyEnergy: 10);
            clock.ConsumeEnergy(8);
            var target = new DateTimeOffset(2024, 1, 16, 1, 0, 0, TimeSpan.Zero);
            clock.AdvanceTo(target);
            Assert.Equal(10, clock.RemainingEnergy);
        }

        [Fact]
        public void AdvanceTo_SameDay_DoesNotReplenish()
        {
            var clock = new GameClock(MakeTime(10), dailyEnergy: 10);
            clock.ConsumeEnergy(3);
            clock.AdvanceTo(MakeTime(15));
            Assert.Equal(7, clock.RemainingEnergy);
        }

        // --- ConsumeEnergy ---

        [Fact]
        public void ConsumeEnergy_Success()
        {
            var clock = new GameClock(MakeTime(10), dailyEnergy: 15);
            Assert.True(clock.ConsumeEnergy(5));
            Assert.Equal(10, clock.RemainingEnergy);
        }

        [Fact]
        public void ConsumeEnergy_ExactlyRemaining_Success()
        {
            var clock = new GameClock(MakeTime(10), dailyEnergy: 10);
            Assert.True(clock.ConsumeEnergy(10));
            Assert.Equal(0, clock.RemainingEnergy);
        }

        [Fact]
        public void ConsumeEnergy_Insufficient_ReturnsFalseNoDeduction()
        {
            var clock = new GameClock(MakeTime(10), dailyEnergy: 3);
            Assert.False(clock.ConsumeEnergy(5));
            Assert.Equal(3, clock.RemainingEnergy);
        }

        [Fact]
        public void ConsumeEnergy_ZeroAmount_Throws()
        {
            var clock = new GameClock(MakeTime(10));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => clock.ConsumeEnergy(0));
        }

        [Fact]
        public void ConsumeEnergy_NegativeAmount_Throws()
        {
            var clock = new GameClock(MakeTime(10));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => clock.ConsumeEnergy(-1));
        }

        [Fact]
        public void ConsumeEnergy_ZeroEnergyBudget_AlwaysFails()
        {
            var clock = new GameClock(MakeTime(10), dailyEnergy: 0);
            Assert.False(clock.ConsumeEnergy(1));
            Assert.Equal(0, clock.RemainingEnergy);
        }

        // --- IGameClock interface conformance ---

        [Fact]
        public void ImplementsIGameClock()
        {
            IGameClock clock = new GameClock(MakeTime(10));
            Assert.NotNull(clock);
            Assert.Equal(MakeTime(10), clock.Now);
        }
    }
}
