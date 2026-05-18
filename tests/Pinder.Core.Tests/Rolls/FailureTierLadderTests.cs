using Pinder.Core.Rolls;
using Xunit;

namespace Pinder.Core.Tests
{
    /// <summary>
    /// Exhaustive boundary coverage for <see cref="FailureTierLadder.FromMissMargin"/>.
    /// #901: single source of truth for the miss-margin tier ladder.
    /// </summary>
    public class FailureTierLadderTests
    {
        [Theory]
        [InlineData(0,  FailureTier.Success)]         // success boundary
        [InlineData(-1, FailureTier.Success)]          // success (negative miss)
        [InlineData(-99, FailureTier.Success)]         // large success
        public void FromMissMargin_SuccessCases_ReturnNone(int missMargin, FailureTier expected)
        {
            Assert.Equal(expected, FailureTierLadder.FromMissMargin(missMargin));
        }

        [Theory]
        [InlineData(1, FailureTier.Fumble)]
        [InlineData(2, FailureTier.Fumble)]
        public void FromMissMargin_FumbleBoundaries(int missMargin, FailureTier expected)
        {
            Assert.Equal(expected, FailureTierLadder.FromMissMargin(missMargin));
        }

        [Theory]
        [InlineData(3, FailureTier.Misfire)]
        [InlineData(5, FailureTier.Misfire)]
        public void FromMissMargin_MisfireBoundaries(int missMargin, FailureTier expected)
        {
            Assert.Equal(expected, FailureTierLadder.FromMissMargin(missMargin));
        }

        [Theory]
        [InlineData(6,  FailureTier.TropeTrap)]
        [InlineData(9,  FailureTier.TropeTrap)]
        public void FromMissMargin_TropeTrapBoundaries(int missMargin, FailureTier expected)
        {
            Assert.Equal(expected, FailureTierLadder.FromMissMargin(missMargin));
        }

        [Theory]
        [InlineData(10, FailureTier.Catastrophe)]
        [InlineData(99, FailureTier.Catastrophe)]
        public void FromMissMargin_CatastropheBoundaries(int missMargin, FailureTier expected)
        {
            Assert.Equal(expected, FailureTierLadder.FromMissMargin(missMargin));
        }
    }
}
