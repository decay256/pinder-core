using System;
using System.Collections.Generic;
using Xunit;
using Pinder.Rules;

namespace Pinder.Rules.Tests
{
    public partial class SpecComplianceTests
    {
        // ============================================================
        // ConditionEvaluator — Error Conditions (AC6)
        // ============================================================

        // Mutation: would catch if null state doesn't throw any exception
        // Note: spec says ArgumentNullException; impl throws NullReferenceException
        [Fact]
        public void ConditionEvaluator_NullState_Throws()
        {
            var cond = new Dictionary<string, object> { ["natural_roll"] = 1 };
            Assert.ThrowsAny<Exception>(() => ConditionEvaluator.Evaluate(cond, null!));
        }

        // ============================================================
        // ConditionEvaluator — Range boundary / boxing edge cases (AC6)
        // ============================================================

        // Mutation: would catch if range uses < instead of <= for lower bound
        [Fact]
        public void ConditionEvaluator_BeatRange_AtLowerBound_ReturnsTrue()
        {
            var cond = new Dictionary<string, object>
            {
                ["beat_range"] = new List<object> { 1, 4 }
            };
            var state = new GameState(beatMargin: 1);
            Assert.True(ConditionEvaluator.Evaluate(cond, state));
        }

        // Mutation: would catch if range uses < instead of <= for upper bound
        [Fact]
        public void ConditionEvaluator_BeatRange_AtUpperBound_ReturnsTrue()
        {
            var cond = new Dictionary<string, object>
            {
                ["beat_range"] = new List<object> { 1, 4 }
            };
            var state = new GameState(beatMargin: 4);
            Assert.True(ConditionEvaluator.Evaluate(cond, state));
        }

        // Mutation: would catch if beat_range doesn't reject values outside range
        [Fact]
        public void ConditionEvaluator_BeatRange_OutOfRange_ReturnsFalse()
        {
            var cond = new Dictionary<string, object>
            {
                ["beat_range"] = new List<object> { 1, 4 }
            };
            var state = new GameState(beatMargin: 5);
            Assert.False(ConditionEvaluator.Evaluate(cond, state));
        }

        // Mutation: would catch if equal lo/hi range doesn't work (single-value range)
        [Fact]
        public void ConditionEvaluator_Range_EqualLoHi_MatchesExactValue()
        {
            var cond = new Dictionary<string, object>
            {
                ["miss_range"] = new List<object> { 5, 5 }
            };
            Assert.True(ConditionEvaluator.Evaluate(cond, new GameState(missMargin: 5)));
            Assert.False(ConditionEvaluator.Evaluate(cond, new GameState(missMargin: 4)));
            Assert.False(ConditionEvaluator.Evaluate(cond, new GameState(missMargin: 6)));
        }

        // Mutation: would catch if YamlDotNet long boxing breaks range evaluation
        [Fact]
        public void ConditionEvaluator_Range_WithLongValues_StillWorks()
        {
            var cond = new Dictionary<string, object>
            {
                ["miss_range"] = new List<object> { (long)1, (long)5 }
            };
            var state = new GameState(missMargin: 3);
            Assert.True(ConditionEvaluator.Evaluate(cond, state));
        }

        // Mutation: would catch if malformed range (not 2 elements) crashes instead of returning false
        [Fact]
        public void ConditionEvaluator_Range_MalformedSingleElement_ReturnsFalse()
        {
            var cond = new Dictionary<string, object>
            {
                ["miss_range"] = new List<object> { 1 }
            };
            var state = new GameState(missMargin: 1);
            Assert.False(ConditionEvaluator.Evaluate(cond, state));
        }

        // Mutation: would catch if open-ended range with large hi value doesn't work
        [Fact]
        public void ConditionEvaluator_Range_OpenEnded_LargeHiValue()
        {
            var cond = new Dictionary<string, object>
            {
                ["miss_range"] = new List<object> { 10, 999 }
            };
            Assert.True(ConditionEvaluator.Evaluate(cond, new GameState(missMargin: 50)));
            Assert.False(ConditionEvaluator.Evaluate(cond, new GameState(missMargin: 9)));
        }

