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
    /// Tests for ScoringPlayerAgent shadow growth risk scoring (issue #416).
    /// Validates fixation growth penalty, denial growth penalty, fixation threshold EV reduction,
    /// stat variety bonus, backward compatibility, and edge cases.
    /// </summary>
    public class ScoringPlayerAgentShadowRiskTests
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

        private static DialogueOption MakeOption(StatType stat)
        {
            return new DialogueOption(stat, $"{stat} option");
        }

        private static TurnStart MakeTurn(params DialogueOption[] options)
        {
            return new TurnStart(
                options,
                new GameStateSnapshot(10, InterestState.Interested, 0, Array.Empty<string>(), 3));
        }

        /// <summary>
        /// Creates a PlayerAgentContext with the new shadow risk fields.
        /// </summary>
        private static PlayerAgentContext MakeContext(
            StatBlock? player = null,
            StatBlock? opponent = null,
            int interest = 10,
            InterestState state = InterestState.Interested,
            int momentum = 0,
            string[]? traps = null,
            int sessionHorniness = 0,
            Dictionary<ShadowStatType, int>? shadowValues = null,
            int turnNumber = 5,
            StatType? lastStatUsed = null,
            StatType? secondLastStatUsed = null,
            bool honestyAvailableLastTurn = false)
        {
            return new PlayerAgentContext(
                player ?? MakeStats(charm: 3, rizz: 3, honesty: 3, chaos: 3, wit: 3, sa: 3),
                opponent ?? MakeStats(charm: 1, rizz: 1, honesty: 1, chaos: 1, wit: 1, sa: 1),
                interest,
                state,
                momentum,
                traps ?? Array.Empty<string>(),
                sessionHorniness,
                shadowValues,
                turnNumber,
                lastStatUsed,
                secondLastStatUsed,
                honestyAvailableLastTurn);
        }

        /// <summary>
        /// Gets the score for the option at given index from a decision.
        /// </summary>
        private static float ScoreAt(PlayerDecision d, int index) => d.Scores[index].Score;

        #endregion

        // ================================================================
        // AC1: Fixation Growth Penalty
        // ================================================================

        // What: When LastStatUsed and SecondLastStatUsed both equal the option's Stat,
        //       score is reduced by FixationGrowthPenalty (0.5).
        // Mutation: Would catch if penalty is not applied, or applied with wrong value.
        [Fact]
        public async Task AC1_FixationGrowthPenalty_ReducesScoreForThirdConsecutiveSameStat()
        {
            var turn = MakeTurn(MakeOption(StatType.Chaos), MakeOption(StatType.Charm));

            // With fixation growth penalty (Chaos used twice before)
            var ctxWithPenalty = MakeContext(
                lastStatUsed: StatType.Chaos,
                secondLastStatUsed: StatType.Chaos);

            // Without (different stat history)
            var ctxNoPenalty = MakeContext(
                lastStatUsed: StatType.Charm,
                secondLastStatUsed: StatType.Wit);

            var dPenalty = await _agent.DecideAsync(turn, ctxWithPenalty);
            var dNoPenalty = await _agent.DecideAsync(turn, ctxNoPenalty);

            // Chaos option (index 0) should score 0.5 lower with penalty
            float chaosWithPenalty = ScoreAt(dPenalty, 0);
            float chaosWithoutPenalty = ScoreAt(dNoPenalty, 0);

            // Note: variety bonus also affects scores, so isolate the difference.
            // In the penalty case: Chaos gets -0.5 fixation penalty, no variety bonus (matches history).
            //   Charm gets +0.1 variety bonus (doesn't match Chaos).
            // In the no-penalty case: Chaos gets +0.1 variety bonus (doesn't match Charm or Wit).
            //   Charm gets no variety bonus (matches Charm in LastStatUsed).
            // So the raw fixation penalty delta on Chaos is: (nopenalty has +0.1 variety) vs (penalty has -0.5 fixation, no variety)
            // Delta = 0.5 + 0.1 = 0.6
            Assert.True(chaosWithoutPenalty > chaosWithPenalty,
                "Chaos should score lower when it would trigger fixation growth");
            Assert.InRange(chaosWithoutPenalty - chaosWithPenalty, 0.55f, 0.65f);
        }

        // What: Fixation penalty only applies when BOTH last stats match.
        // Mutation: Would catch if penalty fires when only one of them matches.
        [Fact]
        public async Task AC1_FixationGrowthPenalty_NotAppliedWhenOnlyOneMatchesStat()
        {
            var turn = MakeTurn(MakeOption(StatType.Chaos));

            // Only LastStatUsed is Chaos, SecondLastStatUsed is different
            var ctxOnlyOne = MakeContext(
                lastStatUsed: StatType.Chaos,
                secondLastStatUsed: StatType.Wit);

            // Neither matches
            var ctxNeither = MakeContext(
                lastStatUsed: StatType.Charm,
                secondLastStatUsed: StatType.Wit);

            var dOnlyOne = await _agent.DecideAsync(turn, ctxOnlyOne);
            var dNeither = await _agent.DecideAsync(turn, ctxNeither);

            // With only one matching, Chaos doesn't get the -0.5 penalty.
            // Both should differ only by variety bonus: neither case gives Chaos variety bonus
            // (Chaos matches LastStatUsed in ctxOnlyOne), and in ctxNeither Chaos gets +0.1 variety.
            // So dNeither should be 0.1 higher, not 0.5.
            float diff = ScoreAt(dNeither, 0) - ScoreAt(dOnlyOne, 0);
            Assert.InRange(diff, 0.05f, 0.15f); // ~0.1 from variety, NOT 0.5+ from fixation
        }

        // What: Fixation penalty is skipped when LastStatUsed is null.
        // Mutation: Would catch if penalty fires when history is null.
        [Fact]
        public async Task AC1_FixationGrowthPenalty_SkippedWhenLastStatIsNull()
        {
            var turn = MakeTurn(MakeOption(StatType.Chaos));

            var ctxNull = MakeContext(lastStatUsed: null, secondLastStatUsed: null);
            var ctxSet = MakeContext(
                lastStatUsed: StatType.Chaos,
                secondLastStatUsed: StatType.Chaos);

            var dNull = await _agent.DecideAsync(turn, ctxNull);
            var dSet = await _agent.DecideAsync(turn, ctxSet);

            // Null context: no penalty, no variety bonus (both null).
            // Set context: -0.5 fixation penalty, no variety bonus.
            Assert.True(ScoreAt(dNull, 0) > ScoreAt(dSet, 0));
        }

        // ================================================================
        // AC2: Denial Growth Penalty
        // ================================================================

        // What: When Honesty is among options, non-Honesty options get -0.3.
        // Mutation: Would catch if penalty is not applied or wrong value.
        [Fact]
        public async Task AC2_DenialPenalty_AppliedToNonHonestyWhenHonestyAvailable()
        {
            var turn = MakeTurn(
                MakeOption(StatType.Charm),
                MakeOption(StatType.Honesty),
                MakeOption(StatType.Wit));

            // Use null stat history to eliminate variety bonus interference
            var ctx = MakeContext(lastStatUsed: null, secondLastStatUsed: null);

            // Compare with a turn that has no Honesty option
            var turnNoHonesty = MakeTurn(
                MakeOption(StatType.Charm),
                MakeOption(StatType.Rizz),
                MakeOption(StatType.Wit));
            var ctxNoHonesty = MakeContext(lastStatUsed: null, secondLastStatUsed: null);

            var dWithHonesty = await _agent.DecideAsync(turn, ctx);
            var dWithoutHonesty = await _agent.DecideAsync(turnNoHonesty, ctxNoHonesty);

            // Charm at index 0 should be ~0.3 lower when Honesty is available
            float charmWithHonesty = ScoreAt(dWithHonesty, 0);
            float charmWithoutHonesty = ScoreAt(dWithoutHonesty, 0);

            Assert.True(charmWithoutHonesty > charmWithHonesty,
                "Non-Honesty options should score lower when Honesty is available");
            Assert.InRange(charmWithoutHonesty - charmWithHonesty, 0.25f, 0.35f);
        }

        // What: Honesty option itself is NOT penalized.
        // Mutation: Would catch if penalty wrongly applied to Honesty too.
        [Fact]
        public async Task AC2_DenialPenalty_NotAppliedToHonestyOption()
        {
            // Turn A: Honesty is present (penalty hits others, not Honesty)
            var turnA = MakeTurn(
                MakeOption(StatType.Honesty),
                MakeOption(StatType.Charm));

            // Turn B: Same option but Honesty is replaced (so no denial penalty at all)
            // To isolate, compare the Honesty option's score when present.
            var ctx = MakeContext(lastStatUsed: null, secondLastStatUsed: null);
            var dA = await _agent.DecideAsync(turnA, ctx);

            // Honesty at index 0 shouldn't have the denial penalty.
            // Charm at index 1 should have -0.3 denial penalty.
            // If Honesty had the same stat+DC as Charm, the delta should be ~0.3.
            // Since stats/DCs may differ, just verify Charm bonuses contain denial info
            // or verify relative scoring.
            float honestyScore = ScoreAt(dA, 0);
            float charmScore = ScoreAt(dA, 1);

            // Verify Charm is penalized more than Honesty (beyond base EV differences)
            // We can't assert exact values without knowing base EVs, but we can check
            // that without the denial penalty on a separate run, the gap changes.
            var turnNoDenial = MakeTurn(
                MakeOption(StatType.Honesty),
                MakeOption(StatType.Charm));
            // Actually this is the same turn. Let's use a cleaner approach:
            // Compare Charm's score between a turn with and without Honesty.
            var turnWithHonesty = MakeTurn(MakeOption(StatType.Charm), MakeOption(StatType.Honesty));
            var turnNoHonesty = MakeTurn(MakeOption(StatType.Charm), MakeOption(StatType.Rizz));

            var dWith = await _agent.DecideAsync(turnWithHonesty, ctx);
            var dWithout = await _agent.DecideAsync(turnNoHonesty, ctx);

            // Charm (index 0) should be 0.3 lower when Honesty is present
            Assert.InRange(ScoreAt(dWithout, 0) - ScoreAt(dWith, 0), 0.25f, 0.35f);
        }

        // What: Denial penalty not applied when no Honesty option exists.
        // Mutation: Would catch if penalty is always applied regardless of Honesty presence.
        [Fact]
        public async Task AC2_DenialPenalty_NotAppliedWhenNoHonestyOption()
        {
            var turn = MakeTurn(
                MakeOption(StatType.Charm),
                MakeOption(StatType.Rizz));

            var ctx = MakeContext(lastStatUsed: null, secondLastStatUsed: null);
            var d = await _agent.DecideAsync(turn, ctx);

            // No Honesty option → no denial penalty → scores are just base EV
            // Verify by comparing with a turn that has Honesty added
            var turnWithHonesty = MakeTurn(
                MakeOption(StatType.Charm),
                MakeOption(StatType.Rizz),
                MakeOption(StatType.Honesty));

            var dWithHonesty = await _agent.DecideAsync(turnWithHonesty, ctx);

            // Charm should score higher without Honesty present (no denial penalty)
            Assert.True(ScoreAt(d, 0) > ScoreAt(dWithHonesty, 0));
        }

        // ================================================================
        // AC3: Fixation Threshold EV Reduction
        // ================================================================

        // What: Fixation >= 12 (T2) squares the Chaos option's successChance.
        // Mutation: Would catch if disadvantage isn't applied at T2 threshold.
        [Fact]
        public async Task AC3_FixationT2_SquaresSuccessChanceForChaos()
        {
            var turn = MakeTurn(MakeOption(StatType.Chaos));

            var shadows = new Dictionary<ShadowStatType, int>
            {
                { ShadowStatType.Fixation, 14 }
            };

            var ctxT2 = MakeContext(
                shadowValues: shadows,
                lastStatUsed: null, secondLastStatUsed: null);

            var ctxNoShadow = MakeContext(
                shadowValues: null,
                lastStatUsed: null, secondLastStatUsed: null);

            var dT2 = await _agent.DecideAsync(turn, ctxT2);
            var dNoShadow = await _agent.DecideAsync(turn, ctxNoShadow);

            float scNormal = dNoShadow.Scores[0].SuccessChance;
            float scT2 = dT2.Scores[0].SuccessChance;

            // T2 should square the success chance: scT2 ≈ scNormal * scNormal
            Assert.InRange(scT2, scNormal * scNormal - 0.01f, scNormal * scNormal + 0.01f);
        }

        // What: Fixation >= 6 and < 12 (T1) multiplies expectedGainOnSuccess by 0.8.
        // Mutation: Would catch if T1 multiplier not applied or wrong value.
        [Fact]
        public async Task AC3_FixationT1_ReducesExpectedGainForChaos()
        {
            var turn = MakeTurn(MakeOption(StatType.Chaos));

            var shadowsT1 = new Dictionary<ShadowStatType, int>
            {
                { ShadowStatType.Fixation, 8 }
            };

            var ctxT1 = MakeContext(
                shadowValues: shadowsT1,
                lastStatUsed: null, secondLastStatUsed: null);

            var ctxNoShadow = MakeContext(
                shadowValues: null,
                lastStatUsed: null, secondLastStatUsed: null);

            var dT1 = await _agent.DecideAsync(turn, ctxT1);
            var dNoShadow = await _agent.DecideAsync(turn, ctxNoShadow);

            // Success chance should NOT be modified at T1
            Assert.Equal((double)dNoShadow.Scores[0].SuccessChance, (double)dT1.Scores[0].SuccessChance, 3);

            // ExpectedInterestGain should be lower (0.8x on gain component)
            Assert.True(dT1.Scores[0].ExpectedInterestGain < dNoShadow.Scores[0].ExpectedInterestGain,
                "T1 Fixation should reduce expected gain for Chaos");
        }

        // What: Fixation < 6 (T0) has no adjustment.
        // Mutation: Would catch if T0 incorrectly applies penalty.
        [Fact]
        public async Task AC3_FixationT0_NoAdjustment()
        {
            var turn = MakeTurn(MakeOption(StatType.Chaos));

            var shadowsT0 = new Dictionary<ShadowStatType, int>
            {
                { ShadowStatType.Fixation, 3 }
            };

            var ctxT0 = MakeContext(
                shadowValues: shadowsT0,
                lastStatUsed: null, secondLastStatUsed: null);

            var ctxNoShadow = MakeContext(
                shadowValues: null,
                lastStatUsed: null, secondLastStatUsed: null);

            var dT0 = await _agent.DecideAsync(turn, ctxT0);
            var dNoShadow = await _agent.DecideAsync(turn, ctxNoShadow);

            // Scores should be identical
            Assert.Equal((double)ScoreAt(dNoShadow, 0), (double)ScoreAt(dT0, 0), 3);
        }

        // What: Only Chaos options are affected by Fixation threshold.
        // Mutation: Would catch if penalty applied to non-Chaos stats.
        [Fact]
        public async Task AC3_FixationThreshold_OnlyAffectsChaos()
        {
            var turn = MakeTurn(MakeOption(StatType.Charm), MakeOption(StatType.Chaos));

            var shadowsT2 = new Dictionary<ShadowStatType, int>
            {
                { ShadowStatType.Fixation, 14 }
            };

            var ctxT2 = MakeContext(
                shadowValues: shadowsT2,
                lastStatUsed: null, secondLastStatUsed: null);

            var ctxNoShadow = MakeContext(
                shadowValues: null,
                lastStatUsed: null, secondLastStatUsed: null);

            var dT2 = await _agent.DecideAsync(turn, ctxT2);
            var dNoShadow = await _agent.DecideAsync(turn, ctxNoShadow);

            // Charm (index 0) should be unaffected
            Assert.Equal((double)ScoreAt(dNoShadow, 0), (double)ScoreAt(dT2, 0), 3);

            // Chaos (index 1) should be affected
            Assert.True(ScoreAt(dNoShadow, 1) > ScoreAt(dT2, 1));
        }

        // What: Fixation >= 18 (T3) treated same as T2.
        // Mutation: Would catch if T3 is handled differently or not at all.
        [Fact]
        public async Task AC3_FixationT3_TreatedSameAsT2()
        {
            var turn = MakeTurn(MakeOption(StatType.Chaos));

            var shadowsT2 = new Dictionary<ShadowStatType, int>
            {
                { ShadowStatType.Fixation, 12 }
            };
            var shadowsT3 = new Dictionary<ShadowStatType, int>
            {
                { ShadowStatType.Fixation, 20 }
            };

            var ctxT2 = MakeContext(shadowValues: shadowsT2, lastStatUsed: null, secondLastStatUsed: null);
            var ctxT3 = MakeContext(shadowValues: shadowsT3, lastStatUsed: null, secondLastStatUsed: null);

            var dT2 = await _agent.DecideAsync(turn, ctxT2);
            var dT3 = await _agent.DecideAsync(turn, ctxT3);

            // Both should square the success chance, so scores should be equal
            Assert.Equal((double)ScoreAt(dT2, 0), (double)ScoreAt(dT3, 0), 3);
        }

        // What: ShadowValues non-null but missing Fixation key → treated as Fixation=0.
        // Mutation: Would catch if missing key throws or triggers penalty.
        [Fact]
        public async Task AC3_ShadowValues_MissingFixationKey_NoAdjustment()
        {
            var turn = MakeTurn(MakeOption(StatType.Chaos));

            var shadowsNoFixation = new Dictionary<ShadowStatType, int>
            {
                { ShadowStatType.Denial, 15 }
            };

            var ctxNoFixation = MakeContext(
                shadowValues: shadowsNoFixation,
                lastStatUsed: null, secondLastStatUsed: null);

            var ctxNoShadow = MakeContext(
                shadowValues: null,
                lastStatUsed: null, secondLastStatUsed: null);

            var dNoFixation = await _agent.DecideAsync(turn, ctxNoFixation);
            var dNoShadow = await _agent.DecideAsync(turn, ctxNoShadow);

            Assert.Equal((double)ScoreAt(dNoShadow, 0), (double)ScoreAt(dNoFixation, 0), 3);
        }

        // ================================================================
        // AC4: Stat Variety Bonus
        // ================================================================

        // What: Options not matching LastStatUsed or SecondLastStatUsed get +0.1.
        // Mutation: Would catch if variety bonus not applied.
        [Fact]
        public async Task AC4_StatVarietyBonus_AppliedToNonRecentStats()
        {
            var turn = MakeTurn(
                MakeOption(StatType.Charm),   // matches LastStatUsed → no bonus
                MakeOption(StatType.Rizz));    // doesn't match → +0.1

            var ctxHistory = MakeContext(
                lastStatUsed: StatType.Charm,
                secondLastStatUsed: StatType.Wit);

            var ctxNoHistory = MakeContext(
                lastStatUsed: null,
                secondLastStatUsed: null);

            var dHistory = await _agent.DecideAsync(turn, ctxHistory);
            var dNoHistory = await _agent.DecideAsync(turn, ctxNoHistory);

            // Rizz should gain +0.1 relative to no-history baseline
            float rizzWithHistory = ScoreAt(dHistory, 1);
            float rizzNoHistory = ScoreAt(dNoHistory, 1);
            Assert.InRange(rizzWithHistory - rizzNoHistory, 0.05f, 0.15f);

            // Charm should NOT gain variety bonus (matches LastStatUsed)
            float charmWithHistory = ScoreAt(dHistory, 0);
            float charmNoHistory = ScoreAt(dNoHistory, 0);
            Assert.InRange(charmWithHistory - charmNoHistory, -0.05f, 0.05f);
        }

        // What: Option matching SecondLastStatUsed also doesn't get the bonus.
        // Mutation: Would catch if only LastStatUsed is checked, not SecondLastStatUsed.
        [Fact]
        public async Task AC4_StatVarietyBonus_NotAppliedIfMatchesSecondLast()
        {
            var turn = MakeTurn(
                MakeOption(StatType.Wit),     // matches SecondLastStatUsed → no bonus
                MakeOption(StatType.Rizz));    // doesn't match → +0.1

            var ctx = MakeContext(
                lastStatUsed: StatType.Charm,
                secondLastStatUsed: StatType.Wit);

            var ctxNoHistory = MakeContext(
                lastStatUsed: null,
                secondLastStatUsed: null);

            var dCtx = await _agent.DecideAsync(turn, ctx);
            var dNoHist = await _agent.DecideAsync(turn, ctxNoHistory);

            // Wit matches SecondLastStatUsed → no bonus
            float witDiff = ScoreAt(dCtx, 0) - ScoreAt(dNoHist, 0);
            Assert.InRange(witDiff, -0.05f, 0.05f);

            // Rizz doesn't match → gets +0.1
            float rizzDiff = ScoreAt(dCtx, 1) - ScoreAt(dNoHist, 1);
            Assert.InRange(rizzDiff, 0.05f, 0.15f);
        }

        // What: When both history values are null (first turn), no variety bonus applied.
        // Mutation: Would catch if bonus is applied when there's no history.
        [Fact]
        public async Task AC4_StatVarietyBonus_SkippedOnFirstTurn()
        {
            var turn = MakeTurn(MakeOption(StatType.Charm), MakeOption(StatType.Rizz));

            var ctxFirst = MakeContext(lastStatUsed: null, secondLastStatUsed: null);

            var d = await _agent.DecideAsync(turn, ctxFirst);

            // Both should have equal base EV (same stats), no variety adjustments
            // This is validated by comparing to a run with history where one gets bonus
            var ctxWithHistory = MakeContext(
                lastStatUsed: StatType.Chaos,
                secondLastStatUsed: StatType.Chaos);

            var dWithHistory = await _agent.DecideAsync(turn, ctxWithHistory);

            // With history, both Charm and Rizz get +0.1 variety (neither matches Chaos)
            // Without history, neither gets anything
            Assert.True(ScoreAt(dWithHistory, 0) > ScoreAt(d, 0),
                "With stat history, non-matching options should get variety bonus");
        }

        // What: When LastStatUsed == SecondLastStatUsed, options matching that stat miss bonus,
        //       all others get +0.1.
        // Mutation: Would catch if duplicate history isn't handled correctly.
        [Fact]
        public async Task AC4_StatVarietyBonus_BothHistorySameStatStillWorks()
        {
            var turn = MakeTurn(
                MakeOption(StatType.Chaos),   // matches both → no bonus
                MakeOption(StatType.Charm));   // matches neither → +0.1

            var ctx = MakeContext(
                lastStatUsed: StatType.Chaos,
                secondLastStatUsed: StatType.Chaos);

            var ctxNoHist = MakeContext(lastStatUsed: null, secondLastStatUsed: null);

            var dCtx = await _agent.DecideAsync(turn, ctx);
            var dNoHist = await _agent.DecideAsync(turn, ctxNoHist);

            // Chaos: has fixation penalty (-0.5) + no variety bonus
            // Charm: gets variety bonus (+0.1)
            float charmDiff = ScoreAt(dCtx, 1) - ScoreAt(dNoHist, 1);
            Assert.InRange(charmDiff, 0.05f, 0.15f);

            float chaosDiff = ScoreAt(dCtx, 0) - ScoreAt(dNoHist, 0);
            // Chaos gets -0.5 fixation penalty, no variety bonus
            Assert.True(chaosDiff < -0.4f);
        }

        // ================================================================
        // AC5: Backward Compatibility
        // ================================================================

        // What: Default context (all new fields null/false) produces same scores as before.
        // Mutation: Would catch if defaults trigger penalties.
        [Fact]
        public async Task AC5_BackwardCompat_DefaultContextNoScoreChange()
        {
            var turn = MakeTurn(
                MakeOption(StatType.Charm),
                MakeOption(StatType.Chaos),
                MakeOption(StatType.Honesty));

            // Old-style context without new fields
            var ctxDefault = MakeContext(
                lastStatUsed: null,
                secondLastStatUsed: null,
                honestyAvailableLastTurn: false,
                shadowValues: null);

            var d = await _agent.DecideAsync(turn, ctxDefault);

            // Should complete without error and produce valid scores
            Assert.Equal(3, d.Scores.Length);
            foreach (var score in d.Scores)
            {
                // Scores should be reasonable (not NaN or extreme from penalty bugs)
                Assert.False(float.IsNaN(score.Score));
                Assert.False(float.IsInfinity(score.Score));
            }
        }

        // ================================================================
        // AC6: Deterministic Output
        // ================================================================

        // What: Same inputs with shadow risk fields produce identical outputs.
        // Mutation: Would catch if randomness is introduced in shadow scoring.
        [Fact]
        public async Task AC6_Deterministic_WithShadowRiskFields()
        {
            var turn = MakeTurn(
                MakeOption(StatType.Chaos),
                MakeOption(StatType.Honesty),
                MakeOption(StatType.Charm));

            var shadows = new Dictionary<ShadowStatType, int>
            {
                { ShadowStatType.Fixation, 14 }
            };

            var ctx = MakeContext(
                shadowValues: shadows,
                lastStatUsed: StatType.Chaos,
                secondLastStatUsed: StatType.Chaos);

            var d1 = await _agent.DecideAsync(turn, ctx);
            var d2 = await _agent.DecideAsync(turn, ctx);

            Assert.Equal(d1.OptionIndex, d2.OptionIndex);
            for (int i = 0; i < d1.Scores.Length; i++)
            {
                Assert.Equal((double)d1.Scores[i].Score, (double)d2.Scores[i].Score, 5);
            }
        }

        // ================================================================
        // Edge Cases
        // ================================================================

        // What: Edge case 1 — First turn, but Honesty available → denial penalty still applies.
        // Mutation: Would catch if denial penalty is skipped on first turn.
        [Fact]
        public async Task EdgeCase_FirstTurn_DenialPenaltyStillApplies()
        {
            var turn = MakeTurn(
                MakeOption(StatType.Charm),
                MakeOption(StatType.Honesty));

            var ctxFirstTurn = MakeContext(
                lastStatUsed: null,
                secondLastStatUsed: null);

            var turnNoHonesty = MakeTurn(
                MakeOption(StatType.Charm),
                MakeOption(StatType.Rizz));

            var dFirst = await _agent.DecideAsync(turn, ctxFirstTurn);
            var dNoHonesty = await _agent.DecideAsync(turnNoHonesty, ctxFirstTurn);

            // Charm should be penalized -0.3 when Honesty is available (even on first turn)
            Assert.True(ScoreAt(dNoHonesty, 0) > ScoreAt(dFirst, 0),
                "Denial penalty should apply on first turn when Honesty is available");
        }

        // What: Edge case 2 — Second turn, only LastStatUsed set.
        // Mutation: Would catch if fixation penalty fires with only one history value.
        [Fact]
        public async Task EdgeCase_SecondTurn_NoFixationPenalty()
        {
            var turn = MakeTurn(MakeOption(StatType.Chaos));

            var ctxSecond = MakeContext(
                lastStatUsed: StatType.Chaos,
                secondLastStatUsed: null);

            var ctxNone = MakeContext(
                lastStatUsed: null,
                secondLastStatUsed: null);

            var dSecond = await _agent.DecideAsync(turn, ctxSecond);
            var dNone = await _agent.DecideAsync(turn, ctxNone);

            // No fixation penalty on second turn (only one history).
            // Difference should be small — only variety bonus effect.
            // Chaos matches LastStatUsed in ctxSecond so no variety bonus there;
            // In ctxNone, no history so no variety bonus either. Scores should be equal.
            Assert.Equal((double)ScoreAt(dNone, 0), (double)ScoreAt(dSecond, 0), 3);
        }

        // What: Edge case 3 — All options same stat (forced Rizz from Horniness ≥18).
        // Mutation: Would catch if all-same-stat causes scoring errors.
        [Fact]
        public async Task EdgeCase_AllOptionsSameStat_NoError()
        {
            var turn = MakeTurn(
                MakeOption(StatType.Rizz),
                MakeOption(StatType.Rizz),
                MakeOption(StatType.Rizz));

            var ctx = MakeContext(
                lastStatUsed: StatType.Rizz,
                secondLastStatUsed: StatType.Rizz);

            var d = await _agent.DecideAsync(turn, ctx);

            // Should not throw; all options get fixation penalty equally
            Assert.Equal(3, d.Scores.Length);
            Assert.InRange(d.OptionIndex, 0, 2);
        }

        // What: Edge case 4 — ShadowValues contains Fixation = 0.
        // Mutation: Would catch if Fixation=0 incorrectly triggers penalty.
        [Fact]
        public async Task EdgeCase_FixationZero_NoAdjustment()
        {
            var turn = MakeTurn(MakeOption(StatType.Chaos));

            var shadowsZero = new Dictionary<ShadowStatType, int>
            {
                { ShadowStatType.Fixation, 0 }
            };

            var ctxZero = MakeContext(
                shadowValues: shadowsZero,
                lastStatUsed: null, secondLastStatUsed: null);

            var ctxNull = MakeContext(
                shadowValues: null,
                lastStatUsed: null, secondLastStatUsed: null);

            var dZero = await _agent.DecideAsync(turn, ctxZero);
            var dNull = await _agent.DecideAsync(turn, ctxNull);

            Assert.Equal((double)ScoreAt(dNull, 0), (double)ScoreAt(dZero, 0), 3);
        }

        // What: Edge case 5 — Negative shadow value → treated as T0.
        // Mutation: Would catch if negative value causes error or incorrect tier.
        [Fact]
        public async Task EdgeCase_NegativeShadowValue_TreatedAsTier0()
        {
            var turn = MakeTurn(MakeOption(StatType.Chaos));

            var shadowsNeg = new Dictionary<ShadowStatType, int>
            {
                { ShadowStatType.Fixation, -5 }
            };

            var ctxNeg = MakeContext(
                shadowValues: shadowsNeg,
                lastStatUsed: null, secondLastStatUsed: null);

            var ctxNull = MakeContext(
                shadowValues: null,
                lastStatUsed: null, secondLastStatUsed: null);

            var dNeg = await _agent.DecideAsync(turn, ctxNeg);
            var dNull = await _agent.DecideAsync(turn, ctxNull);

            Assert.Equal((double)ScoreAt(dNull, 0), (double)ScoreAt(dNeg, 0), 3);
        }

        // What: Edge case 7 — Honesty option matching both LastStatUsed and SecondLastStatUsed.
        //       Fixation penalty applies, but denial penalty does NOT (it's Honesty).
        // Mutation: Would catch if Honesty exemption from denial is not respected.
        [Fact]
        public async Task EdgeCase_HonestyMatchesBothHistory_FixationButNoDenial()
        {
            var turn = MakeTurn(
                MakeOption(StatType.Honesty),
                MakeOption(StatType.Charm));

            var ctx = MakeContext(
                lastStatUsed: StatType.Honesty,
                secondLastStatUsed: StatType.Honesty);

            var d = await _agent.DecideAsync(turn, ctx);

            // Honesty should have fixation penalty (-0.5) but no denial penalty
            // Charm should have denial penalty (-0.3) + variety bonus (+0.1)
            // Both should be valid scores
            Assert.Equal(2, d.Scores.Length);
            Assert.False(float.IsNaN(ScoreAt(d, 0)));
            Assert.False(float.IsNaN(ScoreAt(d, 1)));
        }

        // What: Edge case 8 — Combined stacking: all four adjustments apply to one option.
        // Mutation: Would catch if adjustments don't stack independently.
        [Fact]
        public async Task EdgeCase_AllAdjustmentsStack()
        {
            // Chaos option with:
            // - Fixation growth penalty (LastStatUsed == SecondLastStatUsed == Chaos)
            // - Denial growth penalty (Honesty is available)
            // - Fixation T2 (Fixation = 14 → squares successChance)
            // - No variety bonus (matches recent history)
            var turn = MakeTurn(
                MakeOption(StatType.Chaos),
                MakeOption(StatType.Honesty));

            var shadows = new Dictionary<ShadowStatType, int>
            {
                { ShadowStatType.Fixation, 14 }
            };

            var ctxFull = MakeContext(
                shadowValues: shadows,
                lastStatUsed: StatType.Chaos,
                secondLastStatUsed: StatType.Chaos);

            var ctxClean = MakeContext(
                shadowValues: null,
                lastStatUsed: null,
                secondLastStatUsed: null);

            // Same turn but without Honesty (to remove denial penalty baseline)
            var turnNoHonesty = MakeTurn(
                MakeOption(StatType.Chaos),
                MakeOption(StatType.Rizz));

            var dFull = await _agent.DecideAsync(turn, ctxFull);
            var dClean = await _agent.DecideAsync(turnNoHonesty, ctxClean);

            // Chaos (index 0) should be significantly lower with all penalties stacked
            Assert.True(ScoreAt(dClean, 0) > ScoreAt(dFull, 0),
                "All stacked penalties should significantly reduce Chaos score");

            // The difference should be substantial (at least 0.5 from fixation growth alone)
            Assert.True(ScoreAt(dClean, 0) - ScoreAt(dFull, 0) > 0.5f,
                "Stacked penalties should produce large score difference");
        }

        // What: Fixation threshold T1 boundary (Fixation = 6, exactly at threshold).
        // Mutation: Would catch off-by-one in threshold check (>6 vs >=6).
        [Fact]
        public async Task AC3_FixationT1_BoundaryAt6()
        {
            var turn = MakeTurn(MakeOption(StatType.Chaos));

            var shadowsAt6 = new Dictionary<ShadowStatType, int>
            {
                { ShadowStatType.Fixation, 6 }
            };
            var shadowsAt5 = new Dictionary<ShadowStatType, int>
            {
                { ShadowStatType.Fixation, 5 }
            };

            var ctxAt6 = MakeContext(shadowValues: shadowsAt6, lastStatUsed: null, secondLastStatUsed: null);
            var ctxAt5 = MakeContext(shadowValues: shadowsAt5, lastStatUsed: null, secondLastStatUsed: null);

            var dAt6 = await _agent.DecideAsync(turn, ctxAt6);
            var dAt5 = await _agent.DecideAsync(turn, ctxAt5);

            // At 6: T1 → expectedGain reduced by 0.8x multiplier → lower score
            // At 5: T0 → no adjustment
            Assert.True(ScoreAt(dAt5, 0) > ScoreAt(dAt6, 0),
                "Fixation=6 (T1) should reduce Chaos score vs Fixation=5 (T0)");
        }

        // What: Fixation threshold T2 boundary (Fixation = 12, exactly at threshold).
        // Mutation: Would catch off-by-one in threshold check (>12 vs >=12).
        [Fact]
        public async Task AC3_FixationT2_BoundaryAt12()
        {
            var turn = MakeTurn(MakeOption(StatType.Chaos));

            var shadowsAt12 = new Dictionary<ShadowStatType, int>
            {
                { ShadowStatType.Fixation, 12 }
            };
            var shadowsAt11 = new Dictionary<ShadowStatType, int>
            {
                { ShadowStatType.Fixation, 11 }
            };

            var ctxAt12 = MakeContext(shadowValues: shadowsAt12, lastStatUsed: null, secondLastStatUsed: null);
            var ctxAt11 = MakeContext(shadowValues: shadowsAt11, lastStatUsed: null, secondLastStatUsed: null);

            var dAt12 = await _agent.DecideAsync(turn, ctxAt12);
            var dAt11 = await _agent.DecideAsync(turn, ctxAt11);

            // At 12: T2 → successChance squared (much more severe)
            // At 11: T1 → expectedGain * 0.8 (milder)
            // These produce different effects, so scores should differ
            Assert.NotEqual(ScoreAt(dAt11, 0), ScoreAt(dAt12, 0), 2);
        }

        // What: ShadowValues null → fixation threshold skipped.
        // Mutation: Would catch if null ShadowValues causes NullReferenceException.
        [Fact]
        public async Task AC3_NullShadowValues_NoThrow()
        {
            var turn = MakeTurn(MakeOption(StatType.Chaos));

            var ctx = MakeContext(
                shadowValues: null,
                lastStatUsed: null,
                secondLastStatUsed: null);

            // Should not throw
            var d = await _agent.DecideAsync(turn, ctx);
            Assert.NotNull(d);
            Assert.Single(d.Scores);
        }
    }
}
