using System;
using System.Collections.Generic;
using Pinder.Core.Conversation;
using Pinder.Core.Stats;
using Xunit;

namespace Pinder.LlmAdapters.Tests
{
    public class Issue647_ActiveTellOptionsTests
    {
        [Fact]
        public void BuildDialogueOptionsPrompt_WithActiveTell_InjectsTellDirective()
        {
            // Arrange
            var activeTell = new Tell(StatType.Charm, "They look flustered.");
            var context = new DialogueContext(
                playerPrompt: "Player prompt",
                opponentPrompt: "Opponent prompt",
                conversationHistory: new List<(string, string)>(),
                opponentLastMessage: "Last msg",
                activeTraps: new List<string>(),
                currentInterest: 10,
                activeTell: activeTell
            );

            // Act
            var result = SessionDocumentBuilder.BuildDialogueOptionsPrompt(context);

            // Assert
            Assert.Contains("📡 TELL DETECTED: The opponent revealed a vulnerability around Charm.", result);
            Assert.Contains("One option using Charm should explicitly capitalize on this moment —", result);
            Assert.Contains("it landed differently than intended. The player read the room.", result);
        }

        [Fact]
        public void BuildDialogueOptionsPrompt_WithoutActiveTell_OmitsTellDirective()
        {
            // Arrange
            var context = new DialogueContext(
                playerPrompt: "Player prompt",
                opponentPrompt: "Opponent prompt",
                conversationHistory: new List<(string, string)>(),
                opponentLastMessage: "Last msg",
                activeTraps: new List<string>(),
                currentInterest: 10,
                activeTell: null
            );

            // Act
            var result = SessionDocumentBuilder.BuildDialogueOptionsPrompt(context);

            // Assert
            Assert.DoesNotContain("📡 TELL DETECTED", result);
        }
    }
}
