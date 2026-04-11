using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Pinder.Core.Conversation;
using Pinder.Core.Stats;
using Pinder.SessionRunner;
using Xunit;

namespace Pinder.Core.Tests
{
    /// <summary>
    /// Spec-driven tests for issue #346: IPlayerAgent interface and supporting DTOs.
    /// Tests verify acceptance criteria from docs/specs/issue-346-spec.md.
    /// </summary>
    public class PlayerDecisionSpecTests
    {
        #region Helpers

        private static OptionScore MakeScore(int index, float score = 1.0f)
        {
            return new OptionScore(index, score, 0.5f, 0.0f, Array.Empty<string>());
        }

        #endregion

        // -- AC2: PlayerDecision properties are read-only and set via constructor --

        // Fails if: OptionIndex property returns wrong value or isn't set from constructor
        [Fact]
        public void Constructor_SetsOptionIndex()
        {
            var scores = new[] { MakeScore(0), MakeScore(1), MakeScore(2) };
            var decision = new PlayerDecision(2, "picked third", scores);
            Assert.Equal(2, decision.OptionIndex);
        }

        // Fails if: Reasoning property returns null or different string
        [Fact]
        public void Constructor_SetsReasoning()
        {
            var scores = new[] { MakeScore(0) };
            var decision = new PlayerDecision(0, "some reasoning", scores);
            Assert.Equal("some reasoning", decision.Reasoning);
        }

        // Fails if: Scores array reference is lost or copied incorrectly
        [Fact]
        public void Constructor_SetsScoresArray()
        {
            var scores = new[] { MakeScore(0), MakeScore(1) };
            var decision = new PlayerDecision(1, "reason", scores);
            Assert.Equal(2, decision.Scores.Length);
            Assert.Equal(1, decision.Scores[1].OptionIndex);
        }

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
                    { ShadowStatType.Madness, 0 }, { ShadowStatType.Horniness, 0 },
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
                { ShadowStatType.Horniness, 6 },
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
            Assert.Equal(6, ctx.ShadowValues![ShadowStatType.Horniness]);
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

    /// <summary>
    /// Tests for HighestModAgent (the baseline IPlayerAgent implementation from #346).
    /// Verifies AC1 (IPlayerAgent interface contract) and AC4 (session runner integration).
    /// </summary>
    public class HighestModAgentSpecTests
    {
        private static StatBlock MakePlayerStats()
        {
            return new StatBlock(
                new Dictionary<StatType, int>
                {
                    { StatType.Charm, 4 }, { StatType.Rizz, 1 }, { StatType.Honesty, 3 },
                    { StatType.Chaos, 2 }, { StatType.Wit, 2 }, { StatType.SelfAwareness, 3 }
                },
                new Dictionary<ShadowStatType, int>
                {
                    { ShadowStatType.Madness, 0 }, { ShadowStatType.Horniness, 0 },
                    { ShadowStatType.Denial, 0 }, { ShadowStatType.Fixation, 0 },
                    { ShadowStatType.Dread, 0 }, { ShadowStatType.Overthinking, 0 }
                });
        }

        private static StatBlock MakeOpponentStats()
        {
            return new StatBlock(
                new Dictionary<StatType, int>
                {
                    { StatType.Charm, 2 }, { StatType.Rizz, 3 }, { StatType.Honesty, 1 },
                    { StatType.Chaos, 2 }, { StatType.Wit, 1 }, { StatType.SelfAwareness, 2 }
                },
                new Dictionary<ShadowStatType, int>
                {
                    { ShadowStatType.Madness, 0 }, { ShadowStatType.Horniness, 0 },
                    { ShadowStatType.Denial, 0 }, { ShadowStatType.Fixation, 0 },
                    { ShadowStatType.Dread, 0 }, { ShadowStatType.Overthinking, 0 }
                });
        }

        private static PlayerAgentContext MakeContext(StatBlock player, StatBlock opponent,
            int interest = 12, InterestState state = InterestState.Interested, int momentum = 0)
        {
            return new PlayerAgentContext(player, opponent, interest, state, momentum,
                Array.Empty<string>(), 0, null, 1);
        }

        private static TurnStart MakeTurn(DialogueOption[] options, int interest = 12,
            InterestState state = InterestState.Interested, int momentum = 0, int turn = 1)
        {
            return new TurnStart(options,
                new GameStateSnapshot(interest, state, momentum, Array.Empty<string>(), turn));
        }

        // -- AC1: IPlayerAgent interface compliance --

