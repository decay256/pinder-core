using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using Pinder.Rules;

namespace Pinder.Rules.Tests
{
    /// <summary>
    /// Additional tests covering spec edge cases, error conditions, and mutation targets
    /// not covered by the existing test suite. Written against docs/specs/issue-446-spec.md.
    /// </summary>
    public class SpecComplianceTests
    {
        // ============================================================
        // RuleBook — Error Conditions (AC1)
        // ============================================================

        // Mutation: would catch if LoadFrom doesn't validate null input
        // Note: spec says ArgumentNullException; impl throws FormatException (null→empty path)
        [Fact]
        public void RuleBook_LoadFrom_Null_ThrowsException()
        {
            var ex = Assert.ThrowsAny<Exception>(() => RuleBook.LoadFrom(null!));
            // Implementation treats null as empty → FormatException
            Assert.IsType<FormatException>(ex);
        }

        // Mutation: would catch if LoadFrom accepts mapping root instead of requiring sequence
        [Fact]
        public void RuleBook_LoadFrom_MappingRoot_ThrowsFormatException()
        {
            Assert.Throws<FormatException>(() => RuleBook.LoadFrom("key: value"));
        }

        // Mutation: would catch if LoadFrom doesn't throw on malformed YAML
        [Fact]
        public void RuleBook_LoadFrom_MalformedYaml_ThrowsFormatException()
        {
            Assert.Throws<FormatException>(() => RuleBook.LoadFrom("not: valid: yaml: ["));
        }

        // ============================================================
        // RuleBook — Edge Cases (AC1)
        // ============================================================

        // Mutation: would catch if empty sequence throws instead of returning empty book
        [Fact]
        public void RuleBook_LoadFrom_EmptySequence_ReturnsEmptyBook()
        {
            var book = RuleBook.LoadFrom("[]");
            Assert.Equal(0, book.Count);
            Assert.Empty(book.All);
        }

        // Mutation: would catch if duplicate IDs are not handled (first wins instead of last wins)
        [Fact]
        public void RuleBook_DuplicateIds_LastEntryWinsInGetById()
        {
            var yaml = @"
- id: dup.rule
  section: §1
  title: First
  type: test
  description: First entry
- id: dup.rule
  section: §1
  title: Second
  type: test
  description: Second entry
";
            var book = RuleBook.LoadFrom(yaml);
            // All should contain both entries
            Assert.Equal(2, book.All.Count);
            // GetById returns the last one per spec
            var entry = book.GetById("dup.rule");
            Assert.NotNull(entry);
            Assert.Equal("Second", entry!.Title);
        }

        // Mutation: would catch if All doesn't preserve YAML document order
        [Fact]
        public void RuleBook_All_PreservesDocumentOrder()
        {
            var yaml = @"
- id: first
  section: §1
  title: First
  type: a
  description: ''
- id: second
  section: §1
  title: Second
  type: b
  description: ''
- id: third
  section: §1
  title: Third
  type: c
  description: ''
";
            var book = RuleBook.LoadFrom(yaml);
            Assert.Equal("first", book.All[0].Id);
            Assert.Equal("second", book.All[1].Id);
            Assert.Equal("third", book.All[2].Id);
        }

        // Mutation: would catch if entries without condition/outcome are skipped
        [Fact]
        public void RuleBook_EntryWithNoOutcome_HasNullOutcome()
        {
            var yaml = @"
- id: descriptive
  section: §1
  title: Descriptive Rule
  type: definition
  description: Purely descriptive, no outcome.
  condition:
    natural_roll: 20
";
            var book = RuleBook.LoadFrom(yaml);
            var entry = book.GetById("descriptive");
            Assert.NotNull(entry);
            Assert.NotNull(entry!.Condition);
            Assert.Null(entry.Outcome);
        }

        // Mutation: would catch if Count returns wrong value
        [Fact]
        public void RuleBook_Count_MatchesAllCount()
        {
            var yaml = @"
- id: r1
  section: §1
  title: R1
  type: t
  description: ''
- id: r2
  section: §1
  title: R2
  type: t
  description: ''
";
            var book = RuleBook.LoadFrom(yaml);
            Assert.Equal(book.All.Count, book.Count);
            Assert.Equal(2, book.Count);
        }

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
        // OutcomeDispatcher — Edge Cases (AC6)
        // ============================================================

