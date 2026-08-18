using System.Collections.Generic;
using Pinder.Core.Conversation;
using Pinder.Core.Text;
using Pinder.Core.TestCommon;
using Xunit;

namespace Pinder.LlmAdapters.Tests
{
    /// <summary>
    /// Issue #1129 regression guard for the post-OPPONENT naming prompt builders.
    /// Prompt provenance is asserted directly now that the legacy global trace
    /// registry has been retired.
    /// </summary>
    public sealed class Issue1129_SchemaRenameGuardTests
    {
        [Fact]
        public void LivePromptBuilders_ReturnAnnotatedResultsUnderNewTerminology()
        {
            PromptCatalogInitializer.Initialize();

            PromptTraceResult dialogue = SessionDocumentBuilder.BuildDialogueOptionsPromptEx(MakeDialogueContext());
            PromptTraceResult datee = SessionDocumentBuilder.BuildDateePromptEx(MakeDateeContext());
            PromptTraceResult dialogueSystem = SessionSystemPromptBuilder.BuildPlayerAvatarEx("You are reuben.");
            PromptTraceResult dateeSystem = SessionSystemPromptBuilder.BuildDateeEx("You are velvet.");

            Assert.NotEmpty(dialogue.Text);
            Assert.NotEmpty(datee.Text);
            Assert.NotEmpty(dialogueSystem.Text);
            Assert.NotEmpty(dateeSystem.Text);
            Assert.NotEmpty(dialogue.Spans);
            Assert.NotEmpty(datee.Spans);
            Assert.NotEmpty(dialogueSystem.Spans);
            Assert.NotEmpty(dateeSystem.Spans);

            Assert.Contains(dialogue.Spans, span => span.Key.Contains("dialogue-options"));
            Assert.Contains(datee.Spans, span => span.Key.Contains("datee"));
            Assert.Contains(dialogueSystem.Spans, span => span.Key.Contains("player"));
            Assert.Contains(dateeSystem.Spans, span => span.Key.Contains("datee"));

            Assert.DoesNotContain(dialogue.Spans, span => span.Key.Contains("opponent"));
            Assert.DoesNotContain(datee.Spans, span => span.Key.Contains("opponent"));
            Assert.DoesNotContain(dialogueSystem.Spans, span => span.Key.Contains("opponent"));
            Assert.DoesNotContain(dateeSystem.Spans, span => span.Key.Contains("opponent"));
        }

        private static DialogueContext MakeDialogueContext() => new DialogueContext(
            playerAvatarPrompt: "You are reuben.",
            dateePrompt: "You are talking to velvet.",
            conversationHistory: new List<(string Sender, string Text)> { ("Velvet", "Hello") },
            dateeLastMessage: "Hello",
            activeTraps: new List<string>(),
            currentInterest: 10,
            currentTurn: 3,
            availableStats: new[]
            {
                Pinder.Core.Stats.StatType.Charm,
                Pinder.Core.Stats.StatType.Rizz,
                Pinder.Core.Stats.StatType.Honesty,
            },
            playerName: "P",
            dateeName: "O");

        private static DateeContext MakeDateeContext() => new DateeContext(
            dateePrompt: "You are velvet.",
            conversationHistory: new List<(string Sender, string Text)> { ("Reuben", "Hi") },
            dateeLastMessage: "Hi",
            activeTraps: new List<string>(),
            currentInterest: 10,
            playerDeliveredMessage: "Hey there",
            interestBefore: 10,
            interestAfter: 12,
            responseDelayMinutes: 0.0,
            playerName: "Reuben",
            dateeName: "Velvet",
            currentTurn: 3);
    }
}
