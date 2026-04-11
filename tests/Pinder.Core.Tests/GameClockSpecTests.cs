using System;
using Pinder.Core.Conversation;
using Pinder.Core.Interfaces;
using Xunit;

namespace Pinder.Core.Tests
{
    /// <summary>
    /// Spec-driven tests for GameClock (issue #54, updated by issue #711).
    /// Based on docs/specs/issue-54-spec.md acceptance criteria.
    /// Issue #711: GetHorninessModifier() is now configurable via HorninessModifiers.
    /// </summary>
    public class GameClockSpecTests
    {
        private static DateTimeOffset MakeTime(int hour, int minute = 0) =>
            new DateTimeOffset(2024, 1, 15, hour, minute, 0, TimeSpan.Zero);

        /// <summary>
        /// Default modifiers matching game-definition.yaml (issue #711).
        /// morning(09-11)=3, afternoon(12-17)=0, evening(18-23)=2, overnight(00-08)=5.
        /// </summary>
        private static readonly HorninessModifiers DefaultModifiers =
            new HorninessModifiers(morning: 3, afternoon: 0, evening: 2, overnight: 5);

        // ===== AC1: IGameClock interface exists =====

        // Mutation: Would catch if IGameClock interface was removed or renamed
        [Fact]
        public void AC1_IGameClock_InterfaceExists()
        {
            var type = typeof(IGameClock);
            Assert.True(type.IsInterface);
        }

        // Mutation: Would catch if TimeOfDay enum was moved to a different namespace
        [Fact]
        public void AC1_TimeOfDay_EnumInInterfacesNamespace()
        {
            Assert.Equal("Pinder.Core.Interfaces", typeof(TimeOfDay).Namespace);
        }

        // Mutation: Would catch if IGameClock was moved to wrong namespace
        [Fact]
        public void AC1_IGameClock_InInterfacesNamespace()
        {
            Assert.Equal("Pinder.Core.Interfaces", typeof(IGameClock).Namespace);
        }

        // ===== AC2: GameClock is sealed and implements IGameClock =====

        // Mutation: Would catch if GameClock was not sealed
        [Fact]
        public void AC2_GameClock_IsSealed()
        {
            Assert.True(typeof(GameClock).IsSealed);
        }

        // Mutation: Would catch if GameClock did not implement IGameClock
        [Fact]
        public void AC2_GameClock_ImplementsIGameClock()
        {
            Assert.True(typeof(IGameClock).IsAssignableFrom(typeof(GameClock)));
        }

        // Mutation: Would catch if GameClock was in wrong namespace
        [Fact]
        public void AC2_GameClock_InConversationNamespace()
        {
            Assert.Equal("Pinder.Core.Conversation", typeof(GameClock).Namespace);
        }

        // ===== AC4: TimeOfDay enum with correct hour ranges — full 24-hour coverage =====

        // Mutation: Would catch if any hour maps to wrong TimeOfDay
        [Theory]
        [InlineData(0, TimeOfDay.LateNight)]
        [InlineData(1, TimeOfDay.LateNight)]
        [InlineData(2, TimeOfDay.AfterTwoAm)]
        [InlineData(3, TimeOfDay.AfterTwoAm)]
        [InlineData(4, TimeOfDay.AfterTwoAm)]
        [InlineData(5, TimeOfDay.AfterTwoAm)]
        [InlineData(6, TimeOfDay.Morning)]
        [InlineData(7, TimeOfDay.Morning)]
        [InlineData(8, TimeOfDay.Morning)]
        [InlineData(9, TimeOfDay.Morning)]
        [InlineData(10, TimeOfDay.Morning)]
        [InlineData(11, TimeOfDay.Morning)]
        [InlineData(12, TimeOfDay.Afternoon)]
        [InlineData(13, TimeOfDay.Afternoon)]
        [InlineData(14, TimeOfDay.Afternoon)]
        [InlineData(15, TimeOfDay.Afternoon)]
        [InlineData(16, TimeOfDay.Afternoon)]
        [InlineData(17, TimeOfDay.Afternoon)]
        [InlineData(18, TimeOfDay.Evening)]
        [InlineData(19, TimeOfDay.Evening)]
        [InlineData(20, TimeOfDay.Evening)]
        [InlineData(21, TimeOfDay.Evening)]
        [InlineData(22, TimeOfDay.LateNight)]
        [InlineData(23, TimeOfDay.LateNight)]
        public void AC4_GetTimeOfDay_AllHours(int hour, TimeOfDay expected)
        {
            var clock = new GameClock(MakeTime(hour), DefaultModifiers);
            Assert.Equal(expected, clock.GetTimeOfDay());
        }

