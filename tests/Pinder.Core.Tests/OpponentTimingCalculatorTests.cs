using System;
using System.Collections.Generic;
using Pinder.Core.Conversation;
using Pinder.Core.Interfaces;
using Pinder.Core.Stats;
using Xunit;

namespace Pinder.Core.Tests
{
    public class OpponentTimingCalculatorTests
    {
        /// <summary>
        /// Deterministic dice that returns a fixed sequence of values.
        /// </summary>
        private class SequenceDice : IDiceRoller
        {
            private readonly int[] _values;
            private int _index;

            public SequenceDice(params int[] values)
            {
                _values = values;
                _index = 0;
            }

            public int Roll(int sides)
            {
                if (_index >= _values.Length)
                    throw new InvalidOperationException("SequenceDice exhausted — not enough values provided.");
                return _values[_index++];
            }
        }

        private class FixedDice : IDiceRoller
        {
            private readonly int _value;
            public FixedDice(int value) => _value = value;
            public int Roll(int sides) => _value;
        }

        private static TimingProfile MakeProfile(int baseDelay = 10, float variance = 0.0f, float drySpell = 0.0f, string receipt = "neutral")
            => new TimingProfile(baseDelay, variance, drySpell, receipt);

        private static Dictionary<ShadowStatType, int> NoShadows => new Dictionary<ShadowStatType, int>();

        // =====================================================================
        // Basic computation (no shadows, no variance)
        // =====================================================================

        [Fact]
        public void BasicDelay_Interested_NoShadows_ReturnsBase()
        {
            var profile = MakeProfile(baseDelay: 10, variance: 0.0f);
            var dice = new FixedDice(50); // midpoint

            double result = OpponentTimingCalculator.ComputeDelayMinutes(
                profile, InterestState.Interested, NoShadows, dice);

            Assert.Equal(10.0, result, precision: 1);
        }

        // =====================================================================
        // Interest multiplier tests
        // =====================================================================

        [Fact]
        public void Interest_Bored_MultipliesBy5()
        {
            var profile = MakeProfile(baseDelay: 10, variance: 0.0f);
            var dice = new FixedDice(50);

            double result = OpponentTimingCalculator.ComputeDelayMinutes(
                profile, InterestState.Bored, NoShadows, dice);

            Assert.Equal(50.0, result, precision: 1);
        }

        [Fact]
        public void Interest_VeryIntoIt_MultipliesBy0_5()
        {
            var profile = MakeProfile(baseDelay: 10, variance: 0.0f);
            var dice = new FixedDice(50);

            double result = OpponentTimingCalculator.ComputeDelayMinutes(
                profile, InterestState.VeryIntoIt, NoShadows, dice);

            Assert.Equal(5.0, result, precision: 1);
        }

        [Fact]
        public void Interest_AlmostThere_MultipliesBy0_3()
        {
            var profile = MakeProfile(baseDelay: 10, variance: 0.0f);
            var dice = new FixedDice(50);

            double result = OpponentTimingCalculator.ComputeDelayMinutes(
                profile, InterestState.AlmostThere, NoShadows, dice);

            Assert.Equal(3.0, result, precision: 1);
        }

        // =====================================================================
        // Terminal states
        // =====================================================================

        [Fact]
        public void Unmatched_ReturnsVeryLargeDelay()
        {
            var profile = MakeProfile(baseDelay: 10);
            var dice = new FixedDice(50);

            double result = OpponentTimingCalculator.ComputeDelayMinutes(
                profile, InterestState.Unmatched, NoShadows, dice);

            Assert.Equal(999999.0, result);
        }

        [Fact]
        public void DateSecured_Returns1()
        {
            var profile = MakeProfile(baseDelay: 10);
            var dice = new FixedDice(50);

            double result = OpponentTimingCalculator.ComputeDelayMinutes(
                profile, InterestState.DateSecured, NoShadows, dice);

            Assert.Equal(1.0, result);
        }

        // =====================================================================
        // Shadow modifier: Overthinking
        // =====================================================================

