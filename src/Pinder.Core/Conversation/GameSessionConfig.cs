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

        public GameSessionConfig(
            IGameClock? clock = null,
            SessionShadowTracker? playerShadows = null,
            SessionShadowTracker? opponentShadows = null,
            int? startingInterest = null,
            string? previousOpener = null)
        {
            Clock = clock;
            PlayerShadows = playerShadows;
            OpponentShadows = opponentShadows;
            StartingInterest = startingInterest;
            PreviousOpener = previousOpener;
        }
    }
}
