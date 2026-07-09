using System;
using Pinder.Core.Data;
using Xunit;

namespace Pinder.Core.Tests.Data
{
    [Trait("Category", "Core")]
    public class JsonParserContractTests
    {
        [Fact]
        public void Parse_ValidNestedObjectsArraysAndPrimitives_ReturnsExpectedTree()
        {
            const string json = @"
            {
                ""name"": ""root"",
                ""items"": [
                    { ""id"": ""one"", ""flags"": [true, false, null] },
                    { ""id"": ""two"", ""values"": [1, -2, 3.5, 6.02e23, -4.2E-3] }
                ],
                ""meta"": { ""active"": true, ""missing"": null }
            }";

            var root = Assert.IsType<JsonObject>(JsonParser.Parse(json));
            Assert.Equal("root", root.GetString("name"));

            var items = Assert.IsType<JsonArray>(root.Properties["items"]);
            Assert.Equal(2, items.Items.Count);

            var first = Assert.IsType<JsonObject>(items.Items[0]);
            Assert.Equal("one", first.GetString("id"));
            var flags = Assert.IsType<JsonArray>(first.Properties["flags"]);
            Assert.True(Assert.IsType<JsonBool>(flags.Items[0]).Value);
            Assert.False(Assert.IsType<JsonBool>(flags.Items[1]).Value);
            Assert.IsType<JsonNull>(flags.Items[2]);

            var second = Assert.IsType<JsonObject>(items.Items[1]);
            var values = Assert.IsType<JsonArray>(second.Properties["values"]);
            Assert.Equal(1d, Assert.IsType<JsonNumber>(values.Items[0]).Value);
            Assert.Equal(-2d, Assert.IsType<JsonNumber>(values.Items[1]).Value);
            Assert.Equal(3.5, Assert.IsType<JsonNumber>(values.Items[2]).Value);
            Assert.Equal(6.02e23, Assert.IsType<JsonNumber>(values.Items[3]).Value);
            Assert.Equal(-4.2e-3, Assert.IsType<JsonNumber>(values.Items[4]).Value);

            var meta = Assert.IsType<JsonObject>(root.Properties["meta"]);
            Assert.True(Assert.IsType<JsonBool>(meta.Properties["active"]).Value);
            Assert.IsType<JsonNull>(meta.Properties["missing"]);
        }

        [Fact]
        public void Parse_ValidEscapedStringsAndUnicodeEscapes_UnescapesValues()
        {
            const string json = "{\"text\":\"quote: \\\" slash: \\\\ solidus: \\/ newline:\\n tab:\\t unicode:\\u263A\"}";

            var root = Assert.IsType<JsonObject>(JsonParser.Parse(json));

            Assert.Equal(
                "quote: \" slash: \\ solidus: / newline:\n tab:\t unicode:\u263A",
                root.GetString("text"));
        }

        [Theory]
        [InlineData("  \r\n\t {\"ok\": true}  \n")]
        [InlineData("\n[ true, false, null, 1.25e-2 ]\t")]
        public void Parse_LeadingAndTrailingWhitespace_Succeeds(string json)
        {
            var value = JsonParser.Parse(json);

            Assert.NotNull(value);
        }

        [Theory]
        [InlineData("{\"ok\": true} trailing")]
        [InlineData("tru")]
        [InlineData("fals")]
        [InlineData("nul")]
        [InlineData("{\"x\":\"bad\\qescape\"}")]
        [InlineData("{\"x\":\"bad\\u12G4\"}")]
        [InlineData("{\"x\":\"unterminated}")]
        [InlineData("{\"a\":1 \"b\":2}")]
        [InlineData("[1 2]")]
        [InlineData("[1, 2")]
        [InlineData("{\"a\":1")]
        public void Parse_InvalidJson_ThrowsFormatException(string json)
        {
            Assert.Throws<FormatException>(() => JsonParser.Parse(json));
        }
    }
}
