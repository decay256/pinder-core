using System;
using System.Collections.Generic;
using Pinder.Core.Conversation;
using Pinder.LlmAdapters;
using Xunit;

namespace Pinder.LlmAdapters.Tests
{
    // Regression for issue #1155 (cold-opener off-by-one).
    //
    // The cold-opener guard used to gate on `context.CurrentTurn == 1`, but the
    // turn counter is 0-based and incremented at the END of ResolveTurnAsync.
    // So the guard NEVER fired on the genuine first turn (CurrentTurn == 0, empty
    // history) and WRONGLY fired on the second turn (CurrentTurn == 1, which already
    // has a player+datee exchange). The guard is now re-anchored to an empty
    // ConversationHistory.
    //
    // These tests deliberately set CurrentTurn to values that PROVE the fix keys on
    // history rather than the integer:
    //   (a) empty history + CurrentTurn == 0 -> cold opener PRESENT
    //   (b) 1-exchange history + CurrentTurn == 1 -> cold opener ABSENT
    // Under the OLD code, (a) would fail (absent at turn 0) and (b) would fail
    // (present at turn 1), so this test would have caught the original bug.
    public class Issue1155_ColdOpenerGuardTests
    {
        private const string ColdOpenerMarker = "COLD OPENER RULE";

        private static DialogueContext MakeContext(
            IReadOnlyList<(string Sender, string Text)> history,
            int currentTurn)
        {
            return new DialogueContext(
                playerAvatarPrompt: "player prompt",
                dateePrompt: "datee prompt",
                conversationHistory: history,
                dateeLastMessage: "",
                activeTraps: Array.Empty<string>(),
                currentInterest: 10,
                playerName: "P",
                dateeName: "O",
                currentTurn: currentTurn, availableStats: new[] { Pinder.Core.Stats.StatType.Charm, Pinder.Core.Stats.StatType.Rizz, Pinder.Core.Stats.StatType.Honesty,  });
        }

        [Fact]
        public void ColdOpener_Present_WhenHistoryEmpty_EvenAtTurnZero()
        {
            // Genuine first turn: nobody has spoken; counter is still 0.
            var context = MakeContext(
                new List<(string, string)>(),
                currentTurn: 0);

            Assert.Empty(context.ConversationHistory);

            string prompt = SessionDocumentBuilder.BuildDialogueOptionsPrompt(context);

            Assert.Contains(ColdOpenerMarker, prompt);
        }

        [Fact]
        public void ColdOpener_Absent_WhenHistoryHasOneExchange_EvenAtTurnOne()
        {
            // Second turn: a player message + a datee reply already exist; the
            // (previously buggy) counter reads 1 here.
            var history = new List<(string, string)>
            {
                ("P", "hey, nice profile"),
                ("O", "ha, thanks — you too"),
            };
            var context = MakeContext(history, currentTurn: 1);

            Assert.NotEmpty(context.ConversationHistory);

            string prompt = SessionDocumentBuilder.BuildDialogueOptionsPrompt(context);

            Assert.DoesNotContain(ColdOpenerMarker, prompt);
        }
    }
}
