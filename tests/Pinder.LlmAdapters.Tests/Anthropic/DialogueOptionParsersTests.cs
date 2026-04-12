using Newtonsoft.Json.Linq;
using Pinder.Core.Stats;
using Pinder.LlmAdapters.Anthropic;
using Xunit;

namespace Pinder.LlmAdapters.Tests.Anthropic
{
    public class DialogueOptionParsersTests
    {
        [Fact]
        public void ParseDialogueOptionsText_NullInput_ReturnsPaddedDefaults()
        {
            var result = DialogueOptionParsers.ParseDialogueOptionsText(null);
            Assert.Equal(3, result.Length);
            // Defaults should use Charm, Honesty, Wit
            Assert.Equal(StatType.Charm, result[0].Stat);
            Assert.Equal(StatType.Honesty, result[1].Stat);
            Assert.Equal(StatType.Wit, result[2].Stat);
        }

        [Fact]
        public void ParseDialogueOptionsText_EmptyInput_ReturnsPaddedDefaults()
        {
            var result = DialogueOptionParsers.ParseDialogueOptionsText("");
            Assert.Equal(3, result.Length);
        }

        [Fact]
        public void ParseDialogueOptionsText_ValidInput_ParsesCorrectly()
        {
            var input = @"OPTION_1 [STAT: Charm] ""Hey there, looking good!"" [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
OPTION_2 [STAT: Wit] ""Did it hurt when you fell from heaven?"" [CALLBACK: 3] [COMBO: SmoothTalker] [TELL_BONUS: yes]
OPTION_3 [STAT: Honesty] ""I just wanted to say hi."" [CALLBACK: turn_5] [COMBO: none] [TELL_BONUS: no]";

            var result = DialogueOptionParsers.ParseDialogueOptionsText(input);
            Assert.Equal(3, result.Length);

            Assert.Equal(StatType.Charm, result[0].Stat);
            Assert.Equal("Hey there, looking good!", result[0].IntendedText);
            Assert.Null(result[0].CallbackTurnNumber);
            Assert.Null(result[0].ComboName);
            Assert.False(result[0].HasTellBonus);

            Assert.Equal(StatType.Wit, result[1].Stat);
            Assert.Equal(3, result[1].CallbackTurnNumber);
            Assert.Equal("SmoothTalker", result[1].ComboName);
            Assert.True(result[1].HasTellBonus);

            Assert.Equal(StatType.Honesty, result[2].Stat);
            Assert.Equal(5, result[2].CallbackTurnNumber);
        }

        [Fact]
        public void ParseDialogueOptionsText_PartialInput_PadsToThree()
        {
            var input = @"OPTION_1 [STAT: Charm] ""Hello!"" [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]";

            var result = DialogueOptionParsers.ParseDialogueOptionsText(input);
            Assert.Equal(3, result.Length);
            Assert.Equal(StatType.Charm, result[0].Stat);
            Assert.Equal("Hello!", result[0].IntendedText);
            // Padding fills with Honesty, Wit (since Charm is used)
            Assert.Equal(StatType.Honesty, result[1].Stat);
            Assert.Equal(StatType.Wit, result[2].Stat);
        }

        [Fact]
        public void ParseDialogueOptionsTool_ValidInput_ParsesCorrectly()
        {
            var json = JObject.Parse(@"{
                ""options"": [
                    { ""stat"": ""Charm"", ""text"": ""Hey!"", ""callback"": ""none"", ""combo"": ""none"", ""tell_bonus"": false, ""weakness_window"": false },
                    { ""stat"": ""Wit"", ""text"": ""Nice one."", ""callback"": ""3"", ""combo"": ""Joker"", ""tell_bonus"": true, ""weakness_window"": true },
                    { ""stat"": ""Honesty"", ""text"": ""Truth."", ""callback"": ""null"", ""combo"": ""null"", ""tell_bonus"": false, ""weakness_window"": false }
                ]
            }");

            var result = DialogueOptionParsers.ParseDialogueOptionsTool(json);
            Assert.NotNull(result);
            Assert.Equal(3, result!.Length);

            Assert.Equal(StatType.Charm, result[0].Stat);
            Assert.Equal("Hey!", result[0].IntendedText);
            Assert.Null(result[0].CallbackTurnNumber);

            Assert.Equal(StatType.Wit, result[1].Stat);
            Assert.Equal(3, result[1].CallbackTurnNumber);
            Assert.Equal("Joker", result[1].ComboName);
            Assert.True(result[1].HasTellBonus);
            Assert.True(result[1].HasWeaknessWindow);
        }

        [Fact]
        public void ParseDialogueOptionsTool_EmptyOptions_ReturnsNull()
        {
            var json = JObject.Parse(@"{ ""options"": [] }");
            var result = DialogueOptionParsers.ParseDialogueOptionsTool(json);
            Assert.Null(result);
        }

        [Fact]
        public void ParseDialogueOptionsTool_MalformedInput_ReturnsNull()
        {
            var json = JObject.Parse(@"{ ""garbage"": true }");
            var result = DialogueOptionParsers.ParseDialogueOptionsTool(json);
            Assert.Null(result);
        }

        [Fact]
        public void ParseDialogueOptionsText_SelfAwareness_NormalizesCorrectly()
        {
            var input = @"OPTION_1 [STAT: SELF_AWARENESS] ""I know myself."" [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]";
            var result = DialogueOptionParsers.ParseDialogueOptionsText(input);
            Assert.Equal(StatType.SelfAwareness, result[0].Stat);
        }
    }
}
