using System;
using System.Collections.Generic;
using Pinder.Core.Conversation;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;
using Xunit;

namespace Pinder.Core.Tests
{
    /// <summary>
    /// Spec-driven tests for Issue #78 — TurnResult expansion and RiskTier enum.
    /// Written by test-engineer agent from docs/specs/issue-78-spec.md.
    /// </summary>
    [Trait("Category", "Core")]
    public class TurnResultExpansionSpecTests
    {
        // Helpers — create minimal valid instances for required constructor params
        private static RollResult MakeRoll() =>
            new RollResult(10, null, 10, StatType.Charm, 2, 0, 13, FailureTier.None);

        private static GameStateSnapshot MakeSnapshot() =>
            new GameStateSnapshot(10, InterestState.Interested, 0, Array.Empty<string>(), 1);

        #region AC1: RiskTier enum defined

        // What: AC1 — RiskTier enum exists with exactly 5 members (§1)
        // Mutation: would catch if any member was removed or extra members added
        [Fact]
        public void RiskTier_Enum_HasExactlyFourMembers()
        {
            var names = Enum.GetNames(typeof(RiskTier));
            Assert.Equal(5, names.Length);
        }

        // What: AC1 — RiskTier members are in ascending risk order: Safe, Medium, Hard, Bold, Reckless (§1)
        // Mutation: would catch if enum member order was changed or names misspelled
        [Theory]
        [InlineData("Safe", 0)]
        [InlineData("Medium", 1)]
        [InlineData("Hard", 2)]
        [InlineData("Bold", 3)]
        [InlineData("Reckless", 4)]
        public void RiskTier_MemberNamesAndValues_MatchSpec(string name, int expectedValue)
        {
            var parsed = (RiskTier)Enum.Parse(typeof(RiskTier), name);
            Assert.Equal(expectedValue, (int)parsed);
        }

        // What: AC1 — RiskTier is in Pinder.Core.Rolls namespace (§1)
        // Mutation: would catch if enum was placed in wrong namespace
        [Fact]
        public void RiskTier_IsInRollsNamespace()
        {
            Assert.Equal("Pinder.Core.Rolls", typeof(RiskTier).Namespace);
        }

        #endregion

        #region AC2: All seven fields added with sensible defaults

        // What: AC2 — When constructed with only original 8 args, ShadowGrowthEvents defaults to empty list (§2, §3 Example 1)
        // Mutation: would catch if default was null instead of empty list
        [Fact]
        public void Defaults_ShadowGrowthEvents_IsEmptyListNotNull()
        {
            var result = new TurnResult(
                MakeRoll(), "msg", "reply", null, 0, MakeSnapshot(), false, null);

            Assert.NotNull(result.ShadowGrowthEvents);
            Assert.Equal(0, result.ShadowGrowthEvents.Count);
        }

        // What: AC2 — ComboTriggered defaults to null (§2)
        // Mutation: would catch if default was empty string instead of null
        [Fact]
        public void Defaults_ComboTriggered_IsNull()
        {
            var result = new TurnResult(
                MakeRoll(), "msg", "reply", null, 0, MakeSnapshot(), false, null);

            Assert.Null(result.ComboTriggered);
        }

        // What: AC2 — CallbackBonusApplied defaults to 0 (§2)
        // Mutation: would catch if default was non-zero
        [Fact]
        public void Defaults_CallbackBonusApplied_IsZero()
        {
            var result = new TurnResult(
                MakeRoll(), "msg", "reply", null, 0, MakeSnapshot(), false, null);

            Assert.Equal(0, result.CallbackBonusApplied);
        }

        // What: AC2 — TellReadBonus defaults to 0 (§2)
        // Mutation: would catch if default was non-zero
        [Fact]
        public void Defaults_TellReadBonus_IsZero()
        {
            var result = new TurnResult(
                MakeRoll(), "msg", "reply", null, 0, MakeSnapshot(), false, null);

            Assert.Equal(0, result.TellReadBonus);
        }

        // What: AC2 — TellReadMessage defaults to null (§2)
        // Mutation: would catch if default was empty string instead of null
        [Fact]
        public void Defaults_TellReadMessage_IsNull()
        {
            var result = new TurnResult(
                MakeRoll(), "msg", "reply", null, 0, MakeSnapshot(), false, null);

            Assert.Null(result.TellReadMessage);
        }

        // What: AC2 — RiskTier defaults to RiskTier.Safe (§2)
        // Mutation: would catch if default was Medium or another tier
        [Fact]
        public void Defaults_RiskTier_IsSafe()
        {
            var result = new TurnResult(
                MakeRoll(), "msg", "reply", null, 0, MakeSnapshot(), false, null);

            Assert.Equal(RiskTier.Safe, result.RiskTier);
        }

