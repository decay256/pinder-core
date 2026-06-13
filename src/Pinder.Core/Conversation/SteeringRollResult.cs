namespace Pinder.Core.Conversation
{
    using System.Collections.Generic;

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

        /// <summary>Steering DC: 16 + average of datee's (SA + RIZZ + HONESTY) effective modifiers.</summary>
        public int SteeringDC { get; }

        /// <summary>The steering question text, or null if the roll failed.</summary>
        public string SteeringQuestion { get; }

        /// <summary>Stat names contributing to the steering modifier.</summary>
        public IReadOnlyList<string> AttackerGroup { get; }

        /// <summary>Stat names contributing to the steering DC.</summary>
        public IReadOnlyList<string> DefenderGroup { get; }

        /// <summary>The base DC before the datee's stat average is added.</summary>
        public int DcBase { get; }

        /// <summary>
        /// Canonical check result from <see cref="RollEngine.ResolveCheck"/>.

        /// Captures the raw roll before LLM success/failure affects <see cref="SteeringSucceeded"/>.
        /// Phase 1 (additive): attached alongside existing bespoke fields.
        /// Null only for the <see cref="NotAttempted"/> sentinel.
        /// </summary>
        public Pinder.Core.Rolls.RollCheckResult? Check { get; }

        public SteeringRollResult(
            bool steeringAttempted,
            bool steeringSucceeded,
            int steeringRoll,
            int steeringMod,
            int steeringDC,
            string steeringQuestion,
            Pinder.Core.Rolls.RollCheckResult? check = null,
            IReadOnlyList<string>? attackerGroup = null,
            IReadOnlyList<string>? defenderGroup = null,
            int dcBase = 0)
        {
            SteeringAttempted = steeringAttempted;
            SteeringSucceeded = steeringSucceeded;
            SteeringRoll = steeringRoll;
            SteeringMod = steeringMod;
            SteeringDC = steeringDC;
            SteeringQuestion = steeringQuestion;
            Check = check;
            AttackerGroup = attackerGroup ?? new List<string>().AsReadOnly();
            DefenderGroup = defenderGroup ?? new List<string>().AsReadOnly();
            DcBase = dcBase;
        }

        /// <summary>A no-op result when steering was not attempted.</summary>
        public static SteeringRollResult NotAttempted { get; } = new SteeringRollResult(
            steeringAttempted: false,
            steeringSucceeded: false,
            steeringRoll: 0,
            steeringMod: 0,
            steeringDC: 0,
            steeringQuestion: null,
            check: null,
            attackerGroup: null,
            defenderGroup: null,
            dcBase: 0);
    }
}
