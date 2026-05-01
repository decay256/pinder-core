using System;
using Pinder.Core.Conversation;
using Pinder.Core.Interfaces;
using Xunit;

namespace Pinder.Core.Tests
{
    /// <summary>
    /// Time-of-day and horniness-modifier behaviour for <see cref="GameClock"/>.
    /// Energy mechanics were removed in #786.
    /// </summary>
    [Trait("Category", "Core")]
    public class GameClockTests
    {
        private static DateTimeOffset MakeTime(int hour) =>
            new DateTimeOffset(2024, 1, 15, hour, 0, 0, TimeSpan.Zero);

        /// <summary>
        /// Default modifiers matching the game-definition.yaml values (issue #711).
        /// morning=3, afternoon=0, evening=2, overnight=5.
        /// </summary>
        private static readonly HorninessModifiers DefaultModifiers =
            new HorninessModifiers(morning: 3, afternoon: 0, evening: 2, overnight: 5);

        // --- Constructor ---

        [Fact]
        public void Constructor_SetsNow()
        {
            var start = MakeTime(10);
            var clock = new GameClock(start, DefaultModifiers);

            Assert.Equal(start, clock.Now);
        }

        [Fact]
        public void Constructor_NullModifiers_Throws()
        {
            Assert.Throws<ArgumentNullException>(
                () => new GameClock(MakeTime(10), null));
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
            var clock = new GameClock(MakeTime(hour), DefaultModifiers);
            Assert.Equal(expected, clock.GetTimeOfDay());
        }

        // --- GetHorninessModifier (configurable, issue #711) ---
        // New buckets: overnight(00-08)=5, morning(09-11)=3, afternoon(12-17)=0, evening(18-23)=2

        [Theory]
        [InlineData(0, 5)]   // overnight
        [InlineData(3, 5)]   // overnight
        [InlineData(8, 5)]   // overnight
        [InlineData(9, 3)]   // morning
        [InlineData(11, 3)]  // morning
        [InlineData(12, 0)]  // afternoon
        [InlineData(14, 0)]  // afternoon
        [InlineData(17, 0)]  // afternoon
        [InlineData(18, 2)]  // evening
        [InlineData(19, 2)]  // evening
        [InlineData(23, 2)]  // evening
        public void GetHorninessModifier_CorrectPerTimeOfDay(int hour, int expected)
        {
            var clock = new GameClock(MakeTime(hour), DefaultModifiers);
            Assert.Equal(expected, clock.GetHorninessModifier());
        }

        [Fact]
        public void GetHorninessModifier_UsesLoadedValues_NotHardcoded()
        {
            var customModifiers = new HorninessModifiers(morning: 10, afternoon: 20, evening: 30, overnight: 40);
            var clock = new GameClock(MakeTime(10), customModifiers); // morning hour (09-11) → 10
            Assert.Equal(10, clock.GetHorninessModifier());
        }

        // --- Advance ---

        [Fact]
        public void Advance_MovesTimeForward()
        {
            var clock = new GameClock(MakeTime(10), DefaultModifiers);
            clock.Advance(TimeSpan.FromHours(4));
            Assert.Equal(MakeTime(14), clock.Now);
        }

        [Fact]
        public void Advance_Zero_Throws()
        {
            var clock = new GameClock(MakeTime(10), DefaultModifiers);
            Assert.Throws<ArgumentOutOfRangeException>(
                () => clock.Advance(TimeSpan.Zero));
        }

        [Fact]
        public void Advance_Negative_Throws()
        {
            var clock = new GameClock(MakeTime(10), DefaultModifiers);
            Assert.Throws<ArgumentOutOfRangeException>(
                () => clock.Advance(TimeSpan.FromHours(-1)));
        }

        // --- AdvanceTo ---

        [Fact]
        public void AdvanceTo_SetsNow()
        {
            var clock = new GameClock(MakeTime(10), DefaultModifiers);
            var target = MakeTime(14);
            clock.AdvanceTo(target);
            Assert.Equal(target, clock.Now);
        }

        [Fact]
        public void AdvanceTo_SameTime_Throws()
        {
            var start = MakeTime(10);
            var clock = new GameClock(start, DefaultModifiers);
            Assert.Throws<ArgumentException>(() => clock.AdvanceTo(start));
        }

        [Fact]
        public void AdvanceTo_PastTime_Throws()
        {
            var clock = new GameClock(MakeTime(10), DefaultModifiers);
            Assert.Throws<ArgumentException>(
                () => clock.AdvanceTo(MakeTime(8)));
        }

        // --- IGameClock interface conformance ---

        [Fact]
        public void ImplementsIGameClock()
        {
            IGameClock clock = new GameClock(MakeTime(10), DefaultModifiers);
            Assert.NotNull(clock);
            Assert.Equal(MakeTime(10), clock.Now);
        }
    }
}
