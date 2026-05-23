using System;
using System.Collections.Generic;
using System.Text;
using Pinder.LlmAdapters;
using Xunit;

namespace Pinder.LlmAdapters.Tests
{
    public class HistoryFormatterTests
    {
        [Fact]
        public void Format_CorrectlyConstructsFullHistory()
        {
            // Arrange
            var sb = new StringBuilder();
            var history = new List<(string Sender, string Text)>
            {
                ("O", "Hello, how are you?"),
                ("P", "I am good!"),
                ("[scene]", "A scenic backdrop rolls in.")
            };
            var playerName = "P";

            // Act
            HistoryFormatter.Format(sb, history, playerName);
            var result = sb.ToString();

            // Assert
            Assert.Contains("[CONVERSATION_START]", result);
            Assert.Contains("[T1|OPPONENT|O] \"Hello, how are you?\"", result);
            Assert.Contains("[T1|PLAYER|P] \"I am good!\"", result);
            Assert.DoesNotContain("[scene]", result);
            Assert.Contains("[CURRENT_TURN]", result);
        }

        [Fact]
        public void FormatRecent_CorrectlyFormatsRecentHistory()
        {
            // Arrange
            var sb = new StringBuilder();
            var history = new List<(string Sender, string Text)>
            {
                ("[scene]", "Outfit context: fancy dress"),
                ("O", "Hi"),
                ("P", "Hey"),
                ("O", "How's your day?"),
                ("P", "Good, you?"),
                ("O", "Great"),
                ("P", "Awesome"),
                ("O", "Are you free?")
            };
            var playerName = "P";

            // Act
            HistoryFormatter.FormatRecent(sb, history, playerName);
            var result = sb.ToString();

            // Assert
            // It should only take up to 6 non-scene entries
            // The filtered entries would be: O:Hi, P:Hey, O:How's your day, P:Good, O:Great, P:Awesome, O:Are you free? (total 7 entries)
            // The last 6 would be: P:Hey, O:How's your day, P:Good, O:Great, P:Awesome, O:Are you free?
            Assert.DoesNotContain("Outfit context", result);
            Assert.DoesNotContain("[OPPONENT] \"Hi\"", result); // First entry is truncated
            Assert.Contains("[PLAYER] \"Hey\"", result);
            Assert.Contains("[OPPONENT] \"How's your day?\"", result);
            Assert.Contains("[PLAYER] \"Good, you?\"", result);
            Assert.Contains("[OPPONENT] \"Great\"", result);
            Assert.Contains("[PLAYER] \"Awesome\"", result);
            Assert.Contains("[OPPONENT] \"Are you free?\"", result);
        }
    }
}
