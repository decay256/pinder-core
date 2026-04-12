using Pinder.LlmAdapters.Anthropic;
using Xunit;

namespace Pinder.LlmAdapters.Tests.Anthropic
{
    public class StatNameNormalizerTests
    {
        [Theory]
        [InlineData("SELF_AWARENESS", "SelfAwareness")]
        [InlineData("SELFAWARENESS", "SelfAwareness")]
        [InlineData("self_awareness", "SelfAwareness")]
        [InlineData("selfawareness", "SelfAwareness")]
        [InlineData("Charm", "Charm")]
        [InlineData("Honesty", "Honesty")]
        [InlineData("Wit", "Wit")]
        [InlineData("Chaos", "Chaos")]
        [InlineData("", "")]
        public void NormalizeStatName_ReturnsExpected(string input, string expected)
        {
            var result = StatNameNormalizer.NormalizeStatName(input);
            Assert.Equal(expected, result);
        }
    }
}
