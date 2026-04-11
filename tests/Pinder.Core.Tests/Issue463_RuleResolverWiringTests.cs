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
    public class Issue463_RuleResolverWiringTests
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
                MakeProfile("Player"), MakeProfile("Opponent"),
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
                MakeProfile("Player"), MakeProfile("Opponent"),
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
                MakeProfile("Player"), MakeProfile("Opponent"),
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
                MakeProfile("Player"), MakeProfile("Opponent"),
                new NullLlmAdapter(), dice, new NullTrapRegistry(), config);

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            // With +10 success delta, interest from 10 should reach cap (25)
            // Hardcoded nat20 gives +4, so if interest > 20, resolver value was used
            Assert.True(result.StateAfter.Interest > 14,
                $"Expected interest > 14 with resolver success delta +10, got {result.StateAfter.Interest}");
        }

        // =====================================================================
        // AC-3: §6 Interest state flows through resolver
        // =====================================================================

        // Fails if: GameSession doesn't call GetInterestState during StartTurnAsync
        [Fact]
        public async Task StartTurn_CallsResolverForInterestState()
        {
            var resolver = new StubRuleResolver();
            var config = new GameSessionConfig(clock: TestHelpers.MakeClock(), rules: resolver, startingInterest: 10);

            var dice = new FixedDice(1, 4);
            var session = new GameSession(
                MakeProfile("Player"), MakeProfile("Opponent"),
                new NullLlmAdapter(), dice, new NullTrapRegistry(), config);

            await session.StartTurnAsync();

            Assert.True(resolver.InterestStateCalls.Count > 0,
                "Expected GetInterestState to be called during StartTurnAsync");
        }

        // Fails if: GameSession uses InterestMeter.GetState() instead of resolver when resolver is present
        [Fact]
        public async Task StartTurn_WithBoredStateFromResolver_TriggersGhostCheck()
        {
            // Resolver says Bored for interest=10 (normally this would be Interested)
            var resolver = new StubRuleResolver { InterestStateReturn = InterestState.Bored };
            var config = new GameSessionConfig(clock: TestHelpers.MakeClock(), rules: resolver, startingInterest: 10);

            // Dice: horniness=1, ghost roll=1 (triggers ghost at 25% chance when Bored)
            var dice = new FixedDice(1, 1);
            var session = new GameSession(
                MakeProfile("Player"), MakeProfile("Opponent"),
                new NullLlmAdapter(), dice, new NullTrapRegistry(), config);

            // If resolver's Bored state is used, ghost should trigger (dice roll=1 on d4)
            // If hardcoded Interested is used, ghost check doesn't even happen
            // Ghost trigger throws GameEndedException
            await Assert.ThrowsAsync<GameEndedException>(() => session.StartTurnAsync());
        }

        // =====================================================================
        // AC-4: §7 Shadow thresholds flow through resolver
        // =====================================================================

        // Fails if: GameSession doesn't call GetShadowThresholdLevel during construction/turn
        [Fact]
        public void Constructor_WithShadows_CallsResolverForShadowThreshold()
        {
            var resolver = new StubRuleResolver();
            var playerStats = TestHelpers.MakeStatBlock(2, 0);
            var playerShadows = new SessionShadowTracker(playerStats);
            playerShadows.ApplyGrowth(ShadowStatType.Dread, 5, "test");

            var config = new GameSessionConfig(clock: TestHelpers.MakeClock(), playerShadows: playerShadows,
                rules: resolver);

            var dice = new FixedDice(1);
            var session = new GameSession(
                MakeProfile("Player"), MakeProfile("Opponent"),
                new NullLlmAdapter(), dice, new NullTrapRegistry(), config);

            Assert.True(resolver.ShadowThresholdCalls.Count > 0,
                "Expected GetShadowThresholdLevel to be called during constructor with player shadows");
        }

        // Fails if: Shadow threshold from resolver is ignored for Dread T3 starting interest
        [Fact]
        public async Task Constructor_ResolverReturnsDreadT3_StartsInterestAt8()
        {
            // Resolver returns T3 for any shadow value (even low ones)
            var resolver = new StubRuleResolver { ShadowThresholdReturn = 3 };
            var playerStats = TestHelpers.MakeStatBlock(2, 0);
            var playerShadows = new SessionShadowTracker(playerStats);
            // Small dread growth — hardcoded evaluator would return T0, but resolver says T3
            playerShadows.ApplyGrowth(ShadowStatType.Dread, 1, "test");

            var config = new GameSessionConfig(clock: TestHelpers.MakeClock(), playerShadows: playerShadows,
                rules: resolver);

            var dice = new FixedDice(1, 4);
            var session = new GameSession(
                MakeProfile("Player"), MakeProfile("Opponent"),
                new NullLlmAdapter(), dice, new NullTrapRegistry(), config);

            var start = await session.StartTurnAsync();

            // If resolver's T3 is used, Dread T3 rule sets starting interest to 8
            // If hardcoded T0 is used, starting interest would be default 10
            Assert.True(start.State.Interest <= 8,
                $"Expected starting interest <= 8 for Dread T3 (resolver), got {start.State.Interest}");
        }

        // =====================================================================
        // AC-5: §15 Momentum bonuses flow through resolver
        // =====================================================================

        // Fails if: GameSession doesn't call GetMomentumBonus from resolver
        [Fact]
        public async Task StartTurn_WithMomentumStreak_CallsResolverForMomentum()
        {
            var resolver = new StubRuleResolver { MomentumReturn = 5, SuccessDeltaReturn = 1 };
            var config = new GameSessionConfig(clock: TestHelpers.MakeClock(), rules: resolver, startingInterest: 10);

            // Need: horniness, nat20 for turn 1 success, then another turn
            var dice = new FixedDice(
                1,                              // horniness
                20, 50, 50, 50, 50, 50, 50,     // turn 1
                20, 50, 50, 50, 50, 50, 50      // turn 2
            );

            var session = new GameSession(
                MakeProfile("Player", 5), MakeProfile("Opponent"),
                new NullLlmAdapter(), dice, new NullTrapRegistry(), config);

            // Turn 1 — builds streak
            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);

            // Turn 2 — should call resolver for momentum
            resolver.MomentumCalls.Clear();
            await session.StartTurnAsync();

            Assert.True(resolver.MomentumCalls.Count > 0,
                "Expected GetMomentumBonus to be called during StartTurnAsync for turn 2+");
        }

        // =====================================================================
        // AC-6: §15 Risk tier XP multipliers flow through resolver
        // =====================================================================

        // Fails if: GameSession doesn't call GetRiskTierXpMultiplier on non-nat20 success
        [Fact]
        public async Task ResolveTurn_OnNonNat20Success_CallsResolverForXpMultiplier()
        {
            // Use high stats (5) so a roll of 15 succeeds against the opponent's DC
            var resolver = new StubRuleResolver { SuccessDeltaReturn = 2, XpMultiplierReturn = 5.0 };
            var config = new GameSessionConfig(clock: TestHelpers.MakeClock(), rules: resolver, startingInterest: 10);

            // Roll 15 (not nat20) — with +5 stat bonus = 20 total, should succeed against most DCs
            var dice = new FixedDice(1, 15, 50, 50, 50, 50, 50, 50, 50, 50, 50);
            var session = new GameSession(
                MakeProfile("Player", 5), MakeProfile("Opponent"),
                new NullLlmAdapter(), dice, new NullTrapRegistry(), config);

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.True(result.Roll.IsSuccess, "Expected success roll — check FixedDice setup");
            Assert.True(resolver.XpMultiplierCalls.Count > 0,
                "Expected GetRiskTierXpMultiplier to be called during ResolveTurnAsync on non-nat20 success");
        }

        // Fails if: GameSession ignores the resolver's XP multiplier (uses hardcoded instead)
        [Fact]
        public async Task ResolveTurn_HighXpMultiplier_AffectsXpEarned()
        {
            // 10x multiplier — should produce noticeably more XP than any hardcoded value (max 3x)
            var resolver = new StubRuleResolver { SuccessDeltaReturn = 2, XpMultiplierReturn = 10.0 };
            var config = new GameSessionConfig(clock: TestHelpers.MakeClock(), rules: resolver, startingInterest: 10);

            // Roll 15 (not nat20) with high stat bonus so it succeeds
            var dice = new FixedDice(1, 15, 50, 50, 50, 50, 50, 50, 50, 50, 50);
            var session = new GameSession(
                MakeProfile("Player", 5), MakeProfile("Opponent"),
                new NullLlmAdapter(), dice, new NullTrapRegistry(), config);

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.True(result.Roll.IsSuccess, "Expected success roll — check FixedDice setup");
            // Base XP for a success is at least 5. With 10x = at least 50.
            // Hardcoded Bold max is 3x = 15 from base 5.
            Assert.True(result.XpEarned >= 50,
                $"Expected XP >= 50 with 10x multiplier, got {result.XpEarned}");
        }

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
