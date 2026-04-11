using System;
using Pinder.Core.Interfaces;
using Pinder.Core.Stats;

namespace Pinder.Core.Conversation
{
    /// <summary>
    /// Optional configuration carrier for GameSession. All properties are nullable —
    /// null means "use the default behavior".
    /// </summary>
    public sealed class GameSessionConfig
    {
        /// <summary>Simulated game clock for time-based mechanics.</summary>
        public IGameClock? Clock { get; }

        /// <summary>Mutable shadow tracker for the player character.</summary>
        public SessionShadowTracker? PlayerShadows { get; }

        /// <summary>Mutable shadow tracker for the opponent character.</summary>
        public SessionShadowTracker? OpponentShadows { get; }

        /// <summary>Override the default starting interest value (normally 10).</summary>
        public int? StartingInterest { get; }

        /// <summary>Previous conversation opener for callback bonus calculation (per #162 resolution).</summary>
        public string? PreviousOpener { get; }

        /// <summary>
        /// Optional rule resolver for data-driven game constants.
        /// When non-null, GameSession uses this for §5/§6/§7/§15 lookups.
        /// When null or when a lookup returns null, hardcoded fallback is used.
        /// </summary>
        public IRuleResolver? Rules { get; }

        /// <summary>
        /// Global DC adjustment applied to every roll. Positive = harder (DC raised),
        /// negative = easier (DC lowered). Does not affect Nat 1 / Nat 20 detection.
        /// </summary>
        public int GlobalDcBias { get; }

        /// <summary>
        /// Optional RNG for the steering roll. When null, a new System.Random is used.
        /// Inject a seeded Random for deterministic test scenarios.
        /// </summary>
        public Random? SteeringRng { get; }

        public GameSessionConfig(
            IGameClock? clock = null,
            SessionShadowTracker? playerShadows = null,
            SessionShadowTracker? opponentShadows = null,
            int? startingInterest = null,
            string? previousOpener = null,
            IRuleResolver? rules = null,
            int globalDcBias = 0,
            Random? steeringRng = null)
        {
            Clock = clock;
            PlayerShadows = playerShadows;
            OpponentShadows = opponentShadows;
            StartingInterest = startingInterest;
            PreviousOpener = previousOpener;
            Rules = rules;
            GlobalDcBias = globalDcBias;
            SteeringRng = steeringRng;
        }
    }
}
