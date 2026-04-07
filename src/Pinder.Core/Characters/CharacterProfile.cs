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
        public string AssembledSystemPrompt { get; private set; }

        /// <summary>Display name shown in conversation history.</summary>
        public string DisplayName { get; }

        /// <summary>Timing profile for reply delay computation.</summary>
        public TimingProfile Timing { get; }

        /// <summary>Character level (1-based) for level bonus in rolls.</summary>
        public int Level { get; }

        /// <summary>The character's one-liner bio shown on their profile.</summary>
        public string Bio { get; }

        /// <summary>
        /// The texting style fragment(s) joined, for injection into
        /// option-generation prompts. Empty string if not available.
        /// </summary>
        public string TextingStyleFragment { get; }

        /// <summary>
        /// The character's active archetype, or null if none resolved.
        /// Carries name, behavior directive, and interference level.
        /// </summary>
        public ActiveArchetype ActiveArchetype { get; }

        /// <summary>LLM-generated psychological portrait. Set at session start.</summary>
        public string? PsychologicalStake { get; set; }

        /// <summary>Appends additional text to the assembled system prompt.</summary>
        public void AppendToSystemPrompt(string text)
        {
            if (!string.IsNullOrEmpty(text))
                AssembledSystemPrompt += text;
        }

        public CharacterProfile(
            StatBlock stats,
            string assembledSystemPrompt,
            string displayName,
            TimingProfile timing,
            int level,
            string bio = "",
            string textingStyleFragment = "",
            ActiveArchetype activeArchetype = null)
        {
            Stats = stats ?? throw new ArgumentNullException(nameof(stats));
            AssembledSystemPrompt = assembledSystemPrompt ?? throw new ArgumentNullException(nameof(assembledSystemPrompt));
            DisplayName = displayName ?? throw new ArgumentNullException(nameof(displayName));
            Timing = timing ?? throw new ArgumentNullException(nameof(timing));
            Level = level;
            Bio = bio ?? string.Empty;
            TextingStyleFragment = textingStyleFragment ?? string.Empty;
            ActiveArchetype = activeArchetype;
        }
    }
}
