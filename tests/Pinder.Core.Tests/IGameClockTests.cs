using System;
using Pinder.Core.Interfaces;
using Xunit;

namespace Pinder.Core.Tests
{
    /// <summary>
    /// Tests for the IGameClock interface contract via a minimal FixedGameClock test double.
    /// Validates the TimeOfDay enum values and horniness modifier mapping.
    /// </summary>
    public class IGameClockTests
    {
        [Fact]
        public void TimeOfDay_HasFiveValues()
        {
            var values = Enum.GetValues(typeof(TimeOfDay));
            Assert.Equal(5, values.Length);
        }

        [Fact]
        public void TimeOfDay_EnumValuesExist()
        {
            Assert.True(Enum.IsDefined(typeof(TimeOfDay), TimeOfDay.Morning));
            Assert.True(Enum.IsDefined(typeof(TimeOfDay), TimeOfDay.Afternoon));
            Assert.True(Enum.IsDefined(typeof(TimeOfDay), TimeOfDay.Evening));
            Assert.True(Enum.IsDefined(typeof(TimeOfDay), TimeOfDay.LateNight));
            Assert.True(Enum.IsDefined(typeof(TimeOfDay), TimeOfDay.AfterTwoAm));
        }

        [Fact]
        public void FixedGameClock_GetTimeOfDay_Morning()
        {
            var clock = new FixedGameClock(new DateTimeOffset(2024, 1, 1, 8, 0, 0, TimeSpan.Zero));
            Assert.Equal(TimeOfDay.Morning, clock.GetTimeOfDay());
        }

        [Fact]
        public void FixedGameClock_GetTimeOfDay_Afternoon()
        {
            var clock = new FixedGameClock(new DateTimeOffset(2024, 1, 1, 14, 0, 0, TimeSpan.Zero));
            Assert.Equal(TimeOfDay.Afternoon, clock.GetTimeOfDay());
        }

        [Fact]
        public void FixedGameClock_GetTimeOfDay_Evening()
        {
            var clock = new FixedGameClock(new DateTimeOffset(2024, 1, 1, 19, 0, 0, TimeSpan.Zero));
            Assert.Equal(TimeOfDay.Evening, clock.GetTimeOfDay());
        }

        [Fact]
        public void FixedGameClock_GetTimeOfDay_LateNight()
        {
            var clock = new FixedGameClock(new DateTimeOffset(2024, 1, 1, 23, 0, 0, TimeSpan.Zero));
            Assert.Equal(TimeOfDay.LateNight, clock.GetTimeOfDay());
        }

        [Fact]
        public void FixedGameClock_GetTimeOfDay_LateNight_Hour0()
        {
            var clock = new FixedGameClock(new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero));
            Assert.Equal(TimeOfDay.LateNight, clock.GetTimeOfDay());
        }

        [Fact]
        public void FixedGameClock_GetTimeOfDay_LateNight_Hour1()
        {
            var clock = new FixedGameClock(new DateTimeOffset(2024, 1, 1, 1, 30, 0, TimeSpan.Zero));
            Assert.Equal(TimeOfDay.LateNight, clock.GetTimeOfDay());
        }

        [Fact]
        public void FixedGameClock_GetTimeOfDay_AfterTwoAm()
        {
            var clock = new FixedGameClock(new DateTimeOffset(2024, 1, 1, 3, 0, 0, TimeSpan.Zero));
            Assert.Equal(TimeOfDay.AfterTwoAm, clock.GetTimeOfDay());
        }

        [Theory]
        [InlineData(6, -2)]   // Morning
        [InlineData(12, 0)]   // Afternoon
        [InlineData(18, 1)]   // Evening
        [InlineData(22, 3)]   // LateNight
        [InlineData(3, 5)]    // AfterTwoAm
        public void FixedGameClock_GetHorninessModifier(int hour, int expected)
        {
            var clock = new FixedGameClock(new DateTimeOffset(2024, 1, 1, hour, 0, 0, TimeSpan.Zero));
            Assert.Equal(expected, clock.GetHorninessModifier());
        }

        [Fact]
        public void FixedGameClock_Advance()
        {
            var clock = new FixedGameClock(new DateTimeOffset(2024, 1, 1, 8, 0, 0, TimeSpan.Zero));
            clock.Advance(TimeSpan.FromHours(6));
            Assert.Equal(TimeOfDay.Afternoon, clock.GetTimeOfDay());
        }

