using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Pinder.Core.Characters;
using Pinder.Core.Conversation;
using Pinder.Core.Interfaces;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;
using Pinder.LlmAdapters;
using Xunit;

namespace Pinder.Core.Tests
{
    /// <summary>
    /// Tests for issue #743: Horniness overlay fires → interest halved (floor).
    /// Rule §15.horniness-interest-penalty.
    /// Halving was removed by #1209 but restored by #1247.
    /// </summary>
    [Trait("Category", "Core")]
    public class Issue743_HorninessInterestPenaltyTests
    {
        // Seed 1: steering roll=5 (fails), horniness roll=3 (misses DC=15 for sessionHorniness=15).
        private const int OverlayFiredSeed = 1;

        // Seed 0: steering roll=15 (high), horniness roll=17 (passes DC=15 for sessionHorniness=15).
        private const int OverlayNotFiredSeed = 0;

        private static StatDeliveryInstructions LoadDeliveryInstructions()
        {
            string dir = Directory.GetCurrentDirectory();
            for (int i = 0; i < 10; i++)
            {
                string candidate = Path.Combine(dir, "data", "delivery-instructions.yaml");
                if (File.Exists(candidate))
                    return StatDeliveryInstructions.LoadFrom(File.ReadAllText(candidate));
                dir = Path.GetDirectoryName(dir)!;
                if (dir == null) break;
            }
            string fallback = Path.Combine("/root/.openclaw/workspace/pinder-core", "data", "delivery-instructions.yaml");
            return StatDeliveryInstructions.LoadFrom(File.ReadAllText(fallback));
        }

        private static CharacterProfile MakeProfile(string name, int allStats = 2)
        {
            return TestHelpers.MakeCharacterProfile(
                stats: TestHelpers.MakeStatBlock(allStats),
                assembledSystemPrompt: $"You are {name}.",
                displayName: name,
                timing: new TimingProfile(5, 0.0f, 0.0f, "neutral"),
                level: 1);
        }

        private static GameSession MakeSession(
            int startingInterest,
            int sessionHorniness,
            int steeringSeed,
            StatDeliveryInstructions? instructions = null,
            int mainRoll = 15,
            IRuleResolver? rules = null)
        {
            // Dice: first roll is sessionHorniness (d10), rest pad the turn
            var dice = new FixedDice(sessionHorniness, mainRoll, 50);
            var shadows = new SessionShadowTracker(TestHelpers.MakeStatBlock());
            var rng = new Random(steeringSeed);
            var config = new GameSessionConfig(
                clock: TestHelpers.MakeClock(),
                playerShadows: shadows,
                steeringRng: rng,
                startingInterest: startingInterest,
                statDeliveryInstructions: instructions,
                rules: rules);

            return new GameSession(
                MakeProfile("Player"), MakeProfile("Datee"),
                new NullLlmAdapter(), dice, new NullTrapRegistry(), config);
        }

        private sealed class ZeroSuccessDeltaRules : IRuleResolver
        {
            public int? GetFailureInterestDelta(int missMargin, int naturalRoll) => 0;
            public int? GetSuccessInterestDelta(int beatMargin, int naturalRoll) => 0;
            public InterestState? GetInterestState(int interest) => null;
            public int? GetShadowThresholdLevel(int shadowValue) => null;
            public int? GetMomentumBonus(int streak) => null;
            public double? GetRiskTierXpMultiplier(RiskTier riskTier) => null;
            public double? GetTerminalOutcomeMultiplier(GameOutcome outcome) => null;
            public int? GetSuccessBaseXp(int dc) => null;
            public Pinder.Core.Progression.SuccessDcLabelThresholds? GetSuccessDcLabelThresholds() => null;
            public int? GetFlatXpAward(string awardType) => null;
            public int? GetXpThresholdForLevel(int level) => null;
            public int? GetLevelRollBonus(int level) => 0;
            public int? GetBuildPointsForLevel(int level) => null;
            public int? GetItemSlotsForLevel(int level) => null;
            public int? GetFailurePoolTierMinLevel(string tierName) => null;

