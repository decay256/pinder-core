using System;

namespace Pinder.Core.Conversation
{
    /// <summary>
    /// Private, turn-local emotional direction for any character who is about
    /// to communicate. The model is role-neutral; callers supply character and
    /// situation context separately.
    /// </summary>
    public sealed class CharacterEmotionalDirection
    {
        public CharacterEmotionalDirection(
            string primaryEmotion,
            string intensity,
            string underlyingFeeling,
            string interpretation,
            string impulse,
            string restraint,
            string responsePosture)
        {
            PrimaryEmotion = primaryEmotion ?? throw new ArgumentNullException(nameof(primaryEmotion));
            Intensity = intensity ?? throw new ArgumentNullException(nameof(intensity));
            UnderlyingFeeling = underlyingFeeling ?? throw new ArgumentNullException(nameof(underlyingFeeling));
            Interpretation = interpretation ?? throw new ArgumentNullException(nameof(interpretation));
            Impulse = impulse ?? throw new ArgumentNullException(nameof(impulse));
            Restraint = restraint ?? throw new ArgumentNullException(nameof(restraint));
            ResponsePosture = responsePosture ?? throw new ArgumentNullException(nameof(responsePosture));
        }

        public string PrimaryEmotion { get; }
        public string Intensity { get; }
        public string UnderlyingFeeling { get; }
        public string Interpretation { get; }
        public string Impulse { get; }
        public string Restraint { get; }
        public string ResponsePosture { get; }
    }
}