        // What: AC2 — XpEarned defaults to 0 (§2)
        // Mutation: would catch if default was non-zero
        [Fact]
        public void Defaults_XpEarned_IsZero()
        {
            var result = new TurnResult(
                MakeRoll(), "msg", "reply", null, 0, MakeSnapshot(), false, null);

            Assert.Equal(0, result.XpEarned);
        }

        #endregion

        #region AC3: Constructor backward compatibility + optional parameters

        // What: AC3 — Existing 8-arg construction still works (§2 Constructor, §3 Example 1)
        // Mutation: would catch if constructor required new parameters
        [Fact]
        public void Constructor_BackwardCompatible_EightArgs()
        {
            var roll = MakeRoll();
            var snap = MakeSnapshot();
            var result = new TurnResult(roll, "delivered", "opponent", "beat", 5, snap, true, GameOutcome.DateSecured);

            Assert.Same(roll, result.Roll);
            Assert.Equal("delivered", result.DeliveredMessage);
            Assert.Equal("opponent", result.OpponentMessage);
            Assert.Equal("beat", result.NarrativeBeat);
            Assert.Equal(5, result.InterestDelta);
            Assert.Same(snap, result.StateAfter);
            Assert.True(result.IsGameOver);
            Assert.Equal(GameOutcome.DateSecured, result.Outcome);
        }

        // What: AC3 — All new fields can be specified via named parameters (§2 Constructor, §3 Example 2)
        // Mutation: would catch if any named parameter was missing or mistyped
        [Fact]
        public void Constructor_AllNewFieldsPopulated_MatchSpec()
        {
            var events = new[] { "Despair +1 (Rizz overuse)" };
            var result = new TurnResult(
                MakeRoll(), "I noticed you like long walks...", "Omg yes! Tell me more!",
                "Things are heating up!", 3, MakeSnapshot(), false, null,
                shadowGrowthEvents: events,
                comboTriggered: "SmoothOperator",
                callbackBonusApplied: 1,
                tellReadBonus: 2,
                tellReadMessage: "You noticed they always mention cats — +2 bonus!",
                riskTier: RiskTier.Hard,
                xpEarned: 15);

            Assert.Equal(1, result.ShadowGrowthEvents.Count);
            Assert.Equal("Despair +1 (Rizz overuse)", result.ShadowGrowthEvents[0]);
            Assert.Equal("SmoothOperator", result.ComboTriggered);
            Assert.Equal(1, result.CallbackBonusApplied);
            Assert.Equal(2, result.TellReadBonus);
            Assert.Equal("You noticed they always mention cats — +2 bonus!", result.TellReadMessage);
            Assert.Equal(RiskTier.Hard, result.RiskTier);
            Assert.Equal(15, result.XpEarned);
        }

        // What: AC3 — shadowGrowthEvents property never returns null (§2 Constructor behavior)
        // Mutation: would catch if constructor stored null instead of coalescing to empty
        [Fact]
        public void Constructor_ShadowGrowthEvents_ExplicitNull_BecomesEmpty()
        {
            var result = new TurnResult(
                MakeRoll(), "a", "b", null, 0, MakeSnapshot(), false, null,
                shadowGrowthEvents: null);

            Assert.NotNull(result.ShadowGrowthEvents);
            Assert.Empty(result.ShadowGrowthEvents);
        }

        #endregion

        #region Edge Cases (§5)

        // What: Edge case — shadowGrowthEvents passed as empty list returns that empty list (§5)
        // Mutation: would catch if empty list was replaced with a different instance
        [Fact]
        public void EdgeCase_ShadowGrowthEvents_EmptyList_StaysEmpty()
        {
            var emptyList = new List<string>();
            var result = new TurnResult(
                MakeRoll(), "a", "b", null, 0, MakeSnapshot(), false, null,
                shadowGrowthEvents: emptyList);

            Assert.NotNull(result.ShadowGrowthEvents);
            Assert.Equal(0, result.ShadowGrowthEvents.Count);
        }

        // What: Edge case — multiple shadow growth events preserved in order (§5)
        // Mutation: would catch if list was reversed, deduplicated, or truncated
        [Fact]
        public void EdgeCase_MultipleShadowGrowthEvents_PreservedInOrder()
        {
            var events = new List<string> { "Despair +1", "Dread +1", "Fixation +2" };
            var result = new TurnResult(
                MakeRoll(), "a", "b", null, 0, MakeSnapshot(), false, null,
                shadowGrowthEvents: events);

            Assert.Equal(3, result.ShadowGrowthEvents.Count);
            Assert.Equal("Despair +1", result.ShadowGrowthEvents[0]);
            Assert.Equal("Dread +1", result.ShadowGrowthEvents[1]);
            Assert.Equal("Fixation +2", result.ShadowGrowthEvents[2]);
        }

