using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Pinder.Core.Characters;
using Pinder.Core.Conversation;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;
using Pinder.Core.Traps;
using Xunit;

namespace Pinder.Core.Tests
{
    public partial class Issue463_RuleResolverWiringTests
    {
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
                MakeProfile("Player"), MakeProfile("Datee"),
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
                MakeProfile("Player"), MakeProfile("Datee"),
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
                MakeProfile("Player"), MakeProfile("Datee"),
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
                MakeProfile("Player"), MakeProfile("Datee"),
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
                MakeProfile("Player", 5), MakeProfile("Datee"),
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
            // Use high stats (5) so a roll of 15 succeeds against the datee's DC
            var resolver = new StubRuleResolver { SuccessDeltaReturn = 2, XpMultiplierReturn = 5.0 };
            var config = new GameSessionConfig(clock: TestHelpers.MakeClock(), rules: resolver, startingInterest: 10);

            // Roll 15 (not nat20) — with +5 stat bonus = 20 total, should succeed against most DCs
            var dice = new FixedDice(1, 15, 50, 50, 50, 50, 50, 50, 50, 50, 50);
            var session = new GameSession(
                MakeProfile("Player", 5), MakeProfile("Datee"),
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
                MakeProfile("Player", 5), MakeProfile("Datee"),
                new NullLlmAdapter(), dice, new NullTrapRegistry(), config);

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.True(result.Roll.IsSuccess, "Expected success roll — check FixedDice setup");
            // Base XP for a success is at least 5. With 10x = at least 50.
            // Hardcoded Bold max is 3x = 15 from base 5.
            Assert.True(result.XpEarned >= 50,
                $"Expected XP >= 50 with 10x multiplier, got {result.XpEarned}");
        }
    }
}
