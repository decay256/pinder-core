namespace Pinder.Core.Conversation
{
    /// <summary>
    /// Interest states derived from rules v3.4 §6.
    /// Each state maps to a specific interest-value range on the 0–25 meter.
    /// </summary>
    public enum InterestState
    {
        /// <summary>Interest = 0. Game over.</summary>
        Unmatched,

        /// <summary>Interest 1–4. Player rolls with disadvantage.</summary>
        Bored,

        /// <summary>Interest 5–15. No modifier.</summary>
        Interested,

        /// <summary>Interest 16–20. Player rolls with advantage.</summary>
        VeryIntoIt,

        /// <summary>Interest 21–24. Advantage + next good roll closes.</summary>
        AlmostThere,

        /// <summary>Interest = 25. XP payout.</summary>
        DateSecured
    }
}
