using System;
using System.Collections.Generic;
using Pinder.LlmAdapters;
using Xunit;

namespace Pinder.LlmAdapters.Tests
{
    public class CatalogParserTests
    {
        [Fact]
        public void ParseDeliveryRules_ValidDictionary_ReturnsParsedInstance()
        {
            // Arrange
            var dict = new Dictionary<object, object>
            {
                { "clean", "clean-val" },
                { "strong", "strong-val" },
                { "critical", "critical-val" },
                { "exceptional", "exceptional-val" },
                { "test", "test-val" },
                { "register_instruction", "reg-val" },
                { "medium_rule", "medium-val" }
            };

            // Act
            var result = CatalogParser.ParseDeliveryRules(dict);

            // Assert
            Assert.NotNull(result);
            Assert.Equal("clean-val", result.Clean);
            Assert.Equal("strong-val", result.Strong);
            Assert.Equal("critical-val", result.Critical);
            Assert.Equal("exceptional-val", result.Exceptional);
            Assert.Equal("test-val", result.Test);
            Assert.Equal("reg-val", result.RegisterInstruction);
            Assert.Equal("medium-val", result.MediumRule);
        }

        [Fact]
        public void ParseDeliveryRules_InvalidType_ReturnsNull()
        {
            // Act
            var result = CatalogParser.ParseDeliveryRules("not a dictionary");

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public void ParseDramaticCraft_ValidDictionary_ReturnsParsedInstance()
        {
            // Arrange
            var dict = new Dictionary<object, object>
            {
                { "goal", "goal-val" },
                { "opponent_want", "want-val" },
                { "revelation_budget", "budget-val" },
                { "directness_dial", "dial-val" },
                { "failure_cost", "cost-val" },
                { "earning_the_close", "close-val" }
            };

            // Act
            var result = CatalogParser.ParseDramaticCraft(dict);

            // Assert
            Assert.NotNull(result);
            Assert.Equal("goal-val", result.Goal);
            Assert.Equal("want-val", result.OpponentWant);
            Assert.Equal("budget-val", result.RevelationBudget);
            Assert.Equal("dial-val", result.DirectnessDial);
            Assert.Equal("cost-val", result.FailureCost);
            Assert.Equal("close-val", result.EarningTheClose);
        }

        [Fact]
        public void ParseDramaticCraft_InvalidType_ReturnsNull()
        {
            // Act
            var result = CatalogParser.ParseDramaticCraft(new List<object>());

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public void ParseHorninessTimeModifiers_ValidDictionary_ReturnsParsedInstance()
        {
            // Arrange
            var dict = new Dictionary<object, object>
            {
                { "morning", 1 },
                { "afternoon", "2" },
                { "evening", 3 },
                { "overnight", 4 }
            };

            // Act
            var result = CatalogParser.ParseHorninessTimeModifiers(dict);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(1, result.Morning);
            Assert.Equal(2, result.Afternoon);
            Assert.Equal(3, result.Evening);
            Assert.Equal(4, result.Overnight);
        }

        [Fact]
        public void ParseHorninessTimeModifiers_Null_ThrowsInvalidOperationException()
        {
            // Act & Assert
            var ex = Assert.Throws<InvalidOperationException>(() => CatalogParser.ParseHorninessTimeModifiers(null));
            Assert.Contains("missing required key: horniness_time_modifiers", ex.Message);
        }

        [Fact]
        public void ParseHorninessTimeModifiers_InvalidType_ThrowsInvalidOperationException()
        {
            // Act & Assert
            var ex = Assert.Throws<InvalidOperationException>(() => CatalogParser.ParseHorninessTimeModifiers("not dict"));
            Assert.Contains("missing required key: horniness_time_modifiers", ex.Message);
        }

        [Fact]
        public void ParseHorninessTimeModifiers_MissingSubKey_ThrowsInvalidOperationException()
        {
            // Arrange
            var dict = new Dictionary<object, object>
            {
                { "morning", 1 },
                { "evening", 3 },
                { "overnight", 4 }
                // afternoon is missing
            };

            // Act & Assert
            var ex = Assert.Throws<InvalidOperationException>(() => CatalogParser.ParseHorninessTimeModifiers(dict));
            Assert.Contains("missing required sub-key: afternoon", ex.Message);
        }

        [Fact]
        public void ParseHorninessTimeModifiers_InvalidIntValue_ThrowsInvalidOperationException()
        {
            // Arrange
            var dict = new Dictionary<object, object>
            {
                { "morning", 1 },
                { "afternoon", "abc" },
                { "evening", 3 },
                { "overnight", 4 }
            };

            // Act & Assert
            var ex = Assert.Throws<InvalidOperationException>(() => CatalogParser.ParseHorninessTimeModifiers(dict));
            Assert.Contains("must be an integer", ex.Message);
        }

        [Fact]
        public void ParseI18nCatalog_NullPath_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => CatalogParser.ParseI18nCatalog(null!));
        }

        [Fact]
        public void ParseConsequenceCatalog_NullCatalog_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => CatalogParser.ParseConsequenceCatalog(null!));
        }
    }
}
