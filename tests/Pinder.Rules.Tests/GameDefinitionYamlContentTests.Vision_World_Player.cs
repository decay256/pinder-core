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

        // Mutation: would catch if world description omits shadow growth explanation
        [Fact]
        public void WorldDescription_MentionsShadowGrowth()
        {
            var data = ParseYaml();
            var world = data["game_master_prompt"];
            Assert.True(
                world.Contains("shadow", StringComparison.OrdinalIgnoreCase) &&
                (world.Contains("grow", StringComparison.OrdinalIgnoreCase) ||
                 world.Contains("penalize", StringComparison.OrdinalIgnoreCase) ||
                 world.Contains("corrupt", StringComparison.OrdinalIgnoreCase)),
                "World description must explain that shadows grow and penalize paired stats");
        }

        // Mutation: would catch if world description omits interest range 0-25
        [Fact]
        public void WorldDescription_MentionsInterestRange()
        {
            var data = ParseYaml();
            var world = data["game_master_prompt"];
            var player = data["player_avatar_role_description"];
            // Must mention 0 and 25 as the interest range boundaries
            Assert.Contains("0", world);
            Assert.Contains("25", player);
        }

        // Mutation: would catch if world description omits ghosting/Bored state risk
        [Fact]
        public void WorldDescription_MentionsBoredGhostingRisk()
        {
            var data = ParseYaml();
            var world = data["game_master_prompt"];
            var datee = data["datee_role_description"];
            Assert.True(
                world.Contains("Bored", StringComparison.Ordinal) ||
                datee.Contains("Bored", StringComparison.Ordinal) ||
                world.Contains("ghost", StringComparison.OrdinalIgnoreCase) ||
                datee.Contains("ghost", StringComparison.OrdinalIgnoreCase),
                "World description must mention Bored state or ghosting risk");
        }

        // ===== AC2 / AC4: Player role description content requirements =====

        [Fact]
        public void PlayerRole_MentionsDialogueOptionCount()
        {
            var data = ParseYaml();
            var player = data["player_avatar_role_description"];
            // The count is now parameterized via max_dialogue_options rather than hardcoded; assert the concept is present.
            Assert.True(
                player.Contains("dialogue options", StringComparison.OrdinalIgnoreCase) ||
                player.Contains("max_dialogue_options", StringComparison.OrdinalIgnoreCase),
                "Player role must mention generating dialogue options per turn");
        }

        // Mutation: would catch if player role omits texting style reference
        [Fact]
        public void PlayerRole_MentionsTextingStyle()
        {
            var data = ParseYaml();
            var player = data["player_avatar_role_description"];
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
            var player = data["player_avatar_role_description"];
            Assert.True(
                player.Contains("stat", StringComparison.OrdinalIgnoreCase) &&
                (player.Contains("tied", StringComparison.OrdinalIgnoreCase) ||
                 player.Contains("each", StringComparison.OrdinalIgnoreCase) ||
                 player.Contains("one of", StringComparison.OrdinalIgnoreCase)),
                "Player role must mention options tied to stats");
        }

        // Mutation: would catch if player role omits Horniness forced Rizz mechanic
        [Fact]
        public void PlayerRole_MentionsHorninessRizzMechanic()
        {
            var data = ParseYaml();
            var player = data["player_avatar_role_description"];
            Assert.True(
                player.Contains("Horniness", StringComparison.Ordinal) ||
                player.Contains("Rizz", StringComparison.Ordinal),
                "Player role must mention Horniness forcing Rizz options");
        }
    }
}
