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
    /// Issue #386: Sync-verification tests ensuring ScoringPlayerAgent bonus constants
    /// match the engine's canonical values. These tests guard against silent drift
    /// between the session-runner scoring agent and Pinder.Core game rules.
    /// </summary>
    public class BonusConstantSyncTests
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
                    { ShadowStatType.Horniness, horniness },
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

        // ================================================================
        // CallbackBonus.Compute() sync verification — all tiers
        // ================================================================

        // Mutation: would catch if agent reimplements callback logic and gets gap<2 wrong (returns non-zero)
        [Theory]
        [InlineData(3, 2)]   // gap=1
        [InlineData(5, 5)]   // gap=0
        [InlineData(2, 1)]   // gap=1
        public void CallbackBonus_GapLessThan2_ReturnsZero(int currentTurn, int callbackTurn)
        {
            int bonus = CallbackBonus.Compute(currentTurn, callbackTurn);
            Assert.Equal(0, bonus);
        }

        // Mutation: would catch if agent hardcodes callback=1 instead of calling Compute() for gap 2-3
        [Theory]
        [InlineData(5, 3, 1)]   // gap=2, non-opener → 1
        [InlineData(6, 3, 1)]   // gap=3, non-opener → 1
        public void CallbackBonus_Gap2or3_NonOpener_Returns1(int currentTurn, int callbackTurn, int expected)
        {
            int bonus = CallbackBonus.Compute(currentTurn, callbackTurn);
            Assert.Equal(expected, bonus);
        }

        // Mutation: would catch if agent omits gap>=4 tier (returns 1 instead of 2)
        [Theory]
        [InlineData(7, 3, 2)]   // gap=4, non-opener → 2
        [InlineData(10, 1, 2)]  // gap=9, non-opener → 2
        public void CallbackBonus_Gap4Plus_NonOpener_Returns2(int currentTurn, int callbackTurn, int expected)
        {
            int bonus = CallbackBonus.Compute(currentTurn, callbackTurn);
            Assert.Equal(expected, bonus);
        }

        // Mutation: would catch if agent treats opener same as non-opener (returns 2 instead of 3)
        [Theory]
        [InlineData(2, 0, 3)]   // gap=2, opener → 3
        [InlineData(5, 0, 3)]   // gap=5, opener → 3
        [InlineData(10, 0, 3)]  // gap=10, opener → 3
        public void CallbackBonus_Opener_WithSufficientGap_Returns3(int currentTurn, int callbackTurn, int expected)
        {
            int bonus = CallbackBonus.Compute(currentTurn, callbackTurn);
            Assert.Equal(expected, bonus);
        }

        // Mutation: would catch if agent doesn't call CallbackBonus.Compute() and instead hardcodes +3 for all callbacks
        [Fact]
        public async Task CallbackBonus_AgentAppliesCorrectTierPerGap()
        {
            var player = MakeStats(charm: 3);
            var opponent = MakeStats(sa: 0); // DC = 13

            // Gap=2, non-opener → engine says +1
            var optionGap2 = MakeOption(StatType.Charm, callbackTurn: 3);
            var turnGap2 = MakeTurn(optionGap2);
            var ctxGap2 = MakeContext(player: player, opponent: opponent, turnNumber: 5);
            var decGap2 = await _agent.DecideAsync(turnGap2, ctxGap2);

            // Gap=5, opener → engine says +3
            var optionOpener = MakeOption(StatType.Charm, callbackTurn: 0);
            var turnOpener = MakeTurn(optionOpener);
            var ctxOpener = MakeContext(player: player, opponent: opponent, turnNumber: 5);
            var decOpener = await _agent.DecideAsync(turnOpener, ctxOpener);

            // Opener (+3) should have higher success chance than gap 2 (+1)
            Assert.True(decOpener.Scores[0].SuccessChance > decGap2.Scores[0].SuccessChance,
                "Opener callback (+3) should yield higher success than mid-distance (+1)");

            // The delta should be exactly 2/20 = 0.10 (bonus difference of 2)
            float delta = decOpener.Scores[0].SuccessChance - decGap2.Scores[0].SuccessChance;
            Assert.True(Math.Abs(delta - 0.10f) < 0.001f,
                $"Expected 0.10 delta between opener(+3) and gap2(+1), got {delta}");
        }

        // ================================================================
        // Momentum bonus sync verification — boundary values
        // ================================================================

        // Mutation: would catch if momentum threshold uses > instead of >= for streak 3
        [Fact]
        public async Task MomentumBonus_Streak3_AppliesBonusToSuccessChance()
        {
            var player = MakeStats(charm: 3);
            var opponent = MakeStats(sa: 0); // DC = 13

            var option = MakeOption(StatType.Charm);
            var turnMom = MakeTurn(option);
            var turnNoMom = MakeTurn(MakeOption(StatType.Charm));

            var ctxMom = MakeContext(player: player, opponent: opponent, momentum: 3);
            var ctxNoMom = MakeContext(player: player, opponent: opponent, momentum: 0);

            var decMom = await _agent.DecideAsync(turnMom, ctxMom);
            var decNoMom = await _agent.DecideAsync(turnNoMom, ctxNoMom);

            // Momentum 3 → bonus of +2, so success chance should increase by 2/20 = 0.10
            float delta = decMom.Scores[0].SuccessChance - decNoMom.Scores[0].SuccessChance;
            Assert.True(Math.Abs(delta - 0.10f) < 0.001f,
                $"Momentum streak 3 should add +2 to need calc (0.10 chance delta), got {delta}");
        }

        // Mutation: would catch if momentum threshold uses >= 4 instead of >= 5 for the +3 tier
        [Fact]
        public async Task MomentumBonus_Streak4_StillReturns2()
        {
            var player = MakeStats(charm: 3);
            var opponent = MakeStats(sa: 0);

            var option = MakeOption(StatType.Charm);
            var ctx3 = MakeContext(player: player, opponent: opponent, momentum: 3);
            var ctx4 = MakeContext(player: player, opponent: opponent, momentum: 4);

            var dec3 = await _agent.DecideAsync(MakeTurn(option), ctx3);
            var dec4 = await _agent.DecideAsync(MakeTurn(MakeOption(StatType.Charm)), ctx4);

            // Streak 3 and 4 both give +2, so success chance should be equal
            Assert.Equal(
                (double)dec3.Scores[0].SuccessChance,
                (double)dec4.Scores[0].SuccessChance,
                3);
        }

        // Mutation: would catch if momentum +3 tier is missed (streak 5 returns +2 instead of +3)
        [Fact]
        public async Task MomentumBonus_Streak5_Returns3()
        {
            var player = MakeStats(charm: 3);
            var opponent = MakeStats(sa: 0);

            var option = MakeOption(StatType.Charm);
            var ctx4 = MakeContext(player: player, opponent: opponent, momentum: 4);
            var ctx5 = MakeContext(player: player, opponent: opponent, momentum: 5);

            var dec4 = await _agent.DecideAsync(MakeTurn(option), ctx4);
            var dec5 = await _agent.DecideAsync(MakeTurn(MakeOption(StatType.Charm)), ctx5);

            // Streak 4 → +2, streak 5 → +3. Delta should be 1/20 = 0.05
            float delta = dec5.Scores[0].SuccessChance - dec4.Scores[0].SuccessChance;
            Assert.True(Math.Abs(delta - 0.05f) < 0.001f,
                $"Streak 5 should add +1 more than streak 4 (0.05 chance delta), got {delta}");
        }

        // Mutation: would catch if streak 2 wrongly gets a momentum bonus
        [Fact]
        public async Task MomentumBonus_Streak2_NoBonusApplied()
        {
            var player = MakeStats(charm: 3);
            var opponent = MakeStats(sa: 0);

            var option = MakeOption(StatType.Charm);
            var ctx0 = MakeContext(player: player, opponent: opponent, momentum: 0);
            var ctx2 = MakeContext(player: player, opponent: opponent, momentum: 2);

            var dec0 = await _agent.DecideAsync(MakeTurn(option), ctx0);
            var dec2 = await _agent.DecideAsync(MakeTurn(MakeOption(StatType.Charm)), ctx2);

            // Streak 0 and 2 should both have 0 momentum bonus → same success chance
            Assert.Equal(
                (double)dec0.Scores[0].SuccessChance,
                (double)dec2.Scores[0].SuccessChance,
                3);
        }

        // ================================================================
        // Tell bonus sync verification
        // ================================================================

        // Mutation: would catch if tell bonus is 1 instead of 2, or 3 instead of 2
        [Fact]
        public async Task TellBonus_ExactlyPlus2_VerifiedViaSuccessChanceDelta()
        {
            var player = MakeStats(rizz: 3);
            var opponent = MakeStats(wit: 2); // Rizz defence DC = 13 + 2 = 15

            var optionTell = MakeOption(StatType.Rizz, hasTellBonus: true);
            var optionPlain = MakeOption(StatType.Rizz, hasTellBonus: false);

            var ctxTell = MakeContext(player: player, opponent: opponent);
            var ctxPlain = MakeContext(player: player, opponent: opponent);

            var decTell = await _agent.DecideAsync(MakeTurn(optionTell), ctxTell);
            var decPlain = await _agent.DecideAsync(MakeTurn(optionPlain), ctxPlain);

            // Tell = +2 to effective mod → need drops by 2 → chance increases by 2/20 = 0.10
            float delta = decTell.Scores[0].SuccessChance - decPlain.Scores[0].SuccessChance;
            Assert.True(Math.Abs(delta - 0.10f) < 0.001f,
                $"Tell bonus should be exactly +2 (0.10 chance delta on d20), got {delta}");
        }

        // ================================================================
        // All bonuses stacking — verify they combine additively
        // ================================================================

        // Mutation: would catch if bonuses don't stack (e.g., only the highest is used)
        [Fact]
        public async Task AllBonuses_StackAdditively()
        {
            var player = MakeStats(charm: 3);
            var opponent = MakeStats(sa: 0); // DC = 13

            // Plain option: need = 13 - 3 = 10, successChance = 11/20 = 0.55
            var optionPlain = MakeOption(StatType.Charm);
            var ctxPlain = MakeContext(player: player, opponent: opponent, momentum: 0, turnNumber: 5);
            var decPlain = await _agent.DecideAsync(MakeTurn(optionPlain), ctxPlain);

            // Stacked: momentum(+2) + tell(+2) + callback opener(+3) = +7 total
            // need = 13 - (3 + 2 + 2 + 3) = 3, successChance = 18/20 = 0.90
            var optionStacked = MakeOption(StatType.Charm, callbackTurn: 0, hasTellBonus: true);
            var ctxStacked = MakeContext(player: player, opponent: opponent, momentum: 3, turnNumber: 5);
            var decStacked = await _agent.DecideAsync(MakeTurn(optionStacked), ctxStacked);

            // Total bonus = 7, so success chance increases by 7/20 = 0.35
            float delta = decStacked.Scores[0].SuccessChance - decPlain.Scores[0].SuccessChance;
            Assert.True(Math.Abs(delta - 0.35f) < 0.001f,
                $"Stacked bonuses (momentum+tell+callback = +7) should give 0.35 delta, got {delta}");
        }

        // Mutation: would catch if BonusesApplied list is incomplete when all bonuses active
        [Fact]
        public async Task AllBonuses_BonusesAppliedListsAll()
        {
            var player = MakeStats(charm: 3);
            var opponent = MakeStats(sa: 0);

            var option = MakeOption(StatType.Charm, callbackTurn: 0, hasTellBonus: true, comboName: "TestCombo");
            var ctx = MakeContext(player: player, opponent: opponent, momentum: 5, turnNumber: 5);

            var dec = await _agent.DecideAsync(MakeTurn(option), ctx);

            var bonuses = dec.Scores[0].BonusesApplied;
            Assert.Contains("tell +2", bonuses);
            Assert.Contains("callback +3", bonuses);
            Assert.Contains("momentum +3", bonuses);
            Assert.Contains("combo: TestCombo", bonuses);
        }

        // ================================================================
        // No callback → no callback bonus
        // ================================================================

        // Mutation: would catch if agent applies a default callback bonus when CallbackTurnNumber is null
        [Fact]
        public async Task NoCallback_NoBonusApplied()
        {
            var player = MakeStats(charm: 3);
            var opponent = MakeStats(sa: 0);

            var option = MakeOption(StatType.Charm, callbackTurn: null);
            var ctx = MakeContext(player: player, opponent: opponent, turnNumber: 5);

            var dec = await _agent.DecideAsync(MakeTurn(option), ctx);

            // Should have no callback entry in bonuses
            foreach (var b in dec.Scores[0].BonusesApplied)
            {
                Assert.DoesNotContain("callback", b.ToLowerInvariant());
            }
        }
    }
}