        // Mutation: Would catch if exactly 5 enum values was changed (e.g. added/removed one)
        [Fact]
        public void AC4_TimeOfDay_ExactlyFiveValues()
        {
            Assert.Equal(5, Enum.GetValues(typeof(TimeOfDay)).Length);
        }

        // ===== AC5: GetHorninessModifier returns configurable values (issue #711) =====
        // New 4-bucket system: overnight(00-08), morning(09-11), afternoon(12-17), evening(18-23)
        // Default values from game-definition.yaml: overnight=5, morning=3, afternoon=0, evening=2

        // Mutation: Would catch if morning bucket (09-11) returned wrong value
        [Fact]
        public void AC5_HorninessModifier_MorningBucket_Returns3()
        {
            var clock = new GameClock(MakeTime(9), DefaultModifiers);
            Assert.Equal(3, clock.GetHorninessModifier());
        }

        // Mutation: Would catch if afternoon bucket (12-17) returned wrong value
        [Fact]
        public void AC5_HorninessModifier_AfternoonBucket_Returns0()
        {
            var clock = new GameClock(MakeTime(12), DefaultModifiers);
            Assert.Equal(0, clock.GetHorninessModifier());
        }

        // Mutation: Would catch if evening bucket (18-23) returned wrong value
        [Fact]
        public void AC5_HorninessModifier_EveningBucket_Returns2()
        {
            var clock = new GameClock(MakeTime(18), DefaultModifiers);
            Assert.Equal(2, clock.GetHorninessModifier());
        }

        // Mutation: Would catch if evening bucket boundary (23) was misclassified
        [Fact]
        public void AC5_HorninessModifier_Hour23_EveningBucket_Returns2()
        {
            var clock = new GameClock(MakeTime(23), DefaultModifiers);
            Assert.Equal(2, clock.GetHorninessModifier());
        }

        // Mutation: Would catch if overnight bucket (00-08) returned wrong value
        [Fact]
        public void AC5_HorninessModifier_OvernightBucket_Hour0_Returns5()
        {
            var clock = new GameClock(MakeTime(0), DefaultModifiers);
            Assert.Equal(5, clock.GetHorninessModifier());
        }

        // Mutation: Would catch if overnight bucket boundary (08) was misclassified as morning
        [Fact]
        public void AC5_HorninessModifier_OvernightBucket_Hour8_Returns5()
        {
            var clock = new GameClock(MakeTime(8), DefaultModifiers);
            Assert.Equal(5, clock.GetHorninessModifier());
        }

        // Mutation: Would catch if morning bucket start (09) was classified as overnight
        [Fact]
        public void AC5_HorninessModifier_MorningBucket_Hour9_Returns3()
        {
            var clock = new GameClock(MakeTime(9), DefaultModifiers);
            Assert.Equal(3, clock.GetHorninessModifier());
        }

        // Mutation: Would catch if modifier after advance didn't update with new time
        [Fact]
        public void AC5_HorninessModifier_UpdatesAfterAdvance()
        {
            var clock = new GameClock(MakeTime(8), DefaultModifiers); // overnight → 5
            Assert.Equal(5, clock.GetHorninessModifier());

            clock.Advance(TimeSpan.FromHours(15)); // 8 + 15 = 23 → evening → 2
            Assert.Equal(2, clock.GetHorninessModifier());
        }

        // Mutation: Would catch if custom modifiers were ignored (hardcoded values used instead)
        [Fact]
        public void AC5_HorninessModifier_UsesCustomModifiers_NotHardcoded()
        {
            var custom = new HorninessModifiers(morning: 99, afternoon: 88, evening: 77, overnight: 66);
            var clock = new GameClock(MakeTime(10), custom); // morning hour (09-11)
            Assert.Equal(99, clock.GetHorninessModifier());

            var clock2 = new GameClock(MakeTime(14), custom); // afternoon
            Assert.Equal(88, clock2.GetHorninessModifier());

            var clock3 = new GameClock(MakeTime(20), custom); // evening
            Assert.Equal(77, clock3.GetHorninessModifier());

            var clock4 = new GameClock(MakeTime(4), custom); // overnight
            Assert.Equal(66, clock4.GetHorninessModifier());
        }

