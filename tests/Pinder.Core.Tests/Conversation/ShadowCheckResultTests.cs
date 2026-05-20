using System;
using Xunit;
using Pinder.Core.Conversation;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;

namespace Pinder.Core.Tests
{
    /// <summary>
    /// Unit tests for <see cref="ShadowCheckResult.Consequence"/> (#964).
    /// </summary>
    public class ShadowCheckResultTests
    {
        private static ShadowCheckResult CreateSample()
        {
            return new ShadowCheckResult(
                checkPerformed: true,
                shadow: ShadowStatType.Madness,
                roll: 8,
                dc: 12,
                isMiss: true,
                tier: FailureTier.Fumble,
                overlayApplied: false);
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
            result.ApplyConsequence("Your mind clouds with madness.");
            Assert.Equal("Your mind clouds with madness.", result.Consequence);
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