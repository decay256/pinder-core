using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Pinder.Core.Conversation;
using Pinder.Core.Stats;
using Pinder.SessionRunner;
using Xunit;

namespace Pinder.Core.Tests
{
    [Trait("Category", "Core")]
    public partial class PlayerDecisionSpecTests
    {
        // -- AC2: Constructor validation --

        // Fails if: null reasoning check is removed
        [Fact]
        public void Constructor_NullReasoning_ThrowsArgumentNullException()
        {
            var scores = new[] { MakeScore(0) };
            var ex = Assert.Throws<ArgumentNullException>(() => new PlayerDecision(0, null!, scores));
            Assert.Equal("reasoning", ex.ParamName);
        }

        // Fails if: null scores check is removed
        [Fact]
        public void Constructor_NullScores_ThrowsArgumentNullException()
        {
            var ex = Assert.Throws<ArgumentNullException>(() => new PlayerDecision(0, "r", null!));
            Assert.Equal("scores", ex.ParamName);
        }

        // Fails if: upper-bound check uses <= instead of <
        [Fact]
        public void Constructor_OptionIndexEqualToScoresLength_ThrowsOutOfRange()
        {
            var scores = new[] { MakeScore(0), MakeScore(1) };
            Assert.Throws<ArgumentOutOfRangeException>(() => new PlayerDecision(2, "r", scores));
        }

        // Fails if: negative index check is removed
        [Fact]
        public void Constructor_NegativeOptionIndex_ThrowsOutOfRange()
        {
            var scores = new[] { MakeScore(0) };
            Assert.Throws<ArgumentOutOfRangeException>(() => new PlayerDecision(-1, "r", scores));
        }

        // Fails if: upper bound check is off by one (allows index == length)
        [Fact]
        public void Constructor_OptionIndexAtLastValid_Succeeds()
        {
            var scores = new[] { MakeScore(0), MakeScore(1), MakeScore(2) };
            var decision = new PlayerDecision(2, "last", scores);
            Assert.Equal(2, decision.OptionIndex);
        }

        // -- Edge case: empty reasoning is valid for deterministic agents --

        // Fails if: empty string is treated as null
        [Fact]
        public void Constructor_EmptyReasoning_IsValid()
        {
            var scores = new[] { MakeScore(0) };
            var decision = new PlayerDecision(0, "", scores);
            Assert.Equal("", decision.Reasoning);
        }
    }

    [Trait("Category", "Core")]
    public class OptionScoreSpecTests
    {
        // -- AC2: OptionScore properties set via constructor --

        // Fails if: Score property returns wrong value
        [Fact]
        public void Constructor_SetsAllProperties()
        {
            var score = new OptionScore(3, 8.5f, 0.75f, 2.1f, new[] { "tell +2", "callback" });
            Assert.Equal(3, score.OptionIndex);
            Assert.Equal(8.5f, score.Score);
            Assert.Equal(0.75f, score.SuccessChance);
            Assert.Equal(2.1f, score.ExpectedInterestGain);
            Assert.Equal(2, score.BonusesApplied.Length);
        }

        // Fails if: null bonuses check is removed
        [Fact]
        public void Constructor_NullBonuses_ThrowsArgumentNullException()
        {
            var ex = Assert.Throws<ArgumentNullException>(
                () => new OptionScore(0, 1.0f, 0.5f, 0.0f, null!));
            Assert.Equal("bonusesApplied", ex.ParamName);
        }

        // -- Spec invariant: SuccessChance clamped to [0.0, 1.0] --

        // Fails if: upper clamp is removed
        [Fact]
        public void SuccessChance_Above1_ClampedTo1()
        {
            var score = new OptionScore(0, 1.0f, 1.5f, 0.0f, Array.Empty<string>());
            Assert.Equal(1.0f, score.SuccessChance);
        }

        // Fails if: lower clamp is removed
        [Fact]
        public void SuccessChance_BelowZero_ClampedToZero()
        {
            var score = new OptionScore(0, 1.0f, -0.3f, 0.0f, Array.Empty<string>());
            Assert.Equal(0.0f, score.SuccessChance);
        }

        // Fails if: boundary values are excluded from valid range
        [Fact]
        public void SuccessChance_ExactBoundaries_Preserved()
        {
            var zero = new OptionScore(0, 1.0f, 0.0f, 0.0f, Array.Empty<string>());
            Assert.Equal(0.0f, zero.SuccessChance);

            var one = new OptionScore(0, 1.0f, 1.0f, 0.0f, Array.Empty<string>());
            Assert.Equal(1.0f, one.SuccessChance);
        }

        // -- Edge case: negative expected interest gain is valid --

        // Fails if: ExpectedInterestGain is clamped to >= 0
        [Fact]
        public void ExpectedInterestGain_CanBeNegative()
        {
            var score = new OptionScore(0, -2.0f, 0.2f, -3.5f, Array.Empty<string>());
            Assert.Equal(-3.5f, score.ExpectedInterestGain);
        }

        // -- Edge case: empty bonuses array is valid --

        // Fails if: empty array treated as invalid
        [Fact]
        public void EmptyBonusesArray_IsValid()
        {
            var score = new OptionScore(0, 1.0f, 0.5f, 0.0f, Array.Empty<string>());
            Assert.Empty(score.BonusesApplied);
        }

        // -- Edge case: all bonuses stacked --

        // Fails if: bonuses array length is limited
        [Fact]
        public void AllBonusesStacked_Accepted()
        {
            var bonuses = new[] { "tell +2", "callback +2", "combo", "weakness -2" };
            var score = new OptionScore(0, 10.0f, 0.6f, 3.0f, bonuses);
            Assert.Equal(4, score.BonusesApplied.Length);
        }
    }

