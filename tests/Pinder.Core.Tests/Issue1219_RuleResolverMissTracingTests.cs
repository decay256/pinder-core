using System;
using System.Linq;
using System.Reflection;
using Pinder.Core.Conversation;
using Pinder.Core.Interfaces;
using Pinder.Core.Rolls;
using Xunit;

namespace Pinder.Core.Tests
{
    [Trait("Category", "Core")]
    public class Issue1219_RuleResolverMissTracingTests
    {
        private class FakeRuleResolver : IRuleResolver
        {
            public int? MomentumBonus { get; set; }
            public int? FailureInterestDelta { get; set; }
            public int? SuccessInterestDelta { get; set; }
            public InterestState? InterestState { get; set; }
            public int? ShadowThresholdLevel { get; set; }
            public double? RiskTierXpMultiplierValue { get; set; }
            public double? TerminalOutcomeMultiplierValue { get; set; }
            public int? SuccessBaseXpValue { get; set; }
            public int? FlatXpAwardValue { get; set; }
            public int? XpThresholdForLevelValue { get; set; }
            public int? LevelRollBonusValue { get; set; }
            public int? BuildPointsForLevelValue { get; set; }
            public int? ItemSlotsForLevelValue { get; set; }

            public int? GetFailureInterestDelta(int missMargin, int naturalRoll) => FailureInterestDelta;
            public int? GetSuccessInterestDelta(int beatMargin, int naturalRoll) => SuccessInterestDelta;
            public InterestState? GetInterestState(int interest) => InterestState;
            public int? GetShadowThresholdLevel(int shadowValue) => ShadowThresholdLevel;
            public int? GetMomentumBonus(int streak) => MomentumBonus;
            public double? GetRiskTierXpMultiplier(RiskTier riskTier) => RiskTierXpMultiplierValue;
            public double? GetTerminalOutcomeMultiplier(GameOutcome outcome) => TerminalOutcomeMultiplierValue;
            public int? GetSuccessBaseXp(int dc) => SuccessBaseXpValue;
            public Pinder.Core.Progression.SuccessDcLabelThresholds? GetSuccessDcLabelThresholds() => null;
            public int? GetFlatXpAward(string awardType) => FlatXpAwardValue;
            public int? GetXpThresholdForLevel(int level) => XpThresholdForLevelValue;
            public int? GetLevelRollBonus(int level) => LevelRollBonusValue;
            public int? GetBuildPointsForLevel(int level) => BuildPointsForLevelValue;
            public int? GetItemSlotsForLevel(int level) => ItemSlotsForLevelValue;
            public int? GetFailurePoolTierMinLevel(string tierName) => null;

            // Behaves like a production resolver: unresolved rules fall back to defaults.
            public bool AllowDefaultFallback => true;
        }

        [Fact]
        public void Test1_GameSessionConfig_CallbackProperty_Exists()
        {
            // Assert GameSessionConfig exposes a public property whose name CONTAINS 'Rule'
            // AND ('Resolution' OR 'Resolver' OR 'Trace' OR 'Source') (case-insensitive)
            // and whose type is a delegate (assignable to System.Delegate).
            var properties = typeof(GameSessionConfig).GetProperties(BindingFlags.Public | BindingFlags.Instance);
            var matched = properties.Where(p =>
            {
                var lowerName = p.Name.ToLowerInvariant();
                bool containsRule = lowerName.Contains("rule");
                bool containsSub = lowerName.Contains("resolution") ||
                                  lowerName.Contains("resolver") ||
                                  lowerName.Contains("trace") ||
                                  lowerName.Contains("source");
                bool isDelegate = typeof(Delegate).IsAssignableFrom(p.PropertyType);
                return containsRule && containsSub && isDelegate;
            }).ToList();

            Assert.True(matched.Count > 0, 
                "Expected GameSessionConfig to expose a public property representing a Rule Resolution trace callback delegate, but none was found.");
        }

        [Fact]
        public void Test2_RuleResolutionTraceEventType_Exists()
        {
            // Assert a type whose name contains 'RuleResolution' OR 'RuleResolverTrace' exists in the Pinder.Core assembly.
            var assembly = typeof(GameSessionConfig).Assembly;
            var types = assembly.GetTypes();
            var matchedTypes = types.Where(t =>
                t.Name.Contains("RuleResolution") ||
                t.Name.Contains("RuleResolverTrace")).ToList();

            Assert.True(matchedTypes.Count > 0,
                "Expected a structured rule resolution trace type (e.g. RuleResolutionEvent) to exist in the Pinder.Core assembly, but none was found.");
        }

        [Theory]
        [InlineData(5, 3)]
        [InlineData(4, 2)]
        [InlineData(3, 2)]
        [InlineData(2, 0)]
        [InlineData(0, 0)]
        public void Test3_ParityGuard_ResolverMiss_ReturnsHardcodedValue(int streak, int expectedBonus)
        {
            // With a resolver returning null (MISS), it must fall back to the hardcoded value.
            var fakeResolver = new FakeRuleResolver { MomentumBonus = null };
            var actualWithResolver = TurnOrchestratorHelpers.GetMomentumBonus(streak, fakeResolver);
            Assert.Equal(expectedBonus, actualWithResolver);

            // With no resolver (null), it must return the same hardcoded value.
            var actualWithNoResolver = TurnOrchestratorHelpers.GetMomentumBonus(streak, null);
            Assert.Equal(expectedBonus, actualWithNoResolver);
        }

        [Theory]
        [InlineData(5, 7)]
        [InlineData(3, 10)]
        [InlineData(0, -1)]
        public void Test4_ParityGuard_ResolverHit_ReturnsResolverValue(int streak, int resolverValue)
        {
            // When resolver returns a value (HIT), that value must win.
            var fakeResolver = new FakeRuleResolver { MomentumBonus = resolverValue };
            var actual = TurnOrchestratorHelpers.GetMomentumBonus(streak, fakeResolver);
            Assert.Equal(resolverValue, actual);
        }

        [Fact]
        public void Test5_ResolverHit_TracesExpectedEvent()
        {
            var fakeResolver = new FakeRuleResolver { MomentumBonus = 42 };
            RuleResolutionTraceEvent? trace = null;
            Action<RuleResolutionTraceEvent> callback = ev => trace = ev;

            var result = TurnOrchestratorHelpers.GetMomentumBonus(5, fakeResolver, callback);

            Assert.Equal(42, result);
            Assert.NotNull(trace);
            Assert.Equal("momentum_bonus", trace.RuleKey);
            Assert.Equal("resolver", trace.Source);
            Assert.True(trace.ResolverConfigured);
            Assert.Equal(42, trace.NumericValue);
            Assert.Null(trace.StateValue);
        }

        [Fact]
        public void Test6_ResolverMiss_TracesExpectedEvent()
        {
            RuleResolutionTraceEvent? trace = null;
            Action<RuleResolutionTraceEvent> callback = ev => trace = ev;

            var result = TurnOrchestratorHelpers.GetMomentumBonus(5, null, callback);

            Assert.Equal(3, result);
            Assert.NotNull(trace);
            Assert.Equal("momentum_bonus", trace.RuleKey);
            Assert.Equal("hardcoded_fallback", trace.Source);
            Assert.False(trace.ResolverConfigured);
            Assert.Equal(3, trace.NumericValue);
            Assert.Null(trace.StateValue);
        }
    }
}
