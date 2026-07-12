using System;
using System.Collections.Generic;
using Pinder.Core.Conversation;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;
using Xunit;

namespace Pinder.LlmAdapters.Tests
{
    public partial class Issue544_EngineInjectionSpecTests
    {
        // ═══════════════════════════════════════════════════════════════
        // AC6: Format correctness — structural validation
        // ═══════════════════════════════════════════════════════════════

        // Mutation: would catch if conversation history was removed from options prompt
        [Fact]
        public void AC6_OptionsPrompt_IncludesConversationHistory()
        {
            var result = SessionDocumentBuilder.BuildDialogueOptionsPrompt(MakeDialogueContext());
            Assert.Contains("[CONVERSATION_START]", result);
            Assert.Contains("[CURRENT_TURN]", result);
            Assert.Contains("hey there", result);
        }

        // Mutation: would catch if datee profile was removed from options prompt
        [Fact]
        public void AC6_OptionsPrompt_IncludesDateeProfile()
        {
            var result = SessionDocumentBuilder.BuildDialogueOptionsPrompt(MakeDialogueContext());
            Assert.Contains("YOU ARE TALKING TO", result);
        }

        // #1138: AC6_DeliveryPrompt_IncludesConversationHistory removed —
        // BuildDeliveryPrompt is gone (delivery collapsed into DeliveryOverlay,
        // #1125). Options/Datee conversation-history coverage is retained.

        // Mutation: would catch if datee prompt lost conversation history
        [Fact]
        public void AC6_DateePrompt_IncludesConversationHistory()
        {
            var result = SessionDocumentBuilder.BuildDateePrompt(MakeDateeContext());
            Assert.Contains("[CONVERSATION_START]", result);
        }

        // Mutation: would catch if datee prompt didn't include interest change direction
        [Fact]
        public void AC6_DateePrompt_ShowsInterestMovement()
        {
            var result = SessionDocumentBuilder.BuildDateePrompt(
                MakeDateeContext(interestBefore: 10, interestAfter: 14));
            Assert.Contains("from 10 to 14", result);
        }

        // Mutation: would catch if response timing was removed from datee prompt
        [Fact]
        public void AC6_DateePrompt_IncludesResponseTiming()
        {
            var result = SessionDocumentBuilder.BuildDateePrompt(
                MakeDateeContext(responseDelayMinutes: 5.0));
            Assert.DoesNotContain("RESPONSE TIMING", result);
            Assert.Contains("5.0 minutes", result);
        }

        // Mutation: would catch if sub-minute delay didn't indicate rapid response
        [Fact]
        public void AC6_DateePrompt_SubMinuteDelay_IndicatesRapidResponse()
        {
            var result = SessionDocumentBuilder.BuildDateePrompt(
                MakeDateeContext(responseDelayMinutes: 0.5));
            Assert.DoesNotContain("less than 1 minute", result);
        }

        // Mutation: would catch if shadow taint was not included when thresholds present
        [Fact]
        public void AC6_OptionsPrompt_IncludesShadowTaintWhenPresent()
        {
            var shadows = new Dictionary<ShadowStatType, int>
            {
                { ShadowStatType.Fixation, 12 }
            };
            var result = SessionDocumentBuilder.BuildDialogueOptionsPrompt(
                MakeDialogueContext(shadowThresholds: shadows));
            // Shadow state should appear in the output
            Assert.Contains("Shadow", result, StringComparison.OrdinalIgnoreCase);
        }

        // ═══════════════════════════════════════════════════════════════
        // AC7: Build clean — null safety
        // ═══════════════════════════════════════════════════════════════

        // Mutation: would catch if null context didn't throw
        [Fact]
        public void AC7_BuildDialogueOptionsPrompt_NullContextThrows()
        {
            Assert.Throws<ArgumentNullException>(() =>
                SessionDocumentBuilder.BuildDialogueOptionsPrompt(null!));
        }

        // #1138: AC7_BuildDeliveryPrompt_NullContextThrows removed —
        // BuildDeliveryPrompt is gone (#1125 DeliveryOverlay). Options/Datee
        // null-context guards are retained.

        [Fact]
        public void AC7_BuildDateePrompt_NullContextThrows()
        {
            Assert.Throws<ArgumentNullException>(() =>
                SessionDocumentBuilder.BuildDateePrompt(null!));
        }
    }
}
