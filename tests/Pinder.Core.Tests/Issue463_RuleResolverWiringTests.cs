using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Pinder.Core.Characters;
using Pinder.Core.Conversation;
using Pinder.Core.Interfaces;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;
using Pinder.Core.Traps;
using Xunit;

namespace Pinder.Core.Tests
{
    /// <summary>
    /// Tests for Issue #463: Wire GameSession to use IRuleResolver for §5/§6/§7/§15 rules.
    /// Verifies the Dependency Inversion wiring, fallback behavior, and edge cases.
    /// </summary>
    [Trait("Category", "Rules")]
    public partial class Issue463_RuleResolverWiringTests
    {
        // =====================================================================
        // Test-only mock: tracks calls and returns configurable values
        // =====================================================================
        private sealed class StubRuleResolver : IRuleResolver
        {
            public int? FailureDeltaReturn { get; set; }
            public int? SuccessDeltaReturn { get; set; }
            public InterestState? InterestStateReturn { get; set; }
            public int? ShadowThresholdReturn { get; set; }
            public int? MomentumReturn { get; set; }
            public double? XpMultiplierReturn { get; set; }

            // Call tracking
            public List<(int missMargin, int naturalRoll)> FailureDeltaCalls { get; } = new List<(int, int)>();
            public List<(int beatMargin, int naturalRoll)> SuccessDeltaCalls { get; } = new List<(int, int)>();
            public List<int> InterestStateCalls { get; } = new List<int>();
            public List<int> ShadowThresholdCalls { get; } = new List<int>();
            public List<int> MomentumCalls { get; } = new List<int>();
            public List<RiskTier> XpMultiplierCalls { get; } = new List<RiskTier>();

            public int? GetFailureInterestDelta(int missMargin, int naturalRoll)
            {
                FailureDeltaCalls.Add((missMargin, naturalRoll));
                return FailureDeltaReturn;
            }

            public int? GetSuccessInterestDelta(int beatMargin, int naturalRoll)
            {
                SuccessDeltaCalls.Add((beatMargin, naturalRoll));
                return SuccessDeltaReturn;
            }

            public InterestState? GetInterestState(int interest)
            {
                InterestStateCalls.Add(interest);
                return InterestStateReturn;
            }

            public int? GetShadowThresholdLevel(int shadowValue)
            {
                ShadowThresholdCalls.Add(shadowValue);
                return ShadowThresholdReturn;
            }

            public int? GetMomentumBonus(int streak)
            {
                MomentumCalls.Add(streak);
                return MomentumReturn;
            }

            public double? GetRiskTierXpMultiplier(RiskTier riskTier)
            {
                XpMultiplierCalls.Add(riskTier);
                return XpMultiplierReturn;
            }

            public double? GetTerminalOutcomeMultiplier(GameOutcome outcome) => null;
            public int? GetSuccessBaseXp(int dc) => null;
            public int? GetFlatXpAward(string awardType) => null;
        }

        private static CharacterProfile MakeProfile(string name, int allStats = 2)
        {
            return new CharacterProfile(
                stats: TestHelpers.MakeStatBlock(allStats),
                assembledSystemPrompt: $"You are {name}.",
                displayName: name,
                timing: new TimingProfile(5, 0.0f, 0.0f, "neutral"),
                level: 1);
        }

        // =====================================================================
        // AC-1: GameSessionConfig accepts IRuleResolver
        // =====================================================================

        // Fails if: GameSessionConfig constructor drops the rules parameter
        [Fact]
        public void GameSessionConfig_Rules_Property_StoresResolver()
        {
            var resolver = new StubRuleResolver();
            var config = new GameSessionConfig(clock: TestHelpers.MakeClock(), rules: resolver);
            Assert.Same(resolver, config.Rules);
        }

        // Fails if: GameSessionConfig default for rules is non-null
        [Fact]
        public void GameSessionConfig_Rules_DefaultsToNull()
        {
            var config = new GameSessionConfig(clock: TestHelpers.MakeClock());
            Assert.Null(config.Rules);
        }

        // =====================================================================
        // AC-2: §5 Failure delta flows through resolver
        // =====================================================================