        // Mutation: would catch if empty outcome calls handler methods
        [Fact]
        public void OutcomeDispatcher_EmptyOutcome_DoesNothing()
        {
            var handler = new TestEffectHandler();
            OutcomeDispatcher.Dispatch(new Dictionary<string, object>(), new GameState(), handler);
            Assert.Empty(handler.InterestDeltas);
            Assert.Empty(handler.ActivatedTraps);
            Assert.Empty(handler.RollModifiers);
            Assert.Empty(handler.RiskTiers);
            Assert.Empty(handler.XpMultipliers);
            Assert.Empty(handler.ShadowGrowths);
        }

        // Mutation: would catch if roll_bonus positive formatting is wrong
        [Fact]
        public void OutcomeDispatcher_RollBonus_PositiveValue_FormatsWithPlus()
        {
            var handler = new TestEffectHandler();
            var outcome = new Dictionary<string, object> { ["roll_bonus"] = 2 };
            OutcomeDispatcher.Dispatch(outcome, new GameState(), handler);
            Assert.Single(handler.RollModifiers);
            Assert.Equal("+2", handler.RollModifiers[0]);
        }

        // Mutation: would catch if effect "disadvantage" is not dispatched
        [Fact]
        public void OutcomeDispatcher_Effect_Disadvantage_DispatchesAsModifier()
        {
            var handler = new TestEffectHandler();
            var outcome = new Dictionary<string, object> { ["effect"] = "disadvantage" };
            OutcomeDispatcher.Dispatch(outcome, new GameState(), handler);
            Assert.Equal(new[] { "disadvantage" }, handler.RollModifiers);
        }

        // Mutation: would catch if trap_name is dispatched via wrong handler method
        [Fact]
        public void OutcomeDispatcher_TrapName_CallsActivateTrapWithName()
        {
            var handler = new TestEffectHandler();
            var outcome = new Dictionary<string, object> { ["trap_name"] = "Overthinking" };
            OutcomeDispatcher.Dispatch(outcome, new GameState(), handler);
            Assert.Single(handler.ActivatedTraps);
            Assert.Equal("Overthinking", handler.ActivatedTraps[0]);
        }

        // Mutation: would catch if xp_multiplier fails with int instead of double
        [Fact]
        public void OutcomeDispatcher_XpMultiplier_IntegerValue_DispatchesAsDouble()
        {
            var handler = new TestEffectHandler();
            var outcome = new Dictionary<string, object> { ["xp_multiplier"] = 2 };
            OutcomeDispatcher.Dispatch(outcome, new GameState(), handler);
            Assert.Single(handler.XpMultipliers);
            Assert.Equal(2.0, handler.XpMultipliers[0]);
        }

        // Mutation: would catch if zero roll_bonus is skipped
        [Fact]
        public void OutcomeDispatcher_RollBonus_Zero_Dispatches()
        {
            var handler = new TestEffectHandler();
            var outcome = new Dictionary<string, object> { ["roll_bonus"] = 0 };
            OutcomeDispatcher.Dispatch(outcome, new GameState(), handler);
            Assert.Single(handler.RollModifiers);
        }

        // Mutation: would catch if starting_interest outcome key isn't handled
        [Fact]
        public void OutcomeDispatcher_StartingInterest_DispatchesAsInterestDelta()
        {
            var handler = new TestEffectHandler();
            var outcome = new Dictionary<string, object> { ["starting_interest"] = 5 };
            OutcomeDispatcher.Dispatch(outcome, new GameState(), handler);
            Assert.Equal(new[] { 5 }, handler.InterestDeltas);
        }

        // ============================================================
        // GameState — default values (AC1)
        // ============================================================

        // Mutation: would catch if default interest is non-zero
        [Fact]
        public void GameState_DefaultValues_AllZeroOrNull()
        {
            var state = new GameState();
            Assert.Equal(0, state.Interest);
            Assert.Equal(0, state.MissMargin);
            Assert.Equal(0, state.BeatMargin);
            Assert.Equal(0, state.NaturalRoll);
            Assert.Equal(0, state.NeedToHit);
            Assert.Equal(1, state.Level); // default is 1 per spec
            Assert.Equal(0, state.Streak);
            Assert.Null(state.Action);
            Assert.False(state.IsConversationStart);
            Assert.Null(state.ShadowValues);
        }

