using System;
using System.Collections.Generic;
using System.IO;
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
            public string ResponseToReturn { get; set; } = "[]";

            public Task<string> SendAsync(string systemPrompt, string userMessage, double temperature = 0.9, int maxTokens = 1024, string? phase = null, CancellationToken ct = default)
            {
                LastSystemPrompt = systemPrompt;
                LastUserMessage = userMessage;
                return Task.FromResult(ResponseToReturn);
            }
        }

        private static (PromptCatalog Catalog, string TempDir) CreateTemporaryCatalog(string promptKey, string systemPrompt, string userTemplate)
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "PinderTests_PromptCatalog_" + Guid.NewGuid());
            Directory.CreateDirectory(tempDir);
            
            var content = $@"schema_version: 1
prompts:
  {promptKey}:
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
        public async Task GenerateAsync_WithValidJson_ParsesSuccessfully()
        {
            // Arrange
            var transport = new FakeLlmTransport { ResponseToReturn = "[\"Afraid of commitment\", \"Deep trust issues\"]" };
            var (catalog, tempDir) = CreateTemporaryCatalog("stakes", "SYSTEM_INSTRUCTION", "USER_TEMPLATE {{backstory}}");

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
                Assert.Equal(2, result.Count);
                Assert.Contains("Afraid of commitment", result);
                Assert.Contains("Deep trust issues", result);

                Assert.Equal("SYSTEM_INSTRUCTION", transport.LastSystemPrompt);
                Assert.NotNull(transport.LastUserMessage);
                Assert.Contains("USER_TEMPLATE", transport.LastUserMessage);
                Assert.Contains("Divorce", transport.LastUserMessage);
            }
            finally
            {
                CleanupCatalog(tempDir);
            }
        }

        [Fact]
        public async Task GenerateAsync_WithMalformedJson_ThrowsInvalidOperationException()
        {
            // Arrange
            var transport = new FakeLlmTransport { ResponseToReturn = "Malformed / non-JSON LLM response" };
            var (catalog, tempDir) = CreateTemporaryCatalog("stakes", "SYSTEM_INSTRUCTION", "USER_TEMPLATE {{backstory}}");

            try
            {
                var generator = new LlmSequentialStakeGenerator(transport, catalog);

                var backstory = new Dictionary<string, BackstoryFact>();

                // Act & Assert
                var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => 
                    generator.GenerateAsync("CharName", "gender", "bio", backstory));
                
                Assert.Contains("Failed to parse stakes JSON from LLM response", ex.Message);
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
            var (catalog, tempDir) = CreateTemporaryCatalog("stakes", "SYSTEM_INSTRUCTION", "USER_TEMPLATE {{backstory}}");

            try
            {
                var generator = new LlmSequentialStakeGenerator(transport, catalog);

                var backstory = new Dictionary<string, BackstoryFact>();

                // Act & Assert
                var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => 
                    generator.GenerateAsync("CharName", "gender", "bio", backstory));
                
                Assert.Contains("Failed to parse stakes JSON from LLM response", ex.Message);
            }
            finally
            {
                CleanupCatalog(tempDir);
            }
        }
    }
}
