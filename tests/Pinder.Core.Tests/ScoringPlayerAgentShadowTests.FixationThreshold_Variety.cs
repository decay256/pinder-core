using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Pinder.Core.Conversation;
using Pinder.Core.Stats;
using Xunit;

namespace Pinder.Core.Tests
{
    public partial class ScoringPlayerAgentShadowTests
    {
        #region AC: Fixation threshold — Chaos EV reduction

        // T1 (Fixation >= 6): Chaos EV reduced by 20%
        [Fact]
        public async Task FixationT1_ChaosEvReduced20Percent()
        {
            var player = MakeStats(chaos: 3, charm: 3);
            var datee = MakeStats();

            var shadowsT1 = new Dictionary<ShadowStatType, int>
            {
                { ShadowStatType.Fixation, 6 },
                { ShadowStatType.Madness, 0 },
                { ShadowStatType.Despair, 0 },
                { ShadowStatType.Denial, 0 },
                { ShadowStatType.Dread, 0 },
                { ShadowStatType.Overthinking, 0 }
            };
            var shadowsNone = new Dictionary<ShadowStatType, int>
            {
                { ShadowStatType.Fixation, 0 },
                { ShadowStatType.Madness, 0 },
                { ShadowStatType.Despair, 0 },
                { ShadowStatType.Denial, 0 },
                { ShadowStatType.Dread, 0 },
                { ShadowStatType.Overthinking, 0 }
            };

            var turnChaos = MakeTurn(MakeOption(StatType.Chaos));

            var resultT1 = await _agent.DecideAsync(turnChaos,
                MakeContext(player: player, datee: datee, shadowValues: shadowsT1));
            var resultNone = await _agent.DecideAsync(turnChaos,
                MakeContext(player: player, datee: datee, shadowValues: shadowsNone));

            // Chaos EV should be lower with Fixation T1
            Assert.True(resultT1.Scores[0].Score < resultNone.Scores[0].Score,
                $"Fixation T1 should reduce Chaos score: T1={resultT1.Scores[0].Score}, None={resultNone.Scores[0].Score}");
        }

        // T2 (Fixation >= 12): Chaos gets disadvantage
        [Fact]
        public async Task FixationT2_ChaosDisadvantageApplied()
        {
            var player = MakeStats(chaos: 3, charm: 3);
            var datee = MakeStats();

            var shadowsT2 = new Dictionary<ShadowStatType, int>
            {
                { ShadowStatType.Fixation, 12 },
                { ShadowStatType.Madness, 0 },
                { ShadowStatType.Despair, 0 },
                { ShadowStatType.Denial, 0 },
                { ShadowStatType.Dread, 0 },
                { ShadowStatType.Overthinking, 0 }
            };
            var shadowsT1 = new Dictionary<ShadowStatType, int>
            {
                { ShadowStatType.Fixation, 6 },
                { ShadowStatType.Madness, 0 },
                { ShadowStatType.Despair, 0 },
                { ShadowStatType.Denial, 0 },
                { ShadowStatType.Dread, 0 },
                { ShadowStatType.Overthinking, 0 }
            };

            var turnChaos = MakeTurn(MakeOption(StatType.Chaos));

            var resultT2 = await _agent.DecideAsync(turnChaos,
                MakeContext(player: player, datee: datee, shadowValues: shadowsT2));
            var resultT1 = await _agent.DecideAsync(turnChaos,
                MakeContext(player: player, datee: datee, shadowValues: shadowsT1));

            // T2 disadvantage should reduce score more than T1 reduction
            Assert.True(resultT2.Scores[0].Score < resultT1.Scores[0].Score,
                $"Fixation T2 should reduce Chaos score more than T1: T2={resultT2.Scores[0].Score}, T1={resultT1.Scores[0].Score}");
        }

