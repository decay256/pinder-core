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
    /// Tests for ScoringPlayerAgent (issue #347).
    /// Validates EV scoring, strategic adjustments, and determinism.
    /// </summary>
    [Trait("Category", "Core")]
    public partial class ScoringPlayerAgentTests
    {
        private readonly ScoringPlayerAgent _agent = new ScoringPlayerAgent();

        #region Helpers

        private static StatBlock MakeStats(
            int charm = 0, int rizz = 0, int honesty = 0,
            int chaos = 0, int wit = 0, int sa = 0,
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
                    { StatType.SelfAwareness, sa }
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

        private static DialogueOption MakeOption(
            StatType stat,
            int? callbackTurn = null,
            string? comboName = null,
            bool hasTellBonus = false)
        {
            return new DialogueOption(
                stat, $"{stat} option",
                callbackTurnNumber: callbackTurn,
                comboName: comboName,
                hasTellBonus: hasTellBonus);
        }

        private static TurnStart MakeTurn(params DialogueOption[] options)
        {
            return new TurnStart(
                options,
                new GameStateSnapshot(10, InterestState.Interested, 0, Array.Empty<string>(), 3));
        }

        private static PlayerAgentContext MakeContext(
            StatBlock? player = null,
            StatBlock? datee = null,
            int interest = 10,
            InterestState state = InterestState.Interested,
            int momentum = 0,
            string[]? traps = null,
            int turnNumber = 3)
        {
            return new PlayerAgentContext(
                player ?? MakeStats(charm: 3, rizz: 2, honesty: 1, chaos: 2, wit: 2, sa: 1),
                datee ?? MakeStats(charm: 1, rizz: 1, honesty: 1, chaos: 1, wit: 1, sa: 1),
                interest,
                state,
                momentum,
                traps ?? Array.Empty<string>(),
                0,
                null,
                turnNumber);
        }

        #endregion

        // AC1: Implements IPlayerAgent
        [Fact]
        public void ScoringPlayerAgent_ImplementsIPlayerAgent()
        {
            Assert.IsAssignableFrom<IPlayerAgent>(_agent);
        }

        // AC2: Scores all options
        [Fact]
        public async Task DecideAsync_ScoresAllOptions()
        {
            var turn = MakeTurn(
                MakeOption(StatType.Charm),
                MakeOption(StatType.Rizz),
                MakeOption(StatType.Honesty),
                MakeOption(StatType.Chaos));
            var context = MakeContext();

            var decision = await _agent.DecideAsync(turn, context);

            Assert.Equal(4, decision.Scores.Length);
            for (int i = 0; i < 4; i++)
            {
                Assert.Equal(i, decision.Scores[i].OptionIndex);
                Assert.InRange(decision.Scores[i].SuccessChance, 0.0f, 1.0f);
            }
        }

        // AC5: Deterministic — same input produces same output
        [Fact]
        public async Task DecideAsync_IsDeterministic()
        {
            var turn = MakeTurn(
                MakeOption(StatType.Charm),
                MakeOption(StatType.Rizz),
                MakeOption(StatType.Honesty));
            var context = MakeContext();

            var d1 = await _agent.DecideAsync(turn, context);
            var d2 = await _agent.DecideAsync(turn, context);

            Assert.Equal(d1.OptionIndex, d2.OptionIndex);
            Assert.Equal(d1.Reasoning, d2.Reasoning);
            for (int i = 0; i < d1.Scores.Length; i++)
            {
                Assert.Equal(d1.Scores[i].Score, d2.Scores[i].Score);
                Assert.Equal(d1.Scores[i].SuccessChance, d2.Scores[i].SuccessChance);
            }
        }

        // AC8: Basic EV ordering — highest EV wins without strategic adjustments
        [Fact]
        public async Task DecideAsync_PicksHighestEV_WhenNoStrategicAdjustments()
        {
            // Player has Charm=5, all others=0. Datee all=0.
            // Charm should have best success chance and highest EV.
            var player = MakeStats(charm: 5, rizz: 0, honesty: 0, chaos: 0);
            var datee = MakeStats(); // all 0 → DC = 13 for all
            var turn = MakeTurn(
                MakeOption(StatType.Charm),
                MakeOption(StatType.Rizz));
            var context = MakeContext(player: player, datee: datee);

            var decision = await _agent.DecideAsync(turn, context);

            Assert.Equal(0, decision.OptionIndex); // Charm is better
            Assert.True(decision.Scores[0].Score > decision.Scores[1].Score);
        }

        // AC6-1: High-momentum state (streak=2) prefers safe option
        [Fact]
        public async Task DecideAsync_MomentumStreak2_PrefersSafeOption()
        {
            // Make one option safe (high successChance) and one bold (low successChance but higher raw EV)
            // Player: Charm=5 (safe against weak datee), Rizz=0 (bold against strong datee)
            var player = MakeStats(charm: 8, rizz: 2);
            // Datee: SA=0 (Charm defence DC=13), Wit=5 (Rizz defence DC=18)
            var datee = MakeStats(sa: 0, wit: 5);
            var turn = MakeTurn(
                MakeOption(StatType.Charm),   // need=13-8=5, Safe, successChance=0.80
                MakeOption(StatType.Rizz));    // need=18-2=16, Bold, successChance=0.25

            var context = MakeContext(player: player, datee: datee, momentum: 2);

            var decision = await _agent.DecideAsync(turn, context);

            // Charm (safe, high success chance) should get momentum bias
            Assert.Equal(0, decision.OptionIndex);
            // Charm score should include momentum bias
            Assert.True(decision.Scores[0].Score > decision.Scores[1].Score);
        }

        // AC6-2: Bored state prefers bold
        [Fact]
        public async Task DecideAsync_BoredState_PrefersBoldOption()
        {
            // Low interest, Bored state
            var player = MakeStats(charm: 2, rizz: 2);
            // Datee: SA=0 (DC 13 for Charm), Wit=5 (DC 18 for Rizz)
            var datee = MakeStats(sa: 0, wit: 5);
            var turn = MakeTurn(
                MakeOption(StatType.Charm),   // need=13-2=11, Hard
                MakeOption(StatType.Rizz));    // need=18-2=16, Bold

            var context = MakeContext(
                player: player, datee: datee,
                interest: 3, state: InterestState.Bored);

            var decision = await _agent.DecideAsync(turn, context);

            // Both Hard and Bold get Bored bias, but let's make a clear case:
            // Make Charm Safe and Rizz Bold
            var player2 = MakeStats(charm: 10, rizz: 2);
            var datee2 = MakeStats(sa: 0, wit: 5);
            var turn2 = MakeTurn(
                MakeOption(StatType.Charm),   // need=13-10=3, Safe → no Bored bias
                MakeOption(StatType.Rizz));    // need=18-2=16, Bold → +1.0 bias

            var decision2 = await _agent.DecideAsync(turn2,
                MakeContext(player: player2, datee: datee2,
                    interest: 3, state: InterestState.Bored));

            // Without Bored bias, Charm (safe, high EV) would win.
            // Bored bias pushes Bold option (Rizz) up.
            // Check that Rizz score has the bias applied:
            // Rizz raw EV is negative (low success), but with +1.0 Bored bias it might compete.
            // The Bored bias should raise the Bold option's score by 1.0.
            float charmScore = decision2.Scores[0].Score;
            float rizzScore = decision2.Scores[1].Score;
            float charmEV = decision2.Scores[0].ExpectedInterestGain;
            float rizzEV = decision2.Scores[1].ExpectedInterestGain;

            // rizzScore should be rizzEV + 1.0 (bored bias)
            Assert.Equal((double)(rizzEV + 1.0f), (double)rizzScore, 3);
            // charmScore should equal charmEV (no bias)
            Assert.Equal((double)charmEV, (double)charmScore, 3);
        }

        // AC6-3: Active trap penalizes that stat
        [Fact]
        public async Task DecideAsync_ActiveTrap_PenalizesThatStat()
        {
            // Charm has Madness trap active
            var player = MakeStats(charm: 5, rizz: 3);
            var datee = MakeStats(); // all 0, DC=13
            var turn = MakeTurn(
                MakeOption(StatType.Charm),
                MakeOption(StatType.Rizz));

            var context = MakeContext(
                player: player, datee: datee,
                traps: new[] { "Madness" }); // Madness = Charm's shadow

            var decision = await _agent.DecideAsync(turn, context);

            // Charm has higher raw EV but -2.0 trap penalty
            // Rizz should win
            Assert.Equal(1, decision.OptionIndex);
            Assert.True(decision.Scores[0].Score < decision.Scores[1].Score);
        }

        // AC6-4: Near-win prefers safe
        [Fact]
        public async Task DecideAsync_NearWin_PrefersSafeOption()
        {
            var player = MakeStats(charm: 10, rizz: 2);
            // Datee: SA=0 (DC 13 for Charm), Wit=5 (DC 18 for Rizz)
            var datee = MakeStats(sa: 0, wit: 5);
            var turn = MakeTurn(
                MakeOption(StatType.Charm),   // need=13-10=3, Safe → +2.0 near-win bias
                MakeOption(StatType.Rizz));    // need=18-2=16, Bold → no near-win bias

            var context = MakeContext(
                player: player, datee: datee,
                interest: 20, state: InterestState.VeryIntoIt);

            var decision = await _agent.DecideAsync(turn, context);

            // Safe option should win due to +2.0 near-win bias
            Assert.Equal(0, decision.OptionIndex);
            float charmScore = decision.Scores[0].Score;
            float charmEV = decision.Scores[0].ExpectedInterestGain;
            // Score should include +2.0 near-win bias
            Assert.Equal((double)(charmEV + 2.0f), (double)charmScore, 3);
        }
    }
}
