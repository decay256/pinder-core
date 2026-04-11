using System;
using System.Collections.Generic;
using Pinder.Core.Conversation;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;
using Xunit;

namespace Pinder.Core.Tests
{
    /// <summary>
    /// Tests for TurnResult expansion (Issue #78) and RiskTier enum.
    /// </summary>
    [Trait("Category", "Core")]
    public class TurnResultExpansionTests
    {
        // Helper to create a minimal valid RollResult
        private static RollResult MakeRoll() =>
            new RollResult(10, null, 10, StatType.Charm, 2, 0, 13, FailureTier.None);

        // Helper to create a minimal valid GameStateSnapshot
        private static GameStateSnapshot MakeSnapshot() =>
            new GameStateSnapshot(10, InterestState.Interested, 0, Array.Empty<string>(), 1);

        #region RiskTier Enum

        [Fact]
        public void RiskTier_HasFourMembers()
        {
            var values = Enum.GetValues(typeof(RiskTier));
            Assert.Equal(5, values.Length);
        }

        [Theory]
        [InlineData(RiskTier.Safe, 0)]
        [InlineData(RiskTier.Medium, 1)]
        [InlineData(RiskTier.Hard, 2)]
        [InlineData(RiskTier.Bold, 3)]
        public void RiskTier_MembersHaveExpectedValues(RiskTier tier, int expected)
        {
            Assert.Equal(expected, (int)tier);
        }

        #endregion

        #region TurnResult Defaults

        [Fact]
        public void TurnResult_NewFieldsDefaultCorrectly_WhenNotSpecified()
        {
            var result = new TurnResult(
                MakeRoll(), "hello", "hi back", null, 1, MakeSnapshot(), false, null);

            Assert.NotNull(result.ShadowGrowthEvents);
            Assert.Empty(result.ShadowGrowthEvents);
            Assert.Null(result.ComboTriggered);
            Assert.Equal(0, result.CallbackBonusApplied);
            Assert.Equal(0, result.TellReadBonus);
            Assert.Null(result.TellReadMessage);
            Assert.Equal(RiskTier.Safe, result.RiskTier);
            Assert.Equal(0, result.XpEarned);
        }

        [Fact]
        public void TurnResult_ExistingFieldsUnchanged()
        {
            var roll = MakeRoll();
            var snap = MakeSnapshot();
            var result = new TurnResult(roll, "msg", "reply", "beat", -2, snap, true, GameOutcome.Ghosted);

            Assert.Same(roll, result.Roll);
            Assert.Equal("msg", result.DeliveredMessage);
            Assert.Equal("reply", result.OpponentMessage);
            Assert.Equal("beat", result.NarrativeBeat);
            Assert.Equal(-2, result.InterestDelta);
            Assert.Same(snap, result.StateAfter);
            Assert.True(result.IsGameOver);
            Assert.Equal(GameOutcome.Ghosted, result.Outcome);
        }

        #endregion

        #region ShadowGrowthEvents null-coalescing

        [Fact]
        public void TurnResult_ShadowGrowthEvents_NullBecomesEmptyList()
        {
            var result = new TurnResult(
                MakeRoll(), "a", "b", null, 0, MakeSnapshot(), false, null,
                shadowGrowthEvents: null);

            Assert.NotNull(result.ShadowGrowthEvents);
            Assert.Empty(result.ShadowGrowthEvents);
        }

        [Fact]
        public void TurnResult_ShadowGrowthEvents_PreservesList()
        {
            var events = new List<string> { "Despair +1 (Rizz overuse)", "Dread +1" };
            var result = new TurnResult(
                MakeRoll(), "a", "b", null, 0, MakeSnapshot(), false, null,
                shadowGrowthEvents: events);

            Assert.Equal(2, result.ShadowGrowthEvents.Count);
            Assert.Equal("Despair +1 (Rizz overuse)", result.ShadowGrowthEvents[0]);
            Assert.Equal("Dread +1", result.ShadowGrowthEvents[1]);
        }

