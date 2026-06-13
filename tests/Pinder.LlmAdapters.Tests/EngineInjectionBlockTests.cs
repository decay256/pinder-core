using System;
using System.Collections.Generic;
using Pinder.Core.Conversation;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;
using Xunit;

namespace Pinder.LlmAdapters.Tests
{
    /// <summary>
    /// Tests for [ENGINE] injection block format in SessionDocumentBuilder.
    /// Verifies AC from Issue #544: options, delivery, and datee injection formats.
    /// </summary>
    [Collection("PromptTraceSingleton")]
    public partial class EngineInjectionBlockTests
    {
        // ═══════════════════════════════════════════════════════════════
        // AC1: Options injection format — [ENGINE — Turn N]
        // ═══════════════════════════════════════════════════════════════

        [Fact]
        public void OptionsPrompt_ContainsEngineBlock()
        {
            var result = SessionDocumentBuilder.BuildDialogueOptionsPrompt(MakeDialogueContext());
            Assert.Contains("[ENGINE — Turn 3]", result);
        }

        [Fact]
        public void OptionsPrompt_EngineBlockContainsPlayerName()
        {
            var result = SessionDocumentBuilder.BuildDialogueOptionsPrompt(MakeDialogueContext());
            Assert.Contains("Velvet is deciding what to send next", result);
        }

        [Fact]
        public void OptionsPrompt_EngineBlockContainsInterest()
        {
            var result = SessionDocumentBuilder.BuildDialogueOptionsPrompt(MakeDialogueContext(currentInterest: 18));
            Assert.Contains("Interest: 18/25", result);
        }

        [Fact]
        public void OptionsPrompt_ContainsGenerateInstruction()
        {
            var result = SessionDocumentBuilder.BuildDialogueOptionsPrompt(MakeDialogueContext());
            Assert.Contains("Generate 3 options for what Velvet might send", result);
        }

        [Fact]
        public void OptionsPrompt_ContainsFormatInstruction()
        {
            var result = SessionDocumentBuilder.BuildDialogueOptionsPrompt(MakeDialogueContext());
            Assert.Contains("Format: OPTION_A: [message] OPTION_B: [message] OPTION_C: [message]", result);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(5)]
        [InlineData(15)]
        public void OptionsPrompt_TurnNumberMatches(int turn)
        {
            var result = SessionDocumentBuilder.BuildDialogueOptionsPrompt(MakeDialogueContext(currentTurn: turn));
            Assert.Contains($"[ENGINE — Turn {turn}]", result);
        }

        // ═══════════════════════════════════════════════════════════════
        // AC2: Delivery injection format — [ENGINE — DELIVERY]
        //
        // #1138: the BuildDeliveryPrompt-based facts (DeliveryPrompt_*,
        // DeliveryPrompt_WithRollContextBuilder_*) were removed — the creative
        // delivery LLM call / prompt builder no longer exists (collapsed into
        // the deterministic DeliveryOverlay in #1125). RollContextBuilder's own
        // behaviour is still covered directly by the AC5 fallback/override
        // tests below (GetSuccessContext/GetFailureContext), which do not route
        // through the removed delivery prompt.
        // ═══════════════════════════════════════════════════════════════

        // ═══════════════════════════════════════════════════════════════
        // AC3: Datee injection format — [ENGINE — DATEE]
        // ═══════════════════════════════════════════════════════════════

        [Fact]
        public void DateePrompt_ContainsEngineDateeBlock()
        {
            var result = SessionDocumentBuilder.BuildDateePrompt(MakeDateeContext());
            Assert.Contains("[ENGINE — DATEE]", result);
        }

        [Fact]
        public void DateePrompt_ContainsDateeNameAndInterest()
        {
            var result = SessionDocumentBuilder.BuildDateePrompt(
                MakeDateeContext(interestAfter: 14));
            Assert.Contains("Sable is at Interest 14/25", result);
        }

        [Fact]
        public void DateePrompt_ContainsWriteInstruction()
        {
            var result = SessionDocumentBuilder.BuildDateePrompt(MakeDateeContext());
            Assert.Contains("Write Sable's response", result);
        }

