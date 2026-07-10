using System;
using System.Text.RegularExpressions;

namespace Pinder.LlmAdapters
{
    public enum CharacterPromptRole
    {
        PlayerAvatar,
        Datee
    }

    public sealed class CharacterPromptStructure
    {
        private static readonly Regex XmlTagNamePattern =
            new Regex("^[A-Za-z_][A-Za-z0-9_.-]*$", RegexOptions.Compiled);

        public static CharacterPromptStructure PinderDefaults { get; } =
            new CharacterPromptStructure(
                "== CHARACTER YOU CONTROL ==",
                "PLAYER_AVATAR_CHARACTER",
                "DATEE_CHARACTER");

        public string CharacterSpecHeader { get; }
        public string PlayerAvatarCharacterTag { get; }
        public string DateeCharacterTag { get; }

        public CharacterPromptStructure(
            string characterSpecHeader,
            string playerAvatarCharacterTag,
            string dateeCharacterTag)
        {
            CharacterSpecHeader = RequireHeader(characterSpecHeader);
            PlayerAvatarCharacterTag = RequireTag(playerAvatarCharacterTag, nameof(playerAvatarCharacterTag));
            DateeCharacterTag = RequireTag(dateeCharacterTag, nameof(dateeCharacterTag));
        }

        public string GetCharacterTag(CharacterPromptRole role)
        {
            return role switch
            {
                CharacterPromptRole.PlayerAvatar => PlayerAvatarCharacterTag,
                CharacterPromptRole.Datee => DateeCharacterTag,
                _ => throw new ArgumentOutOfRangeException(nameof(role), role, null)
            };
        }

        private static string RequireHeader(string value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ConfigurationException("game-definition.yaml character_prompt_structure.character_spec_header cannot be empty.");
            }

            if (value.IndexOf('\n') >= 0 || value.IndexOf('\r') >= 0)
            {
                throw new ConfigurationException("game-definition.yaml character_prompt_structure.character_spec_header must be a single line.");
            }

            return value;
        }

        private static string RequireTag(string value, string key)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            if (!XmlTagNamePattern.IsMatch(value))
            {
                throw new ConfigurationException($"game-definition.yaml character_prompt_structure.{key} must be a valid XML-style tag name.");
            }

            return value;
        }
    }
}
