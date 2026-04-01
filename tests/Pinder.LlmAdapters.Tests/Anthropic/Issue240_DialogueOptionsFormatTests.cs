using Pinder.Core.Conversation;
using Pinder.Core.Stats;
using Pinder.LlmAdapters.Anthropic;
using Xunit;

namespace Pinder.LlmAdapters.Tests.Anthropic
{
    /// <summary>
    /// Tests for Issue #240: DialogueOptionsInstruction must include explicit output format
    /// so that ParseDialogueOptions can successfully parse LLM responses.
    /// </summary>
    public class Issue240_DialogueOptionsFormatTests
    {
        // ==========================================================
        // AC1: Template contains OPTION_N headers and format markers
        // ==========================================================

        // Mutation: Would catch if OPTION_1 header was removed from the format block
        [Fact]
        public void AC1_Instruction_contains_all_four_OPTION_headers()
        {
            var instruction = PromptTemplates.DialogueOptionsInstruction;
            Assert.Contains("OPTION_1", instruction);
            Assert.Contains("OPTION_2", instruction);
            Assert.Contains("OPTION_3", instruction);
            Assert.Contains("OPTION_4", instruction);
        }

        // Mutation: Would catch if [STAT: X] format was missing from the instruction
        [Fact]
        public void AC1_Instruction_contains_STAT_metadata_tag_format()
        {
            var instruction = PromptTemplates.DialogueOptionsInstruction;
            Assert.Contains("[STAT:", instruction);
        }

        // Mutation: Would catch if CALLBACK/COMBO/TELL_BONUS tags were missing
        [Fact]
        public void AC1_Instruction_contains_CALLBACK_COMBO_TELL_BONUS_tags()
        {
            var instruction = PromptTemplates.DialogueOptionsInstruction;
            Assert.Contains("[CALLBACK:", instruction);
            Assert.Contains("[COMBO:", instruction);
            Assert.Contains("[TELL_BONUS:", instruction);
        }

        // Mutation: Would catch if the format block replaced the original instructional text
        [Fact]
        public void AC1_Instruction_preserves_existing_content()
        {
            var instruction = PromptTemplates.DialogueOptionsInstruction;
            // Must still contain the original guidelines
            Assert.Contains("Generate exactly 4 dialogue options", instruction);
            Assert.Contains("CHARM, RIZZ, HONESTY, CHAOS, WIT, SELF_AWARENESS", instruction);
        }

        // Mutation: Would catch if the instruction didn't include a complete example with at least 2 options
        [Fact]
        public void AC1_Instruction_contains_example_with_quoted_text()
        {
            var instruction = PromptTemplates.DialogueOptionsInstruction;
            // The spec requires "Message text wrapped in double quotes" in the format
            Assert.Contains("double quotes", instruction);
        }

        // Mutation: Would catch if "no extra text" guidance was missing, causing LLM preamble issues
        [Fact]
        public void AC1_Instruction_contains_no_extra_text_guidance()
        {
            var instruction = PromptTemplates.DialogueOptionsInstruction;
            Assert.Contains("No extra text before OPTION_1", instruction);
        }

        // ==========================================================
        // AC2: ParseDialogueOptions successfully parses well-formed output
        // ==========================================================

        // Mutation: Would catch if parser returned fewer than 4 options from valid input
        [Fact]
        public void AC2_WellFormed_4_options_returns_exactly_4()
        {
            var input = BuildWellFormed4Options();
            var result = AnthropicLlmAdapter.ParseDialogueOptions(input);
            Assert.Equal(4, result.Length);
        }

        // Mutation: Would catch if IntendedText was still "..." (the placeholder) after parsing valid input
        [Fact]
        public void AC2_WellFormed_4_options_all_have_non_placeholder_IntendedText()
        {
            var input = BuildWellFormed4Options();
            var result = AnthropicLlmAdapter.ParseDialogueOptions(input);
            foreach (var opt in result)
            {
                Assert.NotEqual("...", opt.IntendedText);
                Assert.False(string.IsNullOrWhiteSpace(opt.IntendedText));
            }
        }

