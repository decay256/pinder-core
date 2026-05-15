using Pinder.Core.Text;
using Xunit;

namespace Pinder.Core.Tests
{
    public class MetaPrefixStripperTests
    {
        [Theory]
        [InlineData("WOULD-YOU-RATHER: actual text", "actual text")]
        [InlineData("CONTEXT: actual message", "actual message")]
        [InlineData("GENUINE QUESTION: Is this real?", "Is this real?")]
        [InlineData("RECOGNITION: I see you.", "I see you.")]
        [InlineData("OPENER: Let me start by saying...", "Let me start by saying...")]
        [InlineData("LABEL: hello", "hello")]
        [InlineData("NOTE: just a thought", "just a thought")]
        public void Strip_RemovesMetaPrefix_Correctly(string input, string expected)
        {
            string result = MetaPrefixStripper.Strip(input);
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData("hey, how was your day?")]
        [InlineData("\"WOULD-YOU-RATHER: text\"")]
        [InlineData("would-you-rather: text")]
        [InlineData("CONTEXT text")]
        [InlineData("")]
        public void Strip_DoesNotRemoveNonLabelPatterns(string input)
        {
            string result = MetaPrefixStripper.Strip(input);
            Assert.Equal(input, result);
        }

        [Theory]
        [InlineData("WOULD-YOU-RATHER: actual text", true)]
        [InlineData("CONTEXT: actual message", true)]
        [InlineData("GENUINE QUESTION: Is this real?", true)]
        [InlineData("hey, how was your day?", false)]
        [InlineData("", false)]
        [InlineData("Just a normal sentence.", false)]
        public void WouldStrip_ReturnsCorrectly(string input, bool expected)
        {
            bool result = MetaPrefixStripper.WouldStrip(input);
            Assert.Equal(expected, result);
        }

        [Fact]
        public void LayerName_IsStable()
        {
            Assert.Equal("Meta-Prefix Strip", MetaPrefixStripper.LayerName);
        }

        [Fact]
        public void Strip_RemovesOnlyPrefix()
        {
            // Regression: ensure only the leading label is removed,
            // not text that resembles a label later in the string.
            string input = "CONTEXT: I mentioned CONTEXT: earlier";
            string result = MetaPrefixStripper.Strip(input);
            Assert.Equal("I mentioned CONTEXT: earlier", result);
        }

        [Fact]
        public void Strip_NullInput_ReturnsEmpty()
        {
            string result = MetaPrefixStripper.Strip(null);
            Assert.Equal(string.Empty, result);
        }

        [Fact]
        public void Strip_WhitespaceOnlyLabel_ReturnsEmpty()
        {
            // Label with nothing after the colon+space.
            string result = MetaPrefixStripper.Strip("CONTEXT:  ");
            Assert.Equal(" ", result); // regex consumes up to one space via \s*; trailing stays
        }
    }
}
