using System;
using Pinder.Core.Conversation;
using Pinder.Core.Interfaces;
using Xunit;

namespace Pinder.Core.Tests
{
    /// <summary>
    /// Spec-driven tests for GameClock (issue #54).
    /// Based on docs/specs/issue-54-spec.md acceptance criteria.
    /// </summary>
    public class GameClockSpecTests
    {
        private static DateTimeOffset MakeTime(int hour, int minute = 0) =>
            new DateTimeOffset(2024, 1, 15, hour, minute, 0, TimeSpan.Zero);

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
            var clock = new GameClock(MakeTime(hour));
            Assert.Equal(expected, clock.GetTimeOfDay());
        }

        // Mutation: Would catch if exactly 5 enum values was changed (e.g. added/removed one)
        [Fact]
        public void AC4_TimeOfDay_ExactlyFiveValues()
        {
            Assert.Equal(5, Enum.GetValues(typeof(TimeOfDay)).Length);
        }

        // ===== AC5: GetHorninessModifier returns correct value =====

        // Mutation: Would catch if Morning returned 0 instead of -2
        [Fact]
        public void AC5_HorninessModifier_Morning_Negative2()
        {
            var clock = new GameClock(MakeTime(6));
            Assert.Equal(-2, clock.GetHorninessModifier());
        }

        // Mutation: Would catch if Afternoon returned -2 instead of 0
        [Fact]
        public void AC5_HorninessModifier_Afternoon_Zero()
        {
            var clock = new GameClock(MakeTime(12));
            Assert.Equal(0, clock.GetHorninessModifier());
        }

        // Mutation: Would catch if Evening returned 0 instead of +1
        [Fact]
        public void AC5_HorninessModifier_Evening_Plus1()
        {
            var clock = new GameClock(MakeTime(18));
            Assert.Equal(1, clock.GetHorninessModifier());
        }

        // Mutation: Would catch if LateNight returned +5 instead of +3
        [Fact]
        public void AC5_HorninessModifier_LateNight_Plus3()
        {
            var clock = new GameClock(MakeTime(22));
            Assert.Equal(3, clock.GetHorninessModifier());
        }

        // Mutation: Would catch if AfterTwoAm returned +3 instead of +5
        [Fact]
        public void AC5_HorninessModifier_AfterTwoAm_Plus5()
        {
            var clock = new GameClock(MakeTime(3));
            Assert.Equal(5, clock.GetHorninessModifier());
        }

        // Mutation: Would catch if LateNight hour 0 was misclassified as AfterTwoAm
        [Fact]
        public void AC5_HorninessModifier_Hour0_IsLateNight_Returns3()
        {
            var clock = new GameClock(MakeTime(0));
            Assert.Equal(3, clock.GetHorninessModifier());
        }

        // Mutation: Would catch if modifier after advance didn't update with new time
        [Fact]
        public void AC5_HorninessModifier_UpdatesAfterAdvance()
        {
            var clock = new GameClock(MakeTime(8)); // Morning → -2
            Assert.Equal(-2, clock.GetHorninessModifier());

            clock.Advance(TimeSpan.FromHours(15)); // 8 + 15 = 23 → LateNight → +3
            Assert.Equal(3, clock.GetHorninessModifier());
        }

        // ===== AC6: DailyEnergy system =====

        // Mutation: Would catch if default dailyEnergy was 0 or some other value instead of 10
        [Fact]
        public void AC6_DefaultDailyEnergy_Is10()
        {
            var clock = new GameClock(MakeTime(10));
            Assert.Equal(10, clock.RemainingEnergy);
        }

        // Mutation: Would catch if ConsumeEnergy didn't deduct on success
        [Fact]
        public void AC6_ConsumeEnergy_DeductsOnSuccess()
        {
            var clock = new GameClock(MakeTime(10), dailyEnergy: 15);
            Assert.True(clock.ConsumeEnergy(5));
            Assert.Equal(10, clock.RemainingEnergy);
        }

        // Mutation: Would catch if ConsumeEnergy deducted even when insufficient
        [Fact]
        public void AC6_ConsumeEnergy_NoDeductionOnInsufficient()
        {
            var clock = new GameClock(MakeTime(10), dailyEnergy: 3);
            Assert.False(clock.ConsumeEnergy(5));
            Assert.Equal(3, clock.RemainingEnergy);
        }

