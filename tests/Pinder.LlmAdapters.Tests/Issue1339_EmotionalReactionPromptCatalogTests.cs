using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Pinder.Core.Conversation;
using Pinder.Core.Stats;
using Xunit;

namespace Pinder.LlmAdapters.Tests
{
    public class Issue1339_EmotionalReactionPromptCatalogTests
    {
        private static readonly InterestState[] ExpectedInterestStates =
        {
            InterestState.Unmatched,
            InterestState.Bored,
            InterestState.Lukewarm,
            InterestState.Interested,
            InterestState.VeryIntoIt,
            InterestState.AlmostThere,
            InterestState.DateSecured,
        };

        private static readonly StatType[] ExpectedStats =
        {
            StatType.Charm,
            StatType.Rizz,
            StatType.Honesty,
            StatType.Chaos,
            StatType.Wit,
            StatType.SelfAwareness,
        };

        private static readonly string[] ExpectedTransitionKeys =
        {
            "strengthened",
            "preserved",
            "damaged",
            "transformed",
        };

        [Fact]
        public void BuiltInCatalog_HasSevenInterestStateMeanings()
        {
            var catalog = BuiltInCatalog();

            foreach (var state in ExpectedInterestStates)
            {
                var entry = catalog.Get(EmotionalReactionPromptCatalog.GetInterestStateMeaningKey(state));

                Assert.Equal("data/prompts/emotional-reactions.yaml", entry.SourceFile);
                Assert.False(string.IsNullOrWhiteSpace(entry.SystemPrompt));
                Assert.DoesNotContain("/", entry.SystemPrompt!);
                Assert.DoesNotMatch(@"\b\d+\s*-\s*\d+\b", entry.SystemPrompt!);
            }
        }

        [Fact]
        public void BuiltInCatalog_HasFourRelationshipTransitionInstructionsWithPlaceholders()
        {
            var catalog = BuiltInCatalog();

            foreach (string transition in ExpectedTransitionKeys)
            {
                var entry = catalog.Get(EmotionalReactionPromptCatalog.GetRelationshipTransitionInstructionKey(transition));

                Assert.Equal("data/prompts/emotional-reactions.yaml", entry.SourceFile);
                Assert.Contains("{prior_relationship}", entry.SystemPrompt!, StringComparison.Ordinal);
                Assert.Contains("{resulting_relationship}", entry.SystemPrompt!, StringComparison.Ordinal);
            }
        }

        [Fact]
        public void BuiltInCatalog_HasExactSixtyStatOutcomeEventMeanings()
        {
            var catalog = BuiltInCatalog();
            var keys = new HashSet<string>(catalog.Names, StringComparer.Ordinal);

            foreach (var stat in ExpectedStats)
            {
                foreach (string outcomeKey in StatDeliveryInstructions.OutcomeTierKeys)
                {
                    string key = EmotionalReactionPromptCatalog.GetEventMeaningKey(stat, outcomeKey);
                    Assert.Contains(key, keys);

                    var entry = catalog.Get(key);
                    Assert.Equal("data/prompts/emotional-reactions.yaml", entry.SourceFile);
                    Assert.False(string.IsNullOrWhiteSpace(entry.SystemPrompt));
                    Assert.Contains("you", entry.SystemPrompt!, StringComparison.OrdinalIgnoreCase);
                    Assert.Contains("closeness", entry.SystemPrompt!, StringComparison.OrdinalIgnoreCase);
                }
            }

            Assert.Equal(
                60,
                keys.Count(key => key.StartsWith("emotional-reaction-event-", StringComparison.Ordinal)));

            var prompts = ExpectedStats
                .SelectMany(stat => StatDeliveryInstructions.OutcomeTierKeys.Select(outcomeKey =>
                    catalog.Get(EmotionalReactionPromptCatalog.GetEventMeaningKey(stat, outcomeKey)).SystemPrompt!))
                .ToArray();

            Assert.Equal(60, prompts.Distinct(StringComparer.Ordinal).Count());
        }

