using System;
using Pinder.Core.Characters;
using Pinder.Core.Stats;

namespace Pinder.Core.Conversation
{
    /// <summary>Resolved HFI/TOR state for one communicating character.</summary>
    public sealed class CharacterEmotionalStatus
    {
        public CharacterEmotionalStatus(int hungerForIntimacy, int terrorOfRejection)
        {
            HungerForIntimacy = hungerForIntimacy;
            TerrorOfRejection = terrorOfRejection;
        }

        public int HungerForIntimacy { get; }
        public int TerrorOfRejection { get; }
    }

    /// <summary>
    /// Single source of truth for deriving HFI/TOR from game overrides or a
    /// character's base stats. A zero override preserves the established
    /// Charm/Rizz fallback contract.
    /// </summary>
    public static class CharacterEmotionalStatusResolver
    {
        public static CharacterEmotionalStatus Resolve(
            CharacterProfile character,
            int hungerForIntimacyOverride,
            int terrorOfRejectionOverride)
        {
            if (character == null) throw new ArgumentNullException(nameof(character));
            return new CharacterEmotionalStatus(
                hungerForIntimacyOverride != 0
                    ? hungerForIntimacyOverride
                    : character.Stats.GetBase(StatType.Charm),
                terrorOfRejectionOverride != 0
                    ? terrorOfRejectionOverride
                    : character.Stats.GetBase(StatType.Rizz));
        }
    }
}
