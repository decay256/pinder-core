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
    /// Deterministic dice roller that returns values from a queue.
    /// </summary>
    public sealed class FixedDice : IDiceRoller
    {
        private readonly Queue<int> _values;

        public FixedDice(params int[] values)
        {
            _values = new Queue<int>(values);
        }

        public void Enqueue(params int[] values)
        {
            foreach (var v in values)
                _values.Enqueue(v);
        }

        public int Roll(int sides)
        {
            if (_values.Count == 0)
                throw new InvalidOperationException("FixedDice: no more values in queue.");
            return _values.Dequeue();
        }
    }

    /// <summary>
    /// Trap registry that returns no traps for any stat.
    /// </summary>
    public sealed class NullTrapRegistry : ITrapRegistry
    {
        public TrapDefinition? GetTrap(StatType stat) => null;
        public string? GetLlmInstruction(StatType stat) => null;
    }

    public class FailureScaleTests
    {
        [Theory]
        [InlineData(FailureTier.None, 0)]
        [InlineData(FailureTier.Fumble, -1)]
        [InlineData(FailureTier.Misfire, -2)]
        [InlineData(FailureTier.TropeTrap, -3)]
        [InlineData(FailureTier.Catastrophe, -4)]
        [InlineData(FailureTier.Legendary, -5)]
        public void GetInterestDelta_ReturnsCorrectValue(FailureTier tier, int expected)
        {
            // Build a RollResult with the given tier
            var result = new RollResult(
                dieRoll: 5,
                secondDieRoll: null,
                usedDieRoll: 5,
                stat: StatType.Charm,
                statModifier: 0,
                levelBonus: 0,
                dc: 20, // ensure it's a fail for non-None tiers
                tier: tier);

            int delta = FailureScale.GetInterestDelta(result);
            Assert.Equal(expected, delta);
        }
    }

    public class CharacterProfileTests
    {
        [Fact]
        public void Constructor_SetsAllProperties()
        {
            var stats = TestHelpers.MakeStatBlock();
            var timing = new TimingProfile(5, 1.0f, 0.0f, "neutral");
            var profile = new CharacterProfile(stats, "prompt", "TestPlayer", timing, 3);

            Assert.Equal(stats, profile.Stats);
            Assert.Equal("prompt", profile.AssembledSystemPrompt);
            Assert.Equal("TestPlayer", profile.DisplayName);
            Assert.Equal(timing, profile.Timing);
            Assert.Equal(3, profile.Level);
        }

        [Fact]
        public void Constructor_ThrowsOnNullStats()
        {
            var timing = new TimingProfile(5, 1.0f, 0.0f, "neutral");
            Assert.Throws<ArgumentNullException>(() =>
                new CharacterProfile(null!, "prompt", "name", timing, 1));
        }
    }

    public class GameSessionTests
    {
        private static StatBlock MakeStatBlock(int allStats = 2, int allShadow = 0)
        {
            return TestHelpers.MakeStatBlock(allStats, allShadow);
        }

        private static CharacterProfile MakeProfile(string name, int allStats = 2)
        {
            return new CharacterProfile(
                stats: MakeStatBlock(allStats),
                assembledSystemPrompt: $"You are {name}.",
                displayName: name,
                timing: new TimingProfile(5, 0.0f, 0.0f, "neutral"),
                level: 1);
        }

        /// <summary>
        /// Happy path: run a 3-turn session with high rolls (always succeed).
        /// Asserts history length and state progression.
        /// </summary>
        [Fact]
        public async Task ThreeTurnSession_HighRolls_SuccessfulTurns()
        {
            // Each turn needs: d20 roll for RollEngine, d100 for TimingProfile.ComputeDelay
            // With stat mod +2, level bonus +0, DC = 13 + 2 = 15
            // Roll of 15: 15 + 2 + 0 = 17 >= 15 → success, beat by 2 → +1 interest
            var dice = new FixedDice(
                // Turn 1: d20=15, d100=50 (timing delay)
                15, 50,
                // Turn 2: d20=15, d100=50
                15, 50,
                // Turn 3: d20=15, d100=50
                15, 50
            );

            var player = MakeProfile("Player");
            var opponent = MakeProfile("Opponent");
            var llm = new NullLlmAdapter();
            var traps = new NullTrapRegistry();

            var session = new GameSession(player, opponent, llm, dice, traps);

            // Turn 1
            var start1 = await session.StartTurnAsync();
            Assert.Equal(4, start1.Options.Length);
            Assert.Equal(10, start1.State.Interest);
            Assert.Equal(InterestState.Interested, start1.State.State);

            var result1 = await session.ResolveTurnAsync(0); // Charm
            Assert.True(result1.Roll.IsSuccess);
            // beat by 2 → SuccessScale +1, need=13 → Hard → RiskTierBonus +1, no momentum at streak=1
            Assert.Equal(2, result1.InterestDelta);
            Assert.Equal(12, result1.StateAfter.Interest);
            Assert.False(result1.IsGameOver);
            Assert.Equal(1, result1.StateAfter.TurnNumber);

            // Turn 2
            var start2 = await session.StartTurnAsync();
            var result2 = await session.ResolveTurnAsync(0);
            Assert.True(result2.Roll.IsSuccess);
            Assert.Equal(2, result2.InterestDelta); // streak=2, no momentum; SuccessScale +1 + RiskTier +1
            Assert.Equal(14, result2.StateAfter.Interest);
            Assert.Equal(2, result2.StateAfter.TurnNumber);

            // Turn 3
            var start3 = await session.StartTurnAsync();
            var result3 = await session.ResolveTurnAsync(0);
            Assert.True(result3.Roll.IsSuccess);
            // streak=3 → +2 momentum bonus, SuccessScale +1, RiskTier +1, so delta = 1 + 1 + 2 = 4
            Assert.Equal(4, result3.InterestDelta);
            Assert.Equal(18, result3.StateAfter.Interest);
            Assert.Equal(3, result3.StateAfter.TurnNumber);
        }

        [Fact]
        public async Task FailedRoll_ResetsStreak_AppliesNegativeDelta()
        {
            // DC = 13 + 2 = 15. Roll 5: 5 + 2 + 0 = 7 < 15. Miss by 8 → TropeTrap (-3)
            var dice = new FixedDice(
                5, 50  // d20=5, d100 for timing
            );

            var session = new GameSession(
                MakeProfile("P"), MakeProfile("O"),
                new NullLlmAdapter(), dice, new NullTrapRegistry());

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.False(result.Roll.IsSuccess);
            Assert.Equal(FailureTier.TropeTrap, result.Roll.Tier);
            Assert.Equal(-3, result.InterestDelta);
            Assert.Equal(7, result.StateAfter.Interest); // 10 - 3
            Assert.Equal(0, result.StateAfter.MomentumStreak);
        }

        [Fact]
        public async Task GhostTrigger_WhenBored_25PercentChance()
        {
            // Start at interest 10 (Interested). Need to get to Bored (1-4).
            // We'll manipulate by having bad rolls to lower interest to 4 (Bored).
            // Then on next StartTurnAsync, ghost check: dice.Roll(4)==1 → Ghosted.

            // Turn 1: roll 1 (nat 1 → Legendary → -5 interest) → interest = 5 (Interested)
            // Turn 2: roll 5 → miss by 8 → TropeTrap → -3 → interest = 2 (Bored)
            // Turn 3 start: ghost check Roll(4)=1 → Ghosted
            var dice = new FixedDice(
                1, 50,    // Turn 1: nat 1, timing
                5, 50,    // Turn 2: d20=5, timing
                1         // Turn 3 ghost check: Roll(4)=1
            );

            var session = new GameSession(
                MakeProfile("P"), MakeProfile("O"),
                new NullLlmAdapter(), dice, new NullTrapRegistry());

            // Turn 1
            await session.StartTurnAsync();
            var r1 = await session.ResolveTurnAsync(0);
            Assert.Equal(5, r1.StateAfter.Interest); // 10 - 5

            // Turn 2
            await session.StartTurnAsync();
            var r2 = await session.ResolveTurnAsync(0);
            Assert.Equal(2, r2.StateAfter.Interest); // 5 - 3 (TropeTrap)

            // Turn 3 start — should be ghosted
            var ex = await Assert.ThrowsAsync<GameEndedException>(() => session.StartTurnAsync());
            Assert.Equal(GameOutcome.Ghosted, ex.Outcome);
        }

        [Fact]
        public async Task EndCondition_InterestHitsZero_ThrowsOnNextStart()
        {
            // Nat 1 twice: -5 each → interest 10 → 5 → 0 (Unmatched)
            var dice = new FixedDice(
                1, 50,    // Turn 1: nat 1, timing
                1, 50     // Turn 2: nat 1, timing
            );

            var session = new GameSession(
                MakeProfile("P"), MakeProfile("O"),
                new NullLlmAdapter(), dice, new NullTrapRegistry());

            await session.StartTurnAsync();
            var r1 = await session.ResolveTurnAsync(0);
            Assert.Equal(5, r1.StateAfter.Interest);

            await session.StartTurnAsync();
            var r2 = await session.ResolveTurnAsync(0);
            Assert.Equal(0, r2.StateAfter.Interest);
            Assert.True(r2.IsGameOver);
            Assert.Equal(GameOutcome.Unmatched, r2.Outcome);

            // Next call should throw
            await Assert.ThrowsAsync<GameEndedException>(() => session.StartTurnAsync());
        }

        [Fact]
        public async Task MomentumBonus_AppliedCorrectly()
        {
            // 5 consecutive successes with roll=15, each beating DC by 2 → +1 base
            // need = 15-(2+0) = 13 → Hard → +1 risk tier bonus per success
            // streak 1: +1 base +1 risk +0 momentum = 2
            // streak 2: +1 base +1 risk +0 momentum = 2
            // streak 3: +1 base +1 risk +2 momentum = 4
            // streak 4: +1 base +1 risk +2 momentum = 4 (advantage from VeryIntoIt)
            // streak 5: +1 base +1 risk +3 momentum = 5 (advantage from AlmostThere, clamped to 25)
            // Interest progression: 10→12→14→18→22→25 (clamped)
            // At turn 4 start, interest=18 (VeryIntoIt) → advantage → 2x d20
            // At turn 5 start, interest=22 (AlmostThere) → advantage → 2x d20
            var dice = new FixedDice(
                15, 50,        // Turn 1: d20, d100 (timing)
                15, 50,        // Turn 2: d20, d100
                15, 50,        // Turn 3: d20, d100. After: 18 (VeryIntoIt)
                15, 15, 50,    // Turn 4: 2x d20 (advantage), d100. After: 22 (AlmostThere)
                15, 15, 50     // Turn 5: 2x d20 (advantage), d100. After: 25 (clamped)
            );

            var session = new GameSession(
                MakeProfile("P"), MakeProfile("O"),
                new NullLlmAdapter(), dice, new NullTrapRegistry());

            // Note: interest is clamped to 25, so effective delta at turn 5 may be less
            int[] expectedDeltas = { 2, 2, 4, 4, 5 };
            int expectedInterest = 10;

            for (int i = 0; i < 5; i++)
            {
                await session.StartTurnAsync();
                var result = await session.ResolveTurnAsync(0);
                Assert.Equal(expectedDeltas[i], result.InterestDelta);
                expectedInterest += expectedDeltas[i];
                if (expectedInterest > 25) expectedInterest = 25; // clamp
                Assert.Equal(expectedInterest, result.StateAfter.Interest);
            }
        }

        [Fact]
        public async Task ResolveTurnAsync_ThrowsWhenCalledWithoutStart()
        {
            var dice = new FixedDice();
            var session = new GameSession(
                MakeProfile("P"), MakeProfile("O"),
                new NullLlmAdapter(), dice, new NullTrapRegistry());

            await Assert.ThrowsAsync<InvalidOperationException>(() => session.ResolveTurnAsync(0));
        }

        [Fact]
        public async Task ResolveTurnAsync_ThrowsOnInvalidIndex()
        {
            var dice = new FixedDice(15, 50);
            var session = new GameSession(
                MakeProfile("P"), MakeProfile("O"),
                new NullLlmAdapter(), dice, new NullTrapRegistry());

            await session.StartTurnAsync();
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => session.ResolveTurnAsync(5));
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => session.ResolveTurnAsync(-1));
        }

        [Fact]
        public async Task DeliveredMessage_AppearsInHistory()
        {
            var dice = new FixedDice(15, 50);
            var session = new GameSession(
                MakeProfile("Player"), MakeProfile("Opponent"),
                new NullLlmAdapter(), dice, new NullTrapRegistry());

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            // NullLlmAdapter echoes the intended text for success
            Assert.Equal("Hey, you come here often?", result.DeliveredMessage);
            Assert.Equal("...", result.OpponentMessage);
        }
    }

    internal static class TestHelpers
    {
        public static StatBlock MakeStatBlock(int allStats = 2, int allShadow = 0)
        {
            var stats = new Dictionary<StatType, int>
            {
                { StatType.Charm, allStats },
                { StatType.Rizz, allStats },
                { StatType.Honesty, allStats },
                { StatType.Chaos, allStats },
                { StatType.Wit, allStats },
                { StatType.SelfAwareness, allStats }
            };
            var shadow = new Dictionary<ShadowStatType, int>
            {
                { ShadowStatType.Madness, allShadow },
                { ShadowStatType.Horniness, allShadow },
                { ShadowStatType.Denial, allShadow },
                { ShadowStatType.Fixation, allShadow },
                { ShadowStatType.Dread, allShadow },
                { ShadowStatType.Overthinking, allShadow }
            };
            return new StatBlock(stats, shadow);
        }
    }
}
