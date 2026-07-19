using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Pinder.Core.Characters;
using Pinder.Core.Data;
using Pinder.Core.Prompts;
using Pinder.Core.Stats;
using Pinder.Core.Traps;
using Xunit;

namespace Pinder.Core.Tests
{
    [Trait("Category", "Characters")]
    [Collection("StaticWiring")]
    public sealed class Issue1127_1128_SummaryTextDataTests
    {
        private const string TestMetadataJson = @"{
  ""group"": ""test"", ""section"": ""test"",
  ""label_key"": ""anatomy.summary_param.label"", ""control_type"": ""slider"",
  ""normalized_min"": 0, ""normalized_max"": 1,
  ""normalized_default"": 0.5, ""normalized_step"": 0.01,
  ""display_order"": 10
}";

        private static string RepoRoot => TestRepoLocator.RepoRoot;

        private static string LoadJson(params string[] parts)
            => File.ReadAllText(Path.Combine(new[] { RepoRoot }.Concat(parts).ToArray()));

        [Fact]
        public void StarterItems_AllHaveNonEmptySummaryText()
        {
            var repo = new JsonItemRepository(LoadJson("data", "items", "starter-items.json"));

            var missing = repo.GetAll()
                .Where(item => string.IsNullOrWhiteSpace(item.SummaryText))
                .Select(item => item.ItemId)
                .ToList();

            Assert.Empty(missing);
        }

        [Fact]
        public void AnatomyBands_AllHaveNonEmptySummaryText()
        {
            var repo = new JsonAnatomyRepository(LoadJson("data", "anatomy", "anatomy-parameters.json"));

            var missing = repo.GetAll()
                .SelectMany(parameter => parameter.Bands.Select((band, index) => new
                {
                    parameter.Id,
                    Index = index,
                    band.SummaryText
                }))
                .Where(band => string.IsNullOrWhiteSpace(band.SummaryText))
                .Select(band => $"{band.Id}[{band.Index}]")
                .ToList();

            Assert.Empty(missing);
        }