        // ═══════════════════════════════════════════════════════════════
        // AC4: Interest narratives — 6 bands
        // ═══════════════════════════════════════════════════════════════

        [Theory]
        [InlineData(1, "Reconsidering")]
        [InlineData(4, "Reconsidering")]
        [InlineData(5, "Skeptical")]
        [InlineData(9, "Skeptical")]
        [InlineData(10, "Engaged but not sold")]
        [InlineData(14, "Engaged but not sold")]
        [InlineData(15, "Interested but holding back")]
        [InlineData(20, "Interested but holding back")]
        [InlineData(21, "Basically sold")]
        [InlineData(24, "Basically sold")]
        [InlineData(25, "resistance dissolved")]
        public void DateePrompt_InterestNarrativeMatchesBand(int interest, string expectedFragment)
        {
            var result = SessionDocumentBuilder.BuildDateePrompt(
                MakeDateeContext(interestAfter: interest));
            Assert.Contains(expectedFragment, result);
        }

        [Fact]
        public void DateePrompt_Interest0_ShowsUnmatched()
        {
            var result = SessionDocumentBuilder.BuildDateePrompt(
                MakeDateeContext(interestBefore: 2, interestAfter: 0));
            Assert.Contains("Unmatched", result);
        }

        // ═══════════════════════════════════════════════════════════════
        // AC5: Roll context narratives — RollContextBuilder
        // ═══════════════════════════════════════════════════════════════

        [Fact]
        public void RollContextBuilder_Fallback_CleanSuccess()
        {
            var builder = new RollContextBuilder();
            var result = builder.GetSuccessContext(3, false);
            Assert.Equal(RollContextBuilder.FallbackCleanSuccess, result);
        }

        [Fact]
        public void RollContextBuilder_Fallback_StrongSuccess()
        {
            var builder = new RollContextBuilder();
            var result = builder.GetSuccessContext(7, false);
            Assert.Equal(RollContextBuilder.FallbackStrongSuccess, result);
        }

        [Fact]
        public void RollContextBuilder_Fallback_CriticalSuccess()
        {
            var builder = new RollContextBuilder();
            var result = builder.GetSuccessContext(12, false);
            Assert.Equal(RollContextBuilder.FallbackCriticalSuccess, result);
        }

        [Fact]
        public void RollContextBuilder_Fallback_Nat20()
        {
            var builder = new RollContextBuilder();
            var result = builder.GetSuccessContext(15, true);
            Assert.Equal(RollContextBuilder.FallbackNat20, result);
        }

        [Theory]
        [InlineData(FailureTier.Fumble)]
        [InlineData(FailureTier.Misfire)]
        [InlineData(FailureTier.TropeTrap)]
        [InlineData(FailureTier.Catastrophe)]
        [InlineData(FailureTier.Legendary)]
        public void RollContextBuilder_Fallback_AllFailureTiers(FailureTier tier)
        {
            var builder = new RollContextBuilder();
            var result = builder.GetFailureContext(tier);
            Assert.False(string.IsNullOrEmpty(result));
        }

        [Fact]
        public void RollContextBuilder_WithYaml_OverridesFallback()
        {
            var flavors = new Dictionary<string, string>
            {
                { "§7.fail-tier.fumble", "Custom fumble text from YAML" }
            };
            var builder = new RollContextBuilder(flavors);
            var result = builder.GetFailureContext(FailureTier.Fumble);
            Assert.Equal("Custom fumble text from YAML", result);
        }

        [Fact]
        public void RollContextBuilder_WithYaml_FallsBackForMissingEntries()
        {
            var flavors = new Dictionary<string, string>
            {
                { "§7.fail-tier.fumble", "Custom fumble" }
            };
            var builder = new RollContextBuilder(flavors);

            // Misfire is not in the yaml dict, should use fallback
            var result = builder.GetFailureContext(FailureTier.Misfire);
            Assert.Equal(RollContextBuilder.FallbackMisfire, result);
        }

