using Newtonsoft.Json.Linq;
using Pinder.Core.Stats;
using Pinder.LlmAdapters.Anthropic;
using Xunit;

namespace Pinder.LlmAdapters.Tests.Anthropic
{
    public class OpponentResponseParsersTests
    {
        [Fact]
        public void ParseOpponentResponseText_NullInput_ReturnsEmptyResponse()
        {
            var result = OpponentResponseParsers.ParseOpponentResponseText(null);
            Assert.Equal("", result.MessageText);
            Assert.Null(result.DetectedTell);
            Assert.Null(result.WeaknessWindow);
        }

        [Fact]
        public void ParseOpponentResponseText_PlainText_ReturnsMessage()
        {
            var result = OpponentResponseParsers.ParseOpponentResponseText("Hello there!");
            Assert.Equal("Hello there!", result.MessageText);
            Assert.Null(result.DetectedTell);
            Assert.Null(result.WeaknessWindow);
        }

        [Fact]
        public void ParseOpponentResponseText_WithSignals_ParsesTellAndWeakness()
        {
            var input = @"Nice try, but you'll have to do better than that.
[SIGNALS]
TELL: Charm (she twirls her hair when nervous)
WEAKNESS: Wit -2 (distracted by the joke)";

            var result = OpponentResponseParsers.ParseOpponentResponseText(input);
            Assert.Equal("Nice try, but you'll have to do better than that.", result.MessageText);

            Assert.NotNull(result.DetectedTell);
            Assert.Equal(StatType.Charm, result.DetectedTell!.Stat);
            Assert.Equal("she twirls her hair when nervous", result.DetectedTell.Description);

            Assert.NotNull(result.WeaknessWindow);
            Assert.Equal(StatType.Wit, result.WeaknessWindow!.DefendingStat);
            Assert.Equal(2, result.WeaknessWindow.DcReduction);
        }

        [Fact]
        public void ParseOpponentResponseText_WithResponseTag_StripsTag()
        {
            var input = "[RESPONSE] Actual message here.";
            var result = OpponentResponseParsers.ParseOpponentResponseText(input);
            Assert.Equal("Actual message here.", result.MessageText);
        }

        [Fact]
        public void ParseOpponentResponseText_WithQuotes_StripsQuotes()
        {
            var input = "\"Hello there!\"";
            var result = OpponentResponseParsers.ParseOpponentResponseText(input);
            Assert.Equal("Hello there!", result.MessageText);
        }

        [Fact]
        public void ParseOpponentResponseText_WithEvalHeader_StripsHeader()
        {
            var input = "Some eval stuff. The content works as written. Actual message here.";
            var result = OpponentResponseParsers.ParseOpponentResponseText(input);
            Assert.Equal("Actual message here.", result.MessageText);
        }

        [Fact]
        public void ParseOpponentResponseTool_ValidInput_ParsesCorrectly()
        {
            var json = JObject.Parse(@"{
                ""message"": ""You think you're clever?"",
                ""tell"": { ""stat"": ""Honesty"", ""description"": ""avoiding eye contact"" },
                ""weakness"": { ""defending_stat"": ""Chaos"", ""dc_reduction"": 3 }
            }");

            var result = OpponentResponseParsers.ParseOpponentResponseTool(json);
            Assert.NotNull(result);
            Assert.Equal("You think you're clever?", result!.MessageText);

            Assert.NotNull(result.DetectedTell);
            Assert.Equal(StatType.Honesty, result.DetectedTell!.Stat);
            Assert.Equal("avoiding eye contact", result.DetectedTell.Description);

            Assert.NotNull(result.WeaknessWindow);
            Assert.Equal(StatType.Chaos, result.WeaknessWindow!.DefendingStat);
            Assert.Equal(3, result.WeaknessWindow.DcReduction);
        }

        [Fact]
        public void ParseOpponentResponseTool_NoMessage_ReturnsNull()
        {
            var json = JObject.Parse(@"{ ""tell"": null }");
            var result = OpponentResponseParsers.ParseOpponentResponseTool(json);
            Assert.Null(result);
        }

        [Fact]
        public void ParseOpponentResponseTool_MessageOnly_NoSignals()
        {
            var json = JObject.Parse(@"{ ""message"": ""Just a message."" }");
            var result = OpponentResponseParsers.ParseOpponentResponseTool(json);
            Assert.NotNull(result);
            Assert.Equal("Just a message.", result!.MessageText);
            Assert.Null(result.DetectedTell);
            Assert.Null(result.WeaknessWindow);
        }

        [Fact]
        public void ParseOpponentResponseText_SelfAwareness_NormalizesCorrectly()
        {
            var input = @"Response text.
[SIGNALS]
TELL: SELF_AWARENESS (knows her flaws)";

            var result = OpponentResponseParsers.ParseOpponentResponseText(input);
            Assert.NotNull(result.DetectedTell);
            Assert.Equal(StatType.SelfAwareness, result.DetectedTell!.Stat);
        }
    }
}
