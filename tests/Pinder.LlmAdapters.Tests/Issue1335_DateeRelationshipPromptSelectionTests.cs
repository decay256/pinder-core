using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Pinder.Core.Conversation;
using Pinder.Core.TestCommon;
using Xunit;

namespace Pinder.LlmAdapters.Tests
{
    public sealed class Issue1335_DateeRelationshipPromptSelectionTests
    {
        private static readonly string[] RequiredSemanticRelationshipKeys =
        {
            "interest-narrative-unmatched",
            "interest-narrative-bored",
            "interest-narrative-lukewarm",
            "interest-narrative-interested",
            "interest-narrative-very-into-it",
            "interest-narrative-almost-there",
            "interest-narrative-date-secured",
            "resistance-unmatched",
            "resistance-bored",
            "resistance-lukewarm",
            "resistance-interested",
            "resistance-very-into-it",
            "resistance-almost-there",
            "resistance-date-secured",
        };

        private static DateeContext MakeContext(int interestAfter, InterestState state)
            => new DateeContext(
                dateePrompt: "datee prompt",
                conversationHistory: new List<(string Sender, string Text)>
                {
                    ("Player", "hey"),
                    ("Datee", "hi"),
                },
                dateeLastMessage: "hi",
                activeTraps: Array.Empty<string>(),
                currentInterest: interestAfter,
                playerDeliveredMessage: "tell me the real thing",
                interestBefore: interestAfter,
                interestAfter: interestAfter,
                responseDelayMinutes: 1.0,
                playerName: "Player",
                dateeName: "Datee",
                interestAfterState: state);

        [Fact]
        public void DateePrompt_Interest15_UsesInterestedTypedStateLowerNarrativeAndResistance()
        {
            PromptCatalogInitializer.Initialize();

            string prompt = SessionDocumentBuilder.BuildDateePrompt(
                MakeContext(15, InterestState.Interested));

            Assert.Contains("Engaged but not sold. Evaluating.", prompt);
            Assert.Contains("Unstable agreement", prompt);
            Assert.DoesNotContain("Interested but holding back. Close.", prompt);
            Assert.DoesNotContain("Deliberate approach", prompt);
        }

        [Fact]
        public void DateePrompt_Interest16_UsesVeryIntoItTypedStateUpperNarrativeAndResistance()
        {
            PromptCatalogInitializer.Initialize();

            string prompt = SessionDocumentBuilder.BuildDateePrompt(
                MakeContext(16, InterestState.VeryIntoIt));

            Assert.Contains("Interested but holding back. Close.", prompt);
            Assert.Contains("Deliberate approach", prompt);
            Assert.DoesNotContain("Engaged but not sold. Evaluating.", prompt);
            Assert.DoesNotContain("Unstable agreement", prompt);
        }

        [Theory]
        [InlineData(0, InterestState.Unmatched, "Unmatched", "resistance-unmatched")]
        [InlineData(1, InterestState.Bored, "Reconsidering", "resistance-bored")]
        [InlineData(4, InterestState.Bored, "Reconsidering", "resistance-bored")]
        [InlineData(5, InterestState.Lukewarm, "Skeptical", "resistance-lukewarm")]
        [InlineData(9, InterestState.Lukewarm, "Skeptical", "resistance-lukewarm")]
        [InlineData(10, InterestState.Interested, "Engaged but not sold", "resistance-interested")]
        [InlineData(15, InterestState.Interested, "Engaged but not sold", "resistance-interested")]
        [InlineData(16, InterestState.VeryIntoIt, "Interested but holding back", "resistance-very-into-it")]
        [InlineData(20, InterestState.VeryIntoIt, "Interested but holding back", "resistance-very-into-it")]
        [InlineData(21, InterestState.AlmostThere, "Basically sold", "resistance-almost-there")]
        [InlineData(24, InterestState.AlmostThere, "Basically sold", "resistance-almost-there")]
        [InlineData(25, InterestState.DateSecured, "The resistance dissolved", "resistance-date-secured")]
        public void DateePrompt_AllBoundaries_UseCanonicalTypedStates(
            int interest,
            InterestState state,
            string expectedNarrative,
            string expectedResistanceKey)
        {
            PromptCatalogInitializer.Initialize();

            var trace = SessionDocumentBuilder.BuildDateePromptEx(MakeContext(interest, state));

            Assert.Contains(expectedNarrative, trace.Text);
            Assert.Contains(trace.Spans, span => span.Key == expectedResistanceKey);
        }

        [Fact]
        public void PromptCatalogPublication_RequiresEverySemanticRelationshipPrompt()
        {
            var previous = PromptTemplates.Catalog;
            string dir = Directory.CreateTempSubdirectory("issue-1335-prompts-").FullName;
            try
            {
                CopyBundledPrompts(dir);
                string templatesPath = Path.Combine(dir, "templates.yaml");
                string templates = File.ReadAllText(templatesPath);
                File.WriteAllText(
                    templatesPath,
                    templates.Replace(
                        "  resistance-very-into-it:",
                        "  removed-resistance-very-into-it:",
                        StringComparison.Ordinal));
                var incomplete = PromptCatalog.LoadFromDirectory(dir);

                var ex = Assert.Throws<InvalidOperationException>(
                    () => PromptTemplates.Catalog = incomplete);

                Assert.Contains("resistance-very-into-it", ex.Message);
            }
            finally
            {
                PromptTemplates.Catalog = previous;
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public void RealPromptCatalog_UsesSemanticRelationshipKeysAsSingleAuthority()
        {
            PromptCatalogInitializer.Initialize();

            var keys = PromptTemplates.Catalog!.Names.ToHashSet(StringComparer.Ordinal);

            foreach (string key in RequiredSemanticRelationshipKeys)
            {
                Assert.Contains(key, keys);
            }

            Assert.DoesNotContain("interest-narrative-10-14", keys);
            Assert.DoesNotContain("interest-narrative-15-20", keys);
        }

        [Fact]
        public void Trace_AttributesSelectedNarrativeAndResistanceToSemanticYamlKeys()
        {
            PromptCatalogInitializer.Initialize();

            var interestedTrace = SessionDocumentBuilder.BuildDateePromptEx(
                MakeContext(15, InterestState.Interested));
            var veryIntoItTrace = SessionDocumentBuilder.BuildDateePromptEx(
                MakeContext(16, InterestState.VeryIntoIt));

            Assert.Contains(interestedTrace.Spans, span =>
                span.SourceFile == "data/prompts/templates.yaml" &&
                span.Key == "interest-narrative-interested");
            Assert.Contains(interestedTrace.Spans, span =>
                span.SourceFile == "data/prompts/templates.yaml" &&
                span.Key == "resistance-interested");
            Assert.Contains(veryIntoItTrace.Spans, span =>
                span.SourceFile == "data/prompts/templates.yaml" &&
                span.Key == "interest-narrative-very-into-it");
            Assert.Contains(veryIntoItTrace.Spans, span =>
                span.SourceFile == "data/prompts/templates.yaml" &&
                span.Key == "resistance-very-into-it");
        }

        private static void CopyBundledPrompts(string destination)
        {
            string? directory = AppDomain.CurrentDomain.BaseDirectory;
            while (directory != null)
            {
                string source = Path.Combine(directory, "data", "prompts");
                if (Directory.Exists(source))
                {
                    foreach (string file in Directory.EnumerateFiles(source, "*.yaml"))
                    {
                        File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
                    }
                    return;
                }
                directory = Path.GetDirectoryName(directory);
            }

            throw new DirectoryNotFoundException("Could not locate bundled data/prompts.");
        }
    }
}
