using System;
using System.Collections.Generic;
using Pinder.Core.Conversation;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;
using Pinder.LlmAdapters;
using Pinder.LlmAdapters.Anthropic;
using Pinder.LlmAdapters.Anthropic.Dto;
using Xunit;

namespace Pinder.LlmAdapters.Tests
{
    public partial class SessionDocumentBuilderSpecTests
    {
        // ═══════════════════════════════════════════════════════════════
        // BuildDateePrompt
        // ═══════════════════════════════════════════════════════════════

        [Fact]
        public void BuildDateePrompt_PositiveDelta_ShowsPlusSign()
        {
            var result = SessionDocumentBuilder.BuildDateePrompt(
                MakeDateeContext(interestBefore: 8, interestAfter: 11, responseDelayMinutes: 2.0));

            Assert.Contains("Interest moved from 8 to 11 (+3)", result);
        }

        [Fact]
        public void BuildDateePrompt_ZeroDelta_ShowsPlusZero()
        {
            var result = SessionDocumentBuilder.BuildDateePrompt(
                MakeDateeContext(interestBefore: 10, interestAfter: 10, responseDelayMinutes: 2.0));

            Assert.Contains("Interest moved from 10 to 10 (+0)", result);
        }

        [Fact]
        public void BuildDateePrompt_ShowsCurrentInterestOutOf25()
        {
            var result = SessionDocumentBuilder.BuildDateePrompt(
                MakeDateeContext(interestBefore: 10, interestAfter: 15));

            Assert.Contains("Interest 15/25", result);
        }

        [Fact]
        public void BuildDateePrompt_NormalDelay_DoesNotExposeTimingToModel()
        {
            var result = SessionDocumentBuilder.BuildDateePrompt(
                MakeDateeContext(responseDelayMinutes: 5.5));

            Assert.DoesNotContain("approximately 5.5 minutes", result);
            Assert.DoesNotContain("RESPONSE TIMING", result);
        }

        [Fact]
        public void BuildDateePrompt_SubMinuteDelay_DoesNotExposeTimingToModel()
        {
            var result = SessionDocumentBuilder.BuildDateePrompt(
                MakeDateeContext(responseDelayMinutes: 0.3));

            Assert.DoesNotContain("less than 1 minute", result);
        }

        [Fact]
        public void BuildDateePrompt_Interest21_ExtremelyInterested()
        {
            var result = SessionDocumentBuilder.BuildDateePrompt(
                MakeDateeContext(interestBefore: 20, interestAfter: 21));

            Assert.Contains("Basically sold", result);
        }

        [Fact]
        public void BuildDateePrompt_Interest13_Engaged()
        {
            var result = SessionDocumentBuilder.BuildDateePrompt(
                MakeDateeContext(interestBefore: 10, interestAfter: 13));

            Assert.Contains("Engaged but not sold", result);
        }

        [Fact]
        public void BuildDateePrompt_Interest9_Lukewarm()
        {
            var result = SessionDocumentBuilder.BuildDateePrompt(
                MakeDateeContext(interestBefore: 10, interestAfter: 9));

            Assert.Contains("Skeptical", result);
        }

        [Fact]
        public void BuildDateePrompt_Interest5_Cooling()
        {
            var result = SessionDocumentBuilder.BuildDateePrompt(
                MakeDateeContext(interestBefore: 10, interestAfter: 5));

            Assert.Contains("Skeptical", result);
        }

        [Fact]
        public void BuildDateePrompt_Interest0_Unmatching()
        {
            var result = SessionDocumentBuilder.BuildDateePrompt(
                MakeDateeContext(interestBefore: 2, interestAfter: 0));

            Assert.Contains("Unmatched", result);
        }

        [Fact]
        public void BuildDateePrompt_ContainsSignalsInstruction()
        {
            var result = SessionDocumentBuilder.BuildDateePrompt(
                MakeDateeContext(interestBefore: 10, interestAfter: 12));

            Assert.Contains("[RESPONSE]", result);
            Assert.Contains("[SIGNALS]", result);
        }

        [Fact]
        public void BuildDateePrompt_WithTrapInstructions_IncludesThem()
        {
            var result = SessionDocumentBuilder.BuildDateePrompt(
                MakeDateeContext(activeTrapInstructions: new[] { "Trap effect: cringe aura" }));

            Assert.Contains("Trap effect: cringe aura", result);
        }

        // ═══════════════════════════════════════════════════════════════
        // BuildInterestChangeBeatPrompt
        // ═══════════════════════════════════════════════════════════════

