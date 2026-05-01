using System;

namespace Pinder.Core.Interfaces
{
    /// <summary>
    /// Time-of-day buckets for the simulated game clock.
    /// Hour boundaries are inclusive-start, exclusive-end.
    /// </summary>
    public enum TimeOfDay
    {
        /// <summary>06:00–11:59</summary>
        Morning,

        /// <summary>12:00–17:59</summary>
        Afternoon,

        /// <summary>18:00–21:59</summary>
        Evening,

        /// <summary>22:00–01:59</summary>
        LateNight,

        /// <summary>02:00–05:59</summary>
        AfterTwoAm
    }

    /// <summary>
    /// Simulated in-game clock. Injectable for testing via a FixedGameClock implementation.
    /// Concrete implementation (GameClock) is built in issue #54; this issue defines the interface only.
    /// </summary>
    public interface IGameClock
    {
        /// <summary>Current simulated time.</summary>
        DateTimeOffset Now { get; }

        /// <summary>Advance clock by the given amount.</summary>
        void Advance(TimeSpan amount);

        /// <summary>
        /// Advance clock to a specific point in time.
        /// </summary>
        /// <exception cref="ArgumentException">Thrown when target is less than or equal to Now.</exception>
        void AdvanceTo(DateTimeOffset target);

        /// <summary>
        /// Returns the current time-of-day bucket based on Now.Hour.
        /// </summary>
        TimeOfDay GetTimeOfDay();

        /// <summary>
        /// Returns the horniness modifier for the current time of day.
        /// Morning=-2, Afternoon=0, Evening=+1, LateNight=+3, AfterTwoAm=+5.
        /// </summary>
        int GetHorninessModifier();
    }
}
