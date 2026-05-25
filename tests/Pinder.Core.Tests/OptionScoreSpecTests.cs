using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Pinder.Core.Conversation;
using Pinder.Core.Stats;
using Pinder.SessionRunner;
using Xunit;

namespace Pinder.Core.Tests
{
    [Trait("Category", "Core")]
    public class OptionScoreSpecTests
    {
        // -- AC2: OptionScore properties set via constructor --

        [Fact]
        public void Constructor_SetsAllProperties()
        {
            var score = new OptionScore(3, 8.5f, 0.75f, 2.1f, new[] { "tell +2", "callback" });
            Assert.Equal(3, score.OptionIndex);
            Assert.Equal(8.5f, score.Score);
            Assert.Equal(0.75f, score.SuccessChance);
            Assert.Equal(2.1f, score.ExpectedInterestGain);
            Assert.Equal(2, score.BonusesApplied.Length);
        }

        [Fact]
        public void Constructor_NullBonuses_ThrowsArgumentNullException()
        {
            var ex = Assert.Throws<ArgumentNullException>(
                () => new OptionScore(0, 1.0f, 0.5f, 0.0f, null!));
            Assert.Equal("bonusesApplied", ex.ParamName);
        }

        // -- Spec invariant: SuccessChance clamped to [0.0, 1.0] --

        [Fact]
        public void SuccessChance_Above1_ClampedTo1()
        {
            var score = new OptionScore(0, 1.0f, 1.5f, 0.0f, Array.Empty<string>());
            Assert.Equal(1.0f, score.SuccessChance);
        }

        [Fact]
        public void SuccessChance_BelowZero_ClampedToZero()
        {
            var score = new OptionScore(0, 1.0f, -0.3f, 0.0f, Array.Empty<string>());
            Assert.Equal(0.0f, score.SuccessChance);
        }

        [Fact]
        public void SuccessChance_ExactBoundaries_Preserved()
        {
            var zero = new OptionScore(0, 1.0f, 0.0f, 0.0f, Array.Empty<string>());
            Assert.Equal(0.0f, zero.SuccessChance);

            var one = new OptionScore(0, 1.0f, 1.0f, 0.0f, Array.Empty<string>());
            Assert.Equal(1.0f, one.SuccessChance);
        }

        // -- Edge case: negative expected interest gain is valid --

        [Fact]
        public void ExpectedInterestGain_CanBeNegative()
        {
            var score = new OptionScore(0, -2.0f, 0.2f, -3.5f, Array.Empty<string>());
            Assert.Equal(-3.5f, score.ExpectedInterestGain);
        }

        // -- Edge case: empty bonuses array is valid --

        [Fact]
        public void EmptyBonusesArray_IsValid()
        {
            var score = new OptionScore(0, 1.0f, 0.5f, 0.0f, Array.Empty<string>());
            Assert.Empty(score.BonusesApplied);
        }

        // -- Edge case: all bonuses stacked --

        [Fact]
        public void AllBonusesStacked_Accepted()
        {
            var bonuses = new[] { "tell +2", "callback +2", "combo", "weakness -2" };
            var score = new OptionScore(0, 10.0f, 0.6f, 3.0f, bonuses);
            Assert.Equal(4, score.BonusesApplied.Length);
        }
    }
}