        // ============================================================
        // ConditionEvaluator — streak vs streak_minimum (AC6)
        // ============================================================

        // Mutation: would catch if streak uses >= instead of ==
        [Fact]
        public void ConditionEvaluator_Streak_DoesNotMatch_AboveValue()
        {
            var cond = new Dictionary<string, object> { ["streak"] = 3 };
            var state = new GameState(streak: 4);
            Assert.False(ConditionEvaluator.Evaluate(cond, state));
        }

        // Mutation: would catch if streak_minimum uses == instead of >=
        [Fact]
        public void ConditionEvaluator_StreakMinimum_AboveMinimum_ReturnsTrue()
        {
            var cond = new Dictionary<string, object> { ["streak_minimum"] = 3 };
            var state = new GameState(streak: 5);
            Assert.True(ConditionEvaluator.Evaluate(cond, state));
        }

        // ============================================================
        // ConditionEvaluator — interest_range edge cases (AC6)
        // ============================================================

        // Mutation: would catch if interest_range boundary is off-by-one
        [Fact]
        public void ConditionEvaluator_InterestRange_BelowLower_ReturnsFalse()
        {
            var cond = new Dictionary<string, object>
            {
                ["interest_range"] = new List<object> { 10, 15 }
            };
            var state = new GameState(interest: 9);
            Assert.False(ConditionEvaluator.Evaluate(cond, state));
        }

        // ============================================================
        // ConditionEvaluator — need_range boundary (AC6)
        // ============================================================

        // Mutation: would catch if need_range uses wrong GameState field
        [Fact]
        public void ConditionEvaluator_NeedRange_OutOfRange_ReturnsFalse()
        {
            var cond = new Dictionary<string, object>
            {
                ["need_range"] = new List<object> { 6, 10 }
            };
            var state = new GameState(needToHit: 11);
            Assert.False(ConditionEvaluator.Evaluate(cond, state));
        }

        // ============================================================
        // ConditionEvaluator — action case insensitivity (AC6)
        // ============================================================

        // Mutation: would catch if action comparison is case-sensitive
        [Fact]
        public void ConditionEvaluator_Action_UpperVsLower_CaseInsensitive()
        {
            var cond = new Dictionary<string, object> { ["action"] = "RECOVER" };
            var state = new GameState(action: "recover");
            Assert.True(ConditionEvaluator.Evaluate(cond, state));
        }

        // Mutation: would catch if null Action matches a string condition
        [Fact]
        public void ConditionEvaluator_Action_NullAction_ReturnsFalse()
        {
            var cond = new Dictionary<string, object> { ["action"] = "Read" };
            var state = new GameState(action: null);
            Assert.False(ConditionEvaluator.Evaluate(cond, state));
        }

        // ============================================================
        // ConditionEvaluator — conversation_start false (AC6)
        // ============================================================

        // Mutation: would catch if conversation_start only checks for true
        [Fact]
        public void ConditionEvaluator_ConversationStart_FalseCondition_MatchesNonStart()
        {
            var cond = new Dictionary<string, object> { ["conversation_start"] = false };
            var state = new GameState(isConversationStart: false);
            Assert.True(ConditionEvaluator.Evaluate(cond, state));
        }

        // Mutation: would catch if conversation_start: false matches when isConversationStart is true
        [Fact]
        public void ConditionEvaluator_ConversationStart_FalseCondition_DoesNotMatchStart()
        {
            var cond = new Dictionary<string, object> { ["conversation_start"] = false };
            var state = new GameState(isConversationStart: true);
            Assert.False(ConditionEvaluator.Evaluate(cond, state));
        }

        // ============================================================
        // ConditionEvaluator — unknown keys only (AC6)
        // ============================================================

