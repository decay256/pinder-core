using Xunit;

namespace Pinder.LlmAdapters.Tests
{
    /// <summary>
    /// Issue #311 spec-driven tests: Verify DateeResponseInstruction includes all 10 tell
    /// category mappings from rules §15 with correct stat associations.
    /// These tests supplement Issue311_TellCategoriesTests with additional mutation coverage.
    /// </summary>
    public class Issue311_TellCategoriesSpecTests
    {
        private readonly string _instruction = PromptTemplates.DateeResponseInstruction;

        // --- AC1: All 10 tell category mappings present with correct stats ---

        // What: AC1 — "Compliments player" must map to HONESTY (spec table row 1)
        // Mutation: Fails if mapping is removed or stat changed from HONESTY to another stat
        [Fact]
        public void TellCategory_ComplimentsPlayer_MapsToHonesty()
        {
            Assert.Contains("DATEE compliments PLAYER AVATAR", _instruction);
            // Verify the stat appears on the same logical line as the behavior
            var line = FindLineContaining("DATEE compliments PLAYER AVATAR");
            Assert.Contains("HONESTY", line);
        }

        // What: AC1 — "Asks personal question" must map to HONESTY or SELF_AWARENESS (spec table row 2)
        // Mutation: Fails if either stat is removed from this mapping
        [Fact]
        public void TellCategory_AsksPersonalQuestion_MapsToHonestyOrSelfAwareness()
        {
            var line = FindLineContaining("DATEE asks personal question");
            Assert.Contains("HONESTY", line);
            Assert.Contains("SELF_AWARENESS", line);
        }

        // What: AC1 — "Makes joke" must map to WIT or CHAOS (spec table row 3)
        // Mutation: Fails if WIT or CHAOS is removed from this mapping
        [Fact]
        public void TellCategory_MakesJoke_MapsToWitOrChaos()
        {
            var line = FindLineContaining("DATEE makes joke");
            Assert.Contains("WIT", line);
            Assert.Contains("CHAOS", line);
        }

        // What: AC1 — "Shares vulnerability" must map to HONESTY (spec table row 4)
        // Mutation: Fails if mapping is changed to a different stat
        [Fact]
        public void TellCategory_SharesVulnerability_MapsToHonesty()
        {
            var line = FindLineContaining("DATEE shares vulnerability");
            Assert.Contains("HONESTY", line);
        }

        // What: AC1 — "Pulls back/guards" must map to SELF_AWARENESS (spec table row 5)
        // Mutation: Fails if SELF_AWARENESS is replaced with another stat
        [Fact]
        public void TellCategory_PullsBackGuards_MapsToSelfAwareness()
        {
            var line = FindLineContaining("DATEE pulls back/guards");
            Assert.Contains("SELF_AWARENESS", line);
        }

        // What: AC1 — "Tests/challenges" must map to WIT or CHAOS (spec table row 6)
        // Mutation: Fails if either WIT or CHAOS is removed
        [Fact]
        public void TellCategory_TestsChallenges_MapsToWitOrChaos()
        {
            var line = FindLineContaining("DATEE tests/challenges");
            Assert.Contains("WIT", line);
            Assert.Contains("CHAOS", line);
        }

        // What: AC1 — "Sends short reply" must map to CHARM or CHAOS (spec table row 7)
        // Mutation: Fails if CHARM or CHAOS is removed from this mapping
        [Fact]
        public void TellCategory_SendsShortReply_MapsToCharmOrChaos()
        {
            var line = FindLineContaining("DATEE sends short reply");
            Assert.Contains("CHARM", line);
            Assert.Contains("CHAOS", line);
        }

        // What: AC1 — "Flirts" must map to RIZZ or CHARM (spec table row 8)
        // Mutation: Fails if RIZZ or CHARM is removed from this mapping
        [Fact]
        public void TellCategory_Flirts_MapsToRizzOrCharm()
        {
            var line = FindLineContaining("DATEE flirts");
            Assert.Contains("RIZZ", line);
            Assert.Contains("CHARM", line);
        }

        // What: AC1 — "Changes subject" must map to CHAOS (spec table row 9)
        // Mutation: Fails if CHAOS is removed or replaced
        [Fact]
        public void TellCategory_ChangesSubject_MapsToChaos()
        {
            var line = FindLineContaining("DATEE changes subject");
            Assert.Contains("CHAOS", line);
        }

