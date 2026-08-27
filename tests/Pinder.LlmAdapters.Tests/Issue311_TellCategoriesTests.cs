using Xunit;

namespace Pinder.LlmAdapters.Tests
{
    /// <summary>
    /// Issue #311: Verify that DateeResponseInstruction includes all 10 typed tell mappings
    /// so the LLM emits the correct private signals.tell stat for each datee behavior.
    /// </summary>
    public class Issue311_TellCategoriesTests
    {
        [Fact]
        public void DateeResponseInstruction_ContainsTellCategoryHeader()
        {
            Assert.Contains(
                "Tell guidance for the private `signals.tell` field:",
                PromptTemplates.DateeResponseInstruction);
        }

        [Theory]
        [InlineData("compliments PLAYER AVATAR", "HONESTY")]
        [InlineData("asks a personal question", "HONESTY or SELF_AWARENESS")]
        [InlineData("makes a joke", "WIT or CHAOS")]
        [InlineData("shares vulnerability", "HONESTY")]
        [InlineData("pulls back or guards", "SELF_AWARENESS")]
        [InlineData("tests or challenges", "WIT or CHAOS")]
        [InlineData("sends a short reply", "CHARM or CHAOS")]
        [InlineData("flirts", "RIZZ or CHARM")]
        [InlineData("changes subject", "CHAOS")]
        [InlineData("goes quiet or silent", "SELF_AWARENESS")]
        public void DateeResponseInstruction_ContainsTellCategory(string behavior, string expectedTell)
        {
            var expectedMapping = $"- {behavior} -> {expectedTell}";
            Assert.Contains(expectedMapping, PromptTemplates.DateeResponseInstruction);
        }

        [Fact]
        public void DateeResponseInstruction_ContainsAll10TellCategories()
        {
            var instruction = PromptTemplates.DateeResponseInstruction;

            int start = instruction.IndexOf("Tell guidance for the private `signals.tell` field:");
            int end = instruction.IndexOf("Signal field rules:", start);
            string tellSection = instruction.Substring(start, end - start);
            int categoryLines = 0;
            foreach (string line in tellSection.Split('\n'))
            {
                if (line.TrimStart().StartsWith("- ")) categoryLines++;
            }

            Assert.Equal(10, categoryLines);
        }
    }
}
