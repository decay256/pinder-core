using System;
using System.Collections.Generic;
using System.Linq;
using Pinder.Core.Conversation;
using Pinder.Core.Data;
using Pinder.Core.Interfaces;
using Pinder.Core.Stats;
using Xunit;

namespace Pinder.Core.Tests
{
    /// <summary>
    /// Spec-driven tests for OpponentTimingCalculator and JsonTimingRepository.
    /// Based on docs/specs/issue-53-spec.md — all assertions derived from spec only.
    /// </summary>
    public class OpponentTimingCalculatorSpecTests
    {
        // =====================================================================
        // Test helpers (deterministic dice implementations)
        // =====================================================================

        private sealed class FixedDice : IDiceRoller
        {
            private readonly int _value;
            public FixedDice(int value) => _value = value;
            public int Roll(int sides) => _value;
        }

        private sealed class SequenceDice : IDiceRoller
        {
            private readonly Queue<int> _values;

            public SequenceDice(params int[] values)
            {
                _values = new Queue<int>(values);
            }

            public int Roll(int sides)
            {
                if (_values.Count == 0)
                    throw new InvalidOperationException("SequenceDice exhausted.");
                return _values.Dequeue();
            }
        }

        private static TimingProfile MakeProfile(
            int baseDelay = 10, float variance = 0.0f,
            float drySpell = 0.0f, string receipt = "neutral")
            => new TimingProfile(baseDelay, variance, drySpell, receipt);

        private static Dictionary<ShadowStatType, int> NoShadows
            => new Dictionary<ShadowStatType, int>();

        // =====================================================================
        // AC-1: ComputeDelayMinutes exists with correct signature
        // =====================================================================

        // What: AC-1 — method exists and returns double (spec §2)
        // Mutation: would catch if method signature changed or was removed
        [Fact]
        public void AC1_ComputeDelayMinutes_Exists_ReturnsDouble()
        {
            double result = OpponentTimingCalculator.ComputeDelayMinutes(
                MakeProfile(), InterestState.Interested, NoShadows, new FixedDice(50));

            Assert.IsType<double>(result);
        }

        // What: AC-1 — accepts optional previousDelay parameter (spec §2)
        // Mutation: would catch if previousDelay parameter was removed
        [Fact]
        public void AC1_AcceptsOptionalPreviousDelay()
        {
            double result = OpponentTimingCalculator.ComputeDelayMinutes(
                MakeProfile(), InterestState.Interested, NoShadows, new FixedDice(50), previousDelay: 5.0);

            Assert.IsType<double>(result);
        }

        // =====================================================================
        // AC-2: Interest multipliers applied correctly per InterestState
        // =====================================================================

        // What: AC-2 — Bored multiplier is ×5.0 (spec §4, PO VC-59 fix)
        // Mutation: would catch if Bored multiplier was ×2.0 (old value) or any other
        [Fact]
        public void AC2_Bored_MultipliedBy5()
        {
            var result = OpponentTimingCalculator.ComputeDelayMinutes(
                MakeProfile(baseDelay: 10, variance: 0.0f),
                InterestState.Bored, NoShadows, new FixedDice(50));

            Assert.Equal(50.0, result, precision: 1);
        }

        // What: AC-2 — Interested multiplier is ×1.0 (spec §4)
        // Mutation: would catch if Interested multiplier was anything other than 1.0
        [Fact]
        public void AC2_Interested_MultipliedBy1()
        {
            var result = OpponentTimingCalculator.ComputeDelayMinutes(
                MakeProfile(baseDelay: 10, variance: 0.0f),
                InterestState.Interested, NoShadows, new FixedDice(50));

            Assert.Equal(10.0, result, precision: 1);
        }

        // What: AC-2 — VeryIntoIt multiplier is ×0.5 (spec §4)
        // Mutation: would catch if VeryIntoIt multiplier was ×1.0 or ×0.3
        [Fact]
        public void AC2_VeryIntoIt_MultipliedBy0_5()
        {
            var result = OpponentTimingCalculator.ComputeDelayMinutes(
                MakeProfile(baseDelay: 20, variance: 0.0f),
                InterestState.VeryIntoIt, NoShadows, new FixedDice(50));

            Assert.Equal(10.0, result, precision: 1);
        }

        // What: AC-2 — AlmostThere multiplier is ×0.3 (spec §4)
        // Mutation: would catch if AlmostThere multiplier was ×0.5 or ×1.0
        [Fact]
        public void AC2_AlmostThere_MultipliedBy0_3()
        {
            var result = OpponentTimingCalculator.ComputeDelayMinutes(
                MakeProfile(baseDelay: 100, variance: 0.0f),
                InterestState.AlmostThere, NoShadows, new FixedDice(50));

            Assert.Equal(30.0, result, precision: 1);
        }