        // ===== AC6: DailyEnergy system =====

        // Mutation: Would catch if default dailyEnergy was 0 or some other value instead of 10
        [Fact]
        public void AC6_DefaultDailyEnergy_Is10()
        {
            var clock = new GameClock(MakeTime(10), DefaultModifiers);
            Assert.Equal(10, clock.RemainingEnergy);
        }

        // Mutation: Would catch if ConsumeEnergy didn't deduct on success
        [Fact]
        public void AC6_ConsumeEnergy_DeductsOnSuccess()
        {
            var clock = new GameClock(MakeTime(10), DefaultModifiers, dailyEnergy: 15);
            Assert.True(clock.ConsumeEnergy(5));
            Assert.Equal(10, clock.RemainingEnergy);
        }

        // Mutation: Would catch if ConsumeEnergy deducted even when insufficient
        [Fact]
        public void AC6_ConsumeEnergy_NoDeductionOnInsufficient()
        {
            var clock = new GameClock(MakeTime(10), DefaultModifiers, dailyEnergy: 3);
            Assert.False(clock.ConsumeEnergy(5));
            Assert.Equal(3, clock.RemainingEnergy);
        }

        // Mutation: Would catch if ConsumeEnergy used > instead of >= for boundary
        [Fact]
        public void AC6_ConsumeEnergy_ExactlyRemainingSucceeds()
        {
            var clock = new GameClock(MakeTime(10), DefaultModifiers, dailyEnergy: 7);
            Assert.True(clock.ConsumeEnergy(7));
            Assert.Equal(0, clock.RemainingEnergy);
        }

        // Mutation: Would catch if multiple consecutive consumes didn't accumulate
        [Fact]
        public void AC6_ConsumeEnergy_MultipleCalls_Accumulate()
        {
            var clock = new GameClock(MakeTime(10), DefaultModifiers, dailyEnergy: 15);
            Assert.True(clock.ConsumeEnergy(5));
            Assert.Equal(10, clock.RemainingEnergy);
            Assert.True(clock.ConsumeEnergy(10));
            Assert.Equal(0, clock.RemainingEnergy);
            Assert.False(clock.ConsumeEnergy(1));
            Assert.Equal(0, clock.RemainingEnergy);
        }

        // Mutation: Would catch if midnight replenishment set energy to 0 instead of dailyEnergy
        [Fact]
        public void AC6_MidnightCrossing_ReplenishesToDailyEnergy()
        {
            var clock = new GameClock(MakeTime(23), DefaultModifiers, dailyEnergy: 15);
            clock.ConsumeEnergy(15);
            Assert.Equal(0, clock.RemainingEnergy);

            clock.Advance(TimeSpan.FromHours(2));
            Assert.Equal(15, clock.RemainingEnergy);
        }

        // Mutation: Would catch if midnight replenishment used hardcoded 10 instead of constructor dailyEnergy
        [Fact]
        public void AC6_MidnightCrossing_ReplenishesToCustomDailyEnergy()
        {
            var clock = new GameClock(MakeTime(23), DefaultModifiers, dailyEnergy: 20);
            clock.ConsumeEnergy(20);
            clock.Advance(TimeSpan.FromHours(2));
            Assert.Equal(20, clock.RemainingEnergy);
        }

        // Mutation: Would catch if midnight detection was off-by-one (same day advance incorrectly replenishes)
        [Fact]
        public void AC6_SameDayAdvance_DoesNotReplenish()
        {
            var clock = new GameClock(MakeTime(10), DefaultModifiers, dailyEnergy: 10);
            clock.ConsumeEnergy(5);
            clock.Advance(TimeSpan.FromHours(3));
            Assert.Equal(5, clock.RemainingEnergy);
        }

        // Mutation: Would catch if zero dailyEnergy was rejected instead of allowed
        [Fact]
        public void AC6_ZeroDailyEnergy_AllConsumesFail()
        {
            var clock = new GameClock(MakeTime(10), DefaultModifiers, dailyEnergy: 0);
            Assert.Equal(0, clock.RemainingEnergy);
            Assert.False(clock.ConsumeEnergy(1));
        }

        // ===== AC7: Consumers inject IGameClock =====

