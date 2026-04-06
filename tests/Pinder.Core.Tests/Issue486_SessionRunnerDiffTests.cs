using Xunit;

namespace Pinder.Core.Tests
{
    public class Issue486_SessionRunnerDiffTests
    {
        [Fact]
        public void FormatDeliveredAdditions_WithFumbleAddition_AppliesStrikethroughToAddition()
        {
            string intended = "that's not nothing";
            string delivered = "that's not nothing well i mean it's not everything either but you know what i mean";
            
            string result = global::Program.FormatDeliveredAdditions(intended, delivered, "~~");
            
            Assert.Equal("that's not nothing ~~well i mean it's not everything either but you know what i mean~~", result);
        }

        [Fact]
        public void FormatDeliveredAdditions_WithEmbellishment_AppliesItalicsToAddition()
        {
            string intended = "she never had to learn";
            string delivered = "she never had to learn, because she never had to survive it";
            
            string result = global::Program.FormatDeliveredAdditions(intended, delivered, "*");
            
            Assert.Equal("she never had to learn, *because she never had to survive it*", result);
        }

        [Fact]
        public void FormatDeliveredAdditions_WithPlaceholder_WrapsEntireString()
        {
            string intended = "...";
            string delivered = "this is completely new";
            
            string result = global::Program.FormatDeliveredAdditions(intended, delivered, "~~");
            
            Assert.Equal("~~this is completely new~~", result);
        }

        [Fact]
        public void FormatDeliveredAdditions_WithWhitespaceDiff_IgnoresWhitespaceForMatch()
        {
            string intended = "whitespace   diff";
            string delivered = "whitespace diff and more";
            
            string result = global::Program.FormatDeliveredAdditions(intended, delivered, "*");
            
            Assert.Equal("whitespace diff *and more*", result);
        }

        [Fact]
        public void FormatDeliveredAdditions_NoMatch_WrapsEntireString()
        {
            string intended = "hello";
            string delivered = "world";
            
            string result = global::Program.FormatDeliveredAdditions(intended, delivered, "~~");
            
            Assert.Equal("~~world~~", result);
        }
    }
}
