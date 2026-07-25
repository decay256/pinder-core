using System.IO;
using Pinder.Core.Conversation;
using Xunit;

namespace Pinder.Rules.Tests
{
    [Trait("Category", "Rules")]
    public sealed class Issue1333_InterestBandRuleCharacterizationTests
    {
        [Theory]
        [InlineData(0, InterestState.Unmatched)]
        [InlineData(1, InterestState.Bored)]
        [InlineData(4, InterestState.Bored)]
        [InlineData(5, InterestState.Lukewarm)]
        [InlineData(9, InterestState.Lukewarm)]
        [InlineData(10, InterestState.Interested)]
        [InlineData(15, InterestState.Interested)]
        [InlineData(16, InterestState.VeryIntoIt)]
        [InlineData(20, InterestState.VeryIntoIt)]
        [InlineData(21, InterestState.AlmostThere)]
        [InlineData(24, InterestState.AlmostThere)]
        [InlineData(25, InterestState.DateSecured)]
        public void RuleBookResolver_RealYamlInterestStateBoundaries_MatchCanonicalFallback(
            int interest,
            InterestState expected)
        {
            var resolver = RuleBookResolver.FromYaml(File.ReadAllText(FindRulesYaml()));

            Assert.Equal(expected, resolver.GetInterestState(interest));
        }

        private static string FindRulesYaml()
        {
            string? dir = Directory.GetCurrentDirectory();
            while (dir != null)
            {
                var candidate = Path.Combine(dir, "rules", "extracted", "rules-v3-enriched.yaml");
                if (File.Exists(candidate))
                    return candidate;
                dir = Path.GetDirectoryName(dir);
            }

            throw new FileNotFoundException("Could not locate rules/extracted/rules-v3-enriched.yaml.");
        }
    }
}
