using Pinder.Core.Stats;
using Pinder.LlmAdapters.Anthropic;
using Pinder.LlmAdapters.OpenAi;
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
            var result = DialogueOptionParsers.ParseDialogueOptionsText(rawInput);
            
            Assert.Equal(expected, result[0].IntendedText);
        }

        [Fact]
        public void OpenAiLlmAdapter_StripsMetaPrefix_Correctly()
        {
            var input = @"OPTION_1 [STAT: Charm] ""CONTEXT: OpenAI message"" [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]";
            var result = OpenAiLlmAdapter.ParseDialogueOptions(input);
            
            Assert.Equal("OpenAI message", result[0].IntendedText);
        }

        [Fact]
        public void DialogueOptionParsers_Tool_StripsMetaPrefix_Correctly()
        {
            var json = JObject.Parse(@"{
                ""options"": [
                    { ""stat"": ""Charm"", ""text"": ""CONTEXT: tool message"", ""callback"": ""none"", ""combo"": ""none"", ""tell_bonus"": false, ""weakness_window"": false }
                ]
            }");
            var result = DialogueOptionParsers.ParseDialogueOptionsTool(json);
            
            Assert.Equal("tool message", result![0].IntendedText);
        }
    }
}
