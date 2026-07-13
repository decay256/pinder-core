using Pinder.Core.Stats;
using Pinder.LlmAdapters.Anthropic;
using Xunit;
using Newtonsoft.Json.Linq;

namespace Pinder.LlmAdapters.Tests
{
    public class MetaPrefixTests
    {
        [Theory]
        [InlineData("CONTEXT: actual message", "actual message")]
        [InlineData("GENUINE QUESTION: Is this real?", "Is this real?")]
        [InlineData("RECOGNITION: I see you.", "I see you.")]
        [InlineData("NOTE: just a thought", "just a thought")]
        [InlineData("WOULD-YOU-RATHER: duck vs horse", "duck vs horse")]
        [InlineData("hey, how was your day?", "hey, how was your day?")]
        public void DialogueOptionParsers_StripsMetaPrefix_Correctly(string input, string expected)
        {
            // We test via the Anthropic parser which is the primary implementation
            var rawInput = $@"OPTION_1 [STAT: Charm] ""{input}"" [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]";
            var result = DialogueOptionParsers.ParseDialogueOptionsText(rawInput, new[] { StatType.Charm, StatType.Honesty, StatType.Wit, StatType.Chaos });
            
            Assert.Equal(expected, result[0].IntendedText);
        }

        [Fact]
        public void DialogueOptionParsers_Tool_StripsMetaPrefix_Correctly()
        {
            var json = JObject.Parse(@"{
                ""options"": [
                    { ""stat"": ""Charm"", ""text"": ""CONTEXT: tool message"", ""callback"": ""none"", ""combo"": ""none"" }
                ]
            }");
            var result = DialogueOptionParsers.ParseDialogueOptionsTool(json, new[] { StatType.Charm });
            
            Assert.Equal("tool message", result![0].IntendedText);
        }
    }
}
