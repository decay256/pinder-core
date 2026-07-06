using Newtonsoft.Json.Linq;
using Pinder.Core.Stats;
using Pinder.LlmAdapters.Anthropic;
using Xunit;

namespace Pinder.LlmAdapters.Tests.Anthropic
{
    public class DateeResponseParsersTests
    {
        [Fact]
        public void ParseDateeResponseText_NullInput_ReturnsEmptyResponse()
        {
            var result = DateeResponseParsers.ParseDateeResponseText(null);
            Assert.Equal("", result.MessageText);
            Assert.Null(result.DetectedTell);
            Assert.Null(result.WeaknessWindow);
        }

        [Fact]
        public void ParseDateeResponseText_PlainText_ReturnsMessage()
        {
            var result = DateeResponseParsers.ParseDateeResponseText("Hello there!");
            Assert.Equal("Hello there!", result.MessageText);
            Assert.Null(result.DetectedTell);
            Assert.Null(result.WeaknessWindow);
        }

        [Fact]
        public void ParseDateeResponseText_WithSignals_ParsesTellAndWeakness()
        {
            var input = @"Nice try, but you'll have to do better than that.
[SIGNALS]
TELL: Charm (she twirls her hair when nervous)
WEAKNESS: Wit -2 (distracted by the joke)";

            var result = DateeResponseParsers.ParseDateeResponseText(input);
            Assert.Equal("Nice try, but you'll have to do better than that.", result.MessageText);

            Assert.NotNull(result.DetectedTell);
            Assert.Equal(StatType.Charm, result.DetectedTell!.Stat);
            Assert.Equal("she twirls her hair when nervous", result.DetectedTell.Description);

            Assert.NotNull(result.WeaknessWindow);
            Assert.Equal(StatType.Wit, result.WeaknessWindow!.DefendingStat);
            Assert.Equal(2, result.WeaknessWindow.DcReduction);
        }

        [Fact]
        public void ParseDateeResponseText_WithResponseTag_StripsTag()
        {
            var input = "[RESPONSE] Actual message here.";
            var result = DateeResponseParsers.ParseDateeResponseText(input);
            Assert.Equal("Actual message here.", result.MessageText);
        }

        [Fact]
        public void ParseDateeResponseText_WithQuotes_StripsQuotes()
        {
            var input = "\"Hello there!\"";
            var result = DateeResponseParsers.ParseDateeResponseText(input);
            Assert.Equal("Hello there!", result.MessageText);
        }

        [Fact]
        public void ParseDateeResponseText_WithEvalHeader_StripsHeader()
        {
            var input = "Some eval stuff. The content works as written. Actual message here.";
            var result = DateeResponseParsers.ParseDateeResponseText(input);
            Assert.Equal("Actual message here.", result.MessageText);
        }