        // Fails if: HighestModAgent doesn't implement IPlayerAgent
        [Fact]
        public void HighestModAgent_ImplementsIPlayerAgent()
        {
            IPlayerAgent agent = new HighestModAgent();
            Assert.NotNull(agent);
        }

        // -- AC1: DecideAsync returns Task<PlayerDecision> --

        // Fails if: DecideAsync doesn't return completed task
        [Fact]
        public async Task DecideAsync_ReturnsCompletedTask()
        {
            var agent = new HighestModAgent();
            var options = new[] { new DialogueOption(StatType.Charm, "hi") };
            var turn = MakeTurn(options);
            var ctx = MakeContext(MakePlayerStats(), MakeOpponentStats());

            var decision = await agent.DecideAsync(turn, ctx);
            Assert.NotNull(decision);
        }

        // -- AC1 + AC4: Picks option with highest modifier --

        // Fails if: selection logic picks lowest instead of highest modifier
        [Fact]
        public async Task DecideAsync_PicksHighestModifierOption()
        {
            var agent = new HighestModAgent();
            var player = MakePlayerStats(); // Charm=4 is highest
            var options = new[]
            {
                new DialogueOption(StatType.Rizz, "low"),     // mod +1
                new DialogueOption(StatType.Charm, "high"),    // mod +4
                new DialogueOption(StatType.Honesty, "mid"),   // mod +3
            };
            var turn = MakeTurn(options);
            var ctx = MakeContext(player, MakeOpponentStats());

            var decision = await agent.DecideAsync(turn, ctx);

            Assert.Equal(1, decision.OptionIndex);
        }

        // -- Edge case: Scores array length matches options --

        // Fails if: Scores length doesn't match Options length
        [Fact]
        public async Task DecideAsync_ScoresLengthMatchesOptionsLength()
        {
            var agent = new HighestModAgent();
            var options = new[]
            {
                new DialogueOption(StatType.Charm, "a"),
                new DialogueOption(StatType.Rizz, "b"),
                new DialogueOption(StatType.Honesty, "c"),
                new DialogueOption(StatType.Chaos, "d"),
            };
            var turn = MakeTurn(options);
            var ctx = MakeContext(MakePlayerStats(), MakeOpponentStats());

            var decision = await agent.DecideAsync(turn, ctx);

            Assert.Equal(4, decision.Scores.Length);
        }

        // -- Edge case: single option always picks index 0 --

        // Fails if: single option returns wrong index
        [Fact]
        public async Task DecideAsync_SingleOption_ReturnsZero()
        {
            var agent = new HighestModAgent();
            var options = new[] { new DialogueOption(StatType.Wit, "only") };
            var turn = MakeTurn(options);
            var ctx = MakeContext(MakePlayerStats(), MakeOpponentStats());

            var decision = await agent.DecideAsync(turn, ctx);

            Assert.Equal(0, decision.OptionIndex);
            Assert.Single(decision.Scores);
        }

        // -- Edge case: all identical stats → tiebreak to lowest index --

        // Fails if: tiebreak doesn't pick index 0
        [Fact]
        public async Task DecideAsync_IdenticalStats_TiebreaksToLowestIndex()
        {
            var agent = new HighestModAgent();
            var equalStats = new StatBlock(
                new Dictionary<StatType, int>
                {
                    { StatType.Charm, 3 }, { StatType.Rizz, 3 }, { StatType.Honesty, 3 },
                    { StatType.Chaos, 3 }, { StatType.Wit, 3 }, { StatType.SelfAwareness, 3 }
                },
                new Dictionary<ShadowStatType, int>
                {
                    { ShadowStatType.Madness, 0 }, { ShadowStatType.Horniness, 0 },
                    { ShadowStatType.Denial, 0 }, { ShadowStatType.Fixation, 0 },
                    { ShadowStatType.Dread, 0 }, { ShadowStatType.Overthinking, 0 }
                });
            var options = new[]
            {
                new DialogueOption(StatType.Charm, "a"),
                new DialogueOption(StatType.Rizz, "b"),
                new DialogueOption(StatType.Wit, "c"),
            };
            var turn = MakeTurn(options);
            var ctx = MakeContext(equalStats, MakeOpponentStats());

            var decision = await agent.DecideAsync(turn, ctx);

            Assert.Equal(0, decision.OptionIndex);
        }

        // -- Error condition: empty options throws InvalidOperationException --

        // Fails if: empty options doesn't throw or throws wrong exception type
        [Fact]
        public async Task DecideAsync_EmptyOptions_ThrowsInvalidOperationException()
        {
            var agent = new HighestModAgent();
            var turn = MakeTurn(Array.Empty<DialogueOption>());
            var ctx = MakeContext(MakePlayerStats(), MakeOpponentStats());

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => agent.DecideAsync(turn, ctx));
        }

