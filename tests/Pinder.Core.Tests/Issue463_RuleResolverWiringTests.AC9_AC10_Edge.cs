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
    public partial class Issue463_RuleResolverWiringTests
    {
        // =====================================================================
        // AC-9/AC-10: Fallback to hardcoded when resolver is null
        // =====================================================================

        // Fails if: GameSession throws when no resolver is provided
        [Fact]
        public async Task GameSession_NullResolver_WorksWithHardcodedFallback()
        {
            var config = new GameSessionConfig(clock: TestHelpers.MakeClock(), startingInterest: 10);

            var dice = new FixedDice(1, 20, 50, 50, 50, 50, 50, 50, 50, 50, 50);
            var session = new GameSession(
                MakeProfile("Player"), MakeProfile("Opponent"),
                new NullLlmAdapter(), dice, new NullTrapRegistry(), config);

            var start = await session.StartTurnAsync();
            Assert.NotNull(start);

            var result = await session.ResolveTurnAsync(0);
            Assert.True(result.Roll.IsSuccess, "Nat 20 should always succeed");
            Assert.True(result.StateAfter.Interest > 10,
                "Hardcoded success scale should increase interest from 10");
        }

        // Fails if: GameSession throws when no config is provided at all
        [Fact]
        public async Task GameSession_NoConfig_WorksWithHardcodedFallback()
        {
            // Clock is now required; use a config with zero-modifier clock and no resolver.
            var dice = new FixedDice(1, 20, 50, 50, 50, 50, 50, 50, 50, 50, 50);
            var config = new GameSessionConfig(clock: TestHelpers.MakeClock());
            var session = new GameSession(
                MakeProfile("Player"), MakeProfile("Opponent"),
                new NullLlmAdapter(), dice, new NullTrapRegistry(), config);

            var start = await session.StartTurnAsync();
            Assert.NotNull(start);

            var result = await session.ResolveTurnAsync(0);
            Assert.True(result.Roll.IsSuccess, "Nat 20 should always succeed");
        }

        // =====================================================================
        // AC-10: Fallback when resolver returns null for specific lookups
        // =====================================================================

        // Fails if: GameSession throws or uses zero when resolver returns null (instead of falling back to hardcoded)
        [Fact]
        public async Task ResolveTurn_ResolverReturnsNull_FallsBackToHardcoded()
        {
            // Resolver returns null for everything
            var resolver = new StubRuleResolver();
            var config = new GameSessionConfig(clock: TestHelpers.MakeClock(), rules: resolver, startingInterest: 10);

            // Nat 20 → success
            var dice = new FixedDice(1, 20, 50, 50, 50, 50, 50, 50, 50, 50, 50);
            var session = new GameSession(
                MakeProfile("Player"), MakeProfile("Opponent"),
                new NullLlmAdapter(), dice, new NullTrapRegistry(), config);

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            // Hardcoded Nat 20 gives +4 (plus risk tier bonus), should increase interest
            Assert.True(result.Roll.IsSuccess);
            Assert.True(result.StateAfter.Interest > 10,
                $"Expected interest > 10 from hardcoded fallback on null resolver, got {result.StateAfter.Interest}");
        }

        // Fails if: Partial null (some methods return values, some null) causes error
        [Fact]
        public async Task ResolveTurn_PartialResolver_MixesFallbackAndResolved()
        {
            // Resolver only returns success delta, everything else is null
            var resolver = new StubRuleResolver { SuccessDeltaReturn = 7 };
            var config = new GameSessionConfig(clock: TestHelpers.MakeClock(), rules: resolver, startingInterest: 10);

            var dice = new FixedDice(1, 20, 50, 50, 50, 50, 50, 50, 50, 50, 50);
            var session = new GameSession(
                MakeProfile("Player"), MakeProfile("Opponent"),
                new NullLlmAdapter(), dice, new NullTrapRegistry(), config);

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            // Interest should go up by at least 7 (resolver success delta) + risk tier bonus
            Assert.True(result.StateAfter.Interest >= 17,
                $"Expected interest >= 17 with resolver success delta 7, got {result.StateAfter.Interest}");
        }

        // =====================================================================
        // Edge case: Resolver provided but all methods return null (degenerate case)
        // =====================================================================

        // Fails if: All-null resolver doesn't behave identically to no resolver
        [Fact]
        public async Task AllNullResolver_BehavesIdenticallyToNoResolver()
        {
            // With null resolver
            var config1 = new GameSessionConfig(clock: TestHelpers.MakeClock(), startingInterest: 10);
            var dice1 = new FixedDice(1, 20, 50, 50, 50, 50, 50, 50, 50, 50, 50);
            var session1 = new GameSession(
                MakeProfile("Player"), MakeProfile("Opponent"),
                new NullLlmAdapter(), dice1, new NullTrapRegistry(), config1);
            await session1.StartTurnAsync();
            var result1 = await session1.ResolveTurnAsync(0);

            // With all-null resolver
            var resolver = new StubRuleResolver();
            var config2 = new GameSessionConfig(clock: TestHelpers.MakeClock(), rules: resolver, startingInterest: 10);
            var dice2 = new FixedDice(1, 20, 50, 50, 50, 50, 50, 50, 50, 50, 50);
            var session2 = new GameSession(
                MakeProfile("Player"), MakeProfile("Opponent"),
                new NullLlmAdapter(), dice2, new NullTrapRegistry(), config2);
            await session2.StartTurnAsync();
            var result2 = await session2.ResolveTurnAsync(0);

            Assert.Equal(result1.StateAfter.Interest, result2.StateAfter.Interest);
        }

        // =====================================================================
        // Edge case: Boundary interest values for state resolution
        // =====================================================================

        // Fails if: GetInterestState is passed wrong interest value
        [Fact]
        public async Task InterestStateResolver_ReceivesCurrentInterestValue()
        {
            var resolver = new StubRuleResolver();
            var config = new GameSessionConfig(clock: TestHelpers.MakeClock(), rules: resolver, startingInterest: 15);

            var dice = new FixedDice(1, 4);
            var session = new GameSession(
                MakeProfile("Player"), MakeProfile("Opponent"),
                new NullLlmAdapter(), dice, new NullTrapRegistry(), config);

            await session.StartTurnAsync();

            // Should have called with interest value 15 (the starting interest)
            Assert.Contains(15, resolver.InterestStateCalls);
        }

        // =====================================================================
        // Edge case: Shadow value of 0
        // =====================================================================

        // Fails if: Shadow threshold resolver is called with wrong value for zero shadow
        [Fact]
        public void Constructor_ZeroShadow_CallsResolverWithZero()
        {
            var resolver = new StubRuleResolver { ShadowThresholdReturn = 0 };
            var playerStats = TestHelpers.MakeStatBlock(2, 0);
            var playerShadows = new SessionShadowTracker(playerStats);

            var config = new GameSessionConfig(clock: TestHelpers.MakeClock(), playerShadows: playerShadows,
                rules: resolver);

            var dice = new FixedDice(1);
            var session = new GameSession(
                MakeProfile("Player"), MakeProfile("Opponent"),
                new NullLlmAdapter(), dice, new NullTrapRegistry(), config);

            // With all zeros, Dread check should pass 0 for Dread shadow value
            Assert.Contains(0, resolver.ShadowThresholdCalls);
        }

        // =====================================================================
        // AC-11: Build clean — verified by the test compilation itself
        // =====================================================================

        // Fails if: IRuleResolver interface is not in Pinder.Core.Interfaces
        [Fact]
        public void IRuleResolver_IsAccessibleFromCoreTests()
        {
            // This compiles = the interface is in the right namespace and accessible
            IRuleResolver? resolver = null;
            Assert.Null(resolver);
        }

        // Fails if: GameSessionConfig.Rules property type changed or removed
        [Fact]
        public void GameSessionConfig_Rules_PropertyType_IsIRuleResolver()
        {
            var config = new GameSessionConfig(clock: TestHelpers.MakeClock());
            // Verify the property exists and is the right type
            IRuleResolver? rules = config.Rules;
            Assert.Null(rules);
        }
    }
}
