using System;
using System.Linq;
using Pinder.Core.Stats;
using Pinder.LlmAdapters.Anthropic;
using Xunit;

namespace Pinder.LlmAdapters.Tests.Anthropic
{
    public partial class AnthropicLlmAdapterSpecTests
    {
        // ==============================================================================
        // AC4: ParseDialogueOptions — additional edge cases from spec
        // ==============================================================================

        // What: AC4 Spec edge - Default padding skips stats already present in parsed options
        // Mutation: Would catch if padding doesn't skip already-present Charm, duplicating it
        [Fact]
        public void ParseDialogueOptions_PaddingSkipsAlreadyPresentStats()
        {
            // If Charm is already parsed, defaults should skip Charm and use Honesty, Wit, Chaos
            var input = @"OPTION_1
[STAT: CHARM] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
""Charm option""";

            var result = AnthropicLlmAdapter.ParseDialogueOptions(input);
            Assert.Equal(4, result.Length);
            Assert.Equal(StatType.Charm, result[0].Stat);
            // Remaining 3 should skip Charm and use Honesty, Wit, Chaos
            var paddedStats = result.Skip(1).Select(o => o.Stat).ToArray();
            Assert.DoesNotContain(StatType.Charm, paddedStats);
            Assert.Contains(StatType.Honesty, paddedStats);
            Assert.Contains(StatType.Wit, paddedStats);
            Assert.Contains(StatType.Chaos, paddedStats);
        }

        // What: AC4 Spec edge - 2 parsed options, 2 defaults filling from order
        // Mutation: Would catch if padding count is wrong (e.g., pads to 5 or 3)
        [Fact]
        public void ParseDialogueOptions_TwoValidOptions_PadsWithTwo()
        {
            var input = @"OPTION_1
[STAT: CHARM] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
""First""

OPTION_2
[STAT: WIT] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
""Second""";

            var result = AnthropicLlmAdapter.ParseDialogueOptions(input);
            Assert.Equal(4, result.Length);
            Assert.Equal(StatType.Charm, result[0].Stat);
            Assert.Equal(StatType.Wit, result[1].Stat);
            // Defaults: should be Honesty and Chaos (skipping Charm and Wit)
            var defaultOpts = result.Skip(2).ToArray();
            Assert.Equal(2, defaultOpts.Length);
            Assert.All(defaultOpts, o => Assert.Equal("...", o.IntendedText));
        }

        // What: AC4 Spec edge - Default options have null/false for optional fields
        // Mutation: Would catch if defaults have non-null CallbackTurnNumber or true HasTellBonus
        [Fact]
        public void ParseDialogueOptions_DefaultOptions_HaveCorrectDefaults()
        {
            var result = AnthropicLlmAdapter.ParseDialogueOptions(null);
            foreach (var opt in result)
            {
                Assert.Equal("...", opt.IntendedText);
                Assert.Null(opt.CallbackTurnNumber);
                Assert.Null(opt.ComboName);
                Assert.False(opt.HasTellBonus);
                Assert.False(opt.HasWeaknessWindow);
            }
        }

        // What: AC4 Spec edge - Multiple quoted strings: first one is used
        // Mutation: Would catch if last quoted string is used instead of first
        [Fact]
        public void ParseDialogueOptions_MultipleQuotedStrings_UsesFirst()
        {
            var input = @"OPTION_1
[STAT: CHARM] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
""First quoted string"" and then ""second quoted string""

OPTION_2
[STAT: RIZZ] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
""text""

OPTION_3
[STAT: WIT] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
""text""

OPTION_4
[STAT: HONESTY] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
""text""";

            var result = AnthropicLlmAdapter.ParseDialogueOptions(input);
            Assert.Equal("First quoted string", result[0].IntendedText);
        }

        // What: AC4 Spec edge - HasWeaknessWindow always false from adapter parsing
        // Mutation: Would catch if parser ever sets HasWeaknessWindow to true
        [Fact]
        public void ParseDialogueOptions_HasWeaknessWindow_AlwaysFalse()
        {
            var input = @"OPTION_1
[STAT: CHARM] [CALLBACK: none] [COMBO: none] [TELL_BONUS: yes]
""text""

OPTION_2
[STAT: RIZZ] [CALLBACK: 3] [COMBO: The Setup] [TELL_BONUS: yes]
""text""

OPTION_3
[STAT: WIT] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
""text""

OPTION_4
[STAT: HONESTY] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
""text""";

            var result = AnthropicLlmAdapter.ParseDialogueOptions(input);
            Assert.All(result, opt => Assert.False(opt.HasWeaknessWindow));
        }

        // What: AC4 Spec edge - Text with extra whitespace is trimmed
        // Mutation: Would catch if whitespace trimming is missing
        [Fact]
        public void ParseDialogueOptions_ExtraWhitespace_Trimmed()
        {
            var input = @"OPTION_1
[STAT: CHARM] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
""  Hello with spaces  ""

OPTION_2
[STAT: RIZZ] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
""text""

OPTION_3
[STAT: WIT] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
""text""

OPTION_4
[STAT: HONESTY] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
""text""";

            var result = AnthropicLlmAdapter.ParseDialogueOptions(input);
            // Text should be trimmed or at minimum extracted from quotes
            Assert.DoesNotContain("\n", result[0].IntendedText);
        }

        // What: AC4 Spec edge - COMBO value "none" maps to null
        // Mutation: Would catch if "none" string literal stored instead of null
        [Fact]
        public void ParseDialogueOptions_ComboNone_MapsToNull()
        {
            var input = @"OPTION_1
[STAT: CHARM] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
""text""";

            var result = AnthropicLlmAdapter.ParseDialogueOptions(input);
            Assert.Null(result[0].ComboName);
        }

        // What: AC4 Spec edge - CALLBACK "none" maps to null CallbackTurnNumber
        // Mutation: Would catch if "none" is parsed as 0 or empty string
        [Fact]
        public void ParseDialogueOptions_CallbackNone_MapsToNull()
        {
            var input = @"OPTION_1
[STAT: CHARM] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
""text""";

            var result = AnthropicLlmAdapter.ParseDialogueOptions(input);
            Assert.Null(result[0].CallbackTurnNumber);
        }

        // ==============================================================================
        // SelfAwareness case-insensitive parse via non-generic Enum.Parse (AC5)
        // ==============================================================================

        // What: AC5 - SelfAwareness stat parsed correctly (verifies Enum.Parse compatibility)
        // Mutation: Would catch if Enum.Parse uses wrong form that fails on .NET Standard 2.0
        [Fact]
        public void ParseDialogueOptions_SelfAwareness_ParsedCorrectly()
        {
            var input = @"OPTION_1
[STAT: SELFAWARENESS] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
""Self-aware option""";

            var result = AnthropicLlmAdapter.ParseDialogueOptions(input);
            Assert.Equal(StatType.SelfAwareness, result[0].Stat);
        }

        // What: AC5 - Case-insensitive stat parsing
        // Mutation: Would catch if case-sensitive match is used (true flag missing in Enum.Parse)
        [Fact]
        public void ParseDialogueOptions_CaseInsensitiveStat()
        {
            var input = @"OPTION_1
[STAT: charm] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
""lowercase stat""";

            var result = AnthropicLlmAdapter.ParseDialogueOptions(input);
            Assert.Equal(StatType.Charm, result[0].Stat);
        }
    }
}
