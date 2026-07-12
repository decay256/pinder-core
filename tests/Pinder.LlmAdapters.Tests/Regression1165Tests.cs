using System;
using Pinder.Core.Conversation;
using Pinder.Core.Stats;
using Pinder.LlmAdapters;
using Xunit;

namespace Pinder.LlmAdapters.Tests
{
    public class Regression1165Tests
    {
        [Fact]
        public void EmptyPlayerNameDateeName_ThrowsArgumentException_Regression1165()
        {
            var contextNoPlayer = new DialogueContext(
                "p", "d", new System.Collections.Generic.List<(string, string)>(), "last", new string[0], 10,
                playerName: "", dateeName: "O", currentTurn: 1, availableStats: new[] { StatType.Charm });
            
            var ex1 = Assert.Throws<ArgumentException>(() => SessionDocumentBuilder.BuildDialogueOptionsPromptEx(contextNoPlayer));
            Assert.Contains("PlayerName cannot be null or empty", ex1.Message);

            var contextNoDatee = new DialogueContext(
                "p", "d", new System.Collections.Generic.List<(string, string)>(), "last", new string[0], 10,
                playerName: "P", dateeName: "", currentTurn: 1, availableStats: new[] { StatType.Charm });
            
            var ex2 = Assert.Throws<ArgumentException>(() => SessionDocumentBuilder.BuildDialogueOptionsPromptEx(contextNoDatee));
            Assert.Contains("DateeName cannot be null or empty", ex2.Message);
        }

        [Fact]
        public void EmptyAvailableStats_ThrowsInvalidOperationException_Regression1165()
        {
            var contextNoStats = new DialogueContext(
                "p", "d", new System.Collections.Generic.List<(string, string)>(), "last", new string[0], 10,
                playerName: "P", dateeName: "O", currentTurn: 1, availableStats: new StatType[0]);

            var ex1 = Assert.Throws<InvalidOperationException>(() => SessionDocumentBuilder.BuildDialogueOptionsPromptEx(contextNoStats));
            Assert.Contains("AvailableStats cannot be null or empty", ex1.Message);

            var contextNullStats = new DialogueContext(
                "p", "d", new System.Collections.Generic.List<(string, string)>(), "last", new string[0], 10,
                playerName: "P", dateeName: "O", currentTurn: 1, availableStats: null);

            var ex2 = Assert.Throws<InvalidOperationException>(() => SessionDocumentBuilder.BuildDialogueOptionsPromptEx(contextNullStats));
            Assert.Contains("AvailableStats cannot be null or empty", ex2.Message);
        }
    }
}

    public class LegacyTemplateReplaceRemovalTests
    {
        [Fact]
        public void DialogueOptionsInstruction_ReplacesOptionsCount_WithoutLegacyFallback_Regression1165()
        {
            var context = new DialogueContext(
                "p", "d", new System.Collections.Generic.List<(string, string)>(), "last", new string[0], 10,
                playerName: "P", dateeName: "O", currentTurn: 1, availableStats: new[] { StatType.Charm, StatType.Rizz });

            var result = SessionDocumentBuilder.BuildDialogueOptionsPromptEx(context);
            // It should say "Generate exactly 2 dialogue options" because optionCount=2
            Assert.Contains("Generate exactly 2 dialogue options", result.Text);
            // It should not contain "Generate exactly 4 dialogue options" from legacy
            Assert.DoesNotContain("Generate exactly 4 dialogue options", result.Text);
            Assert.DoesNotContain("Generate exactly 6 dialogue options", result.Text);
        }

        [Fact]
        public void DialogueOptionsInstruction_FormatsSelfAwarenessAsCanonicalWireToken()
        {
            var context = new DialogueContext(
                "p", "d", new System.Collections.Generic.List<(string, string)>(), "last", new string[0], 10,
                playerName: "P", dateeName: "O", currentTurn: 1, availableStats: new[] { StatType.Wit, StatType.SelfAwareness });

            var result = SessionDocumentBuilder.BuildDialogueOptionsPromptEx(context);

            Assert.Contains("WIT, SELF_AWARENESS", result.Text);
            Assert.DoesNotContain("SELFAWARENESS", result.Text);
        }
    }
