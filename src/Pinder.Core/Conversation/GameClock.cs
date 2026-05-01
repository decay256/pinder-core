using System;
using Pinder.Core.Interfaces;

namespace Pinder.Core.Conversation
{
    /// <summary>
    /// Configurable time-of-day horniness modifiers for <see cref="GameClock"/>.
    /// </summary>
    public sealed class HorninessModifiers
    {
        /// <summary>Modifier for 09:00–11:59.</summary>
        public int Morning { get; }

        /// <summary>Modifier for 12:00–17:59.</summary>
        public int Afternoon { get; }

        /// <summary>Modifier for 18:00–23:59.</summary>
        public int Evening { get; }

        /// <summary>Modifier for 00:00–08:59.</summary>
        public int Overnight { get; }

        public HorninessModifiers(int morning, int afternoon, int evening, int overnight)
        {
            Morning = morning;
            Afternoon = afternoon;
            Evening = evening;
            Overnight = overnight;
        }
    }

    /// <summary>
    /// Simulated in-game clock that tracks time-of-day and provides horniness modifiers.
    /// Energy mechanics were removed in #786 (deferred indefinitely; refactor blocker for #393).
    /// </summary>
    public sealed class GameClock : IGameClock
    {
        private readonly HorninessModifiers _horninessModifiers;

        /// <summary>Current simulated time.</summary>
        public DateTimeOffset Now { get; private set; }

        /// <summary>
        /// Creates a new GameClock starting at <paramref name="startTime"/>
        /// with the specified horniness modifiers.
        /// </summary>
        /// <param name="startTime">Initial simulated time.</param>
        /// <param name="modifiers">
        /// Time-of-day horniness modifiers. Must not be null.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="modifiers"/> is null.
        /// </exception>
        public GameClock(DateTimeOffset startTime, HorninessModifiers modifiers)
        {
            if (modifiers == null)
                throw new ArgumentNullException(nameof(modifiers));

            Now = startTime;
            _horninessModifiers = modifiers;
        }

        /// <summary>
        /// Advance the clock forward by the given amount.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when <paramref name="amount"/> is zero or negative.
        /// </exception>
        public void Advance(TimeSpan amount)
        {
            if (amount <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(amount), "amount must be positive");

            Now = Now.Add(amount);
        }

        /// <summary>
        /// Advance the clock to the specified target time.
        /// </summary>
        /// <exception cref="ArgumentException">
        /// Thrown when <paramref name="target"/> is less than or equal to <see cref="Now"/>.
        /// </exception>
        public void AdvanceTo(DateTimeOffset target)
        {
            if (target <= Now)
                throw new ArgumentException("target must be after Now", nameof(target));

            Now = target;
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
        /// Uses the configurable <see cref="HorninessModifiers"/> provided at construction.
        /// Buckets: overnight (00:00-08:59), morning (09:00-11:59),
        /// afternoon (12:00-17:59), evening (18:00-23:59).
        /// </summary>
        public int GetHorninessModifier()
        {
            int hour = Now.Hour;
            if (hour >= 9 && hour <= 11) return _horninessModifiers.Morning;
            if (hour >= 12 && hour <= 17) return _horninessModifiers.Afternoon;
            if (hour >= 18 && hour <= 23) return _horninessModifiers.Evening;
            return _horninessModifiers.Overnight; // 00:00-08:59
        }
    }
}
