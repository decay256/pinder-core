namespace Pinder.Core.Conversation
{
    /// <summary>
    /// Interest states derived from InterestMeter value (Rules v3.4 §6).
    /// </summary>
    public enum InterestState
    {
        /// <summary>Value = 0. Game over.</summary>
        Unmatched,

        /// <summary>Value 1–4. Player rolls with disadvantage.</summary>
        Bored,

        /// <summary>Value 5–15. No modifier.</summary>
        Interested,

        /// <summary>Value 16–20. Player rolls with advantage.</summary>
        VeryIntoIt,

        /// <summary>Value 21–24. Advantage + next good roll closes.</summary>
        AlmostThere,

        /// <summary>Value = 25. Date secured, XP payout.</summary>
        DateSecured,
    }
}
