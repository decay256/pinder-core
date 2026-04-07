using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Pinder.Core.Conversation;
using Pinder.Core.Stats;
using Pinder.LlmAdapters.OpenAi;
using Xunit;

namespace Pinder.LlmAdapters.Tests.OpenAi
{
    public class OpenAiLlmAdapterParseTests
    {
        [Fact]
        public void OpenAiLlmAdapter_ParsesDialogueOptions_FromChatResponse()
        {
            // Arrange: typical LLM output in the OPTION_N format
            var llmOutput = @"OPTION_1
[STAT: CHARM] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
""Hey, I noticed your dog in that photo — what's their name?""

OPTION_2
[STAT: WIT] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
""So your bio says you love hiking. Does that mean you're always up for an adventure?""

OPTION_3
[STAT: HONESTY] [CALLBACK: none] [COMBO: none] [TELL_BONUS: yes]
""I'll be real — your profile caught my eye and I had to say something.""

OPTION_4
[STAT: CHAOS] [CALLBACK: 3] [COMBO: The Setup] [TELL_BONUS: no]
""Quick question: pineapple on pizza — deal-breaker or deal-maker?""";

            // Act
            var result = OpenAiLlmAdapter.ParseDialogueOptions(llmOutput);

            // Assert
            Assert.Equal(4, result.Length);
            Assert.Equal(StatType.Charm, result[0].Stat);
            Assert.Contains("dog", result[0].IntendedText);
            Assert.Equal(StatType.Wit, result[1].Stat);
            Assert.Equal(StatType.Honesty, result[2].Stat);
            Assert.True(result[2].HasTellBonus);
            Assert.Equal(StatType.Chaos, result[3].Stat);
            Assert.Equal(3, result[3].CallbackTurnNumber);
            Assert.Equal("The Setup", result[3].ComboName);
        }

        [Fact]
        public void OpenAiLlmAdapter_ParsesOpponentResponse_FromChatResponse()
        {
            // Arrange: opponent response with signals
            var llmOutput = @"Oh wow, you actually noticed Biscuit! Most people just swipe past. His name's Biscuit and he's basically my emotional support goblin.

[SIGNALS]
TELL: Charm (responds warmly to genuine interest in personal details)
WEAKNESS: Honesty-2 (gets flustered when people are direct about attraction)";

            // Act
            var result = OpenAiLlmAdapter.ParseOpponentResponse(llmOutput);

            // Assert
            Assert.Contains("Biscuit", result.MessageText);
            Assert.NotNull(result.DetectedTell);
            Assert.Equal(StatType.Charm, result.DetectedTell!.Stat);
            Assert.Contains("genuine interest", result.DetectedTell.Description);
            Assert.NotNull(result.WeaknessWindow);
            Assert.Equal(StatType.Honesty, result.WeaknessWindow!.DefendingStat);
            Assert.Equal(2, result.WeaknessWindow.DcReduction);
        }

        [Fact]
        public void ParseDialogueOptions_NullInput_Returns4Defaults()
        {
            var result = OpenAiLlmAdapter.ParseDialogueOptions(null);
            Assert.Equal(4, result.Length);
            foreach (var opt in result)
                Assert.Equal("...", opt.IntendedText);
        }

        [Fact]
        public void ParseOpponentResponse_EmptyInput_ReturnsEmptyMessage()
        {
            var result = OpenAiLlmAdapter.ParseOpponentResponse("");
            Assert.Equal("", result.MessageText);
            Assert.Null(result.DetectedTell);
            Assert.Null(result.WeaknessWindow);
        }
    }
}
