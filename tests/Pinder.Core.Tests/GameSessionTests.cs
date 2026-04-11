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
        [InlineData(FailureTier.Misfire, -1)]
        [InlineData(FailureTier.TropeTrap, -2)]
        [InlineData(FailureTier.Catastrophe, -3)]
        [InlineData(FailureTier.Legendary, -4)]
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
            // With player stat mod +2, opponent allStats=0 → DC = 16 + 0 = 16
            // Roll of 15: 15 + 2 + 0 = 17 >= 16 → success, beat by 1 → SuccessScale +1
            // need = 16 - 2 = 14 → Hard → RiskTierBonus +3. Total delta = 4.
            // Turn 3 starts at VeryIntoIt (interest=18) → advantage → 2 d20 rolls
            var dice = new FixedDice(
                5,  // Constructor: horniness roll (1d10)
                // Turn 1: d20=15, d100=50
                15, 50,
                // Turn 2: d20=15, d100=50
                15, 50,
                // Turn 3: advantage (VeryIntoIt) → d20=15, d20=15, d100=50
                15, 15, 50
            );

            var player = MakeProfile("Player");
            var opponent = MakeProfile("Opponent", 0);
            var llm = new NullLlmAdapter();
            var traps = new NullTrapRegistry();

            var session = new GameSession(player, opponent, llm, dice, traps);

            // Turn 1
            var start1 = await session.StartTurnAsync();
            Assert.True(start1.Options.Length >= 1);
            Assert.Equal(10, start1.State.Interest);
            Assert.Equal(InterestState.Interested, start1.State.State);

            var result1 = await session.ResolveTurnAsync(0); // Charm
            Assert.True(result1.Roll.IsSuccess);
            // SuccessScale +1 (beat by 1), Hard → RiskTierBonus +3, momentum=0 (streak was 0)
            Assert.Equal(4, result1.InterestDelta);
            Assert.Equal(14, result1.StateAfter.Interest);
            Assert.False(result1.IsGameOver);
            Assert.Equal(1, result1.StateAfter.TurnNumber);

            // Turn 2
            var start2 = await session.StartTurnAsync();
            var result2 = await session.ResolveTurnAsync(0);
            Assert.True(result2.Roll.IsSuccess);
            Assert.Equal(4, result2.InterestDelta); // streak=1 at start, SuccessScale +1 + Hard RiskTier +3
            Assert.Equal(18, result2.StateAfter.Interest);
            Assert.Equal(2, result2.StateAfter.TurnNumber);

            // Turn 3 (VeryIntoIt → advantage, 2 d20 rolls)
            var start3 = await session.StartTurnAsync();
            var result3 = await session.ResolveTurnAsync(0);
            Assert.True(result3.Roll.IsSuccess);
            // streak=2 at start → momentum bonus=0 (applied as roll bonus, not interest delta #268)
            // SuccessScale +1, Hard RiskTier +3 = 4
            Assert.Equal(4, result3.InterestDelta);
            Assert.Equal(22, result3.StateAfter.Interest);
            Assert.Equal(3, result3.StateAfter.TurnNumber);
        }

        [Fact]
        public async Task FailedRoll_ResetsStreak_AppliesNegativeDelta()
        {
            // DC = 16 + 2 = 18. Roll 10: 10 + 2 + 0 = 12 < 18. Miss by 6 → TropeTrap (-2 per rules-v3.4 §5)
            var dice = new FixedDice(
                5,  // Constructor: horniness roll (1d10)
                10, 50  // d20=10, d100 for timing
            );

            var session = new GameSession(
                MakeProfile("P"), MakeProfile("O"),
                new NullLlmAdapter(), dice, new NullTrapRegistry(), new GameSessionConfig(clock: TestHelpers.MakeClock()));

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.False(result.Roll.IsSuccess);
            Assert.Equal(FailureTier.TropeTrap, result.Roll.Tier);
            Assert.Equal(-2, result.InterestDelta);
            Assert.Equal(8, result.StateAfter.Interest); // 10 - 2
            Assert.Equal(0, result.StateAfter.MomentumStreak);
        }

        [Fact]
        public async Task GhostTrigger_WhenBored_25PercentChance()
        {
            // Start at interest 10 (Interested). Need to get to Bored (1-4).
            // We'll manipulate by having bad rolls to lower interest to 4 (Bored).
            // Then on next StartTurnAsync, ghost check: dice.Roll(4)==1 → Ghosted.

            // Turn 1: roll 1 (nat 1 → Legendary → -4 interest) → interest = 6 (Interested)
            // Turn 2: roll 7 → 7+2=9 vs DC 18 → miss by 9 → TropeTrap → -2 → interest = 4 (Bored)
            // Turn 3 start: ghost check Roll(4)=1 → Ghosted
            var dice = new FixedDice(
                5,  // Constructor: horniness roll (1d10)
                1, 50,    // Turn 1: nat 1, timing
                7, 50,    // Turn 2: d20=7, timing
                1         // Turn 3 ghost check: Roll(4)=1
            );

            var session = new GameSession(
                MakeProfile("P"), MakeProfile("O"),
                new NullLlmAdapter(), dice, new NullTrapRegistry(), new GameSessionConfig(clock: TestHelpers.MakeClock()));

            // Turn 1
            await session.StartTurnAsync();
            var r1 = await session.ResolveTurnAsync(0);
            Assert.Equal(6, r1.StateAfter.Interest); // 10 - 4

            // Turn 2
            await session.StartTurnAsync();
            var r2 = await session.ResolveTurnAsync(0);
            Assert.Equal(4, r2.StateAfter.Interest); // 6 - 2 (TropeTrap)

            // Turn 3 start — should be ghosted
            var ex = await Assert.ThrowsAsync<GameEndedException>(() => session.StartTurnAsync());
            Assert.Equal(GameOutcome.Ghosted, ex.Outcome);
        }

        [Fact]
        public async Task EndCondition_InterestHitsZero_ThrowsOnNextStart()
        {
            // Nat 1 three times: -4 each → interest 10 → 6 → 2 → 0 (clamped, Unmatched)
            // After turn 2, interest=2 (Bored), so StartTurnAsync does ghost check: need d4≠1
            // Turn 3 has disadvantage (Bored), so RollEngine rolls 2 d20s
            var dice = new FixedDice(
                5,  // Constructor: horniness roll (1d10)
                1, 50,       // Turn 1: nat 1, timing
                1, 50,       // Turn 2: nat 1, timing
                2,           // Turn 3: ghost check d4=2 (no ghost)
                1, 2, 50     // Turn 3: d20=1 (disadv d20=2), timing
            );

            var session = new GameSession(
                MakeProfile("P"), MakeProfile("O"),
                new NullLlmAdapter(), dice, new NullTrapRegistry(), new GameSessionConfig(clock: TestHelpers.MakeClock()));

            await session.StartTurnAsync();
            var r1 = await session.ResolveTurnAsync(0);
            Assert.Equal(6, r1.StateAfter.Interest); // 10 - 4

            await session.StartTurnAsync();
            var r2 = await session.ResolveTurnAsync(0);
            Assert.Equal(2, r2.StateAfter.Interest); // 6 - 4

            await session.StartTurnAsync();
            var r3 = await session.ResolveTurnAsync(0);
            Assert.Equal(0, r3.StateAfter.Interest); // 2 - 4 clamped to 0
            Assert.True(r3.IsGameOver);
            Assert.Equal(GameOutcome.Unmatched, r3.Outcome);

            // Next call should throw
            await Assert.ThrowsAsync<GameEndedException>(() => session.StartTurnAsync());
        }

        [Fact]
        public async Task MomentumBonus_AppliedAsRollBonus()
        {
            // 5 consecutive successes with player allStats=9, opponent allStats=0 → DC=16
            // roll=8: 8+9=17 ≥ 16 → success, margin=1 → scale=+1. need=7 → Safe → +1 risk bonus.
            // Momentum is a roll bonus (ExternalBonus), not an interest delta (#268).
            // streak 0→1: pending momentum=0, interestDelta = +1 scale +1 safe = 2
            // streak 1→2: pending momentum=0, interestDelta = 2
            // streak 2→3: pending momentum=0, interestDelta = 2. After: interest=16 (VeryIntoIt → advantage)
            // streak 3→4: pending momentum=+2 (roll bonus), interestDelta = 2
            // streak 4→5: pending momentum=+2 (roll bonus), interestDelta = 2
            // Interest progression: 10→12→14→16→18→20
            // At turn 4 start, interest=16 (VeryIntoIt) → advantage → 2x d20
            // At turn 5 start, interest=18 (VeryIntoIt) → advantage → 2x d20
            var dice = new FixedDice(
                5,  // Constructor: horniness roll (1d10)
                8, 50,        // Turn 1: d20, d100 (timing)
                8, 50,        // Turn 2: d20, d100
                8, 50,        // Turn 3: d20, d100. After: 16 (VeryIntoIt)
                8, 8, 50,     // Turn 4: 2x d20 (advantage), d100
                8, 8, 50      // Turn 5: 2x d20 (advantage), d100
            );

            var session = new GameSession(
                MakeProfile("P", 9), MakeProfile("O", 0),
                new NullLlmAdapter(), dice, new NullTrapRegistry(), new GameSessionConfig(clock: TestHelpers.MakeClock()));

            // Every turn has the same interestDelta; momentum is in ExternalBonus
            int[] expectedDeltas = { 2, 2, 2, 2, 2 };
            int[] expectedMomentumBonus = { 0, 0, 0, 2, 2 };
            int expectedInterest = 10;

            for (int i = 0; i < 5; i++)
            {
                await session.StartTurnAsync();
                var result = await session.ResolveTurnAsync(0);
                Assert.Equal(expectedDeltas[i], result.InterestDelta);
                Assert.Equal(expectedMomentumBonus[i], result.Roll.ExternalBonus);
                expectedInterest += expectedDeltas[i];
                Assert.Equal(expectedInterest, result.StateAfter.Interest);
            }
        }

        [Fact]
        public async Task MomentumBonus_CanChangeOutcomeTier()
        {
            // AC: 3-win streak → next roll has +2 external bonus → can change outcome tier
            // Player allStats=9, opponent allStats=0 → DC=16.
            // Setup: 3 successes with roll=8 (Total=17, DC=16, success), then
            // turn 4 with roll=5 (Total=14, DC=16 → normally fail). With +2 momentum,
            // FinalTotal=16 → success.
            var dice = new FixedDice(
                5,  // Constructor: horniness roll (1d10)
                8, 50,   // Turn 1: success (8+9=17)
                8, 50,   // Turn 2: success
                8, 50,   // Turn 3: success. After: interest=16 (VeryIntoIt → advantage)
                5, 5, 50  // Turn 4: advantage → 2 d20s, both 5. Total=14, DC=16. Without momentum: fail. With +2: success.
            );

            var session = new GameSession(
                MakeProfile("P", 9), MakeProfile("O", 0),
                new NullLlmAdapter(), dice, new NullTrapRegistry(), new GameSessionConfig(clock: TestHelpers.MakeClock()));

            // Build a 3-win streak
            for (int i = 0; i < 3; i++)
            {
                await session.StartTurnAsync();
                var r = await session.ResolveTurnAsync(0);
                Assert.True(r.Roll.IsSuccess);
            }

            // Turn 4: roll that would fail without momentum but succeeds with it
            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            // Momentum bonus of +2 is in ExternalBonus
            Assert.Equal(2, result.Roll.ExternalBonus);
            // Total=14 (5+9+0), FinalTotal=16 (14+2) >= DC=16 → success
            Assert.Equal(14, result.Roll.Total);
            Assert.True(result.Roll.IsSuccess, "Momentum +2 should make this roll succeed (FinalTotal 16 >= DC 16)");
        }

        [Fact]
        public async Task ResolveTurnAsync_ThrowsWhenCalledWithoutStart()
        {
            var dice = new FixedDice(5);  // 5=horniness roll
            var session = new GameSession(
                MakeProfile("P"), MakeProfile("O"),
                new NullLlmAdapter(), dice, new NullTrapRegistry(), new GameSessionConfig(clock: TestHelpers.MakeClock()));

            await Assert.ThrowsAsync<InvalidOperationException>(() => session.ResolveTurnAsync(0));
        }

        [Fact]
        public async Task ResolveTurnAsync_ThrowsOnInvalidIndex()
        {
            var dice = new FixedDice(5, 15, 50);
            var session = new GameSession(
                MakeProfile("P"), MakeProfile("O"),
                new NullLlmAdapter(), dice, new NullTrapRegistry(), new GameSessionConfig(clock: TestHelpers.MakeClock()));

            await session.StartTurnAsync();
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => session.ResolveTurnAsync(5));
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => session.ResolveTurnAsync(-1));
        }

        [Fact]
        public async Task DeliveredMessage_AppearsInHistory()
        {
            var dice = new FixedDice(5, 16, 50);
            var session = new GameSession(
                MakeProfile("Player"), MakeProfile("Opponent"),
                new NullLlmAdapter(), dice, new NullTrapRegistry(), new GameSessionConfig(clock: TestHelpers.MakeClock()));

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            // NullLlmAdapter echoes the intended text for success
            Assert.Equal("Hey, you come here often?", result.DeliveredMessage);
            Assert.Equal("...", result.OpponentMessage);
        }
    }

    internal static class TestHelpers
    {
        /// <summary>
        /// Returns a zero-modifier IGameClock for test isolation.
        /// </summary>
        public static IGameClock MakeClock(int horninessModifier = 0)
            => new ZeroModifierClock(horninessModifier);

        private sealed class ZeroModifierClock : Pinder.Core.Interfaces.IGameClock
        {
            private readonly int _mod;
            public ZeroModifierClock(int mod) => _mod = mod;
            public DateTimeOffset Now => DateTimeOffset.UtcNow;
            public int RemainingEnergy => 100;
            public void Advance(TimeSpan amount) { }
            public void AdvanceTo(DateTimeOffset target) { }
            public Pinder.Core.Interfaces.TimeOfDay GetTimeOfDay() => Pinder.Core.Interfaces.TimeOfDay.Afternoon;
            public int GetHorninessModifier() => _mod;
            public bool ConsumeEnergy(int amount) => true;
        }

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
                { ShadowStatType.Despair, allShadow },
                { ShadowStatType.Denial, allShadow },
                { ShadowStatType.Fixation, allShadow },
                { ShadowStatType.Dread, allShadow },
                { ShadowStatType.Overthinking, allShadow }
            };
            return new StatBlock(stats, shadow);
        }
    }
}
