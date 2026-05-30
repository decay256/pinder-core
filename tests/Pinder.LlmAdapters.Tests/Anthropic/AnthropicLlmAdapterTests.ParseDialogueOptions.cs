using System;
using Pinder.Core.Conversation;
using Pinder.Core.Stats;
using Pinder.LlmAdapters.Anthropic;
using Xunit;

namespace Pinder.LlmAdapters.Tests.Anthropic
{
    public partial class AnthropicLlmAdapterTests
    {
        [Fact]
        public void Null_input_returns_4_defaults()
        {
            var result = AnthropicLlmAdapter.ParseDialogueOptions(null);
            Assert.Equal(4, result.Length);
            Assert.Equal(StatType.Charm, result[0].Stat);
            Assert.Equal(StatType.Honesty, result[1].Stat);
            Assert.Equal(StatType.Wit, result[2].Stat);
            Assert.Equal(StatType.Chaos, result[3].Stat);
            foreach (var opt in result) Assert.Equal("...", opt.IntendedText);
        }

        [Fact]
        public void Empty_string_returns_4_defaults()
        {
            var result = AnthropicLlmAdapter.ParseDialogueOptions("");
            Assert.Equal(4, result.Length);
            foreach (var opt in result) Assert.Equal("...", opt.IntendedText);
        }

        [Fact]
        public void Parses_4_valid_options()
        {
            var input = @"OPTION_1
[STAT: CHARM] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
""Thanks! You're not so bad yourself.""

OPTION_2
[STAT: RIZZ] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
""Oh, you haven't even seen my best angle yet""

OPTION_3
[STAT: WIT] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
""Bold opening. I like that in a match.""

OPTION_4
[STAT: HONESTY] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
""I appreciate the compliment. Honestly I spent way too long picking that photo.""";

            var result = AnthropicLlmAdapter.ParseDialogueOptions(input);
            Assert.Equal(4, result.Length);
            Assert.Equal(StatType.Charm, result[0].Stat);
            Assert.Equal(StatType.Rizz, result[1].Stat);
            Assert.Equal(StatType.Wit, result[2].Stat);
            Assert.Equal(StatType.Honesty, result[3].Stat);
            Assert.Contains("not so bad", result[0].IntendedText);
        }

        [Fact]
        public void Parses_callback_and_combo_metadata()
        {
            var input = @"OPTION_1
[STAT: WIT] [CALLBACK: 3] [COMBO: The Setup] [TELL_BONUS: yes]
""Speaking of pizza, remember that crime?""";

            var result = AnthropicLlmAdapter.ParseDialogueOptions(input);
            Assert.Equal(StatType.Wit, result[0].Stat);
            Assert.Equal(3, result[0].CallbackTurnNumber);
            Assert.Equal("The Setup", result[0].ComboName);
            Assert.True(result[0].HasTellBonus);
        }

        [Fact]
        public void Callback_turn_N_format_parsed()
        {
            var input = @"OPTION_1
[STAT: CHARM] [CALLBACK: turn_5] [COMBO: none] [TELL_BONUS: no]
""Some text here""";
            var result = AnthropicLlmAdapter.ParseDialogueOptions(input);
            Assert.Equal(5, result[0].CallbackTurnNumber);
        }

        [Fact]
        public void NonNumeric_callback_returns_null_turn()
        {
            var input = @"OPTION_1
[STAT: CHARM] [CALLBACK: pizza_story] [COMBO: none] [TELL_BONUS: no]
""Some text here""";
            var result = AnthropicLlmAdapter.ParseDialogueOptions(input);
            Assert.Null(result[0].CallbackTurnNumber);
        }

        [Fact]
        public void One_valid_option_padded_to_4()
        {
            var input = @"OPTION_1
[STAT: RIZZ] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
""Just one option""";

            var result = AnthropicLlmAdapter.ParseDialogueOptions(input);
            Assert.Equal(4, result.Length);
            Assert.Equal(StatType.Rizz, result[0].Stat);
            Assert.Equal("Just one option", result[0].IntendedText);
            // Padding skips stats already present — Rizz is not in defaults so all 3 defaults used
            Assert.Equal(StatType.Charm, result[1].Stat);
            Assert.Equal("...", result[1].IntendedText);
        }

        [Fact]
        public void Five_options_truncated_to_4()
        {
            var input = @"OPTION_1
[STAT: CHARM] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
""One""
OPTION_2
[STAT: RIZZ] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
""Two""
OPTION_3
[STAT: WIT] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
""Three""
OPTION_4
[STAT: HONESTY] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
""Four""
OPTION_5
[STAT: CHAOS] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
""Five""";

            var result = AnthropicLlmAdapter.ParseDialogueOptions(input);
            Assert.Equal(4, result.Length);
            Assert.Equal("One", result[0].IntendedText);
            Assert.Equal("Four", result[3].IntendedText);
        }

