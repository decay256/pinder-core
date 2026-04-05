using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Pinder.Rules.Tests
{
    /// <summary>
    /// Deep content validation for game-definition.yaml against the spec for issue #545.
    /// These tests verify that each section contains Pinder-specific creative direction,
    /// not generic boilerplate, per the acceptance criteria.
    /// </summary>
    public class GameDefinitionYamlContentTests
    {
        private static string LoadYamlContent()
        {
            var dir = AppDomain.CurrentDomain.BaseDirectory;
            while (dir != null && !File.Exists(Path.Combine(dir, "data", "game-definition.yaml")))
            {
                dir = Directory.GetParent(dir)?.FullName;
            }
            if (dir == null)
                throw new FileNotFoundException("Could not find data/game-definition.yaml from test directory");
            return File.ReadAllText(Path.Combine(dir, "data", "game-definition.yaml"));
        }

        private static Dictionary<string, string> ParseYaml()
        {
            var content = LoadYamlContent();
            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(UnderscoredNamingConvention.Instance)
                .Build();
            return deserializer.Deserialize<Dictionary<string, string>>(content);
        }

        // ===== AC1: File location and format =====

        // Mutation: would catch if file contained tab characters causing YAML parse issues
        [Fact]
        public void YamlFile_ContainsNoTabs()
        {
            var content = LoadYamlContent();
            Assert.DoesNotContain("\t", content);
        }

        // Mutation: would catch if file had BOM marker (spec requires UTF-8 without BOM)
        [Fact]
        public void YamlFile_HasNoBom()
        {
            var dir = AppDomain.CurrentDomain.BaseDirectory;
            while (dir != null && !File.Exists(Path.Combine(dir, "data", "game-definition.yaml")))
            {
                dir = Directory.GetParent(dir)?.FullName;
            }
            var bytes = File.ReadAllBytes(Path.Combine(dir!, "data", "game-definition.yaml"));
            // UTF-8 BOM is 0xEF 0xBB 0xBF
            if (bytes.Length >= 3)
            {
                Assert.False(bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF,
                    "File has a UTF-8 BOM — spec requires UTF-8 without BOM");
            }
        }

        // Mutation: would catch if all 7 values aren't scalar strings (e.g. nested objects)
        [Fact]
        public void YamlFile_AllValuesAreScalarStrings()
        {
            var content = LoadYamlContent();
            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(UnderscoredNamingConvention.Instance)
                .Build();
            // Parse as Dictionary<string, object> to detect non-string values
            var data = deserializer.Deserialize<Dictionary<string, object>>(content);
            foreach (var kvp in data)
            {
                Assert.IsType<string>(kvp.Value);
            }
        }

        // Mutation: would catch if exactly 7 top-level keys aren't present
        [Fact]
        public void YamlFile_HasExactly7Keys()
        {
            var data = ParseYaml();
            Assert.Equal(7, data.Count);
        }

        // ===== AC2 / AC4: Vision content requirements =====

        // Mutation: would catch if vision omits multiplayer structure mention
        [Fact]
        public void Vision_MentionsMultiplayerStructure()
        {
            var data = ParseYaml();
            var vision = data["vision"];
            // Must mention that opponents are other players' characters
            Assert.True(
                vision.Contains("player", StringComparison.OrdinalIgnoreCase) &&
                (vision.Contains("opponent", StringComparison.OrdinalIgnoreCase) ||
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
            var vision = data["vision"];
            Assert.True(
                vision.Contains("emotional", StringComparison.OrdinalIgnoreCase) ||
                vision.Contains("tension", StringComparison.OrdinalIgnoreCase) ||
                vision.Contains("stakes", StringComparison.OrdinalIgnoreCase) ||
                vision.Contains("feel", StringComparison.OrdinalIgnoreCase),
                "Vision must mention emotional stakes beneath absurdity");
        }

        // Mutation: would catch if vision omits RPG identity (dice, stats)
        [Fact]
        public void Vision_MentionsRpgMechanics()
        {
            var data = ParseYaml();
            var vision = data["vision"];
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
            var world = data["world_description"];
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
            var world = data["world_description"];
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
            var world = data["world_description"];
            // Must mention 0 and 25 as the interest range boundaries
            Assert.Contains("0", world);
            Assert.Contains("25", world);
        }

        // Mutation: would catch if world description omits ghosting/Bored state risk
        [Fact]
        public void WorldDescription_MentionsBoredGhostingRisk()
        {
            var data = ParseYaml();
            var world = data["world_description"];
            Assert.True(
                world.Contains("Bored", StringComparison.Ordinal) ||
                world.Contains("ghost", StringComparison.OrdinalIgnoreCase),
                "World description must mention Bored state or ghosting risk");
        }

        // ===== AC2 / AC4: Player role description content requirements =====

        // Mutation: would catch if player role omits 4 dialogue options per turn
        [Fact]
        public void PlayerRole_MentionsDialogueOptionCount()
        {
            var data = ParseYaml();
            var player = data["player_role_description"];
            Assert.True(
                player.Contains("4 option", StringComparison.OrdinalIgnoreCase) ||
                player.Contains("four option", StringComparison.OrdinalIgnoreCase) ||
                player.Contains("4 dialogue", StringComparison.OrdinalIgnoreCase) ||
                player.Contains("four dialogue", StringComparison.OrdinalIgnoreCase),
                "Player role must mention generating 4 dialogue options per turn");
        }

        // Mutation: would catch if player role omits texting style reference
        [Fact]
        public void PlayerRole_MentionsTextingStyle()
        {
            var data = ParseYaml();
            var player = data["player_role_description"];
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
            var player = data["player_role_description"];
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
            var player = data["player_role_description"];
            Assert.True(
                player.Contains("Horniness", StringComparison.Ordinal) ||
                player.Contains("Rizz", StringComparison.Ordinal),
                "Player role must mention Horniness forcing Rizz options");
        }

        // ===== AC2 / AC4: Opponent role description content requirements =====

        // Mutation: would catch if opponent role omits resistance below Interest 25
        [Fact]
        public void OpponentRole_MentionsResistanceBelowTwentyFive()
        {
            var data = ParseYaml();
            var opponent = data["opponent_role_description"];
            Assert.True(
                opponent.Contains("resist", StringComparison.OrdinalIgnoreCase) ||
                opponent.Contains("not won over", StringComparison.OrdinalIgnoreCase) ||
                opponent.Contains("holdback", StringComparison.OrdinalIgnoreCase),
                "Opponent role must establish resistance below Interest 25");
        }

        // Mutation: would catch if opponent role omits that it's another player's character
        [Fact]
        public void OpponentRole_MentionsOtherPlayerCharacter()
        {
            var data = ParseYaml();
            var opponent = data["opponent_role_description"];
            Assert.True(
                opponent.Contains("player", StringComparison.OrdinalIgnoreCase) &&
                (opponent.Contains("uploaded", StringComparison.OrdinalIgnoreCase) ||
                 opponent.Contains("puppet", StringComparison.OrdinalIgnoreCase) ||
                 opponent.Contains("another", StringComparison.OrdinalIgnoreCase) ||
                 opponent.Contains("other", StringComparison.OrdinalIgnoreCase)),
                "Opponent role must mention the opponent is another player's uploaded character");
        }

        // Mutation: would catch if opponent role omits failure tier reaction guidance
        [Fact]
        public void OpponentRole_MentionsFailureReactions()
        {
            var data = ParseYaml();
            var opponent = data["opponent_role_description"];
            Assert.True(
                opponent.Contains("fail", StringComparison.OrdinalIgnoreCase) ||
                opponent.Contains("tier", StringComparison.OrdinalIgnoreCase),
                "Opponent role must mention reacting to failure tiers");
        }

        // Mutation: would catch if opponent role omits Date Secured at 25
        [Fact]
        public void OpponentRole_MentionsDateSecured()
        {
            var data = ParseYaml();
            var opponent = data["opponent_role_description"];
            Assert.True(
                opponent.Contains("Date Secured", StringComparison.Ordinal) ||
                (opponent.Contains("25", StringComparison.Ordinal) &&
                 opponent.Contains("resist", StringComparison.OrdinalIgnoreCase)),
                "Opponent role must mention Date Secured / resistance dissolving at 25");
        }

        // ===== AC2 / AC4: Meta contract content requirements =====

        // Mutation: would catch if meta contract omits "never reference game mechanics"
        [Fact]
        public void MetaContract_ForbidsReferencingGameMechanics()
        {
            var data = ParseYaml();
            var meta = data["meta_contract"];
            Assert.True(
                (meta.Contains("dice", StringComparison.OrdinalIgnoreCase) ||
                 meta.Contains("DC", StringComparison.Ordinal) ||
                 meta.Contains("mechanic", StringComparison.OrdinalIgnoreCase) ||
                 meta.Contains("interest meter", StringComparison.OrdinalIgnoreCase)) &&
                meta.Contains("never", StringComparison.OrdinalIgnoreCase),
                "Meta contract must forbid referencing game mechanics in dialogue");
        }

        // Mutation: would catch if meta contract omits "never add content player didn't choose"
        [Fact]
        public void MetaContract_ForbidsAddingContent()
        {
            var data = ParseYaml();
            var meta = data["meta_contract"];
            Assert.True(
                meta.Contains("add", StringComparison.OrdinalIgnoreCase) &&
                (meta.Contains("didn't choose", StringComparison.OrdinalIgnoreCase) ||
                 meta.Contains("didn't select", StringComparison.OrdinalIgnoreCase) ||
                 meta.Contains("not chosen", StringComparison.OrdinalIgnoreCase) ||
                 meta.Contains("player didn", StringComparison.OrdinalIgnoreCase) ||
                 meta.Contains("content", StringComparison.OrdinalIgnoreCase)),
                "Meta contract must forbid adding ideas the player didn't choose");
        }

        // Mutation: would catch if meta contract omits "never resolve date early"
        [Fact]
        public void MetaContract_ForbidsEarlyDateResolution()
        {
            var data = ParseYaml();
            var meta = data["meta_contract"];
            Assert.True(
                (meta.Contains("resolve", StringComparison.OrdinalIgnoreCase) ||
                 meta.Contains("date", StringComparison.OrdinalIgnoreCase)) &&
                (meta.Contains("25", StringComparison.Ordinal) ||
                 meta.Contains("Interest", StringComparison.Ordinal) ||
                 meta.Contains("early", StringComparison.OrdinalIgnoreCase)),
                "Meta contract must forbid resolving the date before Interest 25");
        }

        // Mutation: would catch if meta contract omits two distinct voices rule
        [Fact]
        public void MetaContract_RequiresDistinctVoices()
        {
            var data = ParseYaml();
            var meta = data["meta_contract"];
            Assert.True(
                meta.Contains("distinct", StringComparison.OrdinalIgnoreCase) ||
                meta.Contains("voice", StringComparison.OrdinalIgnoreCase) ||
                meta.Contains("sound alike", StringComparison.OrdinalIgnoreCase),
                "Meta contract must require maintaining two distinct character voices");
        }

        // Mutation: would catch if meta contract omits ENGINE block rule
        [Fact]
        public void MetaContract_MentionsEngineBlocks()
        {
            var data = ParseYaml();
            var meta = data["meta_contract"];
            Assert.Contains("ENGINE", meta);
        }

        // ===== AC2 / AC4: Writing rules content requirements =====

        // Mutation: would catch if writing rules omit message length guidance
        [Fact]
        public void WritingRules_MentionsMessageLength()
        {
            var data = ParseYaml();
            var rules = data["writing_rules"];
            Assert.True(
                rules.Contains("sentence", StringComparison.OrdinalIgnoreCase) ||
                rules.Contains("short", StringComparison.OrdinalIgnoreCase) ||
                rules.Contains("length", StringComparison.OrdinalIgnoreCase),
                "Writing rules must include message length guidance");
        }

        // Mutation: would catch if writing rules omit emoji usage convention
        [Fact]
        public void WritingRules_MentionsEmojiUsage()
        {
            var data = ParseYaml();
            var rules = data["writing_rules"];
            Assert.True(
                rules.Contains("emoji", StringComparison.OrdinalIgnoreCase),
                "Writing rules must mention emoji usage conventions");
        }

        // Mutation: would catch if writing rules omit no-asterisk-actions rule
        [Fact]
        public void WritingRules_ForbidsAsteriskActions()
        {
            var data = ParseYaml();
            var rules = data["writing_rules"];
            Assert.Contains("asterisk", rules, StringComparison.OrdinalIgnoreCase);
        }

        // Mutation: would catch if writing rules omit comedy-through-voice principle
        [Fact]
        public void WritingRules_MentionsComedyThroughVoice()
        {
            var data = ParseYaml();
            var rules = data["writing_rules"];
            Assert.True(
                rules.Contains("comedy", StringComparison.OrdinalIgnoreCase) &&
                rules.Contains("voice", StringComparison.OrdinalIgnoreCase),
                "Writing rules must establish comedy through character voice");
        }

        // Mutation: would catch if writing rules omit "strong rolls sharpen, don't add"
        [Fact]
        public void WritingRules_MentionsStrongRollSharpening()
        {
            var data = ParseYaml();
            var rules = data["writing_rules"];
            Assert.True(
                rules.Contains("sharpen", StringComparison.OrdinalIgnoreCase) ||
                rules.Contains("improve", StringComparison.OrdinalIgnoreCase) ||
                rules.Contains("phrasing", StringComparison.OrdinalIgnoreCase),
                "Writing rules must state strong rolls sharpen phrasing, not add ideas");
        }

        // Mutation: would catch if writing rules omit failure corruption
        [Fact]
        public void WritingRules_MentionsFailureCorruption()
        {
            var data = ParseYaml();
            var rules = data["writing_rules"];
            Assert.True(
                rules.Contains("fail", StringComparison.OrdinalIgnoreCase) &&
                (rules.Contains("corrupt", StringComparison.OrdinalIgnoreCase) ||
                 rules.Contains("typo", StringComparison.OrdinalIgnoreCase) ||
                 rules.Contains("degrade", StringComparison.OrdinalIgnoreCase) ||
                 rules.Contains("awkward", StringComparison.OrdinalIgnoreCase)),
                "Writing rules must describe how failures corrupt/degrade messages");
        }

        // ===== AC3: YAML is parseable — multi-line values preserved =====

        // Mutation: would catch if block scalars are broken (single-line instead of multi-line)
        [Fact]
        public void YamlFile_MultiLineValuesContainNewlines()
        {
            var data = ParseYaml();
            // All content sections should be multi-line (contain newlines)
            foreach (var key in new[] { "vision", "world_description", "player_role_description",
                                        "opponent_role_description", "meta_contract", "writing_rules" })
            {
                Assert.Contains("\n", data[key]);
            }
        }

        // ===== Edge case: no emoji characters in YAML file itself (spec says words only) =====

        // Mutation: would catch if YAML file uses emoji instead of describing them
        [Fact]
        public void YamlFile_ContainsNoEmojiCharacters()
        {
            var content = LoadYamlContent();
            // Check for common emoji ranges — the spec says "no emoji in the YAML file itself"
            // Simple check: no characters outside BMP common ranges that look like emoji
            foreach (var ch in content)
            {
                Assert.False(ch >= '\uD800' && ch <= '\uDFFF',
                    "YAML file should not contain emoji (surrogate pair detected)");
            }
        }
    }
}
