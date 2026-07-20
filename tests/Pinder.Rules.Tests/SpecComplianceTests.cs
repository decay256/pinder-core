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
    public partial class SpecComplianceTests
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

        // Duplicate IDs are malformed rule configuration and must fail at load time.
        [Fact]
        public void RuleBook_DuplicateIds_ThrowsFormatException()
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
            var ex = Assert.Throws<FormatException>(() => RuleBook.LoadFrom(yaml));
            Assert.Contains("Duplicate rule id", ex.Message);
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
        public void GameState_ValuesDriveCompositeRuleConditions()
        {
            var cond = new Dictionary<string, object>
            {
                ["interest_range"] = new List<object> { 10, 20 },
                ["beat_range"] = new List<object> { 5, 9 },
                ["natural_roll"] = 20,
                ["need_range"] = new List<object> { 10, 15 },
                ["level_range"] = new List<object> { 5, 7 },
                ["streak_minimum"] = 3,
                ["action"] = "read",
                ["conversation_start"] = true
            };
            var state = new GameState(
                interest: 15,
                beatMargin: 5,
                naturalRoll: 20,
                needToHit: 12,
                level: 5,
                streak: 3,
                action: "Read",
                isConversationStart: true);

            Assert.True(ConditionEvaluator.Evaluate(cond, state));
            Assert.False(ConditionEvaluator.Evaluate(cond, new GameState(
                interest: 15,
                beatMargin: 5,
                naturalRoll: 19,
                needToHit: 12,
                level: 5,
                streak: 3,
                action: "Read",
                isConversationStart: true)));
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
    }
}
