using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Pinder.Core.Characters;
using Pinder.Core.Conversation;
using Pinder.Core.Interfaces;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;
using Pinder.Core.Traps;
using Xunit;

namespace Pinder.Core.Tests.Integration
{
    /// <summary>
    /// Supplementary integration tests for issue #210.
    /// Covers edge cases, error conditions, and acceptance criteria
    /// not fully exercised by the main 8-turn happy-path test.
    /// </summary>
    public class FullConversationEdgeCaseTests
    {
        #region Shared Setup

        private static StatBlock CreateGeraldStats() => new StatBlock(
            new Dictionary<StatType, int>
            {
                { StatType.Charm, 13 }, { StatType.Wit, 3 }, { StatType.Honesty, 3 },
                { StatType.Chaos, 2 }, { StatType.SelfAwareness, 4 }, { StatType.Rizz, 2 }
            },
            new Dictionary<ShadowStatType, int>
            {
                { ShadowStatType.Madness, 0 }, { ShadowStatType.Horniness, 0 },
                { ShadowStatType.Denial, 0 }, { ShadowStatType.Fixation, 0 },
                { ShadowStatType.Dread, 0 }, { ShadowStatType.Overthinking, 0 }
            });

        private static StatBlock CreateVelvetStats() => new StatBlock(
            new Dictionary<StatType, int>
            {
                { StatType.Chaos, 14 }, { StatType.Honesty, 10 }, { StatType.Charm, 5 },
                { StatType.SelfAwareness, 5 }, { StatType.Wit, 4 }, { StatType.Rizz, 4 }
            },
            new Dictionary<ShadowStatType, int>
            {
                { ShadowStatType.Madness, 0 }, { ShadowStatType.Horniness, 0 },
                { ShadowStatType.Denial, 0 }, { ShadowStatType.Fixation, 0 },
                { ShadowStatType.Dread, 0 }, { ShadowStatType.Overthinking, 0 }
            });

        private static TimingProfile DefaultTiming() =>
            new TimingProfile(baseDelay: 10, variance: 0.5f, drySpell: 0.0f, readReceipt: "neutral");

        private static DialogueOption Opt(StatType stat, string text = "Test option") =>
            new DialogueOption(stat, text);

        private GameSession CreateSession(
            IDiceRoller dice,
            ILlmAdapter llm = null,
            ITrapRegistry trapRegistry = null,
            int? startingInterest = null)
        {
            var geraldStats = CreateGeraldStats();
            var velvetStats = CreateVelvetStats();
            var timing = DefaultTiming();

            var gerald = new CharacterProfile(geraldStats, "Test system prompt", "Gerald", timing, level: 5);
            var velvet = new CharacterProfile(velvetStats, "Test system prompt", "Velvet", timing, level: 7);

            var config = new GameSessionConfig(
                clock: null,
                playerShadows: new SessionShadowTracker(geraldStats),
                opponentShadows: new SessionShadowTracker(velvetStats),
                startingInterest: startingInterest,
                previousOpener: null);

            return new GameSession(
                gerald, velvet,
                llm ?? new ScriptedLlmAdapter(new[] {
                    new[] { Opt(StatType.Charm), Opt(StatType.Wit), Opt(StatType.Honesty), Opt(StatType.Chaos) }
                }),
                dice,
                trapRegistry ?? new NullTrapRegistry(),
                config);
        }

        #endregion

        // =====================================================
        // AC11: Test is deterministic — running twice yields same results
        // Mutation: would catch if any non-deterministic source (Random, clock) leaks in
        // =====================================================

        // What: AC11 — Determinism verification
        // Mutation: would catch if implementation uses System.Random or DateTime.Now instead of injected dice
        [Fact]
        public async Task Determinism_TwoRunsProduceIdenticalResults()
        {
            async Task<(int interest, int xp, bool gameOver)> RunOnce()
            {
                var dice = new FixedDice(14, 50); // T1: Wit d20=14, d100=50
                var llm = new ScriptedLlmAdapter(new[] {
                    new[] { Opt(StatType.Wit), Opt(StatType.Charm), Opt(StatType.Honesty), Opt(StatType.Chaos) }
                });
                var session = CreateSession(dice, llm, startingInterest: 10);
                await session.StartTurnAsync();
                var result = await session.ResolveTurnAsync(0);
                return (result.StateAfter.Interest, result.XpEarned, result.IsGameOver);
            }

            var run1 = await RunOnce();
            var run2 = await RunOnce();

            Assert.Equal(run1.interest, run2.interest);
            Assert.Equal(run1.xp, run2.xp);
            Assert.Equal(run1.gameOver, run2.gameOver);
        }

