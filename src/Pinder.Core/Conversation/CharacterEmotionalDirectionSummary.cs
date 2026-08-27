using System;

namespace Pinder.Core.Conversation
{
    /// <summary>
    /// Bounded private summary of an accepted DATEE emotional direction.
    /// Carries only the continuity fields needed by the next director turn.
    /// </summary>
    public sealed class CharacterEmotionalDirectionSummary
    {
        public CharacterEmotionalDirectionSummary(
            int turn,
            string primaryEmotion,
            string secondaryEmotion,
            string regulatoryState,
            int activation,
            string trajectory,
            string impulse)
        {
            Turn = turn;
            PrimaryEmotion = primaryEmotion ?? throw new ArgumentNullException(nameof(primaryEmotion));
            SecondaryEmotion = secondaryEmotion ?? throw new ArgumentNullException(nameof(secondaryEmotion));
            RegulatoryState = regulatoryState ?? throw new ArgumentNullException(nameof(regulatoryState));
            if (activation < 1 || activation > 5)
                throw new ArgumentOutOfRangeException(nameof(activation), "Activation must be between 1 and 5.");
            Activation = activation;
            Trajectory = trajectory ?? throw new ArgumentNullException(nameof(trajectory));
            Impulse = impulse ?? throw new ArgumentNullException(nameof(impulse));
        }

        public int Turn { get; }
        public string PrimaryEmotion { get; }
        public string SecondaryEmotion { get; }
        public string RegulatoryState { get; }
        public int Activation { get; }
        public string Trajectory { get; }
        public string Impulse { get; }

        public static CharacterEmotionalDirectionSummary FromDirection(
            int turn,
            CharacterEmotionalDirection direction)
        {
            if (direction == null) throw new ArgumentNullException(nameof(direction));
            return new CharacterEmotionalDirectionSummary(
                turn,
                direction.PrimaryEmotion,
                direction.SecondaryEmotion,
                direction.RegulatoryState,
                direction.Activation,
                direction.Trajectory,
                direction.Impulse);
        }
    }
}
