using System;
using System.Collections.Generic;
using Pinder.Core.Conversation;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;
using Xunit;

namespace Pinder.LlmAdapters.Tests
{
    /// <summary>
    /// Issue #307: Verify that shadow taint block fires when raw shadow values exceed thresholds.
    /// The bug: GameSession stored tier (0-3) but builder checked raw value (>5).
    /// Fix: GameSession now stores raw values, so builder comparisons work correctly.
    /// These tests verify the builder side with raw values as input.
    /// Maturity: Prototype (happy-path per AC).
    /// </summary>
    [Collection("PromptTraceSingleton")]
    public class Issue307_ShadowTaintFiringTests
    {
        // ============== AC: Madness=8 → SHADOW STATE section present ==============

        // Mutation: would catch if threshold check is > 8 instead of > 5
        [Fact]
        public void Madness8_DialoguePrompt_ContainsShadowStateSection()
        {
            var shadows = new Dictionary<ShadowStatType, int>
            {
                { ShadowStatType.Madness, 8 }
            };

            var result = SessionDocumentBuilder.BuildDialogueOptionsPrompt(
                MakeDialogueContext(shadows));

            Assert.Contains("Shadow state:", result);
            Assert.Contains("Madness", result);
        }

        // ============== AC: Madness=3 → no SHADOW STATE section ==============

        // Mutation: would catch if threshold check is > 2 instead of > 5
        [Fact]
        public void Madness3_DialoguePrompt_NoShadowStateSection()
        {
            var shadows = new Dictionary<ShadowStatType, int>
            {
                { ShadowStatType.Madness, 3 }
            };

            var result = SessionDocumentBuilder.BuildDialogueOptionsPrompt(
                MakeDialogueContext(shadows));

            Assert.DoesNotContain("Shadow state:", result);
        }

        // ============== Edge: Boundary value Madness=5 → no taint (> 5 needed) ==============

        // Mutation: would catch if threshold check is >= 5 instead of > 5
        [Fact]
        public void Madness5_DialoguePrompt_NoShadowState()
        {
            var shadows = new Dictionary<ShadowStatType, int>
            {
                { ShadowStatType.Madness, 5 }
            };

            var result = SessionDocumentBuilder.BuildDialogueOptionsPrompt(
                MakeDialogueContext(shadows));

            Assert.DoesNotContain("Shadow state:", result);
        }

        // ============== Edge: Boundary value Madness=6 → taint fires (> 5) ==============

        // Mutation: would catch if threshold check is > 6 instead of > 5
        [Fact]
        public void Madness6_DialoguePrompt_HasShadowState()
        {
            var shadows = new Dictionary<ShadowStatType, int>
            {
                { ShadowStatType.Madness, 6 }
            };

            var result = SessionDocumentBuilder.BuildDialogueOptionsPrompt(
                MakeDialogueContext(shadows));

            Assert.Contains("Shadow state:", result);
        }

        // ============== Edge: Despair has different threshold (> 6) ==============

        // Mutation: would catch if Despair threshold is > 5 instead of > 6
        [Fact]
        public void Despair6_NoTaint()
        {
            var shadows = new Dictionary<ShadowStatType, int>
            {
                { ShadowStatType.Despair, 6 }
            };

            var result = SessionDocumentBuilder.BuildDialogueOptionsPrompt(
                MakeDialogueContext(shadows));

            Assert.DoesNotContain("Shadow state:", result);
        }

        // Mutation: would catch if Despair threshold is > 7 instead of > 6
        [Fact]
        public void Despair7_HasTaint()
        {
            var shadows = new Dictionary<ShadowStatType, int>
            {
                { ShadowStatType.Despair, 7 }
            };

            var result = SessionDocumentBuilder.BuildDialogueOptionsPrompt(
                MakeDialogueContext(shadows));

            Assert.Contains("Shadow state:", result);
            Assert.Contains("Despair", result);
        }

        // #1138: DeliveryPrompt_Madness8_ContainsShadowState removed —
        // BuildDeliveryPrompt is gone (delivery collapsed into DeliveryOverlay,
        // #1125). Shadow-taint pass-through is still covered on the Dialogue and
        // Datee prompt types below.

        // ============== Taint fires on Datee prompt too ==============

        // Mutation: would catch if datee prompt doesn't pass shadows through to taint builder
        [Fact]
        public void DateePrompt_Dread10_ContainsShadowState()
        {
            var shadows = new Dictionary<ShadowStatType, int>
            {
                { ShadowStatType.Dread, 10 }
            };

            var result = SessionDocumentBuilder.BuildDateePrompt(
                MakeDateeContext(shadows));

            Assert.Contains("SHADOW STATE", result);
            Assert.Contains("Dread", result);
        }

        // ============== Null shadows → no taint on any prompt type ==============

        // Mutation: would catch if null shadows throw instead of producing no taint
        [Fact]
        public void NullShadows_AllPromptTypes_NoShadowState()
        {
            var dialogue = SessionDocumentBuilder.BuildDialogueOptionsPrompt(
                MakeDialogueContext(null));
            Assert.DoesNotContain("SHADOW STATE", dialogue);

            // #1138: delivery branch removed — BuildDeliveryPrompt is gone (#1125).
            var datee = SessionDocumentBuilder.BuildDateePrompt(
                MakeDateeContext(null));
            Assert.DoesNotContain("SHADOW STATE", datee);
        }

        // ============== Multiple shadows above threshold ==============

        // Mutation: would catch if only first shadow is checked and rest are skipped
        [Fact]
        public void MultipleShadowsAboveThreshold_AllIncluded()
        {
            var shadows = new Dictionary<ShadowStatType, int>
            {
                { ShadowStatType.Madness, 8 },
                { ShadowStatType.Denial, 9 },
                { ShadowStatType.Dread, 7 }
            };

            var result = SessionDocumentBuilder.BuildDialogueOptionsPrompt(
                MakeDialogueContext(shadows));

            Assert.Contains("Madness", result);
            Assert.Contains("Denial", result);
            Assert.Contains("Dread", result);
        }

        // ============ Helpers ============

        private static DialogueContext MakeDialogueContext(
            Dictionary<ShadowStatType, int>? shadowThresholds)
        {
            return new DialogueContext(
                playerAvatarPrompt: "player prompt",
                dateePrompt: "datee prompt",
                conversationHistory: new List<(string, string)> { ("Datee", "Hey") },
                dateeLastMessage: "Hey",
                activeTraps: Array.Empty<string>(),
                currentInterest: 10,
                shadowThresholds: shadowThresholds,
                playerName: "Player",
                dateeName: "Datee",
                currentTurn: 1);
        }

        // #1138: MakeDeliveryContext() removed — BuildDeliveryPrompt no longer
        // exists (delivery collapsed into DeliveryOverlay, #1125).

        private static DateeContext MakeDateeContext(
            Dictionary<ShadowStatType, int>? shadowThresholds)
        {
            return new DateeContext(
                dateePrompt: "datee prompt",
                conversationHistory: new List<(string, string)> { ("Datee", "Hey") },
                dateeLastMessage: "Hey",
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
    }
}