        // Mutation: Would catch if ConsumeEnergy used > instead of >= for boundary
        [Fact]
        public void AC6_ConsumeEnergy_ExactlyRemainingSucceeds()
        {
            var clock = new GameClock(MakeTime(10), dailyEnergy: 7);
            Assert.True(clock.ConsumeEnergy(7));
            Assert.Equal(0, clock.RemainingEnergy);
        }

        // Mutation: Would catch if multiple consecutive consumes didn't accumulate
        [Fact]
        public void AC6_ConsumeEnergy_MultipleCalls_Accumulate()
        {
            var clock = new GameClock(MakeTime(10), dailyEnergy: 15);
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
            var clock = new GameClock(MakeTime(23), dailyEnergy: 15);
            clock.ConsumeEnergy(15);
            Assert.Equal(0, clock.RemainingEnergy);

            clock.Advance(TimeSpan.FromHours(2));
            Assert.Equal(15, clock.RemainingEnergy);
        }

        // Mutation: Would catch if midnight replenishment used hardcoded 10 instead of constructor dailyEnergy
        [Fact]
        public void AC6_MidnightCrossing_ReplenishesToCustomDailyEnergy()
        {
            var clock = new GameClock(MakeTime(23), dailyEnergy: 20);
            clock.ConsumeEnergy(20);
            clock.Advance(TimeSpan.FromHours(2));
            Assert.Equal(20, clock.RemainingEnergy);
        }

        // Mutation: Would catch if midnight detection was off-by-one (same day advance incorrectly replenishes)
        [Fact]
        public void AC6_SameDayAdvance_DoesNotReplenish()
        {
            var clock = new GameClock(MakeTime(10), dailyEnergy: 10);
            clock.ConsumeEnergy(5);
            clock.Advance(TimeSpan.FromHours(3));
            Assert.Equal(5, clock.RemainingEnergy);
        }

        // Mutation: Would catch if zero dailyEnergy was rejected instead of allowed
        [Fact]
        public void AC6_ZeroDailyEnergy_AllConsumesFail()
        {
            var clock = new GameClock(MakeTime(10), dailyEnergy: 0);
            Assert.Equal(0, clock.RemainingEnergy);
            Assert.False(clock.ConsumeEnergy(1));
        }

        // ===== AC7: Consumers inject IGameClock =====

        // Mutation: Would catch if GameClock couldn't be assigned to IGameClock variable
        [Fact]
        public void AC7_GameClock_UsableAsIGameClock()
        {
            IGameClock clock = new GameClock(MakeTime(10));
            Assert.NotNull(clock);
            Assert.IsAssignableFrom<IGameClock>(clock);
        }

        // ===== AC8: Boundary tests =====

        // Mutation: Would catch if hour 2 boundary was >= 3 instead of >= 2
        [Fact]
        public void AC8_Boundary_Hour2_IsAfterTwoAm_NotLateNight()
        {
            var clock = new GameClock(MakeTime(2));
            Assert.Equal(TimeOfDay.AfterTwoAm, clock.GetTimeOfDay());
        }

        // Mutation: Would catch if hour 1 boundary was off (classified as AfterTwoAm)
        [Fact]
        public void AC8_Boundary_Hour1_IsLateNight_NotAfterTwoAm()
        {
            var clock = new GameClock(MakeTime(1));
            Assert.Equal(TimeOfDay.LateNight, clock.GetTimeOfDay());
        }

