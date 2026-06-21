using System.Collections.Generic;
using System.Linq;
using Pinder.Core.Conversation;
using Pinder.Core.Stats;
using Pinder.LlmAdapters.Anthropic;
using Xunit;

namespace Pinder.LlmAdapters.Tests
{
    [Trait("Category", "Core")]
    public class Issue1210_NoEllipsisOptionTests
    {
        [Fact]
        public void ParseDialogueOptionsText_WithTooFewOptions_NeverPadsWithEllipsis()
        {
            // Arrange
            // Provide 6 available stats to force padding (since max parsed options from below is 1).
            var availableStats = new[] { StatType.Charm, StatType.Honesty, StatType.Wit, StatType.Chaos, StatType.Rizz, StatType.SelfAwareness };
            var input = "OPTION_1\n[STAT: Charm]\n\"Hello there!\"";

            // Act
            var result = DialogueOptionParsers.ParseDialogueOptionsText(input, availableStats);

            // Assert
            Assert.Equal(6, result.Length);
            foreach (var option in result)
            {
                Assert.NotNull(option.IntendedText);
                Assert.False(string.IsNullOrWhiteSpace(option.IntendedText), "Option text should not be whitespace-only.");
                Assert.NotEqual("...", option.IntendedText.Trim());
                Assert.False(option.IntendedText.All(c => c == '.' || char.IsWhiteSpace(c)), "Option text should not be only ellipsis/whitespace.");
                Assert.True(option.IntendedText.Length >= 4, $"Option text '{option.IntendedText}' must be at least 4 chars.");
            }
        }

        [Fact]
        public void ReconcileAndPadDialogueOptions_WithShortList_NeverPadsWithEllipsis()
        {
            // Arrange
            var availableStats = new[] { StatType.Charm, StatType.Honesty, StatType.Wit, StatType.Chaos };
            var parsed = new List<DialogueOption>
            {
                new DialogueOption(StatType.Charm, "A valid line")
            };

            // Act
            var result = DialogueOptionParsers.ReconcileAndPadDialogueOptions(parsed, availableStats);

            // Assert
            Assert.Equal(4, result.Length);
            foreach (var option in result)
            {
                Assert.NotNull(option.IntendedText);
                Assert.False(string.IsNullOrWhiteSpace(option.IntendedText), "Option text should not be whitespace-only.");
                Assert.NotEqual("...", option.IntendedText.Trim());
                Assert.False(option.IntendedText.All(c => c == '.' || char.IsWhiteSpace(c)), "Option text should not be only ellipsis/whitespace.");
                Assert.True(option.IntendedText.Length >= 4, $"Option text '{option.IntendedText}' must be at least 4 chars.");
            }
        }
    }
}
