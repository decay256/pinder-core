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
        // BuildDeliveryPrompt — Success path
        // ═══════════════════════════════════════════════════════════════

        [Fact]
        public void BuildDeliveryPrompt_Success_IncludesStatNameUppercase()
        {
            var option = new DialogueOption(StatType.Charm, "Smooth line");
            var result = SessionDocumentBuilder.BuildDeliveryPrompt(
                MakeDeliveryContext(chosenOption: option, beatDcBy: 7));

            Assert.Contains("Stat: CHARM", result);
        }

        [Fact]
        public void BuildDeliveryPrompt_Success_ShowsBeatDcMargin()
        {
            var option = new DialogueOption(StatType.Honesty, "Truth bomb");
            var result = SessionDocumentBuilder.BuildDeliveryPrompt(
                MakeDeliveryContext(chosenOption: option, beatDcBy: 9));

            Assert.Contains("Beat DC by 9", result);
        }

        [Fact]
        public void BuildDeliveryPrompt_SuccessPath_DoesNotContainFailureTier()
        {
            var option = new DialogueOption(StatType.Wit, "Clever quip");
            var result = SessionDocumentBuilder.BuildDeliveryPrompt(
                MakeDeliveryContext(chosenOption: option, beatDcBy: 3));

            Assert.Contains("[ENGINE — DELIVERY]", result);
            Assert.DoesNotContain("Failure tier:", result);
        }

        [Fact]
        public void BuildDeliveryPrompt_Success_ContainsOutputInstruction()
        {
            var option = new DialogueOption(StatType.Rizz, "Flirty msg");
            var result = SessionDocumentBuilder.BuildDeliveryPrompt(
                MakeDeliveryContext(chosenOption: option, beatDcBy: 2));

            Assert.Contains("Output only the message text", result);
        }

        // ═══════════════════════════════════════════════════════════════
        // BuildDeliveryPrompt — Failure path
        // ═══════════════════════════════════════════════════════════════

        [Theory]
        [InlineData(FailureTier.Fumble, "FUMBLE")]
        [InlineData(FailureTier.Misfire, "MISFIRE")]
        [InlineData(FailureTier.TropeTrap, "TROPE_TRAP")]
        [InlineData(FailureTier.Catastrophe, "CATASTROPHE")]
        [InlineData(FailureTier.Legendary, "LEGENDARY")]
        public void BuildDeliveryPrompt_EachFailureTier_ShowsCorrectTierName(FailureTier tier, string expectedLabel)
        {
            var option = new DialogueOption(StatType.Charm, "Attempt");
            var result = SessionDocumentBuilder.BuildDeliveryPrompt(
                MakeDeliveryContext(chosenOption: option, outcome: tier, beatDcBy: -5));

            Assert.Contains(expectedLabel, result);
        }

        [Fact]
        public void BuildDeliveryPrompt_Failure_ContainsCorruptContentPrinciple()
        {
            var option = new DialogueOption(StatType.Chaos, "Wild swing");
            var result = SessionDocumentBuilder.BuildDeliveryPrompt(
                MakeDeliveryContext(chosenOption: option, outcome: FailureTier.Catastrophe, beatDcBy: -12));

            Assert.Contains("FORM stays. The CONTENT breaks", result);
        }

        [Fact]
        public void BuildDeliveryPrompt_Failure_ShowsMissedDcByPositiveMargin()
        {
            var option = new DialogueOption(StatType.SelfAwareness, "Meta comment");
            var result = SessionDocumentBuilder.BuildDeliveryPrompt(
                MakeDeliveryContext(chosenOption: option, outcome: FailureTier.Fumble, beatDcBy: -2));

            Assert.Contains("FAILED", result);
            Assert.Contains("missed DC by 2", result);
        }

        [Fact]
        public void BuildDeliveryPrompt_WithTrapInstructions_IncludesTrapSection()
        {
            var option = new DialogueOption(StatType.Charm, "Line");
            var result = SessionDocumentBuilder.BuildDeliveryPrompt(
                MakeDeliveryContext(chosenOption: option, outcome: FailureTier.TropeTrap, beatDcBy: -7,
                    activeTrapInstructions: new[] { "Trap instruction A", "Trap instruction B" }));

            Assert.Contains("Trap instruction A", result);
            Assert.Contains("Trap instruction B", result);
        }

        [Fact]
        public void BuildDeliveryPrompt_NullTrapInstructions_NoTrapSection()
        {
            var option = new DialogueOption(StatType.Wit, "Joke");
            var result = SessionDocumentBuilder.BuildDeliveryPrompt(
                MakeDeliveryContext(chosenOption: option, outcome: FailureTier.Misfire, beatDcBy: -3));

            Assert.DoesNotContain("Active trap instructions:", result);
        }

        [Fact]
        public void BuildDeliveryPrompt_IncludesChosenOptionText()
        {
            var option = new DialogueOption(StatType.Honesty, "I really like your vibe");
            var result = SessionDocumentBuilder.BuildDeliveryPrompt(
                MakeDeliveryContext(chosenOption: option, beatDcBy: 5));

            Assert.Contains("I really like your vibe", result);
        }

        // Issue #491 — success delivery tiers use margin-based language
        [Fact]
        public void PromptTemplates_SuccessDelivery_ContainsMarginBasedTiers()
        {
            var t = PromptTemplates.SuccessDeliveryInstruction;
            // Clean success tier
            Assert.Contains("margin 1-4", t);
            // Strong success tier
            Assert.Contains("margin 5-9", t);
            // Critical / Nat 20 tier
            Assert.Contains("Critical success / Nat 20", t);
        }

        [Fact]
        public void PromptTemplates_SuccessDelivery_ContainsSharpenNotExpandConstraint()
        {
            var t = PromptTemplates.SuccessDeliveryInstruction;
            Assert.Contains("Rewrite — do not extend", t);
            Assert.Contains("Every sentence in the delivered version must have a counterpart in the intended version", t);
        }

        [Fact]
        public void PromptTemplates_SuccessDelivery_StrongSuccessAllowsOneAddition()
        {
            var t = PromptTemplates.SuccessDeliveryInstruction;
            Assert.Contains("If you add a word, you should cut a different word", t);
        }

        [Fact]
        public void PromptTemplates_SuccessDelivery_StrongSuccessProhibitsNewIdeas()
        {
            var t = PromptTemplates.SuccessDeliveryInstruction;
            Assert.Contains("You must NOT: append a new sentence at the end", t);
        }
    }
}
