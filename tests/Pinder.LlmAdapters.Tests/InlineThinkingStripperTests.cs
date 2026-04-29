using Pinder.LlmAdapters;
using Xunit;

namespace Pinder.LlmAdapters.Tests
{
    /// <summary>
    /// Issue #351: post-processor that strips inline &lt;thinking&gt; / &lt;reasoning&gt;
    /// blocks from prose-only LLM surfaces (steering question, horniness /
    /// shadow / trap overlays).
    /// </summary>
    public class InlineThinkingStripperTests
    {
        [Fact]
        public void Strip_RemovesWholeBlock_WhenWrappingEntireResponse()
        {
            string input = "<thinking>The model is reasoning here.</thinking>Y";
            string result = InlineThinkingStripper.Strip(input);
            Assert.Equal("Y", result);
        }

        [Fact]
        public void Strip_RemovesLeadingBlock_AndKeepsRemainingProse()
        {
            string input = "<thinking>plan goes here</thinking>That's an interesting take.";
            string result = InlineThinkingStripper.Strip(input);
            Assert.Equal("That's an interesting take.", result);
        }

        [Fact]
        public void Strip_HandlesMultilineThinkingBlock()
        {
            string input =
                "<thinking>\n" +
                "Step 1: read the message\n" +
                "Step 2: respond playfully\n" +
                "</thinking>\n" +
                "haha sure, why not.";
            string result = InlineThinkingStripper.Strip(input);
            Assert.Equal("haha sure, why not.", result);
        }

        [Fact]
        public void Strip_IsCaseInsensitive_OnTagName()
        {
            string input = "<Thinking>...</Thinking>visible";
            Assert.Equal("visible", InlineThinkingStripper.Strip(input));

            string upper = "<THINKING>...</THINKING>visible";
            Assert.Equal("visible", InlineThinkingStripper.Strip(upper));
        }

        [Fact]
        public void Strip_HandlesReasoningTagVariant()
        {
            string input = "<reasoning>weighing options</reasoning>I'll go with the first one.";
            string result = InlineThinkingStripper.Strip(input);
            Assert.Equal("I'll go with the first one.", result);
        }

        [Fact]
        public void Strip_ReturnsInputUnchanged_WhenNoTagsPresent()
        {
            string input = "no tags here, just regular dialogue.";
            Assert.Equal(input, InlineThinkingStripper.Strip(input));
        }

        [Fact]
        public void Strip_ReturnsInputUnchanged_WhenOnlyClosingTagPresent()
        {
            // Malformed: no opening tag. Don't try to be clever — leave it.
            string input = "</thinking>visible";
            Assert.Equal(input, InlineThinkingStripper.Strip(input));
        }

        [Fact]
        public void Strip_DoesNotRemoveTagsInTheMiddleOfPlayerText()
        {
            // The opening tag is mid-text — could be legitimate angle-bracket
            // content in a player line. Conservative: leave it alone.
            string input = "Hey there <thinking>just a stray</thinking> friend.";
            Assert.Equal(input, InlineThinkingStripper.Strip(input));
        }

        [Fact]
        public void Strip_HandlesNullInput()
        {
            // null in -> empty out (defensive: same shape as the existing
            // string-aware helpers in the adapter layer).
            Assert.Equal(string.Empty, InlineThinkingStripper.Strip(null));
        }

        [Fact]
        public void Strip_HandlesEmptyInput()
        {
            Assert.Equal(string.Empty, InlineThinkingStripper.Strip(string.Empty));
        }

        [Fact]
        public void Strip_RemovesBlock_WhenLeadingWhitespacePresent()
        {
            // Some models emit a blank line / whitespace before the thinking
            // tag. The leading-anchor regex tolerates that.
            string input = "   \n<thinking>...</thinking>visible";
            Assert.Equal("visible", InlineThinkingStripper.Strip(input));
        }

        [Fact]
        public void Strip_OnlyRemovesFirstBlock_WhenMultipleAtStart()
        {
            // Two consecutive thinking blocks at the start. The first match
            // anchors at ^, so we strip the leading one and return the rest
            // (including the second block) unchanged. That's the conservative
            // contract \u2014 "strip a single occurrence at the start".
            string input =
                "<thinking>plan A</thinking><thinking>plan B</thinking>final text";
            string result = InlineThinkingStripper.Strip(input);
            // The first leading-anchor match is greedy enough to consume up
            // to the first closing tag, so the second block remains.
            Assert.Equal("<thinking>plan B</thinking>final text", result);
        }

        // ── #351 acceptance criteria: regression check ─────────────────────

        [Fact]
        public void Strip_KnownModelOutput_StripsThinkingTagsFromPlayerVisibleText()
        {
            // Acceptance criterion: \"a test that takes a known model response
            // with inline tags and asserts the player-visible text doesn't
            // contain them.\" Use a representative shape similar to what
            // non-native-thinking models emit.
            string modelResponse =
                "<thinking>The player is being aggressive, I should respond playfully...</thinking>" +
                "That's an interesting take.";
            string playerVisible = InlineThinkingStripper.Strip(modelResponse);
            Assert.DoesNotContain("<thinking>", playerVisible, System.StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("</thinking>", playerVisible, System.StringComparison.OrdinalIgnoreCase);
            Assert.Equal("That's an interesting take.", playerVisible);
        }
    }
}
