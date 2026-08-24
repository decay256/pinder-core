using System;
using System.Collections.Generic;
using System.IO;
using Pinder.Core.Conversation;
using Pinder.Core.Diagnostics.AgentJournals;
using Pinder.Core.Prompts;
using Pinder.Core.Stats;
using Pinder.Core.Text;
using Xunit;

namespace Pinder.LlmAdapters.Tests
{
    [Trait("Category", "LlmAdapters")]
    public sealed class Issue1392_PromptTextingStyleReconciliationTests
    {
        private const string Style = "length: wall-of-text; casing: lowercase; signature: end with 🫡";

        [Fact]
        public void DialogueAndDateeInstructions_TreatStyleAsSoftInfluenceWithoutCrossCharacterRegisterOrLengthCaps()
        {
            PromptCatalog catalog = PromptCatalog.LoadFromDirectory(Path.Combine(RepoRoot(), "data", "prompts"));
            string options = catalog.TryGet("dialogue-options-instruction")!.SystemPrompt!;
            string datee = catalog.TryGet("datee-response-instruction")!.SystemPrompt!;

            Assert.DoesNotContain("One to three sentences", options);
            Assert.DoesNotContain("Match the DATEE's register", options);
            Assert.Contains("loose expressive influences", options);
            Assert.Contains("texting-style tendencies may recur when they fit naturally", options);
            Assert.Contains("texting-style tendencies may recur when they fit naturally", datee);
            Assert.DoesNotContain("sound exactly like", options, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("sound exactly like", datee, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("MUST be maintained consistently", options, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("MUST be maintained consistently", datee, StringComparison.OrdinalIgnoreCase);
            AssertNoMandatoryStyleLanguage(options);
            AssertNoMandatoryStyleLanguage(datee);

            string rewrite = catalog.TryGet("default-register-instruction")!.SystemPrompt!;
            Assert.Contains("loose expressive influence", rewrite, StringComparison.OrdinalIgnoreCase);
            AssertNoMandatoryStyleLanguage(rewrite);
        }

        [Fact]
        public void DateeUserPrompt_InjectsDesignatedStyleBeforeFinalInstruction_AndDoesNotClampLength()
        {
            var context = new DateeContext(
                dateePrompt: "datee system prompt",
                conversationHistory: new List<(string, string)> { ("P", "hello"), ("D", "hey") },
                dateeLastMessage: "hey",
                activeTraps: Array.Empty<string>(),
                currentInterest: 12,
                playerDeliveredMessage: "tell me everything",
                interestBefore: 11,
                interestAfter: 12,
                responseDelayMinutes: 1,
                playerName: "P",
                dateeName: "D",
                dateeTextingStyle: Style);

            string prompt = SessionDocumentBuilder.BuildDateePrompt(context);

            int styleIndex = prompt.IndexOf("YOUR TEXTING STYLE", StringComparison.Ordinal);
            int instructionIndex = prompt.IndexOf("CONTEXT BOUNDARY", StringComparison.Ordinal);
            Assert.True(styleIndex >= 0 && styleIndex < instructionIndex);
            Assert.Contains("loose expressive influences", prompt);
            Assert.Contains(Style, prompt);
            Assert.Contains("guided by your designated texting-style length axis", prompt);
            Assert.DoesNotContain("engine-specified ceiling", prompt);
            Assert.DoesNotContain("characters regardless of your texting style", prompt);
            Assert.DoesNotContain("follow this exactly", prompt, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("no deviations", prompt, StringComparison.OrdinalIgnoreCase);
            AssertNoMandatoryStyleLanguage(prompt);
        }

        [Fact]
        public void PostProcessingDocuments_IncludePlayerDesignatedStyle()
        {
            string root = RepoRoot();
            var gameDefinition = GameDefinition.LoadFrom(File.ReadAllText(Path.Combine(root, "data", "game-definition.yaml")));
            var delivery = StatDeliveryInstructions.LoadFrom(File.ReadAllText(Path.Combine(root, "data", "delivery-instructions.yaml")));
            var history = new List<(string Sender, string Text)> { ("P", "hello"), ("D", "hey") };

            GameRunPromptDocumentPair improvement = GameRunPromptDocumentBuilder.BuildSuccessImprovementDocuments(
                new SuccessImprovementContext("player prompt", "D", "P", "hello", StatType.Charm, "strong", history, playerTextingStyle: Style),
                delivery,
                gameDefinition,
                null)!;
            GameRunPromptDocumentPair steering = GameRunPromptDocumentBuilder.BuildSteeringQuestionDocuments(
                new SteeringContext("player prompt", "D", "P", "hello", history, playerTextingStyle: Style),
                gameDefinition,
                null);
            GameRunPromptDocumentPair horniness = GameRunPromptDocumentBuilder.BuildHorninessQuestionDocuments(
                new HorninessQuestionContext("player prompt", "D", "P", "hello", history, playerTextingStyle: Style),
                gameDefinition,
                null);

            Assert.Contains(Style, improvement.User.Text);
            Assert.Contains(Style, steering.User.Text);
            Assert.Contains(Style, horniness.User.Text);
            Assert.Contains("loose expressive influences", improvement.User.Text);
            Assert.Contains("loose expressive influences", steering.User.Text);
            Assert.Contains("loose expressive influences", horniness.User.Text);
            Assert.DoesNotContain("follow this exactly", improvement.User.Text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("follow this exactly", steering.User.Text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("follow this exactly", horniness.User.Text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("preserve this exactly", improvement.User.Text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("preserve this exactly", steering.User.Text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("preserve this exactly", horniness.User.Text, StringComparison.OrdinalIgnoreCase);
            AssertNoMandatoryStyleLanguage(improvement.User.Text);
            AssertNoMandatoryStyleLanguage(steering.User.Text);
            AssertNoMandatoryStyleLanguage(horniness.User.Text);
        }

        [Fact]
        public void PostProcessingDocuments_KeepConfiguredFramingSeparateFromRuntimeStyleProvenance()
        {
            string root = RepoRoot();
            var gameDefinition = GameDefinition.LoadFrom(File.ReadAllText(Path.Combine(root, "data", "game-definition.yaml")));
            var delivery = StatDeliveryInstructions.LoadFrom(File.ReadAllText(Path.Combine(root, "data", "delivery-instructions.yaml")));
            var history = new List<(string Sender, string Text)> { ("P", "hello"), ("D", "hey") };

            GameRunPromptDocumentPair[] documents =
            {
                GameRunPromptDocumentBuilder.BuildSuccessImprovementDocuments(
                    new SuccessImprovementContext("player prompt", "D", "P", "hello", StatType.Charm, "strong", history, playerTextingStyle: Style),
                    delivery,
                    gameDefinition,
                    null)!,
                GameRunPromptDocumentBuilder.BuildSteeringQuestionDocuments(
                    new SteeringContext("player prompt", "D", "P", "hello", history, playerTextingStyle: Style),
                    gameDefinition,
                    null),
                GameRunPromptDocumentBuilder.BuildHorninessQuestionDocuments(
                    new HorninessQuestionContext("player prompt", "D", "P", "hello", history, playerTextingStyle: Style),
                    gameDefinition,
                    null),
            };
            string[] runtimeKeys =
            {
                "SuccessImprovementContext.PlayerTextingStyle",
                "SteeringContext.PlayerTextingStyle",
                "HorninessQuestionContext.PlayerTextingStyle",
            };

            for (int index = 0; index < documents.Length; index++)
            {
                AgentJournalProvenanceRange framing = Assert.Single(documents[index].User.Ranges, range =>
                    range.RangeKind == AgentJournalRangeKind.Configured
                    && range.Source.KeyPath == PromptBuilder.TextingStyleRuntimeFramingKey);
                AgentJournalProvenanceRange runtimeStyle = Assert.Single(documents[index].User.Ranges, range =>
                    range.RangeKind == AgentJournalRangeKind.RuntimeGenerated
                    && range.Source.KeyPath == runtimeKeys[index]);

                Assert.Equal("prompt.catalog", framing.Source.SourceId);
                Assert.StartsWith("YOUR TEXTING STYLE\n", Slice(documents[index].User, framing), StringComparison.Ordinal);
                Assert.Contains("loose expressive influences", Slice(documents[index].User, framing));
                Assert.Equal(Style, Slice(documents[index].User, runtimeStyle));
                Assert.DoesNotContain(documents[index].User.Ranges, range =>
                    range.Source.KeyPath == "texting_style.heading");
            }
        }

        [Fact]
        public void SessionDocuments_AttributeConfiguredFramingAndRuntimeStylesHonestly()
        {
            PromptCatalog catalog = PromptCatalog.LoadFromDirectory(Path.Combine(RepoRoot(), "data", "prompts"));
            var history = new List<(string Sender, string Text)> { ("P", "hello"), ("D", "hey") };
            var dialogueContext = new DialogueContext(
                "player prompt",
                "datee prompt",
                history,
                "hey",
                Array.Empty<string>(),
                12,
                playerName: "P",
                dateeName: "D",
                currentTurn: 2,
                playerTextingStyle: Style,
                availableStats: new[] { StatType.Charm, StatType.Honesty });
            var dateeContext = new DateeContext(
                "datee prompt",
                history,
                "hey",
                Array.Empty<string>(),
                12,
                "hello",
                11,
                12,
                1,
                playerName: "P",
                dateeName: "D",
                dateeTextingStyle: Style);

            AnnotatedInvocationDocument player = GameRunPromptDocumentBuilder.BuildDialogueOptionsUserDocument(dialogueContext, catalog);
            AnnotatedInvocationDocument datee = GameRunPromptDocumentBuilder.BuildDateeUserDocument(dateeContext, catalog);

            AssertStyleRanges(player, "DialogueContext.PlayerTextingStyle");
            AssertStyleRanges(datee, "DateeContext.DateeTextingStyle");
            AssertNoMandatoryStyleLanguage(player.Text);
            AssertNoMandatoryStyleLanguage(datee.Text);
        }

        [Fact]
        public void AdapterEmittersUseTheExplicitCatalogForRuntimeStyleFraming()
        {
            string root = RepoRoot();
            string temporaryRoot = Path.Combine(
                Path.GetTempPath(),
                "pinder-1405-prompts-" + Guid.NewGuid().ToString("N"));
            string temporaryPrompts = Path.Combine(temporaryRoot, "data", "prompts");
            try
            {
                CopyDirectory(Path.Combine(root, "data", "prompts"), temporaryPrompts);
                string structuralPath = Path.Combine(temporaryPrompts, "structural.yaml");
                File.WriteAllText(
                    structuralPath,
                    File.ReadAllText(structuralPath).Replace(
                        "YOUR TEXTING STYLE",
                        "EXPLICIT CATALOG TEXTING STYLE"));
                PromptCatalog catalog = PromptCatalog.LoadFromDirectory(temporaryPrompts);
                var history = new List<(string Sender, string Text)> { ("P", "hello"), ("D", "hey") };
                var gameDefinition = GameDefinition.LoadFrom(
                    File.ReadAllText(Path.Combine(root, "data", "game-definition.yaml")));
                var delivery = StatDeliveryInstructions.LoadFrom(
                    File.ReadAllText(Path.Combine(root, "data", "delivery-instructions.yaml")));
                var dialogueContext = new DialogueContext(
                    "player prompt",
                    "datee prompt",
                    history,
                    "hey",
                    Array.Empty<string>(),
                    12,
                    playerName: "P",
                    dateeName: "D",
                    currentTurn: 2,
                    playerTextingStyle: Style,
                    availableStats: new[] { StatType.Charm, StatType.Honesty });
                var dateeContext = new DateeContext(
                    "datee prompt",
                    history,
                    "hey",
                    Array.Empty<string>(),
                    12,
                    "hello",
                    11,
                    12,
                    1,
                    playerName: "P",
                    dateeName: "D",
                    dateeTextingStyle: Style);

                string[] rendered =
                {
                    GameRunPromptDocumentBuilder.BuildDialogueOptionsUserDocument(dialogueContext, catalog).Text,
                    GameRunPromptDocumentBuilder.BuildDateeUserDocument(dateeContext, catalog).Text,
                    GameRunPromptDocumentBuilder.BuildSuccessImprovementDocuments(
                        new SuccessImprovementContext("player prompt", "D", "P", "hello", StatType.Charm, "strong", history, playerTextingStyle: Style),
                        delivery,
                        gameDefinition,
                        catalog)!.User.Text,
                    GameRunPromptDocumentBuilder.BuildSteeringQuestionDocuments(
                        new SteeringContext("player prompt", "D", "P", "hello", history, playerTextingStyle: Style),
                        gameDefinition,
                        catalog).User.Text,
                    GameRunPromptDocumentBuilder.BuildHorninessQuestionDocuments(
                        new HorninessQuestionContext("player prompt", "D", "P", "hello", history, playerTextingStyle: Style),
                        gameDefinition,
                        catalog).User.Text,
                };

                foreach (string prompt in rendered)
                    Assert.Contains("EXPLICIT CATALOG TEXTING STYLE", prompt, StringComparison.Ordinal);
            }
            finally
            {
                if (Directory.Exists(temporaryRoot))
                    Directory.Delete(temporaryRoot, recursive: true);
            }
        }

        [Fact]
        public void SessionSystemPrompts_KeepCanonicalStaticBaseThenCharacterSpecOrdering()
        {
            PromptTraceResult player = SessionSystemPromptBuilder.BuildPlayerAvatarEx("PLAYER STYLE");
            PromptTraceResult datee = SessionSystemPromptBuilder.BuildDateeEx("DATEE STYLE");

            AssertCanonicalSystemOrder(player, "player-profile");
            AssertCanonicalSystemOrder(datee, "datee-profile");
        }

        private static void AssertCanonicalSystemOrder(PromptTraceResult prompt, string profileKey)
        {
            AnnotatedSpan baseSpan = Assert.Single(prompt.Spans, span => span.Key == "game_master_prompt");
            AnnotatedSpan profileSpan = Assert.Single(prompt.Spans, span => span.Key == profileKey);
            Assert.True(baseSpan.Start < profileSpan.Start);
            Assert.Equal(profileSpan, prompt.Spans[^1]);
        }

        private static string Slice(AnnotatedInvocationDocument document, AgentJournalProvenanceRange range)
            => document.Text.Substring(range.StartUtf16, range.EndUtf16 - range.StartUtf16);

        private static void AssertStyleRanges(AnnotatedInvocationDocument document, string runtimeKey)
        {
            AgentJournalProvenanceRange framing = Assert.Single(document.Ranges, range =>
                range.RangeKind == AgentJournalRangeKind.Configured
                && range.Source.KeyPath == PromptBuilder.TextingStyleRuntimeFramingKey);
            AgentJournalProvenanceRange style = Assert.Single(document.Ranges, range =>
                range.RangeKind == AgentJournalRangeKind.RuntimeGenerated
                && range.Source.KeyPath == runtimeKey);

            Assert.Equal("prompt.catalog", framing.Source.SourceId);
            Assert.StartsWith("YOUR TEXTING STYLE\n", Slice(document, framing), StringComparison.Ordinal);
            Assert.Contains("loose expressive influences", Slice(document, framing));
            Assert.Equal("runtime", style.Source.SourceId);
            Assert.Equal(Style + Environment.NewLine, Slice(document, style));
            Assert.DoesNotContain(document.Ranges, range =>
                range.RangeKind == AgentJournalRangeKind.Configured
                && Slice(document, range).Contains(Style, StringComparison.Ordinal));
        }

        private static void AssertNoMandatoryStyleLanguage(string prompt)
        {
            Assert.DoesNotContain("match the register exactly", prompt, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("match the texting register", prompt, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("sound exactly like", prompt, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("must be maintained consistently", prompt, StringComparison.OrdinalIgnoreCase);
        }

        private static string RepoRoot()
        {
            string? current = AppContext.BaseDirectory;
            while (!string.IsNullOrEmpty(current))
            {
                if (File.Exists(Path.Combine(current, "Pinder.Core.sln"))) return current;
                current = Directory.GetParent(current)?.FullName;
            }

            throw new DirectoryNotFoundException("Could not locate repository root.");
        }

        private static void CopyDirectory(string source, string destination)
        {
            Directory.CreateDirectory(destination);
            foreach (string file in Directory.GetFiles(source))
                File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
            foreach (string directory in Directory.GetDirectories(source))
                CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
        }
    }
}
