using Pinder.Core.Stats;
using Xunit;

namespace Pinder.Core.Tests
{
    [Trait("Category", "Core")]
    public class ShadowThresholdEvaluatorTests
    {
        [Theory]
        [InlineData(0, 0)]
        [InlineData(5, 0)]
        [InlineData(6, 1)]
        [InlineData(11, 1)]
        [InlineData(12, 2)]
        [InlineData(17, 2)]
        [InlineData(18, 3)]
        [InlineData(25, 3)]
        [InlineData(100, 3)]
        public void GetThresholdLevel_ReturnsCorrectTier(int shadowValue, int expected)
        {
            Assert.Equal(expected, ShadowThresholdEvaluator.GetThresholdLevel(shadowValue));
        }
    }
}