        [Fact]
        public void RuntimeValidation_RejectsMissingEventMeaning()
        {
            string root = CopyPromptsToTemp(FindPromptsRoot());
            try
            {
                DeleteLineBlock(root, EmotionalReactionPromptCatalog.GetEventMeaningKey(StatType.Charm, "clean"));
                var catalog = PromptCatalog.LoadFromDirectory(root);

                var error = Assert.Throws<InvalidOperationException>(
                    () => catalog.ValidateRuntimeCatalog());

                Assert.Contains("emotional-reaction-event-charm-clean", error.Message);
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        [Fact]
        public void RuntimeValidation_RejectsTransitionWithoutRequiredPlaceholder()
        {
            string root = CopyPromptsToTemp(FindPromptsRoot());
            try
            {
                string path = Path.Combine(root, "emotional-reactions.yaml");
                File.WriteAllText(
                    path,
                    File.ReadAllText(path).Replace("{prior_relationship}", "prior relationship", StringComparison.Ordinal));
                var catalog = PromptCatalog.LoadFromDirectory(root);

                var error = Assert.Throws<InvalidOperationException>(
                    () => catalog.ValidateRuntimeCatalog());

                Assert.Contains("emotional-reaction-transition-", error.Message);
                Assert.Contains("{prior_relationship}", error.Message);
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        [Fact]
        public void RuntimeValidation_RejectsBlankInterestMeaning()
        {
            string root = CopyPromptsToTemp(FindPromptsRoot());
            try
            {
                string path = Path.Combine(root, "emotional-reactions.yaml");
                string content = File.ReadAllText(path);
                string newline = content.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
                File.WriteAllText(
                    path,
                    content.Replace(
                        $"emotional-reaction-interest-bored:{newline}    system_prompt:",
                        $"emotional-reaction-interest-bored:{newline}    user_template:",
                        StringComparison.Ordinal));
                var catalog = PromptCatalog.LoadFromDirectory(root);

                var error = Assert.Throws<InvalidOperationException>(
                    () => catalog.ValidateRuntimeCatalog());

                Assert.Contains("emotional-reaction-interest-bored", error.Message);
                Assert.Contains("system_prompt", error.Message);
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        [Fact]
        public void EventMeaningKeys_ReuseExistingOutcomeKeyStrings()
        {
            Assert.Equal(
                "emotional-reaction-event-self-awareness-trope_trap",
                EmotionalReactionPromptCatalog.GetEventMeaningKey(StatType.SelfAwareness, "trope_trap"));

            Assert.Throws<ArgumentException>(
                () => EmotionalReactionPromptCatalog.GetEventMeaningKey(StatType.Charm, "legendary"));
        }

        [Fact]
        public void EventMeanings_AreConcreteAndDistinctByStatAndIntensity()
        {
            var catalog = BuiltInCatalog();

            string charmClean = EmotionalReactionPromptCatalog.GetEventMeaning(catalog, StatType.Charm, "clean");
            string honestyClean = EmotionalReactionPromptCatalog.GetEventMeaning(catalog, StatType.Honesty, "clean");
            string charmNatOne = EmotionalReactionPromptCatalog.GetEventMeaning(catalog, StatType.Charm, "nat1");

            Assert.NotEqual(charmClean, honestyClean);
            Assert.NotEqual(charmClean, charmNatOne);
            Assert.Contains("social timing", charmClean, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("truth", honestyClean, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("collapse", charmNatOne, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void EmotionalReactionPrompts_AvoidNumericBandNotationAndFinalReplyDrafting()
        {
            var catalog = BuiltInCatalog();
            var entries = catalog.Names
                .Where(key => key.StartsWith("emotional-reaction-", StringComparison.Ordinal))
                .Select(key => (Key: key, Prompt: catalog.Get(key).SystemPrompt!))
                .ToArray();

            Assert.Equal(78, entries.Length);

            foreach (var entry in entries)
            {
                Assert.DoesNotMatch(@"\b\d+\s*(?:/|-|to)\s*\d+\b", entry.Prompt);
                Assert.DoesNotContain("InterestState.", entry.Prompt, StringComparison.Ordinal);
                Assert.DoesNotContain("StatType.", entry.Prompt, StringComparison.Ordinal);
                Assert.DoesNotContain("write your reply", entry.Prompt, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("respond with", entry.Prompt, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("send this", entry.Prompt, StringComparison.OrdinalIgnoreCase);
                Assert.True(
                    entry.Prompt.Split(new[] { ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length >= 8,
                    entry.Key + " should contain actionable prose, not an enum-only label.");
            }
        }

        private static PromptCatalog BuiltInCatalog()
        {
            var catalog = PromptCatalog.LoadFromDirectory(FindPromptsRoot());
            catalog.ValidateRuntimeCatalog();
            return catalog;
        }

        private static string FindPromptsRoot()
        {
            string? dir = AppDomain.CurrentDomain.BaseDirectory;
            while (dir != null)
            {
                var candidate = Path.Combine(dir, "data", "prompts");
                if (Directory.Exists(candidate)) return candidate;
                dir = Path.GetDirectoryName(dir);
            }

            throw new DirectoryNotFoundException("Could not locate bundled data/prompts.");
        }

        private static string CopyPromptsToTemp(string source)
        {
            string destination = Path.Combine(
                Path.GetTempPath(),
                "issue1339-prompt-catalog-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(destination);
            foreach (var file in Directory.EnumerateFiles(source, "*.yaml"))
            {
                File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
            }

            return destination;
        }

        private static void DeleteLineBlock(string root, string key)
        {
            string path = Path.Combine(root, "emotional-reactions.yaml");
            var lines = File.ReadAllLines(path).ToList();
            int start = lines.FindIndex(line => line.Trim() == key + ":");
            Assert.True(start >= 0, "Could not find prompt key " + key);

            int end = start + 1;
            while (end < lines.Count && !lines[end].StartsWith("  emotional-reaction-", StringComparison.Ordinal))
            {
                end++;
            }

            lines.RemoveRange(start, end - start);
            File.WriteAllLines(path, lines);
        }
    }
}
