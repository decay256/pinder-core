using System;
using Pinder.Core.Conversation;
using Pinder.Core.Interfaces;
using Xunit;

namespace Pinder.Core.Tests
{
    /// <summary>
    /// Tests for issue #711: configurable time-of-day Horniness modifiers.
    /// Verifies that HorninessModifiers are loaded from config and used by GameClock.
    /// </summary>
    [Trait("Category", "Core")]
    public class Issue711_HorninessTimeModifiersTests
    {
        private static DateTimeOffset MakeTime(int hour) =>
            new DateTimeOffset(2024, 1, 15, hour, 0, 0, TimeSpan.Zero);

        // Default modifiers matching game-definition.yaml spec
        private static readonly HorninessModifiers DefaultModifiers =
            new HorninessModifiers(morning: 3, afternoon: 0, evening: 2, overnight: 5);

        // ===== HorninessModifiers class =====

        [Fact]
        public void HorninessModifiers_Constructor_SetsAllProperties()
        {
            var m = new HorninessModifiers(morning: 3, afternoon: 0, evening: 2, overnight: 5);
            Assert.Equal(3, m.Morning);
            Assert.Equal(0, m.Afternoon);
            Assert.Equal(2, m.Evening);
            Assert.Equal(5, m.Overnight);
        }

        [Fact]
        public void HorninessModifiers_AllowsNegativeValues()
        {
            var m = new HorninessModifiers(morning: -3, afternoon: -1, evening: -2, overnight: -5);
            Assert.Equal(-3, m.Morning);
        }

        // ===== GameClock hour-to-bucket mapping (issue #711 spec) =====
        // morning: 09:00-11:59, afternoon: 12:00-17:59, evening: 18:00-23:59, overnight: 00:00-08:59

        [Theory]
        [InlineData(0, 5)]   // overnight start
        [InlineData(1, 5)]   // overnight
        [InlineData(4, 5)]   // overnight
        [InlineData(8, 5)]   // overnight end
        [InlineData(9, 3)]   // morning start
        [InlineData(10, 3)]  // morning mid
        [InlineData(11, 3)]  // morning end
        [InlineData(12, 0)]  // afternoon start
        [InlineData(15, 0)]  // afternoon mid
        [InlineData(17, 0)]  // afternoon end
        [InlineData(18, 2)]  // evening start
        [InlineData(21, 2)]  // evening mid
        [InlineData(23, 2)]  // evening end
        public void GetHorninessModifier_CorrectBucketAllHours(int hour, int expected)
        {
            var clock = new GameClock(MakeTime(hour), DefaultModifiers);
            Assert.Equal(expected, clock.GetHorninessModifier());
        }

        // ===== Config values are used, not hardcoded =====

        [Fact]
        public void GetHorninessModifier_UsesConfiguredMorningValue()
        {
            var modifiers = new HorninessModifiers(morning: 42, afternoon: 0, evening: 0, overnight: 0);
            var clock = new GameClock(MakeTime(10), modifiers); // hour 10 = morning
            Assert.Equal(42, clock.GetHorninessModifier());
        }

        [Fact]
        public void GetHorninessModifier_UsesConfiguredAfternoonValue()
        {
            var modifiers = new HorninessModifiers(morning: 0, afternoon: 77, evening: 0, overnight: 0);
            var clock = new GameClock(MakeTime(14), modifiers); // hour 14 = afternoon
            Assert.Equal(77, clock.GetHorninessModifier());
        }

        [Fact]
        public void GetHorninessModifier_UsesConfiguredEveningValue()
        {
            var modifiers = new HorninessModifiers(morning: 0, afternoon: 0, evening: 99, overnight: 0);
            var clock = new GameClock(MakeTime(20), modifiers); // hour 20 = evening
            Assert.Equal(99, clock.GetHorninessModifier());
        }

        [Fact]
        public void GetHorninessModifier_UsesConfiguredOvernightValue()
        {
            var modifiers = new HorninessModifiers(morning: 0, afternoon: 0, evening: 0, overnight: 55);
            var clock = new GameClock(MakeTime(3), modifiers); // hour 3 = overnight
            Assert.Equal(55, clock.GetHorninessModifier());
        }