        [Fact]
        public void FixedGameClock_AdvanceTo_Future()
        {
            var start = new DateTimeOffset(2024, 1, 1, 8, 0, 0, TimeSpan.Zero);
            var clock = new FixedGameClock(start);
            var target = start.AddHours(12);
            clock.AdvanceTo(target);
            Assert.Equal(target, clock.Now);
        }

        [Fact]
        public void FixedGameClock_AdvanceTo_Past_Throws()
        {
            var start = new DateTimeOffset(2024, 1, 1, 8, 0, 0, TimeSpan.Zero);
            var clock = new FixedGameClock(start);
            Assert.Throws<ArgumentException>(() => clock.AdvanceTo(start.AddHours(-1)));
        }

        [Fact]
        public void FixedGameClock_AdvanceTo_SameTime_Throws()
        {
            var start = new DateTimeOffset(2024, 1, 1, 8, 0, 0, TimeSpan.Zero);
            var clock = new FixedGameClock(start);
            Assert.Throws<ArgumentException>(() => clock.AdvanceTo(start));
        }

        [Fact]
        public void FixedGameClock_ConsumeEnergy_Succeeds()
        {
            var clock = new FixedGameClock(new DateTimeOffset(2024, 1, 1, 8, 0, 0, TimeSpan.Zero), energy: 10);
            Assert.True(clock.ConsumeEnergy(5));
            Assert.Equal(5, clock.RemainingEnergy);
        }

        [Fact]
        public void FixedGameClock_ConsumeEnergy_Insufficient_ReturnsFalse()
        {
            var clock = new FixedGameClock(new DateTimeOffset(2024, 1, 1, 8, 0, 0, TimeSpan.Zero), energy: 3);
            Assert.False(clock.ConsumeEnergy(5));
            Assert.Equal(3, clock.RemainingEnergy); // no deduction
        }

        /// <summary>
        /// Boundary: hour 6 is Morning start, hour 11 is still Morning
        /// </summary>
        [Theory]
        [InlineData(6, TimeOfDay.Morning)]
        [InlineData(11, TimeOfDay.Morning)]
        [InlineData(12, TimeOfDay.Afternoon)]
        [InlineData(17, TimeOfDay.Afternoon)]
        [InlineData(18, TimeOfDay.Evening)]
        [InlineData(21, TimeOfDay.Evening)]
        [InlineData(22, TimeOfDay.LateNight)]
        [InlineData(23, TimeOfDay.LateNight)]
        [InlineData(0, TimeOfDay.LateNight)]
        [InlineData(1, TimeOfDay.LateNight)]
        [InlineData(2, TimeOfDay.AfterTwoAm)]
        [InlineData(5, TimeOfDay.AfterTwoAm)]
        public void FixedGameClock_HourBoundaries(int hour, TimeOfDay expected)
        {
            var clock = new FixedGameClock(new DateTimeOffset(2024, 1, 1, hour, 0, 0, TimeSpan.Zero));
            Assert.Equal(expected, clock.GetTimeOfDay());
        }
    }

    /// <summary>
    /// Minimal IGameClock test double for testing Wave 0 components.
    /// </summary>
    internal sealed class FixedGameClock : IGameClock
    {
        public DateTimeOffset Now { get; private set; }
        public int RemainingEnergy { get; private set; }

        public FixedGameClock(DateTimeOffset now, int energy = 10)
        {
            Now = now;
            RemainingEnergy = energy;
        }

        public void Advance(TimeSpan amount) => Now = Now.Add(amount);

        public void AdvanceTo(DateTimeOffset target)
        {
            if (target <= Now)
                throw new ArgumentException("Target must be in the future.", nameof(target));
            Now = target;
        }

        public TimeOfDay GetTimeOfDay()
        {
            int hour = Now.Hour;
            if (hour >= 6 && hour <= 11) return TimeOfDay.Morning;
            if (hour >= 12 && hour <= 17) return TimeOfDay.Afternoon;
            if (hour >= 18 && hour <= 21) return TimeOfDay.Evening;
            if (hour >= 22 || hour <= 1) return TimeOfDay.LateNight;
            return TimeOfDay.AfterTwoAm; // 2-5
        }

        public int GetHorninessModifier()
        {
            switch (GetTimeOfDay())
            {
                case TimeOfDay.Morning: return -2;
                case TimeOfDay.Afternoon: return 0;
                case TimeOfDay.Evening: return 1;
                case TimeOfDay.LateNight: return 3;
                case TimeOfDay.AfterTwoAm: return 5;
                default: return 0;
            }
        }

        public bool ConsumeEnergy(int amount)
        {
            if (amount > RemainingEnergy) return false;
            RemainingEnergy -= amount;
            return true;
        }
    }
}