        // -- Error condition: null turn throws ArgumentNullException --

        // Fails if: null turn check removed
        [Fact]
        public async Task DecideAsync_NullTurn_ThrowsArgumentNullException()
        {
            var agent = new HighestModAgent();
            var ctx = MakeContext(MakePlayerStats(), MakeOpponentStats());

            await Assert.ThrowsAsync<ArgumentNullException>(
                () => agent.DecideAsync(null!, ctx));
        }

        // -- Error condition: null context throws ArgumentNullException --

        // Fails if: null context check removed
        [Fact]
        public async Task DecideAsync_NullContext_ThrowsArgumentNullException()
        {
            var agent = new HighestModAgent();
            var options = new[] { new DialogueOption(StatType.Charm, "hi") };
            var turn = MakeTurn(options);

            await Assert.ThrowsAsync<ArgumentNullException>(
                () => agent.DecideAsync(turn, null!));
        }

        // -- Spec: SuccessChance is probability 0.0-1.0 --

        // Fails if: SuccessChance returned as percentage (0-100) instead of probability (0.0-1.0)
        [Fact]
        public async Task DecideAsync_SuccessChance_IsProbabilityNotPercentage()
        {
            var agent = new HighestModAgent();
            var player = MakePlayerStats(); // Charm +4
            var opponent = MakeOpponentStats(); // SA defence DC = 16 + 2 = 18
            // Charm +4 vs DC 18: need 14, success = (21-14)/20 = 0.35
            var options = new[] { new DialogueOption(StatType.Charm, "charm line") };
            var turn = MakeTurn(options);
            var ctx = MakeContext(player, opponent);

            var decision = await agent.DecideAsync(turn, ctx);

            // Must be 0.35, not 35.0
            Assert.True(decision.Scores[0].SuccessChance >= 0.0f);
            Assert.True(decision.Scores[0].SuccessChance <= 1.0f);
            Assert.Equal(0.35f, decision.Scores[0].SuccessChance);
        }

        // -- Spec: Reasoning is never null --

        // Fails if: Reasoning is null
        [Fact]
        public async Task DecideAsync_ReasoningIsNeverNull()
        {
            var agent = new HighestModAgent();
            var options = new[] { new DialogueOption(StatType.Charm, "hi") };
            var turn = MakeTurn(options);
            var ctx = MakeContext(MakePlayerStats(), MakeOpponentStats());

            var decision = await agent.DecideAsync(turn, ctx);

            Assert.NotNull(decision.Reasoning);
        }

        // -- Spec: OptionIndex is in valid range --

        // Fails if: OptionIndex is out of [0, Scores.Length)
        [Fact]
        public async Task DecideAsync_OptionIndex_WithinScoresRange()
        {
            var agent = new HighestModAgent();
            var options = new[]
            {
                new DialogueOption(StatType.Charm, "a"),
                new DialogueOption(StatType.Rizz, "b"),
                new DialogueOption(StatType.Honesty, "c"),
            };
            var turn = MakeTurn(options);
            var ctx = MakeContext(MakePlayerStats(), MakeOpponentStats());

            var decision = await agent.DecideAsync(turn, ctx);

            Assert.True(decision.OptionIndex >= 0);
            Assert.True(decision.OptionIndex < decision.Scores.Length);
        }

        // -- Spec: Each score has matching OptionIndex --

        // Fails if: Scores[i].OptionIndex != i
        [Fact]
        public async Task DecideAsync_ScoreOptionIndicesMatchPositions()
        {
            var agent = new HighestModAgent();
            var options = new[]
            {
                new DialogueOption(StatType.Charm, "a"),
                new DialogueOption(StatType.Rizz, "b"),
            };
            var turn = MakeTurn(options);
            var ctx = MakeContext(MakePlayerStats(), MakeOpponentStats());

            var decision = await agent.DecideAsync(turn, ctx);

            for (int i = 0; i < decision.Scores.Length; i++)
            {
                Assert.Equal(i, decision.Scores[i].OptionIndex);
            }
        }

        // -- Spec: BonusesApplied is never null on any score --

        // Fails if: BonusesApplied is null for any score
        [Fact]
        public async Task DecideAsync_BonusesApplied_NeverNull()
        {
            var agent = new HighestModAgent();
            var options = new[]
            {
                new DialogueOption(StatType.Charm, "a"),
                new DialogueOption(StatType.Rizz, "b", callbackTurnNumber: 2),
                new DialogueOption(StatType.Honesty, "c", hasTellBonus: true),
            };
            var turn = MakeTurn(options);
            var ctx = MakeContext(MakePlayerStats(), MakeOpponentStats());

            var decision = await agent.DecideAsync(turn, ctx);

            foreach (var score in decision.Scores)
            {
                Assert.NotNull(score.BonusesApplied);
            }
        }