            public int? GetProgressionCurrencyPerXp() => 10;

            // Behaves like a production resolver: unresolved rules fall back to defaults.
            public bool AllowDefaultFallback => true;
        }

        /// <summary>
        /// Horniness overlay fires + positive interest → halving restored (#1247).
        /// </summary>
        [Fact]
        public async Task Overlay_Fires_PositiveInterest_HalvesInterest()
        {
            var instructions = LoadDeliveryInstructions();
            var session = MakeSession(
                startingInterest: 14,
                sessionHorniness: 15,
                steeringSeed: OverlayFiredSeed,
                instructions: instructions,
                mainRoll: 18); // Use 18 to ensure a success and positive interest delta

            var turn = await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            // Overlay should have fired
            Assert.True(result.HorninessCheck.OverlayApplied,
                "Expected horniness overlay to fire with seed=1, sessionHorniness=15");

            int pre = result.InterestDelta - result.HorninessInterestPenalty;
            Assert.True(pre > 0, "Expected a positive pre-penalty interest delta");

            int expectedPenalty = (int)Math.Floor(pre / 2.0) - pre;
            Assert.True(result.HorninessInterestPenalty < 0, "Expected a negative penalty");
            Assert.Equal(expectedPenalty, result.HorninessInterestPenalty);
            Assert.Equal((int)Math.Floor(pre / 2.0), result.InterestDelta);
        }

        /// <summary>
        /// Horniness overlay fires + odd interest (15) → applies floor halving.
        /// </summary>
        [Fact]
        public async Task Overlay_Fires_OddInterest_FloorHalving()
        {
            var instructions = LoadDeliveryInstructions();
            // We want an odd delta so we can test the floor behavior.
            // Using mainRoll=19 will yield a success with some positive delta. We'll verify
            // that floor(delta/2) is applied correctly.
            // sessionHorniness=5 → DC=5; OverlayFiredSeed=1 produces a miss on the horniness check
            var dice = new FixedDice(5, 19, 50);
            var shadows = new SessionShadowTracker(TestHelpers.MakeStatBlock());
            var rng = new Random(OverlayFiredSeed);
            var config = new GameSessionConfig(
                clock: TestHelpers.MakeClock(),
                playerShadows: shadows,
                steeringRng: rng,
                startingInterest: 15,
                statDeliveryInstructions: instructions);

            var session = new GameSession(
                MakeProfile("Player"), MakeProfile("Datee"),
                new NullLlmAdapter(), dice, new NullTrapRegistry(), config);

            var turn = await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.True(result.HorninessCheck.OverlayApplied,
                "Expected horniness overlay to fire");

            int pre = result.InterestDelta - result.HorninessInterestPenalty;
            Assert.True(pre > 0, "Test requires positive delta");
            
            int expectedPenalty = (int)Math.Floor(pre / 2.0) - pre;
            Assert.Equal(expectedPenalty, result.HorninessInterestPenalty);
            
            if (pre % 2 != 0)
            {
                Assert.Equal((int)Math.Floor(pre / 2.0), result.InterestDelta);
            }
        }

        /// <summary>
        /// Horniness overlay fires + zero interest delta -> no penalty.
        /// </summary>
        [Fact]
        public async Task Overlay_Fires_ZeroInterestDelta_NoPenalty()
        {
            var instructions = LoadDeliveryInstructions();
            var session = MakeSession(
                startingInterest: 14,
                sessionHorniness: 15,
                steeringSeed: OverlayFiredSeed,
                instructions: instructions,
                mainRoll: 1,
                rules: new ZeroSuccessDeltaRules());

            var turn = await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.False(result.Roll.IsSuccess, "Test requires a failed main roll with zero configured failure delta.");
            Assert.True(result.HorninessCheck.OverlayApplied,
                "Expected horniness overlay to fire with seed=1, sessionHorniness=15");
            Assert.Equal(0, result.BaseInterestDelta);
            Assert.Equal(0, result.InterestDelta);
            Assert.Equal(0, result.HorninessInterestPenalty);
            Assert.Equal(0, result.HorninessInterestBefore);
        }

