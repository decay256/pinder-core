using System;

namespace Pinder.Core.Conversation
{
    /// <summary>
    /// Tracks the opponent's interest level (0–25) during a conversation.
    /// Starts at 10. Clamped to [Min, Max] on every Apply call.
    /// </summary>
    public sealed class InterestMeter
    {
        public const int Max           = 25;
        public const int Min           = 0;
        public const int StartingValue = 10;

        public int Current { get; private set; }

        public InterestMeter()
        {
            Current = StartingValue;
        }

        /// <summary>Apply a positive or negative delta, clamped to [Min, Max].</summary>
        public void Apply(int delta)
        {
            Current = Math.Max(Min, Math.Min(Max, Current + delta));
        }

        /// <summary>True when interest is at maximum.</summary>
        public bool IsMaxed => Current >= Max;

        /// <summary>True when interest has hit zero.</summary>
        public bool IsZero => Current <= Min;
    }
}
