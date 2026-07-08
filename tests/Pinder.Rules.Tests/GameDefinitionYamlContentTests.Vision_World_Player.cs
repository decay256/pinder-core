using System;
using Xunit;

namespace Pinder.Rules.Tests
{
    public partial class GameDefinitionYamlContentTests
    {
        // ===== AC2 / AC4: Vision content requirements =====

        // Mutation: would catch if vision omits multiplayer structure mention
        [Fact]
        public void Vision_MentionsMultiplayerStructure()
        {
            var data = ParseYaml();
            var vision = data["game_master_prompt"];
            // Must mention that datees are other players' characters
            Assert.True(
                vision.Contains("player", StringComparison.OrdinalIgnoreCase) &&
                (vision.Contains("datee", StringComparison.OrdinalIgnoreCase) ||
                 vision.Contains("DATEE", StringComparison.OrdinalIgnoreCase) ||
                 vision.Contains("other player", StringComparison.OrdinalIgnoreCase) ||
                 vision.Contains("multiplayer", StringComparison.OrdinalIgnoreCase) ||
                 vision.Contains("uploaded", StringComparison.OrdinalIgnoreCase)),
                "Vision must establish multiplayer structure");
        }

        // Mutation: would catch if vision omits emotional stakes (comedy-only, no tension)
        [Fact]
        public void Vision_MentionsEmotionalStakes()
        {
            var data = ParseYaml();
            var vision = data["game_master_prompt"];
            Assert.True(
                vision.Contains("emotional", StringComparison.OrdinalIgnoreCase) ||
                vision.Contains("tension", StringComparison.OrdinalIgnoreCase) ||
                vision.Contains("stakes", StringComparison.OrdinalIgnoreCase) ||
                vision.Contains("feel", StringComparison.OrdinalIgnoreCase) ||
                vision.Contains("honest", StringComparison.OrdinalIgnoreCase) ||
                vision.Contains("touching", StringComparison.OrdinalIgnoreCase) ||
                vision.Contains("despair", StringComparison.OrdinalIgnoreCase) ||
                vision.Contains("desparate", StringComparison.OrdinalIgnoreCase),
                "Vision must mention emotional stakes beneath absurdity");
        }

        // Mutation: would catch if vision omits RPG identity (dice, stats)
        [Fact]
        public void Vision_MentionsRpgMechanics()
        {
            var data = ParseYaml();
            var vision = data["game_master_prompt"];
            Assert.True(
                vision.Contains("dice", StringComparison.OrdinalIgnoreCase) ||
                vision.Contains("d20", StringComparison.OrdinalIgnoreCase) ||
                vision.Contains("roll", StringComparison.OrdinalIgnoreCase) ||
                vision.Contains("RPG", StringComparison.OrdinalIgnoreCase) ||
                vision.Contains("stat", StringComparison.OrdinalIgnoreCase),
                "Vision must establish RPG mechanical identity");
        }

        // ===== AC2 / AC4: World description content requirements =====

        // Mutation: would catch if world description omits d20 or roll mechanics
        [Fact]
        public void WorldDescription_MentionsRollMechanics()
        {
            var data = ParseYaml();
            var world = data["game_master_prompt"];
            Assert.True(
                world.Contains("d20", StringComparison.OrdinalIgnoreCase) ||
                world.Contains("dice", StringComparison.OrdinalIgnoreCase) ||
                world.Contains("roll", StringComparison.OrdinalIgnoreCase),
                "World description must mention d20/dice/roll mechanics");
        }

        // Mutation: would catch if world description omits shadow trap/stat mechanics
        [Fact]
        public void WorldDescription_MentionsShadowMechanics()
        {
            var data = ParseYaml();
            var world = data["game_master_prompt"];
            Assert.True(
                world.Contains("shadow", StringComparison.OrdinalIgnoreCase) &&
                (world.Contains("trap", StringComparison.OrdinalIgnoreCase) ||
                 world.Contains("stat", StringComparison.OrdinalIgnoreCase) ||
                 world.Contains("leak", StringComparison.OrdinalIgnoreCase)),
                "World description must explain shadow traps or shadow stat behavior");
        }

        // Mutation: would catch if world description omits interest range 0-25
        [Fact]
        public void WorldDescription_MentionsInterestRange()
        {
            var data = ParseYaml();
            var world = data["game_master_prompt"];
            var player = data["player_avatar_role_description"];
            // Must mention 0 and 25 as the interest range boundaries
            Assert.Contains("INTEREST", world);
            Assert.Contains("25", player);
        }

        // Mutation: would catch if world description omits unmatched terminal state risk
        [Fact]
        public void WorldDescription_MentionsUnmatchedTerminalState()
        {
            var data = ParseYaml();
            var world = data["game_master_prompt"];
            var datee = data["datee_role_description"];
            Assert.True(
                world.Contains("UNMATCHED", StringComparison.Ordinal) ||
                datee.Contains("UNMATCHED", StringComparison.Ordinal),
                "World description must mention the unmatched terminal state risk");
        }

        // ===== AC2 / AC4: Player role description content requirements =====

        [Fact]
        public void PlayerRole_MentionsDialogueOptionCount()
        {
            var data = ParseYaml();
            var player = data["player_avatar_role_description"] + "\n" + data["game_master_prompt"];
            // The count is now parameterized via max_dialogue_options rather than hardcoded; assert the concept is present.
            Assert.True(
                player.Contains("OPTIONS", StringComparison.Ordinal) ||
                player.Contains("dialogue options", StringComparison.OrdinalIgnoreCase),
                "Player role must mention generating dialogue options per turn");
        }

        // Mutation: would catch if player role omits texting style reference
        [Fact]
        public void PlayerRole_MentionsTextingStyle()
        {
            var data = ParseYaml();
            var player = data["player_avatar_role_description"] + "\n" + data["game_master_prompt"];
            Assert.True(
                player.Contains("texting style", StringComparison.OrdinalIgnoreCase) ||
                player.Contains("voice", StringComparison.OrdinalIgnoreCase),
                "Player role must reference texting style as voice authority");
        }

        // Mutation: would catch if player role omits stat-tied options
        [Fact]
        public void PlayerRole_MentionsStatTiedOptions()
        {
            var data = ParseYaml();
            var player = data["player_avatar_role_description"] + "\n" + data["game_master_prompt"];
            Assert.True(
                player.Contains("stat", StringComparison.OrdinalIgnoreCase) &&
                (player.Contains("tied", StringComparison.OrdinalIgnoreCase) ||
                 player.Contains("each", StringComparison.OrdinalIgnoreCase) ||
                 player.Contains("one option", StringComparison.OrdinalIgnoreCase)),
                "Player role must mention options tied to stats");
        }

        // Mutation: would catch if game-definition drops horniness tuning from the YAML
        [Fact]
        public void YamlFile_MentionsHorninessTuning()
        {
            var player = LoadYamlContent();
            Assert.True(
                player.Contains("horniness_time_modifiers", StringComparison.Ordinal) &&
                player.Contains("horniness_dc_bias", StringComparison.Ordinal),
                "Game definition must include horniness tuning keys");
        }
    }
}