        [Fact]
        public void Invalid_stat_skipped_and_padded()
        {
            var input = @"OPTION_1
[STAT: INVALID_STAT] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
""Bad stat option""
OPTION_2
[STAT: CHARM] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
""Good option""";

            var result = AnthropicLlmAdapter.ParseDialogueOptions(input);
            Assert.Equal(4, result.Length);
            Assert.Equal(StatType.Charm, result[0].Stat);
            Assert.Equal("Good option", result[0].IntendedText);
        }

        [Fact]
        public void SelfAwareness_stat_parsed_from_SELF_AWARENESS()
        {
            var input = @"OPTION_1
[STAT: SELF_AWARENESS] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
""I know this is awkward but...""";

            var result = AnthropicLlmAdapter.ParseDialogueOptions(input);
            Assert.Equal(StatType.SelfAwareness, result[0].Stat);
        }

        [Fact]
        public void Missing_quoted_text_skips_option()
        {
            var input = @"OPTION_1
[STAT: CHARM] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
no quotes here
OPTION_2
[STAT: RIZZ] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
""Valid text""";

            var result = AnthropicLlmAdapter.ParseDialogueOptions(input);
            Assert.Equal(4, result.Length);
            Assert.Equal(StatType.Rizz, result[0].Stat);
        }

        [Fact]
        public void HasWeaknessWindow_always_false()
        {
            var input = @"OPTION_1
[STAT: CHARM] [CALLBACK: none] [COMBO: none] [TELL_BONUS: yes]
""Some text""";

            var result = AnthropicLlmAdapter.ParseDialogueOptions(input);
            Assert.False(result[0].HasWeaknessWindow);
        }

        [Fact]
        public void Garbage_input_returns_4_defaults()
        {
            var result = AnthropicLlmAdapter.ParseDialogueOptions("just some random text\nwith lines");
            Assert.Equal(4, result.Length);
            foreach (var opt in result) Assert.Equal("...", opt.IntendedText);
        }

        [Fact]
        public void Instruction_contains_OPTION_headers()
        {
            Assert.Contains("OPTION_1", PromptTemplates.DialogueOptionsInstruction);
            Assert.Contains("OPTION_2", PromptTemplates.DialogueOptionsInstruction);
        }

        [Fact]
        public void Instruction_contains_output_format_rules()
        {
            Assert.Contains("STAT must be one of", PromptTemplates.DialogueOptionsInstruction);
            Assert.Contains("{available_stats}", PromptTemplates.DialogueOptionsInstruction); // stat list injected at runtime
            Assert.Contains("double quotes", PromptTemplates.DialogueOptionsInstruction);
            Assert.Contains("No extra text before OPTION_1", PromptTemplates.DialogueOptionsInstruction);
        }

        [Fact]
        public void Instruction_preserves_original_guidelines()
        {
            // Verify original instructional content was not removed
            Assert.Contains("Generate exactly {options_count} dialogue options", PromptTemplates.DialogueOptionsInstruction);
            Assert.Contains("{available_stats}", PromptTemplates.DialogueOptionsInstruction); // stat list is now injected at runtime
            Assert.Contains("Keep options concise", PromptTemplates.DialogueOptionsInstruction);
        }

        [Fact]
        public void WellFormed_output_matching_instruction_format_parses_correctly()
        {
            // Simulate LLM output that follows the format described in the instruction
            var llmOutput = @"OPTION_1
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

            var result = AnthropicLlmAdapter.ParseDialogueOptions(llmOutput);

            Assert.Equal(4, result.Length);

            // All options have non-empty IntendedText (not "...")
            foreach (var opt in result)
            {
                Assert.NotEqual("...", opt.IntendedText);
                Assert.False(string.IsNullOrWhiteSpace(opt.IntendedText));
            }

            // Correct stats
            Assert.Equal(StatType.Charm, result[0].Stat);
            Assert.Equal(StatType.Honesty, result[1].Stat);
            Assert.Equal(StatType.Chaos, result[2].Stat);
            Assert.Equal(StatType.Wit, result[3].Stat);

            // Metadata on option 2
            Assert.Equal(2, result[1].CallbackTurnNumber);
            Assert.Equal("The Reveal", result[1].ComboName);
            Assert.True(result[1].HasTellBonus);

            // Option 1 has no callback/combo/tell
            Assert.Null(result[0].CallbackTurnNumber);
            Assert.Null(result[0].ComboName);
            Assert.False(result[0].HasTellBonus);
        }
    }
}
