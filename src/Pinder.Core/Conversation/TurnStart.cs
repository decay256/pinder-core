namespace Pinder.Core.Conversation
{
    /// <summary>
    /// Result of starting a turn: the dialogue options and current game state.
    /// </summary>
    public sealed class TurnStart
    {
        /// <summary>The dialogue options available to the player.</summary>
        public DialogueOption[] Options { get; }

        /// <summary>Snapshot of game state at the start of this turn.</summary>
        public GameStateSnapshot State { get; }

        public TurnStart(DialogueOption[] options, GameStateSnapshot state)
        {
            Options = options ?? throw new System.ArgumentNullException(nameof(options));
            State = state ?? throw new System.ArgumentNullException(nameof(state));
        }
    }
}
