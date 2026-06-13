using Xunit;

namespace Pinder.LlmAdapters.Tests
{
    /// <summary>
    /// Issue #311: Verify that DateeResponseInstruction includes all 10 tell category mappings
    /// from rules §15, so the LLM uses the correct stat for each datee behavior.
    /// </summary>
    public class Issue311_TellCategoriesTests
    {
        [Fact]
        public void DateeResponseInstruction_ContainsTellCategoryHeader()
        {
            Assert.Contains(
                "When generating a TELL, use ONLY these category mappings:",
                PromptTemplates.DateeResponseInstruction);
        }

        [Theory]
        [InlineData("DATEE compliments PLAYER AVATAR", "TELL: HONESTY")]
        [InlineData("DATEE asks personal question", "TELL: HONESTY or SELF_AWARENESS")]
        [InlineData("DATEE makes joke", "TELL: WIT or CHAOS")]
        [InlineData("DATEE shares vulnerability", "TELL: HONESTY")]
        [InlineData("DATEE pulls back/guards", "TELL: SELF_AWARENESS")]
        [InlineData("DATEE tests/challenges", "TELL: WIT or CHAOS")]
        [InlineData("DATEE sends short reply", "TELL: CHARM or CHAOS")]
        [InlineData("DATEE flirts", "TELL: RIZZ or CHARM")]
        [InlineData("DATEE changes subject", "TELL: CHAOS")]
        [InlineData("DATEE goes quiet/silent", "TELL: SELF_AWARENESS")]
        public void DateeResponseInstruction_ContainsTellCategory(string behavior, string expectedTell)
        {
            // Each mapping should appear as "- {behavior} → {expectedTell}"
            var expectedMapping = $"- {behavior} \u2192 {expectedTell}";
            Assert.Contains(expectedMapping, PromptTemplates.DateeResponseInstruction);
        }

        [Fact]
        public void DateeResponseInstruction_ContainsAll10TellCategories()
        {
            var instruction = PromptTemplates.DateeResponseInstruction;

            // Count the number of tell category mapping lines (lines starting with "- DATEE")
            var lines = instruction.Split('\n');
            var categoryLines = 0;
            foreach (var line in lines)
            {
                if (line.TrimStart().StartsWith("- DATEE"))
                    categoryLines++;
            }

            Assert.Equal(10, categoryLines);
        }
    }
}