        // Mutation: Would catch if GameClock couldn't be assigned to IGameClock variable
        [Fact]
        public void AC7_GameClock_UsableAsIGameClock()
        {
            IGameClock clock = new GameClock(MakeTime(10), DefaultModifiers);
            Assert.NotNull(clock);
            Assert.IsAssignableFrom<IGameClock>(clock);
        }

        // ===== AC8: Boundary tests =====

        // Mutation: Would catch if hour 2 boundary was >= 3 instead of >= 2
        [Fact]
        public void AC8_Boundary_Hour2_IsAfterTwoAm_NotLateNight()
        {
            var clock = new GameClock(MakeTime(2), DefaultModifiers);
            Assert.Equal(TimeOfDay.AfterTwoAm, clock.GetTimeOfDay());
        }

        // Mutation: Would catch if hour 1 boundary was off (classified as AfterTwoAm)
        [Fact]
        public void AC8_Boundary_Hour1_IsLateNight_NotAfterTwoAm()
        {
            var clock = new GameClock(MakeTime(1), DefaultModifiers);
            Assert.Equal(TimeOfDay.LateNight, clock.GetTimeOfDay());
        }

        // Mutation: Would catch if AdvanceTo midnight crossing didn't trigger replenish
        [Fact]
        public void AC8_AdvanceTo_CrossingMidnight_ReplenishesEnergy()
        {
            var clock = new GameClock(MakeTime(23), DefaultModifiers, dailyEnergy: 10);
            clock.ConsumeEnergy(8);
            Assert.Equal(2, clock.RemainingEnergy);

            var target = new DateTimeOffset(2024, 1, 16, 1, 0, 0, TimeSpan.Zero);
            clock.AdvanceTo(target);
            Assert.Equal(10, clock.RemainingEnergy);
        }

        // ===== Error Conditions =====

        // Mutation: Would catch if negative dailyEnergy was silently accepted
        [Fact]
        public void Error_Constructor_NegativeEnergy_Throws_ArgumentOutOfRangeException()
        {
            var ex = Assert.Throws<ArgumentOutOfRangeException>(
                () => new GameClock(MakeTime(10), DefaultModifiers, dailyEnergy: -1));
            Assert.NotNull(ex);
        }

        // Mutation: Would catch if null modifiers was silently accepted
        [Fact]
        public void Error_Constructor_NullModifiers_Throws_ArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(
                () => new GameClock(MakeTime(10), null));
        }

        // Mutation: Would catch if Advance(Zero) was silently accepted
        [Fact]
        public void Error_Advance_Zero_Throws_ArgumentOutOfRangeException()
        {
            var clock = new GameClock(MakeTime(10), DefaultModifiers);
            Assert.Throws<ArgumentOutOfRangeException>(
                () => clock.Advance(TimeSpan.Zero));
        }

        // Mutation: Would catch if negative advance was silently accepted
        [Fact]
        public void Error_Advance_Negative_Throws_ArgumentOutOfRangeException()
        {
            var clock = new GameClock(MakeTime(10), DefaultModifiers);
            Assert.Throws<ArgumentOutOfRangeException>(
                () => clock.Advance(TimeSpan.FromMinutes(-30)));
        }

        // Mutation: Would catch if AdvanceTo same time was silently accepted
        [Fact]
        public void Error_AdvanceTo_SameTime_Throws_ArgumentException()
        {
            var start = MakeTime(10);
            var clock = new GameClock(start, DefaultModifiers);
            Assert.Throws<ArgumentException>(() => clock.AdvanceTo(start));
        }

        // Mutation: Would catch if AdvanceTo past time was silently accepted
        [Fact]
        public void Error_AdvanceTo_PastTime_Throws_ArgumentException()
        {
            var clock = new GameClock(MakeTime(14), DefaultModifiers);
            Assert.Throws<ArgumentException>(() => clock.AdvanceTo(MakeTime(10)));
        }

        // Mutation: Would catch if ConsumeEnergy(0) was silently accepted instead of throwing
        [Fact]
        public void Error_ConsumeEnergy_Zero_Throws_ArgumentOutOfRangeException()
        {
            var clock = new GameClock(MakeTime(10), DefaultModifiers);
            Assert.Throws<ArgumentOutOfRangeException>(
                () => clock.ConsumeEnergy(0));
        }