        [Fact]
        public void BuildInterestChangeBeatPrompt_IncludesNameAndValues()
        {
            var result = SessionDocumentBuilder.BuildInterestChangeBeatPrompt(
                "MEGA_V", 10, 16, InterestState.VeryIntoIt);

            Assert.Contains("MEGA_V", result);
            Assert.Contains("10", result);
            Assert.Contains("16", result);
        }

        [Fact]
        public void BuildInterestChangeBeatPrompt_GenericCase_DoesNotCrash()
        {
            var result = SessionDocumentBuilder.BuildInterestChangeBeatPrompt(
                "OPP", 10, 13, InterestState.Interested);

            Assert.False(string.IsNullOrWhiteSpace(result));
            Assert.Contains("OPP", result);
        }

        [Fact]
        public void BuildInterestChangeBeatPrompt_ContainsOutputInstruction()
        {
            var result = SessionDocumentBuilder.BuildInterestChangeBeatPrompt(
                "V", 14, 16, InterestState.VeryIntoIt);

            Assert.Contains("Output only the message", result);
        }

        [Fact]
        public void BuildInterestChangeBeatPrompt_CrossedAbove15FromBelow_ShowsInvested()
        {
            var result = SessionDocumentBuilder.BuildInterestChangeBeatPrompt(
                "V", 14, 17, InterestState.VeryIntoIt);

            Assert.Contains("becoming more invested", result);
        }

        [Fact]
        public void BuildInterestChangeBeatPrompt_CrossedBelow8FromAbove_ShowsPullingBack()
        {
            var result = SessionDocumentBuilder.BuildInterestChangeBeatPrompt(
                "V", 8, 6, InterestState.Interested);

            Assert.Contains("pulling back", result);
        }

        [Fact]
        public void BuildInterestChangeBeatPrompt_DateSecured_SuggestsMeetUp()
        {
            var result = SessionDocumentBuilder.BuildInterestChangeBeatPrompt(
                "V", 24, 25, InterestState.DateSecured);

            Assert.Contains("meeting up", result);
        }

        [Fact]
        public void BuildInterestChangeBeatPrompt_Unmatched_ShowsUnmatching()
        {
            var result = SessionDocumentBuilder.BuildInterestChangeBeatPrompt(
                "V", 1, 0, InterestState.Unmatched);

            Assert.Contains("unmatching", result);
        }

        // ═══════════════════════════════════════════════════════════════
        // Interest behaviour boundary tests
        // ═══════════════════════════════════════════════════════════════

        [Fact]
        public void BuildDateePrompt_Interest16_Engaged()
        {
            var result = SessionDocumentBuilder.BuildDateePrompt(
                MakeDateeContext(interestBefore: 10, interestAfter: 16));

            Assert.Contains("Interested but holding back", result);
        }

        [Fact]
        public void BuildDateePrompt_Interest17_VeryInterested()
        {
            var result = SessionDocumentBuilder.BuildDateePrompt(
                MakeDateeContext(interestBefore: 10, interestAfter: 17));

            Assert.Contains("Interested but holding back", result);
        }

        [Fact]
        public void BuildDateePrompt_Interest12_Lukewarm()
        {
            var result = SessionDocumentBuilder.BuildDateePrompt(
                MakeDateeContext(interestBefore: 10, interestAfter: 12));

            Assert.Contains("Engaged but not sold", result);
        }

        [Fact]
        public void BuildDateePrompt_Interest8_Cooling()
        {
            var result = SessionDocumentBuilder.BuildDateePrompt(
                MakeDateeContext(interestBefore: 10, interestAfter: 8));

            Assert.Contains("Skeptical", result);
        }

        [Fact]
        public void BuildDateePrompt_Interest4_Disengaged()
        {
            var result = SessionDocumentBuilder.BuildDateePrompt(
                MakeDateeContext(interestBefore: 10, interestAfter: 4));

            Assert.Contains("Reconsidering", result);
        }

        [Fact]
        public void BuildDateePrompt_Interest25_ExtremelyInterested()
        {
            var result = SessionDocumentBuilder.BuildDateePrompt(
                MakeDateeContext(interestBefore: 10, interestAfter: 25));

            Assert.Contains("resistance dissolved", result);
        }

        [Fact]
        public void BuildDateePrompt_Interest1_Disengaged_NotUnmatching()
        {
            var result = SessionDocumentBuilder.BuildDateePrompt(
                MakeDateeContext(interestBefore: 2, interestAfter: 1));

            Assert.Contains("Reconsidering", result);
            Assert.DoesNotContain("Unmatched", result);
        }
    }
}
