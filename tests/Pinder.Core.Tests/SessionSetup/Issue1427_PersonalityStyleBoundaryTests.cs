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
        [InlineData("End every reply with an ellipsis.", "surface.punctuation")]
        [InlineData("Write every reply in lowercase.", "surface.casing")]
        [InlineData("Put an emoji in each message.", "surface.emoji")]
        [InlineData("Open every reply with hey.", "surface.fixed_opening")]
        [InlineData("Use a clipped cadence for every reply.", "surface.sentence_template")]
        [InlineData("Keep replies clipped.", "surface.sentence_template")]
        [InlineData("Reply in lowercase.", "surface.casing")]
        [InlineData("Always reply without punctuation.", "surface.punctuation")]
        [InlineData("Always reply without emojis.", "surface.emoji")]
        [InlineData("Never use uppercase replies.", "surface.casing")]
        [InlineData("Replies have no punctuation.", "surface.punctuation")]
        [InlineData("Never use line breaks in replies.", "surface.line_break")]
        [InlineData("Messages contain no emoji.", "surface.emoji")]
        [InlineData("Never open replies with a greeting.", "surface.fixed_opening")]
        [InlineData("Replies follow no fixed cadence.", "surface.sentence_template")]
        [InlineData("Do not keep replies clipped.", "surface.sentence_template")]
        [InlineData("They should not reply in lowercase.", "surface.casing")]
        [InlineData("Never reply in lowercase.", "surface.casing")]
        [InlineData("Always reply without a greeting.", "surface.fixed_opening")]
        [InlineData("Replies contain no capital letters.", "surface.casing")]
        [InlineData("Always reply without line breaks.", "surface.line_break")]
        [InlineData("Always reply without a fixed cadence.", "surface.sentence_template")]
        public void Surface_mandates_are_rejected_with_stable_categories(string value, string category)
        {
            var result = ConsolidatedPersonalityValidator.Validate(value);
            Assert.False(result.IsValid);
            Assert.Equal(category, result.ViolationCode);
        }

        [Theory]
        [InlineData("They reject rules requiring emojis, but always reply in lowercase.", "surface.casing")]
        [InlineData("They always reply in lowercase, but reject rules requiring emojis.", "surface.casing")]
        [InlineData("They reject rules that make messages use emoji; replies are lowercase.", "surface.casing")]
        [InlineData("They reject rules that make messages use emoji, whereas replies stay lowercase.", "surface.casing")]
        [InlineData("They reject rules that make messages use emoji, and always reply in lowercase.", "surface.casing")]
        [InlineData("They reject rules that make replies lowercase, but always use emoji.", "surface.emoji")]
        [InlineData("Always reply without punctuation and reject rules requiring emojis.", "surface.punctuation")]
        [InlineData("They always reply in lowercase and reject rules requiring emojis.", "surface.casing")]
        [InlineData("Always reply without line breaks and reject rules requiring emojis.", "surface.line_break")]
        [InlineData("Always reply without emojis and reject rules requiring punctuation.", "surface.emoji")]
        [InlineData("Always reply without a greeting and reject rules requiring emojis.", "surface.fixed_opening")]
        [InlineData("Always reply without a fixed cadence and reject rules requiring emojis.", "surface.sentence_template")]
        [MemberData(nameof(AntiMandateFirstPrescriptionCases))]
        [MemberData(nameof(MixedPrescriptionAndAntiMandateCases))]
        public void Meta_anti_mandate_exempts_only_its_governed_clause(string value, string category)
        {
            var result = ConsolidatedPersonalityValidator.Validate(value);
            Assert.False(result.IsValid);
            Assert.Equal(category, result.ViolationCode);
        }

        public static IEnumerable<object[]> AntiMandateFirstPrescriptionCases()
        {
            var cases = new[]
            {
                new PrescriptionCase("surface.punctuation", new[]
                {
                    "end every reply with an ellipsis",
                    "every reply ends with an ellipsis",
                    "replies contain no punctuation",
                    "always reply without punctuation",
                    "never use punctuation in replies",
                    "do not keep punctuation in replies",
                    "they should not use punctuation in replies",
                }),
                new PrescriptionCase("surface.casing", new[]
                {
                    "write every reply in lowercase",
                    "every message stays lowercase",
                    "replies are lowercase",
                    "always reply in lowercase",
                    "never use uppercase replies",
                    "do not use uppercase in replies",
                    "they should not reply in lowercase",
                }),
                new PrescriptionCase("surface.line_break", new[]
                {
                    "use line breaks in replies",
                    "every message has a line break",
                    "messages contain no line breaks",
                    "always reply without line breaks",
                    "never use line breaks in replies",
                    "do not use line breaks in replies",
                    "they should not use line breaks in replies",
                }),
                new PrescriptionCase("surface.emoji", new[]
                {
                    "put an emoji in each message",
                    "every message includes an emoji",
                    "messages contain no emoji",
                    "always reply without emojis",
                    "never use emojis in replies",
                    "do not use emojis in replies",
                    "they should not use emojis in replies",
                }),
                new PrescriptionCase("surface.fixed_opening", new[]
                {
                    "open every reply with hey",
                    "every reply opens with hey",
                    "messages open with hey",
                    "always reply without a greeting",
                    "never open replies with a greeting",
                    "do not open replies with a greeting",
                    "they should not open replies with a greeting",
                }),
                new PrescriptionCase("surface.sentence_template", new[]
                {
                    "follow a clipped cadence in every reply",
                    "every reply follows a clipped cadence",
                    "replies are short and clipped",
                    "always reply without a fixed cadence",
                    "never use a fixed cadence in replies",
                    "do not keep replies clipped",
                    "they should not keep replies clipped",
                }),
            };

            foreach (PrescriptionCase item in cases)
                foreach (string prescription in item.Prescriptions)
                    yield return new object[] { "They reject rules requiring emojis and " + prescription + ".", item.Category };
        }

        public static IEnumerable<object[]> MixedPrescriptionAndAntiMandateCases()
        {
            var prescriptions = new[]
            {
                new PrescriptionCase("surface.punctuation", new[] { "Always reply without punctuation" }),
                new PrescriptionCase("surface.casing", new[] { "Always reply in lowercase" }),
                new PrescriptionCase("surface.line_break", new[] { "Always reply without line breaks" }),
                new PrescriptionCase("surface.emoji", new[] { "Always reply without emojis" }),
                new PrescriptionCase("surface.fixed_opening", new[] { "Always reply without a greeting" }),
                new PrescriptionCase("surface.sentence_template", new[] { "Always reply without a fixed cadence" }),
            };
            string[] antiMandates =
            {
                "she rejects rules requiring emojis",
                "they consistently reject rules requiring emojis",
                "he usually refuses instructions requiring emojis",
                "we generally avoid mandates requiring emojis",
                "you often oppose directives requiring emojis",
                "it typically resists requirements requiring emojis",
            };

            foreach (PrescriptionCase item in prescriptions)
            {
                string prescription = item.Prescriptions[0];
                foreach (string antiMandate in antiMandates)
                {
                    yield return new object[]
                    {
                        prescription + " and " + antiMandate + ".",
                        item.Category,
                    };
                    yield return new object[]
                    {
                        UppercaseFirst(antiMandate) + " and " + LowercaseFirst(prescription) + ".",
                        item.Category,
                    };
                }
            }
        }

        private static string UppercaseFirst(string value)
        {
            return char.ToUpperInvariant(value[0]) + value.Substring(1);
        }

        private static string LowercaseFirst(string value)
        {
            return char.ToLowerInvariant(value[0]) + value.Substring(1);
        }

        [Theory]
        [InlineData("They reject instructions to keep replies clipped.")]
        [InlineData("They do not require replies to be lowercase.")]
        [InlineData("They refuse rules that make messages use emoji.")]
        [InlineData("They should not be instructed to open every reply with hey.")]
        public void Meta_level_style_anti_mandates_remain_valid(string value)
        {
            var result = ConsolidatedPersonalityValidator.Validate(value);
            Assert.True(result.IsValid);
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
                "Always reply without punctuation.",
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

        private sealed class PrescriptionCase
        {
            public PrescriptionCase(string category, string[] prescriptions)
            {
                Category = category;
                Prescriptions = prescriptions;
            }

            public string Category { get; }
            public string[] Prescriptions { get; }
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