        // Mutation: Would catch if ConsumeEnergy(-1) was silently accepted
        [Fact]
        public void Error_ConsumeEnergy_Negative_Throws_ArgumentOutOfRangeException()
        {
            var clock = new GameClock(MakeTime(10), DefaultModifiers);
            Assert.Throws<ArgumentOutOfRangeException>(
                () => clock.ConsumeEnergy(-5));
        }

        // ===== Edge Cases =====

        // Mutation: Would catch if multiple midnight crossings didn't replenish
        [Fact]
        public void Edge_MultipleMidnightCrossings_StillReplenishes()
        {
            var clock = new GameClock(MakeTime(23), DefaultModifiers, dailyEnergy: 10);
            clock.ConsumeEnergy(10);
            Assert.Equal(0, clock.RemainingEnergy);

            // Advance 50 hours — crosses midnight at least twice
            clock.Advance(TimeSpan.FromHours(50));
            Assert.Equal(10, clock.RemainingEnergy);
        }

        // Mutation: Would catch if Advance didn't actually update Now
        [Fact]
        public void Edge_Advance_UpdatesNow()
        {
            var clock = new GameClock(MakeTime(10), DefaultModifiers);
            clock.Advance(TimeSpan.FromHours(4));
            Assert.Equal(MakeTime(14), clock.Now);
        }

        // Mutation: Would catch if AdvanceTo didn't actually set Now to target
        [Fact]
        public void Edge_AdvanceTo_SetsNowToTarget()
        {
            var clock = new GameClock(MakeTime(10), DefaultModifiers);
            var target = MakeTime(14);
            clock.AdvanceTo(target);
            Assert.Equal(target, clock.Now);
        }

        // Mutation: Would catch if constructor didn't store startTime as Now
        [Fact]
        public void Edge_Constructor_StoresStartTimeAsNow()
        {
            var start = new DateTimeOffset(2024, 6, 15, 14, 30, 45, TimeSpan.FromHours(5));
            var clock = new GameClock(start, DefaultModifiers);
            Assert.Equal(start, clock.Now);
        }

        // Mutation: Would catch if Advance with small amount (minutes) didn't work
        [Fact]
        public void Edge_Advance_SmallAmount_Minutes()
        {
            var clock = new GameClock(MakeTime(10), DefaultModifiers);
            clock.Advance(TimeSpan.FromMinutes(30));
            Assert.Equal(MakeTime(10, 30), clock.Now);
        }

        // Mutation: Would catch if energy replenish happened on same-day AdvanceTo
        [Fact]
        public void Edge_AdvanceTo_SameDay_NoReplenish()
        {
            var clock = new GameClock(MakeTime(8), DefaultModifiers, dailyEnergy: 10);
            clock.ConsumeEnergy(6);
            clock.AdvanceTo(MakeTime(20));
            Assert.Equal(4, clock.RemainingEnergy);
        }

        // Mutation: Would catch if GetTimeOfDay used minutes instead of just hour
        [Fact]
        public void Edge_GetTimeOfDay_MinutesIgnored()
        {
            // 5:59 should still be AfterTwoAm (hour 5), not Morning
            var clock = new GameClock(new DateTimeOffset(2024, 1, 15, 5, 59, 59, TimeSpan.Zero), DefaultModifiers);
            Assert.Equal(TimeOfDay.AfterTwoAm, clock.GetTimeOfDay());
        }

        // Mutation: Would catch if GetTimeOfDay at 11:59 was Afternoon instead of Morning
        [Fact]
        public void Edge_GetTimeOfDay_11_59_StillMorning()
        {
            var clock = new GameClock(new DateTimeOffset(2024, 1, 15, 11, 59, 59, TimeSpan.Zero), DefaultModifiers);
            Assert.Equal(TimeOfDay.Morning, clock.GetTimeOfDay());
        }

        // Mutation: Would catch if consuming after replenish didn't work
        [Fact]
        public void Edge_ConsumeAfterMidnightReplenish()
        {
            var clock = new GameClock(MakeTime(23), DefaultModifiers, dailyEnergy: 10);
            clock.ConsumeEnergy(10);
            clock.Advance(TimeSpan.FromHours(2)); // cross midnight
            Assert.Equal(10, clock.RemainingEnergy);

            // Should be able to consume again after replenish
            Assert.True(clock.ConsumeEnergy(3));
            Assert.Equal(7, clock.RemainingEnergy);
        }
    }
}