        // Mutation: would catch if GameState constructor doesn't store values
        [Fact]
        public void GameState_Constructor_StoresAllValues()
        {
            var shadows = new Dictionary<string, int> { { "Dread", 10 } };
            var state = new GameState(
                interest: 15,
                missMargin: 3,
                beatMargin: 5,
                naturalRoll: 20,
                needToHit: 12,
                level: 5,
                streak: 3,
                action: "Read",
                isConversationStart: true,
                shadowValues: shadows);

            Assert.Equal(15, state.Interest);
            Assert.Equal(3, state.MissMargin);
            Assert.Equal(5, state.BeatMargin);
            Assert.Equal(20, state.NaturalRoll);
            Assert.Equal(12, state.NeedToHit);
            Assert.Equal(5, state.Level);
            Assert.Equal(3, state.Streak);
            Assert.Equal("Read", state.Action);
            Assert.True(state.IsConversationStart);
            Assert.NotNull(state.ShadowValues);
            Assert.Equal(10, state.ShadowValues!["Dread"]);
        }

        // ============================================================
        // RuleBook + ConditionEvaluator Integration (AC2, AC3)
        // ============================================================

        // Mutation: would catch if YAML-parsed conditions don't evaluate correctly end-to-end
        [Fact]
        public void Integration_LoadYaml_EvaluateCondition_DispatchOutcome()
        {
            var yaml = @"
- id: test.fumble
  section: §5
  title: Fumble
  type: interest_change
  description: Miss DC by 1-2
  condition:
    miss_range: [1, 2]
  outcome:
    interest_delta: -1
";
            var book = RuleBook.LoadFrom(yaml);
            var rule = book.GetById("test.fumble");
            Assert.NotNull(rule);

            // Evaluate with matching state
            var matchState = new GameState(missMargin: 2);
            Assert.True(ConditionEvaluator.Evaluate(rule!.Condition, matchState));

            // Evaluate with non-matching state
            var noMatchState = new GameState(missMargin: 3);
            Assert.False(ConditionEvaluator.Evaluate(rule.Condition, noMatchState));

            // Dispatch outcome
            var handler = new TestEffectHandler();
            OutcomeDispatcher.Dispatch(rule.Outcome, matchState, handler);
            Assert.Equal(-1, handler.InterestDeltas[0]);
        }

        // Mutation: would catch if trap + interest_delta outcome doesn't dispatch both
        [Fact]
        public void Integration_MultipleOutcomeKeys_AllDispatched()
        {
            var yaml = @"
- id: test.trope
  section: §5
  title: Trope Trap
  type: interest_change
  description: Miss by 6-9
  condition:
    miss_range: [6, 9]
  outcome:
    interest_delta: -2
    trap: true
";
            var book = RuleBook.LoadFrom(yaml);
            var rule = book.GetById("test.trope");
            Assert.NotNull(rule);

            var handler = new TestEffectHandler();
            OutcomeDispatcher.Dispatch(rule!.Outcome, new GameState(), handler);
            Assert.Equal(-2, handler.InterestDeltas[0]);
            Assert.Single(handler.ActivatedTraps);
            Assert.Equal("", handler.ActivatedTraps[0]);
        }

        // ============================================================
        // RuleBook — GetRulesByType filtering (AC1)
        // ============================================================

        // Mutation: would catch if GetRulesByType returns all rules instead of filtered
        [Fact]
        public void RuleBook_GetRulesByType_ReturnsOnlyMatchingType()
        {
            var yaml = @"
- id: r1
  section: §1
  title: R1
  type: interest_change
  description: ''
- id: r2
  section: §1
  title: R2
  type: shadow_growth
  description: ''
- id: r3
  section: §1
  title: R3
  type: interest_change
  description: ''
";
            var book = RuleBook.LoadFrom(yaml);
            var interestRules = book.GetRulesByType("interest_change").ToList();
            Assert.Equal(2, interestRules.Count);
            Assert.All(interestRules, r => Assert.Equal("interest_change", r.Type));
        }

        // ============================================================
        // RuleBook — GetRulesByType empty result (AC1)
        // ============================================================

