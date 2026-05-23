using System.Collections.Generic;
using Pinder.Core.Text;
using Xunit;

namespace Pinder.Core.Tests
{
    public class TextSanitizerTests
    {
        [Fact]
        public void Sanitize_MetaPrefix_StripsAndAddsDiff()
        {
            var diffs = new List<TextDiff>();
            string raw = "CONTEXT: this is a test";
            string result = TextSanitizer.Sanitize(raw, MetaPrefixStripper.LayerName, diffs);

            Assert.Equal("this is a test", result);
            Assert.Single(diffs);
            Assert.Equal("Meta-Prefix Strip", diffs[0].LayerName);
            Assert.Equal(raw, diffs[0].Before);
            Assert.Equal("this is a test", diffs[0].After);
        }

        [Fact]
        public void Sanitize_Callback_StripsAndAddsDiff()
        {
            var diffs = new List<TextDiff>();
            string raw = "As you just said, this is a test";
            string result = TextSanitizer.Sanitize(raw, CallbackStripper.LayerName, diffs);

            Assert.Equal("This is a test", result);
            Assert.Single(diffs);
            Assert.Equal("Callback Strip", diffs[0].LayerName);
            Assert.Equal(raw, diffs[0].Before);
            Assert.Equal("This is a test", diffs[0].After);
        }

        [Fact]
        public void Sanitize_NoChange_DoesNotAddDiff()
        {
            var diffs = new List<TextDiff>();
            string raw = "Just a normal sentence.";
            string result = TextSanitizer.Sanitize(raw, MetaPrefixStripper.LayerName, diffs);

            Assert.Equal(raw, result);
            Assert.Empty(diffs);
        }

        [Fact]
        public void Sanitize_NullInput_ReturnsEmptyAndDoesNotAddDiff()
        {
            var diffs = new List<TextDiff>();
            string result = TextSanitizer.Sanitize(null!, MetaPrefixStripper.LayerName, diffs);

            Assert.Equal(string.Empty, result);
            Assert.Empty(diffs);
        }
    }
}