        [Fact]
        public void Overthinking_GTE6_Adds50Percent()
        {
            var profile = MakeProfile(baseDelay: 10, variance: 0.0f);
            var dice = new FixedDice(50);
            var shadows = new Dictionary<ShadowStatType, int> { { ShadowStatType.Overthinking, 8 } };

            double result = OpponentTimingCalculator.ComputeDelayMinutes(
                profile, InterestState.Interested, shadows, dice);

            Assert.Equal(15.0, result, precision: 1);
        }

        [Fact]
        public void Overthinking_Below6_NoEffect()
        {
            var profile = MakeProfile(baseDelay: 10, variance: 0.0f);
            var dice = new FixedDice(50);
            var shadows = new Dictionary<ShadowStatType, int> { { ShadowStatType.Overthinking, 5 } };

            double result = OpponentTimingCalculator.ComputeDelayMinutes(
                profile, InterestState.Interested, shadows, dice);

            Assert.Equal(10.0, result, precision: 1);
        }

        // =====================================================================
        // Shadow modifier: Denial (snap to 5-min interval)
        // =====================================================================

        [Fact]
        public void Denial_SnapsToNearest5()
        {
            // Base=7, no variance, Interested ×1.0 = 7.0, Denial snaps → 5.0
            var profile = MakeProfile(baseDelay: 7, variance: 0.0f);
            var dice = new FixedDice(50);
            var shadows = new Dictionary<ShadowStatType, int> { { ShadowStatType.Denial, 7 } };

            double result = OpponentTimingCalculator.ComputeDelayMinutes(
                profile, InterestState.Interested, shadows, dice);

            Assert.Equal(5.0, result);
        }

        [Fact]
        public void Denial_AlreadyAligned_NoChange()
        {
            var profile = MakeProfile(baseDelay: 10, variance: 0.0f);
            var dice = new FixedDice(50);
            var shadows = new Dictionary<ShadowStatType, int> { { ShadowStatType.Denial, 6 } };

            double result = OpponentTimingCalculator.ComputeDelayMinutes(
                profile, InterestState.VeryIntoIt, shadows, dice);

            // 10 * 0.5 = 5.0, snaps to 5.0
            Assert.Equal(5.0, result);
        }

        [Fact]
        public void Denial_VerySmallDelay_FloorAt5()
        {
            // BaseDelay=1, AlmostThere ×0.3 = 0.3, Denial snap → round(0.3/5)*5 = 0 → floor to 5
            var profile = MakeProfile(baseDelay: 1, variance: 0.0f);
            var dice = new FixedDice(50);
            var shadows = new Dictionary<ShadowStatType, int> { { ShadowStatType.Denial, 6 } };

            double result = OpponentTimingCalculator.ComputeDelayMinutes(
                profile, InterestState.AlmostThere, shadows, dice);

            Assert.Equal(5.0, result);
        }

        // =====================================================================
        // Shadow modifier: Fixation (rigid schedule)
        // =====================================================================

        [Fact]
        public void Fixation_WithPreviousDelay_ReturnsPrevious()
        {
            var profile = MakeProfile(baseDelay: 10, variance: 0.0f);
            var dice = new FixedDice(50);
            var shadows = new Dictionary<ShadowStatType, int> { { ShadowStatType.Fixation, 6 } };

            double result = OpponentTimingCalculator.ComputeDelayMinutes(
                profile, InterestState.Interested, shadows, dice, previousDelay: 42.0);

            Assert.Equal(42.0, result);
        }

        [Fact]
        public void Fixation_NoPreviousDelay_ComputesNormally()
        {
            var profile = MakeProfile(baseDelay: 10, variance: 0.0f);
            var dice = new FixedDice(50);
            var shadows = new Dictionary<ShadowStatType, int> { { ShadowStatType.Fixation, 6 } };

            double result = OpponentTimingCalculator.ComputeDelayMinutes(
                profile, InterestState.Interested, shadows, dice, previousDelay: null);

            Assert.Equal(10.0, result, precision: 1);
        }

        // =====================================================================
        // Shadow modifier: Madness (outlier)
        // =====================================================================

