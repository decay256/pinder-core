using System;
using Pinder.Core.Conversation;
using Pinder.Core.Rolls;
using Xunit;

namespace Pinder.Core.Tests
{
    /// <summary>
    /// Regression tests for #1245 — Bad-roll delivery degradation should not mechanically
    /// force ALL CAPS and should not append a trailing em dash.
    /// </summary>
    [Trait("Category", "Core")]
    public class Issue1245_BadRollDeliveryTests
    {
        [Theory]
        [InlineData("I think the wizard hat is doing community theatre in your emotional support ecosystem and honestly I respect the commitment.", "wizard", FailureTier.Catastrophe, 10)]
        [InlineData("I think the wizard hat is doing community theatre in your emotional support ecosystem and honestly I respect the commitment.", "hat", FailureTier.Legendary, 99)]
        [InlineData("Maybe we could grab tacos this weekend if you are free?", "tacos", FailureTier.Catastrophe, 15)]
        [InlineData("Maybe we could grab tacos this weekend if you are free?", "tacos", FailureTier.Legendary, 99)]
        public void SevereDegradation_DoesNotForceAllCaps_NorTrailingEmDash(string input, string expectedTopic, FailureTier tier, int margin)
        {
            var output = DeliveryOverlay.Apply(input, tier, margin);

            // a. Not entirely ALL CAPS (must retain lowercase letters)
            Assert.NotEqual(output.ToUpperInvariant(), output);

            // b. No trailing em dash (U+2014)
            Assert.False(output.TrimEnd().EndsWith("—"), "Output should not end with an em dash.");

            // c. Is a sendable message
            Assert.False(string.IsNullOrWhiteSpace(output), "Output should not be empty or whitespace.");
            Assert.NotEqual("...", output.Trim());

            // d. Topic preserved
            Assert.True(output.IndexOf(expectedTopic, StringComparison.OrdinalIgnoreCase) >= 0, $"Output must preserve the topic token '{expectedTopic}'.");
        }

        [Theory]
        [InlineData(FailureTier.Catastrophe, 12)]
        [InlineData(FailureTier.Legendary, 99)]
        public void SevereDegradation_IsDeterministic(FailureTier tier, int margin)
        {
            var input = "Determinism check line.";
            var output1 = DeliveryOverlay.Apply(input, tier, margin);
            var output2 = DeliveryOverlay.Apply(input, tier, margin);

            Assert.Equal(output1, output2);
        }

        [Fact]
        public void Success_CommitsVerbatim()
        {
            var input = "This is a perfect delivery.";
            var output = DeliveryOverlay.Apply(input, FailureTier.Success, 0);

            Assert.Equal(input, output);
        }
    }
}