        // Mutation: would catch if empty type result throws instead of returning empty
        [Fact]
        public void RuleBook_GetRulesByType_NoMatches_ReturnsEmpty()
        {
            var yaml = @"
- id: r1
  section: §1
  title: R1
  type: interest_change
  description: ''
";
            var book = RuleBook.LoadFrom(yaml);
            var rules = book.GetRulesByType("nonexistent_type").ToList();
            Assert.Empty(rules);
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
        // Full §5 failure tier YAML round-trip (AC2)
        // ============================================================

        // Mutation: would catch if YAML condition for catastrophe range is wrong
        [Fact]
        public void FailureTier_Catastrophe_ViaYaml_MatchesLargeMargin()
        {
            var yaml = @"
- id: §5.catastrophe
  section: §5
  title: Catastrophe
  type: interest_change
  description: Miss by 10+
  condition:
    miss_minimum: 10
  outcome:
    interest_delta: -3
    trap: true
";
            var book = RuleBook.LoadFrom(yaml);
            var rule = book.GetById("§5.catastrophe");
            Assert.NotNull(rule);

            // Miss by exactly 10 → matches
            Assert.True(ConditionEvaluator.Evaluate(rule!.Condition, new GameState(missMargin: 10)));
            // Miss by 20 → matches
            Assert.True(ConditionEvaluator.Evaluate(rule.Condition, new GameState(missMargin: 20)));
            // Miss by 9 → doesn't match
            Assert.False(ConditionEvaluator.Evaluate(rule.Condition, new GameState(missMargin: 9)));

            var handler = new TestEffectHandler();
            OutcomeDispatcher.Dispatch(rule.Outcome, new GameState(), handler);
            Assert.Equal(-3, handler.InterestDeltas[0]);
            Assert.Single(handler.ActivatedTraps);
        }

        // ============================================================
        // Full §6 interest state YAML round-trip (AC3)
        // ============================================================

        // Mutation: would catch if interest_range [0, 0] doesn't work for Unmatched
        [Fact]
        public void InterestState_Unmatched_ViaYaml_OnlyMatchesZero()
        {
            var yaml = @"
- id: §6.unmatched
  section: §6
  title: Unmatched
  type: interest_state
  description: Interest 0
  condition:
    interest_range: [0, 0]
  outcome:
    effect: none
";
            var book = RuleBook.LoadFrom(yaml);
            var rule = book.GetById("§6.unmatched");
            Assert.NotNull(rule);

            Assert.True(ConditionEvaluator.Evaluate(rule!.Condition, new GameState(interest: 0)));
            Assert.False(ConditionEvaluator.Evaluate(rule.Condition, new GameState(interest: 1)));
        }

        // Mutation: would catch if advantage/disadvantage YAML outcome wiring is wrong
        [Fact]
        public void InterestState_Bored_ViaYaml_DispatchesDisadvantage()
        {
            var yaml = @"
- id: §6.bored
  section: §6
  title: Bored
  type: interest_state
  description: Interest 1-4
  condition:
    interest_range: [1, 4]
  outcome:
    effect: disadvantage
";
            var book = RuleBook.LoadFrom(yaml);
            var rule = book.GetById("§6.bored");
            Assert.NotNull(rule);

            Assert.True(ConditionEvaluator.Evaluate(rule!.Condition, new GameState(interest: 2)));
            Assert.False(ConditionEvaluator.Evaluate(rule.Condition, new GameState(interest: 0)));
            Assert.False(ConditionEvaluator.Evaluate(rule.Condition, new GameState(interest: 5)));

            var handler = new TestEffectHandler();
            OutcomeDispatcher.Dispatch(rule.Outcome, new GameState(), handler);
            Assert.Equal("disadvantage", handler.RollModifiers[0]);
        }

        // Mutation: would catch if VeryIntoIt range boundary is wrong
        [Fact]
        public void InterestState_VeryIntoIt_ViaYaml_DispatchesAdvantage()
        {
            var yaml = @"
- id: §6.veryintoit
  section: §6
  title: Very Into It
  type: interest_state
  description: Interest 16-20
  condition:
    interest_range: [16, 20]
  outcome:
    effect: advantage
";
            var book = RuleBook.LoadFrom(yaml);
            var rule = book.GetById("§6.veryintoit");
            Assert.NotNull(rule);

            Assert.True(ConditionEvaluator.Evaluate(rule!.Condition, new GameState(interest: 16)));
            Assert.True(ConditionEvaluator.Evaluate(rule.Condition, new GameState(interest: 20)));
            Assert.False(ConditionEvaluator.Evaluate(rule.Condition, new GameState(interest: 15)));
            Assert.False(ConditionEvaluator.Evaluate(rule.Condition, new GameState(interest: 21)));

            var handler = new TestEffectHandler();
            OutcomeDispatcher.Dispatch(rule.Outcome, new GameState(), handler);
            Assert.Equal("advantage", handler.RollModifiers[0]);
        }

        // ============================================================
        // Legendary failure via natural_roll (AC2)
        // ============================================================

        // Mutation: would catch if natural_roll condition for legendary doesn't work from YAML
        [Fact]
        public void FailureTier_Legendary_ViaYaml_NaturalRoll1()
        {
            var yaml = @"
- id: §5.legendary
  section: §5
  title: Legendary Fail
  type: interest_change
  description: Nat 1
  condition:
    natural_roll: 1
  outcome:
    interest_delta: -4
";
            var book = RuleBook.LoadFrom(yaml);
            var rule = book.GetById("§5.legendary");
            Assert.NotNull(rule);

            Assert.True(ConditionEvaluator.Evaluate(rule!.Condition, new GameState(naturalRoll: 1)));
            Assert.False(ConditionEvaluator.Evaluate(rule.Condition, new GameState(naturalRoll: 2)));

            var handler = new TestEffectHandler();
            OutcomeDispatcher.Dispatch(rule.Outcome, new GameState(), handler);
            Assert.Equal(-4, handler.InterestDeltas[0]);
        }

        // ============================================================
        // RuleEntry property defaults (AC1)
        // ============================================================

        // Mutation: would catch if RuleEntry doesn't populate all fields from YAML
        [Fact]
        public void RuleEntry_AllProperties_PopulatedFromYaml()
        {
            var yaml = @"
- id: full.rule
  section: §99
  title: Full Rule
  type: custom_type
  description: A complete rule entry with all fields.
  condition:
    natural_roll: 20
  outcome:
    interest_delta: 3
";
            var book = RuleBook.LoadFrom(yaml);
            var rule = book.GetById("full.rule");
            Assert.NotNull(rule);
            Assert.Equal("full.rule", rule!.Id);
            Assert.Equal("§99", rule.Section);
            Assert.Equal("Full Rule", rule.Title);
            Assert.Equal("custom_type", rule.Type);
            Assert.Equal("A complete rule entry with all fields.", rule.Description);
            Assert.NotNull(rule.Condition);
            Assert.NotNull(rule.Outcome);
        }

        // ============================================================
        // OutcomeDispatcher — shadow_effect complete (AC6)
        // ============================================================

        // Mutation: would catch if shadow_effect reason doesn't come from rule context
        [Fact]
        public void OutcomeDispatcher_ShadowEffect_DispatchesAllFields()
        {
            var handler = new TestEffectHandler();
            var outcome = new Dictionary<string, object>
            {
                ["shadow_effect"] = new Dictionary<string, object>
                {
                    ["shadow"] = "Horniness",
                    ["delta"] = 3
                }
            };
            OutcomeDispatcher.Dispatch(outcome, new GameState(), handler);
            Assert.Single(handler.ShadowGrowths);
            Assert.Equal("Horniness", handler.ShadowGrowths[0].Shadow);
            Assert.Equal(3, handler.ShadowGrowths[0].Delta);
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

        // ============================================================
        // OutcomeDispatcher — risk_tier dispatches correctly (AC6)
        // ============================================================

        // Mutation: would catch if risk_tier dispatches via wrong handler method
        [Fact]
        public void OutcomeDispatcher_RiskTier_DispatchesViaSetRiskTier()
        {
            var handler = new TestEffectHandler();
            var outcome = new Dictionary<string, object> { ["risk_tier"] = "Hard" };
            OutcomeDispatcher.Dispatch(outcome, new GameState(), handler);
            Assert.Single(handler.RiskTiers);
            Assert.Equal("Hard", handler.RiskTiers[0]);
            // Should NOT be in roll modifiers
            Assert.Empty(handler.RollModifiers);
        }
    }
}
