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

        // Mutation: would catch if delivery prompt lost conversation history
        [Fact]
        public void AC6_DeliveryPrompt_IncludesConversationHistory()
        {
            var result = SessionDocumentBuilder.BuildDeliveryPrompt(MakeDeliveryContext());
            Assert.Contains("[CONVERSATION_START]", result);
        }

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
            Assert.Contains("RESPONSE TIMING", result);
            Assert.Contains("5.0 minutes", result);
        }

        // Mutation: would catch if sub-minute delay didn't indicate rapid response
        [Fact]
        public void AC6_DateePrompt_SubMinuteDelay_IndicatesRapidResponse()
        {
            var result = SessionDocumentBuilder.BuildDateePrompt(
                MakeDateeContext(responseDelayMinutes: 0.5));
            Assert.Contains("less than 1 minute", result);
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

        [Fact]
        public void AC7_BuildDeliveryPrompt_NullContextThrows()
        {
            Assert.Throws<ArgumentNullException>(() =>
                SessionDocumentBuilder.BuildDeliveryPrompt(null!));
        }

        [Fact]
        public void AC7_BuildDateePrompt_NullContextThrows()
        {
            Assert.Throws<ArgumentNullException>(() =>
                SessionDocumentBuilder.BuildDateePrompt(null!));
        }
    }
}
