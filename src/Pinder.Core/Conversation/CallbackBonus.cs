namespace Pinder.Core.Conversation
{
    /// <summary>
    /// Pure static utility: computes the hidden callback bonus from turn distance.
    /// §15 callback distance detection: referencing earlier conversation topics
    /// gives a hidden roll bonus based on how far back the topic was introduced.
    /// </summary>
    public static class CallbackBonus
    {
        /// <summary>
        /// Compute the hidden callback bonus given the current turn number
        /// and the turn the referenced topic was introduced.
        /// Returns 0 if no bonus applies (distance &lt; 2).
        /// </summary>
        /// <param name="currentTurn">The current turn number (0-based).</param>
        /// <param name="callbackTurnNumber">The turn when the topic was introduced (0-based).</param>
        /// <returns>0, 1, 2, or 3.</returns>
        public static int Compute(int currentTurn, int callbackTurnNumber)
        {
            int distance = currentTurn - callbackTurnNumber;

            // Too recent or same turn — no bonus
            if (distance < 2)
                return 0;

            // Opener reference always wins when distance >= 2
            if (callbackTurnNumber == 0)
                return 3;

            // Long-distance callback
            if (distance >= 4)
                return 2;

            // Mid-distance callback (distance 2 or 3, non-opener)
            return 1;
        }
    }
}
