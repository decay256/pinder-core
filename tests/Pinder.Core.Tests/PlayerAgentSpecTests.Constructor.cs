using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Pinder.Core.Conversation;
using Pinder.Core.Stats;
using Pinder.SessionRunner;
using Xunit;

namespace Pinder.Core.Tests
{
    public partial class PlayerDecisionSpecTests
    {
        // -- AC2: PlayerDecision properties are read-only and set via constructor --

        [Fact]
        public void Constructor_SetsOptionIndex()
        {
            var scores = new[] { MakeScore(0), MakeScore(1), MakeScore(2) };
            var decision = new PlayerDecision(2, "picked third", scores);
            Assert.Equal(2, decision.OptionIndex);
        }

        [Fact]
        public void Constructor_SetsReasoning()
        {
            var scores = new[] { MakeScore(0) };
            var decision = new PlayerDecision(0, "some reasoning", scores);
            Assert.Equal("some reasoning", decision.Reasoning);
        }

        [Fact]
        public void Constructor_SetsScoresArray()
        {
            var scores = new[] { MakeScore(0), MakeScore(1) };
            var decision = new PlayerDecision(1, "reason", scores);
            Assert.Equal(2, decision.Scores.Length);
            Assert.Equal(1, decision.Scores[1].OptionIndex);
        }

        // -- AC2: Constructor validation --

        [Fact]
        public void Constructor_NullReasoning_ThrowsArgumentNullException()
        {
            var scores = new[] { MakeScore(0) };
            var ex = Assert.Throws<ArgumentNullException>(() => new PlayerDecision(0, null!, scores));
            Assert.Equal("reasoning", ex.ParamName);
        }

        [Fact]
        public void Constructor_NullScores_ThrowsArgumentNullException()
        {
            var ex = Assert.Throws<ArgumentNullException>(() => new PlayerDecision(0, "r", null!));
            Assert.Equal("scores", ex.ParamName);
        }

        [Fact]
        public void Constructor_OptionIndexEqualToScoresLength_ThrowsOutOfRange()
        {
            var scores = new[] { MakeScore(0), MakeScore(1) };
            Assert.Throws<ArgumentOutOfRangeException>(() => new PlayerDecision(2, "r", scores));
        }

        [Fact]
        public void Constructor_NegativeOptionIndex_ThrowsOutOfRange()
        {
            var scores = new[] { MakeScore(0) };
            Assert.Throws<ArgumentOutOfRangeException>(() => new PlayerDecision(-1, "r", scores));
        }

        [Fact]
        public void Constructor_OptionIndexAtLastValid_Succeeds()
        {
            var scores = new[] { MakeScore(0), MakeScore(1), MakeScore(2) };
            var decision = new PlayerDecision(2, "last", scores);
            Assert.Equal(2, decision.OptionIndex);
        }

        // -- Edge case: empty reasoning is valid for deterministic agents --

        [Fact]
        public void Constructor_EmptyReasoning_IsValid()
        {
            var scores = new[] { MakeScore(0) };
            var decision = new PlayerDecision(0, "", scores);
            Assert.Equal("", decision.Reasoning);
        }
    }
}
