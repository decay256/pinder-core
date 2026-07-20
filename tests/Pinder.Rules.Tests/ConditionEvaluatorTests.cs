using System.Collections.Generic;
using Xunit;
using Pinder.Rules;

namespace Pinder.Rules.Tests
{
    public class ConditionEvaluatorTests
    {
        [Fact]
        public void Evaluate_NullCondition_ReturnsFalse()
        {
            var state = new GameState();
            Assert.False(ConditionEvaluator.Evaluate(null, state));
        }

        [Fact]
        public void Evaluate_EmptyCondition_ReturnsFalse()
        {
            var state = new GameState();
            Assert.False(ConditionEvaluator.Evaluate(new Dictionary<string, object>(), state));
        }

        [Fact]
        public void MissRange_InRange_ReturnsTrue()
        {
            var cond = new Dictionary<string, object>
            {
                ["miss_range"] = new List<object> { 1, 5 }
            };
            var state = new GameState(missMargin: 3);
            Assert.True(ConditionEvaluator.Evaluate(cond, state));
        }

        [Fact]
        public void MissRange_AtLowerBound_ReturnsTrue()
        {
            var cond = new Dictionary<string, object>
            {
                ["miss_range"] = new List<object> { 1, 5 }
            };
            var state = new GameState(missMargin: 1);
            Assert.True(ConditionEvaluator.Evaluate(cond, state));
        }

        [Fact]
        public void MissRange_AtUpperBound_ReturnsTrue()
        {
            var cond = new Dictionary<string, object>
            {
                ["miss_range"] = new List<object> { 1, 5 }
            };
            var state = new GameState(missMargin: 5);
            Assert.True(ConditionEvaluator.Evaluate(cond, state));
        }

        [Fact]
        public void MissRange_OutOfRange_ReturnsFalse()
        {
            var cond = new Dictionary<string, object>
            {
                ["miss_range"] = new List<object> { 1, 5 }
            };
            var state = new GameState(missMargin: 6);
            Assert.False(ConditionEvaluator.Evaluate(cond, state));
        }

        [Fact]
        public void MissMinimum_AtMinimum_ReturnsTrue()
        {
            var cond = new Dictionary<string, object>
            {
                ["miss_minimum"] = 10
            };
            var state = new GameState(missMargin: 10);
            Assert.True(ConditionEvaluator.Evaluate(cond, state));
        }

        [Fact]
        public void MissMinimum_AboveMinimum_ReturnsTrue()
        {
            var cond = new Dictionary<string, object>
            {
                ["miss_minimum"] = 10
            };
            var state = new GameState(missMargin: 15);
            Assert.True(ConditionEvaluator.Evaluate(cond, state));
        }

        [Fact]
        public void MissMinimum_BelowMinimum_ReturnsFalse()
        {
            var cond = new Dictionary<string, object>
            {
                ["miss_minimum"] = 10
            };
            var state = new GameState(missMargin: 9);
            Assert.False(ConditionEvaluator.Evaluate(cond, state));
        }

        [Fact]
        public void BeatRange_InRange_ReturnsTrue()
        {
            var cond = new Dictionary<string, object>
            {
                ["beat_range"] = new List<object> { 1, 4 }
            };
            var state = new GameState(beatMargin: 2);
            Assert.True(ConditionEvaluator.Evaluate(cond, state));
        }

        [Fact]
        public void InterestRange_InRange_ReturnsTrue()
        {
            var cond = new Dictionary<string, object>
            {
                ["interest_range"] = new List<object> { 10, 15 }
            };
            var state = new GameState(interest: 12);
            Assert.True(ConditionEvaluator.Evaluate(cond, state));
        }

        [Fact]
        public void InterestRange_OutOfRange_ReturnsFalse()
        {
            var cond = new Dictionary<string, object>
            {
                ["interest_range"] = new List<object> { 10, 15 }
            };
            var state = new GameState(interest: 16);
            Assert.False(ConditionEvaluator.Evaluate(cond, state));
        }