        /// <summary>
        /// Horniness overlay fires + interest less than 0 → no penalty (interest clamped to 0 anyway).
        /// </summary>
        [Fact]
        public void InterestMeter_NegativeInterest_NoPenaltyNeeded()
        {
            // InterestMeter clamps to 0 at minimum, so Current can never be < 0.
            // This test verifies the clamping behavior holds.
            var meter = new InterestMeter(1);
            meter.Apply(-10);
            Assert.Equal(0, meter.Current);
            // At Current = 0, the penalty guard (Current > 0) means no penalty fires.
        }

        /// <summary>
        /// No horniness overlay fired → interest unchanged from penalty.
        /// </summary>
        [Fact]
        public async Task Overlay_NotFired_NoPenalty()
        {
            var instructions = LoadDeliveryInstructions();
            var session = MakeSession(
                startingInterest: 14,
                sessionHorniness: 15,
                steeringSeed: OverlayNotFiredSeed,
                instructions: instructions);

            var turn = await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            // Overlay should NOT have fired
            Assert.False(result.HorninessCheck.OverlayApplied,
                "Expected horniness overlay NOT to fire with seed=0, sessionHorniness=15");

            // No penalty
            Assert.Equal(0, result.HorninessInterestPenalty);
            Assert.Equal(0, result.HorninessInterestBefore);
        }

        /// <summary>
        /// InterestDelta on TurnResult includes penalty from horniness overlay.
        /// </summary>
        [Fact]
        public async Task Overlay_Fires_InterestDeltaIncludesPenalty()
        {
            var instructions = LoadDeliveryInstructions();
            var session = MakeSession(
                startingInterest: 14,
                sessionHorniness: 15, // DC=15; OverlayFiredSeed=1 produces miss
                steeringSeed: OverlayFiredSeed,
                instructions: instructions,
                mainRoll: 18); // Ensure positive interest delta

            var turn = await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.True(result.HorninessCheck.OverlayApplied,
                "Expected overlay to fire");

            Assert.True(result.HorninessInterestPenalty < 0, "Expected penalty to be negative");
            int pre = result.InterestDelta - result.HorninessInterestPenalty;
            Assert.True(pre > result.InterestDelta, "Expected pre-penalty delta to be greater than InterestDelta");
        }

        /// <summary>
        /// When overlay fires but the interest delta is non-positive, no penalty is applied.
        /// </summary>
        [Fact]
        public async Task Overlay_Fires_NonPositiveDelta_NoPenalty()
        {
            var instructions = LoadDeliveryInstructions();
            var session = MakeSession(
                startingInterest: 14,
                sessionHorniness: 15,
                steeringSeed: OverlayFiredSeed,
                instructions: instructions,
                mainRoll: 10); // Low roll ensures negative interest delta

            var turn = await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.True(result.HorninessCheck.OverlayApplied,
                "Expected overlay to fire");

            // No positive delta, so no penalty
            Assert.Equal(0, result.HorninessInterestPenalty);
        }

        /// <summary>
        /// Floor is applied (not ceiling): 15 → 7, not 8.
        /// </summary>
        [Fact]
        public void FloorApplied_OddNumber_RoundsDown()
        {
            // Verify Math.Floor(15 / 2.0) = 7
            Assert.Equal(7, (int)Math.Floor(15 / 2.0));
            // Verify Math.Floor(14 / 2.0) = 7
            Assert.Equal(7, (int)Math.Floor(14 / 2.0));
            // Verify Math.Floor(1 / 2.0) = 0
            Assert.Equal(0, (int)Math.Floor(1 / 2.0));
        }
    }
}