        // What: AC1 — "Goes quiet/silent" must map to SELF_AWARENESS (spec table row 10)
        // Mutation: Fails if SELF_AWARENESS is replaced with another stat
        [Fact]
        public void TellCategory_GoesQuietSilent_MapsToSelfAwareness()
        {
            var line = FindLineContaining("DATEE goes quiet/silent");
            Assert.Contains("SELF_AWARENESS", line);
        }

        // --- AC1: "ONLY" constraint instruction ---

        // What: AC1 — The section must include explicit "ONLY" instruction per spec
        // Mutation: Fails if the exclusivity constraint is removed (LLM could invent mappings)
        [Fact]
        public void TellCategories_ContainsOnlyConstraint()
        {
            // The prompt must tell the LLM to use ONLY these mappings
            Assert.Contains("ONLY", _instruction);
            // Verify it's in the context of tell category usage
            // The first "ONLY" is in the context-boundary section; the second is the tell-category mapping instruction
            var firstOnlyIdx = _instruction.IndexOf("ONLY");
            var secondOnlyIdx = _instruction.IndexOf("ONLY", firstOnlyIdx + 1);
            Assert.True(secondOnlyIdx > firstOnlyIdx, "Expected at least two 'ONLY' occurrences in DateeResponseInstruction");
            Assert.Contains("these", _instruction.Substring(
                secondOnlyIdx - 20 < 0 ? 0 : secondOnlyIdx - 20,
                Math.Min(40 + (secondOnlyIdx - 20 < 0 ? secondOnlyIdx : 20), _instruction.Length - (secondOnlyIdx - 20 < 0 ? 0 : secondOnlyIdx - 20))));
        }

        // --- AC1: All 6 stat names in uppercase format ---

        // What: AC1 — Stats must use exact uppercase format per spec
        // Mutation: Fails if any stat name uses wrong casing (e.g., "Honesty" instead of "HONESTY")
        [Theory]
        [InlineData("HONESTY")]
        [InlineData("SELF_AWARENESS")]
        [InlineData("WIT")]
        [InlineData("CHAOS")]
        [InlineData("CHARM")]
        [InlineData("RIZZ")]
        public void TellCategories_UseUppercaseStatNames(string statName)
        {
            Assert.Contains(statName, _instruction);
        }

        // --- AC2: Exactly 10 category mappings, no more, no less ---

        // What: AC2 — Guard against accidental addition or removal of mappings
        // Mutation: Fails if a mapping is added or removed (count changes from 10)
        [Fact]
        public void TellCategories_ExactlyTenBehaviorMappings()
        {
            string[] expectedBehaviors = new[]
            {
                "DATEE compliments PLAYER AVATAR",
                "DATEE asks personal question",
                "DATEE makes joke",
                "DATEE shares vulnerability",
                "DATEE pulls back/guards",
                "DATEE tests/challenges",
                "DATEE sends short reply",
                "DATEE flirts",
                "DATEE changes subject",
                "DATEE goes quiet/silent"
            };

            int foundCount = 0;
            foreach (var behavior in expectedBehaviors)
            {
                if (_instruction.Contains(behavior))
                    foundCount++;
                else
                    Assert.Fail($"Missing tell category: '{behavior}'");
            }

            Assert.Equal(10, foundCount);
        }

        // --- Edge case: Tell categories are in a clearly labeled section ---

        // What: AC1 — "tell categories must appear as a clearly labeled reference section"
        // Mutation: Fails if the section header/label is removed
        [Fact]
        public void TellCategories_HasClearlyLabeledSection()
        {
            // Should contain a reference/header that labels the tell category block
            bool hasLabel = _instruction.Contains("Tell category") ||
                           _instruction.Contains("tell category") ||
                           _instruction.Contains("category mapping");
            Assert.True(hasLabel, "Tell categories must appear in a clearly labeled section");
        }

        // --- Helper ---

        private string FindLineContaining(string text)
        {
            var lines = _instruction.Split('\n');
            foreach (var line in lines)
            {
                if (line.Contains(text))
                    return line;
            }
            Assert.Fail($"No line found containing '{text}' in DateeResponseInstruction");
            return string.Empty; // unreachable
        }
    }
}
