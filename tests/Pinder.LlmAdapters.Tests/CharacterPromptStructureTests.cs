using System;
using Xunit;

namespace Pinder.LlmAdapters.Tests
{
    public class CharacterPromptStructureTests
    {
        private const string BaseYaml = @"
name: Test
game_master_prompt: gm
player_avatar_role_description: player role
datee_role_description: datee role
global_dc_bias: 0
horniness_time_modifiers:
  morning: 3
  afternoon: 0
  evening: 2
  overnight: 5
active_trap_interest_penalty: -0.25
max_turns: 30
max_dialogue_options: 3
max_delivery_words: 80
hunger_for_intimacy: 0
terror_of_rejection: 0
xp_flat_awards:
  nat20: 25
  nat1: 10
  failure: 2
xp_success_base:
  dc_low_max: 16
  dc_low_xp: 5
  dc_mid_max: 20
  dc_mid_xp: 10
  dc_high_xp: 15
xp_risk_multipliers:
  safe: 1.0
  medium: 1.5
  hard: 2.0
  bold: 3.0
  reckless: 10.0
xp_terminal_multipliers:
  date_secured: 3.0
  unmatched: 1.0
  ghosted: 1.0
progression_xp_thresholds:
  ""1"": 0
  ""2"": 50
  ""3"": 150
  ""4"": 300
  ""5"": 500
  ""6"": 750
  ""7"": 1100
  ""8"": 1500
  ""9"": 2000
  ""10"": 2750
  ""11"": 3500
progression_build_points:
  ""1"": 0
  ""2"": 2
  ""3"": 2
  ""4"": 2
  ""5"": 3
  ""6"": 3
  ""7"": 3
  ""8"": 4
  ""9"": 4
  ""10"": 5
  ""11"": 0
progression_level_bonuses:
  ""1"": 0
  ""2"": 0
  ""3"": 1
  ""4"": 1
  ""5"": 2
  ""6"": 2
  ""7"": 3
  ""8"": 3
  ""9"": 4
  ""10"": 4
  ""11"": 5
progression_item_slots:
  ""1"": 2
  ""2"": 2
  ""3"": 3
  ""4"": 3
  ""5"": 4
  ""6"": 4
  ""7"": 5
  ""8"": 5
  ""9"": 6
  ""10"": 6
  ""11"": 6
progression_failure_pool_tiers:
  intermediate_min: 4
  advanced_min: 7
  legendary_min: 10
";

        [Fact]
        public void LoadFrom_CustomCharacterPromptStructure_IsEmittedByBuilder()
        {
            var yaml = BaseYaml + @"
character_prompt_structure:
  character_spec_header: ""== CUSTOM CHARACTER DATA ==""
  player_avatar_character_tag: ""CUSTOM_PLAYER_DATA""
  datee_character_tag: ""CUSTOM_DATEE_DATA""
";

            var def = GameDefinition.LoadFrom(yaml);
            var playerPrompt = SessionSystemPromptBuilder.BuildPlayerAvatar("player profile", def);
            var dateePrompt = SessionSystemPromptBuilder.BuildDatee("datee profile", def);

            Assert.Equal("== CUSTOM CHARACTER DATA ==", def.CharacterPromptStructure.CharacterSpecHeader);
            Assert.Contains("== CUSTOM CHARACTER DATA ==", playerPrompt);
            Assert.Contains("<CUSTOM_PLAYER_DATA>", playerPrompt);
            Assert.Contains("</CUSTOM_PLAYER_DATA>", playerPrompt);
            Assert.Contains("<CUSTOM_DATEE_DATA>", dateePrompt);
            Assert.Contains("</CUSTOM_DATEE_DATA>", dateePrompt);
            Assert.DoesNotContain("<PLAYER_AVATAR_CHARACTER>", playerPrompt);
            Assert.DoesNotContain("<DATEE_CHARACTER>", dateePrompt);
        }

        [Fact]
        public void LoadFrom_MissingCharacterPromptStructure_Throws()
        {
            var ex = Assert.Throws<InvalidOperationException>(() => GameDefinition.LoadFrom(BaseYaml));
            Assert.Contains("character_prompt_structure", ex.Message);
        }

        [Fact]
        public void LoadFrom_InvalidCharacterTag_Throws()
        {
            var yaml = BaseYaml + @"
character_prompt_structure:
  character_spec_header: ""== CUSTOM CHARACTER DATA ==""
  player_avatar_character_tag: ""PLAYER AVATAR""
  datee_character_tag: ""CUSTOM_DATEE_DATA""
";

            var ex = Assert.Throws<ConfigurationException>(() => GameDefinition.LoadFrom(yaml));
            Assert.Contains("playerAvatarCharacterTag", ex.Message);
        }
    }
}
