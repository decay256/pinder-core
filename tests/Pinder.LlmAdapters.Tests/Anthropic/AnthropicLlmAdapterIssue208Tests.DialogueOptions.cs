using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Pinder.Core.Conversation;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;
using Pinder.LlmAdapters.Anthropic;
using Pinder.LlmAdapters.Anthropic.Dto;
using Xunit;

namespace Pinder.LlmAdapters.Tests.Anthropic
{
    public partial class AnthropicLlmAdapterIssue208Tests
    {
        // ======================================================================
        // AC2: cache_control: ephemeral on system blocks
        // ======================================================================

        // What: AC2 — GetDialogueOptionsAsync system blocks have cache_control ephemeral
        // Mutation: Would catch if cache_control is omitted from system blocks
        [Fact]
        public async Task AC2_DialogueOptions_SystemBlocks_HaveCacheControl()
        {
            var handler = new MockHttpHandler
            {
                ResponseBody = MakeApiResponse(@"OPTION_1
[STAT: CHARM] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
""Hello there""")
            };
            using var client = new HttpClient(handler);
            using var adapter = new AnthropicLlmAdapter(DefaultOptions(), client);

            await adapter.GetDialogueOptionsAsync(MakeDialogueContext());

            var body = JObject.Parse(handler.LastRequestBody!);
            var system = body["system"] as JArray;
            Assert.NotNull(system);
            // Must have at least one block with cache_control
            var cacheBlocks = system!.Where(s => s["cache_control"] != null).ToList();
            Assert.True(cacheBlocks.Count >= 1, "Expected system blocks with cache_control");
            foreach (var block in cacheBlocks)
            {
                Assert.Equal("ephemeral", block["cache_control"]!["type"]!.ToString());
            }
        }

        // What: AC2 — Both player and opponent prompts in system for dialogue options
        // Mutation: Would catch if opponent prompt leaks into system blocks (voice bleed fix #487)
        [Fact]
        public async Task AC2_DialogueOptions_SystemBlocks_ContainPlayerOnly()
        {
            var handler = new MockHttpHandler
            {
                ResponseBody = MakeApiResponse(@"OPTION_1
[STAT: CHARM] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
""Hello""")
            };
            using var client = new HttpClient(handler);
            using var adapter = new AnthropicLlmAdapter(DefaultOptions(), client);

            await adapter.GetDialogueOptionsAsync(MakeDialogueContext());

            var body = JObject.Parse(handler.LastRequestBody!);
            var system = body["system"] as JArray;
            Assert.NotNull(system);
            // Only player prompt in system — opponent is in user message
            Assert.Single(system!);
            var systemText = string.Join(" ", system!.Select(s => s["text"]?.ToString() ?? ""));
            Assert.Contains("Thundercock", systemText);
            Assert.DoesNotContain("Velvet", systemText);
            // Opponent profile is in user message instead
            var messages = body["messages"] as JArray;
            Assert.NotNull(messages);
            var userContent = messages![0]["content"]?.ToString() ?? "";
            Assert.Contains("Velvet", userContent);
            Assert.Contains("YOU ARE TALKING TO", userContent);
        }

        // ======================================================================
        // AC4: ParseDialogueOptions falls back gracefully (never throws)
        // ======================================================================

        // What: AC4 — null input returns 4 defaults
        // Mutation: Would catch if null input throws instead of returning defaults
        [Fact]
        public void AC4_ParseDialogueOptions_Null_Returns4Defaults()
        {
            var result = AnthropicLlmAdapter.ParseDialogueOptions(null!);
            Assert.Equal(4, result.Length);
            // Default stats order: Charm, Honesty, Wit, Chaos
            Assert.Equal(StatType.Charm, result[0].Stat);
            Assert.Equal(StatType.Honesty, result[1].Stat);
            Assert.Equal(StatType.Wit, result[2].Stat);
            Assert.Equal(StatType.Chaos, result[3].Stat);
            Assert.All(result, o => Assert.Equal("...", o.IntendedText));
        }

        // What: AC4 — empty input returns 4 defaults
        // Mutation: Would catch if empty string is not handled as invalid
        [Fact]
        public void AC4_ParseDialogueOptions_Empty_Returns4Defaults()
        {
            var result = AnthropicLlmAdapter.ParseDialogueOptions("");
            Assert.Equal(4, result.Length);
            Assert.All(result, o => Assert.Equal("...", o.IntendedText));
        }

        // What: AC4 — garbage input returns 4 defaults without throwing
        // Mutation: Would catch if parse exception propagates instead of being caught
        [Fact]
        public void AC4_ParseDialogueOptions_Garbage_Returns4Defaults()
        {
            var result = AnthropicLlmAdapter.ParseDialogueOptions("!@#$%^&*() completely random gibberish\n\n");
            Assert.Equal(4, result.Length);
        }

