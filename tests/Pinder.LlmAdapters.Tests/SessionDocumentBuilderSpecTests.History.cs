using System;
using System.Collections.Generic;
using Pinder.Core.Conversation;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;
using Pinder.LlmAdapters;
using Pinder.LlmAdapters.Anthropic;
using Pinder.LlmAdapters.Anthropic.Dto;
using Xunit;

namespace Pinder.LlmAdapters.Tests
{
    public partial class SessionDocumentBuilderSpecTests
    {
        // ═══════════════════════════════════════════════════════════════
        // AC2: Conversation History Formatting
        // ═══════════════════════════════════════════════════════════════

        [Fact]
        public void BuildDialogueOptionsPrompt_EmptyHistory_NoTurnMarkersBetweenStartAndCurrent()
        {
            var result = SessionDocumentBuilder.BuildDialogueOptionsPrompt(MakeDialogueContext());

            int startEnd = result.IndexOf("[CONVERSATION_START]") + "[CONVERSATION_START]".Length;
            int currentStart = result.IndexOf("[CURRENT_TURN]");
            string between = result.Substring(startEnd, currentStart - startEnd);

            Assert.DoesNotContain("[T", between);
        }

        [Fact]
        public void BuildDialogueOptionsPrompt_FourEntries_TurnNumbersIncrementByPair()
        {
            var history = new List<(string, string)>
            {
                ("ALICE", "msg0"),
                ("BOB", "msg1"),
                ("ALICE", "msg2"),
                ("BOB", "msg3")
            };

            var result = SessionDocumentBuilder.BuildDialogueOptionsPrompt(
                MakeDialogueContext(conversationHistory: history, dateeLastMessage: "msg3",
                    currentTurn: 3, playerName: "ALICE", dateeName: "BOB"));

            Assert.Contains("[T1|PLAYER AVATAR] \"msg0\"", result);
            Assert.Contains("[T1|DATEE] \"msg1\"", result);
            Assert.Contains("[T2|PLAYER AVATAR] \"msg2\"", result);
            Assert.Contains("[T2|DATEE] \"msg3\"", result);
            Assert.DoesNotContain("[T3|", result);
            Assert.DoesNotContain("[T4|", result);
        }

        [Fact]
        public void BuildDialogueOptionsPrompt_TwentyTurnHistory_AllTurnsPresent()
        {
            var history = new List<(string, string)>();
            for (int i = 0; i < 40; i++)
            {
                string sender = i % 2 == 0 ? "PLAYER_X" : "OPP_Y";
                history.Add((sender, $"msg{i}"));
            }

            var result = SessionDocumentBuilder.BuildDialogueOptionsPrompt(
                MakeDialogueContext(conversationHistory: history, dateeLastMessage: "msg39",
                    currentTurn: 21, playerName: "PLAYER_X", dateeName: "OPP_Y"));

            for (int turn = 1; turn <= 20; turn++)
            {
                Assert.Contains($"[T{turn}|PLAYER AVATAR]", result);
                Assert.Contains($"[T{turn}|DATEE]", result);
            }
        }

        [Fact]
        public void BuildDialogueOptionsPrompt_SingleEntry_FormatsCorrectly()
        {
            var history = new List<(string, string)> { ("GERALD", "Hello!") };

            var result = SessionDocumentBuilder.BuildDialogueOptionsPrompt(
                MakeDialogueContext(conversationHistory: history, playerName: "GERALD", dateeName: "V"));

            Assert.Contains("[T1|PLAYER AVATAR] \"Hello!\"", result);
            Assert.Contains("[CURRENT_TURN]", result);
        }

        [Fact]
        public void BuildDialogueOptionsPrompt_MessageWithDoubleQuotes_PreservedAsIs()
        {
            var history = new List<(string, string)>
            {
                ("P", "She said \"wow\" to me"),
                ("O", "Really?")
            };

            var result = SessionDocumentBuilder.BuildDialogueOptionsPrompt(
                MakeDialogueContext(conversationHistory: history, dateeLastMessage: "Really?", currentTurn: 2));

            Assert.Contains("She said \"wow\" to me", result);
        }

        [Fact]
        public void BuildDialogueOptionsPrompt_EmptyMessageText_FormatsAsEmptyQuotes()
        {
            var history = new List<(string, string)>
            {
                ("P", ""),
                ("O", "response")
            };

            var result = SessionDocumentBuilder.BuildDialogueOptionsPrompt(
                MakeDialogueContext(conversationHistory: history, dateeLastMessage: "response", currentTurn: 2));

            Assert.Contains("[T1|PLAYER AVATAR] \"\"", result);
        }

        [Fact]
        public void BuildDialogueOptionsPrompt_NamesWithSpaces_UsedAsIs()
        {
            var history = new List<(string, string)>
            {
                ("Big Gerald", "Hey"),
                ("Lady V", "Hi")
            };

            var result = SessionDocumentBuilder.BuildDialogueOptionsPrompt(
                MakeDialogueContext(conversationHistory: history, dateeLastMessage: "Hi",
                    currentTurn: 2, playerName: "Big Gerald", dateeName: "Lady V"));

            Assert.Contains("[T1|PLAYER AVATAR] \"Hey\"", result);
            Assert.Contains("[T1|DATEE] \"Hi\"", result);
        }

        [Fact]
        public void BuildDialogueOptionsPrompt_RoleDetermination_CaseSensitive()
        {
            var history = new List<(string, string)>
            {
                ("Gerald", "Hey"),
                ("gerald", "Hi")
            };

            var result = SessionDocumentBuilder.BuildDialogueOptionsPrompt(
                MakeDialogueContext(conversationHistory: history, dateeLastMessage: "Hi",
                    currentTurn: 2, playerName: "Gerald", dateeName: "gerald"));

            Assert.Contains("[T1|PLAYER AVATAR] \"Hey\"", result);
            Assert.Contains("[T1|DATEE] \"Hi\"", result);
        }

        [Fact]
        public void BuildDateePrompt_HistoryExcludesCurrentPlayerMessage()
        {
            var history = new List<(string, string)>
            {
                ("P", "Turn1Player"),
                ("O", "Turn1Opp")
            };

            var result = SessionDocumentBuilder.BuildDateePrompt(
                MakeDateeContext(conversationHistory: history, playerDeliveredMessage: "Turn2Player",
                    interestBefore: 10, interestAfter: 12, responseDelayMinutes: 3.0));

            Assert.Contains("[T1|PLAYER AVATAR] \"Turn1Player\"", result);
            Assert.Contains("[T1|DATEE] \"Turn1Opp\"", result);
            Assert.Contains("PLAYER'S LAST MESSAGE", result);
            Assert.Contains("\"Turn2Player\"", result);
        }
    }
}
