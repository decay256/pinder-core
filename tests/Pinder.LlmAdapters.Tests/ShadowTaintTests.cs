using System.Collections.Generic;
using Pinder.Core.Stats;
using Xunit;

namespace Pinder.LlmAdapters.Tests
{
    public class ShadowTaintTests
    {
        private static readonly IReadOnlyList<(string Sender, string Text)> MinimalHistory =
            new List<(string, string)> { ("Opponent", "Hey there") };

        [Fact]
        public void DialogueOptionsPrompt_HighMadness_ContainsShadowStateSection()
        {
            var shadows = new Dictionary<ShadowStatType, int>
            {
                { ShadowStatType.Madness, 8 }
            };

            var result = SessionDocumentBuilder.BuildDialogueOptionsPrompt(
                MinimalHistory, "Hey there", new string[0], 10, 1, "Player", "Opponent",
                playerShadowThresholds: shadows);

            Assert.Contains("SHADOW STATE (corrupting forces on your communication)", result);
            Assert.Contains("Your Madness is elevated", result);
        }

        [Fact]
        public void DialogueOptionsPrompt_LowShadow_NoShadowStateSection()
        {
            var shadows = new Dictionary<ShadowStatType, int>
            {
                { ShadowStatType.Madness, 3 }
            };

            var result = SessionDocumentBuilder.BuildDialogueOptionsPrompt(
                MinimalHistory, "Hey there", new string[0], 10, 1, "Player", "Opponent",
                playerShadowThresholds: shadows);

            Assert.DoesNotContain("SHADOW STATE", result);
        }

        [Fact]
        public void DialogueOptionsPrompt_MultipleShadowsAboveThreshold_MultipleLines()
        {
            var shadows = new Dictionary<ShadowStatType, int>
            {
                { ShadowStatType.Madness, 8 },
                { ShadowStatType.Denial, 7 },
                { ShadowStatType.Dread, 6 }
            };

            var result = SessionDocumentBuilder.BuildDialogueOptionsPrompt(
                MinimalHistory, "Hey there", new string[0], 10, 1, "Player", "Opponent",
                playerShadowThresholds: shadows);

            Assert.Contains("Your Madness is elevated", result);
            Assert.Contains("Your Denial is elevated", result);
            Assert.Contains("Your Dread is elevated", result);
        }

        [Fact]
        public void DialogueOptionsPrompt_HorninessAt6_NoTaint()
        {
            var shadows = new Dictionary<ShadowStatType, int>
            {
                { ShadowStatType.Horniness, 6 }
            };

            var result = SessionDocumentBuilder.BuildDialogueOptionsPrompt(
                MinimalHistory, "Hey there", new string[0], 10, 1, "Player", "Opponent",
                playerShadowThresholds: shadows);

            Assert.DoesNotContain("SHADOW STATE", result);
            Assert.DoesNotContain("Your Horniness is elevated", result);
        }

        [Fact]
        public void DialogueOptionsPrompt_HorninessAt7_HasTaint()
        {
            var shadows = new Dictionary<ShadowStatType, int>
            {
                { ShadowStatType.Horniness, 7 }
            };

            var result = SessionDocumentBuilder.BuildDialogueOptionsPrompt(
                MinimalHistory, "Hey there", new string[0], 10, 1, "Player", "Opponent",
                playerShadowThresholds: shadows);

            Assert.Contains("SHADOW STATE (corrupting forces on your communication)", result);
            Assert.Contains("Your Horniness is elevated", result);
        }

        [Fact]
        public void DialogueOptionsPrompt_NullShadow_NoShadowStateSection()
        {
            var result = SessionDocumentBuilder.BuildDialogueOptionsPrompt(
                MinimalHistory, "Hey there", new string[0], 10, 1, "Player", "Opponent",
                playerShadowThresholds: null);

            Assert.DoesNotContain("SHADOW STATE", result);
        }

        [Fact]
        public void DeliveryPrompt_HighShadow_ContainsShadowStateSection()
        {
            var shadows = new Dictionary<ShadowStatType, int>
            {
                { ShadowStatType.Overthinking, 7 }
            };
            var option = new Pinder.Core.Conversation.DialogueOption(
                StatType.Wit, "Test message", null, null, false, false);

            var result = SessionDocumentBuilder.BuildDeliveryPrompt(
                MinimalHistory, option, Pinder.Core.Rolls.FailureTier.None, 3, null,
                "Player", "Opponent",
                playerShadowThresholds: shadows);

            Assert.Contains("SHADOW STATE (corrupting forces on your communication)", result);
            Assert.Contains("Your Overthinking is elevated", result);
        }

        [Fact]
        public void OpponentPrompt_HighShadow_ContainsShadowStateSection()
        {
            var shadows = new Dictionary<ShadowStatType, int>
            {
                { ShadowStatType.Fixation, 9 }
            };

            var result = SessionDocumentBuilder.BuildOpponentPrompt(
                MinimalHistory, "Hello", 10, 12, 2.0, null,
                "Player", "Opponent",
                opponentShadowThresholds: shadows);

            Assert.Contains("SHADOW STATE (corrupting forces on your communication)", result);
            Assert.Contains("Your Fixation is elevated", result);
        }
    }
}
