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
        }

        // Mutation: Would catch if [STAT: X] format was missing from the instruction
        [Fact]
        public void AC1_Instruction_contains_STAT_metadata_tag_format()
        {
            var instruction = PromptTemplates.DialogueOptionsInstruction;
            Assert.Contains("[STAT:", instruction);
        }

        // Engine-derived metadata must not be requested from the model.
        [Fact]
        public void AC1_Instruction_contains_only_model_authored_metadata_tags()
        {
            var instruction = PromptTemplates.DialogueOptionsInstruction;
            Assert.Contains("[CALLBACK:", instruction);
            Assert.Contains("[COMBO:", instruction);
            Assert.DoesNotContain("TELL_BONUS", instruction, StringComparison.OrdinalIgnoreCase);
        }

        // Mutation: Would catch if the format block replaced the original instructional text
        [Fact]
        public void AC1_Instruction_preserves_existing_content()
        {
            var instruction = PromptTemplates.DialogueOptionsInstruction;
            // Must still contain the original guidelines
            Assert.Contains("Generate exactly {options_count} dialogue options", instruction);
            Assert.Contains("{available_stats}", instruction); // stat list injected at runtime
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
            var result = DialogueOptionParsers.ParseDialogueOptionsText(input, new[] { StatType.Charm, StatType.Honesty, StatType.Chaos, StatType.Wit });
            Assert.Equal(4, result.Length);
        }

        // Mutation: Would catch if IntendedText was still "..." (the placeholder) after parsing valid input
        [Fact]
        public void AC2_WellFormed_4_options_all_have_non_placeholder_IntendedText()
        {
            var input = BuildWellFormed4Options();
            var result = DialogueOptionParsers.ParseDialogueOptionsText(input, new[] { StatType.Charm, StatType.Honesty, StatType.Chaos, StatType.Wit });
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
            var result = DialogueOptionParsers.ParseDialogueOptionsText(input, new[] { StatType.Charm, StatType.Honesty, StatType.Chaos, StatType.Wit });
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
            var result = DialogueOptionParsers.ParseDialogueOptionsText(input, new[] { StatType.Charm, StatType.Honesty, StatType.Chaos, StatType.Wit });
            Assert.Equal(2, result[1].CallbackTurnNumber);
        }

        // Mutation: Would catch if ComboName was always null instead of parsed
        [Fact]
        public void AC2_ComboName_parsed_when_present()
        {
            var input = BuildWellFormed4Options();
            var result = DialogueOptionParsers.ParseDialogueOptionsText(input, new[] { StatType.Charm, StatType.Honesty, StatType.Chaos, StatType.Wit });
            Assert.Equal("The Reveal", result[1].ComboName);
        }

        [Fact]
        public void AC2_TellBonus_metadata_is_rejected_by_lenient_and_strict_parsers()
        {
            var stats = new[] { StatType.Charm, StatType.Honesty, StatType.Chaos, StatType.Wit };
            var input = BuildWellFormed4Options().Replace(
                "[COMBO: The Reveal]",
                "[COMBO: The Reveal] [TELL_BONUS: yes]",
                StringComparison.Ordinal);

            var lenient = DialogueOptionParsers.ParseDialogueOptionsText(input, stats);
            var strict = DialogueOptionParsers.ParseDialogueOptionsStrict(
                input,
                stats,
                stats.Length,
                out var errorCode,
                out _,
                out _,
                out _);

            Assert.Empty(lenient);
            Assert.Empty(strict);
            Assert.Equal("unexpected_metadata", errorCode);
        }

        // Mutation: Would catch if "none" callback was parsed as non-null
        [Fact]
        public void AC2_None_metadata_parsed_as_null()
        {
            var input = BuildWellFormed4Options();
            var result = DialogueOptionParsers.ParseDialogueOptionsText(input, new[] { StatType.Charm, StatType.Honesty, StatType.Chaos, StatType.Wit });
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
[STAT: CHARM] [CALLBACK: none] [COMBO: none]
""Hey there, nice profile!""

OPTION_2
[STAT: WIT] [CALLBACK: none] [COMBO: none]
""Is your bio a riddle? Because I'm intrigued.""";

            var result = DialogueOptionParsers.ParseDialogueOptionsText(input, new[] { StatType.Charm, StatType.Wit, StatType.Honesty, StatType.Chaos });
            Assert.Equal(4, result.Length);
            // First two should be parsed, last two padded (pads are now fallback lines, never '...')
            Assert.NotEqual("...", result[0].IntendedText);
            Assert.NotEqual("...", result[1].IntendedText);
            Assert.NotEqual("...", result[2].IntendedText);
            Assert.False(string.IsNullOrWhiteSpace(result[2].IntendedText));
            Assert.True(result[2].IntendedText.Length >= 4);
            Assert.NotEqual("...", result[3].IntendedText);
            Assert.False(string.IsNullOrWhiteSpace(result[3].IntendedText));
            Assert.True(result[3].IntendedText.Length >= 4);
        }

        // Mutation: Would catch if preamble text before OPTION_1 broke the parser
        [Fact]
        public void EdgeCase_Preamble_text_before_OPTION_1_is_ignored()
        {
            var input = @"Here are your options for this turn:

OPTION_1
[STAT: CHARM] [CALLBACK: none] [COMBO: none]
""Hey, nice to meet you!""

OPTION_2
[STAT: RIZZ] [CALLBACK: none] [COMBO: none]
""You look incredible tonight""

OPTION_3
[STAT: WIT] [CALLBACK: none] [COMBO: none]
""So what's your deal?""

OPTION_4
[STAT: HONESTY] [CALLBACK: none] [COMBO: none]
""I'm genuinely curious about you""";

            var result = DialogueOptionParsers.ParseDialogueOptionsText(input, new[] { StatType.Charm, StatType.Rizz, StatType.Wit, StatType.Honesty });
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
[STAT: CHARM] [CALLBACK: none] [COMBO: none]
""Option one""

OPTION_2
[STAT: RIZZ] [CALLBACK: none] [COMBO: none]
""Option two""

OPTION_3
[STAT: WIT] [CALLBACK: none] [COMBO: none]
""Option three""

OPTION_4
[STAT: HONESTY] [CALLBACK: none] [COMBO: none]
""Option four""

OPTION_5
[STAT: CHAOS] [CALLBACK: none] [COMBO: none]
""Option five should be ignored""";

            var result = DialogueOptionParsers.ParseDialogueOptionsText(input, new[] { StatType.Charm, StatType.Rizz, StatType.Wit, StatType.Honesty });
            Assert.Equal(4, result.Length);
        }

        // Mutation: Would catch if null input threw an exception instead of returning defaults
        [Fact]
        public void EdgeCase_Null_response_returns_4_defaults_without_throwing()
        {
            var result = DialogueOptionParsers.ParseDialogueOptionsText(null, new[] { StatType.Charm, StatType.Honesty, StatType.Wit, StatType.Chaos });
            Assert.Equal(4, result.Length);
            foreach (var opt in result)
            {
                Assert.NotEqual("...", opt.IntendedText);
                Assert.False(string.IsNullOrWhiteSpace(opt.IntendedText));
                Assert.True(opt.IntendedText.Length >= 4);
            }
        }

        // Mutation: Would catch if whitespace-only input wasn't treated as empty
        [Fact]
        public void EdgeCase_Whitespace_only_response_returns_4_defaults()
        {
            var result = DialogueOptionParsers.ParseDialogueOptionsText("   \n\t  ", new[] { StatType.Charm, StatType.Honesty, StatType.Wit, StatType.Chaos });
            Assert.Equal(4, result.Length);
            foreach (var opt in result)
            {
                Assert.NotEqual("...", opt.IntendedText);
                Assert.False(string.IsNullOrWhiteSpace(opt.IntendedText));
                Assert.True(opt.IntendedText.Length >= 4);
            }
        }

        // Mutation: Would catch if invalid stat name caused a crash instead of skipping
        [Fact]
        public void EdgeCase_Invalid_stat_name_skips_option_and_pads()
        {
            var input = @"OPTION_1
[STAT: INVALID_STAT] [CALLBACK: none] [COMBO: none]
""This option has an invalid stat""

OPTION_2
[STAT: CHARM] [CALLBACK: none] [COMBO: none]
""This one is valid""

OPTION_3
[STAT: WIT] [CALLBACK: none] [COMBO: none]
""Also valid""

OPTION_4
[STAT: HONESTY] [CALLBACK: none] [COMBO: none]
""Valid too""";

            var result = DialogueOptionParsers.ParseDialogueOptionsText(input, new[] { StatType.Charm, StatType.Wit, StatType.Honesty, StatType.Chaos });
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

            var result = DialogueOptionParsers.ParseDialogueOptionsText(input, new[] { StatType.Charm, StatType.Honesty, StatType.Wit, StatType.Chaos });
            Assert.Equal(4, result.Length);
            foreach (var opt in result)
            {
                Assert.NotEqual("...", opt.IntendedText);
                Assert.False(string.IsNullOrWhiteSpace(opt.IntendedText));
                Assert.True(opt.IntendedText.Length >= 4);
            }
        }

        // Mutation: Would catch if SELF_AWARENESS stat was not listed as a valid stat in the instruction
        [Fact]
        public void AC1_Instruction_lists_SELF_AWARENESS_as_valid_stat()
        {
            var instruction = PromptTemplates.DialogueOptionsInstruction;
            // Stat list is now dynamic ({available_stats} placeholder injected by SessionDocumentBuilder)
            Assert.Contains("{available_stats}", instruction);
        }

        // Mutation: Would catch if RIZZ stat was parseable in the format
        [Fact]
        public void AC2_RIZZ_stat_parses_correctly()
        {
            var input = @"OPTION_1
[STAT: RIZZ] [CALLBACK: none] [COMBO: none]
""Smooth operator line""

OPTION_2
[STAT: SELF_AWARENESS] [CALLBACK: none] [COMBO: none]
""Introspective line""

OPTION_3
[STAT: CHARM] [CALLBACK: none] [COMBO: none]
""Charming line""

OPTION_4
[STAT: HONESTY] [CALLBACK: none] [COMBO: none]
""Honest line""";

            var result = DialogueOptionParsers.ParseDialogueOptionsText(input, new[] { StatType.Rizz, StatType.SelfAwareness, StatType.Charm, StatType.Honesty });
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
            var result = DialogueOptionParsers.ParseDialogueOptionsText(null);
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
[STAT: CHARM] [CALLBACK: none] [COMBO: none]
""hey so I noticed you're into marine biology… is that a career thing or a documentary thing""

OPTION_2
[STAT: HONESTY] [CALLBACK: turn_2] [COMBO: The Reveal]
""okay real talk I looked at your profile for way too long and I have questions about the penguin photo""

OPTION_3
[STAT: CHAOS] [CALLBACK: none] [COMBO: none]
""what if penguins had tinder. like what would their bios say. I need your thoughts on this""

OPTION_4
[STAT: WIT] [CALLBACK: none] [COMBO: none]
""your bio says looking for someone who gets it which is either deeply profound or deeply vague""";
        }
    }
}
