using System;
using System.Collections.Generic;
using Xunit;
using Pinder.Core.Rolls;

namespace Pinder.Core.Tests
{
    /// <summary>
    /// Unit tests for <see cref="RollCheckResult.Consequence"/> (#964).
    /// </summary>
    public class RollCheckResultTests
    {
        private static RollCheckResult CreateSampleCheck()
        {
            return new RollCheckResult(
                RollCheckKind.OptionRoll, 15, null, 15,
                new List<NamedModifier>(), 0, 15, 10,
                isSuccess: true, isNatOne: false, isNatTwenty: false,
                FailureTier.Success, missMargin: 0);
        }

        [Fact]
        public void Consequence_DefaultIsNull()
        {
            var check = CreateSampleCheck();
            Assert.Null(check.Consequence);
        }

        [Fact]
        public void ApplyConsequence_SetsProperty()
        {
            var check = CreateSampleCheck();
            check.ApplyConsequence("You feel a dark presence.");
            Assert.Equal("You feel a dark presence.", check.Consequence);
        }

        [Fact]
        public void ApplyConsequence_SecondCallThrows()
        {
            var check = CreateSampleCheck();
            check.ApplyConsequence("first");
            var ex = Assert.Throws<InvalidOperationException>(() => check.ApplyConsequence("second"));
            Assert.Equal("Consequence already applied", ex.Message);
        }
    }
}
