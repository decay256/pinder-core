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

        /// <summary>
        /// Returns the current interest state based on rules v3.4 §6 boundaries:
        /// 0 = Unmatched, 1–4 = Bored, 5–15 = Interested,
        /// 16–20 = VeryIntoIt, 21–24 = AlmostThere, 25 = DateSecured.
        /// </summary>
        public InterestState GetState()
        {
            if (Current <= 0)  return InterestState.Unmatched;
            if (Current <= 4)  return InterestState.Bored;
            if (Current <= 15) return InterestState.Interested;
            if (Current <= 20) return InterestState.VeryIntoIt;
            if (Current <= 24) return InterestState.AlmostThere;
            return InterestState.DateSecured;
        }

        /// <summary>
        /// True when the current interest state grants advantage (VeryIntoIt or AlmostThere).
        /// Rules v3.4 §6.
        /// </summary>
        public bool GrantsAdvantage
        {
            get
            {
                var state = GetState();
                return state == InterestState.VeryIntoIt || state == InterestState.AlmostThere;
            }
        }

        /// <summary>
        /// True when the current interest state grants disadvantage (Bored).
        /// Rules v3.4 §6.
        /// </summary>
        public bool GrantsDisadvantage => GetState() == InterestState.Bored;
    }
}