        // What: AC4 — 1 valid option padded to 4
        // Mutation: Would catch if padding logic doesn't fill to exactly 4
        [Fact]
        public void AC4_ParseDialogueOptions_OneValid_PaddedTo4()
        {
            var input = @"OPTION_1
[STAT: RIZZ] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
""Smooth line here""";

            var result = AnthropicLlmAdapter.ParseDialogueOptions(input);
            Assert.Equal(4, result.Length);
            Assert.Equal(StatType.Rizz, result[0].Stat);
            Assert.Equal("Smooth line here", result[0].IntendedText);
            // Remaining 3 are defaults — padded from {Charm, Honesty, Wit, Chaos} skipping Rizz
        }

        // What: AC4 — 5+ options truncated to 4
        // Mutation: Would catch if truncation doesn't happen, returning more than 4
        [Fact]
        public void AC4_ParseDialogueOptions_FivePlus_TruncatedTo4()
        {
            var input = @"OPTION_1
[STAT: CHARM] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
""Line 1""

OPTION_2
[STAT: RIZZ] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
""Line 2""

OPTION_3
[STAT: WIT] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
""Line 3""

OPTION_4
[STAT: HONESTY] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
""Line 4""

OPTION_5
[STAT: CHAOS] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
""Line 5""";

            var result = AnthropicLlmAdapter.ParseDialogueOptions(input);
            Assert.Equal(4, result.Length);
        }

        // What: AC4 — invalid stat name is skipped and padded
        // Mutation: Would catch if invalid enum name causes exception instead of skip
        [Fact]
        public void AC4_ParseDialogueOptions_InvalidStat_Skipped()
        {
            var input = @"OPTION_1
[STAT: INVALID_STAT] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
""Bad stat line""

OPTION_2
[STAT: CHARM] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
""Good line""";

            var result = AnthropicLlmAdapter.ParseDialogueOptions(input);
            Assert.Equal(4, result.Length);
            // First parsed valid is Charm
            Assert.Equal(StatType.Charm, result[0].Stat);
            Assert.Equal("Good line", result[0].IntendedText);
        }

        // What: AC4 — missing quoted text skips option
        // Mutation: Would catch if option without quoted text is accepted with empty/null text
        [Fact]
        public void AC4_ParseDialogueOptions_MissingQuotedText_Skipped()
        {
            var input = @"OPTION_1
[STAT: CHARM] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
no quotes here at all";

            var result = AnthropicLlmAdapter.ParseDialogueOptions(input);
            Assert.Equal(4, result.Length);
            // Should be all defaults since the one option has no quoted text
        }

        // ======================================================================
        // ParseDialogueOptions — metadata parsing edge cases
        // ======================================================================

        // What: Spec edge case — [TELL_BONUS: yes] maps to true
        // Mutation: Would catch if tell bonus parsing doesn't check for "yes" specifically
        [Fact]
        public void ParseDialogueOptions_TellBonusYes_MapsToTrue()
        {
            var input = @"OPTION_1
[STAT: CHARM] [CALLBACK: none] [COMBO: none] [TELL_BONUS: yes]
""Tell bonus line""";

            var result = AnthropicLlmAdapter.ParseDialogueOptions(input);
            Assert.True(result[0].HasTellBonus);
        }

        // What: Spec edge case — [TELL_BONUS: anything_else] maps to false
        // Mutation: Would catch if any non-empty value is treated as true
        [Fact]
        public void ParseDialogueOptions_TellBonusAnythingElse_MapsToFalse()
        {
            var input = @"OPTION_1
[STAT: CHARM] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
""No tell bonus""";

            var result = AnthropicLlmAdapter.ParseDialogueOptions(input);
            Assert.False(result[0].HasTellBonus);
        }

        // What: Spec edge case — [TELL_BONUS: maybe] is not yes, maps to false
        // Mutation: Would catch if parser uses Contains("yes") instead of exact match
        [Fact]
        public void ParseDialogueOptions_TellBonusMaybe_MapsToFalse()
        {
            var input = @"OPTION_1
[STAT: CHARM] [CALLBACK: none] [COMBO: none] [TELL_BONUS: maybe]
""Ambiguous tell""";

            var result = AnthropicLlmAdapter.ParseDialogueOptions(input);
            Assert.False(result[0].HasTellBonus);
        }

        // What: Spec edge case — [CALLBACK: 3] numeric parses to CallbackTurnNumber = 3
        // Mutation: Would catch if numeric callbacks are treated as non-numeric
        [Fact]
        public void ParseDialogueOptions_NumericCallback_ParsedAsInt()
        {
            var input = @"OPTION_1
[STAT: WIT] [CALLBACK: 3] [COMBO: none] [TELL_BONUS: no]
""Callback reference""";

            var result = AnthropicLlmAdapter.ParseDialogueOptions(input);
            Assert.Equal(3, result[0].CallbackTurnNumber);
        }

        // What: Spec edge case — [CALLBACK: pizza_story] non-numeric → null
        // Mutation: Would catch if non-numeric callback is parsed as 0 instead of null
        [Fact]
        public void ParseDialogueOptions_NonNumericCallback_ReturnsNull()
        {
            var input = @"OPTION_1
[STAT: WIT] [CALLBACK: pizza_story] [COMBO: none] [TELL_BONUS: no]
""Callback to story""";

            var result = AnthropicLlmAdapter.ParseDialogueOptions(input);
            Assert.Null(result[0].CallbackTurnNumber);
        }

