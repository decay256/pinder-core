using System;
using System.Collections.Generic;
using System.Text;
using Pinder.Core.Conversation;
using Pinder.Core.Stats;
using Pinder.LlmAdapters;
using Xunit;

namespace Pinder.LlmAdapters.Tests
{
    // Regression for issue #1156 (options-prompt mislabels the PLAYER's own
    // messages as [DATEE]).
    //
    // Root cause: TurnOrchestrator built the options DialogueContext WITHOUT
    // passing playerName/dateeName, so they defaulted to "". SessionDocumentBuilder
    // then ran FallbackName("" -> "Player"), and HistoryFormatter.Format compared
    // entry.Sender ("Sable_xo") == "Player" -> always false -> every line,
    // including the player's own messages, fell through to "DATEE".
    //
    // The datee-response path (DateeResponseStage) already passed
    // playerName: player.DisplayName / dateeName: datee.DisplayName. The fix mirrors
    // that for the options path, and HistoryFormatter.Format now throws loudly if
    // a caller ever again hands it an empty playerName alongside real (non-scene)
    // conversation entries.
    public class Issue1156_OptionsRoleLabelTests
    {
        private const string PlayerSender = "Sable_xo";
        private const string DateeSender = "Brick_haus";
        private const string PlayerText = "hey, your bio made me laugh";
        private const string DateeText = "ha, mission accomplished then";

        private static DialogueContext MakeOptionsContext(string playerName, string dateeName)
        {
            var history = new List<(string Sender, string Text)>
            {
                (PlayerSender, PlayerText),
                (DateeSender, DateeText),
            };

            return new DialogueContext(
                playerAvatarPrompt: "player prompt",
                dateePrompt: "datee prompt",
                conversationHistory: history,
                dateeLastMessage: DateeText,
                activeTraps: Array.Empty<string>(),
                currentInterest: 10,
                playerName: playerName,
                dateeName: dateeName,
                currentTurn: 1);
        }

        [Fact]
        public void OptionsPrompt_PlayerMessage_RendersAsPlayerAvatar_NotDatee()
        {
            // Arrange: names correctly supplied (the post-fix behaviour mirroring
            // the values TurnOrchestrator now passes from player/datee.DisplayName).
            var context = MakeOptionsContext(playerName: PlayerSender, dateeName: DateeSender);

            // Act
            string prompt = SessionDocumentBuilder.BuildDialogueOptionsPrompt(context);

            // Assert: the player's own line is attributed to the player avatar...
            Assert.Contains($"[T1|PLAYER AVATAR] \"{PlayerText}\"", prompt);
            // ...and is NOT mislabeled as DATEE (the #1156 symptom).
            Assert.DoesNotContain($"[T1|DATEE] \"{PlayerText}\"", prompt);

            // And the datee's line renders as DATEE.
            Assert.Contains($"[T1|DATEE] \"{DateeText}\"", prompt);
        }

        // True-regression demonstration: prove that the ORIGINAL behaviour (empty
        // playerName -> FallbackName "Player") would have mislabeled the player's
        // line as DATEE. We exercise HistoryFormatter.Format directly with the
        // buggy fallback value "Player" (which never matches "Sable_xo").
        [Fact]
        public void Formatter_WithBuggyFallbackPlayerName_MislabelsPlayerAsDatee()
        {
            var sb = new StringBuilder();
            var history = new List<(string Sender, string Text)>
            {
                (PlayerSender, PlayerText),
                (DateeSender, DateeText),
            };

            // "Player" is what FallbackName("") produced before the fix.
            HistoryFormatter.Format(sb, history, "Player");
            string result = sb.ToString();

            // This is the BUG: the player's own message is labeled DATEE.
            Assert.Contains($"[T1|DATEE] \"{PlayerText}\"", result);
            Assert.DoesNotContain($"[T1|PLAYER AVATAR] \"{PlayerText}\"", result);
        }

        // And the permanent assert: with the correct playerName, the player line is
        // attributed correctly at the formatter level too.
        [Fact]
        public void Formatter_WithCorrectPlayerName_LabelsPlayerAsPlayerAvatar()
        {
            var sb = new StringBuilder();
            var history = new List<(string Sender, string Text)>
            {
                (PlayerSender, PlayerText),
                (DateeSender, DateeText),
            };

            HistoryFormatter.Format(sb, history, PlayerSender);
            string result = sb.ToString();

            Assert.Contains($"[T1|PLAYER AVATAR] \"{PlayerText}\"", result);
            Assert.Contains($"[T1|DATEE] \"{DateeText}\"", result);
            Assert.DoesNotContain($"[T1|DATEE] \"{PlayerText}\"", result);
        }

        // STEP 2 defensive guard: an empty/whitespace playerName combined with a
        // real (non-scene) conversation entry can NEVER be attributed correctly, so
        // Format must throw loudly rather than silently labeling everything DATEE.
        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(null)]
        public void Formatter_WithEmptyPlayerName_AndRealHistory_ThrowsLoudly(string playerName)
        {
            var sb = new StringBuilder();
            var history = new List<(string Sender, string Text)>
            {
                (PlayerSender, PlayerText),
                (DateeSender, DateeText),
            };

            var ex = Assert.Throws<InvalidOperationException>(
                () => HistoryFormatter.Format(sb, history, playerName));
            Assert.Contains("#1156", ex.Message);
        }

        // The guard must NOT fire for a legitimate scene-only / empty history, where
        // there is nothing to attribute (e.g. the genuine cold-open turn).
        [Fact]
        public void Formatter_WithEmptyPlayerName_AndSceneOnlyHistory_DoesNotThrow()
        {
            var sb = new StringBuilder();
            var sceneOnly = new List<(string Sender, string Text)>
            {
                (Senders.Scene, "A neon-lit bar materialises."),
            };

            // No exception; scene entries are filtered and never attributed.
            HistoryFormatter.Format(sb, sceneOnly, playerName: "");
            string result = sb.ToString();

            Assert.Contains("[CONVERSATION_START]", result);
            Assert.DoesNotContain("[scene]", result);
        }
    }
}
