using Pinder.Core.Conversation;
using Xunit;

namespace Pinder.Core.Tests
{
    /// <summary>
    /// Unit tests for CallbackBonus.Compute — §15 callback distance detection.
    /// Covers all tiers (0, +1, +2, +3) including boundary values and opener priority.
    /// </summary>
    [Trait("Category", "Core")]
    public class CallbackBonusTests
    {
        // --- Distance < 2: no bonus ---

        [Theory]
        [InlineData(5, 5, 0)]  // distance 0 — same turn
        [InlineData(5, 4, 0)]  // distance 1 — too recent
        [InlineData(1, 0, 0)]  // opener at distance 1 — still too recent
        [InlineData(0, 0, 0)]  // turn 0, callback turn 0 — distance 0
        public void Compute_DistanceLessThan2_ReturnsZero(int current, int callback, int expected)
        {
            Assert.Equal(expected, CallbackBonus.Compute(current, callback));
        }

        // --- Mid-distance (distance 2-3, non-opener): +1 ---

        [Theory]
        [InlineData(5, 3, 1)]  // distance 2
        [InlineData(5, 2, 1)]  // distance 3
        [InlineData(3, 1, 1)]  // distance 2, non-opener
        public void Compute_MidDistance_NonOpener_ReturnsOne(int current, int callback, int expected)
        {
            Assert.Equal(expected, CallbackBonus.Compute(current, callback));
        }

        // --- Long-distance (distance >= 4, non-opener): +2 ---

        [Theory]
        [InlineData(5, 1, 2)]   // distance 4
        [InlineData(6, 1, 2)]   // distance 5
        [InlineData(100, 1, 2)] // distance 99
        [InlineData(10, 3, 2)]  // distance 7
        public void Compute_LongDistance_NonOpener_ReturnsTwo(int current, int callback, int expected)
        {
            Assert.Equal(expected, CallbackBonus.Compute(current, callback));
        }

        // --- Opener reference (callbackTurnNumber == 0, distance >= 2): +3 ---

        [Theory]
        [InlineData(2, 0, 3)]   // opener at distance 2 — minimum for bonus
        [InlineData(3, 0, 3)]   // opener at distance 3
        [InlineData(5, 0, 3)]   // opener at distance 5
        [InlineData(6, 0, 3)]   // opener at distance 6 — opener wins over 4+ rule
        [InlineData(100, 0, 3)] // opener at distance 100
        public void Compute_OpenerReference_ReturnsThree(int current, int callback, int expected)
        {
            Assert.Equal(expected, CallbackBonus.Compute(current, callback));
        }

        // --- Boundary: exactly distance 2 ---

        [Fact]
        public void Compute_ExactlyDistance2_NonOpener_ReturnsOne()
        {
            Assert.Equal(1, CallbackBonus.Compute(4, 2));
        }

        [Fact]
        public void Compute_ExactlyDistance2_Opener_ReturnsThree()
        {
            Assert.Equal(3, CallbackBonus.Compute(2, 0));
        }

        // --- Boundary: exactly distance 4 ---

        [Fact]
        public void Compute_ExactlyDistance4_NonOpener_ReturnsTwo()
        {
            Assert.Equal(2, CallbackBonus.Compute(5, 1));
        }

        [Fact]
        public void Compute_ExactlyDistance4_Opener_ReturnsThree()
        {
            // Opener always beats the 4+ rule
            Assert.Equal(3, CallbackBonus.Compute(4, 0));
        }
    }
}
