using System;
using System.Collections.Generic;
using Pinder.Core.Conversation;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;
using Xunit;

namespace Pinder.LlmAdapters.Tests
{
    [Collection("PromptTraceSingleton")]
    public class ShadowTaintTests
    {
        private static DialogueContext MakeDialogueContext(
            Dictionary<ShadowStatType, int> shadowThresholds = null)
        {
            return new DialogueContext(
                playerAvatarPrompt: "player prompt",
                dateePrompt: "datee prompt",
                conversationHistory: new List<(string, string)> { ("Datee", "Hey there") },
                dateeLastMessage: "Hey there",
                activeTraps: Array.Empty<string>(),
                currentInterest: 10,
                shadowThresholds: shadowThresholds,
                playerName: "Player",
                dateeName: "Datee",
                currentTurn: 1, availableStats: new[] { Pinder.Core.Stats.StatType.Charm, Pinder.Core.Stats.StatType.Rizz, Pinder.Core.Stats.StatType.Honesty,  });
        }

        // #1138: MakeDeliveryContext() removed — BuildDeliveryPrompt no longer
        // exists (delivery collapsed into DeliveryOverlay, #1125).

        private static DateeContext MakeDateeContext(
            Dictionary<ShadowStatType, int> shadowThresholds = null)
        {
            return new DateeContext(
                dateePrompt: "datee prompt",
                conversationHistory: new List<(string, string)> { ("Datee", "Hey there") },
                dateeLastMessage: "Hey there",
                activeTraps: Array.Empty<string>(),
                currentInterest: 10,
                playerDeliveredMessage: "Hello",
                interestBefore: 10,
                interestAfter: 12,
                responseDelayMinutes: 2.0,
                shadowThresholds: shadowThresholds,
                playerName: "Player",
                dateeName: "Datee");
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

        // #1138: DeliveryPrompt_HighShadow_ContainsShadowStateSection removed —
        // BuildDeliveryPrompt is gone (#1125 DeliveryOverlay). Shadow-state
        // injection is still covered on the Dialogue and Datee prompt types.

        [Fact]
        public void DateePrompt_HighShadow_ContainsShadowStateSection()
        {
            var shadows = new Dictionary<ShadowStatType, int>
            {
                { ShadowStatType.Fixation, 9 }
            };

            var result = SessionDocumentBuilder.BuildDateePrompt(
                MakeDateeContext(shadowThresholds: shadows));

            Assert.Contains("SHADOW STATE", result);
            Assert.Contains("Your Fixation is elevated", result);
        }
    }
}