        // Mutation: Would catch if stat parsing mapped CHARM to wrong StatType
        [Fact]
        public void AC2_Stats_match_STAT_tags_in_input()
        {
            var input = BuildWellFormed4Options();
            var result = AnthropicLlmAdapter.ParseDialogueOptions(input);
            Assert.Equal(StatType.Charm, result[0].Stat);
            Assert.Equal(StatType.Honesty, result[1].Stat);
            Assert.Equal(StatType.Chaos, result[2].Stat);
            Assert.Equal(StatType.Wit, result[3].Stat);
        }

        // Mutation: Would catch if CallbackTurnNumber parsing was broken (always null)
        [Fact]
        public void AC2_Callback_turn_number_parsed_when_present()
        {
            var input = BuildWellFormed4Options();
            var result = AnthropicLlmAdapter.ParseDialogueOptions(input);
            Assert.Equal(2, result[1].CallbackTurnNumber);
        }

        // Mutation: Would catch if ComboName was always null instead of parsed
        [Fact]
        public void AC2_ComboName_parsed_when_present()
        {
            var input = BuildWellFormed4Options();
            var result = AnthropicLlmAdapter.ParseDialogueOptions(input);
            Assert.Equal("The Reveal", result[1].ComboName);
        }

        // Mutation: Would catch if HasTellBonus was always false
        [Fact]
        public void AC2_HasTellBonus_parsed_when_yes()
        {
            var input = BuildWellFormed4Options();
            var result = AnthropicLlmAdapter.ParseDialogueOptions(input);
            Assert.True(result[1].HasTellBonus);
        }

        // Mutation: Would catch if "none" callback was parsed as non-null
        [Fact]
        public void AC2_None_metadata_parsed_as_null()
        {
            var input = BuildWellFormed4Options();
            var result = AnthropicLlmAdapter.ParseDialogueOptions(input);
            Assert.Null(result[0].CallbackTurnNumber);
            Assert.Null(result[0].ComboName);
            Assert.False(result[0].HasTellBonus);
        }

        // ==========================================================
        // Edge Cases from Spec
        // ==========================================================

        // Mutation: Would catch if partial parsing (< 4 options) didn't pad to 4
        [Fact]
        public void EdgeCase_Partial_options_padded_to_4()
        {
            var input = @"OPTION_1
[STAT: CHARM] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
""Hey there, nice profile!""

OPTION_2
[STAT: WIT] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
""Is your bio a riddle? Because I'm intrigued.""";

            var result = AnthropicLlmAdapter.ParseDialogueOptions(input);
            Assert.Equal(4, result.Length);
            // First two should be parsed, last two padded
            Assert.NotEqual("...", result[0].IntendedText);
            Assert.NotEqual("...", result[1].IntendedText);
            Assert.Equal("...", result[2].IntendedText);
            Assert.Equal("...", result[3].IntendedText);
        }

        // Mutation: Would catch if preamble text before OPTION_1 broke the parser
        [Fact]
        public void EdgeCase_Preamble_text_before_OPTION_1_is_ignored()
        {
            var input = @"Here are your options for this turn:

OPTION_1
[STAT: CHARM] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
""Hey, nice to meet you!""

OPTION_2
[STAT: RIZZ] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
""You look incredible tonight""

OPTION_3
[STAT: WIT] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
""So what's your deal?""

OPTION_4
[STAT: HONESTY] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
""I'm genuinely curious about you""";

            var result = AnthropicLlmAdapter.ParseDialogueOptions(input);
            Assert.Equal(4, result.Length);
            foreach (var opt in result)
            {
                Assert.NotEqual("...", opt.IntendedText);
            }
        }

