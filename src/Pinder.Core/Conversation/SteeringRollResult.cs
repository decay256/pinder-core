namespace Pinder.Core.Conversation
{
    /// <summary>
    /// Result of the steering roll that determines whether the player character
    /// appends a date-steering question to their delivered message.
    /// </summary>
    public sealed class SteeringRollResult
    {
        /// <summary>Whether a steering roll was attempted this turn.</summary>
        public bool SteeringAttempted { get; }

        /// <summary>Whether the steering roll succeeded (question appended).</summary>
        public bool SteeringSucceeded { get; }

        /// <summary>Raw d20 roll value.</summary>
        public int SteeringRoll { get; }

        /// <summary>Steering modifier: average of (CHARM + WIT + SA) effective modifiers.</summary>
        public int SteeringMod { get; }

        /// <summary>Steering DC: 16 + average of opponent's (SA + RIZZ + HONESTY) effective modifiers.</summary>
        public int SteeringDC { get; }

        /// <summary>The steering question text, or null if the roll failed.</summary>
        public string SteeringQuestion { get; }

        public SteeringRollResult(
            bool steeringAttempted,
            bool steeringSucceeded,
            int steeringRoll,
            int steeringMod,
            int steeringDC,
            string steeringQuestion)
        {
            SteeringAttempted = steeringAttempted;
            SteeringSucceeded = steeringSucceeded;
            SteeringRoll = steeringRoll;
            SteeringMod = steeringMod;
            SteeringDC = steeringDC;
            SteeringQuestion = steeringQuestion;
        }

        /// <summary>A no-op result when steering was not attempted.</summary>
        public static SteeringRollResult NotAttempted { get; } = new SteeringRollResult(false, false, 0, 0, 0, null);
    }
}