        // ===== Null modifiers throws (no silent fallback) =====

        [Fact]
        public void GameClock_NullModifiers_ThrowsArgumentNullException()
        {
            var ex = Assert.Throws<ArgumentNullException>(
                () => new GameClock(MakeTime(10), null));
            Assert.Equal("modifiers", ex.ParamName);
        }

        // ===== Dynamic: modifier updates as clock advances =====

        [Fact]
        public void GetHorninessModifier_UpdatesWhenClockAdvances()
        {
            var clock = new GameClock(MakeTime(5), DefaultModifiers); // overnight → 5
            Assert.Equal(5, clock.GetHorninessModifier());

            clock.Advance(TimeSpan.FromHours(5)); // 5 + 5 = 10 → morning → 3
            Assert.Equal(3, clock.GetHorninessModifier());

            clock.Advance(TimeSpan.FromHours(3)); // 10 + 3 = 13 → afternoon → 0
            Assert.Equal(0, clock.GetHorninessModifier());

            clock.Advance(TimeSpan.FromHours(7)); // 13 + 7 = 20 → evening → 2
            Assert.Equal(2, clock.GetHorninessModifier());
        }

        // ===== Boundary: hour 8 (overnight) vs hour 9 (morning) =====

        [Fact]
        public void GetHorninessModifier_Hour8_IsOvernight_NotMorning()
        {
            var modifiers = new HorninessModifiers(morning: 100, afternoon: 0, evening: 0, overnight: 200);
            var clock = new GameClock(MakeTime(8), modifiers);
            Assert.Equal(200, clock.GetHorninessModifier()); // overnight, not morning
        }

        [Fact]
        public void GetHorninessModifier_Hour9_IsMorning_NotOvernight()
        {
            var modifiers = new HorninessModifiers(morning: 100, afternoon: 0, evening: 0, overnight: 200);
            var clock = new GameClock(MakeTime(9), modifiers);
            Assert.Equal(100, clock.GetHorninessModifier()); // morning, not overnight
        }

        // ===== Boundary: hour 11 (morning) vs hour 12 (afternoon) =====

        [Fact]
        public void GetHorninessModifier_Hour11_IsMorning_NotAfternoon()
        {
            var modifiers = new HorninessModifiers(morning: 111, afternoon: 222, evening: 0, overnight: 0);
            var clock = new GameClock(MakeTime(11), modifiers);
            Assert.Equal(111, clock.GetHorninessModifier()); // morning
        }

        [Fact]
        public void GetHorninessModifier_Hour12_IsAfternoon_NotMorning()
        {
            var modifiers = new HorninessModifiers(morning: 111, afternoon: 222, evening: 0, overnight: 0);
            var clock = new GameClock(MakeTime(12), modifiers);
            Assert.Equal(222, clock.GetHorninessModifier()); // afternoon
        }

        // ===== Boundary: hour 17 (afternoon) vs hour 18 (evening) =====

        [Fact]
        public void GetHorninessModifier_Hour17_IsAfternoon_NotEvening()
        {
            var modifiers = new HorninessModifiers(morning: 0, afternoon: 333, evening: 444, overnight: 0);
            var clock = new GameClock(MakeTime(17), modifiers);
            Assert.Equal(333, clock.GetHorninessModifier()); // afternoon
        }

        [Fact]
        public void GetHorninessModifier_Hour18_IsEvening_NotAfternoon()
        {
            var modifiers = new HorninessModifiers(morning: 0, afternoon: 333, evening: 444, overnight: 0);
            var clock = new GameClock(MakeTime(18), modifiers);
            Assert.Equal(444, clock.GetHorninessModifier()); // evening
        }

        // ===== Zero modifiers are valid =====

        [Fact]
        public void GetHorninessModifier_AllZero_AlwaysReturnsZero()
        {
            var modifiers = new HorninessModifiers(morning: 0, afternoon: 0, evening: 0, overnight: 0);
            for (int hour = 0; hour < 24; hour++)
            {
                var clock = new GameClock(MakeTime(hour), modifiers);
                Assert.Equal(0, clock.GetHorninessModifier());
            }
        }
    }
}
