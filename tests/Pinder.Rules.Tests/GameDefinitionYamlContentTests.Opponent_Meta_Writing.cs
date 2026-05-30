using System;
using Xunit;

namespace Pinder.Rules.Tests
{
    public partial class GameDefinitionYamlContentTests
    {
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
            foreach (var key in new[] { "world_description", "player_role_description",
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
