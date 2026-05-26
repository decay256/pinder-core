using System;
using Pinder.Core.Interfaces;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;
using Xunit;

namespace Pinder.Core.Tests
{
    public partial class Wave0SpecTests
    {
        // ==================================================================
        // AC2: IGameClock — boundary hours for TimeOfDay
        // ==================================================================

        // Mutation: Fails if hour 5 is classified as Morning instead of AfterTwoAm
        [Fact]
        public void FixedGameClock_Hour5_IsAfterTwoAm()
        {
            var clock = new FixedGameClock(new DateTimeOffset(2024, 1, 1, 5, 59, 59, TimeSpan.Zero));
            Assert.Equal(TimeOfDay.AfterTwoAm, clock.GetTimeOfDay());
        }

        // Mutation: Fails if hour 2 is classified as LateNight instead of AfterTwoAm
        [Fact]
        public void FixedGameClock_Hour2_IsAfterTwoAm()
        {
            var clock = new FixedGameClock(new DateTimeOffset(2024, 1, 1, 2, 0, 0, TimeSpan.Zero));
            Assert.Equal(TimeOfDay.AfterTwoAm, clock.GetTimeOfDay());
        }

        // Mutation: Fails if Advance doesn't actually change Now
        [Fact]
        public void FixedGameClock_Advance_ChangesTimeOfDay()
        {
            var clock = new FixedGameClock(new DateTimeOffset(2024, 1, 1, 11, 0, 0, TimeSpan.Zero));
            Assert.Equal(TimeOfDay.Morning, clock.GetTimeOfDay());
            clock.Advance(TimeSpan.FromHours(1));
            Assert.Equal(TimeOfDay.Afternoon, clock.GetTimeOfDay());
        }

        // Mutation: Fails if horniness modifiers have wrong values
        [Theory]
        [InlineData(8, -2)]    // Morning → -2
        [InlineData(14, 0)]    // Afternoon → 0
        [InlineData(20, 1)]    // Evening → +1
        [InlineData(23, 3)]    // LateNight → +3
        [InlineData(0, 3)]     // LateNight (hour 0) → +3
        [InlineData(4, 5)]     // AfterTwoAm → +5
        public void FixedGameClock_AllHorninessModifiers(int hour, int expected)
        {
            var clock = new FixedGameClock(new DateTimeOffset(2024, 1, 1, hour, 0, 0, TimeSpan.Zero));
            Assert.Equal(expected, clock.GetHorninessModifier());
        }

        // ==================================================================
        // AC10: ShadowThresholdEvaluator — boundary values
        // ==================================================================

        // Mutation: Fails if threshold boundary at 6 is off-by-one
        [Fact]
        public void ShadowThresholdEvaluator_BoundaryAt5And6()
        {
            Assert.Equal(0, ShadowThresholdEvaluator.GetThresholdLevel(5));
            Assert.Equal(1, ShadowThresholdEvaluator.GetThresholdLevel(6));
        }

        // Mutation: Fails if threshold boundary at 12 is off-by-one
        [Fact]
        public void ShadowThresholdEvaluator_BoundaryAt11And12()
        {
            Assert.Equal(1, ShadowThresholdEvaluator.GetThresholdLevel(11));
            Assert.Equal(2, ShadowThresholdEvaluator.GetThresholdLevel(12));
        }

        // Mutation: Fails if threshold boundary at 18 is off-by-one
        [Fact]
        public void ShadowThresholdEvaluator_BoundaryAt17And18()
        {
            Assert.Equal(2, ShadowThresholdEvaluator.GetThresholdLevel(17));
            Assert.Equal(3, ShadowThresholdEvaluator.GetThresholdLevel(18));
        }
    }
}
