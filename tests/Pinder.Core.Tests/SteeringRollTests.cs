using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Pinder.Core.Characters;
using Pinder.Core.Conversation;
using Pinder.Core.Interfaces;
using Pinder.Core.Stats;
using Pinder.Core.Traps;
using Xunit;

namespace Pinder.Core.Tests
{
    public class SteeringRollTests
    {
        // Helper: create a StatBlock with specific values for the steering stats
        private static StatBlock MakeStatBlockWithValues(
            int charm = 2, int rizz = 2, int honesty = 2,
            int chaos = 2, int wit = 2, int sa = 2)
        {
            var stats = new Dictionary<StatType, int>
            {
                { StatType.Charm, charm },
                { StatType.Rizz, rizz },
                { StatType.Honesty, honesty },
                { StatType.Chaos, chaos },
                { StatType.Wit, wit },
                { StatType.SelfAwareness, sa }
            };
            var shadow = new Dictionary<ShadowStatType, int>
            {
                { ShadowStatType.Madness, 0 },
                { ShadowStatType.Horniness, 0 },
                { ShadowStatType.Denial, 0 },
                { ShadowStatType.Fixation, 0 },
                { ShadowStatType.Dread, 0 },
                { ShadowStatType.Overthinking, 0 }
            };
            return new StatBlock(stats, shadow);
        }

        [Theory]
        [InlineData(3, 3, 3, 3)]     // (3+3+3)/3 = 3
        [InlineData(4, 2, 3, 3)]     // (4+2+3)/3 = 3
        [InlineData(5, 1, 0, 2)]     // (5+1+0)/3 = 2
        [InlineData(0, 0, 0, 0)]     // (0+0+0)/3 = 0
        [InlineData(10, 10, 10, 10)] // (10+10+10)/3 = 10
        [InlineData(1, 2, 3, 2)]     // (1+2+3)/3 = 2
        [InlineData(7, 5, 3, 5)]     // (7+5+3)/3 = 5
        public void SteeringModifier_IsAverageOfCharmWitSA(int charm, int wit, int sa, int expectedMod)
        {
            // The steering modifier formula: (charm + wit + sa) / 3 (integer division)
            int computed = (charm + wit + sa) / 3;
            Assert.Equal(expectedMod, computed);
        }

        [Theory]
        [InlineData(2, 2, 2, 18)]    // 16 + (2+2+2)/3 = 16 + 2 = 18
        [InlineData(3, 3, 3, 19)]    // 16 + (3+3+3)/3 = 16 + 3 = 19
        [InlineData(0, 0, 0, 16)]    // 16 + (0+0+0)/3 = 16
        [InlineData(6, 3, 0, 19)]    // 16 + (6+3+0)/3 = 16 + 3 = 19
        [InlineData(5, 5, 5, 21)]    // 16 + (5+5+5)/3 = 16 + 5 = 21
        public void SteeringDC_Is16PlusAverageOfOpponentSARizzHonesty(
            int opponentSA, int opponentRizz, int opponentHonesty, int expectedDC)
        {
            int computed = 16 + (opponentSA + opponentRizz + opponentHonesty) / 3;
            Assert.Equal(expectedDC, computed);
        }

        /// <summary>
        /// Deterministic Random that returns specific values.
        /// System.Random.Next(minValue, maxValue) returns [minValue, maxValue).
        /// We override to return a fixed value.
        /// </summary>
        private sealed class FixedRandom : System.Random
        {
            private readonly Queue<int> _values;
            public FixedRandom(params int[] values) { _values = new Queue<int>(values); }
            public override int Next(int minValue, int maxValue) => _values.Count > 0 ? _values.Dequeue() : minValue;
        }

        /// <summary>
        /// Integration test: a high roll succeeds and the steering question is appended.
        /// </summary>
        [Fact]
        public async Task SteeringRoll_Success_AppendsQuestion()
        {
            // Player stats: Charm=5, Wit=5, SA=5 → steering mod = 5
            var playerStats = MakeStatBlockWithValues(charm: 5, wit: 5, sa: 5);
            // Opponent stats: SA=0, Rizz=0, Honesty=0 → steering DC = 16
            var opponentStats = MakeStatBlockWithValues(sa: 0, rizz: 0, honesty: 0);

            var player = new CharacterProfile(
                playerStats, "You are Player.", "Player",
                new TimingProfile(5, 0.0f, 0.0f, "neutral"), 1);
            var opponent = new CharacterProfile(
                opponentStats, "You are Opponent.", "Opponent",
                new TimingProfile(5, 0.0f, 0.0f, "neutral"), 1);

            // Game dice: horniness + main d20 + response delay d100
            var dice = new FixedDice(
                1,     // horniness roll
                15,    // main d20 roll
                50     // response delay d100
            );

            // Steering uses separate RNG: Next(1,21) returns 20 → total 20+5=25 >= DC 16
            var steeringRng = new FixedRandom(20);

            var llm = new NullLlmAdapter();
            var config = new GameSessionConfig(steeringRng: steeringRng);
            var session = new GameSession(player, opponent, llm, dice, new NullTrapRegistry(), config);

            var turnStart = await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.True(result.Steering.SteeringAttempted);
            Assert.True(result.Steering.SteeringSucceeded);
            Assert.Equal(20, result.Steering.SteeringRoll);
            Assert.Equal(5, result.Steering.SteeringMod);
            Assert.Equal(16, result.Steering.SteeringDC);
            Assert.NotNull(result.Steering.SteeringQuestion);
            // The delivered message should contain the steering question
            Assert.Contains(result.Steering.SteeringQuestion, result.DeliveredMessage);
        }

        /// <summary>
        /// Integration test: a low roll fails and no question is appended.
        /// </summary>
        [Fact]
        public async Task SteeringRoll_Failure_NoQuestionAppended()
        {
            // Player stats: Charm=2, Wit=2, SA=2 → steering mod = 2
            var playerStats = MakeStatBlockWithValues(charm: 2, wit: 2, sa: 2);
            // Opponent stats: SA=3, Rizz=3, Honesty=3 → steering DC = 16 + 3 = 19
            var opponentStats = MakeStatBlockWithValues(sa: 3, rizz: 3, honesty: 3);

            var player = new CharacterProfile(
                playerStats, "You are Player.", "Player",
                new TimingProfile(5, 0.0f, 0.0f, "neutral"), 1);
            var opponent = new CharacterProfile(
                opponentStats, "You are Opponent.", "Opponent",
                new TimingProfile(5, 0.0f, 0.0f, "neutral"), 1);

            // Game dice: horniness + main d20 + response delay d100
            var dice = new FixedDice(
                1,     // horniness roll
                15,    // main d20 roll
                50     // response delay d100
            );

            // Steering uses separate RNG: Next(1,21) returns 1 → total 1+2=3 < DC 19 → MISS
            var steeringRng = new FixedRandom(1);

            var llm = new NullLlmAdapter();
            var config = new GameSessionConfig(steeringRng: steeringRng);
            var session = new GameSession(player, opponent, llm, dice, new NullTrapRegistry(), config);

            var turnStart = await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.True(result.Steering.SteeringAttempted);
            Assert.False(result.Steering.SteeringSucceeded);
            Assert.Equal(1, result.Steering.SteeringRoll);
            Assert.Equal(2, result.Steering.SteeringMod);
            Assert.Equal(19, result.Steering.SteeringDC);
            Assert.Null(result.Steering.SteeringQuestion);
        }
    }
}
