using Xunit;

namespace Pinder.Core.Tests
{
    [Trait("Category", "SessionRunner")]
    public class Issue486_SessionRunnerDiffTests
    {
        [Fact]
        public void FormatDeliveredAdditions_IdenticalText_ReturnsDelivered()
        {
            string intended = "hello world";
            string delivered = "hello world";

            string result = global::Program.FormatDeliveredAdditions(intended, delivered, "~~");

            Assert.Equal("hello world", result);
        }

        [Fact]
        public void FormatDeliveredAdditions_WithAdditionAtEnd_ReturnsDeliveredAsIs()
        {
            string intended = "that's not nothing";
            string delivered = "that's not nothing well i mean it's not everything either but you know what i mean";

            string result = global::Program.FormatDeliveredAdditions(intended, delivered, "~~");

            Assert.Equal(delivered, result);
        }

        [Fact]
        public void FormatDeliveredAdditions_WithWordSubstitutionInMiddle_ReturnsDeliveredAsIs()
        {
            string intended = "she walked to the bright store";
            string delivered = "she walked to the dark store quickly";

            string result = global::Program.FormatDeliveredAdditions(intended, delivered, "*");

            Assert.Equal(delivered, result);
        }

        [Fact]
        public void FormatDeliveredAdditions_WithPlaceholder_ReturnsDeliveredAsIs()
        {
            string intended = "...";
            string delivered = "this is completely new";

            string result = global::Program.FormatDeliveredAdditions(intended, delivered, "~~");

            Assert.Equal(delivered, result);
        }

        [Fact]
        public void FormatDeliveredAdditions_NoMatch_ReturnsDeliveredAsIs()
        {
            string intended = "hello";
            string delivered = "world";

            string result = global::Program.FormatDeliveredAdditions(intended, delivered, "~~");

            Assert.Equal(delivered, result);
        }

        [Fact]
        public void FormatDeliveredAdditions_EmptyIntended_ReturnsDeliveredAsIs()
        {
            string intended = "";
            string delivered = "something new";

            string result = global::Program.FormatDeliveredAdditions(intended, delivered, "*");

            Assert.Equal(delivered, result);
        }
    }
}