        [Fact]
        public void NeedRange_InRange_ReturnsTrue()
        {
            var cond = new Dictionary<string, object>
            {
                ["need_range"] = new List<object> { 6, 10 }
            };
            var state = new GameState(needToHit: 8);
            Assert.True(ConditionEvaluator.Evaluate(cond, state));
        }

        [Fact]
        public void LevelRange_InRange_ReturnsTrue()
        {
            var cond = new Dictionary<string, object>
            {
                ["level_range"] = new List<object> { 3, 4 }
            };
            var state = new GameState(level: 3);
            Assert.True(ConditionEvaluator.Evaluate(cond, state));
        }

        [Fact]
        public void LevelRange_OutOfRange_ReturnsFalse()
        {
            var cond = new Dictionary<string, object>
            {
                ["level_range"] = new List<object> { 3, 4 }
            };
            var state = new GameState(level: 5);
            Assert.False(ConditionEvaluator.Evaluate(cond, state));
        }

        [Fact]
        public void NaturalRoll_Matches_ReturnsTrue()
        {
            var cond = new Dictionary<string, object>
            {
                ["natural_roll"] = 1
            };
            var state = new GameState(naturalRoll: 1);
            Assert.True(ConditionEvaluator.Evaluate(cond, state));
        }

        [Fact]
        public void NaturalRoll_DoesNotMatch_ReturnsFalse()
        {
            var cond = new Dictionary<string, object>
            {
                ["natural_roll"] = 1
            };
            var state = new GameState(naturalRoll: 5);
            Assert.False(ConditionEvaluator.Evaluate(cond, state));
        }

        [Fact]
        public void Streak_Matches_ReturnsTrue()
        {
            var cond = new Dictionary<string, object>
            {
                ["streak"] = 3
            };
            var state = new GameState(streak: 3);
            Assert.True(ConditionEvaluator.Evaluate(cond, state));
        }

        [Fact]
        public void StreakMinimum_AtMinimum_ReturnsTrue()
        {
            var cond = new Dictionary<string, object>
            {
                ["streak_minimum"] = 3
            };
            var state = new GameState(streak: 3);
            Assert.True(ConditionEvaluator.Evaluate(cond, state));
        }

        [Fact]
        public void StreakMinimum_BelowMinimum_ReturnsFalse()
        {
            var cond = new Dictionary<string, object>
            {
                ["streak_minimum"] = 3
            };
            var state = new GameState(streak: 2);
            Assert.False(ConditionEvaluator.Evaluate(cond, state));
        }

        [Fact]
        public void Action_Matches_CaseInsensitive_ReturnsTrue()
        {
            var cond = new Dictionary<string, object>
            {
                ["action"] = "Read"
            };
            var state = new GameState(action: "read");
            Assert.True(ConditionEvaluator.Evaluate(cond, state));
        }

        [Fact]
        public void Action_DoesNotMatch_ReturnsFalse()
        {
            var cond = new Dictionary<string, object>
            {
                ["action"] = "Read"
            };
            var state = new GameState(action: "Recover");
            Assert.False(ConditionEvaluator.Evaluate(cond, state));
        }

        [Fact]
        public void ConversationStart_True_ReturnsTrue()
        {
            var cond = new Dictionary<string, object>
            {
                ["conversation_start"] = true
            };
            var state = new GameState(isConversationStart: true);
            Assert.True(ConditionEvaluator.Evaluate(cond, state));
        }

        [Fact]
        public void ConversationStart_False_ReturnsFalse()
        {
            var cond = new Dictionary<string, object>
            {
                ["conversation_start"] = true
            };
            var state = new GameState(isConversationStart: false);
            Assert.False(ConditionEvaluator.Evaluate(cond, state));
        }