        // -- Spec Example 1: Charm +4 vs SA DC=15, need 11, success = 50% --

        // Fails if: DC calculation or success probability formula is wrong
        [Fact]
        public async Task DecideAsync_SpecExample1_CharmSuccessChance()
        {
            var agent = new HighestModAgent();
            // Player: Charm +4, Rizz +1, Honesty +3, Chaos +2
            var player = new StatBlock(
                new Dictionary<StatType, int>
                {
                    { StatType.Charm, 4 }, { StatType.Rizz, 1 }, { StatType.Honesty, 3 },
                    { StatType.Chaos, 2 }, { StatType.Wit, 2 }, { StatType.SelfAwareness, 3 }
                },
                new Dictionary<ShadowStatType, int>
                {
                    { ShadowStatType.Madness, 0 }, { ShadowStatType.Horniness, 0 },
                    { ShadowStatType.Denial, 0 }, { ShadowStatType.Fixation, 0 },
                    { ShadowStatType.Dread, 0 }, { ShadowStatType.Overthinking, 0 }
                });
            // Opponent: Charm +2, Rizz +3, Honesty +1, Chaos +2, Wit +1, SA +2
            var opponent = new StatBlock(
                new Dictionary<StatType, int>
                {
                    { StatType.Charm, 2 }, { StatType.Rizz, 3 }, { StatType.Honesty, 1 },
                    { StatType.Chaos, 2 }, { StatType.Wit, 1 }, { StatType.SelfAwareness, 2 }
                },
                new Dictionary<ShadowStatType, int>
                {
                    { ShadowStatType.Madness, 0 }, { ShadowStatType.Horniness, 0 },
                    { ShadowStatType.Denial, 0 }, { ShadowStatType.Fixation, 0 },
                    { ShadowStatType.Dread, 0 }, { ShadowStatType.Overthinking, 0 }
                });

            var options = new[]
            {
                new DialogueOption(StatType.Charm, "charm line"),   // Charm +4 vs SA DC=18, need 14
                new DialogueOption(StatType.Rizz, "rizz line"),     // Rizz +1 vs Wit DC=17, need 16
                new DialogueOption(StatType.Honesty, "hon line"),   // Honesty +3 vs Chaos DC=18, need 15
                new DialogueOption(StatType.Chaos, "chaos line"),   // Chaos +2 vs Charm DC=18, need 16
            };
            var turn = MakeTurn(options, interest: 12, state: InterestState.Interested, momentum: 2, turn: 5);
            var ctx = MakeContext(player, opponent, interest: 12, state: InterestState.Interested, momentum: 2);

            var decision = await agent.DecideAsync(turn, ctx);

            // Charm has highest modifier (+4), should be picked
            Assert.Equal(0, decision.OptionIndex);

            // Verify success chances with DC base 16: 35%, 25%, 30%, 25%
            Assert.Equal(0.35f, decision.Scores[0].SuccessChance);  // Charm
            Assert.Equal(0.25f, decision.Scores[1].SuccessChance);  // Rizz
            Assert.Equal(0.30f, decision.Scores[2].SuccessChance); // Honesty
            Assert.Equal(0.25f, decision.Scores[3].SuccessChance);  // Chaos
        }

        // -- Spec: Horniness-forced all-Rizz scenario (all same stat) --

        // Fails if: agent crashes when all options have same stat under Horniness T3
        [Fact]
        public async Task DecideAsync_AllRizzOptions_ReturnsIndex0()
        {
            var agent = new HighestModAgent();
            var options = new[]
            {
                new DialogueOption(StatType.Rizz, "rizz a"),
                new DialogueOption(StatType.Rizz, "rizz b"),
                new DialogueOption(StatType.Rizz, "rizz c"),
                new DialogueOption(StatType.Rizz, "rizz d"),
            };
            var turn = MakeTurn(options);
            var ctx = MakeContext(MakePlayerStats(), MakeOpponentStats());

            var decision = await agent.DecideAsync(turn, ctx);

            // All identical, tiebreak to lowest index
            Assert.Equal(0, decision.OptionIndex);
            // All success chances should be identical
            var firstChance = decision.Scores[0].SuccessChance;
            foreach (var score in decision.Scores)
            {
                Assert.Equal(firstChance, score.SuccessChance);
            }
        }
    }
}
