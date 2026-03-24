namespace Pinder.Core.Conversation
{
    /// <summary>
    /// How a Pinder conversation ended.
    /// </summary>
    public enum GameOutcome
    {
        /// <summary>Interest hit 25 — the date is secured.</summary>
        DateSecured,

        /// <summary>Interest hit 0 — unmatched.</summary>
        Unmatched,

        /// <summary>Ghosted — random chance while in Bored state.</summary>
        Ghosted
    }
}