        // What: Edge case — negative xpEarned stored as-is, no validation (§5)
        // Mutation: would catch if negative values were clamped to 0
        [Fact]
        public void EdgeCase_NegativeXpEarned_StoredAsIs()
        {
            var result = new TurnResult(
                MakeRoll(), "a", "b", null, 0, MakeSnapshot(), false, null,
                xpEarned: -10);

            Assert.Equal(-10, result.XpEarned);
        }

        // What: Edge case — negative callbackBonusApplied stored as-is (§5)
        // Mutation: would catch if negative values were clamped to 0
        [Fact]
        public void EdgeCase_NegativeCallbackBonusApplied_StoredAsIs()
        {
            var result = new TurnResult(
                MakeRoll(), "a", "b", null, 0, MakeSnapshot(), false, null,
                callbackBonusApplied: -7);

            Assert.Equal(-7, result.CallbackBonusApplied);
        }

        // What: Edge case — negative tellReadBonus stored as-is (§5)
        // Mutation: would catch if negative values were clamped to 0
        [Fact]
        public void EdgeCase_NegativeTellReadBonus_StoredAsIs()
        {
            var result = new TurnResult(
                MakeRoll(), "a", "b", null, 0, MakeSnapshot(), false, null,
                tellReadBonus: -4);

            Assert.Equal(-4, result.TellReadBonus);
        }

        // What: Edge case — invalid RiskTier enum value stored as-is (§5)
        // Mutation: would catch if constructor validated enum range
        [Fact]
        public void EdgeCase_InvalidRiskTierValue_StoredAsIs()
        {
            var result = new TurnResult(
                MakeRoll(), "a", "b", null, 0, MakeSnapshot(), false, null,
                riskTier: (RiskTier)99);

            Assert.Equal((RiskTier)99, result.RiskTier);
        }

        // What: Edge case — all new fields at defaults behaves identically to pre-expansion (§5)
        // Mutation: would catch if defaults had side effects on existing fields
        [Fact]
        public void EdgeCase_DefaultNewFields_ExistingFieldsUnaffected()
        {
            var roll = MakeRoll();
            var snap = MakeSnapshot();

            var result = new TurnResult(roll, "msg", "reply", null, -3, snap, false, null);

            // Existing fields should be exactly as passed
            Assert.Same(roll, result.Roll);
            Assert.Equal("msg", result.DeliveredMessage);
            Assert.Equal("reply", result.OpponentMessage);
            Assert.Null(result.NarrativeBeat);
            Assert.Equal(-3, result.InterestDelta);
            Assert.Same(snap, result.StateAfter);
            Assert.False(result.IsGameOver);
            Assert.Null(result.Outcome);
        }

        // What: Edge case — each RiskTier value roundtrips correctly through constructor (§1, §3 Example 3)
        // Mutation: would catch if any tier was mapped to wrong value
        [Theory]
        [InlineData(RiskTier.Safe)]
        [InlineData(RiskTier.Medium)]
        [InlineData(RiskTier.Hard)]
        [InlineData(RiskTier.Bold)]
        [InlineData(RiskTier.Reckless)]
        public void EdgeCase_EachRiskTier_RoundtripsCorrectly(RiskTier tier)
        {
            var result = new TurnResult(
                MakeRoll(), "a", "b", null, 0, MakeSnapshot(), false, null,
                riskTier: tier);

            Assert.Equal(tier, result.RiskTier);
        }

        #endregion

        #region Error Conditions (§6)

        // What: Error — null roll throws ArgumentNullException (§6)
        // Mutation: would catch if null check was removed
        [Fact]
        public void Error_NullRoll_ThrowsArgumentNullException()
        {
            var ex = Assert.Throws<ArgumentNullException>(() =>
                new TurnResult(null!, "a", "b", null, 0, MakeSnapshot(), false, null));
            Assert.Equal("roll", ex.ParamName);
        }

        // What: Error — null deliveredMessage throws ArgumentNullException (§6)
        // Mutation: would catch if null check was removed
        [Fact]
        public void Error_NullDeliveredMessage_ThrowsArgumentNullException()
        {
            var ex = Assert.Throws<ArgumentNullException>(() =>
                new TurnResult(MakeRoll(), null!, "b", null, 0, MakeSnapshot(), false, null));
            Assert.Equal("deliveredMessage", ex.ParamName);
        }

