using System;
using Pinder.Core.Conversation;
using Pinder.Core.Stats;

namespace Pinder.Core.Characters
{
    /// <summary>
    /// Sealed class wrapping assembled character data needed at runtime.
    /// Produced by the character assembly pipeline and consumed by GameSession.
    /// </summary>
    public sealed class CharacterProfile
    {
        /// <summary>The character's stat block for roll resolution.</summary>
        public StatBlock Stats { get; }

        /// <summary>The fully assembled system prompt for LLM interactions.</summary>
        public string AssembledSystemPrompt { get; }

        /// <summary>Display name shown in conversation history.</summary>
        public string DisplayName { get; }

        /// <summary>Timing profile for reply delay computation.</summary>
        public TimingProfile Timing { get; }

        /// <summary>Character level (1-based) for level bonus in rolls.</summary>
        public int Level { get; }

        /// <summary>The character's one-liner bio shown on their profile.</summary>
        public string Bio { get; }

        public CharacterProfile(
            StatBlock stats,
            string assembledSystemPrompt,
            string displayName,
            TimingProfile timing,
            int level,
            string bio = "")
        {
            Stats = stats ?? throw new ArgumentNullException(nameof(stats));
            AssembledSystemPrompt = assembledSystemPrompt ?? throw new ArgumentNullException(nameof(assembledSystemPrompt));
            DisplayName = displayName ?? throw new ArgumentNullException(nameof(displayName));
            Timing = timing ?? throw new ArgumentNullException(nameof(timing));
            Level = level;
            Bio = bio ?? string.Empty;
        }
    }
}
