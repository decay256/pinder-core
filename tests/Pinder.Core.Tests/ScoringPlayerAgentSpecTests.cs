using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Pinder.Core.Conversation;
using Pinder.Core.Stats;
using Pinder.SessionRunner;
using Xunit;

namespace Pinder.Core.Tests
{
    /// <summary>
    /// Spec-driven tests for ScoringPlayerAgent (issue #347).
    /// Written from docs/specs/issue-347-spec.md only — context-isolated from implementation.
    /// </summary>
    [Trait("Category", "Core")]
    public partial class ScoringPlayerAgentSpecTests
    {
        private readonly ScoringPlayerAgent _agent = new ScoringPlayerAgent();

        #region Helpers

        private static StatBlock MakeStats(
            int charm = 0, int rizz = 0, int honesty = 0,
            int chaos = 0, int wit = 0, int selfAwareness = 0,
            int madness = 0, int horniness = 0, int denial = 0,
            int fixation = 0, int dread = 0, int overthinking = 0)
        {
            return new StatBlock(
                new Dictionary<StatType, int>
                {
                    { StatType.Charm, charm },
                    { StatType.Rizz, rizz },
                    { StatType.Honesty, honesty },
                    { StatType.Chaos, chaos },
                    { StatType.Wit, wit },
                    { StatType.SelfAwareness, selfAwareness }
                },
                new Dictionary<ShadowStatType, int>
                {
                    { ShadowStatType.Madness, madness },
                    { ShadowStatType.Despair, horniness },
                    { ShadowStatType.Denial, denial },
                    { ShadowStatType.Fixation, fixation },
                    { ShadowStatType.Dread, dread },
                    { ShadowStatType.Overthinking, overthinking }
                });
        }

        private static TurnStart MakeTurn(params DialogueOption[] options)
        {
            var snapshot = new GameStateSnapshot(
                interest: 12,
                state: InterestState.Interested,
                momentumStreak: 0,
                activeTrapNames: Array.Empty<string>(),
                turnNumber: 3);
            return new TurnStart(options, snapshot);
        }

        private static PlayerAgentContext MakeContext(
            StatBlock? playerStats = null,
            StatBlock? dateeStats = null,
            int currentInterest = 12,
            InterestState interestState = InterestState.Interested,
            int momentumStreak = 0,
            string[]? activeTrapNames = null,
            int sessionHorniness = 0,
            Dictionary<ShadowStatType, int>? shadowValues = null,
            int turnNumber = 3)
        {
            return new PlayerAgentContext(
                playerStats ?? MakeStats(charm: 3, rizz: 3, honesty: 3, chaos: 3, wit: 3, selfAwareness: 3),
                dateeStats ?? MakeStats(),
                currentInterest,
                interestState,
                momentumStreak,
                activeTrapNames ?? Array.Empty<string>(),
                sessionHorniness,
                shadowValues,
                turnNumber);
        }

        #endregion

        #region AC1: Implements IPlayerAgent

        // Mutation: would catch if ScoringPlayerAgent doesn't implement IPlayerAgent
        [Fact]
        public void ImplementsIPlayerAgentInterface()
        {
            IPlayerAgent agent = _agent;
            Assert.NotNull(agent);
        }

        // Mutation: would catch if DecideAsync returns null instead of Task<PlayerDecision>
        [Fact]
        public async Task DecideAsync_ReturnsNonNullDecision()
        {
            var turn = MakeTurn(new DialogueOption(StatType.Charm, "test"));
            var ctx = MakeContext();
            var result = await _agent.DecideAsync(turn, ctx);
            Assert.NotNull(result);
        }

        #endregion

        #region AC4: Reasoning

        // Mutation: would catch if Reasoning is null or empty
        [Fact]
        public async Task Reasoning_IsNotNullOrEmpty()
        {
            var turn = MakeTurn(new DialogueOption(StatType.Charm, "test"));
            var ctx = MakeContext();
            var result = await _agent.DecideAsync(turn, ctx);

            Assert.NotNull(result.Reasoning);
            Assert.NotEmpty(result.Reasoning);
        }

        #endregion

        #region AC5: Deterministic

        // Mutation: would catch if Random or non-deterministic state is used
        [Fact]
        public async Task Determinism_SameInputsSameOutput()
        {
            var playerStats = MakeStats(charm: 4, rizz: 2, honesty: 1, chaos: 3);
            var dateeStats = MakeStats(selfAwareness: 2, wit: 1, chaos: 0, charm: 1);

            var options = new[]
            {
                new DialogueOption(StatType.Charm, "a"),
                new DialogueOption(StatType.Rizz, "b"),
                new DialogueOption(StatType.Honesty, "c", hasTellBonus: true),
                new DialogueOption(StatType.Chaos, "d", comboName: "The Switcheroo")
            };

            var turn = MakeTurn(options);
            var ctx = MakeContext(playerStats: playerStats, dateeStats: dateeStats);

            var result1 = await _agent.DecideAsync(turn, ctx);
            var result2 = await _agent.DecideAsync(turn, ctx);

            Assert.Equal(result1.OptionIndex, result2.OptionIndex);
            Assert.Equal(result1.Reasoning, result2.Reasoning);
            for (int i = 0; i < result1.Scores.Length; i++)
            {
                Assert.Equal((double)result1.Scores[i].Score, (double)result2.Scores[i].Score, 4);
                Assert.Equal((double)result1.Scores[i].SuccessChance, (double)result2.Scores[i].SuccessChance, 4);
                Assert.Equal((double)result1.Scores[i].ExpectedInterestGain, (double)result2.Scores[i].ExpectedInterestGain, 4);
            }
        }

        #endregion

        #region Error conditions

        // Mutation: would catch if null turn doesn't throw ArgumentNullException
        [Fact]
        public async Task NullTurn_ThrowsArgumentNullException()
        {
            var ctx = MakeContext();
            await Assert.ThrowsAsync<ArgumentNullException>(() => _agent.DecideAsync(null!, ctx));
        }

        // Mutation: would catch if null context doesn't throw ArgumentNullException
        [Fact]
        public async Task NullContext_ThrowsArgumentNullException()
        {
            var turn = MakeTurn(new DialogueOption(StatType.Charm, "test"));
            await Assert.ThrowsAsync<ArgumentNullException>(() => _agent.DecideAsync(turn, null!));
        }

        #endregion
    }
}
