namespace Pinder.Core.Conversation
{
    /// <summary>
    /// Private, turn-local direction for the emotional posture from which the
    /// player avatar's candidate messages are written.
    /// </summary>
    public sealed class AvatarEmotionalDirection
    {
        public AvatarEmotionalDirection(string primaryEmotion, string responsePosture)
        {
            PrimaryEmotion = primaryEmotion ?? throw new System.ArgumentNullException(nameof(primaryEmotion));
            ResponsePosture = responsePosture ?? throw new System.ArgumentNullException(nameof(responsePosture));
        }

        public string PrimaryEmotion { get; }
        public string ResponsePosture { get; }
    }
}