        [Fact]
        public void JsonRepositories_ParseSummaryTextForDisplayConsumers()
        {
            const string itemsJson = @"[
  {
    ""id"": ""item_summary_probe"",
    ""display_name"": ""Summary Probe"",
    ""summary_text"": ""Display-only item summary."",
    ""slot"": ""Head"",
    ""item_type"": ""accessory"",
    ""stat_modifiers"": {},
    ""personality_fragment"": ""item personality"",
    ""backstory_fragment"": """",
    ""texting_style_fragment"": """",
    ""archetype_tendencies"": [],
    ""response_timing_modifier"": {},
    ""shop_price"": 0,
    ""starter_unlocked"": true
  }
]";
            const string anatomyJson = @"[
  {
    ""id"": ""summaryParam"",
    ""name"": ""Summary Parameter"",
    ""metadata"": " + TestMetadataJson + @",
    ""bands"": [
      {
        ""lower"": 0.0,
        ""upper"": 1.0,
        ""summary_text"": ""Display-only anatomy summary."",
        ""personality_fragment"": ""anatomy personality""
      }
    ]
  }
]";

            var item = new JsonItemRepository(itemsJson).GetItem("item_summary_probe");
            var band = new JsonAnatomyRepository(anatomyJson)
                .GetParameter("summaryParam")!
                .ResolveBand(0.5f);

            Assert.Equal("Display-only item summary.", item!.SummaryText);
            Assert.Equal("Display-only anatomy summary.", band!.SummaryText);
        }

        [Fact]
        public void JsonItemRepository_RejectsMissingOrBlankSummaryText()
        {
            const string missingSummaryJson = @"[
  {
    ""id"": ""item_missing_summary"",
    ""display_name"": ""Missing Summary"",
    ""slot"": ""Head"",
    ""item_type"": ""accessory"",
    ""stat_modifiers"": {},
    ""personality_fragment"": """",
    ""backstory_fragment"": """",
    ""texting_style_fragment"": """",
    ""archetype_tendencies"": [],
    ""response_timing_modifier"": {},
    ""shop_price"": 0,
    ""starter_unlocked"": true
  }
]";
            const string blankSummaryJson = @"[
  {
    ""id"": ""item_blank_summary"",
    ""display_name"": ""Blank Summary"",
    ""summary_text"": ""   "",
    ""slot"": ""Head"",
    ""item_type"": ""accessory"",
    ""stat_modifiers"": {},
    ""personality_fragment"": """",
    ""backstory_fragment"": """",
    ""texting_style_fragment"": """",
    ""archetype_tendencies"": [],
    ""response_timing_modifier"": {},
    ""shop_price"": 0,
    ""starter_unlocked"": true
  }
]";

            Assert.Throws<FormatException>(() => new JsonItemRepository(missingSummaryJson));
            Assert.Throws<FormatException>(() => new JsonItemRepository(blankSummaryJson));
        }

        [Fact]
        public void JsonAnatomyRepository_RejectsMissingOrBlankSummaryText()
        {
            const string missingSummaryJson = @"[
  {
    ""id"": ""summaryParam"",
    ""name"": ""Summary Parameter"",
    ""metadata"": " + TestMetadataJson + @",
    ""bands"": [
      {
        ""lower"": 0.0,
        ""upper"": 1.0,
        ""personality_fragment"": """"
      }
    ]
  }
]";
            const string blankSummaryJson = @"[
  {
    ""id"": ""summaryParam"",
    ""name"": ""Summary Parameter"",
    ""metadata"": " + TestMetadataJson + @",
    ""bands"": [
      {
        ""lower"": 0.0,
        ""upper"": 1.0,
        ""summary_text"": "" "",
        ""personality_fragment"": """"
      }
    ]
  }
]";

            Assert.Throws<FormatException>(() => new JsonAnatomyRepository(missingSummaryJson));
            Assert.Throws<FormatException>(() => new JsonAnatomyRepository(blankSummaryJson));
        }

        [Fact]
        public void PromptAssembly_DoesNotIncludeSummaryText()
        {
            const string itemSummary = "ITEM SUMMARY MUST NOT ENTER PROMPT";
            const string anatomySummary = "ANATOMY SUMMARY MUST NOT ENTER PROMPT";
            const string itemsJson = @"[
  {
    ""id"": ""summary_prompt_probe"",
    ""display_name"": ""Prompt Probe"",
    ""summary_text"": """ + itemSummary + @""",
    ""slot"": ""Head"",
    ""item_type"": ""accessory"",
    ""stat_modifiers"": {},
    ""personality_fragment"": ""prompt-visible item personality"",
    ""backstory_fragment"": """",
    ""texting_style_fragment"": """",
    ""archetype_tendencies"": [],
    ""response_timing_modifier"": {},
    ""shop_price"": 0,
    ""starter_unlocked"": true
  }
]";
            const string anatomyJson = @"[
  {
    ""id"": ""summaryParam"",
    ""name"": ""Summary Parameter"",
    ""metadata"": " + TestMetadataJson + @",
    ""bands"": [
      {
        ""lower"": 0.0,
        ""upper"": 1.0,
        ""summary_text"": """ + anatomySummary + @""",
        ""personality_fragment"": ""prompt-visible anatomy personality""
      }
    ]
  }
]";

            var assembler = new CharacterAssembler(
                new JsonItemRepository(itemsJson),
                new JsonAnatomyRepository(anatomyJson));

            var fragments = assembler.Assemble(
                new[] { "summary_prompt_probe" },
                new Dictionary<string, float> { ["summaryParam"] = 0.5f },
                new Dictionary<StatType, int>(),
                new Dictionary<ShadowStatType, int>());

            string prompt = PromptBuilder.BuildSystemPrompt(
                "Summary Probe", "they/them", "summary test bio", fragments, new TrapState());

            Assert.Contains("prompt-visible item personality", prompt);
            Assert.Contains("prompt-visible anatomy personality", prompt);
            Assert.DoesNotContain(itemSummary, prompt);
            Assert.DoesNotContain(anatomySummary, prompt);
        }
    }
}
