using System;

namespace Pinder.Core.Conversation
{
    /// <summary>
    /// Accepted private emotional direction together with the exact role-specific
    /// source packet used to determine it.
    /// </summary>
    public sealed class CharacterEmotionalDirectorResult
    {
        public CharacterEmotionalDirectorResult(
            CharacterEmotionalDirection direction,
            string directorInput)
        {
            Direction = direction ?? throw new ArgumentNullException(nameof(direction));
            DirectorInput = directorInput ?? throw new ArgumentNullException(nameof(directorInput));
        }

        public CharacterEmotionalDirection Direction { get; }
        public string DirectorInput { get; }
    }
}