    [Trait("Category", "Core")]
    public class PlayerAgentContextSpecTests
    {
        private static StatBlock MakeStats(int charm = 3, int rizz = 2, int honesty = 2,
            int chaos = 2, int wit = 2, int sa = 2)
        {
            return new StatBlock(
                new Dictionary<StatType, int>
                {
                    { StatType.Charm, charm }, { StatType.Rizz, rizz }, { StatType.Honesty, honesty },
                    { StatType.Chaos, chaos }, { StatType.Wit, wit }, { StatType.SelfAwareness, sa }
                },
                new Dictionary<ShadowStatType, int>
                {
                    { ShadowStatType.Madness, 0 }, { ShadowStatType.Despair, 0 },
                    { ShadowStatType.Denial, 0 }, { ShadowStatType.Fixation, 0 },
                    { ShadowStatType.Dread, 0 }, { ShadowStatType.Overthinking, 0 }
                });
        }

        // -- AC3: All properties set correctly --

        // Fails if: any property assignment is swapped or missing
        [Fact]
        public void Constructor_SetsAllProperties()
        {
            var player = MakeStats(charm: 4);
            var opponent = MakeStats(charm: 2);
            var shadows = new Dictionary<ShadowStatType, int>
            {
                { ShadowStatType.Despair, 6 },
                { ShadowStatType.Dread, 3 }
            };

            var ctx = new PlayerAgentContext(
                player, opponent, 15, InterestState.VeryIntoIt, 3,
                new[] { "IckTrap", "Cringe" }, 6, shadows, 8);

            Assert.Same(player, ctx.PlayerStats);
            Assert.Same(opponent, ctx.OpponentStats);
            Assert.Equal(15, ctx.CurrentInterest);
            Assert.Equal(InterestState.VeryIntoIt, ctx.InterestState);
            Assert.Equal(3, ctx.MomentumStreak);
            Assert.Equal(2, ctx.ActiveTrapNames.Length);
            Assert.Equal(6, ctx.SessionHorniness);
            Assert.NotNull(ctx.ShadowValues);
            Assert.Equal(6, ctx.ShadowValues![ShadowStatType.Despair]);
            Assert.Equal(8, ctx.TurnNumber);
        }

        // -- AC3: Null validation --

        // Fails if: playerStats null check removed
        [Fact]
        public void Constructor_NullPlayerStats_Throws()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new PlayerAgentContext(null!, MakeStats(), 10, InterestState.Interested,
                    0, Array.Empty<string>(), 0, null, 1));
        }

        // Fails if: opponentStats null check removed
        [Fact]
        public void Constructor_NullOpponentStats_Throws()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new PlayerAgentContext(MakeStats(), null!, 10, InterestState.Interested,
                    0, Array.Empty<string>(), 0, null, 1));
        }

        // Fails if: activeTrapNames null check removed
        [Fact]
        public void Constructor_NullActiveTrapNames_Throws()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new PlayerAgentContext(MakeStats(), MakeStats(), 10, InterestState.Interested,
                    0, null!, 0, null, 1));
        }

        // -- AC3: ShadowValues nullable --

        // Fails if: null shadow values causes crash
        [Fact]
        public void ShadowValues_Null_IsAccepted()
        {
            var ctx = new PlayerAgentContext(
                MakeStats(), MakeStats(), 10, InterestState.Interested,
                0, Array.Empty<string>(), 0, null, 1);
            Assert.Null(ctx.ShadowValues);
        }

        // -- Edge case: extreme interest values --

        // Fails if: interest value 0 is rejected
        [Fact]
        public void CurrentInterest_Zero_Accepted()
        {
            var ctx = new PlayerAgentContext(
                MakeStats(), MakeStats(), 0, InterestState.Unmatched,
                0, Array.Empty<string>(), 0, null, 1);
            Assert.Equal(0, ctx.CurrentInterest);
        }

        // Fails if: interest value 25 is rejected
        [Fact]
        public void CurrentInterest_TwentyFive_Accepted()
        {
            var ctx = new PlayerAgentContext(
                MakeStats(), MakeStats(), 25, InterestState.DateSecured,
                0, Array.Empty<string>(), 0, null, 1);
            Assert.Equal(25, ctx.CurrentInterest);
        }

        // -- Edge case: momentum streak values --

        // Fails if: zero momentum rejected
        [Fact]
        public void MomentumStreak_Zero_Accepted()
        {
            var ctx = new PlayerAgentContext(
                MakeStats(), MakeStats(), 10, InterestState.Interested,
                0, Array.Empty<string>(), 0, null, 1);
            Assert.Equal(0, ctx.MomentumStreak);
        }

        // Fails if: high momentum values rejected
        [Fact]
        public void MomentumStreak_HighValue_Accepted()
        {
            var ctx = new PlayerAgentContext(
                MakeStats(), MakeStats(), 10, InterestState.Interested,
                10, Array.Empty<string>(), 0, null, 1);
            Assert.Equal(10, ctx.MomentumStreak);
        }

        // -- Edge case: empty trap names --

        // Fails if: empty array is treated as null
        [Fact]
        public void ActiveTrapNames_EmptyArray_Accepted()
        {
            var ctx = new PlayerAgentContext(
                MakeStats(), MakeStats(), 10, InterestState.Interested,
                0, Array.Empty<string>(), 0, null, 1);
            Assert.Empty(ctx.ActiveTrapNames);
        }
    }
}