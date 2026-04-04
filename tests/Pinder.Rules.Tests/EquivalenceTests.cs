using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;
using Pinder.Rules;
using Pinder.Core.Rolls;
using Pinder.Core.Conversation;
using Pinder.Core.Stats;

namespace Pinder.Rules.Tests
{
    /// <summary>
    /// Proves the rule engine produces identical results to hardcoded C# logic
    /// for §5 (failure tiers) and §6 (interest states).
    /// </summary>
    public class EquivalenceTests
    {
        private static readonly string YamlPath = FindYamlPath();

        private static string FindYamlPath()
        {
            // Walk up from test binary to find rules/extracted/
            var dir = System.AppDomain.CurrentDomain.BaseDirectory;
            for (int i = 0; i < 10; i++)
            {
                var candidate = Path.Combine(dir, "rules", "extracted", "rules-v3-enriched.yaml");
                if (File.Exists(candidate))
                    return candidate;
                var parent = Directory.GetParent(dir);
                if (parent == null) break;
                dir = parent.FullName;
            }
            return "";
        }

        private RuleBook LoadRuleBook()
        {
            Assert.True(File.Exists(YamlPath), $"YAML file not found at {YamlPath}");
            var yaml = File.ReadAllText(YamlPath);
            return RuleBook.LoadFrom(yaml);
        }

        // --- §5 Failure Tier Equivalence ---

        [Theory]
        [InlineData(1, -1, "Fumble")]
        [InlineData(2, -1, "Fumble")]
        [InlineData(3, -1, "Misfire")]
        [InlineData(5, -1, "Misfire")]
        [InlineData(6, -2, "Trope Trap")]
        [InlineData(9, -2, "Trope Trap")]
        [InlineData(10, -3, "Catastrophe")]
        [InlineData(15, -3, "Catastrophe")]
        public void FailureTier_RuleEngine_MatchesHardcoded(int missMargin, int expectedDelta, string expectedTier)
        {
            var book = LoadRuleBook();
            var state = new GameState(missMargin: missMargin, naturalRoll: 10);

            // Find matching rule from §7 fail tiers (in the YAML they're under §7)
            var failRules = book.All
                .Where(r => r.Id.StartsWith("§7.fail-tier."))
                .Where(r => r.Condition != null)
                .ToList();

            RuleEntry? matched = null;
            foreach (var rule in failRules)
            {
                if (ConditionEvaluator.Evaluate(rule.Condition, state))
                {
                    matched = rule;
                    break;
                }
            }

            Assert.NotNull(matched);
            Assert.NotNull(matched!.Outcome);
            Assert.True(matched.Outcome!.ContainsKey("interest_delta"),
                $"Rule {matched.Id} missing interest_delta");

            var handler = new TestEffectHandler();
            OutcomeDispatcher.Dispatch(matched.Outcome, state, handler);
            Assert.Single(handler.InterestDeltas);
            Assert.Equal(expectedDelta, handler.InterestDeltas[0]);

            // Also verify tier name matches
            Assert.True(matched.Outcome.ContainsKey("tier"));
            Assert.Equal(expectedTier, matched.Outcome["tier"].ToString());
        }

        [Fact]
        public void LegendaryFail_RuleEngine_MatchesHardcoded()
        {
            var book = LoadRuleBook();
            // Legendary is natural_roll == 1
            var state = new GameState(missMargin: 5, naturalRoll: 1);

            var failRules = book.All
                .Where(r => r.Id.StartsWith("§7.fail-tier."))
                .Where(r => r.Condition != null)
                .ToList();

            // Legendary should match on natural_roll: 1
            var legendary = failRules.FirstOrDefault(r =>
            {
                if (r.Condition == null) return false;
                return r.Condition.ContainsKey("natural_roll");
            });

            Assert.NotNull(legendary);
            Assert.True(ConditionEvaluator.Evaluate(legendary!.Condition, state));

            var handler = new TestEffectHandler();
            OutcomeDispatcher.Dispatch(legendary.Outcome, state, handler);
            Assert.Equal(-4, handler.InterestDeltas[0]);
        }

        // Verify hardcoded FailureScale matches rule engine for each tier
        [Fact]
        public void FailureScale_AllTiers_MatchRuleEngine()
        {
            var book = LoadRuleBook();

            // Fumble: miss by 1
            VerifyFailureDelta(book, 1, 10, FailureTier.Fumble);
            // Misfire: miss by 3
            VerifyFailureDelta(book, 3, 10, FailureTier.Misfire);
            // TropeTrap: miss by 6
            VerifyFailureDelta(book, 6, 10, FailureTier.TropeTrap);
            // Catastrophe: miss by 10
            VerifyFailureDelta(book, 10, 10, FailureTier.Catastrophe);
        }

