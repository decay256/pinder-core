using System;
using System.Collections.Generic;
using Pinder.Core.Conversation;
using Pinder.Core.Stats;

namespace Pinder.SessionRunner
{
    /// <summary>
    /// Additional context for player agent decision-making, beyond what TurnStart provides.
    /// Carries stat blocks, shadow values, and session state needed for scoring.
    /// </summary>
    public sealed class PlayerAgentContext
    {
        /// <summary>The player character's stat block (immutable).</summary>
        public StatBlock PlayerStats { get; }

        /// <summary>The opponent character's stat block (immutable).</summary>
        public StatBlock OpponentStats { get; }

        /// <summary>Current interest meter value (0-25).</summary>
        public int CurrentInterest { get; }

        /// <summary>Current interest state (derived from CurrentInterest).</summary>
        public InterestState InterestState { get; }

        /// <summary>Number of consecutive successful rolls (0 = no streak).</summary>
        public int MomentumStreak { get; }

        /// <summary>Names of currently active traps.</summary>
        public string[] ActiveTrapNames { get; }

        /// <summary>Current session horniness value (from SessionShadowTracker, 0 if unavailable).</summary>
        public int SessionHorniness { get; }

        /// <summary>Current shadow stat values (from SessionShadowTracker). Null if shadow tracking disabled.</summary>
        public Dictionary<ShadowStatType, int>? ShadowValues { get; }

        /// <summary>Current turn number (from GameStateSnapshot.TurnNumber).</summary>
        public int TurnNumber { get; }

        /// <summary>Player character's flat level bonus applied to all rolls.</summary>
        public int PlayerLevelBonus { get; }

        /// <summary>Stat used on the previous turn. Null on first turn.</summary>
        public StatType? LastStatUsed { get; }

        /// <summary>Stat used two turns ago. Null on first or second turn.</summary>
        public StatType? SecondLastStatUsed { get; }

        /// <summary>Whether Honesty was available as an option last turn. False on first turn or unknown.</summary>
        public bool HonestyAvailableLastTurn { get; }

        /// <summary>The player character's assembled system prompt (personality, texting style). Empty if not available.</summary>
        public string PlayerSystemPrompt { get; }

        /// <summary>The player character's display name.</summary>
        public string PlayerName { get; }

        /// <summary>The opponent character's display name.</summary>
        public string OpponentName { get; }

        /// <summary>Recent conversation history as (sender, text) pairs. Null or empty on first turn.</summary>
        public IReadOnlyList<(string Sender, string Text)> RecentHistory { get; }

        public PlayerAgentContext(
            StatBlock playerStats,
            StatBlock opponentStats,
            int currentInterest,
            InterestState interestState,
            int momentumStreak,
            string[] activeTrapNames,
            int sessionHorniness,
            Dictionary<ShadowStatType, int>? shadowValues,
            int turnNumber,
            StatType? lastStatUsed = null,
            StatType? secondLastStatUsed = null,
            bool honestyAvailableLastTurn = false,
            string playerSystemPrompt = "",
            string playerName = "",
            string opponentName = "",
            IReadOnlyList<(string Sender, string Text)> recentHistory = null,
            int playerLevelBonus = 0)
        {
            PlayerStats = playerStats ?? throw new ArgumentNullException(nameof(playerStats));
            OpponentStats = opponentStats ?? throw new ArgumentNullException(nameof(opponentStats));
            ActiveTrapNames = activeTrapNames ?? throw new ArgumentNullException(nameof(activeTrapNames));
            CurrentInterest = currentInterest;
            InterestState = interestState;
            MomentumStreak = momentumStreak;
            SessionHorniness = sessionHorniness;
            ShadowValues = shadowValues;
            TurnNumber = turnNumber;
            LastStatUsed = lastStatUsed;
            SecondLastStatUsed = secondLastStatUsed;
            HonestyAvailableLastTurn = honestyAvailableLastTurn;
            PlayerSystemPrompt = playerSystemPrompt ?? "";
            PlayerName = playerName ?? "";
            OpponentName = opponentName ?? "";
            RecentHistory = recentHistory;
            PlayerLevelBonus = playerLevelBonus;
        }
    }
}
