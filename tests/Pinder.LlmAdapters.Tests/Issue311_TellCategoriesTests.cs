using Xunit;

namespace Pinder.LlmAdapters.Tests
{
    /// <summary>
    /// Issue #311: Verify that OpponentResponseInstruction includes all 10 tell category mappings
    /// from rules §15, so the LLM uses the correct stat for each opponent behavior.
    /// </summary>
    public class Issue311_TellCategoriesTests
    {
        [Fact]
        public void OpponentResponseInstruction_ContainsTellCategoryHeader()
        {
            Assert.Contains(
                "When generating a TELL, use ONLY these category mappings:",
                PromptTemplates.OpponentResponseInstruction);
        }

        [Theory]
        [InlineData("Opponent compliments player", "TELL: HONESTY")]
        [InlineData("Opponent asks personal question", "TELL: HONESTY or SELF_AWARENESS")]
        [InlineData("Opponent makes joke", "TELL: WIT or CHAOS")]
        [InlineData("Opponent shares vulnerability", "TELL: HONESTY")]
        [InlineData("Opponent pulls back/guards", "TELL: SELF_AWARENESS")]
        [InlineData("Opponent tests/challenges", "TELL: WIT or CHAOS")]
        [InlineData("Opponent sends short reply", "TELL: CHARM or CHAOS")]
        [InlineData("Opponent flirts", "TELL: RIZZ or CHARM")]
        [InlineData("Opponent changes subject", "TELL: CHAOS")]
        [InlineData("Opponent goes quiet/silent", "TELL: SELF_AWARENESS")]
        public void OpponentResponseInstruction_ContainsTellCategory(string behavior, string expectedTell)
        {
            // Each mapping should appear as "- {behavior} → {expectedTell}"
            var expectedMapping = $"- {behavior} \u2192 {expectedTell}";
            Assert.Contains(expectedMapping, PromptTemplates.OpponentResponseInstruction);
        }

        [Fact]
        public void OpponentResponseInstruction_ContainsAll10TellCategories()
        {
            var instruction = PromptTemplates.OpponentResponseInstruction;

            // Count the number of tell category mapping lines (lines starting with "- Opponent")
            var lines = instruction.Split('\n');
            var categoryLines = 0;
            foreach (var line in lines)
            {
                if (line.TrimStart().StartsWith("- Opponent"))
                    categoryLines++;
            }

            Assert.Equal(10, categoryLines);
        }
    }
}
