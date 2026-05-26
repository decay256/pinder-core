using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Pinder.Core.Conversation;
using Pinder.Core.Stats;
using Pinder.SessionRunner;
using Xunit;

namespace Pinder.Core.Tests
{
    public partial class ScoringPlayerAgentShadowTests
    {
        #region AC: Agent avoids 3rd consecutive same-stat when close EV

        // Integration test: all shadow adjustments combine correctly
        [Fact]
        public async Task CombinedShadowAdjustments_AgentAvoidsThirdSameStat()
        {
            // Setup: Honesty was used last 2 turns, Honesty and Charm available
            // Equal mods (charm=3, honesty=3) → equal base EV.
            // With Fixation penalty (-0.5) on Honesty and variety bonus (+0.1) on Charm, Charm wins.
            var player = MakeStats(charm: 3, honesty: 3);
            var opponent = MakeStats();

            var turn = MakeTurn(
                MakeOption(StatType.Honesty),
                MakeOption(StatType.Charm));

            var context = MakeContext(
                player: player, opponent: opponent,
                lastStatUsed: StatType.Honesty,
                secondLastStatUsed: StatType.Honesty);

            var decision = await _agent.DecideAsync(turn, context);

            // Honesty gets: -0.5 Fixation penalty, no Denial penalty
            // Charm gets: -0.3 Denial penalty (skipping Honesty), +0.1 variety bonus
            // Honesty base EV is higher due to +3 vs +2 mod, but -0.5 Fixation should outweigh
            // Net adjustments: Honesty: -0.5, Charm: -0.3 + 0.1 = -0.2
            // Difference in adjustments: 0.3 in Charm's favor
            // This test validates the agent avoids the third consecutive pick
            Assert.Equal(1, decision.OptionIndex);
        }

        #endregion

        #region AC: PlayerAgentContext new fields

        [Fact]
        public void PlayerAgentContext_NewFieldsHaveDefaults()
        {
            // Existing constructor call should still work (backward compat)
            var context = new PlayerAgentContext(
                MakeStats(),
                MakeStats(),
                10,
                InterestState.Interested,
                0,
                Array.Empty<string>(),
                0,
                null,
                3);

            Assert.Null(context.LastStatUsed);
            Assert.Null(context.SecondLastStatUsed);
            Assert.False(context.HonestyAvailableLastTurn);
        }

        [Fact]
        public void PlayerAgentContext_NewFieldsSetCorrectly()
        {
            var context = new PlayerAgentContext(
                MakeStats(),
                MakeStats(),
                10,
                InterestState.Interested,
                0,
                Array.Empty<string>(),
                0,
                null,
                3,
                lastStatUsed: StatType.Charm,
                secondLastStatUsed: StatType.Rizz,
                honestyAvailableLastTurn: true);

            Assert.Equal(StatType.Charm, context.LastStatUsed);
            Assert.Equal(StatType.Rizz, context.SecondLastStatUsed);
            Assert.True(context.HonestyAvailableLastTurn);
        }

        [Fact]
        public void PlayerAgentContext_ShadowValuesExposed()
        {
            var shadows = new Dictionary<ShadowStatType, int>
            {
                { ShadowStatType.Fixation, 8 },
                { ShadowStatType.Madness, 0 },
                { ShadowStatType.Despair, 0 },
                { ShadowStatType.Denial, 0 },
                { ShadowStatType.Dread, 0 },
                { ShadowStatType.Overthinking, 0 }
            };
            var context = new PlayerAgentContext(
                MakeStats(), MakeStats(),
                10, InterestState.Interested, 0,
                Array.Empty<string>(), 0, shadows, 3);

            Assert.NotNull(context.ShadowValues);
            Assert.Equal(8, context.ShadowValues![ShadowStatType.Fixation]);
        }

        #endregion

        #region Build clean verification

        // Ensures determinism is maintained with new scoring terms
        [Fact]
        public async Task Determinism_WithShadowContext_SameInputsSameOutput()
        {
            var shadows = new Dictionary<ShadowStatType, int>
            {
                { ShadowStatType.Fixation, 7 },
                { ShadowStatType.Madness, 0 },
                { ShadowStatType.Despair, 0 },
                { ShadowStatType.Denial, 0 },
                { ShadowStatType.Dread, 0 },
                { ShadowStatType.Overthinking, 0 }
            };
            var player = MakeStats(charm: 3, chaos: 3, honesty: 2);
            var opponent = MakeStats();

            var turn = MakeTurn(
                MakeOption(StatType.Charm),
                MakeOption(StatType.Chaos),
                MakeOption(StatType.Honesty));

            var context = MakeContext(
                player: player, opponent: opponent,
                shadowValues: shadows,
                lastStatUsed: StatType.Charm,
                secondLastStatUsed: StatType.Charm);

            var d1 = await _agent.DecideAsync(turn, context);
            var d2 = await _agent.DecideAsync(turn, context);

            Assert.Equal(d1.OptionIndex, d2.OptionIndex);
            for (int i = 0; i < d1.Scores.Length; i++)
            {
                Assert.Equal((double)d1.Scores[i].Score, (double)d2.Scores[i].Score, 4);
            }
        }

        #endregion
    }
}
