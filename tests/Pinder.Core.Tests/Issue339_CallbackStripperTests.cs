using Pinder.Core.Text;
using Xunit;

namespace Pinder.Core.Tests
{
    /// <summary>
    /// Issue #339: regression tests for the same-turn callback strip.
    /// </summary>
    public class Issue339_CallbackStripperTests
    {
        // ── Stripped openers ─────────────────────────────────────────────

        [Theory]
        [InlineData("As you said, that was wild.",       "That was wild.")]
        [InlineData("As you just said, that was wild.",   "That was wild.")]
        [InlineData("As we discussed, no thanks.",        "No thanks.")]
        [InlineData("As I just discussed, that's fine.",   "That's fine.")]
        [InlineData("Like we said, fine.",                 "Fine.")]
        [InlineData("Like you mentioned, sure.",           "Sure.")]
        [InlineData("Like you just said, ok.",             "Ok.")]
        [InlineData("As I mentioned, ok.",                 "Ok.")]
        [InlineData("Like I said, fine.",                  "Fine.")]
        public void Strip_removes_known_callback_openers(string input, string expected)
        {
            Assert.Equal(expected, CallbackStripper.Strip(input));
        }

        [Theory]
        [InlineData("It was great. As you said, that was wild.",
                    "It was great. That was wild.")]
        public void Strip_removes_callbacks_after_sentence_boundary(string input, string expected)
        {
            Assert.Equal(expected, CallbackStripper.Strip(input));
        }

        // ── Cross-turn references preserved ──────────────────────────────

        [Theory]
        [InlineData("Earlier, on turn 2, you said something funny.")]
        [InlineData("As you said back on turn 5, the pasta was a sign.")]
        [InlineData("On turn 3 we talked about it.")]
        public void Strip_preserves_explicit_cross_turn_references(string input)
        {
            // Even if the message LOOKS like it has a callback shape,
            // any explicit "turn N" reference flips the whole message
            // into "legitimate cross-turn" mode and nothing is stripped.
            Assert.Equal(input, CallbackStripper.Strip(input));
        }

        // ── Non-matches unchanged ────────────────────────────────────────

        [Theory]
        [InlineData("Hello, how are you?")]
        [InlineData("As a kid I loved that show.")]              // "As" + non-callback continuation
        [InlineData("Like coffee, I prefer dark beans.")]        // "Like" + non-callback continuation
        [InlineData("")]
        public void Strip_leaves_non_matches_alone(string input)
        {
            Assert.Equal(input, CallbackStripper.Strip(input));
        }

        [Fact]
        public void Strip_handles_null_as_empty()
        {
            Assert.Equal(string.Empty, CallbackStripper.Strip(null));
        }

        // ── WouldStrip flag ──────────────────────────────────────────────

        [Fact]
        public void WouldStrip_returns_true_only_when_strip_changes_text()
        {
            Assert.True(CallbackStripper.WouldStrip("As you said, hi."));
            Assert.False(CallbackStripper.WouldStrip("Hi there."));
            Assert.False(CallbackStripper.WouldStrip(""));
            Assert.False(CallbackStripper.WouldStrip(null));
        }

        // ── Cosmetic cleanup ─────────────────────────────────────────────

        [Fact]
        public void Strip_collapses_double_spaces_left_by_removal()
        {
            // After stripping "As you said, " from the middle, we don't
            // want "It happened.  And it stuck." with two spaces.
            string got = CallbackStripper.Strip("It happened. As you said, and it stuck.");
            Assert.Equal("It happened. And it stuck.", got);
        }

        [Fact]
        public void Strip_capitalises_new_opener_after_stripping()
        {
            string got = CallbackStripper.Strip("As you just said, that was a lot.");
            Assert.Equal("That was a lot.", got);
        }
    }
}