        // What: Spec edge case — [COMBO: none] maps to null
        // Mutation: Would catch if "none" string is stored instead of null
        [Fact]
        public void ParseDialogueOptions_ComboNone_MapsToNull()
        {
            var input = @"OPTION_1
[STAT: CHARM] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
""No combo""";

            var result = AnthropicLlmAdapter.ParseDialogueOptions(input);
            Assert.Null(result[0].ComboName);
        }

        // What: Spec edge case — [COMBO: The One-Two Punch] maps to full name
        // Mutation: Would catch if combo name is truncated at first space
        [Fact]
        public void ParseDialogueOptions_ComboWithSpaces_FullNamePreserved()
        {
            var input = @"OPTION_1
[STAT: WIT] [CALLBACK: none] [COMBO: The One-Two Punch] [TELL_BONUS: no]
""Combo line""";

            var result = AnthropicLlmAdapter.ParseDialogueOptions(input);
            Assert.Equal("The One-Two Punch", result[0].ComboName);
        }

        // What: Spec edge case — HasWeaknessWindow always false on dialogue options
        // Mutation: Would catch if weakness window is set from LLM parse (should only come from GameSession)
        [Fact]
        public void ParseDialogueOptions_HasWeaknessWindow_AlwaysFalse()
        {
            var input = @"OPTION_1
[STAT: CHARM] [CALLBACK: none] [COMBO: none] [TELL_BONUS: yes]
""Test line""

OPTION_2
[STAT: WIT] [CALLBACK: 3] [COMBO: The Setup] [TELL_BONUS: no]
""Another line""";

            var result = AnthropicLlmAdapter.ParseDialogueOptions(input);
            Assert.All(result, o => Assert.False(o.HasWeaknessWindow));
        }

        // What: Spec edge case — inner quotes within a single option are preserved (issue #1117)
        // Mutation: Would catch if the option text is truncated at the first inner quote
        [Fact]
        public void ParseDialogueOptions_MultipleQuotedStrings_CapturesFullText()
        {
            var input = @"OPTION_1
[STAT: CHARM] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
""First quote"" and then ""second quote""";

            var result = AnthropicLlmAdapter.ParseDialogueOptions(input);
            // Issue #1117: full text captured including inner quotes, not the
            // leading fragment ("First quote") the old regex produced.
            Assert.Equal(@"First quote"" and then ""second quote", result[0].IntendedText);
        }

        // What: Spec edge case — padding skips stats already present
        // Mutation: Would catch if padding creates duplicates of already-parsed stats
        [Fact]
        public void ParseDialogueOptions_PaddingSkipsExistingStats()
        {
            // Charm already present → padding should skip it
            var input = @"OPTION_1
[STAT: CHARM] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
""Charm line""";

            var result = AnthropicLlmAdapter.ParseDialogueOptions(input);
            Assert.Equal(4, result.Length);
            Assert.Equal(StatType.Charm, result[0].Stat);
            // Padding from {Charm, Honesty, Wit, Chaos, Rizz, SelfAwareness} skipping Charm → Honesty, Wit, Chaos
            var paddedStats = result.Skip(1).Select(o => o.Stat).ToArray();
            Assert.Equal(StatType.Honesty, paddedStats[0]);
            Assert.Equal(StatType.Wit, paddedStats[1]);
            Assert.Equal(StatType.Chaos, paddedStats[2]);
        }

        // What: Spec — SelfAwareness stat parsed correctly (two-word stat)
        // Mutation: Would catch if SELF_AWARENESS is not mapped to StatType.SelfAwareness
        [Fact]
        public void ParseDialogueOptions_SelfAwareness_Parsed()
        {
            var input = @"OPTION_1
[STAT: SELF_AWARENESS] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
""A self-aware line""";

            var result = AnthropicLlmAdapter.ParseDialogueOptions(input);
            Assert.Equal(StatType.SelfAwareness, result[0].Stat);
            Assert.Equal("A self-aware line", result[0].IntendedText);
        }

        // What: Spec — case-insensitive stat parsing
        // Mutation: Would catch if Enum.Parse ignoreCase parameter is false
        [Fact]
        public void ParseDialogueOptions_CaseInsensitive()
        {
            var input = @"OPTION_1
[STAT: charm] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
""Lowercase charm""";

            var result = AnthropicLlmAdapter.ParseDialogueOptions(input);
            Assert.Equal(StatType.Charm, result[0].Stat);
        }

        // What: Spec — default DialogueOption fields for padding
        // Mutation: Would catch if default padding options have non-null combo/callback
        [Fact]
        public void ParseDialogueOptions_DefaultPadding_HasCorrectDefaults()
        {
            var result = AnthropicLlmAdapter.ParseDialogueOptions(null!);
            foreach (var option in result)
            {
                Assert.Equal("...", option.IntendedText);
                Assert.Null(option.CallbackTurnNumber);
                Assert.Null(option.ComboName);
                Assert.False(option.HasTellBonus);
                Assert.False(option.HasWeaknessWindow);
            }
        }
    }
}