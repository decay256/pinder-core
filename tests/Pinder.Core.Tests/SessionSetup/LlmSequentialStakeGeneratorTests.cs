using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Pinder.Core.Characters;
using Pinder.Core.Interfaces;
using Pinder.LlmAdapters;
using Pinder.SessionSetup;

namespace Pinder.Core.Tests.SessionSetup
{
    public class LlmSequentialStakeGeneratorTests
    {
        private class FakeLlmTransport : ILlmTransport
        {
            public string? LastSystemPrompt { get; private set; }
            public string? LastUserMessage { get; private set; }
            public double? LastTemperature { get; private set; }
            public int? LastMaxTokens { get; private set; }
            public string? LastPhase { get; private set; }
            public int CallCount { get; private set; }
            public string ResponseToReturn { get; set; } = "[]";

            public Task<string> SendAsync(string systemPrompt, string userMessage, double temperature = 0.9, int maxTokens = 1024, string? phase = null, CancellationToken ct = default)
            {
                CallCount++;
                LastSystemPrompt = systemPrompt;
                LastUserMessage = userMessage;
                LastTemperature = temperature;
                LastMaxTokens = maxTokens;
                LastPhase = phase;
                return Task.FromResult(ResponseToReturn);
            }
        }

        private static (PromptCatalog Catalog, string TempDir) CreateTemporaryCatalog(
            string promptKey,
            string systemPrompt,
            string userTemplate,
            double temperature = 0.7,
            int maxTokens = 1024)
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "PinderTests_PromptCatalog_" + Guid.NewGuid());
            Directory.CreateDirectory(tempDir);
            string temperatureText = temperature.ToString(CultureInfo.InvariantCulture);

            var content = $@"schema_version: 1
prompts:
  {promptKey}:
    temperature: {temperatureText}
    max_tokens: {maxTokens}
    system_prompt: ""{systemPrompt}""
    user_template: ""{userTemplate}""";

            File.WriteAllText(Path.Combine(tempDir, "temp_prompts.yaml"), content);
            var catalog = PromptCatalog.LoadFromDirectory(tempDir);
            return (catalog, tempDir);
        }

        private static void CleanupCatalog(string tempDir)
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public async Task GenerateAsync_WithCanonicalBullets_ParsesSuccessfully()
        {
            // Arrange
            var transport = new FakeLlmTransport { ResponseToReturn = BuildStakeBullets() };
            var (catalog, tempDir) = CreateTemporaryCatalog("stake", "SYSTEM_INSTRUCTION", "USER_TEMPLATE {character_profile}", 0.61, 777);

            try
            {
                var generator = new LlmSequentialStakeGenerator(transport, catalog);

                var backstory = new Dictionary<string, BackstoryFact>
                {
                    { "f1", new BackstoryFact("Family", "Divorce", "High") }
                };

                // Act
                var result = await generator.GenerateAsync("CharName", "gender", "bio", backstory);

                // Assert
                Assert.Equal(15, result.Count);
                Assert.Contains("Stake 1", result);
                Assert.Contains("Stake 15", result);

                Assert.Equal("SYSTEM_INSTRUCTION", transport.LastSystemPrompt);
                Assert.NotNull(transport.LastUserMessage);
                Assert.Contains("USER_TEMPLATE", transport.LastUserMessage);
                Assert.Contains("BACKSTORY JSON", transport.LastUserMessage);
                Assert.Contains("Divorce", transport.LastUserMessage);
                Assert.Equal(0.61, transport.LastTemperature);
                Assert.Equal(777, transport.LastMaxTokens);
                Assert.Equal(LlmPhase.Synthesis, transport.LastPhase);
                Assert.Equal(1, transport.CallCount);
            }
            finally
            {
                CleanupCatalog(tempDir);
            }
        }

        [Fact]
        public async Task GenerateAsync_WithNonBulletText_ThrowsInvalidOperationException()
        {
            // Arrange
            var transport = new FakeLlmTransport { ResponseToReturn = "Malformed non-bullet LLM response" };
            var (catalog, tempDir) = CreateTemporaryCatalog("stake", "SYSTEM_INSTRUCTION", "USER_TEMPLATE {character_profile}");

            try
            {
                var generator = new LlmSequentialStakeGenerator(transport, catalog);

                var backstory = new Dictionary<string, BackstoryFact>();

                // Act & Assert
                var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => 
                    generator.GenerateAsync("CharName", "gender", "bio", backstory));
                
                Assert.Contains("Failed to parse canonical 15-item stake bullet list from LLM response", ex.Message);
                Assert.DoesNotContain("Malformed non-bullet LLM response", ex.Message);
                Assert.DoesNotContain("Malformed non-bullet LLM response", ex.ToString());
                Assert.Contains("phase=synthesis", ex.Message);
                Assert.Contains("output_length=", ex.Message);
                Assert.Contains("output_sha256=", ex.Message);
            }
            finally
            {
                CleanupCatalog(tempDir);
            }
        }

        [Fact]
        public async Task GenerateAsync_WithEmptyWhitespace_ThrowsInvalidOperationException()
        {
            // Arrange
            var transport = new FakeLlmTransport { ResponseToReturn = "   " };
            var (catalog, tempDir) = CreateTemporaryCatalog("stake", "SYSTEM_INSTRUCTION", "USER_TEMPLATE {character_profile}");

            try
            {
                var generator = new LlmSequentialStakeGenerator(transport, catalog);

                var backstory = new Dictionary<string, BackstoryFact>();

                // Act & Assert
                var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => 
                    generator.GenerateAsync("CharName", "gender", "bio", backstory));
                
                Assert.Contains("Failed to parse canonical 15-item stake bullet list from LLM response", ex.Message);
            }
            finally
            {
                CleanupCatalog(tempDir);
            }
        }

        [Fact]
        public void Constructor_WithMissingUserTemplate_ThrowsBeforeLlmCall()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "PinderTests_PromptCatalog_" + Guid.NewGuid());
            Directory.CreateDirectory(tempDir);
            try
            {
                File.WriteAllText(Path.Combine(tempDir, "temp_prompts.yaml"), @"schema_version: 1
prompts:
  stake:
    temperature: 0.7
    max_tokens: 1024
    system_prompt: ""SYSTEM_INSTRUCTION""");

                var catalog = PromptCatalog.LoadFromDirectory(tempDir);
                var transport = new FakeLlmTransport();

                var ex = Assert.Throws<InvalidOperationException>(
                    () => new LlmSequentialStakeGenerator(transport, catalog));

                Assert.Contains("no user_template", ex.Message);
                Assert.Equal(0, transport.CallCount);
            }
            finally
            {
                CleanupCatalog(tempDir);
            }
        }

        private static string BuildStakeBullets()
            => string.Join("\n", Enumerable.Range(1, 15).Select(i => $"- Stake {i}"));
    }
}