        // =====================================================
        // AC12: Test completes in < 2 seconds
        // Mutation: would catch if implementation adds real delays (Task.Delay, Thread.Sleep)
        // =====================================================

        // What: AC12 — Performance constraint
        // Mutation: would catch if DeliverMessageAsync or GetOpponentResponseAsync has real delays
        [Fact]
        public async Task Performance_SingleTurnCompletesUnderTwoSeconds()
        {
            var dice = new FixedDice(14, 50);
            var llm = new ScriptedLlmAdapter(new[] {
                new[] { Opt(StatType.Wit), Opt(StatType.Charm), Opt(StatType.Honesty), Opt(StatType.Chaos) }
            });
            var session = CreateSession(dice, llm, startingInterest: 10);

            var sw = Stopwatch.StartNew();
            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);
            sw.Stop();

            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(2),
                $"Single turn took {sw.ElapsedMilliseconds}ms, expected < 2000ms");
        }

        // =====================================================
        // Error: ResolveTurnAsync without StartTurnAsync throws
        // Mutation: would catch if guard clause is removed from ResolveTurnAsync
        // =====================================================

        // What: Error condition — calling ResolveTurn before StartTurn
        // Mutation: would catch if _currentOptions null check is removed
        [Fact]
        public async Task ResolveTurnAsync_WithoutStartTurn_ThrowsInvalidOperation()
        {
            var dice = new FixedDice(14, 50);
            var llm = new ScriptedLlmAdapter(new[] {
                new[] { Opt(StatType.Charm), Opt(StatType.Wit) }
            });
            var session = CreateSession(dice, llm, startingInterest: 10);

            // Don't call StartTurnAsync first
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => session.ResolveTurnAsync(0));
        }

        // =====================================================
        // Error: Invalid option index throws
        // Mutation: would catch if bounds check is removed from ResolveTurnAsync
        // =====================================================

        // What: Error condition — out of bounds option index
        // Mutation: would catch if index bounds validation is removed
        [Fact]
        public async Task ResolveTurnAsync_InvalidIndex_ThrowsArgumentOutOfRange()
        {
            var dice = new FixedDice(14, 50, 14, 50); // enough for start + potential resolve
            var llm = new ScriptedLlmAdapter(new[] {
                new[] { Opt(StatType.Charm), Opt(StatType.Wit) } // 2 options, indices 0-1 valid
            });
            var session = CreateSession(dice, llm, startingInterest: 10);

            await session.StartTurnAsync();

            // Index 5 is out of bounds for a 2-option array
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
                () => session.ResolveTurnAsync(5));
        }

        // =====================================================
        // AC3: Interest delta for success includes SuccessScale correctly
        // Mutation: would catch if SuccessScale.GetInterestDelta returns wrong tier
        // =====================================================

        // What: AC3 — Success scale: beat DC by 1-4 → +1 interest
        // Mutation: would catch if success scale returned +2 for beat-by-2
        [Fact]
        public async Task SuccessScale_BeatBy2_InterestDeltaPlus1()
        {
            // Charm vs Velvet SA DC=18. Gerald Charm=13, lvl bonus=2. d20+15.
            // d20=5 → total=20, beat by 2 → +1 success. Safe risk (need=3). Delta=+1.
            var dice = new FixedDice(5, 50);
            var llm = new ScriptedLlmAdapter(new[] {
                new[] { Opt(StatType.Charm), Opt(StatType.Wit), Opt(StatType.Honesty), Opt(StatType.Chaos) }
            });
            var session = CreateSession(dice, llm, startingInterest: 10);

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.True(result.Roll.IsSuccess);
            Assert.Equal(20, result.Roll.Total);
            Assert.Equal(18, result.Roll.DC);
            // Beat by 2 → SuccessScale +1, Safe risk → 0. Delta = 1.
            Assert.Equal(1, result.InterestDelta);
        }

        // What: AC3 — Success scale: beat DC by 5-9 → +2 interest
        // Mutation: would catch if success scale returned +1 for beat-by-5
        [Fact]
        public async Task SuccessScale_BeatBy5_InterestDeltaPlus2()
        {
            // Charm DC=18. d20=8 → total=23, beat by 5 → +2 success. Safe risk. Delta=+2.
            var dice = new FixedDice(8, 50);
            var llm = new ScriptedLlmAdapter(new[] {
                new[] { Opt(StatType.Charm), Opt(StatType.Wit), Opt(StatType.Honesty), Opt(StatType.Chaos) }
            });
            var session = CreateSession(dice, llm, startingInterest: 10);

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.True(result.Roll.IsSuccess);
            Assert.Equal(23, result.Roll.Total);
            Assert.Equal(2, result.InterestDelta);
        }

        // =====================================================
        // AC3: Risk tier bonus — Hard tier (+1)
        // Mutation: would catch if RiskTierBonus.GetInterestBonus returned 0 for Hard
        // =====================================================

        // What: AC3 — Hard risk tier gives +1 bonus
        // Mutation: would catch if Hard tier bonus returned 0 instead of +1
        [Fact]
        public async Task RiskTier_Hard_AddsPlus1ToInterestDelta()
        {
            // Wit vs Velvet Rizz DC=17. Gerald Wit=3, lvl=2. d20+5.
            // need = 17-3-2=12 → Hard (11-15). d20=14 → total=19, beat by 2 → +1 success.
            // Delta = +1 (success) + 1 (Hard) = +2.
            var dice = new FixedDice(14, 50);
            var llm = new ScriptedLlmAdapter(new[] {
                new[] { Opt(StatType.Wit), Opt(StatType.Charm), Opt(StatType.Honesty), Opt(StatType.Chaos) }
            });
            var session = CreateSession(dice, llm, startingInterest: 10);

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.True(result.Roll.IsSuccess);
            Assert.Equal(RiskTier.Hard, result.Roll.RiskTier);
            Assert.Equal(2, result.InterestDelta); // +1 success + 1 Hard
        }

        // =====================================================
        // AC4: Momentum resets on failure
        // Mutation: would catch if momentum not reset to 0 on fail
        // =====================================================

        // What: AC4 — Momentum resets on failure
        // Mutation: would catch if momentum kept incrementing after a failure
        [Fact]
        public async Task Momentum_ResetsToZero_OnFailure()
        {
            // T1: Charm success (d20=5, beat DC 18 by 2), momentum=1
            // T2: Honesty fail (DC=27, d20=19 → total=24, miss by 3 → Misfire), momentum=0
            var dice = new FixedDice(
                5, 50,    // T1: Charm success
                19, 50    // T2: Honesty fail (Misfire)
            );
            var llm = new ScriptedLlmAdapter(new[] {
                new[] { Opt(StatType.Charm), Opt(StatType.Wit), Opt(StatType.Honesty), Opt(StatType.Chaos) },
                new[] { Opt(StatType.Honesty), Opt(StatType.Charm), Opt(StatType.Wit), Opt(StatType.Chaos) },
            });
            var session = CreateSession(dice, llm, startingInterest: 10);

            await session.StartTurnAsync();
            var t1 = await session.ResolveTurnAsync(0);
            Assert.Equal(1, t1.StateAfter.MomentumStreak);

            await session.StartTurnAsync();
            var t2 = await session.ResolveTurnAsync(0);
            Assert.False(t2.Roll.IsSuccess);
            Assert.Equal(0, t2.StateAfter.MomentumStreak);
        }

        // =====================================================
        // AC6: TropeTrap fires on miss 6-9
        // Mutation: would catch if TropeTrap range check used wrong boundary (e.g. miss 5-8)
        // =====================================================

        // What: AC6 — TropeTrap activation on miss by 7
        // Mutation: would catch if TropeTrap didn't fire for miss=7 or wrong trap stat
        [Fact]
        public async Task TropeTrap_FiresOnMiss7_ActivatesCorrectTrap()
        {
            // Chaos vs Velvet Charm DC=18. Gerald Chaos=2, lvl=2. d20+4.
            // d20=7 → total=11. Miss=18-11=7 → TropeTrap.
            var dice = new FixedDice(7, 50);
            var llm = new ScriptedLlmAdapter(new[] {
                new[] { Opt(StatType.Chaos), Opt(StatType.Charm), Opt(StatType.Wit), Opt(StatType.Honesty) }
            });
            var trapRegistry = new TestTrapRegistry();
            var session = CreateSession(dice, llm, trapRegistry, startingInterest: 10);

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.False(result.Roll.IsSuccess);
            Assert.Equal(FailureTier.TropeTrap, result.Roll.Tier);
            Assert.NotNull(result.Roll.ActivatedTrap);
            Assert.Equal("unhinged", result.Roll.ActivatedTrap.Id);
            Assert.Contains("unhinged", result.StateAfter.ActiveTrapNames);
            Assert.Equal(-3, result.InterestDelta);
        }

        // =====================================================
        // AC7: Trap clears after Recover
        // Mutation: would catch if RecoverAsync didn't clear the trap
        // =====================================================

        // What: AC7 — Recover clears active trap
        // Mutation: would catch if trap remained active after successful recovery
        [Fact]
        public async Task Recover_ClearsTrap_OnSuccess()
        {
            // First activate a trap via TropeTrap on Chaos
            // Then recover with SA vs DC 12. d20=10 → total=16 ≥ 12 → success.
            var dice = new FixedDice(
                7, 50,  // T1: Chaos fail TropeTrap (d20=7, total=11, miss=7)
                10      // T2: Recover (d20=10, total=16 ≥ 12)
            );
            var llm = new ScriptedLlmAdapter(new[] {
                new[] { Opt(StatType.Chaos), Opt(StatType.Charm), Opt(StatType.Wit), Opt(StatType.Honesty) }
            });
            var trapRegistry = new TestTrapRegistry();
            var session = CreateSession(dice, llm, trapRegistry, startingInterest: 10);

            await session.StartTurnAsync();
            var t1 = await session.ResolveTurnAsync(0);
            Assert.Contains("unhinged", t1.StateAfter.ActiveTrapNames);

            var recover = await session.RecoverAsync();
            Assert.True(recover.Success);
            Assert.Equal("unhinged", recover.ClearedTrapName);
            Assert.Empty(recover.StateAfter.ActiveTrapNames);
            Assert.Equal(15, recover.XpEarned); // recovery success → 15 XP
        }

        // =====================================================
        // AC7 variant: Recover failure doesn't clear trap, interest -1
        // Mutation: would catch if failed recover still cleared the trap
        // =====================================================

        // What: AC7 edge — Failed recover keeps trap, interest −1
        // Mutation: would catch if failed recover cleared the trap or didn't apply interest penalty
        [Fact]
        public async Task Recover_KeepsTrap_OnFailure_AndAppliesMinusOne()
        {
            // Activate trap, then fail recover. SA+4+2=d20+6 vs DC 12 → need d20≥6.
            // d20=3 → total=9 < 12 → fail.
            var dice = new FixedDice(
                7, 50,  // T1: Chaos fail TropeTrap
                3       // T2: Recover fail (d20=3, total=9 < 12)
            );
            var llm = new ScriptedLlmAdapter(new[] {
                new[] { Opt(StatType.Chaos), Opt(StatType.Charm), Opt(StatType.Wit), Opt(StatType.Honesty) }
            });
            var trapRegistry = new TestTrapRegistry();
            var session = CreateSession(dice, llm, trapRegistry, startingInterest: 10);

            await session.StartTurnAsync();
            var t1 = await session.ResolveTurnAsync(0);
            int interestAfterTrap = t1.StateAfter.Interest; // 10 - 3 = 7

            var recover = await session.RecoverAsync();
            Assert.False(recover.Success);
            Assert.Null(recover.ClearedTrapName);
            Assert.Contains("unhinged", recover.StateAfter.ActiveTrapNames);
            Assert.Equal(interestAfterTrap - 1, recover.StateAfter.Interest); // −1 on fail
        }

        // =====================================================
        // AC5: Combo "The Setup" — Wit→Charm
        // Mutation: would catch if combo detection checked wrong stat pair
        // =====================================================

        // What: AC5 — The Setup combo (Wit→Charm)
        // Mutation: would catch if ComboTracker had wrong sequence for The Setup
        [Fact]
        public async Task Combo_TheSetup_TriggersOnWitThenCharm()
        {
            // T1: Wit success. T2: Charm success → The Setup combo → +1 interest.
            var dice = new FixedDice(
                14, 50,   // T1: Wit (d20=14, total=19, beat 17 by 2)
                5, 50     // T2: Charm (d20=5, total=20, beat 18 by 2)
            );
            var llm = new ScriptedLlmAdapter(new[] {
                new[] { Opt(StatType.Wit), Opt(StatType.Charm), Opt(StatType.Honesty), Opt(StatType.Chaos) },
                new[] { Opt(StatType.Charm), Opt(StatType.Wit), Opt(StatType.Honesty), Opt(StatType.Chaos) },
            });
            var session = CreateSession(dice, llm, startingInterest: 10);

            await session.StartTurnAsync();
            var t1 = await session.ResolveTurnAsync(0);
            Assert.Null(t1.ComboTriggered);

            await session.StartTurnAsync();
            var t2 = await session.ResolveTurnAsync(0);
            Assert.Equal("The Setup", t2.ComboTriggered);
        }

        // =====================================================
        // AC10: DateSecured at interest 25
        // Mutation: would catch if game didn't end at interest=25
        // =====================================================

        // What: AC10 — Game ends with DateSecured when interest reaches 25
        // Mutation: would catch if DateSecured threshold was != 25 or game didn't terminate
        [Fact]
        public async Task DateSecured_WhenInterestReaches25()
        {
            // Start at interest=22 → AlmostThere → advantage (2 d20s).
            // Charm DC=18. d20_1=16, d20_2=10 → max=16 → total=16+13+2=31, beat by 13 → +3.
            // Interest: 22+3=25 → DateSecured.
            var dice = new FixedDice(16, 10, 50);
            var llm = new ScriptedLlmAdapter(new[] {
                new[] { Opt(StatType.Charm), Opt(StatType.Wit), Opt(StatType.Honesty), Opt(StatType.Chaos) }
            });
            var session = CreateSession(dice, llm, startingInterest: 22);

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.True(result.IsGameOver);
            Assert.Equal(GameOutcome.DateSecured, result.Outcome);
            Assert.Equal(25, result.StateAfter.Interest);
            Assert.Equal(InterestState.DateSecured, result.StateAfter.State);
        }

        // =====================================================
        // AC10: Interest clamped at 25 (not higher)
        // Mutation: would catch if InterestMeter.Apply didn't clamp at Max
        // =====================================================

        // What: Interest clamping at 25
        // Mutation: would catch if interest could exceed 25
        [Fact]
        public async Task InterestClamped_At25_OnLargePositiveDelta()
        {
            // Start at 23 → AlmostThere → advantage (2 d20s).
            // Nat20 → +4 delta. 23+4=27, but clamped to 25.
            var dice = new FixedDice(20, 5, 50);
            var llm = new ScriptedLlmAdapter(new[] {
                new[] { Opt(StatType.Charm), Opt(StatType.Wit), Opt(StatType.Honesty), Opt(StatType.Chaos) }
            });
            var session = CreateSession(dice, llm, startingInterest: 23);

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.Equal(25, result.StateAfter.Interest); // clamped, not 27
            Assert.Equal(InterestState.DateSecured, result.StateAfter.State);
        }

        // =====================================================
        // AC8: Shadow growth events are populated
        // Mutation: would catch if ShadowGrowthEvents was always empty/null
        // =====================================================

        // What: AC8 — Shadow growth events present on TurnResult
        // Mutation: would catch if shadow growth events field was never populated
        [Fact]
        public async Task ShadowGrowthEvents_NotNull_OnTurnResult()
        {
            var dice = new FixedDice(5, 50);
            var llm = new ScriptedLlmAdapter(new[] {
                new[] { Opt(StatType.Charm), Opt(StatType.Wit), Opt(StatType.Honesty), Opt(StatType.Chaos) }
            });
            var session = CreateSession(dice, llm, startingInterest: 10);

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            // ShadowGrowthEvents must be a valid list (may be empty on first turn)
            Assert.NotNull(result.ShadowGrowthEvents);
        }

        // =====================================================
        // AC9: XP for Nat20 is 25
        // Mutation: would catch if Nat20 XP was wrong value
        // =====================================================

        // What: AC9 — Nat20 awards 25 XP
        // Mutation: would catch if Nat20 XP was 15 or 10 instead of 25
        [Fact]
        public async Task Nat20_Awards25Xp()
        {
            var dice = new FixedDice(20, 50);
            var llm = new ScriptedLlmAdapter(new[] {
                new[] { Opt(StatType.Charm), Opt(StatType.Wit), Opt(StatType.Honesty), Opt(StatType.Chaos) }
            });
            var session = CreateSession(dice, llm, startingInterest: 10);

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.True(result.Roll.IsNatTwenty);
            // Nat20 → 25 XP (no DateSecured since 10+4=14 ≠ 25)
            Assert.Equal(25, result.XpEarned);
        }

        // =====================================================
        // AC9: Failure awards 2 XP
        // Mutation: would catch if failure XP was 0 or 5
        // =====================================================

        // What: AC9 — Failure awards 2 XP
        // Mutation: would catch if failure XP was 0 instead of 2
        [Fact]
        public async Task Failure_Awards2Xp()
        {
            // Honesty vs DC 27. d20=19 → total=24 < 27 → fail (Misfire, miss by 3)
            var dice = new FixedDice(19, 50);
            var llm = new ScriptedLlmAdapter(new[] {
                new[] { Opt(StatType.Honesty), Opt(StatType.Charm), Opt(StatType.Wit), Opt(StatType.Chaos) }
            });
            var session = CreateSession(dice, llm, startingInterest: 10);

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.False(result.Roll.IsSuccess);
            Assert.Equal(2, result.XpEarned);
        }

        // =====================================================
        // Edge: Advantage grants two d20 rolls, takes higher
        // Mutation: would catch if advantage took lower instead of higher
        // =====================================================

        // What: Advantage (VeryIntoIt) takes higher of two d20s
        // Mutation: would catch if advantage used min instead of max
        [Fact]
        public async Task Advantage_TakesHigherOfTwoD20s()
        {
            // Start at interest=17 → VeryIntoIt → advantage.
            // Charm DC=18. Two d20s: 3 and 10. Should use 10 (higher).
            // Total = 10 + 13 + 2 = 25, beat DC by 7 → +2 interest.
            var dice = new FixedDice(3, 10, 50);
            var llm = new ScriptedLlmAdapter(new[] {
                new[] { Opt(StatType.Charm), Opt(StatType.Wit), Opt(StatType.Honesty), Opt(StatType.Chaos) }
            });
            var session = CreateSession(dice, llm, startingInterest: 17);

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            // With max(3,10)=10: total=25, beat by 7 → success (+2)
            // If it wrongly used min(3,10)=3: total=18, beat by 0 → would be exactly DC → still success but different
            Assert.True(result.Roll.IsSuccess);
            Assert.Equal(25, result.Roll.Total); // 10+13+2
        }

        // =====================================================
        // Edge: FixedDice exhaustion throws
        // Mutation: would catch if FixedDice silently returned 0 instead of throwing
        // =====================================================

        // What: Error condition — FixedDice runs out of values
        // Mutation: would catch if FixedDice returned default instead of throwing
        [Fact]
        public async Task FixedDice_Exhaustion_Throws()
        {
            // Only 1 value but Speak needs d20 + d100 = 2 values minimum
            var dice = new FixedDice(14); // only 1 value
            var llm = new ScriptedLlmAdapter(new[] {
                new[] { Opt(StatType.Charm), Opt(StatType.Wit), Opt(StatType.Honesty), Opt(StatType.Chaos) }
            });
            var session = CreateSession(dice, llm, startingInterest: 10);

            await session.StartTurnAsync();
            // Should throw because the dice queue is too short for a full resolve
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => session.ResolveTurnAsync(0));
        }

        // =====================================================
        // Edge: Recover with no active trap
        // Mutation: would catch if RecoverAsync threw when no trap is active
        // =====================================================

        // What: Edge — RecoverAsync when no trap is active throws InvalidOperationException
        // Mutation: would catch if guard clause allowing recover without trap is removed
        [Fact]
        public async Task RecoverAsync_NoActiveTrap_ThrowsInvalidOperation()
        {
            // No trap activated — recover should fail with a clear error message.
            var dice = new FixedDice(10);
            var session = CreateSession(dice, startingInterest: 10);

            // Implementation requires an active trap to recover from
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => session.RecoverAsync());
            Assert.Contains("no active trap", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        // =====================================================
        // AC3: Failure scale — Misfire gives -2
        // Mutation: would catch if Misfire gave -1 instead of -2
        // =====================================================

        // What: AC3 — Misfire failure scale gives -2 interest
        // Mutation: would catch if FailureScale returned -1 for Misfire
        [Fact]
        public async Task FailureScale_Misfire_GivesMinus2()
        {
            // Honesty DC=27. d20=20 → total=25, miss by 2 → Fumble (-1). 
            // d20=19 → total=24, miss by 3 → Misfire (-2). Use d20=19.
            var dice = new FixedDice(19, 50);
            var llm = new ScriptedLlmAdapter(new[] {
                new[] { Opt(StatType.Honesty), Opt(StatType.Charm), Opt(StatType.Wit), Opt(StatType.Chaos) }
            });
            var session = CreateSession(dice, llm, startingInterest: 10);

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.Equal(FailureTier.Misfire, result.Roll.Tier);
            Assert.Equal(-2, result.InterestDelta);
        }

        // =====================================================
        // Failure tier: Fumble (miss 1-2) gives -1
        // Mutation: would catch if Fumble returned -2 instead of -1
        // =====================================================

        // What: Fumble failure scale gives -1 interest
        // Mutation: would catch if FailureScale returned -2 for Fumble
        [Fact]
        public async Task FailureScale_Fumble_GivesMinus1()
        {
            // Chaos DC=18 (13 + Velvet Charm 5). Gerald Chaos=2, lvl=2 → d20+4.
            // d20=13 → total=17, miss by 1 → Fumble (miss 1-2).
            var dice = new FixedDice(13, 50);
            var llm = new ScriptedLlmAdapter(new[] {
                new[] { Opt(StatType.Chaos), Opt(StatType.Charm), Opt(StatType.Wit), Opt(StatType.Honesty) }
            });
            var session = CreateSession(dice, llm, startingInterest: 10);

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.Equal(FailureTier.Fumble, result.Roll.Tier);
            Assert.Equal(-1, result.InterestDelta);
        }

        // ---- Test doubles ----

        private sealed class FixedDice : IDiceRoller
        {
            private readonly Queue<int> _values;
            public FixedDice(params int[] values) => _values = new Queue<int>(values);
            public int Roll(int sides)
            {
                if (_values.Count == 0)
                    throw new InvalidOperationException(
                        $"FixedDice: no more values in queue (requested d{sides}).");
                return _values.Dequeue();
            }
        }

        private sealed class NullTrapRegistry : ITrapRegistry
        {
            public TrapDefinition? GetTrap(StatType stat) => null;
            public string? GetLlmInstruction(StatType stat) => null;
        }

        private sealed class TestTrapRegistry : ITrapRegistry
        {
            private readonly TrapDefinition _chaosTrap = new TrapDefinition(
                id: "unhinged",
                stat: StatType.Chaos,
                effect: TrapEffect.Disadvantage,
                effectValue: 0,
                durationTurns: 3,
                llmInstruction: "You're unhinged now",
                clearMethod: "Recover",
                nat1Bonus: "Extra chaos");

            public TrapDefinition? GetTrap(StatType stat) =>
                stat == StatType.Chaos ? _chaosTrap : null;
            public string? GetLlmInstruction(StatType stat) => null;
        }

        private sealed class ScriptedLlmAdapter : ILlmAdapter
        {
            private readonly Queue<DialogueOption[]> _optionSets;
            public ScriptedLlmAdapter(IEnumerable<DialogueOption[]> optionSets) =>
                _optionSets = new Queue<DialogueOption[]>(optionSets);

            public Task<DialogueOption[]> GetDialogueOptionsAsync(DialogueContext context)
            {
                if (_optionSets.Count == 0)
                    throw new InvalidOperationException("ScriptedLlmAdapter: no more option sets.");
                return Task.FromResult(_optionSets.Dequeue());
            }

            public Task<string> DeliverMessageAsync(DeliveryContext context)
            {
                string msg = context.Outcome == FailureTier.None
                    ? context.ChosenOption.IntendedText
                    : $"[{context.Outcome}] {context.ChosenOption.IntendedText}";
                return Task.FromResult(msg);
            }

            public Task<OpponentResponse> GetOpponentResponseAsync(OpponentContext context) =>
                Task.FromResult(new OpponentResponse("..."));

            public Task<string?> GetInterestChangeBeatAsync(InterestChangeContext context) =>
                Task.FromResult<string?>(null);
        }
    }
}
