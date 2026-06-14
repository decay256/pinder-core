using System;
using System.Collections.Generic;
using Pinder.LlmAdapters;
using Xunit;

namespace Pinder.LlmAdapters.Tests
{
    public class CatalogParserTests
    {
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
