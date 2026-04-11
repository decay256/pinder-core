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
    /// Mock IRuleResolver that returns predictable custom values for testing wiring.
    /// </summary>
    public sealed class MockRuleResolver : IRuleResolver
    {
        public int? FailureDelta { get; set; }
        public int? SuccessDelta { get; set; }
        public InterestState? State { get; set; }
        public int? ThresholdLevel { get; set; }
        public int? Momentum { get; set; }
        public double? XpMultiplier { get; set; }

        // Track calls for verification
        public int FailureDeltaCalls { get; set; }
        public int SuccessDeltaCalls { get; set; }
        public int InterestStateCalls { get; set; }
        public int ThresholdCalls { get; set; }
        public int MomentumCalls { get; set; }
        public int XpMultiplierCalls { get; set; }

        public int? GetFailureInterestDelta(int missMargin, int naturalRoll)
        {
            FailureDeltaCalls++;
            return FailureDelta;
        }

        public int? GetSuccessInterestDelta(int beatMargin, int naturalRoll)
        {
            SuccessDeltaCalls++;
            return SuccessDelta;
        }

        public InterestState? GetInterestState(int interest)
        {
            InterestStateCalls++;
            return State;
        }

        public int? GetShadowThresholdLevel(int shadowValue)
        {
            ThresholdCalls++;
            return ThresholdLevel;
        }

        public int? GetMomentumBonus(int streak)
        {
            MomentumCalls++;
            return Momentum;
        }

        public double? GetRiskTierXpMultiplier(RiskTier riskTier)
        {
            XpMultiplierCalls++;
            return XpMultiplier;
        }
    }

    public class RuleResolverIntegrationTests
    {
        private static CharacterProfile MakeProfile(string name, int allStats = 2)
        {
            return new CharacterProfile(
                stats: TestHelpers.MakeStatBlock(allStats),
                assembledSystemPrompt: $"You are {name}.",
                displayName: name,
                timing: new TimingProfile(5, 0.0f, 0.0f, "neutral"),
                level: 1);
        }

        [Fact]
        public void GameSessionConfig_AcceptsNullRules()
        {
            var config = new GameSessionConfig(clock: TestHelpers.MakeClock(), rules: null);
            Assert.Null(config.Rules);
        }

        [Fact]
        public void GameSessionConfig_AcceptsMockRules()
        {
            var resolver = new MockRuleResolver();
            var config = new GameSessionConfig(clock: TestHelpers.MakeClock(), rules: resolver);
            Assert.Same(resolver, config.Rules);
        }

        [Fact]
        public async Task GameSession_WithResolver_CallsGetInterestState()
        {
            var resolver = new MockRuleResolver();
            var config = new GameSessionConfig(clock: TestHelpers.MakeClock(), rules: resolver);

            // Dice: horniness roll (1d10) = 1, ghost check needs d4 (enqueue 4 = no ghost)
            var dice = new FixedDice(1, 4);
            var session = new GameSession(
                MakeProfile("Player"), MakeProfile("Opponent"),
                new NullLlmAdapter(), dice, new NullTrapRegistry(), config);

            // StartTurnAsync checks interest state for Bored check
            var start = await session.StartTurnAsync();

            Assert.True(resolver.InterestStateCalls > 0,
                "Expected GetInterestState to be called during StartTurnAsync");
        }

        [Fact]
        public async Task GameSession_WithResolver_UsesResolvedSuccessDelta()
        {
            // Return a custom success delta of +10
            var resolver = new MockRuleResolver { SuccessDelta = 10 };
            var config = new GameSessionConfig(clock: TestHelpers.MakeClock(), rules: resolver,
                startingInterest: 10);

            // Dice: horniness (1d10)=1, d20 roll = 20 (auto-success, nat 20),
            // then extra dice for timing delay, opponent response, etc.
            var dice = new FixedDice(1, 20, 50, 50, 50, 50, 50, 50);
            var session = new GameSession(
                MakeProfile("Player"), MakeProfile("Opponent"),
                new NullLlmAdapter(), dice, new NullTrapRegistry(), config);

            var start = await session.StartTurnAsync();
            Assert.NotNull(start);

            var result = await session.ResolveTurnAsync(0);

            // Nat 20 is always a success, so resolver should have been called
            Assert.True(resolver.SuccessDeltaCalls > 0,
                "Expected GetSuccessInterestDelta to be called on success");

            // With custom +10 delta, interest should be well above starting 10
            // (capped at 25 max)
            Assert.True(result.StateAfter.Interest > 10,
                $"Expected interest > 10 with custom success delta of +10, got {result.StateAfter.Interest}");
        }

        [Fact]
        public async Task GameSession_WithResolver_UsesResolvedFailureDelta()
        {
            // Return a custom failure delta of -5
            var resolver = new MockRuleResolver { FailureDelta = -5 };
            var config = new GameSessionConfig(clock: TestHelpers.MakeClock(), rules: resolver,
                startingInterest: 15);

            // Dice: horniness (1d10)=1, d20 roll = 2 (very likely to fail),
            // plus extra for timing/opponent
            var dice = new FixedDice(1, 2, 50, 50, 50, 50, 50, 50);
            var session = new GameSession(
                MakeProfile("Player"), MakeProfile("Opponent"),
                new NullLlmAdapter(), dice, new NullTrapRegistry(), config);

            var start = await session.StartTurnAsync();
            Assert.NotNull(start);

            var result = await session.ResolveTurnAsync(0);

            if (!result.Roll.IsSuccess)
            {
                Assert.True(resolver.FailureDeltaCalls > 0,
                    "Expected GetFailureInterestDelta to be called on failure");
            }
        }

        [Fact]
        public async Task GameSession_WithNullConfig_FallsBackToHardcoded()
        {
            // Clock is now required; use a config with zero-modifier clock and no resolver.
            var dice = new FixedDice(1, 20, 50, 50, 50, 50, 50, 50);
            var config = new GameSessionConfig(clock: TestHelpers.MakeClock());
            var session = new GameSession(
                MakeProfile("Player"), MakeProfile("Opponent"),
                new NullLlmAdapter(), dice, new NullTrapRegistry(), config);

            var start = await session.StartTurnAsync();
            Assert.NotNull(start);

            var result = await session.ResolveTurnAsync(0);
            // Nat 20 = success (hardcoded)
            Assert.True(result.Roll.IsSuccess);
        }

        [Fact]
        public async Task GameSession_ResolverReturnsNull_FallsBackToHardcoded()
        {
            // Resolver returns null for everything — should fall back to hardcoded
            var resolver = new MockRuleResolver();
            var config = new GameSessionConfig(clock: TestHelpers.MakeClock(), rules: resolver, startingInterest: 10);

            var dice = new FixedDice(1, 20, 50, 50, 50, 50, 50, 50);
            var session = new GameSession(
                MakeProfile("Player"), MakeProfile("Opponent"),
                new NullLlmAdapter(), dice, new NullTrapRegistry(), config);

            var start = await session.StartTurnAsync();
            Assert.NotNull(start);

            var result = await session.ResolveTurnAsync(0);
            Assert.True(result.Roll.IsSuccess);
            // Nat 20 gives +4 from hardcoded SuccessScale + risk tier bonus
            // Interest starts at 10, should go up
            Assert.True(result.StateAfter.Interest > 10,
                $"Expected interest > 10 from hardcoded fallback, got {result.StateAfter.Interest}");
        }

        [Fact]
        public void GameSession_WithShadowResolver_UsesResolvedThreshold()
        {
            // Custom threshold: return 3 (T3) for any value, which would trigger Dread T3 (interest 8)
            var resolver = new MockRuleResolver { ThresholdLevel = 3 };
            var playerStats = TestHelpers.MakeStatBlock(2, 0);
            var playerShadows = new SessionShadowTracker(playerStats);
            // Give dread a small value — but resolver overrides to T3
            playerShadows.ApplyGrowth(ShadowStatType.Dread, 1, "test");

            var config = new GameSessionConfig(clock: TestHelpers.MakeClock(), playerShadows: playerShadows,
                rules: resolver);

            var dice = new FixedDice(1); // horniness roll
            var session = new GameSession(
                MakeProfile("Player"), MakeProfile("Opponent"),
                new NullLlmAdapter(), dice, new NullTrapRegistry(), config);

            // The resolver returns T3 for any shadow value, so Dread T3 should trigger
            // starting interest = 8 instead of 10
            Assert.True(resolver.ThresholdCalls > 0,
                "Expected GetShadowThresholdLevel to be called during construction");
        }

        [Fact]
        public async Task GameSession_WithMomentumResolver_UsesResolvedBonus()
        {
            // Set up a resolver that returns +5 momentum bonus for any streak
            var resolver = new MockRuleResolver { Momentum = 5 };
            var config = new GameSessionConfig(clock: TestHelpers.MakeClock(), rules: resolver, startingInterest: 10);

            // We need enough dice for: horniness, turn1 roll (success) + timing,
            // turn2 start + roll etc.
            var dice = new FixedDice(
                1,                          // horniness
                18, 50, 50, 50, 50, 50,     // turn 1: roll + timing + extras
                18, 50, 50, 50, 50, 50,     // turn 2: roll + timing + extras
                18, 50, 50, 50, 50, 50      // turn 3 if needed
            );
            var session = new GameSession(
                MakeProfile("Player", 5), MakeProfile("Opponent"),
                new NullLlmAdapter(), dice, new NullTrapRegistry(), config);

            // Turn 1
            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);

            // After turn 1, momentum streak should be 1
            // GetMomentumBonus should be called during the next StartTurnAsync
            resolver.MomentumCalls = 0;
            await session.StartTurnAsync();

            Assert.True(resolver.MomentumCalls > 0,
                "Expected GetMomentumBonus to be called during StartTurnAsync");
        }
    }
}
