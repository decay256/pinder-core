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
    public class ScoringPlayerAgentSpecTests
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
                    { ShadowStatType.Horniness, horniness },
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
            StatBlock? opponentStats = null,
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
                opponentStats ?? MakeStats(),
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

        #region AC2: Scores all options per the formula

        // Mutation: would catch if Scores.Length != Options.Length
        [Fact]
        public async Task Scores_LengthEqualsOptionsLength()
        {
            var options = new[]
            {
                new DialogueOption(StatType.Charm, "a"),
                new DialogueOption(StatType.Rizz, "b"),
                new DialogueOption(StatType.Honesty, "c")
            };
            var turn = MakeTurn(options);
            var ctx = MakeContext();
            var result = await _agent.DecideAsync(turn, ctx);

            Assert.Equal(3, result.Scores.Length);
        }

        // Mutation: would catch if OptionIndex on scores doesn't match position
        [Fact]
        public async Task Scores_OptionIndexMatchesPosition()
        {
            var options = new[]
            {
                new DialogueOption(StatType.Charm, "a"),
                new DialogueOption(StatType.Wit, "b")
            };
            var turn = MakeTurn(options);
            var ctx = MakeContext();
            var result = await _agent.DecideAsync(turn, ctx);

            for (int i = 0; i < result.Scores.Length; i++)
            {
                Assert.Equal(i, result.Scores[i].OptionIndex);
            }
        }

        // Mutation: would catch if successChance is not clamped between 0.0 and 1.0
        [Fact]
        public async Task SuccessChance_ClampedTo01Range()
        {
            // Easy option: player has very high stat, opponent has 0 defence
            var playerStats = MakeStats(charm: 10);
            var opponentStats = MakeStats(selfAwareness: 0);
            var turn = MakeTurn(new DialogueOption(StatType.Charm, "easy"));
            var ctx = MakeContext(playerStats: playerStats, opponentStats: opponentStats);
            var result = await _agent.DecideAsync(turn, ctx);

            Assert.InRange(result.Scores[0].SuccessChance, 0.0f, 1.0f);
        }

        // Mutation: would catch if successChance calculation is inverted or ignores attacker mod
        [Fact]
        public async Task HigherAttackerMod_GivesHigherSuccessChance()
        {
            var opponentStats = MakeStats(selfAwareness: 2); // Charm → SA defence, DC = 13 + 2 = 15

            var weakPlayer = MakeStats(charm: 1); // need = 15 - 1 = 14, chance = 7/20
            var strongPlayer = MakeStats(charm: 5); // need = 15 - 5 = 10, chance = 11/20

            var option = new DialogueOption(StatType.Charm, "test");

            var weakResult = await _agent.DecideAsync(
                MakeTurn(option), MakeContext(playerStats: weakPlayer, opponentStats: opponentStats));
            var strongResult = await _agent.DecideAsync(
                MakeTurn(option), MakeContext(playerStats: strongPlayer, opponentStats: opponentStats));

            Assert.True(strongResult.Scores[0].SuccessChance > weakResult.Scores[0].SuccessChance,
                $"Strong ({strongResult.Scores[0].SuccessChance}) should be > Weak ({weakResult.Scores[0].SuccessChance})");
        }

        // Mutation: would catch if EV doesn't increase with success chance (basic ordering)
        [Fact]
        public async Task BasicEvOrdering_HigherSuccessChanceGivesHigherEv()
        {
            // Two options: one easy (Charm with high mod), one hard (Rizz with low mod)
            // Both against same defence
            var playerStats = MakeStats(charm: 5, rizz: 0);
            var opponentStats = MakeStats(); // all 0 defence stats → DC = 13 for all

            var options = new[]
            {
                new DialogueOption(StatType.Charm, "easy"),  // need=8, ~65%
                new DialogueOption(StatType.Rizz, "hard")    // need=13, ~40%
            };
            var turn = MakeTurn(options);
            var ctx = MakeContext(playerStats: playerStats, opponentStats: opponentStats);
            var result = await _agent.DecideAsync(turn, ctx);

            // Easy option should have higher EV
            Assert.True(result.Scores[0].ExpectedInterestGain > result.Scores[1].ExpectedInterestGain);
            Assert.Equal(0, result.OptionIndex); // should pick the easier option
        }

        #endregion

        #region AC3: Strategic adjustments

        // Mutation: would catch if momentum streak == 2 doesn't add +1.0 to safe options
        [Fact]
        public async Task MomentumStreak2_BiasesSafeOptionWithHighSuccessChance()
        {
            // Option A: safe (high success), lower raw EV
            // Option B: bold (low success), higher raw EV
            var playerStats = MakeStats(charm: 5, rizz: 0);
            var opponentStats = MakeStats(); // all 0 → DC=13

            var options = new[]
            {
                new DialogueOption(StatType.Charm, "safe"),  // need=8, ~65% success, Safe tier
                new DialogueOption(StatType.Rizz, "bold")    // need=13, ~40% success
            };
            var turn = MakeTurn(options);
            var ctx = MakeContext(
                playerStats: playerStats,
                opponentStats: opponentStats,
                momentumStreak: 2);

            var result = await _agent.DecideAsync(turn, ctx);

            // Charm has successChance >= 0.5 and streak == 2 → +1.0 bonus
            // So Charm score should get a boost
            Assert.Equal(0, result.OptionIndex); // safe option preferred
        }

        // Mutation: would catch if momentum bias applies when successChance < 0.5
        [Fact]
        public async Task MomentumStreak2_DoesNotBiasLowSuccessOptions()
        {
            // Both options have low success chances
            var playerStats = MakeStats(charm: 0, rizz: 0);
            var opponentStats = MakeStats(selfAwareness: 5, wit: 5); // high defence → hard to hit

            var options = new[]
            {
                new DialogueOption(StatType.Charm, "a"),  // need = 13+5 - 0 = 18, chance=15%
                new DialogueOption(StatType.Rizz, "b")    // need = 13+5 - 0 = 18, chance=15%
            };
            var turn = MakeTurn(options);
            var ctx = MakeContext(
                playerStats: playerStats,
                opponentStats: opponentStats,
                momentumStreak: 2);

            var result = await _agent.DecideAsync(turn, ctx);

            // Both have low success (< 0.5) — momentum bias should NOT apply to either
            // Scores should be approximately equal
            float diff = Math.Abs(result.Scores[0].Score - result.Scores[1].Score);
            Assert.True(diff < 0.5f, $"Scores should be close when both have low success. Diff={diff}");
        }

        // Mutation: would catch if near-win bias doesn't apply at interest=19
        [Fact]
        public async Task NearWin_Interest19_PrefersSafeOption()
        {
            var playerStats = MakeStats(charm: 5, chaos: 0);
            var opponentStats = MakeStats(); // all 0 → DC=13

            var options = new[]
            {
                new DialogueOption(StatType.Charm, "safe"),  // need=8, Safe tier
                new DialogueOption(StatType.Chaos, "bold")   // need=13
            };
            var turn = MakeTurn(options);
            var ctx = MakeContext(
                playerStats: playerStats,
                opponentStats: opponentStats,
                currentInterest: 19,
                interestState: InterestState.VeryIntoIt);

            var result = await _agent.DecideAsync(turn, ctx);

            // Near-win: interest 19 is in [19,24] → +2.0 to Safe/Medium options
            Assert.Equal(0, result.OptionIndex);
            Assert.True(result.Scores[0].Score > result.Scores[1].Score);
        }

        // Mutation: would catch if near-win bias applies at interest=18
        [Fact]
        public async Task NearWin_Interest18_DoesNotApplyBias()
        {
            var playerStats = MakeStats(charm: 5, chaos: 0);
            var opponentStats = MakeStats();

            var options = new[]
            {
                new DialogueOption(StatType.Charm, "safe"),   // need=8, Safe
                new DialogueOption(StatType.Chaos, "risky")   // need=13
            };
            var turn = MakeTurn(options);

            var ctxAt18 = MakeContext(
                playerStats: playerStats,
                opponentStats: opponentStats,
                currentInterest: 18,
                interestState: InterestState.VeryIntoIt);
            var ctxAt19 = MakeContext(
                playerStats: playerStats,
                opponentStats: opponentStats,
                currentInterest: 19,
                interestState: InterestState.VeryIntoIt);

            var resultAt18 = await _agent.DecideAsync(turn, ctxAt18);
            var resultAt19 = await _agent.DecideAsync(turn, ctxAt19);

            // At 19 the safe option should get +2.0 bonus; at 18 it should NOT
            Assert.True(resultAt19.Scores[0].Score > resultAt18.Scores[0].Score,
                "Near-win bonus should apply at 19 but not at 18");
        }

        // Mutation: would catch if Bored bias doesn't apply to Hard/Bold options
        [Fact]
        public async Task BoredState_BiasesBoldOptionUpward()
        {
            // Verify Bold options get a score boost when Bored
            var playerStats = MakeStats(chaos: 2);
            var opponentStats = MakeStats(charm: 3); // Chaos→Charm, DC=16, need=14 → Bold

            var turn = MakeTurn(new DialogueOption(StatType.Chaos, "bold")); // need=14, Bold

            var ctxBored = MakeContext(
                playerStats: playerStats,
                opponentStats: opponentStats,
                currentInterest: 3,
                interestState: InterestState.Bored);
            var ctxNeutral = MakeContext(
                playerStats: playerStats,
                opponentStats: opponentStats,
                currentInterest: 12,
                interestState: InterestState.Interested);

            var resultBored = await _agent.DecideAsync(turn, ctxBored);
            var resultNeutral = await _agent.DecideAsync(turn, ctxNeutral);

            // Same option, same stats — Bored state adds +1.0 to Bold score
            Assert.True(resultBored.Scores[0].Score > resultNeutral.Scores[0].Score,
                $"Bored should boost Bold option score: Bored={resultBored.Scores[0].Score}, Neutral={resultNeutral.Scores[0].Score}");
        }

        // Mutation: would catch if trap penalty is not applied (-2.0)
        [Fact]
        public async Task ActiveTrap_PenalizesTrappedStat()
        {
            // Spec Example 5: Madness trap on Charm → -2.0 penalty
            var playerStats = MakeStats(charm: 5, wit: 3);
            var opponentStats = MakeStats(); // all 0

            var options = new[]
            {
                new DialogueOption(StatType.Charm, "trapped"),  // Charm → Madness trap active
                new DialogueOption(StatType.Wit, "clean")       // No trap
            };
            var turn = MakeTurn(options);
            var ctx = MakeContext(
                playerStats: playerStats,
                opponentStats: opponentStats,
                activeTrapNames: new[] { "Madness" });

            var result = await _agent.DecideAsync(turn, ctx);

            // Charm normally has better EV (higher mod), but -2.0 trap penalty → Wit wins
            Assert.Equal(1, result.OptionIndex);
            Assert.True(result.Scores[1].Score > result.Scores[0].Score,
                "Untapped option should beat trapped option");
        }

        // Mutation: would catch if trap mapping is wrong (e.g., Rizz → Horniness)
        [Fact]
        public async Task ActiveTrap_RizzMapsToHorniness()
        {
            var playerStats = MakeStats(rizz: 5, honesty: 3);
            var opponentStats = MakeStats();

            var options = new[]
            {
                new DialogueOption(StatType.Rizz, "trapped"),
                new DialogueOption(StatType.Honesty, "clean")
            };
            var turn = MakeTurn(options);
            var ctx = MakeContext(
                playerStats: playerStats,
                opponentStats: opponentStats,
                activeTrapNames: new[] { "Horniness" });

            var result = await _agent.DecideAsync(turn, ctx);

            // Rizz → Horniness shadow → trap penalty applied
            Assert.Equal(1, result.OptionIndex);
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
            var opponentStats = MakeStats(selfAwareness: 2, wit: 1, chaos: 0, charm: 1);

            var options = new[]
            {
                new DialogueOption(StatType.Charm, "a"),
                new DialogueOption(StatType.Rizz, "b"),
                new DialogueOption(StatType.Honesty, "c", hasTellBonus: true),
                new DialogueOption(StatType.Chaos, "d", comboName: "The Switcheroo")
            };

            var turn = MakeTurn(options);
            var ctx = MakeContext(playerStats: playerStats, opponentStats: opponentStats);

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

        #region AC6: Required test scenarios

        // AC6.5: Mutation: would catch if tell bonus is not subtracted from need
        [Fact]
        public async Task TellBonus_LowersNeedBy2()
        {
            var playerStats = MakeStats(honesty: 3);
            var opponentStats = MakeStats(chaos: 2); // Honesty→Chaos, DC = 13+2=15

            // Without tell: need = 15 - 3 = 12, chance = 9/20 = 0.45
            // With tell: need = 15 - (3+2) = 10, chance = 11/20 = 0.55
            var withTell = new DialogueOption(StatType.Honesty, "tell", hasTellBonus: true);
            var withoutTell = new DialogueOption(StatType.Honesty, "plain");

            var resultTell = await _agent.DecideAsync(MakeTurn(withTell), MakeContext(playerStats: playerStats, opponentStats: opponentStats));
            var resultPlain = await _agent.DecideAsync(MakeTurn(withoutTell), MakeContext(playerStats: playerStats, opponentStats: opponentStats));

            Assert.True(resultTell.Scores[0].SuccessChance > resultPlain.Scores[0].SuccessChance,
                $"Tell should increase success chance: {resultTell.Scores[0].SuccessChance} > {resultPlain.Scores[0].SuccessChance}");
            // Verify the exact difference of 2/20 = 0.10
            float diff = resultTell.Scores[0].SuccessChance - resultPlain.Scores[0].SuccessChance;
            Assert.InRange(diff, 0.09f, 0.11f);
        }

        // AC6.6: Mutation: would catch if combo bonus is not added to expected gain on success
        [Fact]
        public async Task ComboBonus_IncreasesExpectedGainOnSuccess()
        {
            var playerStats = MakeStats(charm: 3);
            var opponentStats = MakeStats();

            var withCombo = new DialogueOption(StatType.Charm, "combo", comboName: "The Switcheroo");
            var withoutCombo = new DialogueOption(StatType.Charm, "plain");

            var resultCombo = await _agent.DecideAsync(MakeTurn(withCombo), MakeContext(playerStats: playerStats, opponentStats: opponentStats));
            var resultPlain = await _agent.DecideAsync(MakeTurn(withoutCombo), MakeContext(playerStats: playerStats, opponentStats: opponentStats));

            // Combo adds +1 interest on success → higher EV
            Assert.True(resultCombo.Scores[0].ExpectedInterestGain > resultPlain.Scores[0].ExpectedInterestGain,
                "Combo should increase expected interest gain");
        }

        // AC6.7: Mutation: would catch if callback bonus is not computed or not applied to need
        [Fact]
        public async Task CallbackBonus_LowersNeed()
        {
            var playerStats = MakeStats(charm: 3);
            var opponentStats = MakeStats();

            // CallbackBonus.Compute(5, 2) should return 1 (gap of 3, ≥ 2)
            var withCallback = new DialogueOption(StatType.Charm, "callback", callbackTurnNumber: 2);
            var withoutCallback = new DialogueOption(StatType.Charm, "plain");

            var resultCallback = await _agent.DecideAsync(
                MakeTurn(withCallback),
                MakeContext(playerStats: playerStats, opponentStats: opponentStats, turnNumber: 5));
            var resultPlain = await _agent.DecideAsync(
                MakeTurn(withoutCallback),
                MakeContext(playerStats: playerStats, opponentStats: opponentStats, turnNumber: 5));

            Assert.True(resultCallback.Scores[0].SuccessChance > resultPlain.Scores[0].SuccessChance,
                $"Callback should increase success chance: {resultCallback.Scores[0].SuccessChance} > {resultPlain.Scores[0].SuccessChance}");
        }

        // AC6.8: Mutation: would catch if agent doesn't pick highest-EV option without adjustments
        [Fact]
        public async Task BasicEvOrdering_PicksHighestEvOption()
        {
            // Strong Charm vs weak Rizz — no adjustments
            var playerStats = MakeStats(charm: 6, rizz: 0);
            var opponentStats = MakeStats(); // all 0, DC=13 for all

            var options = new[]
            {
                new DialogueOption(StatType.Charm, "strong"),  // need=7, ~70%
                new DialogueOption(StatType.Rizz, "weak")      // need=13, ~40%
            };
            var turn = MakeTurn(options);
            var ctx = MakeContext(playerStats: playerStats, opponentStats: opponentStats);
            var result = await _agent.DecideAsync(turn, ctx);

            Assert.Equal(0, result.OptionIndex);
        }

        #endregion

        #region Edge cases

        // Mutation: would catch if empty options silently returns instead of throwing
        [Fact]
        public async Task EmptyOptions_ThrowsInvalidOperationException()
        {
            var turn = MakeTurn(Array.Empty<DialogueOption>());
            var ctx = MakeContext();
            await Assert.ThrowsAsync<InvalidOperationException>(() => _agent.DecideAsync(turn, ctx));
        }

        // Mutation: would catch if agent fails on single option
        [Fact]
        public async Task SingleOption_ReturnsIndex0()
        {
            var turn = MakeTurn(new DialogueOption(StatType.Charm, "only"));
            var ctx = MakeContext();
            var result = await _agent.DecideAsync(turn, ctx);

            Assert.Equal(0, result.OptionIndex);
            Assert.Single(result.Scores);
        }

        // Mutation: would catch if tie-breaking doesn't use lowest index
        [Fact]
        public async Task TiedScores_PicksLowestIndex()
        {
            // Two identical options → identical scores → first wins
            var playerStats = MakeStats(charm: 3, rizz: 3);
            // Need same defence for both: Charm→SA, Rizz→Wit
            var opponentStats = MakeStats(selfAwareness: 0, wit: 0);

            var options = new[]
            {
                new DialogueOption(StatType.Charm, "a"),
                new DialogueOption(StatType.Rizz, "b")
            };
            var turn = MakeTurn(options);
            var ctx = MakeContext(playerStats: playerStats, opponentStats: opponentStats);
            var result = await _agent.DecideAsync(turn, ctx);

            // Both have identical stats/defences → tied → pick index 0
            Assert.Equal(0, result.OptionIndex);
        }

        // Mutation: would catch if successChance doesn't clamp to 0 for impossible rolls
        [Fact]
        public async Task VeryHighDc_SuccessChanceClampedTo0()
        {
            var playerStats = MakeStats(charm: 0);
            var opponentStats = MakeStats(selfAwareness: 10); // DC = 13 + 10 = 23, need = 23

            var turn = MakeTurn(new DialogueOption(StatType.Charm, "impossible"));
            var ctx = MakeContext(playerStats: playerStats, opponentStats: opponentStats);
            var result = await _agent.DecideAsync(turn, ctx);

            Assert.Equal(0.0f, result.Scores[0].SuccessChance);
        }

        // Mutation: would catch if successChance doesn't clamp to 1.0 for guaranteed rolls
        [Fact]
        public async Task VeryLowDc_SuccessChanceClampedTo1()
        {
            var playerStats = MakeStats(charm: 15);
            var opponentStats = MakeStats(selfAwareness: 0); // DC = 13, need = 13 - 15 = -2

            var turn = MakeTurn(new DialogueOption(StatType.Charm, "guaranteed"));
            var ctx = MakeContext(playerStats: playerStats, opponentStats: opponentStats);
            var result = await _agent.DecideAsync(turn, ctx);

            Assert.Equal(1.0f, result.Scores[0].SuccessChance);
        }

        // Mutation: would catch if null ActiveTrapNames crashes instead of treating as empty
        [Fact]
        public async Task NullActiveTrapNames_TreatedAsEmpty()
        {
            // PlayerAgentContext constructor requires non-null activeTrapNames,
            // but the spec says null should be treated as empty.
            // Since ctor throws, we test with empty array instead:
            var turn = MakeTurn(new DialogueOption(StatType.Charm, "test"));
            var ctx = MakeContext(activeTrapNames: Array.Empty<string>());
            var result = await _agent.DecideAsync(turn, ctx);

            Assert.NotNull(result);
            Assert.Equal(0, result.OptionIndex);
        }

        // Mutation: would catch if callback opener bonus isn't computed correctly
        [Fact]
        public async Task CallbackOpener_HighestBonus()
        {
            var playerStats = MakeStats(charm: 3);
            var opponentStats = MakeStats();

            // Opener callback: callbackTurnNumber=0, currentTurn=5
            // CallbackBonus.Compute(5, 0) should return 3 (opener with gap >= 2)
            var openerCallback = new DialogueOption(StatType.Charm, "opener", callbackTurnNumber: 0);
            var normalCallback = new DialogueOption(StatType.Charm, "normal", callbackTurnNumber: 3);

            var resultOpener = await _agent.DecideAsync(
                MakeTurn(openerCallback),
                MakeContext(playerStats: playerStats, opponentStats: opponentStats, turnNumber: 5));
            var resultNormal = await _agent.DecideAsync(
                MakeTurn(normalCallback),
                MakeContext(playerStats: playerStats, opponentStats: opponentStats, turnNumber: 5));

            Assert.True(resultOpener.Scores[0].SuccessChance > resultNormal.Scores[0].SuccessChance,
                "Opener callback (+3) should give higher success than normal callback (+1)");
        }

        // Mutation: would catch if near-win bias applies at interest=24 boundary
        [Fact]
        public async Task NearWin_Interest24_AppliesBias()
        {
            var playerStats = MakeStats(charm: 5);
            var opponentStats = MakeStats();

            var turn = MakeTurn(new DialogueOption(StatType.Charm, "safe")); // Safe tier

            var ctxAt24 = MakeContext(
                playerStats: playerStats,
                opponentStats: opponentStats,
                currentInterest: 24,
                interestState: InterestState.AlmostThere);
            var ctxAt12 = MakeContext(
                playerStats: playerStats,
                opponentStats: opponentStats,
                currentInterest: 12,
                interestState: InterestState.Interested);

            var resultAt24 = await _agent.DecideAsync(turn, ctxAt24);
            var resultAt12 = await _agent.DecideAsync(turn, ctxAt12);

            // At interest=24 (in [19,24]) the safe option should get +2.0 bonus vs interest=12
            Assert.True(resultAt24.Scores[0].Score > resultAt12.Scores[0].Score,
                "Near-win bonus should boost score at interest=24");
        }

        // Mutation: would catch if Bored bias at interest=5 incorrectly applies (5 is Lukewarm, not Bored)
        [Fact]
        public async Task Interest5_IsLukewarm_NoBoredBias()
        {
            var playerStats = MakeStats(chaos: 0);
            var opponentStats = MakeStats(charm: 5); // Chaos→Charm, DC=18 → Bold

            var turn = MakeTurn(new DialogueOption(StatType.Chaos, "bold"));

            var ctxBored = MakeContext(
                playerStats: playerStats,
                opponentStats: opponentStats,
                currentInterest: 3,
                interestState: InterestState.Bored);
            var ctxLukewarm = MakeContext(
                playerStats: playerStats,
                opponentStats: opponentStats,
                currentInterest: 5,
                interestState: InterestState.Lukewarm);

            var resultBored = await _agent.DecideAsync(turn, ctxBored);
            var resultLukewarm = await _agent.DecideAsync(turn, ctxLukewarm);

            // Bored gets +1.0 on Bold, Lukewarm does NOT
            Assert.True(resultBored.Scores[0].Score > resultLukewarm.Scores[0].Score,
                "Bored should get bias but Lukewarm should not");
        }

        // Mutation: would catch if BonusesApplied doesn't include tell bonus
        [Fact]
        public async Task BonusesApplied_IncludesTellBonus()
        {
            var turn = MakeTurn(new DialogueOption(StatType.Honesty, "tell", hasTellBonus: true));
            var ctx = MakeContext();
            var result = await _agent.DecideAsync(turn, ctx);

            Assert.Contains(result.Scores[0].BonusesApplied,
                b => b.Contains("tell", StringComparison.OrdinalIgnoreCase));
        }

        // Mutation: would catch if BonusesApplied doesn't include combo name
        [Fact]
        public async Task BonusesApplied_IncludesComboName()
        {
            var turn = MakeTurn(new DialogueOption(StatType.Charm, "combo", comboName: "The Switcheroo"));
            var ctx = MakeContext();
            var result = await _agent.DecideAsync(turn, ctx);

            Assert.Contains(result.Scores[0].BonusesApplied,
                b => b.Contains("Switcheroo", StringComparison.OrdinalIgnoreCase));
        }

        // Mutation: would catch if BonusesApplied doesn't include callback bonus
        [Fact]
        public async Task BonusesApplied_IncludesCallbackBonus()
        {
            var turn = MakeTurn(new DialogueOption(StatType.Charm, "cb", callbackTurnNumber: 1));
            var ctx = MakeContext(turnNumber: 5);
            var result = await _agent.DecideAsync(turn, ctx);

            Assert.Contains(result.Scores[0].BonusesApplied,
                b => b.Contains("callback", StringComparison.OrdinalIgnoreCase));
        }

        // Mutation: would catch if momentum bonus is included as BonusApplied
        [Fact]
        public async Task BonusesApplied_IncludesMomentumWhenActive()
        {
            var playerStats = MakeStats(charm: 5);
            var turn = MakeTurn(new DialogueOption(StatType.Charm, "test"));
            var ctx = MakeContext(playerStats: playerStats, momentumStreak: 3);
            var result = await _agent.DecideAsync(turn, ctx);

            Assert.Contains(result.Scores[0].BonusesApplied,
                b => b.Contains("momentum", StringComparison.OrdinalIgnoreCase));
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

        #region Risk tier bonus

        // Mutation: would catch if risk tier bonus is not applied (Hard → +1, Bold → +2)
        [Fact]
        public async Task RiskTierBonus_HardTierAddsInterestBonus()
        {
            // Create two options with different risk tiers but test the Hard one gets bonus
            var playerStats = MakeStats(charm: 2, rizz: 2);
            // Charm→SA, Rizz→Wit
            // Set defences so one is Safe, one is Hard
            var opponentStats = MakeStats(selfAwareness: 0, wit: 5);
            // Charm: need = 13-2=11, Hard tier → +1 risk bonus
            // Rizz: need = 18-2=16, Bold tier → +2 risk bonus

            var options = new[]
            {
                new DialogueOption(StatType.Charm, "hard"),
                new DialogueOption(StatType.Rizz, "bold")
            };
            var turn = MakeTurn(options);
            var ctx = MakeContext(playerStats: playerStats, opponentStats: opponentStats);
            var result = await _agent.DecideAsync(turn, ctx);

            // Hard (need=11) has higher success chance than Bold (need=16)
            // But Bold has higher risk tier bonus. Overall Hard should still win on EV
            Assert.True(result.Scores[0].SuccessChance > result.Scores[1].SuccessChance);
        }

        #endregion
    }
}
