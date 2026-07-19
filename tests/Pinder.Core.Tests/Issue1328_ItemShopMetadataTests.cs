using System;
using System.Linq;
using Pinder.Core.Data;
using Xunit;

namespace Pinder.Core.Tests
{
    [Trait("Category", "Characters")]
    [Collection("StaticWiring")]
    public class Issue1328_ItemShopMetadataTests
    {
        [Fact]
        public void CanonicalItemCatalog_HasShopMetadataForAll99Items()
        {
            var repo = new JsonItemRepository(TestRepoLocator.ReadDataFile("items/starter-items.json"));
            var items = repo.GetAll().ToList();

            Assert.Equal(99, items.Count);
            Assert.All(items, item =>
            {
                if (item.StarterUnlocked)
                    Assert.Equal(0, item.ShopPrice);
                else
                    Assert.True(item.ShopPrice > 0, $"{item.ItemId} must have a positive price when locked.");
            });
            Assert.Contains(items, item => item.StarterUnlocked);
            Assert.Contains(items, item => !item.StarterUnlocked);
        }

        [Fact]
        public void JsonItemRepository_RejectsMissingShopMetadata()
        {
            var json = MinimalItemJson(extraFields: "");

            var ex = Assert.Throws<FormatException>(() => new JsonItemRepository(json));
            Assert.Contains("shop_price", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Theory]
        [InlineData(@"""shop_price"": 25, ""starter_unlocked"": true")]
        [InlineData(@"""shop_price"": 0, ""starter_unlocked"": false")]
        [InlineData(@"""shop_price"": -1, ""starter_unlocked"": false")]
        public void JsonItemRepository_RejectsInvalidShopPricePolicy(string extraFields)
        {
            var json = MinimalItemJson(extraFields);

            Assert.Throws<FormatException>(() => new JsonItemRepository(json));
        }

        private static string MinimalItemJson(string extraFields)
        {
            var suffix = string.IsNullOrWhiteSpace(extraFields) ? string.Empty : "," + extraFields;
            return @"[
  {
    ""id"": ""shop_probe"",
    ""display_name"": ""Shop Probe"",
    ""summary_text"": ""Probe item."",
    ""slot"": ""Head"",
    ""item_type"": ""accessory"",
    ""stat_modifiers"": {},
    ""personality_fragment"": """",
    ""backstory_fragment"": """",
    ""texting_style_fragment"": """",
    ""archetype_tendencies"": [],
    ""response_timing_modifier"": {}" + suffix + @"
  }
]";
        }
    }
}
