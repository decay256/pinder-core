using System;
using System.Collections.Generic;
using Pinder.Core.Conversation;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;
using Xunit;

namespace Pinder.LlmAdapters.Tests
{
    public class ShadowTaintTests
    {
        private static DialogueContext MakeDialogueContext(
            Dictionary<ShadowStatType, int> shadowThresholds = null)
        {
            return new DialogueContext(
                playerPrompt: "player prompt",
                opponentPrompt: "opponent prompt",
                conversationHistory: new List<(string, string)> { ("Opponent", "Hey there") },
                opponentLastMessage: "Hey there",
                activeTraps: Array.Empty<string>(),
                currentInterest: 10,
                shadowThresholds: shadowThresholds,
                playerName: "Player",
                opponentName: "Opponent",
                currentTurn: 1);
        }

        private static DeliveryContext MakeDeliveryContext(
            DialogueOption option,
            Dictionary<ShadowStatType, int> shadowThresholds = null)
        {
            return new DeliveryContext(
                playerPrompt: "player prompt",
                opponentPrompt: "opponent prompt",
                conversationHistory: new List<(string, string)> { ("Opponent", "Hey there") },
                opponentLastMessage: "Hey there",
                chosenOption: option,
                outcome: FailureTier.None,
                beatDcBy: 3,
                activeTraps: Array.Empty<string>(),
                shadowThresholds: shadowThresholds,
                playerName: "Player",
                opponentName: "Opponent");
        }

        private static OpponentContext MakeOpponentContext(
            Dictionary<ShadowStatType, int> shadowThresholds = null)
        {
            return new OpponentContext(
                playerPrompt: "player prompt",
                opponentPrompt: "opponent prompt",
                conversationHistory: new List<(string, string)> { ("Opponent", "Hey there") },
                opponentLastMessage: "Hey there",
                activeTraps: Array.Empty<string>(),
                currentInterest: 10,
                playerDeliveredMessage: "Hello",
                interestBefore: 10,
                interestAfter: 12,
                responseDelayMinutes: 2.0,
                shadowThresholds: shadowThresholds,
                playerName: "Player",
                opponentName: "Opponent");
        }

        [Fact]
        public void DialogueOptionsPrompt_HighMadness_ContainsShadowStateSection()
        {
            var shadows = new Dictionary<ShadowStatType, int>
            {
                { ShadowStatType.Madness, 8 }
            };

            var result = SessionDocumentBuilder.BuildDialogueOptionsPrompt(
                MakeDialogueContext(shadowThresholds: shadows));

            Assert.Contains("Shadow state:", result);
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
                MakeDialogueContext(shadowThresholds: shadows));

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
                MakeDialogueContext(shadowThresholds: shadows));

            Assert.Contains("Your Madness is elevated", result);
            Assert.Contains("Your Denial is elevated", result);
            Assert.Contains("Your Dread is elevated", result);
        }

        [Fact]
        public void DialogueOptionsPrompt_DespairAt6_NoTaint()
        {
            var shadows = new Dictionary<ShadowStatType, int>
            {
                { ShadowStatType.Despair, 6 }
            };

            var result = SessionDocumentBuilder.BuildDialogueOptionsPrompt(
                MakeDialogueContext(shadowThresholds: shadows));

            Assert.DoesNotContain("SHADOW STATE", result);
            Assert.DoesNotContain("Your Despair is elevated", result);
        }

        [Fact]
        public void DialogueOptionsPrompt_DespairAt7_HasTaint()
        {
            var shadows = new Dictionary<ShadowStatType, int>
            {
                { ShadowStatType.Despair, 7 }
            };

            var result = SessionDocumentBuilder.BuildDialogueOptionsPrompt(
                MakeDialogueContext(shadowThresholds: shadows));

            Assert.Contains("Shadow state:", result);
            Assert.Contains("Your Despair is elevated", result);
        }

        [Fact]
        public void DialogueOptionsPrompt_NullShadow_NoShadowStateSection()
        {
            var result = SessionDocumentBuilder.BuildDialogueOptionsPrompt(
                MakeDialogueContext(shadowThresholds: null));

            Assert.DoesNotContain("SHADOW STATE", result);
        }

        [Fact]
        public void DeliveryPrompt_HighShadow_ContainsShadowStateSection()
        {
            var shadows = new Dictionary<ShadowStatType, int>
            {
                { ShadowStatType.Overthinking, 7 }
            };
            var option = new DialogueOption(
                StatType.Wit, "Test message", null, null, false, false);

            var result = SessionDocumentBuilder.BuildDeliveryPrompt(
                MakeDeliveryContext(option, shadowThresholds: shadows));

            Assert.Contains("SHADOW STATE", result);
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
                MakeOpponentContext(shadowThresholds: shadows));

            Assert.Contains("SHADOW STATE", result);
            Assert.Contains("Your Fixation is elevated", result);
        }
    }
}
