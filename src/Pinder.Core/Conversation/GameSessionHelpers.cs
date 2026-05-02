using System.Collections.Generic;
using System.Linq;
using Pinder.Core.Characters;
using Pinder.Core.Progression;
using Pinder.Core.Stats;
using Pinder.Core.Traps;

namespace Pinder.Core.Conversation
{
    /// <summary>
    /// Static utility methods extracted from GameSession.
    /// Provides snapshot creation, profile formatting, and trap helpers.
    /// </summary>
    internal static class GameSessionHelpers
    {
        /// <summary>
        /// Builds the visible profile string passed to the player's options context.
        /// Includes display name, bio, and equipped item display names (appearance).
        /// This is what the player "sees" on the opponent's dating profile.
        /// </summary>
        public static string BuildOpponentVisibleProfile(CharacterProfile opponent)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append(opponent.DisplayName);
            if (!string.IsNullOrWhiteSpace(opponent.Bio))
                sb.Append(": \"").Append(opponent.Bio).Append('"');
            if (opponent.EquippedItemDisplayNames != null && opponent.EquippedItemDisplayNames.Count > 0)
                sb.Append(" | Wearing: ").Append(string.Join(", ", opponent.EquippedItemDisplayNames));
            return sb.ToString();
        }

        /// <summary>
        /// Formats a trap definition's penalty for display.
        /// </summary>
        public static string FormatTrapPenalty(TrapDefinition def)
        {
            switch (def.Effect)
            {
                case TrapEffect.StatPenalty:
                    return $"stat penalty -{def.EffectValue}";
                case TrapEffect.Disadvantage:
                    return "roll at disadvantage";
                case TrapEffect.OpponentDCIncrease:
                    return $"opponent DC +{def.EffectValue}";
                default:
                    return def.Effect.ToString();
            }
        }

        /// <summary>
        /// Gets the last opponent message from the conversation history.
        /// Returns empty string if no opponent messages found.
        /// </summary>
        public static string GetLastOpponentMessage(
            IReadOnlyList<(string Sender, string Text)> history,
            string opponentName)
        {
            for (int i = history.Count - 1; i >= 0; i--)
            {
                if (history[i].Sender == opponentName)
                    return history[i].Text;
            }
            return string.Empty;
        }

        /// <summary>
        /// Collects the IDs of all currently active traps.
        /// </summary>
        public static List<string> GetActiveTrapNames(TrapState traps)
        {
            return traps.AllActive
                .Select(t => t.Definition.Id)
                .ToList();
        }

        /// <summary>
        /// Collects the LLM instruction text from all currently active traps.
        /// Returns null if no traps are active (avoids empty array allocation).
        /// </summary>
        public static string[]? GetActiveTrapInstructions(TrapState traps)
        {
            var instructions = traps.AllActive
                .Select(t => t.Definition.LlmInstruction)
                .Where(s => !string.IsNullOrEmpty(s))
                .ToArray();
            return instructions.Length > 0 ? instructions : null;
        }

        /// <summary>
        /// Creates a game state snapshot from current state.
        /// </summary>
        public static GameStateSnapshot CreateSnapshot(
            InterestMeter interest,
            InterestState state,
            int momentumStreak,
            TrapState traps,
            int turnNumber,
            bool tripleBonusActive,
            System.Collections.Generic.IReadOnlyList<ConversationMessage> opponentHistory = null)
        {
            var trapNames = traps.AllActive
                .Select(t => t.Definition.Id)
                .ToArray();

            var trapDetails = traps.AllActive
                .Select(t => new TrapDetail(
                    name: t.Definition.Id,
                    stat: t.Definition.Stat.ToString().ToUpperInvariant(),
                    turnsRemaining: t.TurnsRemaining,
                    penaltyDescription: FormatTrapPenalty(t.Definition)))
                .ToArray();

            // Snapshot a defensive copy of the opponent history so callers that
            // hold the snapshot aren't observing later mutations.
            ConversationMessage[] historySnapshot = opponentHistory == null
                ? System.Array.Empty<ConversationMessage>()
                : opponentHistory.ToArray();

            return new GameStateSnapshot(
                interest: interest.Current,
                state: state,
                momentumStreak: momentumStreak,
                activeTrapNames: trapNames,
                turnNumber: turnNumber,
                tripleBonusActive: tripleBonusActive,
                activeTrapDetails: trapDetails,
                opponentHistory: historySnapshot);
        }

        /// <summary>
        /// Determines whether the chosen option has the highest (or tied-for-highest)
        /// success probability among all available options.
        /// </summary>
        public static bool IsHighestProbabilityOption(
            DialogueOption chosen,
            DialogueOption[] options,
            CharacterProfile player,
            CharacterProfile opponent)
        {
            int levelBonus = LevelTable.GetBonus(player.Level);

            int chosenMargin = player.Stats.GetEffective(chosen.Stat) + levelBonus
                               - opponent.Stats.GetDefenceDC(chosen.Stat);

            for (int i = 0; i < options.Length; i++)
            {
                int margin = player.Stats.GetEffective(options[i].Stat) + levelBonus
                             - opponent.Stats.GetDefenceDC(options[i].Stat);
                if (margin > chosenMargin)
                    return false;
            }

            return true;
        }
    }
}
