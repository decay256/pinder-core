namespace Pinder.Core.Tests
{
    internal static class GameDefinitionYamlTestFixtures
    {
        public const string RequiredParserBlocksWithoutActiveTrapPenalty = @"
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
  ghosted: 0.0
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
progression_currency_per_xp: 10
character_prompt_structure:
  character_spec_header: ""== CHARACTER YOU CONTROL ==""
  player_avatar_character_tag: ""PLAYER_AVATAR_CHARACTER""
  datee_character_tag: ""DATEE_CHARACTER""
";

        public const string RequiredParserBlocks = @"
active_trap_interest_penalty: -0.25
" + RequiredParserBlocksWithoutActiveTrapPenalty;
    }
}
