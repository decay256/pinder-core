using System;
using System.Collections.Generic;
using System.IO;
using Pinder.Core.Conversation;
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
        public void DialogueAndDateeInstructions_PreserveSignatureRulesWithoutCrossCharacterRegisterOrLengthCaps()
        {
            PromptCatalog catalog = PromptCatalog.LoadFromDirectory(Path.Combine(RepoRoot(), "data", "prompts"));
            string options = catalog.TryGet("dialogue-options-instruction")!.SystemPrompt!;
            string datee = catalog.TryGet("datee-response-instruction")!.SystemPrompt!;

            Assert.DoesNotContain("One to three sentences", options);
            Assert.DoesNotContain("Match the DATEE's register", options);
            Assert.Contains("Exemption: designated signature texting style rules", options);
            Assert.Contains("Exemption: designated signature texting style rules", datee);
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
            Assert.Contains(Style, prompt);
            Assert.Contains("guided by your designated texting-style length axis", prompt);
            Assert.DoesNotContain("engine-specified ceiling", prompt);
            Assert.DoesNotContain("characters regardless of your texting style", prompt);
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
    }
}