        // =====================================================================
        // AC-3: No Lukewarm state — only valid v3.4 states work
        // =====================================================================

        // What: AC-3 — invalid enum value throws ArgumentOutOfRangeException (spec §6)
        // Mutation: would catch if invalid states were silently accepted
        [Fact]
        public void AC3_InvalidEnumValue_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                OpponentTimingCalculator.ComputeDelayMinutes(
                    MakeProfile(), (InterestState)99, NoShadows, new FixedDice(50)));
        }

        // What: AC-3 — all 6 valid InterestState values are handled without exception
        // Mutation: would catch if any valid state was accidentally excluded
        [Theory]
        [InlineData(InterestState.Unmatched)]
        [InlineData(InterestState.Bored)]
        [InlineData(InterestState.Interested)]
        [InlineData(InterestState.VeryIntoIt)]
        [InlineData(InterestState.AlmostThere)]
        [InlineData(InterestState.DateSecured)]
        public void AC3_AllValidStates_DoNotThrow(InterestState state)
        {
            var result = OpponentTimingCalculator.ComputeDelayMinutes(
                MakeProfile(baseDelay: 10, variance: 0.0f),
                state, NoShadows, new FixedDice(50));

            Assert.True(result >= 1.0);
        }

        // =====================================================================
        // AC-4: Shadow modifiers for Overthinking, Denial, Fixation, Madness
        // =====================================================================

        // --- Overthinking ---

        // What: AC-4 — Overthinking ≥ 6 adds +50% (spec §4)
        // Mutation: would catch if multiplier was ×2.0 instead of ×1.5
        [Fact]
        public void AC4_Overthinking_GTE6_Multiplies1_5()
        {
            var shadows = new Dictionary<ShadowStatType, int> { { ShadowStatType.Overthinking, 6 } };
            var result = OpponentTimingCalculator.ComputeDelayMinutes(
                MakeProfile(baseDelay: 10, variance: 0.0f),
                InterestState.Interested, shadows, new FixedDice(50));

            Assert.Equal(15.0, result, precision: 1);
        }

        // What: AC-4 — Overthinking < 6 has no effect (spec §4)
        // Mutation: would catch if threshold was < 6 instead of >= 6
        [Fact]
        public void AC4_Overthinking_Below6_NoEffect()
        {
            var shadows = new Dictionary<ShadowStatType, int> { { ShadowStatType.Overthinking, 5 } };
            var result = OpponentTimingCalculator.ComputeDelayMinutes(
                MakeProfile(baseDelay: 10, variance: 0.0f),
                InterestState.Interested, shadows, new FixedDice(50));

            Assert.Equal(10.0, result, precision: 1);
        }

        // What: AC-4 — Overthinking at exactly 6 is active (boundary, spec §4)
        // Mutation: would catch if threshold was > 6
        [Fact]
        public void AC4_Overthinking_Exactly6_IsActive()
        {
            var shadows = new Dictionary<ShadowStatType, int> { { ShadowStatType.Overthinking, 6 } };
            var result = OpponentTimingCalculator.ComputeDelayMinutes(
                MakeProfile(baseDelay: 20, variance: 0.0f),
                InterestState.Interested, shadows, new FixedDice(50));

            Assert.Equal(30.0, result, precision: 1);
        }

        // What: AC-4/Edge — very high shadow value produces same effect as 6 (spec §5)
        // Mutation: would catch if shadow value scaled beyond threshold
        [Fact]
        public void AC4_Overthinking_HighValue_SameEffectAs6()
        {
            var shadows6 = new Dictionary<ShadowStatType, int> { { ShadowStatType.Overthinking, 6 } };
            var shadows100 = new Dictionary<ShadowStatType, int> { { ShadowStatType.Overthinking, 100 } };

            var result6 = OpponentTimingCalculator.ComputeDelayMinutes(
                MakeProfile(baseDelay: 10, variance: 0.0f),
                InterestState.Interested, shadows6, new FixedDice(50));
            var result100 = OpponentTimingCalculator.ComputeDelayMinutes(
                MakeProfile(baseDelay: 10, variance: 0.0f),
                InterestState.Interested, shadows100, new FixedDice(50));

            Assert.Equal(result6, result100);
        }

        // --- Denial ---

        // What: AC-4 — Denial snaps to nearest 5-minute interval (spec §4)
        // Mutation: would catch if snap target was 10 instead of 5
        [Fact]
        public void AC4_Denial_SnapsToNearest5()
        {
            var shadows = new Dictionary<ShadowStatType, int> { { ShadowStatType.Denial, 7 } };
            // Base=7, Interested ×1.0 = 7.0, snap to 5.0
            var result = OpponentTimingCalculator.ComputeDelayMinutes(
                MakeProfile(baseDelay: 7, variance: 0.0f),
                InterestState.Interested, shadows, new FixedDice(50));

            Assert.Equal(5.0, result);
        }

        // What: AC-4 — Denial snaps 8 to 10 (rounds up at .5 boundary) (spec §4)
        // Mutation: would catch if rounding was always floor
        [Fact]
        public void AC4_Denial_SnapsUp_WhenCloserToHigher()
        {
            var shadows = new Dictionary<ShadowStatType, int> { { ShadowStatType.Denial, 6 } };
            // Base=8, snap → round(8/5)*5 = round(1.6)*5 = 2*5 = 10
            var result = OpponentTimingCalculator.ComputeDelayMinutes(
                MakeProfile(baseDelay: 8, variance: 0.0f),
                InterestState.Interested, shadows, new FixedDice(50));

            Assert.Equal(10.0, result);
        }

        // What: AC-4 — Denial snap to 0 floors to 5 (spec §4)
        // Mutation: would catch if 0 was returned instead of 5
        [Fact]
        public void AC4_Denial_ZeroSnap_FloorsTo5()
        {
            var shadows = new Dictionary<ShadowStatType, int> { { ShadowStatType.Denial, 6 } };
            // Base=1, AlmostThere ×0.3 = 0.3, snap → round(0.3/5)*5 = 0 → floor to 5
            var result = OpponentTimingCalculator.ComputeDelayMinutes(
                MakeProfile(baseDelay: 1, variance: 0.0f),
                InterestState.AlmostThere, shadows, new FixedDice(50));

            Assert.Equal(5.0, result);
        }

        // --- Fixation ---

        // What: AC-4 — Fixation ≥ 6 with previousDelay returns previousDelay (spec §4/§5)
        // Mutation: would catch if Fixation computed fresh instead of returning previous
        [Fact]
        public void AC4_Fixation_WithPreviousDelay_ReturnsPrevious()
        {
            var shadows = new Dictionary<ShadowStatType, int> { { ShadowStatType.Fixation, 6 } };
            var result = OpponentTimingCalculator.ComputeDelayMinutes(
                MakeProfile(baseDelay: 10, variance: 0.0f),
                InterestState.Interested, shadows, new FixedDice(50), previousDelay: 42.0);

            Assert.Equal(42.0, result);
        }

        // What: AC-4 — Fixation ≥ 6 with null previousDelay computes normally (spec §5)
        // Mutation: would catch if Fixation threw or returned 0 on first call
        [Fact]
        public void AC4_Fixation_NullPreviousDelay_ComputesNormally()
        {
            var shadows = new Dictionary<ShadowStatType, int> { { ShadowStatType.Fixation, 6 } };
            var result = OpponentTimingCalculator.ComputeDelayMinutes(
                MakeProfile(baseDelay: 10, variance: 0.0f),
                InterestState.Interested, shadows, new FixedDice(50), previousDelay: null);

            Assert.Equal(10.0, result, precision: 1);
        }

        // What: AC-4 — Fixation < 6 does not return previousDelay (spec §4)
        // Mutation: would catch if Fixation always used previousDelay regardless of threshold
        [Fact]
        public void AC4_Fixation_Below6_IgnoresPreviousDelay()
        {
            var shadows = new Dictionary<ShadowStatType, int> { { ShadowStatType.Fixation, 5 } };
            var result = OpponentTimingCalculator.ComputeDelayMinutes(
                MakeProfile(baseDelay: 10, variance: 0.0f),
                InterestState.Interested, shadows, new FixedDice(50), previousDelay: 42.0);

            // Should compute normally (10.0), not return 42.0
            Assert.Equal(10.0, result, precision: 1);
        }

        // What: AC-4 — Fixation overrides everything (applied last per spec §4 order)
        // Mutation: would catch if Fixation was applied before other modifiers
        [Fact]
        public void AC4_Fixation_OverridesOtherShadows()
        {
            var shadows = new Dictionary<ShadowStatType, int>
            {
                { ShadowStatType.Overthinking, 10 },
                { ShadowStatType.Denial, 10 },
                { ShadowStatType.Fixation, 6 }
            };
            var result = OpponentTimingCalculator.ComputeDelayMinutes(
                MakeProfile(baseDelay: 10, variance: 0.0f),
                InterestState.Interested, shadows, new FixedDice(50), previousDelay: 7.77);

            Assert.Equal(7.77, result);
        }

        // --- Madness ---

        // What: AC-4 — Madness ≥ 6, 20% chance low outlier = 1.0 min (spec §4)
        // Mutation: would catch if low outlier was != 1.0
        [Fact]
        public void AC4_Madness_LowOutlier_Returns1()
        {
            // Dice: variance=50, madnessCheck=10 (≤20 triggers), choice=1 (low)
            var dice = new SequenceDice(50, 10, 1);
            var shadows = new Dictionary<ShadowStatType, int> { { ShadowStatType.Madness, 6 } };

            var result = OpponentTimingCalculator.ComputeDelayMinutes(
                MakeProfile(baseDelay: 10, variance: 0.0f),
                InterestState.Interested, shadows, dice);

            Assert.Equal(1.0, result);
        }

        // What: AC-4 — Madness high outlier is ≥ 240 (spec §4)
        // Mutation: would catch if high outlier was less than 240
        [Fact]
        public void AC4_Madness_HighOutlier_AtLeast240()
        {
            // Dice: variance=50, madnessCheck=20 (≤20 triggers), choice=2 (high), duration=1
            var dice = new SequenceDice(50, 20, 2, 1);
            var shadows = new Dictionary<ShadowStatType, int> { { ShadowStatType.Madness, 6 } };

            var result = OpponentTimingCalculator.ComputeDelayMinutes(
                MakeProfile(baseDelay: 10, variance: 0.0f),
                InterestState.Interested, shadows, dice);

            Assert.True(result >= 240.0, $"Expected >= 240 but got {result}");
        }

        // What: AC-4 — Madness roll > 20 does not trigger outlier (spec §4)
        // Mutation: would catch if threshold was > 20 or always triggered
        [Fact]
        public void AC4_Madness_RollAbove20_NoEffect()
        {
            // Dice: variance=50, madnessCheck=21 (>20, no trigger)
            var dice = new SequenceDice(50, 21);
            var shadows = new Dictionary<ShadowStatType, int> { { ShadowStatType.Madness, 6 } };

            var result = OpponentTimingCalculator.ComputeDelayMinutes(
                MakeProfile(baseDelay: 10, variance: 0.0f),
                InterestState.Interested, shadows, dice);

            Assert.Equal(10.0, result, precision: 1);
        }

        // What: AC-4 — Madness exactly at boundary (roll=20) triggers (spec §4)
        // Mutation: would catch if boundary was < 20 (strict less-than)
        [Fact]
        public void AC4_Madness_ExactlyRoll20_Triggers()
        {
            // Dice: variance=50, madnessCheck=20 (≤20 triggers), choice=1 (low)
            var dice = new SequenceDice(50, 20, 1);
            var shadows = new Dictionary<ShadowStatType, int> { { ShadowStatType.Madness, 6 } };

            var result = OpponentTimingCalculator.ComputeDelayMinutes(
                MakeProfile(baseDelay: 10, variance: 0.0f),
                InterestState.Interested, shadows, dice);

            Assert.Equal(1.0, result);
        }

        // --- Shadow application order ---

        // What: AC-4 — Overthinking applied before Denial (spec §4 order)
        // Mutation: would catch if Denial was applied before Overthinking
        [Fact]
        public void AC4_ApplicationOrder_Overthinking_Then_Denial()
        {
            var shadows = new Dictionary<ShadowStatType, int>
            {
                { ShadowStatType.Overthinking, 6 },
                { ShadowStatType.Denial, 6 }
            };
            // Base=7, Interested ×1.0 = 7, Overthinking ×1.5 = 10.5, Denial snap to 10
            var result = OpponentTimingCalculator.ComputeDelayMinutes(
                MakeProfile(baseDelay: 7, variance: 0.0f),
                InterestState.Interested, shadows, new FixedDice(50));

            Assert.Equal(10.0, result);
        }

        // What: AC-4 — Madness outlier followed by Denial snap (spec §4 order)
        // Mutation: would catch if Denial didn't snap Madness outlier values
        [Fact]
        public void AC4_ApplicationOrder_Madness_Then_Denial()
        {
            // Madness triggers high outlier of 240 min, Denial snaps to nearest 5 → 240 (aligned)
            var dice = new SequenceDice(50, 10, 2, 1); // variance, madness check, choice=high, duration=1
            var shadows = new Dictionary<ShadowStatType, int>
            {
                { ShadowStatType.Madness, 6 },
                { ShadowStatType.Denial, 6 }
            };

            var result = OpponentTimingCalculator.ComputeDelayMinutes(
                MakeProfile(baseDelay: 10, variance: 0.0f),
                InterestState.Interested, shadows, dice);

            // 240 is already aligned to 5, should stay 240
            Assert.Equal(0.0, result % 5.0, precision: 1); // result is a multiple of 5
        }

        // =====================================================================
        // AC-5: Dry spell probability
        // =====================================================================

        // What: AC-5 — Dry spell triggers when roll ≤ probability*100 (spec §4)
        // Mutation: would catch if threshold check was > instead of ≤
        [Fact]
        public void AC5_DrySpell_Triggers_WhenRollAtThreshold()
        {
            // DrySpell = 0.25 → threshold=25, roll=25 → triggers
            var dice = new SequenceDice(50, 25, 180); // variance, drySpellCheck, duration
            var result = OpponentTimingCalculator.ComputeDelayMinutes(
                MakeProfile(baseDelay: 5, variance: 0.0f, drySpell: 0.25f),
                InterestState.Interested, NoShadows, dice);

            Assert.InRange(result, 120.0, 480.0);
        }

        // What: AC-5 — Dry spell does not trigger when roll > threshold (spec §4)
        // Mutation: would catch if check was >= instead of ≤
        [Fact]
        public void AC5_DrySpell_DoesNotTrigger_WhenRollAboveThreshold()
        {
            var dice = new SequenceDice(50, 26); // variance, drySpellCheck=26 > 25
            var result = OpponentTimingCalculator.ComputeDelayMinutes(
                MakeProfile(baseDelay: 5, variance: 0.0f, drySpell: 0.25f),
                InterestState.Interested, NoShadows, dice);

            Assert.Equal(5.0, result, precision: 1);
        }

        // What: AC-5 — Dry spell range is [120, 480] minutes (spec §4)
        // Mutation: would catch if range was wrong (e.g., [60, 360])
        [Fact]
        public void AC5_DrySpell_MinDuration_Is120()
        {
            // duration roll=1 → 120 + 1 - 1 = 120
            var dice = new SequenceDice(50, 1, 1); // variance, drySpellCheck=1 (triggers), duration=1
            var result = OpponentTimingCalculator.ComputeDelayMinutes(
                MakeProfile(baseDelay: 5, variance: 0.0f, drySpell: 1.0f),
                InterestState.Interested, NoShadows, dice);

            Assert.Equal(120.0, result);
        }

        // What: AC-5 — Dry spell max duration is 480 (spec §4)
        // Mutation: would catch if upper bound was different
        [Fact]
        public void AC5_DrySpell_MaxDuration_Is480()
        {
            // duration roll=361 → 120 + 361 - 1 = 480
            var dice = new SequenceDice(50, 1, 361); // variance, drySpellCheck=1 (triggers), duration=361
            var result = OpponentTimingCalculator.ComputeDelayMinutes(
                MakeProfile(baseDelay: 5, variance: 0.0f, drySpell: 1.0f),
                InterestState.Interested, NoShadows, dice);

            Assert.Equal(480.0, result);
        }

        // What: AC-5 — Dry spell is skipped when probability is 0.0 (spec §4)
        // Mutation: would catch if zero probability still rolled dice
        [Fact]
        public void AC5_DrySpell_ZeroProbability_SkipsEntirely()
        {
            // If drySpell check happened, FixedDice(1) would trigger it
            var result = OpponentTimingCalculator.ComputeDelayMinutes(
                MakeProfile(baseDelay: 10, variance: 0.0f, drySpell: 0.0f),
                InterestState.Interested, NoShadows, new FixedDice(1));

            Assert.Equal(10.0, result, precision: 1);
        }

        // =====================================================================
        // AC-6: JsonTimingRepository
        // =====================================================================

        private const string SampleJson = @"[
            {
                ""id"": ""eager-texter"",
                ""baseDelayMinutes"": 3,
                ""varianceMultiplier"": 0.4,
                ""drySpellProbability"": 0.05,
                ""readReceipt"": ""shows""
            },
            {
                ""id"": ""chill-responder"",
                ""baseDelayMinutes"": 15,
                ""varianceMultiplier"": 0.6,
                ""drySpellProbability"": 0.1,
                ""readReceipt"": ""hides""
            }
        ]";

        // What: AC-6 — GetProfile returns correct profile by ID (spec §4)
        // Mutation: would catch if profile lookup was broken or returned wrong profile
        [Fact]
        public void AC6_GetProfile_ReturnsCorrectProfile()
        {
            var repo = new JsonTimingRepository(SampleJson);
            var profile = repo.GetProfile("eager-texter");

            Assert.NotNull(profile);
            Assert.Equal(3, profile!.BaseDelayMinutes);
            Assert.Equal(0.4f, profile.VarianceMultiplier, precision: 2);
            Assert.Equal(0.05f, profile.DrySpellProbability, precision: 3);
            Assert.Equal("shows", profile.ReadReceipt);
        }

        // What: AC-6 — GetProfile returns null for unknown ID (spec §2)
        // Mutation: would catch if method threw instead of returning null
        [Fact]
        public void AC6_GetProfile_UnknownId_ReturnsNull()
        {
            var repo = new JsonTimingRepository(SampleJson);
            Assert.Null(repo.GetProfile("nonexistent"));
        }

        // What: AC-6 — GetAll returns all loaded profiles (spec §2)
        // Mutation: would catch if GetAll returned partial results
        [Fact]
        public void AC6_GetAll_ReturnsAllProfiles()
        {
            var repo = new JsonTimingRepository(SampleJson);
            Assert.Equal(2, repo.GetAll().Count());
        }

        // What: AC-6 — Malformed JSON throws FormatException (spec §6)
        // Mutation: would catch if malformed JSON was silently accepted
        [Fact]
        public void AC6_MalformedJson_ThrowsFormatException()
        {
            Assert.Throws<FormatException>(() => new JsonTimingRepository(@"{ ""key"": 1 }"));
        }

        // What: AC-6 — Null JSON throws ArgumentNullException (spec §6)
        // Mutation: would catch if null was silently accepted
        [Fact]
        public void AC6_NullJson_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => new JsonTimingRepository(null!));
        }

        // =====================================================================
        // AC-7: Deterministic dice verification (spec examples)
        // =====================================================================

        // What: AC-7/Spec Example 1 — basic computation, no shadows (spec §3)
        // Mutation: would catch if base variance formula was wrong
        [Fact]
        public void AC7_SpecExample1_BasicComputation()
        {
            var profile = MakeProfile(baseDelay: 10, variance: 0.5f, drySpell: 0.0f);
            var dice = new FixedDice(50);

            var result = OpponentTimingCalculator.ComputeDelayMinutes(
                profile, InterestState.Interested, NoShadows, dice);

            // Roll=50, normalized=(50-1)/99≈0.4949, factor=1+0.5*(0.4949-0.5)≈0.99975
            // 10 * 0.99975 ≈ 9.9975
            Assert.InRange(result, 9.5, 10.5);
        }

        // What: AC-7/Spec Example 2 — Bored + Overthinking (spec §3)
        // Mutation: would catch if interest and shadow multipliers didn't stack
        [Fact]
        public void AC7_SpecExample2_Bored_Overthinking()
        {
            var profile = MakeProfile(baseDelay: 10, variance: 0.0f, drySpell: 0.0f);
            var dice = new FixedDice(50);
            var shadows = new Dictionary<ShadowStatType, int> { { ShadowStatType.Overthinking, 8 } };

            var result = OpponentTimingCalculator.ComputeDelayMinutes(
                profile, InterestState.Bored, shadows, dice);

            Assert.Equal(75.0, result);
        }

        // What: AC-7/Spec Example 3 — VeryIntoIt + Denial snap (spec §3)
        // Mutation: would catch if Denial snap wasn't applied after interest multiplier
        [Fact]
        public void AC7_SpecExample3_VeryIntoIt_Denial()
        {
            var profile = MakeProfile(baseDelay: 10, variance: 0.0f, drySpell: 0.0f);
            var dice = new FixedDice(50);
            var shadows = new Dictionary<ShadowStatType, int> { { ShadowStatType.Denial, 7 } };

            var result = OpponentTimingCalculator.ComputeDelayMinutes(
                profile, InterestState.VeryIntoIt, shadows, dice);

            Assert.Equal(5.0, result);
        }

        // What: AC-7/Spec Example 4 — dry spell triggers (spec §3)
        // Mutation: would catch if dry spell logic was skipped
        [Fact]
        public void AC7_SpecExample4_DrySpellTriggers()
        {
            var dice = new SequenceDice(50, 20, 100); // variance=50, drySpellCheck=20, duration=100
            var profile = MakeProfile(baseDelay: 5, variance: 0.0f, drySpell: 0.25f);

            var result = OpponentTimingCalculator.ComputeDelayMinutes(
                profile, InterestState.Interested, NoShadows, dice);

            // 120 + 100 - 1 = 219
            Assert.InRange(result, 120.0, 480.0);
        }

        // =====================================================================
        // Edge Cases (spec §5)
        // =====================================================================

        // What: Edge — Unmatched returns very large delay (spec §5)
        // Mutation: would catch if Unmatched returned normal delay
        [Fact]
        public void Edge_Unmatched_ReturnsVeryLargeDelay()
        {
            var result = OpponentTimingCalculator.ComputeDelayMinutes(
                MakeProfile(baseDelay: 10), InterestState.Unmatched, NoShadows, new FixedDice(50));

            Assert.True(result >= 100000.0, $"Expected very large delay for Unmatched, got {result}");
        }

        // What: Edge — DateSecured returns 1.0 (spec §5)
        // Mutation: would catch if DateSecured returned base delay instead of 1.0
        [Fact]
        public void Edge_DateSecured_Returns1()
        {
            var result = OpponentTimingCalculator.ComputeDelayMinutes(
                MakeProfile(baseDelay: 10), InterestState.DateSecured, NoShadows, new FixedDice(50));

            Assert.Equal(1.0, result);
        }

        // What: Edge — null shadows treated as empty (spec §5/§6)
        // Mutation: would catch if null shadows threw NullReferenceException
        [Fact]
        public void Edge_NullShadows_TreatedAsEmpty()
        {
            var result = OpponentTimingCalculator.ComputeDelayMinutes(
                MakeProfile(baseDelay: 10, variance: 0.0f),
                InterestState.Interested, null!, new FixedDice(50));

            Assert.Equal(10.0, result, precision: 1);
        }

        // What: Edge — Horniness shadow stat has no effect (spec §5)
        // Mutation: would catch if Horniness was mistakenly treated as a timing modifier
        [Fact]
        public void Edge_Horniness_HasNoEffect()
        {
            var shadows = new Dictionary<ShadowStatType, int> { { ShadowStatType.Horniness, 10 } };
            var result = OpponentTimingCalculator.ComputeDelayMinutes(
                MakeProfile(baseDelay: 10, variance: 0.0f),
                InterestState.Interested, shadows, new FixedDice(50));

            Assert.Equal(10.0, result, precision: 1);
        }

        // What: Edge — Dread shadow stat has no effect (spec §5)
        // Mutation: would catch if Dread was mistakenly treated as a timing modifier
        [Fact]
        public void Edge_Dread_HasNoEffect()
        {
            var shadows = new Dictionary<ShadowStatType, int> { { ShadowStatType.Dread, 10 } };
            var result = OpponentTimingCalculator.ComputeDelayMinutes(
                MakeProfile(baseDelay: 10, variance: 0.0f),
                InterestState.Interested, shadows, new FixedDice(50));

            Assert.Equal(10.0, result, precision: 1);
        }

        // What: Edge — minimum delay floor is 1.0 (spec §5)
        // Mutation: would catch if result could go below 1.0
        [Fact]
        public void Edge_MinimumFloor_ClampedTo1()
        {
            var result = OpponentTimingCalculator.ComputeDelayMinutes(
                MakeProfile(baseDelay: 0, variance: 0.0f),
                InterestState.Interested, NoShadows, new FixedDice(50));

            Assert.True(result >= 1.0, $"Expected >= 1.0, got {result}");
        }

        // What: Edge — negative baseDelay still clamps to 1.0 (spec §6)
        // Mutation: would catch if negative base delay produced negative result
        [Fact]
        public void Edge_NegativeBaseDelay_ClampsToFloor()
        {
            var result = OpponentTimingCalculator.ComputeDelayMinutes(
                MakeProfile(baseDelay: -5, variance: 0.0f),
                InterestState.Interested, NoShadows, new FixedDice(50));

            Assert.True(result >= 1.0, $"Expected >= 1.0, got {result}");
        }

        // What: Edge — DrySpellProbability > 1.0 is clamped (spec §6)
        // Mutation: would catch if out-of-range probability caused crash
        [Fact]
        public void Edge_DrySpellProbability_Above1_Clamped()
        {
            // With probability=2.0 clamped to 1.0, any roll should trigger dry spell
            var dice = new SequenceDice(50, 50, 100); // variance, drySpellCheck, duration
            var result = OpponentTimingCalculator.ComputeDelayMinutes(
                MakeProfile(baseDelay: 5, variance: 0.0f, drySpell: 2.0f),
                InterestState.Interested, NoShadows, dice);

            Assert.InRange(result, 120.0, 480.0);
        }

        // What: Edge — DrySpellProbability < 0 is clamped to 0 (spec §6)
        // Mutation: would catch if negative probability caused issues
        [Fact]
        public void Edge_DrySpellProbability_Negative_Clamped()
        {
            var result = OpponentTimingCalculator.ComputeDelayMinutes(
                MakeProfile(baseDelay: 10, variance: 0.0f, drySpell: -0.5f),
                InterestState.Interested, NoShadows, new FixedDice(1));

            // Should not trigger dry spell despite dice=1 (probability clamped to 0)
            Assert.Equal(10.0, result, precision: 1);
        }

        // =====================================================================
        // Error Conditions (spec §6)
        // =====================================================================

        // What: Error — null profile throws ArgumentNullException (spec §6)
        // Mutation: would catch if null profile was silently accepted
        [Fact]
        public void Error_NullProfile_ThrowsArgumentNullException()
        {
            var ex = Assert.Throws<ArgumentNullException>(() =>
                OpponentTimingCalculator.ComputeDelayMinutes(
                    null!, InterestState.Interested, NoShadows, new FixedDice(50)));

            Assert.Equal("profile", ex.ParamName);
        }

        // What: Error — null dice throws ArgumentNullException (spec §6)
        // Mutation: would catch if null dice was silently accepted
        [Fact]
        public void Error_NullDice_ThrowsArgumentNullException()
        {
            var ex = Assert.Throws<ArgumentNullException>(() =>
                OpponentTimingCalculator.ComputeDelayMinutes(
                    MakeProfile(), InterestState.Interested, NoShadows, null!));

            Assert.Equal("dice", ex.ParamName);
        }

        // What: Error — invalid InterestState throws ArgumentOutOfRangeException (spec §6)
        // Mutation: would catch if invalid enum was silently handled with default
        [Fact]
        public void Error_InvalidInterestState_ThrowsArgumentOutOfRange()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                OpponentTimingCalculator.ComputeDelayMinutes(
                    MakeProfile(), (InterestState)(-1), NoShadows, new FixedDice(50)));
        }

        // =====================================================================
        // Variance Computation (spec §5)
        // =====================================================================

        // What: Variance — roll=1 gives minimum (spec §5 formula)
        // Mutation: would catch if variance formula had wrong sign or base
        [Fact]
        public void Variance_Roll1_GivesMinimum()
        {
            // Roll=1, normalized=(1-1)/99=0, factor=1+0.5*(0-0.5)=0.75
            var result = OpponentTimingCalculator.ComputeDelayMinutes(
                MakeProfile(baseDelay: 10, variance: 0.5f),
                InterestState.Interested, NoShadows, new FixedDice(1));

            Assert.Equal(7.5, result, precision: 1);
        }

        // What: Variance — roll=100 gives maximum (spec §5 formula)
        // Mutation: would catch if variance range was miscalculated
        [Fact]
        public void Variance_Roll100_GivesMaximum()
        {
            // Roll=100, normalized=(100-1)/99=1.0, factor=1+0.5*(1.0-0.5)=1.25
            var result = OpponentTimingCalculator.ComputeDelayMinutes(
                MakeProfile(baseDelay: 10, variance: 0.5f),
                InterestState.Interested, NoShadows, new FixedDice(100));

            Assert.Equal(12.5, result, precision: 1);
        }

        // What: Variance — zero variance multiplier gives exact base delay (spec §5)
        // Mutation: would catch if zero variance still introduced randomness
        [Fact]
        public void Variance_ZeroMultiplier_GivesExactBase()
        {
            var result = OpponentTimingCalculator.ComputeDelayMinutes(
                MakeProfile(baseDelay: 10, variance: 0.0f),
                InterestState.Interested, NoShadows, new FixedDice(1));

            Assert.Equal(10.0, result, precision: 1);
        }

        // =====================================================================
        // Stacking combinations
        // =====================================================================

        // What: Stacking — Overthinking + Bored interest (spec §3 Example 2)
        // Mutation: would catch if interest multiplied after shadow instead of before
        [Fact]
        public void Stacking_Overthinking_Bored()
        {
            var shadows = new Dictionary<ShadowStatType, int> { { ShadowStatType.Overthinking, 6 } };
            var result = OpponentTimingCalculator.ComputeDelayMinutes(
                MakeProfile(baseDelay: 10, variance: 0.0f),
                InterestState.Bored, shadows, new FixedDice(50));

            // 10 * 5.0 = 50, * 1.5 = 75
            Assert.Equal(75.0, result);
        }

        // What: Stacking — multiple ignored shadows don't affect result
        // Mutation: would catch if all shadow types were treated as timing modifiers
        [Fact]
        public void Stacking_AllIgnoredShadows_NoEffect()
        {
            var shadows = new Dictionary<ShadowStatType, int>
            {
                { ShadowStatType.Horniness, 10 },
                { ShadowStatType.Dread, 10 }
            };
            var result = OpponentTimingCalculator.ComputeDelayMinutes(
                MakeProfile(baseDelay: 10, variance: 0.0f),
                InterestState.Interested, shadows, new FixedDice(50));

            Assert.Equal(10.0, result, precision: 1);
        }

        // What: Fixation with previousDelay at exactly 1.0 (floor boundary)
        // Mutation: would catch if Fixation didn't respect the 1.0 floor on previousDelay
        [Fact]
        public void Fixation_PreviousDelayAt1_ReturnsExactly1()
        {
            var shadows = new Dictionary<ShadowStatType, int> { { ShadowStatType.Fixation, 6 } };
            var result = OpponentTimingCalculator.ComputeDelayMinutes(
                MakeProfile(baseDelay: 10, variance: 0.0f),
                InterestState.Interested, shadows, new FixedDice(50), previousDelay: 1.0);

            Assert.Equal(1.0, result);
        }
    }
}