        [Fact]
        public void Madness_TriggersLowOutlier()
        {
            // Rolls: variance=50, madnessCheck=10 (≤20 triggers), choice=1 (low outlier = 1 min)
            var dice = new SequenceDice(50, 10, 1);
            var profile = MakeProfile(baseDelay: 10, variance: 0.0f);
            var shadows = new Dictionary<ShadowStatType, int> { { ShadowStatType.Madness, 6 } };

            double result = OpponentTimingCalculator.ComputeDelayMinutes(
                profile, InterestState.Interested, shadows, dice);

            Assert.Equal(1.0, result);
        }

        [Fact]
        public void Madness_TriggersHighOutlier()
        {
            // Rolls: variance=50, madnessCheck=15 (≤20 triggers), choice=2 (high outlier), durationRoll=1
            var dice = new SequenceDice(50, 15, 2, 1);
            var profile = MakeProfile(baseDelay: 10, variance: 0.0f);
            var shadows = new Dictionary<ShadowStatType, int> { { ShadowStatType.Madness, 6 } };

            double result = OpponentTimingCalculator.ComputeDelayMinutes(
                profile, InterestState.Interested, shadows, dice);

            // 240 + 1 - 1 = 240
            Assert.Equal(240.0, result);
        }

        [Fact]
        public void Madness_DoesNotTrigger_WhenRollAbove20()
        {
            // Rolls: variance=50, madnessCheck=21 (>20, no trigger)
            var dice = new SequenceDice(50, 21);
            var profile = MakeProfile(baseDelay: 10, variance: 0.0f);
            var shadows = new Dictionary<ShadowStatType, int> { { ShadowStatType.Madness, 6 } };

            double result = OpponentTimingCalculator.ComputeDelayMinutes(
                profile, InterestState.Interested, shadows, dice);

            Assert.Equal(10.0, result, precision: 1);
        }

        // =====================================================================
        // Dry spell
        // =====================================================================

        [Fact]
        public void DrySpell_Triggers_ReturnsLargeDelay()
        {
            // Rolls: variance=50, drySpellCheck=20 (≤25 triggers), drySpellDuration=1
            var dice = new SequenceDice(50, 20, 1);
            var profile = MakeProfile(baseDelay: 5, variance: 0.0f, drySpell: 0.25f);

            double result = OpponentTimingCalculator.ComputeDelayMinutes(
                profile, InterestState.Interested, NoShadows, dice);

            // 120 + 1 - 1 = 120
            Assert.Equal(120.0, result);
        }

        [Fact]
        public void DrySpell_DoesNotTrigger_WhenRollAboveThreshold()
        {
            // Rolls: variance=50, drySpellCheck=26 (>25, no trigger)
            var dice = new SequenceDice(50, 26);
            var profile = MakeProfile(baseDelay: 5, variance: 0.0f, drySpell: 0.25f);

            double result = OpponentTimingCalculator.ComputeDelayMinutes(
                profile, InterestState.Interested, NoShadows, dice);

            Assert.Equal(5.0, result, precision: 1);
        }

        [Fact]
        public void DrySpell_ZeroProbability_SkipsCheck()
        {
            var dice = new FixedDice(1); // would trigger if checked
            var profile = MakeProfile(baseDelay: 10, variance: 0.0f, drySpell: 0.0f);

            double result = OpponentTimingCalculator.ComputeDelayMinutes(
                profile, InterestState.Interested, NoShadows, dice);

            Assert.Equal(10.0, result, precision: 1);
        }

        // =====================================================================
        // Variance
        // =====================================================================

        [Fact]
        public void Variance_HighRoll_IncreasesDelay()
        {
            // Roll 100 → normalized=1.0, factor = 1 + 0.5*(1.0-0.5) = 1.25
            var dice = new FixedDice(100);
            var profile = MakeProfile(baseDelay: 10, variance: 0.5f);

            double result = OpponentTimingCalculator.ComputeDelayMinutes(
                profile, InterestState.Interested, NoShadows, dice);

            Assert.Equal(12.5, result, precision: 1);
        }