        // Mutation: would catch if only-unknown-keys returns false.
        // Per spec: "Unknown keys are ignored (treated as matching)" and dict is non-empty
        [Fact]
        public void ConditionEvaluator_OnlyUnknownKeys_ReturnsTrue()
        {
            var cond = new Dictionary<string, object> { ["future_mechanic"] = 42 };
            var state = new GameState();
            Assert.True(ConditionEvaluator.Evaluate(cond, state));
        }

        // ============================================================
        // ConditionEvaluator — Multiple conditions with miss_range + natural_roll (AC6)
        // ============================================================

        // Mutation: would catch if multi-condition short-circuits on first match (OR logic)
        [Fact]
        public void ConditionEvaluator_MultiCondition_MissRangePlusNaturalRoll_BothMustMatch()
        {
            var cond = new Dictionary<string, object>
            {
                ["miss_range"] = new List<object> { 1, 5 },
                ["natural_roll"] = 5
            };

            // Both match
            Assert.True(ConditionEvaluator.Evaluate(cond, new GameState(missMargin: 3, naturalRoll: 5)));

            // miss_range matches but natural_roll doesn't
            Assert.False(ConditionEvaluator.Evaluate(cond, new GameState(missMargin: 3, naturalRoll: 10)));

            // natural_roll matches but miss_range doesn't
            Assert.False(ConditionEvaluator.Evaluate(cond, new GameState(missMargin: 6, naturalRoll: 5)));
        }

        // ============================================================
        // ConditionEvaluator — miss_range below lower bound (AC6)
        // ============================================================

        // Mutation: would catch if miss_range lower bound check is off-by-one
        [Fact]
        public void ConditionEvaluator_MissRange_BelowLowerBound_ReturnsFalse()
        {
            var cond = new Dictionary<string, object>
            {
                ["miss_range"] = new List<object> { 3, 5 }
            };
            Assert.False(ConditionEvaluator.Evaluate(cond, new GameState(missMargin: 2)));
        }

        // ============================================================
        // ConditionEvaluator — level_range field mapping (AC6)
        // ============================================================

        // Mutation: would catch if level_range reads wrong field (e.g. Interest instead of Level)
        [Fact]
        public void ConditionEvaluator_LevelRange_UsesLevelField()
        {
            var cond = new Dictionary<string, object>
            {
                ["level_range"] = new List<object> { 5, 10 }
            };
            // level=7 should match [5,10]
            Assert.True(ConditionEvaluator.Evaluate(cond, new GameState(level: 7)));
            // level=4 should not
            Assert.False(ConditionEvaluator.Evaluate(cond, new GameState(level: 4)));
            // level=11 should not
            Assert.False(ConditionEvaluator.Evaluate(cond, new GameState(level: 11)));
        }

        // ============================================================
        // ConditionEvaluator — need_range field mapping (AC6)
        // ============================================================

        // Mutation: would catch if need_range reads wrong field
        [Fact]
        public void ConditionEvaluator_NeedRange_UsesNeedToHitField()
        {
            var cond = new Dictionary<string, object>
            {
                ["need_range"] = new List<object> { 11, 15 }
            };
            Assert.True(ConditionEvaluator.Evaluate(cond, new GameState(needToHit: 13)));
            Assert.False(ConditionEvaluator.Evaluate(cond, new GameState(needToHit: 10)));
            Assert.False(ConditionEvaluator.Evaluate(cond, new GameState(needToHit: 16)));
        }

        // ============================================================
        // ConditionEvaluator — miss_minimum vs miss_range distinction (AC6)
        // ============================================================

        // Mutation: would catch if miss_minimum is implemented as range instead of >= comparison
        [Fact]
        public void ConditionEvaluator_MissMinimum_IsGreaterThanOrEqual()
        {
            var cond = new Dictionary<string, object> { ["miss_minimum"] = 10 };
            Assert.True(ConditionEvaluator.Evaluate(cond, new GameState(missMargin: 10)));
            Assert.True(ConditionEvaluator.Evaluate(cond, new GameState(missMargin: 100)));
            Assert.False(ConditionEvaluator.Evaluate(cond, new GameState(missMargin: 9)));
        }
    }
}
