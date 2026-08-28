using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Pinder.Core.Interfaces;
using Pinder.LlmAdapters;
using Pinder.SessionSetup;
using Xunit;

namespace Pinder.Core.Tests.SessionSetup
{
    public sealed class Issue1427_PersonalityStyleBoundaryTests
    {
        [Theory]
        [InlineData("They always end every reply with three dots.", "surface.punctuation")]
        [InlineData("They must use lowercase in every message.", "surface.casing")]
        [InlineData("They always use a new line before replying.", "surface.line_break")]
        [InlineData("They will use emoji whenever anxious.", "surface.emoji")]
        [InlineData("They always open every reply with hey.", "surface.fixed_opening")]
        [InlineData("They must use one sentence when guarded.", "surface.sentence_template")]
        [InlineData("Every reply ends with an ellipsis.", "surface.punctuation")]
        [InlineData("Replies are lowercase.", "surface.casing")]
        [InlineData("Each message has a line break before the final thought.", "surface.line_break")]
        [InlineData("Use emoji to signal discomfort.", "surface.emoji")]
        [InlineData("Messages open with hey.", "surface.fixed_opening")]
        [InlineData("Replies are short and follow a clipped cadence.", "surface.sentence_template")]
        public void Surface_mandates_are_rejected_with_stable_categories(string value, string category)
        {
            var result = ConsolidatedPersonalityValidator.Validate(value);
            Assert.False(result.IsValid);
            Assert.Equal(category, result.ViolationCode);
        }

        [Fact]
        public void Behavioral_pressure_language_remains_valid()
        {
            var result = ConsolidatedPersonalityValidator.Validate(
                "They become guarded under pressure, then test whether closeness is safe before admitting they care.");
            Assert.True(result.IsValid);
        }

        [Fact]
        public void Active_personality_synthesis_template_has_no_texting_style_input()
        {
            string yaml = File.ReadAllText(Path.Combine(FindPromptsRoot(), "personality_consolidation.yaml"));
            Assert.DoesNotContain("TEXTING STYLE SIGNALS", yaml, StringComparison.Ordinal);
            Assert.DoesNotContain("{texting_style}", yaml, StringComparison.Ordinal);
            Assert.Contains("personality-consolidation-repair-surface-style", yaml, StringComparison.Ordinal);
        }

        [Fact]
        public async Task Consolidator_recovers_from_surface_style_output_without_reintroducing_style_input()
        {
            var transport = new QueueTransport(
                "They always end every reply with three dots.",
                "They become guarded under pressure and test for safety before offering real warmth.");
            var consolidator = new LlmPersonalityConsolidator(
                transport,
                PromptCatalog.LoadFromDirectory(FindPromptsRoot()));

            string result = await consolidator.GenerateAsync(
                "Sable", "they/them", "A sharp bio", "GAME LAW", new[] { "guarded, sincere" }, "Charm: 4");

            Assert.Equal(2, transport.CallCount);
            Assert.DoesNotContain("TEXTING STYLE SIGNALS", transport.UserMessages[0], StringComparison.Ordinal);
            Assert.DoesNotContain("{texting_style}", transport.UserMessages[0], StringComparison.Ordinal);
            Assert.Contains("surface.punctuation", transport.SystemPrompts[1], StringComparison.Ordinal);
            Assert.Equal("They become guarded under pressure and test for safety before offering real warmth.", result);
        }

        [Fact]
        public async Task Consolidator_fails_closed_after_bounded_surface_style_rejections()
        {
            var transport = new QueueTransport(
                "They always use emoji.",
                "They always use emoji.",
                "They always use emoji.");
            var consolidator = new LlmPersonalityConsolidator(
                transport,
                PromptCatalog.LoadFromDirectory(FindPromptsRoot()));

            var ex = await Assert.ThrowsAsync<PersonalityConsolidationContractException>(() =>
                consolidator.GenerateAsync("Sable", "they/them", "bio", "law", new[] { "guarded" }, "Charm: 4"));

            Assert.Equal("surface.emoji", ex.ViolationCode);
            Assert.Equal(3, transport.CallCount);
        }

        private static string FindPromptsRoot()
        {
            return TestRepoLocator.FindRepoSubdir("data", "prompts");
        }

        private sealed class QueueTransport : ILlmTransport
        {
            private readonly Queue<string> _responses;

            public QueueTransport(params string[] responses) => _responses = new Queue<string>(responses);
            public int CallCount { get; private set; }
            public List<string> SystemPrompts { get; } = new List<string>();
            public List<string> UserMessages { get; } = new List<string>();

            public Task<string> SendAsync(string systemPrompt, string userMessage, double temperature = 0.9, int? maxTokens = null, string? phase = null, CancellationToken ct = default)
            {
                CallCount++;
                SystemPrompts.Add(systemPrompt);
                UserMessages.Add(userMessage);
                return Task.FromResult(_responses.Dequeue());
            }
        }
    }
}
