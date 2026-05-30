using System;
using System.Collections.Generic;
using Pinder.Core.Conversation;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;
using Pinder.LlmAdapters;
using Xunit;

namespace Pinder.LlmAdapters.Tests
{
    public partial class SessionDocumentBuilderTests
    {
        // Catalog wiring lives in LlmAdaptersTestWiring.ModuleInitializer
        // (runs at assembly load before any test).

        // ── AC2: Conversation history formatting ──

        [Fact]
        public void BuildDialogueOptionsPrompt_OpponentProfileInUserMessage_NotSystem()
        {
            var result = SessionDocumentBuilder.BuildDialogueOptionsPrompt(
                MakeDialogueContext(playerName: "GERALD_42", opponentName: "VELVET"));

            // Opponent profile appears as informational context in user message
            Assert.Contains("YOU ARE TALKING TO", result);
            Assert.Contains("opponent prompt", result);
            Assert.Contains("Do NOT reference anything you would only know from inside knowledge", result);
        }

        [Fact]
        public void BuildDialogueOptionsPrompt_EmptyOpponentPrompt_OmitsOpponentProfile()
        {
            var ctx = new DialogueContext(
                playerPrompt: "player prompt",
                opponentPrompt: "",
                conversationHistory: new List<(string, string)>(),
                opponentLastMessage: "",
                activeTraps: Array.Empty<string>(),
                currentInterest: 10,
                playerName: "P",
                opponentName: "O");

            var result = SessionDocumentBuilder.BuildDialogueOptionsPrompt(ctx);

            Assert.DoesNotContain("YOU ARE TALKING TO", result);
        }

        [Fact]
        public void BuildDialogueOptionsPrompt_OpponentProfileBeforeConversationHistory()
        {
            var result = SessionDocumentBuilder.BuildDialogueOptionsPrompt(
                MakeDialogueContext(playerName: "GERALD_42", opponentName: "VELVET"));

            int opponentIdx = result.IndexOf("YOU ARE TALKING TO");
            int historyIdx = result.IndexOf("[CONVERSATION_START]");
            Assert.True(opponentIdx < historyIdx,
                "Opponent profile should appear before conversation history");
        }

        [Fact]
        public void BuildDialogueOptionsPrompt_EmptyHistory_ContainsConversationStartAndCurrentTurn()
        {
            var result = SessionDocumentBuilder.BuildDialogueOptionsPrompt(
                MakeDialogueContext(playerName: "GERALD_42", opponentName: "VELVET"));

            Assert.Contains("[CONVERSATION_START]", result);
            Assert.Contains("[CURRENT_TURN]", result);

            // No turn markers between start and current
            int startIdx = result.IndexOf("[CONVERSATION_START]");
            int currentIdx = result.IndexOf("[CURRENT_TURN]");
            string between = result.Substring(
                startIdx + "[CONVERSATION_START]".Length,
                currentIdx - startIdx - "[CONVERSATION_START]".Length);
            Assert.DoesNotContain("[T", between.Trim());
        }

        [Fact]
        public void BuildDialogueOptionsPrompt_ThreeTurnHistory_CorrectTurnMarkers()
        {
            var history = new List<(string, string)>
            {
                ("GERALD_42", "Hey"),
                ("VELVET", "Hi"),
                ("GERALD_42", "How are you?"),
                ("VELVET", "Good"),
                ("GERALD_42", "Cool"),
                ("VELVET", "Indeed")
            };

            var result = SessionDocumentBuilder.BuildDialogueOptionsPrompt(
                MakeDialogueContext(conversationHistory: history, opponentLastMessage: "Indeed",
                    currentInterest: 12, currentTurn: 4, playerName: "GERALD_42", opponentName: "VELVET"));

            Assert.Contains("[T1|PLAYER AVATAR] \"Hey\"", result);
            Assert.Contains("[T1|DATEE] \"Hi\"", result);
            Assert.Contains("[T2|PLAYER AVATAR] \"How are you?\"", result);
            Assert.Contains("[T2|DATEE] \"Good\"", result);
            Assert.Contains("[T3|PLAYER AVATAR] \"Cool\"", result);
            Assert.Contains("[T3|DATEE] \"Indeed\"", result);
        }

        [Fact]
        public void BuildDialogueOptionsPrompt_EightTurnHistory_AllTurnsPresent()
        {
            var history = new List<(string, string)>();
            for (int i = 0; i < 16; i++)
            {
                string sender = i % 2 == 0 ? "PLAYER_A" : "OPP_B";
                history.Add((sender, $"Message {i}"));
            }

            var result = SessionDocumentBuilder.BuildDialogueOptionsPrompt(
                MakeDialogueContext(conversationHistory: history, opponentLastMessage: "Message 15",
                    currentTurn: 9, playerName: "PLAYER_A", opponentName: "OPP_B"));

            for (int turn = 1; turn <= 8; turn++)
            {
                Assert.Contains($"[T{turn}|PLAYER AVATAR]", result);
                Assert.Contains($"[T{turn}|DATEE]", result);
            }
        }

        [Fact]
        public void BuildDialogueOptionsPrompt_OddEntries_HandlesLonePlayerMessage()
        {
            var history = new List<(string, string)>
            {
                ("GERALD", "Hey"),
                ("VELVET", "Hi"),
                ("GERALD", "So anyway...")
            };

            var result = SessionDocumentBuilder.BuildDialogueOptionsPrompt(
                MakeDialogueContext(conversationHistory: history, opponentLastMessage: "Hi",
                    currentTurn: 2, playerName: "GERALD", opponentName: "VELVET"));

            Assert.Contains("[T1|PLAYER AVATAR] \"Hey\"", result);
            Assert.Contains("[T1|DATEE] \"Hi\"", result);
            Assert.Contains("[T2|PLAYER AVATAR] \"So anyway...\"", result);
        }
    }
}