        // Mutation: Would catch if AdvanceTo midnight crossing didn't trigger replenish
        [Fact]
        public void AC8_AdvanceTo_CrossingMidnight_ReplenishesEnergy()
        {
            var clock = new GameClock(MakeTime(23), dailyEnergy: 10);
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
                () => new GameClock(MakeTime(10), dailyEnergy: -1));
            Assert.NotNull(ex);
        }

        // Mutation: Would catch if Advance(Zero) was silently accepted
        [Fact]
        public void Error_Advance_Zero_Throws_ArgumentOutOfRangeException()
        {
            var clock = new GameClock(MakeTime(10));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => clock.Advance(TimeSpan.Zero));
        }

        // Mutation: Would catch if negative advance was silently accepted
        [Fact]
        public void Error_Advance_Negative_Throws_ArgumentOutOfRangeException()
        {
            var clock = new GameClock(MakeTime(10));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => clock.Advance(TimeSpan.FromMinutes(-30)));
        }

        // Mutation: Would catch if AdvanceTo same time was silently accepted
        [Fact]
        public void Error_AdvanceTo_SameTime_Throws_ArgumentException()
        {
            var start = MakeTime(10);
            var clock = new GameClock(start);
            Assert.Throws<ArgumentException>(() => clock.AdvanceTo(start));
        }

        // Mutation: Would catch if AdvanceTo past time was silently accepted
        [Fact]
        public void Error_AdvanceTo_PastTime_Throws_ArgumentException()
        {
            var clock = new GameClock(MakeTime(14));
            Assert.Throws<ArgumentException>(() => clock.AdvanceTo(MakeTime(10)));
        }

        // Mutation: Would catch if ConsumeEnergy(0) was silently accepted instead of throwing
        [Fact]
        public void Error_ConsumeEnergy_Zero_Throws_ArgumentOutOfRangeException()
        {
            var clock = new GameClock(MakeTime(10));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => clock.ConsumeEnergy(0));
        }

        // Mutation: Would catch if ConsumeEnergy(-1) was silently accepted
        [Fact]
        public void Error_ConsumeEnergy_Negative_Throws_ArgumentOutOfRangeException()
        {
            var clock = new GameClock(MakeTime(10));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => clock.ConsumeEnergy(-5));
        }

        // ===== Edge Cases =====

        // Mutation: Would catch if multiple midnight crossings didn't replenish
        [Fact]
        public void Edge_MultipleMidnightCrossings_StillReplenishes()
        {
            var clock = new GameClock(MakeTime(23), dailyEnergy: 10);
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
            var clock = new GameClock(MakeTime(10));
            clock.Advance(TimeSpan.FromHours(4));
            Assert.Equal(MakeTime(14), clock.Now);
        }

        // Mutation: Would catch if AdvanceTo didn't actually set Now to target
        [Fact]
        public void Edge_AdvanceTo_SetsNowToTarget()
        {
            var clock = new GameClock(MakeTime(10));
            var target = MakeTime(14);
            clock.AdvanceTo(target);
            Assert.Equal(target, clock.Now);
        }

        // Mutation: Would catch if constructor didn't store startTime as Now
        [Fact]
        public void Edge_Constructor_StoresStartTimeAsNow()
        {
            var start = new DateTimeOffset(2024, 6, 15, 14, 30, 45, TimeSpan.FromHours(5));
            var clock = new GameClock(start);
            Assert.Equal(start, clock.Now);
        }

        // Mutation: Would catch if Advance with small amount (minutes) didn't work
        [Fact]
        public void Edge_Advance_SmallAmount_Minutes()
        {
            var clock = new GameClock(MakeTime(10));
            clock.Advance(TimeSpan.FromMinutes(30));
            Assert.Equal(MakeTime(10, 30), clock.Now);
        }

        // Mutation: Would catch if energy replenish happened on same-day AdvanceTo
        [Fact]
        public void Edge_AdvanceTo_SameDay_NoReplenish()
        {
            var clock = new GameClock(MakeTime(8), dailyEnergy: 10);
            clock.ConsumeEnergy(6);
            clock.AdvanceTo(MakeTime(20));
            Assert.Equal(4, clock.RemainingEnergy);
        }

        // Mutation: Would catch if GetTimeOfDay used minutes instead of just hour
        [Fact]
        public void Edge_GetTimeOfDay_MinutesIgnored()
        {
            // 5:59 should still be AfterTwoAm (hour 5), not Morning
            var clock = new GameClock(new DateTimeOffset(2024, 1, 15, 5, 59, 59, TimeSpan.Zero));
            Assert.Equal(TimeOfDay.AfterTwoAm, clock.GetTimeOfDay());
        }

        // Mutation: Would catch if GetTimeOfDay at 11:59 was Afternoon instead of Morning
        [Fact]
        public void Edge_GetTimeOfDay_11_59_StillMorning()
        {
            var clock = new GameClock(new DateTimeOffset(2024, 1, 15, 11, 59, 59, TimeSpan.Zero));
            Assert.Equal(TimeOfDay.Morning, clock.GetTimeOfDay());
        }

        // Mutation: Would catch if consuming after replenish didn't work
        [Fact]
        public void Edge_ConsumeAfterMidnightReplenish()
        {
            var clock = new GameClock(MakeTime(23), dailyEnergy: 10);
            clock.ConsumeEnergy(10);
            clock.Advance(TimeSpan.FromHours(2)); // cross midnight
            Assert.Equal(10, clock.RemainingEnergy);

            // Should be able to consume again after replenish
            Assert.True(clock.ConsumeEnergy(3));
            Assert.Equal(7, clock.RemainingEnergy);
        }
    }
}
