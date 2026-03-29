using System;
using Pinder.Core.Interfaces;

namespace Pinder.Core.Conversation
{
    /// <summary>
    /// Simulated in-game clock that tracks time-of-day, provides horniness modifiers,
    /// and manages a daily energy budget. Energy replenishes automatically when the
    /// clock crosses midnight via <see cref="Advance"/> or <see cref="AdvanceTo"/>.
    /// </summary>
    public sealed class GameClock : IGameClock
    {
        private readonly int _dailyEnergy;

        /// <summary>Current simulated time.</summary>
        public DateTimeOffset Now { get; private set; }

        /// <summary>Remaining energy for the current in-game day.</summary>
        public int RemainingEnergy { get; private set; }

        /// <summary>
        /// Creates a new GameClock starting at <paramref name="startTime"/>
        /// with the specified daily energy budget.
        /// </summary>
        /// <param name="startTime">Initial simulated time.</param>
        /// <param name="dailyEnergy">
        /// Energy budget per day. Default: 10. Must be &gt;= 0.
        /// </param>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when <paramref name="dailyEnergy"/> is negative.
        /// </exception>
        public GameClock(DateTimeOffset startTime, int dailyEnergy = 10)
        {
            if (dailyEnergy < 0)
                throw new ArgumentOutOfRangeException(nameof(dailyEnergy), "dailyEnergy must be non-negative");

            Now = startTime;
            _dailyEnergy = dailyEnergy;
            RemainingEnergy = dailyEnergy;
        }

        /// <summary>
        /// Advance the clock forward by the given amount.
        /// If the advance crosses midnight, energy is replenished to dailyEnergy.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when <paramref name="amount"/> is zero or negative.
        /// </exception>
        public void Advance(TimeSpan amount)
        {
            if (amount <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(amount), "amount must be positive");

            var oldDate = Now.Date;
            Now = Now.Add(amount);

            if (Now.Date != oldDate)
                RemainingEnergy = _dailyEnergy;
        }

        /// <summary>
        /// Advance the clock to the specified target time.
        /// If the advance crosses midnight, energy is replenished to dailyEnergy.
        /// </summary>
        /// <exception cref="ArgumentException">
        /// Thrown when <paramref name="target"/> is less than or equal to <see cref="Now"/>.
        /// </exception>
        public void AdvanceTo(DateTimeOffset target)
        {
            if (target <= Now)
                throw new ArgumentException("target must be after Now", nameof(target));

            var oldDate = Now.Date;
            Now = target;

            if (Now.Date != oldDate)
                RemainingEnergy = _dailyEnergy;
        }

        /// <summary>
        /// Returns the current time-of-day bucket based on <see cref="Now"/>.Hour.
        /// </summary>
        public TimeOfDay GetTimeOfDay()
        {
            int hour = Now.Hour;
            if (hour >= 6 && hour <= 11) return TimeOfDay.Morning;
            if (hour >= 12 && hour <= 17) return TimeOfDay.Afternoon;
            if (hour >= 18 && hour <= 21) return TimeOfDay.Evening;
            if (hour >= 22 || hour <= 1) return TimeOfDay.LateNight;
            return TimeOfDay.AfterTwoAm; // 2–5
        }

        /// <summary>
        /// Returns the horniness modifier for the current time of day.
        /// Morning=-2, Afternoon=0, Evening=+1, LateNight=+3, AfterTwoAm=+5.
        /// </summary>
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

        /// <summary>
        /// Attempt to consume the given amount of energy.
        /// Returns true and deducts if sufficient energy remains.
        /// Returns false without deducting if insufficient.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when <paramref name="amount"/> is zero or negative.
        /// </exception>
        public bool ConsumeEnergy(int amount)
        {
            if (amount <= 0)
                throw new ArgumentOutOfRangeException(nameof(amount), "amount must be positive");

            if (amount > RemainingEnergy)
                return false;

            RemainingEnergy -= amount;
            return true;
        }
    }
}
