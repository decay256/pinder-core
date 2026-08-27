using Xunit;

namespace Pinder.LlmAdapters.Tests
{
    /// <summary>
    /// Issue #311 mutation coverage for the typed private signals.tell mapping table.
    /// </summary>
    public sealed class Issue311_TellCategoriesSpecTests
    {
        private readonly string _instruction = PromptTemplates.DateeResponseInstruction;

        [Theory]
        [InlineData("compliments PLAYER AVATAR", "HONESTY", null)]
        [InlineData("asks a personal question", "HONESTY", "SELF_AWARENESS")]
        [InlineData("makes a joke", "WIT", "CHAOS")]
        [InlineData("shares vulnerability", "HONESTY", null)]
        [InlineData("pulls back or guards", "SELF_AWARENESS", null)]
        [InlineData("tests or challenges", "WIT", "CHAOS")]
        [InlineData("sends a short reply", "CHARM", "CHAOS")]
        [InlineData("flirts", "RIZZ", "CHARM")]
        [InlineData("changes subject", "CHAOS", null)]
        [InlineData("goes quiet or silent", "SELF_AWARENESS", null)]
        public void TypedTellCategory_MapsToExpectedWireStats(
            string behavior,
            string firstStat,
            string? secondStat)
        {
            string line = FindLineContaining("- " + behavior + " -> ");
            Assert.Contains(firstStat, line);
            if (secondStat != null) Assert.Contains(secondStat, line);
        }

        [Theory]
        [InlineData("HONESTY")]
        [InlineData("SELF_AWARENESS")]
        [InlineData("WIT")]
        [InlineData("CHAOS")]
        [InlineData("CHARM")]
        [InlineData("RIZZ")]
        public void TypedTellCategories_UseUppercaseWireStats(string statName)
        {
            Assert.Contains(statName, TellSection());
        }

        [Fact]
        public void TypedTellCategories_HaveNamedPrivateSectionAndExactlyTenMappings()
        {
            string section = TellSection();
            Assert.Contains("Tell guidance for the private `signals.tell` field:", section);

            int count = 0;
            foreach (string line in section.Split('\n'))
            {
                if (line.TrimStart().StartsWith("- ")) count++;
            }
            Assert.Equal(10, count);
        }

        [Fact]
        public void TypedTellCategories_DoNotReintroduceVisibleLegacyGrammar()
        {
            Assert.DoesNotContain("[SIGNALS]", _instruction);
            Assert.DoesNotContain("TELL:", _instruction);
            Assert.DoesNotContain("WEAKNESS:", _instruction);
            Assert.Contains("Never put private signal content inside `message`", _instruction);
        }

        private string TellSection()
        {
            int start = _instruction.IndexOf("Tell guidance for the private `signals.tell` field:");
            int end = _instruction.IndexOf("Signal field rules:", start);
            Assert.True(start >= 0);
            Assert.True(end > start);
            return _instruction.Substring(start, end - start);
        }

        private string FindLineContaining(string text)
        {
            foreach (string line in _instruction.Split('\n'))
            {
                if (line.Contains(text)) return line;
            }
            Assert.Fail("No typed tell mapping found containing '" + text + "'.");
            return string.Empty;
        }
    }
}
