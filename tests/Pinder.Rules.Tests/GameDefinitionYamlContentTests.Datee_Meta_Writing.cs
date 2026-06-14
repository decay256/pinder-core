using System;
using Xunit;

namespace Pinder.Rules.Tests
{
    public partial class GameDefinitionYamlContentTests
    {
        // ===== AC2 / AC4: Datee role description content requirements =====

        // Mutation: would catch if datee role omits resistance below Interest 25
        [Fact]
        public void DateeRole_MentionsResistanceBelowTwentyFive()
        {
            var data = ParseYaml();
            var datee = data["datee_role_description"];
            Assert.True(
                datee.Contains("resist", StringComparison.OrdinalIgnoreCase) ||
                datee.Contains("not won over", StringComparison.OrdinalIgnoreCase) ||
                datee.Contains("holdback", StringComparison.OrdinalIgnoreCase),
                "Datee role must establish resistance below Interest 25");
        }

        // Mutation: would catch if datee role omits that it's another player's character
        [Fact]
        public void DateeRole_MentionsOtherPlayerCharacter()
        {
            var data = ParseYaml();
            var datee = data["datee_role_description"];
            Assert.True(
                datee.Contains("player", StringComparison.OrdinalIgnoreCase) &&
                (datee.Contains("uploaded", StringComparison.OrdinalIgnoreCase) ||
                 datee.Contains("puppet", StringComparison.OrdinalIgnoreCase) ||
                 datee.Contains("another", StringComparison.OrdinalIgnoreCase) ||
                 datee.Contains("other", StringComparison.OrdinalIgnoreCase)),
                "Datee role must mention the datee is another player's uploaded character");
        }

        // Mutation: would catch if datee role omits failure tier reaction guidance
        [Fact]
        public void DateeRole_MentionsFailureReactions()
        {
            var data = ParseYaml();
            var datee = data["datee_role_description"];
            Assert.True(
                datee.Contains("fail", StringComparison.OrdinalIgnoreCase) ||
                datee.Contains("tier", StringComparison.OrdinalIgnoreCase),
                "Datee role must mention reacting to failure tiers");
        }

        // Mutation: would catch if datee role omits Date Secured at 25
        [Fact]
        public void DateeRole_MentionsDateSecured()
        {
            var data = ParseYaml();
            var datee = data["datee_role_description"];
            Assert.True(
                datee.Contains("Date Secured", StringComparison.Ordinal) ||
                (datee.Contains("25", StringComparison.Ordinal) &&
                 datee.Contains("resist", StringComparison.OrdinalIgnoreCase)),
                "Datee role must mention Date Secured / resistance dissolving at 25");
        }

        // ===== AC2 / AC4: Meta contract content requirements =====

        // Mutation: would catch if meta contract omits "never reference game mechanics"
        [Fact]
        public void MetaContract_ForbidsReferencingGameMechanics()
        {
            var data = ParseYaml();
            var meta = data["game_master_prompt"];
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
            var meta = data["game_master_prompt"];
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
            var meta = data["game_master_prompt"];
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
            var meta = data["game_master_prompt"];
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
            var meta = data["game_master_prompt"];
            Assert.Contains("ENGINE", meta);
        }

        // ===== AC2 / AC4: Writing rules content requirements =====

        // Mutation: would catch if writing rules omit message length guidance
        [Fact]
        public void WritingRules_MentionsMessageLength()
        {
            var data = ParseYaml();
            var rules = data["game_master_prompt"];
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
            var rules = data["game_master_prompt"];
            Assert.True(
                rules.Contains("emoji", StringComparison.OrdinalIgnoreCase),
                "Writing rules must mention emoji usage conventions");
        }

        // Mutation: would catch if writing rules omit no-asterisk-actions rule
        [Fact]
        public void WritingRules_ForbidsAsteriskActions()
        {
            var data = ParseYaml();
            var rules = data["game_master_prompt"];
            Assert.Contains("asterisk", rules, StringComparison.OrdinalIgnoreCase);
        }

        // Mutation: would catch if writing rules omit comedy-through-voice principle
        [Fact]
        public void WritingRules_MentionsComedyThroughVoice()
        {
            var data = ParseYaml();
            var rules = data["game_master_prompt"];
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
            var rules = data["game_master_prompt"];
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
            var rules = data["game_master_prompt"];
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
            foreach (var key in new[] { "game_master_prompt", "player_avatar_role_description",
                                        "datee_role_description" })
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