        #endregion

        #region All new fields populated

        [Fact]
        public void TurnResult_AllNewFieldsPopulated()
        {
            var events = new[] { "Shadow event" };
            var result = new TurnResult(
                MakeRoll(), "a", "b", null, 3, MakeSnapshot(), false, null,
                shadowGrowthEvents: events,
                comboTriggered: "SmoothOperator",
                callbackBonusApplied: 1,
                tellReadBonus: 2,
                tellReadMessage: "Cats tell",
                riskTier: RiskTier.Bold,
                xpEarned: 15);

            Assert.Single(result.ShadowGrowthEvents);
            Assert.Equal("SmoothOperator", result.ComboTriggered);
            Assert.Equal(1, result.CallbackBonusApplied);
            Assert.Equal(2, result.TellReadBonus);
            Assert.Equal("Cats tell", result.TellReadMessage);
            Assert.Equal(RiskTier.Bold, result.RiskTier);
            Assert.Equal(15, result.XpEarned);
        }

        #endregion

        #region Null validation on required fields still works

        [Fact]
        public void TurnResult_ThrowsOnNullRoll()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new TurnResult(null!, "a", "b", null, 0, MakeSnapshot(), false, null));
        }

        [Fact]
        public void TurnResult_ThrowsOnNullDeliveredMessage()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new TurnResult(MakeRoll(), null!, "b", null, 0, MakeSnapshot(), false, null));
        }

        [Fact]
        public void TurnResult_ThrowsOnNullOpponentMessage()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new TurnResult(MakeRoll(), "a", null!, null, 0, MakeSnapshot(), false, null));
        }

        [Fact]
        public void TurnResult_ThrowsOnNullStateAfter()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new TurnResult(MakeRoll(), "a", "b", null, 0, null!, false, null));
        }

        #endregion

        #region Interest delta breakdown fields (#699)

        [Fact]
        public void TurnResult_BreakdownFields_DefaultToZero()
        {
            var result = new TurnResult(
                MakeRoll(), "a", "b", null, 0, MakeSnapshot(), false, null);

            Assert.Equal(0, result.BaseInterestDelta);
            Assert.Equal(0, result.RiskBonusDelta);
            Assert.Equal(0, result.ComboBonusDelta);
        }

        [Fact]
        public void TurnResult_BreakdownFields_PopulatedCorrectly()
        {
            var result = new TurnResult(
                MakeRoll(), "a", "b", null, 6, MakeSnapshot(), false, null,
                baseInterestDelta: 2,
                riskBonusDelta: 3,
                comboBonusDelta: 1);

            Assert.Equal(2, result.BaseInterestDelta);
            Assert.Equal(3, result.RiskBonusDelta);
            Assert.Equal(1, result.ComboBonusDelta);
        }

        [Fact]
        public void TurnResult_BreakdownFields_NegativeValues()
        {
            var result = new TurnResult(
                MakeRoll(), "a", "b", null, -3, MakeSnapshot(), false, null,
                baseInterestDelta: -2,
                riskBonusDelta: 0,
                comboBonusDelta: -1);

            Assert.Equal(-2, result.BaseInterestDelta);
            Assert.Equal(0, result.RiskBonusDelta);
            Assert.Equal(-1, result.ComboBonusDelta);
        }

        #endregion

        #region Edge cases: negative values stored as-is

        [Fact]
        public void TurnResult_NegativeNumericValues_StoredAsIs()
        {
            var result = new TurnResult(
                MakeRoll(), "a", "b", null, 0, MakeSnapshot(), false, null,
                callbackBonusApplied: -1,
                tellReadBonus: -3,
                xpEarned: -5);

            Assert.Equal(-1, result.CallbackBonusApplied);
            Assert.Equal(-3, result.TellReadBonus);
            Assert.Equal(-5, result.XpEarned);
        }

        #endregion
    }
}