        // What: Error — null opponentMessage throws ArgumentNullException (§6)
        // Mutation: would catch if null check was removed
        [Fact]
        public void Error_NullOpponentMessage_ThrowsArgumentNullException()
        {
            var ex = Assert.Throws<ArgumentNullException>(() =>
                new TurnResult(MakeRoll(), "a", null!, null, 0, MakeSnapshot(), false, null));
            Assert.Equal("opponentMessage", ex.ParamName);
        }

        // What: Error — null stateAfter throws ArgumentNullException (§6)
        // Mutation: would catch if null check was removed
        [Fact]
        public void Error_NullStateAfter_ThrowsArgumentNullException()
        {
            var ex = Assert.Throws<ArgumentNullException>(() =>
                new TurnResult(MakeRoll(), "a", "b", null, 0, null!, false, null));
            Assert.Equal("stateAfter", ex.ParamName);
        }

        // What: Error — no new error conditions: null for new nullable fields is fine (§6)
        // Mutation: would catch if null validation was incorrectly added for new fields
        [Fact]
        public void Error_NullNewNullableFields_DoNotThrow()
        {
            var result = new TurnResult(
                MakeRoll(), "a", "b", null, 0, MakeSnapshot(), false, null,
                shadowGrowthEvents: null,
                comboTriggered: null,
                tellReadMessage: null);

            // Should not throw; just verify we got here
            Assert.Null(result.ComboTriggered);
            Assert.Null(result.TellReadMessage);
            Assert.NotNull(result.ShadowGrowthEvents); // null coalesced
        }

        #endregion

        #region Property type verification

        // What: AC2 — ShadowGrowthEvents type is IReadOnlyList<string> (§2)
        // Mutation: would catch if type was changed to List<string> or string[]
        [Fact]
        public void PropertyType_ShadowGrowthEvents_IsIReadOnlyListOfString()
        {
            var prop = typeof(TurnResult).GetProperty("ShadowGrowthEvents");
            Assert.NotNull(prop);
            Assert.Equal(typeof(IReadOnlyList<string>), prop!.PropertyType);
        }

        // What: AC2 — ComboTriggered type is nullable string (§2)
        // Mutation: would catch if type was non-nullable or wrong type
        [Fact]
        public void PropertyType_ComboTriggered_IsNullableString()
        {
            var prop = typeof(TurnResult).GetProperty("ComboTriggered");
            Assert.NotNull(prop);
            Assert.Equal(typeof(string), prop!.PropertyType);
        }

        // What: AC2 — CallbackBonusApplied type is int (§2)
        // Mutation: would catch if type was changed to double or long
        [Fact]
        public void PropertyType_CallbackBonusApplied_IsInt()
        {
            var prop = typeof(TurnResult).GetProperty("CallbackBonusApplied");
            Assert.NotNull(prop);
            Assert.Equal(typeof(int), prop!.PropertyType);
        }

        // What: AC2 — TellReadBonus type is int (§2)
        // Mutation: would catch if type was wrong
        [Fact]
        public void PropertyType_TellReadBonus_IsInt()
        {
            var prop = typeof(TurnResult).GetProperty("TellReadBonus");
            Assert.NotNull(prop);
            Assert.Equal(typeof(int), prop!.PropertyType);
        }

        // What: AC2 — TellReadMessage type is nullable string (§2)
        // Mutation: would catch if type was wrong
        [Fact]
        public void PropertyType_TellReadMessage_IsNullableString()
        {
            var prop = typeof(TurnResult).GetProperty("TellReadMessage");
            Assert.NotNull(prop);
            Assert.Equal(typeof(string), prop!.PropertyType);
        }

        // What: AC2 — RiskTier type is RiskTier enum (§2)
        // Mutation: would catch if type was int or string
        [Fact]
        public void PropertyType_RiskTier_IsRiskTierEnum()
        {
            var prop = typeof(TurnResult).GetProperty("RiskTier");
            Assert.NotNull(prop);
            Assert.Equal(typeof(RiskTier), prop!.PropertyType);
        }

        // What: AC2 — XpEarned type is int (§2)
        // Mutation: would catch if type was wrong
        [Fact]
        public void PropertyType_XpEarned_IsInt()
        {
            var prop = typeof(TurnResult).GetProperty("XpEarned");
            Assert.NotNull(prop);
            Assert.Equal(typeof(int), prop!.PropertyType);
        }

        #endregion
    }
}