        // Fails if: GameSession doesn't call GetFailureInterestDelta on failure rolls
        [Fact]
        public async Task ResolveTurn_OnFailure_CallsResolverForFailureDelta()
        {
            var resolver = new StubRuleResolver { FailureDeltaReturn = -1 };
            var config = new GameSessionConfig(clock: TestHelpers.MakeClock(), rules: resolver, startingInterest: 15);

            // Dice: horniness(1d10)=1, d20=2 (low roll, likely fail)
            var dice = new FixedDice(1, 2, 50, 50, 50, 50, 50, 50, 50, 50, 50);
            var session = new GameSession(
                MakeProfile("Player"), MakeProfile("Datee"),
                new NullLlmAdapter(), dice, new NullTrapRegistry(), config);

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.False(result.Roll.IsSuccess, "Expected failure roll — check FixedDice setup");
            Assert.True(resolver.FailureDeltaCalls.Count > 0,
                "Expected GetFailureInterestDelta to be called on a failed roll");
        }

        // Fails if: GameSession ignores the resolver's failure delta value
        [Fact]
        public async Task ResolveTurn_OnFailure_UsesResolverValue()
        {
            // Custom failure delta much larger than any hardcoded value
            var resolver = new StubRuleResolver { FailureDeltaReturn = -10 };
            var config = new GameSessionConfig(clock: TestHelpers.MakeClock(), rules: resolver, startingInterest: 15);

            // d20=2, very likely to fail against DC 13+
            var dice = new FixedDice(1, 2, 50, 50, 50, 50, 50, 50, 50, 50, 50);
            var session = new GameSession(
                MakeProfile("Player"), MakeProfile("Datee"),
                new NullLlmAdapter(), dice, new NullTrapRegistry(), config);

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.False(result.Roll.IsSuccess, "Expected failure roll — check FixedDice setup");
            // With -10 delta from resolver, interest should drop significantly from 15
            // Hardcoded max failure delta is -4 (Legendary), so if we see interest <= 5,
            // the resolver value was used rather than hardcoded
            Assert.True(result.StateAfter.Interest < 10,
                $"Expected interest < 10 with custom failure delta -10, got {result.StateAfter.Interest}");
        }

        // =====================================================================
        // AC-2: §5 Success delta flows through resolver
        // =====================================================================

        // Fails if: GameSession doesn't call GetSuccessInterestDelta on successful rolls
        [Fact]
        public async Task ResolveTurn_OnSuccess_CallsResolverForSuccessDelta()
        {
            var resolver = new StubRuleResolver { SuccessDeltaReturn = 2 };
            var config = new GameSessionConfig(clock: TestHelpers.MakeClock(), rules: resolver, startingInterest: 10);

            // Nat 20 = guaranteed success
            var dice = new FixedDice(1, 20, 50, 50, 50, 50, 50, 50, 50, 50, 50);
            var session = new GameSession(
                MakeProfile("Player"), MakeProfile("Datee"),
                new NullLlmAdapter(), dice, new NullTrapRegistry(), config);

            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);

            Assert.True(resolver.SuccessDeltaCalls.Count > 0,
                "Expected GetSuccessInterestDelta to be called on a successful roll");
        }

        // Fails if: GameSession ignores the resolver's success delta value (uses hardcoded instead)
        [Fact]
        public async Task ResolveTurn_OnSuccess_UsesResolverSuccessValue()
        {
            // Custom success delta of +10 (way above any hardcoded value of +1 to +4)
            var resolver = new StubRuleResolver { SuccessDeltaReturn = 10 };
            var config = new GameSessionConfig(clock: TestHelpers.MakeClock(), rules: resolver, startingInterest: 10);

            // Nat 20 = guaranteed success
            var dice = new FixedDice(1, 20, 50, 50, 50, 50, 50, 50, 50, 50, 50);
            var session = new GameSession(
                MakeProfile("Player"), MakeProfile("Datee"),
                new NullLlmAdapter(), dice, new NullTrapRegistry(), config);

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            // With +10 success delta, interest from 10 should reach cap (25)
            // Hardcoded nat20 gives +4, so if interest > 20, resolver value was used
            Assert.True(result.StateAfter.Interest > 14,
                $"Expected interest > 14 with resolver success delta +10, got {result.StateAfter.Interest}");
        }
    }
}