        // ═══════════════════════════════════════════════════════════════
        // AC5: RollContextBuilder.FromRuleBook integration
        // ═══════════════════════════════════════════════════════════════

        [Fact]
        public void RollContextBuilder_FromRuleBook_LoadsRealYaml()
        {
            string yamlPath = FindYamlPath("rules/extracted/rules-v3-enriched.yaml");
            if (yamlPath == null)
            {
                // Skip if YAML not available in test environment
                return;
            }

            string yamlContent = System.IO.File.ReadAllText(yamlPath);
            var ruleBook = Pinder.Rules.RuleBook.LoadFrom(yamlContent);
            var builder = RollContextBuilder.FromRuleBook(ruleBook);

            // Should get non-empty results for all tiers
            Assert.False(string.IsNullOrEmpty(builder.GetSuccessContext(3, false)));
            Assert.False(string.IsNullOrEmpty(builder.GetFailureContext(FailureTier.Fumble)));
            Assert.False(string.IsNullOrEmpty(builder.GetFailureContext(FailureTier.Catastrophe)));
        }

        // ═══════════════════════════════════════════════════════════════
        // AC6: Interest narratives configurable via PromptTemplates
        // ═══════════════════════════════════════════════════════════════

        [Fact]
        public void InterestNarrative_AllSixBandsDefined()
        {
            // Verify all 6 band constants exist and are non-empty
            Assert.False(string.IsNullOrEmpty(PromptTemplates.InterestNarrative_1_4));
            Assert.False(string.IsNullOrEmpty(PromptTemplates.InterestNarrative_5_9));
            Assert.False(string.IsNullOrEmpty(PromptTemplates.InterestNarrative_10_14));
            Assert.False(string.IsNullOrEmpty(PromptTemplates.InterestNarrative_15_20));
            Assert.False(string.IsNullOrEmpty(PromptTemplates.InterestNarrative_21_24));
            Assert.False(string.IsNullOrEmpty(PromptTemplates.InterestNarrative_25));
        }

        [Fact]
        public void GetInterestNarrative_CoversBoundaryValues()
        {
            Assert.NotEqual(PromptTemplates.GetInterestNarrative(4), PromptTemplates.GetInterestNarrative(5));
            Assert.NotEqual(PromptTemplates.GetInterestNarrative(9), PromptTemplates.GetInterestNarrative(10));
            Assert.NotEqual(PromptTemplates.GetInterestNarrative(14), PromptTemplates.GetInterestNarrative(15));
            Assert.NotEqual(PromptTemplates.GetInterestNarrative(20), PromptTemplates.GetInterestNarrative(21));
            Assert.NotEqual(PromptTemplates.GetInterestNarrative(24), PromptTemplates.GetInterestNarrative(25));
        }

        // ═══════════════════════════════════════════════════════════════
        // AC7: Build clean — format correctness
        // ═══════════════════════════════════════════════════════════════

        [Fact]
        public void OptionsPrompt_ConversationHistoryPrecedesEngineBlock()
        {
            var result = SessionDocumentBuilder.BuildDialogueOptionsPrompt(MakeDialogueContext());

            int historyIdx = result.IndexOf("[CONVERSATION_START]");
            int engineIdx = result.IndexOf("[ENGINE — Turn");
            Assert.True(historyIdx >= 0, "Conversation history must be present");
            Assert.True(engineIdx >= 0, "ENGINE block must be present");
            Assert.True(historyIdx < engineIdx, "Conversation history must precede ENGINE block");
        }

        // #1138: DeliveryPrompt_ConversationHistoryPrecedesEngineBlock removed —
        // BuildDeliveryPrompt is gone (delivery collapsed into DeliveryOverlay,
        // #1125). The Options/Datee history-ordering guarantees above/below are
        // retained.

        [Fact]
        public void DateePrompt_EngineBlockPresentInOutput()
        {
            var result = SessionDocumentBuilder.BuildDateePrompt(MakeDateeContext());

            int engineIdx = result.IndexOf("[ENGINE — DATEE]");
            Assert.True(engineIdx >= 0, "[ENGINE — DATEE] block must be present");
        }
    }
}