        [Fact]
        public void ConversationStart_ExplicitFalse_MatchesNonStart()
        {
            var cond = new Dictionary<string, object>
            {
                ["conversation_start"] = false
            };
            var state = new GameState(isConversationStart: false);
            Assert.True(ConditionEvaluator.Evaluate(cond, state));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("yes")]
        [InlineData("1")]
        [InlineData(0)]
        public void ConversationStart_MalformedBoolean_ThrowsFormatException(object? value)
        {
            var cond = new Dictionary<string, object>
            {
                ["conversation_start"] = value!
            };

            var ex = Assert.Throws<System.FormatException>(() =>
                ConditionEvaluator.Evaluate(cond, new GameState()));

            Assert.Contains("conversation_start", ex.Message);
            Assert.Contains("boolean", ex.Message);
        }

        [Fact]
        public void MultipleConditions_AllMatch_ReturnsTrue()
        {
            var cond = new Dictionary<string, object>
            {
                ["miss_range"] = new List<object> { 1, 5 },
                ["natural_roll"] = 5
            };
            var state = new GameState(missMargin: 3, naturalRoll: 5);
            Assert.True(ConditionEvaluator.Evaluate(cond, state));
        }

        [Fact]
        public void MultipleConditions_OneFails_ReturnsFalse()
        {
            var cond = new Dictionary<string, object>
            {
                ["miss_range"] = new List<object> { 1, 5 },
                ["natural_roll"] = 1
            };
            var state = new GameState(missMargin: 3, naturalRoll: 5);
            Assert.False(ConditionEvaluator.Evaluate(cond, state));
        }

        [Fact]
        public void UnknownKey_ThrowsFormatException()
        {
            var cond = new Dictionary<string, object>
            {
                ["unknown_key"] = "whatever",
                ["natural_roll"] = 5
            };
            var state = new GameState(naturalRoll: 5);
            var ex = Assert.Throws<System.FormatException>(() => ConditionEvaluator.Evaluate(cond, state));
            Assert.Contains("unknown_key", ex.Message);
        }

        [Fact]
        public void MalformedNumericScalar_ThrowsFormatException()
        {
            var cond = new Dictionary<string, object>
            {
                ["natural_roll"] = "oops"
            };
            var state = new GameState(naturalRoll: 5);
            var ex = Assert.Throws<System.FormatException>(() => ConditionEvaluator.Evaluate(cond, state));
            Assert.Contains("natural_roll", ex.Message);
            Assert.Contains("oops", ex.Message);
        }

        [Fact]
        public void MalformedRangeElement_ThrowsFormatException()
        {
            var cond = new Dictionary<string, object>
            {
                ["miss_range"] = new List<object> { "oops", 5 }
            };
            var state = new GameState(missMargin: 3);
            var ex = Assert.Throws<System.FormatException>(() => ConditionEvaluator.Evaluate(cond, state));
            Assert.Contains("range lower bound", ex.Message);
            Assert.Contains("oops", ex.Message);
        }

        [Fact]
        public void NumericCoercion_AcceptsIntegerStringsForScalarAndRangeConditions()
        {
            var cond = new Dictionary<string, object>
            {
                ["natural_roll"] = "20",
                ["miss_range"] = new List<object> { "1", "3" }
            };

            Assert.True(ConditionEvaluator.Evaluate(cond, new GameState(naturalRoll: 20, missMargin: 2)));
        }

        [Fact]
        public void NumericCoercion_RejectsFractionalScalarCondition()
        {
            var cond = new Dictionary<string, object>
            {
                ["natural_roll"] = 20.5
            };

            var ex = Assert.Throws<System.FormatException>(() =>
                ConditionEvaluator.Evaluate(cond, new GameState(naturalRoll: 20)));

            Assert.Contains("natural_roll", ex.Message);
            Assert.Contains("whole number", ex.Message);
        }

        [Fact]
        public void NumericCoercion_RejectsNonFiniteRangeCondition()
        {
            var cond = new Dictionary<string, object>
            {
                ["miss_range"] = new List<object> { 1, double.PositiveInfinity }
            };

            var ex = Assert.Throws<System.FormatException>(() =>
                ConditionEvaluator.Evaluate(cond, new GameState(missMargin: 3)));

            Assert.Contains("range upper bound", ex.Message);
            Assert.Contains("finite", ex.Message);
        }
    }
}