        [Fact]
        public void ParseDateeResponseTool_ValidInput_ParsesCorrectly()
        {
            var json = JObject.Parse(@"{
                ""message"": ""You think you're clever?"",
                ""tell"": { ""stat"": ""Honesty"", ""description"": ""avoiding eye contact"" },
                ""weakness"": { ""defending_stat"": ""Chaos"", ""dc_reduction"": 3 }
            }");

            var result = DateeResponseParsers.ParseDateeResponseTool(json);
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
        public void ParseDateeResponseTool_NoMessage_ReturnsNull()
        {
            var json = JObject.Parse(@"{ ""tell"": null }");
            var result = DateeResponseParsers.ParseDateeResponseTool(json);
            Assert.Null(result);
        }

        [Fact]
        public void ParseDateeResponseTool_MessageOnly_NoSignals()
        {
            var json = JObject.Parse(@"{ ""message"": ""Just a message."" }");
            var result = DateeResponseParsers.ParseDateeResponseTool(json);
            Assert.NotNull(result);
            Assert.Equal("Just a message.", result!.MessageText);
            Assert.Null(result.DetectedTell);
            Assert.Null(result.WeaknessWindow);
        }

        [Fact]
        public void ParseDateeResponseText_SelfAwareness_NormalizesCorrectly()
        {
            var input = @"Response text.
[SIGNALS]
TELL: SELF_AWARENESS (knows her flaws)";

            var result = DateeResponseParsers.ParseDateeResponseText(input);
            Assert.NotNull(result.DetectedTell);
            Assert.Equal(StatType.SelfAwareness, result.DetectedTell!.Stat);
        }

        // --- Issue #1116: persona "/end" / "/rant" self-tag tics must never persist ---

        [Fact]
        public void ParseDateeResponseText_MisplacedEndTagInParenthetical_StripsTag()
        {
            // The model misplaces "/end" mid-message, inside a parenthetical with more
            // sentences after it. The saved message must not contain the tag.
            var input = "i'll leave that one open /end)\n\nI don't want to get into it now";

            var result = DateeResponseParsers.ParseDateeResponseText(input);

            Assert.DoesNotContain("/end", result.MessageText);
            Assert.Equal("i'll leave that one open)\n\nI don't want to get into it now", result.MessageText);
        }

        [Fact]
        public void ParseDateeResponseText_TrailingEndTag_StripsTag()
        {
            var input = "anyway that's my whole thing about it /end";
            var result = DateeResponseParsers.ParseDateeResponseText(input);
            Assert.DoesNotContain("/end", result.MessageText);
            Assert.Equal("anyway that's my whole thing about it", result.MessageText);
        }

        [Fact]
        public void ParseDateeResponseText_TrailingRantTag_StripsTag()
        {
            // Policy: "/rant" is kept in persona guidance only as a clean terminal
            // suffix, but it is still a meta marker and is stripped from chat content.
            var input = "...anyway. /rant";
            var result = DateeResponseParsers.ParseDateeResponseText(input);
            Assert.DoesNotContain("/rant", result.MessageText);
            Assert.Equal("...anyway.", result.MessageText);
        }

        [Fact]
        public void ParseDateeResponseText_MidTextRantTag_StripsTag()
        {
            var input = "ok so /rant the food was cold and nobody cared";
            var result = DateeResponseParsers.ParseDateeResponseText(input);
            Assert.DoesNotContain("/rant", result.MessageText);
            Assert.Equal("ok so the food was cold and nobody cared", result.MessageText);
        }

        [Fact]
        public void ParseDateeResponseText_LegitimateSlashText_NotMangled()
        {
            // Only the exact self-tag tokens are removed; legitimate slash-bearing prose stays.
            var input = "the and/or thing is fine and the date is 06/10 ok";
            var result = DateeResponseParsers.ParseDateeResponseText(input);
            Assert.Equal("the and/or thing is fine and the date is 06/10 ok", result.MessageText);
        }

        [Fact]
        public void ParseDateeResponseTool_MisplacedEndTag_StripsTag()
        {
            var json = JObject.Parse(@"{ ""message"": ""i'll leave that one open /end) and move on"" }");
            var result = DateeResponseParsers.ParseDateeResponseTool(json);
            Assert.NotNull(result);
            Assert.DoesNotContain("/end", result!.MessageText);
            Assert.Equal("i'll leave that one open) and move on", result.MessageText);
        }

        [Fact]
        public void ParseDateeResponseTool_TrailingRantTag_StripsTag()
        {
            var json = JObject.Parse(@"{ ""message"": ""...that's the post /rant"" }");
            var result = DateeResponseParsers.ParseDateeResponseTool(json);
            Assert.NotNull(result);
            Assert.DoesNotContain("/rant", result!.MessageText);
            Assert.Equal("...that's the post", result.MessageText);
        }

        [Fact]
        public void ParseDateeResponseTool_InvalidStatOrWeakness_LogsAndGracefullyHandles()
        {
            var json = JObject.Parse(@"{
                ""message"": ""Hello there!"",
                ""tell"": { ""stat"": ""InvalidStatName"", ""description"": ""some tell"" },
                ""weakness"": { ""defending_stat"": ""AnotherInvalidStat"", ""dc_reduction"": 5 }
            }");

            using (var sw = new System.IO.StringWriter())
            {
                var originalError = System.Console.Error;
                System.Console.SetError(sw);
                try
                {
                    var result = DateeResponseParsers.ParseDateeResponseTool(json);
                    
                    Assert.NotNull(result);
                    Assert.Equal("Hello there!", result!.MessageText);
                    Assert.Null(result.DetectedTell);
                    Assert.Null(result.WeaknessWindow);

                    var consoleOutput = sw.ToString();
                    Assert.Contains("[DateeResponseParsers] Failed to parse Tell stat 'InvalidStatName'", consoleOutput);
                    Assert.Contains("[DateeResponseParsers] Failed to parse Weakness Window (stat='AnotherInvalidStat')", consoleOutput);
                }
                finally
                {
                    System.Console.SetError(originalError);
                }
            }
        }
    }
}