        [Fact]
        public void Variance_LowRoll_DecreasesDelay()
        {
            // Roll 1 → normalized=0.0, factor = 1 + 0.5*(0.0-0.5) = 0.75
            var dice = new FixedDice(1);
            var profile = MakeProfile(baseDelay: 10, variance: 0.5f);

            double result = OpponentTimingCalculator.ComputeDelayMinutes(
                profile, InterestState.Interested, NoShadows, dice);

            Assert.Equal(7.5, result, precision: 1);
        }

        // =====================================================================
        // Stacking: Overthinking + Denial
        // =====================================================================

        [Fact]
        public void Overthinking_And_Denial_Stack()
        {
            // Base=7, ×1.0, Overthinking +50% = 10.5, Denial snap to 5 → 10.0
            var profile = MakeProfile(baseDelay: 7, variance: 0.0f);
            var dice = new FixedDice(50);
            var shadows = new Dictionary<ShadowStatType, int>
            {
                { ShadowStatType.Overthinking, 6 },
                { ShadowStatType.Denial, 6 }
            };

            double result = OpponentTimingCalculator.ComputeDelayMinutes(
                profile, InterestState.Interested, shadows, dice);

            Assert.Equal(10.0, result);
        }

        // =====================================================================
        // Bored + Overthinking (from spec example)
        // =====================================================================

        [Fact]
        public void Bored_WithOverthinking_SpecExample()
        {
            // Base=10, Bored ×5.0=50, Overthinking +50%=75
            var profile = MakeProfile(baseDelay: 10, variance: 0.0f);
            var dice = new FixedDice(50);
            var shadows = new Dictionary<ShadowStatType, int> { { ShadowStatType.Overthinking, 8 } };

            double result = OpponentTimingCalculator.ComputeDelayMinutes(
                profile, InterestState.Bored, shadows, dice);

            Assert.Equal(75.0, result);
        }

        // =====================================================================
        // Minimum delay floor
        // =====================================================================

        [Fact]
        public void MinimumFloor_NeverBelow1()
        {
            var profile = MakeProfile(baseDelay: 0, variance: 0.0f);
            var dice = new FixedDice(50);

            double result = OpponentTimingCalculator.ComputeDelayMinutes(
                profile, InterestState.Interested, NoShadows, dice);

            Assert.True(result >= 1.0);
        }

        // =====================================================================
        // Error conditions
        // =====================================================================

        [Fact]
        public void NullProfile_Throws()
        {
            Assert.Throws<ArgumentNullException>(() =>
                OpponentTimingCalculator.ComputeDelayMinutes(null!, InterestState.Interested, NoShadows, new FixedDice(50)));
        }

        [Fact]
        public void NullDice_Throws()
        {
            Assert.Throws<ArgumentNullException>(() =>
                OpponentTimingCalculator.ComputeDelayMinutes(MakeProfile(), InterestState.Interested, NoShadows, null!));
        }

        [Fact]
        public void InvalidInterestState_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                OpponentTimingCalculator.ComputeDelayMinutes(MakeProfile(), (InterestState)99, NoShadows, new FixedDice(50)));
        }

        [Fact]
        public void NullShadows_TreatedAsEmpty()
        {
            var profile = MakeProfile(baseDelay: 10, variance: 0.0f);
            var dice = new FixedDice(50);

            double result = OpponentTimingCalculator.ComputeDelayMinutes(
                profile, InterestState.Interested, null!, dice);

            Assert.Equal(10.0, result, precision: 1);
        }

        // =====================================================================
        // Ignored shadow stats (Despair, Dread)
        // =====================================================================

        [Fact]
        public void Despair_And_Dread_HaveNoEffect()
        {
            var profile = MakeProfile(baseDelay: 10, variance: 0.0f);
            var dice = new FixedDice(50);
            var shadows = new Dictionary<ShadowStatType, int>
            {
                { ShadowStatType.Despair, 10 },
                { ShadowStatType.Dread, 10 }
            };

            double result = OpponentTimingCalculator.ComputeDelayMinutes(
                profile, InterestState.Interested, shadows, dice);

            Assert.Equal(10.0, result, precision: 1);
        }
    }
}
