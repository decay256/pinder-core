using System;
using Pinder.Core.Data;
using Xunit;

namespace Pinder.Core.Tests
{
    [Trait("Category", "Core")]
    public sealed class JsonTrapRepositoryValidationTests
    {
        [Fact]
        public void Constructor_ThrowsOnMissingRequiredMechanicsField()
        {
            var json = @"[
                { ""id"": ""bad"", ""stat"": ""charm"", ""effect"": ""disadvantage"", ""duration_turns"": 1, ""llm_instruction"": ""test"" }
            ]";

            var ex = Assert.Throws<FormatException>(() => new JsonTrapRepository(json));
            Assert.Contains("effect_value", ex.Message);
        }

        [Fact]
        public void Constructor_ThrowsOnFractionalEffectValue()
        {
            var json = @"[
                { ""id"": ""bad"", ""stat"": ""charm"", ""effect"": ""disadvantage"", ""effect_value"": 1.5, ""duration_turns"": 1, ""llm_instruction"": ""test"" }
            ]";

            var ex = Assert.Throws<FormatException>(() => new JsonTrapRepository(json));
            Assert.Contains("effect_value", ex.Message);
        }

        [Fact]
        public void Constructor_ThrowsOnWrongTypeMechanicsField()
        {
            var json = @"[
                { ""id"": ""bad"", ""stat"": ""charm"", ""effect"": ""disadvantage"", ""effect_value"": ""1"", ""duration_turns"": 1, ""llm_instruction"": ""test"" }
            ]";

            var ex = Assert.Throws<FormatException>(() => new JsonTrapRepository(json));
            Assert.Contains("effect_value", ex.Message);
        }

        [Fact]
        public void Constructor_ThrowsOnNegativeEffectValue()
        {
            var json = @"[
                { ""id"": ""bad"", ""stat"": ""charm"", ""effect"": ""disadvantage"", ""effect_value"": -1, ""duration_turns"": 1, ""llm_instruction"": ""test"" }
            ]";

            var ex = Assert.Throws<FormatException>(() => new JsonTrapRepository(json));
            Assert.Contains("effect_value", ex.Message);
        }

        [Fact]
        public void Constructor_ThrowsOnDurationLessThanOne()
        {
            var json = @"[
                { ""id"": ""bad"", ""stat"": ""charm"", ""effect"": ""disadvantage"", ""effect_value"": 0, ""duration_turns"": 0, ""llm_instruction"": ""test"" }
            ]";

            var ex = Assert.Throws<FormatException>(() => new JsonTrapRepository(json));
            Assert.Contains("duration_turns", ex.Message);
        }

        [Fact]
        public void Constructor_ThrowsOnNonObjectTrapEntry()
        {
            var json = @"[42]";

            var ex = Assert.Throws<FormatException>(() => new JsonTrapRepository(json));
            Assert.Contains("index 0", ex.Message);
        }

        [Fact]
        public void Constructor_ThrowsOnUnknownProperty()
        {
            var json = @"[
                { ""id"": ""bad"", ""stat"": ""charm"", ""effect"": ""disadvantage"", ""effect_value"": 0, ""duration_turns"": 1, ""llm_instruction"": ""test"", ""extra"": ""nope"" }
            ]";

            var ex = Assert.Throws<FormatException>(() => new JsonTrapRepository(json));
            Assert.Contains("extra", ex.Message);
        }
    }
}
