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
    public class ScoringPlayerAgentTests
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
            StatBlock? opponent = null,
            int interest = 10,
            InterestState state = InterestState.Interested,
            int momentum = 0,
            string[]? traps = null,
            int turnNumber = 3)
        {
            return new PlayerAgentContext(
                player ?? MakeStats(charm: 3, rizz: 2, honesty: 1, chaos: 2, wit: 2, sa: 1),
                opponent ?? MakeStats(charm: 1, rizz: 1, honesty: 1, chaos: 1, wit: 1, sa: 1),
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
            // Player has Charm=5, all others=0. Opponent all=0.
            // Charm should have best success chance and highest EV.
            var player = MakeStats(charm: 5, rizz: 0, honesty: 0, chaos: 0);
            var opponent = MakeStats(); // all 0 → DC = 13 for all
            var turn = MakeTurn(
                MakeOption(StatType.Charm),
                MakeOption(StatType.Rizz));
            var context = MakeContext(player: player, opponent: opponent);

            var decision = await _agent.DecideAsync(turn, context);

            Assert.Equal(0, decision.OptionIndex); // Charm is better
            Assert.True(decision.Scores[0].Score > decision.Scores[1].Score);
        }

        // AC6-1: High-momentum state (streak=2) prefers safe option
        [Fact]
        public async Task DecideAsync_MomentumStreak2_PrefersSafeOption()
        {
            // Make one option safe (high successChance) and one bold (low successChance but higher raw EV)
            // Player: Charm=5 (safe against weak opponent), Rizz=0 (bold against strong opponent)
            var player = MakeStats(charm: 8, rizz: 2);
            // Opponent: SA=0 (Charm defence DC=13), Wit=5 (Rizz defence DC=18)
            var opponent = MakeStats(sa: 0, wit: 5);
            var turn = MakeTurn(
                MakeOption(StatType.Charm),   // need=13-8=5, Safe, successChance=0.80
                MakeOption(StatType.Rizz));    // need=18-2=16, Bold, successChance=0.25

            var context = MakeContext(player: player, opponent: opponent, momentum: 2);

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
            // Opponent: SA=0 (DC 13 for Charm), Wit=5 (DC 18 for Rizz)
            var opponent = MakeStats(sa: 0, wit: 5);
            var turn = MakeTurn(
                MakeOption(StatType.Charm),   // need=13-2=11, Hard
                MakeOption(StatType.Rizz));    // need=18-2=16, Bold

            var context = MakeContext(
                player: player, opponent: opponent,
                interest: 3, state: InterestState.Bored);

            var decision = await _agent.DecideAsync(turn, context);

            // Both Hard and Bold get Bored bias, but let's make a clear case:
            // Make Charm Safe and Rizz Bold
            var player2 = MakeStats(charm: 10, rizz: 2);
            var opponent2 = MakeStats(sa: 0, wit: 5);
            var turn2 = MakeTurn(
                MakeOption(StatType.Charm),   // need=13-10=3, Safe → no Bored bias
                MakeOption(StatType.Rizz));    // need=18-2=16, Bold → +1.0 bias

            var decision2 = await _agent.DecideAsync(turn2,
                MakeContext(player: player2, opponent: opponent2,
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
            var opponent = MakeStats(); // all 0, DC=13
            var turn = MakeTurn(
                MakeOption(StatType.Charm),
                MakeOption(StatType.Rizz));

            var context = MakeContext(
                player: player, opponent: opponent,
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
            // Opponent: SA=0 (DC 13 for Charm), Wit=5 (DC 18 for Rizz)
            var opponent = MakeStats(sa: 0, wit: 5);
            var turn = MakeTurn(
                MakeOption(StatType.Charm),   // need=13-10=3, Safe → +2.0 near-win bias
                MakeOption(StatType.Rizz));    // need=18-2=16, Bold → no near-win bias

            var context = MakeContext(
                player: player, opponent: opponent,
                interest: 20, state: InterestState.VeryIntoIt);

            var decision = await _agent.DecideAsync(turn, context);

            // Safe option should win due to +2.0 near-win bias
            Assert.Equal(0, decision.OptionIndex);
            float charmScore = decision.Scores[0].Score;
            float charmEV = decision.Scores[0].ExpectedInterestGain;
            // Score should include +2.0 near-win bias
            Assert.Equal((double)(charmEV + 2.0f), (double)charmScore, 3);
        }

        // AC6-5: Tell bonus factored into need
        [Fact]
        public async Task DecideAsync_TellBonus_LowersNeedBy2()
        {
            var player = MakeStats(charm: 3);
            var opponent = MakeStats(); // DC=13 for Charm
            // Without tell: need = 13 - 3 = 10, successChance = 11/20 = 0.55
            // With tell: need = 13 - (3+2) = 8, successChance = 13/20 = 0.65
            var optionNoTell = MakeOption(StatType.Charm, hasTellBonus: false);
            var optionWithTell = MakeOption(StatType.Charm, hasTellBonus: true);

            var turnNoTell = MakeTurn(optionNoTell);
            var turnWithTell = MakeTurn(optionWithTell);
            var context = MakeContext(player: player, opponent: opponent);

            var dNoTell = await _agent.DecideAsync(turnNoTell, context);
            var dWithTell = await _agent.DecideAsync(turnWithTell, context);

            Assert.True(dWithTell.Scores[0].SuccessChance > dNoTell.Scores[0].SuccessChance);
            // Tell lowers need by 2, so successChance increases by 2/20 = 0.10
            Assert.Equal((double)(dNoTell.Scores[0].SuccessChance + 0.10f),
                         (double)dWithTell.Scores[0].SuccessChance, 3);
        }

        // AC6-6: Combo bonus adds interest on success
        [Fact]
        public async Task DecideAsync_ComboBonus_IncreasesExpectedGain()
        {
            var player = MakeStats(charm: 3);
            var opponent = MakeStats();
            var optionNoCombo = MakeOption(StatType.Charm);
            var optionWithCombo = MakeOption(StatType.Charm, comboName: "The Switcheroo");

            var turnNoCombo = MakeTurn(optionNoCombo);
            var turnWithCombo = MakeTurn(optionWithCombo);
            var context = MakeContext(player: player, opponent: opponent);

            var dNoCombo = await _agent.DecideAsync(turnNoCombo, context);
            var dWithCombo = await _agent.DecideAsync(turnWithCombo, context);

            // Combo adds +1 to expected gain on success, weighted by successChance
            Assert.True(dWithCombo.Scores[0].ExpectedInterestGain >
                        dNoCombo.Scores[0].ExpectedInterestGain);
        }

        // AC6-7: Callback bonus lowers need
        [Fact]
        public async Task DecideAsync_CallbackBonus_LowersNeed()
        {
            var player = MakeStats(charm: 3);
            var opponent = MakeStats();
            // Turn 5, callback to turn 0 (opener) → CallbackBonus.Compute(5, 0) = 3
            var optionWithCallback = MakeOption(StatType.Charm, callbackTurn: 0);
            var optionNoCallback = MakeOption(StatType.Charm);

            var turnCallback = MakeTurn(optionWithCallback);
            var turnNoCallback = MakeTurn(optionNoCallback);
            var context = MakeContext(player: player, opponent: opponent, turnNumber: 5);

            var dCallback = await _agent.DecideAsync(turnCallback, context);
            var dNoCallback = await _agent.DecideAsync(turnNoCallback, context);

            // Callback bonus = 3 → need drops by 3 → successChance increases by 3/20 = 0.15
            Assert.True(dCallback.Scores[0].SuccessChance > dNoCallback.Scores[0].SuccessChance);
            Assert.Equal((double)(dNoCallback.Scores[0].SuccessChance + 0.15f),
                         (double)dCallback.Scores[0].SuccessChance, 3);
        }

        // Null argument tests
        [Fact]
        public async Task DecideAsync_NullTurn_Throws()
        {
            await Assert.ThrowsAsync<ArgumentNullException>(
                () => _agent.DecideAsync(null!, MakeContext()));
        }

        [Fact]
        public async Task DecideAsync_NullContext_Throws()
        {
            var turn = MakeTurn(MakeOption(StatType.Charm));
            await Assert.ThrowsAsync<ArgumentNullException>(
                () => _agent.DecideAsync(turn, null!));
        }

        // Single option
        [Fact]
        public async Task DecideAsync_SingleOption_ReturnsIt()
        {
            var turn = MakeTurn(MakeOption(StatType.Charm));
            var context = MakeContext();

            var decision = await _agent.DecideAsync(turn, context);

            Assert.Equal(0, decision.OptionIndex);
            Assert.Single(decision.Scores);
        }

        // Reasoning is non-empty
        [Fact]
        public async Task DecideAsync_ReasoningIsNotEmpty()
        {
            var turn = MakeTurn(
                MakeOption(StatType.Charm),
                MakeOption(StatType.Rizz));
            var context = MakeContext();

            var decision = await _agent.DecideAsync(turn, context);

            Assert.False(string.IsNullOrWhiteSpace(decision.Reasoning));
        }

        // BonusesApplied populated correctly
        [Fact]
        public async Task DecideAsync_BonusesApplied_IncludesTellAndCombo()
        {
            var player = MakeStats(charm: 3);
            var opponent = MakeStats();
            var turn = MakeTurn(
                MakeOption(StatType.Charm, hasTellBonus: true, comboName: "TestCombo", callbackTurn: 0));
            var context = MakeContext(player: player, opponent: opponent, turnNumber: 5);

            var decision = await _agent.DecideAsync(turn, context);

            var bonuses = decision.Scores[0].BonusesApplied;
            Assert.Contains("tell +2", bonuses);
            Assert.Contains("combo: TestCombo", bonuses);
            Assert.Contains("callback +3", bonuses);
        }

        // Momentum bonus in bonuses applied
        [Fact]
        public async Task DecideAsync_MomentumBonus_InBonusesApplied()
        {
            var turn = MakeTurn(MakeOption(StatType.Charm));
            var context = MakeContext(momentum: 3);

            var decision = await _agent.DecideAsync(turn, context);

            Assert.Contains("momentum +2", decision.Scores[0].BonusesApplied);
        }

        // Tie-breaking: first option wins on equal score
        [Fact]
        public async Task DecideAsync_TiedScores_PicksFirstOption()
        {
            // Two identical options should produce identical scores, first wins
            var player = MakeStats(charm: 3, rizz: 3);
            // Need same DC for both — opponent SA = opponent Wit
            var opponent = MakeStats(sa: 2, wit: 2);
            var turn = MakeTurn(
                MakeOption(StatType.Charm),
                MakeOption(StatType.Rizz));
            var context = MakeContext(player: player, opponent: opponent);

            var decision = await _agent.DecideAsync(turn, context);

            // Scores should be equal
            Assert.Equal((double)decision.Scores[0].Score, (double)decision.Scores[1].Score, 5);
            // First option wins tie
            Assert.Equal(0, decision.OptionIndex);
        }

        // ================================================================
        // Issue #386: Verify ScoringPlayerAgent uses engine constants correctly
        // These tests guard against silent drift between the agent and the engine.
        // ================================================================

        [Fact]
        public async Task CallbackBonus_UsesEngineMethod_OpenerReturns3()
        {
            // ScoringPlayerAgent must call CallbackBonus.Compute() directly.
            // Verify opener callback (turn 0, current turn 5) yields +3 by checking
            // that the agent's bonus matches CallbackBonus.Compute(5, 0).
            int engineBonus = CallbackBonus.Compute(5, 0);
            Assert.Equal(3, engineBonus);

            var optionWithOpenerCallback = MakeOption(StatType.Charm, callbackTurn: 0);
            var optionPlain = MakeOption(StatType.Charm);
            var player = MakeStats(charm: 3);
            var opponent = MakeStats(sa: 2);

            var turnCb = MakeTurn(optionWithOpenerCallback);
            var turnPlain = MakeTurn(optionPlain);
            var context = MakeContext(player: player, opponent: opponent, turnNumber: 5);

            var decisionCb = await _agent.DecideAsync(turnCb, context);
            var decisionPlain = await _agent.DecideAsync(turnPlain, context);

            // Opener callback should raise success chance (lower need)
            Assert.True(decisionCb.Scores[0].SuccessChance > decisionPlain.Scores[0].SuccessChance,
                "Opener callback (+3) should increase success chance vs no callback");
        }

        [Fact]
        public async Task CallbackBonus_MidDistance_MatchesEngine()
        {
            // Mid-distance callback (gap 2-3, non-opener) → engine returns 1
            int engineBonus = CallbackBonus.Compute(5, 3);
            Assert.Equal(1, engineBonus);

            var option = MakeOption(StatType.Charm, callbackTurn: 3);
            var player = MakeStats(charm: 3);
            var opponent = MakeStats(sa: 2);
            var turn = MakeTurn(option);
            var context = MakeContext(player: player, opponent: opponent, turnNumber: 5);

            var decision = await _agent.DecideAsync(turn, context);
            Assert.Contains(decision.Scores[0].BonusesApplied,
                b => b.Contains("callback", StringComparison.OrdinalIgnoreCase));
        }

        // What: Momentum bonus thresholds match GameSession rules (§15)
        // Mutation: Would catch if agent used wrong streak thresholds (e.g. >=4 instead of >=3)
        [Theory]
        [InlineData(0, null)]
        [InlineData(1, null)]
        [InlineData(2, null)]
        [InlineData(3, "momentum +2")]
        [InlineData(4, "momentum +2")]
        [InlineData(5, "momentum +3")]
        [InlineData(10, "momentum +3")]
        public async Task MomentumBonus_MatchesGameSessionThresholds(int streak, string? expectedBonusLabel)
        {
            // Verify the agent's momentum bonus at each threshold by calling DecideAsync
            // and inspecting BonusesApplied — exercises real production scoring code.
            var turn = MakeTurn(MakeOption(StatType.Charm));
            var context = MakeContext(momentum: streak);

            var decision = await _agent.DecideAsync(turn, context);

            if (expectedBonusLabel == null)
            {
                // No momentum bonus should appear
                Assert.DoesNotContain(decision.Scores[0].BonusesApplied,
                    b => b.Contains("momentum", StringComparison.OrdinalIgnoreCase));
            }
            else
            {
                Assert.Contains(expectedBonusLabel, decision.Scores[0].BonusesApplied);
            }
        }

        [Fact]
        public async Task TellBonus_Hardcoded2_MatchesEngine()
        {
            // SYNC: GameSession ResolveTurnAsync tellBonus = 2.
            // Verify that tell bonus is applied as exactly +2 to need calculation.
            var optionWithTell = MakeOption(StatType.Charm, hasTellBonus: true);
            var optionPlain = MakeOption(StatType.Charm);
            var player = MakeStats(charm: 3);
            var opponent = MakeStats(sa: 5);
            var turnTell = MakeTurn(optionWithTell);
            var turnPlain = MakeTurn(optionPlain);
            var context = MakeContext(player: player, opponent: opponent);

            var decisionTell = await _agent.DecideAsync(turnTell, context);
            var decisionPlain = await _agent.DecideAsync(turnPlain, context);

            // Tell bonus (+2) should raise success chance (lower need by 2)
            Assert.True(decisionTell.Scores[0].SuccessChance > decisionPlain.Scores[0].SuccessChance,
                "Tell bonus (+2) should increase success chance");

            // Verify the delta corresponds to exactly +2 on a d20
            // successChance = (21 - need) / 20; +2 to mod means need drops by 2 → chance increases by 2/20 = 0.1
            float delta = decisionTell.Scores[0].SuccessChance - decisionPlain.Scores[0].SuccessChance;
            Assert.True(Math.Abs(delta - 0.1f) < 0.001f,
                $"Tell bonus should shift success chance by exactly 0.1 (2/20), got {delta}");
        }
    }
}
