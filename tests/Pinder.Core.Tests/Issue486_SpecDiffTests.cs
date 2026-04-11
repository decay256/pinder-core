using System;
using Xunit;
using Pinder.SessionRunner;

namespace Pinder.Core.Tests
{
    [Trait("Category", "SessionRunner")]
    public class Issue486_SpecDiffTests
    {
        // What: 1. Clean success (or exact match)
        // Mutation: Fails if identical strings (ignoring whitespace) return the *Intended:* block instead of just delivered.
        [Fact]
        public void FormatMessageDiff_CleanSuccessOrExactMatch_ReturnsOnlyDelivered()
        {
            string intended = "that's not nothing";
            string delivered = "  that's not nothing  ";
            
            string result = PlaytestFormatter.FormatMessageDiff(intended, delivered);
            
            Assert.Equal("  that's not nothing  ", result);
        }

        // What: 2. Strong success / Nat 20 / Transformed Messages
        // Mutation: Fails if different texts do not include the *Intended:* prefix and *Delivered:* label on newlines.
        [Fact]
        public void FormatMessageDiff_DifferenceDetected_ReturnsBothSeparated()
        {
            string intended = "that's not nothing";
            string delivered = "that's not nothing well i mean it's not everything either but you know what i mean";
            
            string result = PlaytestFormatter.FormatMessageDiff(intended, delivered);
            
            string expected = "*Intended: \"that's not nothing\"*\n*Delivered:*\nthat's not nothing well i mean it's not everything either but you know what i mean";
            Assert.Equal(expected, result);
        }

        // What: 4. Skip Empty Intention Placeholder
        // Mutation: Fails if intended is "..." and it outputs the diff format instead of just delivered text.
        [Fact]
        public void FormatMessageDiff_PlaceholderIntended_ReturnsOnlyDelivered()
        {
            string intended = "...";
            string delivered = "this is completely new";
            
            string result = PlaytestFormatter.FormatMessageDiff(intended, delivered);
            
            Assert.Equal("this is completely new", result);
        }

        // What: Edge Cases - Whitespace Changes
        // Mutation: Fails if intended and delivered only differ by leading/trailing whitespace but it triggers the diff format.
        [Fact]
        public void FormatMessageDiff_OnlyWhitespaceDiffers_ReturnsOnlyDelivered()
        {
            string intended = " hello ";
            string delivered = "hello";
            
            string result = PlaytestFormatter.FormatMessageDiff(intended, delivered);
            
            Assert.Equal("hello", result);
        }

        // What: Edge Cases - Null Values
        // Mutation: Fails if intended is null and it throws instead of gracefully returning delivered.
        [Fact]
        public void FormatMessageDiff_NullIntended_ReturnsOnlyDeliveredGracefully()
        {
            string? intended = null;
            string delivered = "it was null";
            
            string result = PlaytestFormatter.FormatMessageDiff(intended, delivered);
            
            Assert.Equal("it was null", result);
        }

        // What: Edge Cases - Null Values
        // Mutation: Fails if delivered is null and it throws a null reference exception
        [Fact]
        public void FormatMessageDiff_NullDelivered_ReturnsEmptyGracefully()
        {
            string intended = "intended";
            string? delivered = null;
            
            string result = PlaytestFormatter.FormatMessageDiff(intended, delivered);
            
            string expected = "*Intended: \"intended\"*\n*Delivered:*\n";
            Assert.Equal(expected, result);
        }
    }
}
