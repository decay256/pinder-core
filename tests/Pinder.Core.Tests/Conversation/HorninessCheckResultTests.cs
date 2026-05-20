using System;
using Xunit;
using Pinder.Core.Conversation;
using Pinder.Core.Rolls;

namespace Pinder.Core.Tests
{
    /// <summary>
    /// Unit tests for <see cref="HorninessCheckResult.Consequence"/> (#964).
    /// </summary>
    public class HorninessCheckResultTests
    {
        private static HorninessCheckResult CreateSample()
        {
            return new HorninessCheckResult(
                roll: 5, modifier: 3, total: 8, dc: 10,
                isMiss: true, tier: FailureTier.Fumble, overlayApplied: false);
        }

        [Fact]
        public void Consequence_DefaultIsNull()
        {
            var result = CreateSample();
            Assert.Null(result.Consequence);
        }

        [Fact]
        public void ApplyConsequence_SetsProperty()
        {
            var result = CreateSample();
            result.ApplyConsequence("Hormones surge.");
            Assert.Equal("Hormones surge.", result.Consequence);
        }

        [Fact]
        public void ApplyConsequence_SecondCallThrows()
        {
            var result = CreateSample();
            result.ApplyConsequence("first");
            var ex = Assert.Throws<InvalidOperationException>(() => result.ApplyConsequence("second"));
            Assert.Equal("Consequence already applied", ex.Message);
        }
    }
}