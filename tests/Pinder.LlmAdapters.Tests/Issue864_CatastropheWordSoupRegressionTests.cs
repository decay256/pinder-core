using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Pinder.Core.Interfaces;
using Pinder.LlmAdapters;

namespace Pinder.LlmAdapters.Tests
{
    public class Issue864_CatastropheWordSoupRegressionTests
    {
        private sealed class CountingTransport : ILlmTransport
        {
            public int Calls { get; set; }
            private readonly string _response;
            public CountingTransport(string response = "primary-was-called") => _response = response;

            public Task<string> SendAsync(
                string systemPrompt,
                string userMessage,
                double temperature = 0.9,
                int maxTokens = 1024,
                string? phase = null,
                CancellationToken ct = default)
            {
                Calls++;
                return Task.FromResult(_response);
            }
        }

        [Fact]
        public async Task ApplyHorninessOverlay_EmptyInstruction_ReturnsOriginalWithoutLlmCall()
        {
            // #864 note:
            // The original word-count skip-guard lived in the now-deleted vendor-specific overlay applier class;
            // PinderLlmAdapter has no word-count guard (and #1293 must not modify the restricted PinderLlmAdapter.cs),
            // so this preserves the regression's intent — the overlay returns the original text with ZERO LLM calls
            // for input it must not rewrite — via the adapter's real empty-input skip-guard.

            // Arrange
            var transport = new CountingTransport();
            var options = new PinderLlmAdapterOptions { GameDefinition = GameDefinition.PinderDefaults };
            var adapter = new PinderLlmAdapter(transport, options);

            // Act
            var result = await adapter.ApplyHorninessOverlayAsync("This is a short message.", "");

            // Assert
            Assert.Equal("This is a short message.", result);
            Assert.Equal(0, transport.Calls);
        }

        [Fact]
        public void CatastropheTierPrompt_ContainsAbstractNounEscape()
        {
            // Arrange
            // Use absolute path to the data file relative to project root
            var projectRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
            var yamlPath = Path.Combine(projectRoot, "data", "delivery-instructions.yaml");
            
            // Fallback for different build environments
            if (!File.Exists(yamlPath))
            {
                yamlPath = Path.Combine(projectRoot, "src", "Pinder.LlmAdapters", "data", "delivery-instructions.yaml");
            }
            if (!File.Exists(yamlPath))
            {
                throw new FileNotFoundException($"Cannot find delivery-instructions.yaml. Tried: {projectRoot}/data/ and {projectRoot}/src/Pinder.LlmAdapters/data/");
            }

            var content = File.ReadAllText(yamlPath);

            // Assert
            Assert.True(content.Contains("abstract concepts", System.StringComparison.OrdinalIgnoreCase), 
                "The delivery instructions should contain the abstract concepts escape hatch.");
            Assert.True(content.Contains("synonym remains physically or semantically plausible", System.StringComparison.OrdinalIgnoreCase),
                "The delivery instructions should contain the plausibility constraint.");
        }
    }
}
