using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Pinder.Core.Interfaces;
using Pinder.Core.TestCommon;
using Pinder.LlmAdapters;
using Pinder.SessionSetup;
using Xunit;

namespace Pinder.Core.Tests.SessionSetup
{
    public class OutfitDescriberPromptTests
    {
        private static string PromptsRoot
            => TestRepoLocator.FindRepoSubdir("data", "prompts");

        [Fact]
        public void Loader_LoadsOutfitPrompt_FromYamlFile()
        {
            var catalog = PromptCatalog.LoadFromDirectory(PromptsRoot);
            var outfit = catalog.TryGet("outfit");

            Assert.NotNull(outfit);
            Assert.False(string.IsNullOrWhiteSpace(outfit!.SystemPrompt));
            Assert.False(string.IsNullOrWhiteSpace(outfit.UserTemplate));
            Assert.Contains("{playerName}", outfit.UserTemplate);
            Assert.Contains("{playerItems}", outfit.UserTemplate);
            Assert.Contains("{dateeName}", outfit.UserTemplate);
            Assert.Contains("{dateeItems}", outfit.UserTemplate);
        }

        [Fact]
        public async Task OutfitDescriber_UsesCatalog_WhenSupplied()
        {
            var catalogDir = Directory.CreateTempSubdirectory("outfit-prompt-test-").FullName;
            try
            {
                File.WriteAllText(Path.Combine(catalogDir, "outfit.yaml"), @"schema_version: 1
prompts:
  outfit:
    temperature: 0.8
    max_tokens: 250
    system_prompt: 'CUSTOM SYSTEM PROMPT {playerName}'
    user_template: 'CUSTOM USER {playerName} wears {playerItems} datee {dateeName} wears {dateeItems}'
");

                var catalog = PromptCatalog.LoadFromDirectory(catalogDir);
                var transport = new RecordingLlmTransport();
                var describer = new LlmOutfitDescriber(transport, null, catalog);

                var playerItems = new List<string> { "crown", "cape" };
                var dateeItems = new List<string> { "glasses" };

                string result = await describer.GenerateAsync("Alice", playerItems, "Bob", dateeItems);

                Assert.Equal("CUSTOM SYSTEM PROMPT Alice", transport.LastSystemPrompt);
                Assert.Contains("CUSTOM USER Alice wears - crown\n- cape datee Bob wears - glasses", transport.LastUserMessage);
            }
            finally
            {
                Directory.Delete(catalogDir, recursive: true);
            }
        }

        [Fact]
        public async Task OutfitDescriber_FallsBackToLegacy_WhenCatalogOrEntryIsNull()
        {
            var transport = new RecordingLlmTransport();
            var describer = new LlmOutfitDescriber(transport, null, null);

            var playerItems = new List<string> { "crown", "cape" };
            var dateeItems = new List<string> { "glasses" };

            string result = await describer.GenerateAsync("Alice", playerItems, "Bob", dateeItems);

            Assert.StartsWith("You are setting the visual scene", transport.LastSystemPrompt);
            Assert.Contains("Player (Alice) is wearing:", transport.LastUserMessage);
            Assert.Contains("- crown", transport.LastUserMessage);
            Assert.Contains("- cape", transport.LastUserMessage);
            Assert.Contains("Datee (Bob) is wearing:", transport.LastUserMessage);
            Assert.Contains("- glasses", transport.LastUserMessage);
        }

        private class RecordingLlmTransport : ILlmTransport
        {
            public string? LastSystemPrompt { get; private set; }
            public string? LastUserMessage { get; private set; }

            public Task<string> SendAsync(
                string systemPrompt,
                string userMessage,
                double temperature = 0.9,
                int maxTokens = 1024,
                string? phase = null,
                CancellationToken ct = default)
            {
                LastSystemPrompt = systemPrompt;
                LastUserMessage = userMessage;
                return Task.FromResult("generated output");
            }
        }
    }
}