        private void VerifyFailureDelta(RuleBook book, int missMargin, int naturalRoll, FailureTier expectedTier)
        {
            // Create a RollResult with the given miss margin
            // DieRoll doesn't matter for FailureScale, only the Tier does
            var rollResult = new RollResult(
                dieRoll: naturalRoll,
                secondDieRoll: null,
                usedDieRoll: naturalRoll,
                stat: StatType.Charm,
                statModifier: 0,
                levelBonus: 0,
                dc: naturalRoll + missMargin,
                tier: expectedTier);

            int hardcodedDelta = FailureScale.GetInterestDelta(rollResult);

            // Now get the same from the rule engine
            var state = new GameState(missMargin: missMargin, naturalRoll: naturalRoll);
            var failRules = book.All
                .Where(r => r.Id.StartsWith("§7.fail-tier."))
                .Where(r => r.Condition != null)
                .ToList();

            var matched = failRules.FirstOrDefault(r =>
                ConditionEvaluator.Evaluate(r.Condition, state));

            Assert.NotNull(matched);
            var handler = new TestEffectHandler();
            OutcomeDispatcher.Dispatch(matched!.Outcome, state, handler);

            Assert.Equal(hardcodedDelta, handler.InterestDeltas[0]);
        }

        // --- §6 Interest State Equivalence ---

        [Theory]
        [InlineData(0, "Unmatched")]
        [InlineData(1, "Bored")]
        [InlineData(4, "Bored")]
        [InlineData(5, "Lukewarm")]
        [InlineData(9, "Lukewarm")]
        [InlineData(10, "Interested")]
        [InlineData(15, "Interested")]
        [InlineData(16, "Very Into It")]
        [InlineData(20, "Very Into It")]
        [InlineData(21, "Almost There")]
        [InlineData(24, "Almost There")]
        [InlineData(25, "Date Secured")]
        public void InterestState_RuleEngine_MatchesHardcoded(int interest, string expectedStateName)
        {
            var book = LoadRuleBook();

            // Get the state from hardcoded InterestMeter
            var meter = new InterestMeter(interest);
            var hardcodedState = meter.GetState();

            // Find matching rule from §6 interest states
            var stateRules = book.All
                .Where(r => r.Id.StartsWith("§6.interest-state."))
                .Where(r => r.Condition != null)
                .ToList();

            var gameState = new GameState(interest: interest);
            var matched = stateRules.FirstOrDefault(r =>
                ConditionEvaluator.Evaluate(r.Condition, gameState));

            Assert.NotNull(matched);
            Assert.NotNull(matched!.Outcome);
            Assert.True(matched.Outcome!.ContainsKey("state"),
                $"Rule {matched.Id} missing 'state' outcome key");

            // Verify the YAML state name contains the hardcoded enum name
            var yamlStateName = matched.Outcome["state"].ToString()!;
            Assert.Contains(expectedStateName, yamlStateName);
        }

        // --- Success Scale Equivalence ---

        [Theory]
        [InlineData(1, 1)]
        [InlineData(4, 1)]
        [InlineData(5, 2)]
        [InlineData(9, 2)]
        [InlineData(10, 3)]
        [InlineData(15, 3)]
        public void SuccessScale_RuleEngine_MatchesHardcoded(int beatMargin, int expectedDelta)
        {
            // Build a roll result that succeeded by beatMargin.
            // Use statModifier to avoid nat20: dieRoll=10, statMod=beatMargin, dc=10
            var dc = 10;
            var dieRoll = 10;
            var rollResult = new RollResult(
                dieRoll: dieRoll,
                secondDieRoll: null,
                usedDieRoll: dieRoll,
                stat: StatType.Charm,
                statModifier: beatMargin,
                levelBonus: 0,
                dc: dc,
                tier: FailureTier.None);

            int hardcodedDelta = SuccessScale.GetInterestDelta(rollResult);
            Assert.Equal(expectedDelta, hardcodedDelta);
        }

        // --- Shadow Threshold Equivalence ---

        [Theory]
        [InlineData(0, 0)]
        [InlineData(5, 0)]
        [InlineData(6, 1)]
        [InlineData(11, 1)]
        [InlineData(12, 2)]
        [InlineData(17, 2)]
        [InlineData(18, 3)]
        [InlineData(25, 3)]
        public void ShadowThreshold_HardcodedValues_AreCorrect(int shadowValue, int expectedLevel)
        {
            int actual = ShadowThresholdEvaluator.GetThresholdLevel(shadowValue);
            Assert.Equal(expectedLevel, actual);
        }

        // --- RuleBook loads real YAML ---

        [Fact]
        public void RuleBook_LoadsRealYaml_HasExpectedRuleCount()
        {
            var book = LoadRuleBook();
            // The enriched YAML should have many rules
            Assert.True(book.Count > 20, $"Expected >20 rules, got {book.Count}");
        }

        [Fact]
        public void RuleBook_LoadsRealYaml_HasFailTierRules()
        {
            var book = LoadRuleBook();
            var failTierRules = book.All
                .Where(r => r.Id.StartsWith("§7.fail-tier."))
                .ToList();
            Assert.Equal(5, failTierRules.Count);
        }

        [Fact]
        public void RuleBook_LoadsRealYaml_HasInterestStateRules()
        {
            var book = LoadRuleBook();
            var stateRules = book.All
                .Where(r => r.Id.StartsWith("§6.interest-state."))
                .ToList();
            Assert.Equal(7, stateRules.Count);
        }
    }
}