        // Non-Chaos options unaffected by Fixation threshold
        [Fact]
        public async Task FixationThreshold_DoesNotAffectNonChaosOptions()
        {
            var player = MakeStats(charm: 3);
            var datee = MakeStats();

            var shadowsT2 = new Dictionary<ShadowStatType, int>
            {
                { ShadowStatType.Fixation, 12 },
                { ShadowStatType.Madness, 0 },
                { ShadowStatType.Despair, 0 },
                { ShadowStatType.Denial, 0 },
                { ShadowStatType.Dread, 0 },
                { ShadowStatType.Overthinking, 0 }
            };
            var shadowsNone = new Dictionary<ShadowStatType, int>
            {
                { ShadowStatType.Fixation, 0 },
                { ShadowStatType.Madness, 0 },
                { ShadowStatType.Despair, 0 },
                { ShadowStatType.Denial, 0 },
                { ShadowStatType.Dread, 0 },
                { ShadowStatType.Overthinking, 0 }
            };

            var turnCharm = MakeTurn(MakeOption(StatType.Charm));

            var resultT2 = await _agent.DecideAsync(turnCharm,
                MakeContext(player: player, datee: datee, shadowValues: shadowsT2));
            var resultNone = await _agent.DecideAsync(turnCharm,
                MakeContext(player: player, datee: datee, shadowValues: shadowsNone));

            // Charm score should be identical regardless of Fixation level
            Assert.Equal((double)resultT2.Scores[0].Score, (double)resultNone.Scores[0].Score, 4);
        }

        // Null shadow values → skip threshold checks
        [Fact]
        public async Task FixationThreshold_SkippedWhenShadowValuesNull()
        {
            var player = MakeStats(chaos: 3);
            var datee = MakeStats();

            var turnChaos = MakeTurn(MakeOption(StatType.Chaos));

            // No shadow values (null) — should not crash or apply any reduction
            var result = await _agent.DecideAsync(turnChaos,
                MakeContext(player: player, datee: datee, shadowValues: null));

            Assert.NotNull(result);
            Assert.Equal(0, result.OptionIndex);
        }

        #endregion

        #region AC: Stat variety bonus

        // Picking a stat not used recently gets +0.1
        [Fact]
        public async Task StatVarietyBonus_AppliedToUnusedStat()
        {
            var player = MakeStats(charm: 3, rizz: 3, wit: 3);
            var datee = MakeStats();

            var turn = MakeTurn(
                MakeOption(StatType.Charm),
                MakeOption(StatType.Rizz),
                MakeOption(StatType.Wit));

            // Last two turns: Charm, Rizz
            var context = MakeContext(
                player: player, datee: datee,
                lastStatUsed: StatType.Charm,
                secondLastStatUsed: StatType.Rizz);

            var decision = await _agent.DecideAsync(turn, context);

            // Wit (not used recently) should get +0.1 variety bonus
            // Charm and Rizz were used recently → no variety bonus
            float witScore = decision.Scores[2].Score;
            float charmScore = decision.Scores[0].Score;
            float rizzScore = decision.Scores[1].Score;

            // Wit should be higher than both due to variety bonus
            Assert.True(witScore > charmScore,
                $"Wit should beat Charm with variety bonus: Wit={witScore}, Charm={charmScore}");
            Assert.True(witScore > rizzScore,
                $"Wit should beat Rizz with variety bonus: Wit={witScore}, Rizz={rizzScore}");
        }

        // No variety bonus when stat history is empty
        [Fact]
        public async Task StatVarietyBonus_NotAppliedWhenNoHistory()
        {
            var player = MakeStats(charm: 3, rizz: 3);
            var datee = MakeStats();

            var turn = MakeTurn(
                MakeOption(StatType.Charm),
                MakeOption(StatType.Rizz));

            // No stat history
            var context = MakeContext(player: player, datee: datee);

            var decision = await _agent.DecideAsync(turn, context);

            // Scores should be identical (no variety adjustments)
            float diff = Math.Abs(decision.Scores[0].Score - decision.Scores[1].Score);
            Assert.True(diff < 0.01f,
                $"Scores should be equal without stat history: diff={diff}");
        }

        #endregion
    }
}