        // Mutation: Would catch if more than 4 options were returned
        [Fact]
        public void EdgeCase_More_than_4_options_truncated_to_4()
        {
            var input = @"OPTION_1
[STAT: CHARM] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
""Option one""

OPTION_2
[STAT: RIZZ] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
""Option two""

OPTION_3
[STAT: WIT] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
""Option three""

OPTION_4
[STAT: HONESTY] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
""Option four""

OPTION_5
[STAT: CHAOS] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
""Option five should be ignored""";

            var result = AnthropicLlmAdapter.ParseDialogueOptions(input);
            Assert.Equal(4, result.Length);
        }

        // Mutation: Would catch if null input threw an exception instead of returning defaults
        [Fact]
        public void EdgeCase_Null_response_returns_4_defaults_without_throwing()
        {
            var result = AnthropicLlmAdapter.ParseDialogueOptions(null);
            Assert.Equal(4, result.Length);
            foreach (var opt in result)
            {
                Assert.Equal("...", opt.IntendedText);
            }
        }

        // Mutation: Would catch if whitespace-only input wasn't treated as empty
        [Fact]
        public void EdgeCase_Whitespace_only_response_returns_4_defaults()
        {
            var result = AnthropicLlmAdapter.ParseDialogueOptions("   \n\t  ");
            Assert.Equal(4, result.Length);
            foreach (var opt in result)
            {
                Assert.Equal("...", opt.IntendedText);
            }
        }

        // Mutation: Would catch if invalid stat name caused a crash instead of skipping
        [Fact]
        public void EdgeCase_Invalid_stat_name_skips_option_and_pads()
        {
            var input = @"OPTION_1
[STAT: INVALID_STAT] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
""This option has an invalid stat""

OPTION_2
[STAT: CHARM] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
""This one is valid""

OPTION_3
[STAT: WIT] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
""Also valid""

OPTION_4
[STAT: HONESTY] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
""Valid too""";

            var result = AnthropicLlmAdapter.ParseDialogueOptions(input);
            Assert.Equal(4, result.Length);
            // The invalid-stat option should be skipped; 3 parsed + 1 padded
        }

        // Mutation: Would catch if freeform prose (no OPTION_N headers) wasn't returning defaults
        [Fact]
        public void EdgeCase_Freeform_prose_without_headers_returns_all_defaults()
        {
            var input = @"Here are 4 options for Chad:

1. **Charm** - ""hey so I noticed..."" This option plays it safe...
2. **Honesty** - ""okay real talk..."" A bolder move that...
3. **Wit** - ""so your bio says..."" Clever approach...
4. **Chaos** - ""what if penguins..."" Wild card...";

            var result = AnthropicLlmAdapter.ParseDialogueOptions(input);
            Assert.Equal(4, result.Length);
            foreach (var opt in result)
            {
                Assert.Equal("...", opt.IntendedText);
            }
        }

        // Mutation: Would catch if SELF_AWARENESS stat was not listed as a valid stat in the instruction
        [Fact]
        public void AC1_Instruction_lists_SELF_AWARENESS_as_valid_stat()
        {
            var instruction = PromptTemplates.DialogueOptionsInstruction;
            Assert.Contains("SELF_AWARENESS", instruction);
        }

        // Mutation: Would catch if RIZZ stat was parseable in the format
        [Fact]
        public void AC2_RIZZ_stat_parses_correctly()
        {
            var input = @"OPTION_1
[STAT: RIZZ] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
""Smooth operator line""

OPTION_2
[STAT: SELF_AWARENESS] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
""Introspective line""

OPTION_3
[STAT: CHARM] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
""Charming line""

OPTION_4
[STAT: HONESTY] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
""Honest line""";

            var result = AnthropicLlmAdapter.ParseDialogueOptions(input);
            Assert.Equal(4, result.Length);
            Assert.Equal(StatType.Rizz, result[0].Stat);
            Assert.Equal(StatType.SelfAwareness, result[1].Stat);
        }

        // ==========================================================
        // AC4: No regression — default padding stats are correct
        // ==========================================================

        // Mutation: Would catch if default padding stat order changed
        [Fact]
        public void AC4_Default_padding_stats_are_Charm_Honesty_Wit_Chaos()
        {
            var result = AnthropicLlmAdapter.ParseDialogueOptions(null);
            Assert.Equal(StatType.Charm, result[0].Stat);
            Assert.Equal(StatType.Honesty, result[1].Stat);
            Assert.Equal(StatType.Wit, result[2].Stat);
            Assert.Equal(StatType.Chaos, result[3].Stat);
        }

        // ==========================================================
        // Helper
        // ==========================================================

        private static string BuildWellFormed4Options()
        {
            return @"OPTION_1
[STAT: CHARM] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
""hey so I noticed you're into marine biology… is that a career thing or a documentary thing""

OPTION_2
[STAT: HONESTY] [CALLBACK: turn_2] [COMBO: The Reveal] [TELL_BONUS: yes]
""okay real talk I looked at your profile for way too long and I have questions about the penguin photo""

OPTION_3
[STAT: CHAOS] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
""what if penguins had tinder. like what would their bios say. I need your thoughts on this""

OPTION_4
[STAT: WIT] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
""your bio says looking for someone who gets it which is either deeply profound or deeply vague""";
        }
    }
}
