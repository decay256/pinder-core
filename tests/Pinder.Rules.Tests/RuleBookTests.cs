using Xunit;
using Pinder.Rules;
using System.Linq;

namespace Pinder.Rules.Tests
{
    public class RuleBookTests
    {
        private const string SimpleYaml = @"
- id: test.rule1
  section: §1
  title: Test Rule One
  type: interest_change
  description: A test rule.
  condition:
    miss_range: [1, 2]
  outcome:
    interest_delta: -1
- id: test.rule2
  section: §2
  title: Test Rule Two
  type: roll_modifier
  description: Another test rule.
  condition:
    level_range: [3, 4]
  outcome:
    level_bonus: 1
";

        [Fact]
        public void LoadFrom_ValidYaml_ParsesAllEntries()
        {
            var book = RuleBook.LoadFrom(SimpleYaml);
            Assert.Equal(2, book.Count);
        }

        [Fact]
        public void GetById_ExistingId_ReturnsEntry()
        {
            var book = RuleBook.LoadFrom(SimpleYaml);
            var entry = book.GetById("test.rule1");
            Assert.NotNull(entry);
            Assert.Equal("test.rule1", entry!.Id);
            Assert.Equal("§1", entry.Section);
            Assert.Equal("Test Rule One", entry.Title);
            Assert.Equal("interest_change", entry.Type);
            Assert.Equal("A test rule.", entry.Description);
        }

        [Fact]
        public void GetById_NonExistentId_ReturnsNull()
        {
            var book = RuleBook.LoadFrom(SimpleYaml);
            Assert.Null(book.GetById("nonexistent"));
        }

        [Fact]
        public void GetById_IsCaseInsensitive()
        {
            var book = RuleBook.LoadFrom(SimpleYaml);
            var entry = book.GetById("TEST.RULE1");
            Assert.NotNull(entry);
        }

        [Fact]
        public void GetRulesByType_ExistingType_ReturnsMatchingEntries()
        {
            var book = RuleBook.LoadFrom(SimpleYaml);
            var rules = book.GetRulesByType("interest_change").ToList();
            Assert.Single(rules);
            Assert.Equal("test.rule1", rules[0].Id);
        }

        [Fact]
        public void GetRulesByType_NonExistentType_ReturnsEmpty()
        {
            var book = RuleBook.LoadFrom(SimpleYaml);
            var rules = book.GetRulesByType("nonexistent").ToList();
            Assert.Empty(rules);
        }

        [Fact]
        public void GetRulesByType_IsCaseInsensitive()
        {
            var book = RuleBook.LoadFrom(SimpleYaml);
            var rules = book.GetRulesByType("INTEREST_CHANGE").ToList();
            Assert.Single(rules);
        }

        [Fact]
        public void All_ReturnsAllEntries()
        {
            var book = RuleBook.LoadFrom(SimpleYaml);
            Assert.Equal(2, book.All.Count);
        }

        [Fact]
        public void LoadFrom_ParsesConditionDict()
        {
            var book = RuleBook.LoadFrom(SimpleYaml);
            var entry = book.GetById("test.rule1")!;
            Assert.NotNull(entry.Condition);
            Assert.True(entry.Condition!.ContainsKey("miss_range"));
        }

        [Fact]
        public void LoadFrom_ParsesOutcomeDict()
        {
            var book = RuleBook.LoadFrom(SimpleYaml);
            var entry = book.GetById("test.rule1")!;
            Assert.NotNull(entry.Outcome);
            Assert.True(entry.Outcome!.ContainsKey("interest_delta"));
        }

        [Fact]
        public void LoadFrom_EmptyString_ThrowsFormatException()
        {
            Assert.Throws<System.FormatException>(() => RuleBook.LoadFrom(""));
        }

        [Fact]
        public void LoadFrom_InvalidYaml_ThrowsFormatException()
        {
            Assert.Throws<System.FormatException>(() => RuleBook.LoadFrom("{{{invalid"));
        }

        [Fact]
        public void LoadFrom_EntryWithNoCondition_HasNullCondition()
        {
            var yaml = @"
- id: test.noconditition
  section: §1
  title: No Condition
  type: definition
  description: Has no condition.
";
            var book = RuleBook.LoadFrom(yaml);
            var entry = book.GetById("test.noconditition")!;
            Assert.Null(entry.Condition);
        }

        [Fact]
        public void LoadFrom_NestedOutcome_ParsesCorrectly()
        {
            var yaml = @"
- id: test.nested
  section: §1
  title: Nested
  type: shadow_growth
  description: Has nested outcome.
  condition:
    natural_roll: 1
  outcome:
    shadow_effect:
      shadow: Madness
      delta: 1
";
            var book = RuleBook.LoadFrom(yaml);
            var entry = book.GetById("test.nested")!;
            Assert.NotNull(entry.Outcome);
            Assert.True(entry.Outcome!.ContainsKey("shadow_effect"));
            var effect = entry.Outcome["shadow_effect"] as System.Collections.Generic.Dictionary<string, object>;
            Assert.NotNull(effect);
            Assert.Equal("Madness", effect!["shadow"].ToString());
        }
    }
}
