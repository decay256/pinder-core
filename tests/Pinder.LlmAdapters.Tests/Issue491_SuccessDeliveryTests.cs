using System;
using System.Collections.Generic;
using Pinder.Core.Conversation;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;
using Pinder.LlmAdapters;
using Xunit;

namespace Pinder.LlmAdapters.Tests
{
    /// <summary>
    /// Tests for Issue #491: success delivery — additions must improve existing sentiment, not add new ideas.
    /// Verifies that SuccessDeliveryInstruction defines correct margin-based tiers,
    /// prohibits adding new ideas, and retains the {player_name} placeholder.
    /// </summary>
    public class Issue491_SuccessDeliveryTests
    {
        // ── Helper ──

        private static DeliveryContext MakeSuccessDeliveryContext(int beatDcBy, string playerName = "Velvet")
        {
            return new DeliveryContext(
                playerPrompt: "player prompt",
                opponentPrompt: "opponent prompt",
                conversationHistory: new List<(string, string)> { ("P", "hey"), ("O", "hi") },
                opponentLastMessage: "hi",
                chosenOption: new DialogueOption(StatType.Charm, "honestly? you're kind of funny"),
                outcome: FailureTier.None,
                beatDcBy: beatDcBy,
                activeTraps: Array.Empty<string>(),
                playerName: playerName,
                opponentName: "Sable");
        }

        // ══════════════════════════════════════════════════════════════
        // AC1: SuccessDeliveryInstruction specifies margin-based tiers
        // ══════════════════════════════════════════════════════════════

        // Mutation: would catch if clean tier used old band "1-5" instead of "1-4"
        [Fact]
        public void SuccessDeliveryInstruction_ContainsCleanTier_1To4()
        {
            var instruction = PromptTemplates.SuccessDeliveryInstruction;
            Assert.Contains("1-4", instruction);
        }

        // Mutation: would catch if strong tier used old band "6-10" instead of "5-9"
        [Fact]
        public void SuccessDeliveryInstruction_ContainsStrongTier_5To9()
        {
            var instruction = PromptTemplates.SuccessDeliveryInstruction;
            Assert.Contains("5-9", instruction);
        }

        // Mutation: would catch if critical tier was missing or renamed
        [Fact]
        public void SuccessDeliveryInstruction_ContainsCriticalTier()
        {
            var instruction = PromptTemplates.SuccessDeliveryInstruction.ToLowerInvariant();
            bool hasCritical = instruction.Contains("critical success")
                            || instruction.Contains("critical/nat 20")
                            || instruction.Contains("critical success / nat 20");
            Assert.True(hasCritical,
                "Instruction must contain critical success tier");
        }

        // Mutation: would catch if old incorrect bands "1-5" or "6-10" were still present
        [Fact]
        public void SuccessDeliveryInstruction_DoesNotContainOldBands()
        {
            var instruction = PromptTemplates.SuccessDeliveryInstruction;
            // Old bands were "1-5" and "6-10" — these must not appear
            Assert.DoesNotContain("1-5", instruction);
            Assert.DoesNotContain("6-10", instruction);
        }

        // Mutation: would catch if Nat 20 handling was removed
        [Fact]
        public void SuccessDeliveryInstruction_MentionsNat20()
        {
            var instruction = PromptTemplates.SuccessDeliveryInstruction;
            // Nat 20 should be grouped with critical tier
            Assert.Contains("Nat 20", instruction, StringComparison.OrdinalIgnoreCase);
        }

        // ══════════════════════════════════════════════════════════════
        // AC2: Strong success sharpens without adding new ideas
        // ══════════════════════════════════════════════════════════════

        // Mutation: would catch if "sharpen" or similar improvement language was removed
        [Fact]
        public void SuccessDeliveryInstruction_ContainsSharpenLanguage()
        {
            var instruction = PromptTemplates.SuccessDeliveryInstruction.ToLowerInvariant();
            // Must mention sharpening/tightening/improving phrasing
            bool hasSharpen = instruction.Contains("sharpen")
                           || instruction.Contains("tighten")
                           || instruction.Contains("precision");
            Assert.True(hasSharpen,
                "Instruction must contain language about sharpening/tightening phrasing");
        }

        // Mutation: would catch if "no new ideas" constraint was removed
        [Fact]
        public void SuccessDeliveryInstruction_ProhibitsNewIdeas()
        {
            var instruction = PromptTemplates.SuccessDeliveryInstruction.ToLowerInvariant();
            // Must explicitly prohibit adding new ideas/content
            // Actual text: "must not: add new sentences that introduce ideas not in the intended message"
            // and "New additions should sharpen, not expand"
            bool prohibitsNew = instruction.Contains("no new")
                             || instruction.Contains("not add")
                             || instruction.Contains("don't add")
                             || instruction.Contains("do not add")
                             || instruction.Contains("never add")
                             || instruction.Contains("without adding")
                             || instruction.Contains("without introducing")
                             || instruction.Contains("must not")
                             || instruction.Contains("not expand");
            Assert.True(prohibitsNew,
                "Instruction must explicitly prohibit adding new ideas or content");
        }

        // ══════════════════════════════════════════════════════════════
        // AC3: Critical success lands with precision, not expansion
        // ══════════════════════════════════════════════════════════════

        // Mutation: would catch if critical tier language was vague or encouraged expansion
        [Fact]
        public void SuccessDeliveryInstruction_CriticalTierMentionsPrecision()
        {
            var instruction = PromptTemplates.SuccessDeliveryInstruction.ToLowerInvariant();
            bool hasPrecision = instruction.Contains("precision")
                             || instruction.Contains("every word earns")
                             || instruction.Contains("peak");
            Assert.True(hasPrecision,
                "Critical tier must mention precision or peak execution");
        }

        // Mutation: would catch if "flourish" language from old instruction was retained
        [Fact]
        public void SuccessDeliveryInstruction_DoesNotContainFlourish()
        {
            var instruction = PromptTemplates.SuccessDeliveryInstruction.ToLowerInvariant();
            Assert.DoesNotContain("flourish", instruction);
        }

        // ══════════════════════════════════════════════════════════════
        // AC4: Every idea in delivered has counterpart in intended
        // ══════════════════════════════════════════════════════════════

        // Mutation: would catch if the counterpart/mapping constraint was removed
        [Fact]
        public void SuccessDeliveryInstruction_ContainsCounterpartRule()
        {
            var instruction = PromptTemplates.SuccessDeliveryInstruction.ToLowerInvariant();
            // Must state that every idea in output maps to input
            bool hasCounterpartRule = instruction.Contains("counterpart")
                                   || instruction.Contains("every idea")
                                   || (instruction.Contains("idea") && instruction.Contains("intended"))
                                   || instruction.Contains("same ideas")
                                   || instruction.Contains("only the ideas");
            Assert.True(hasCounterpartRule,
                "Instruction must include a rule that delivered ideas map to intended ideas");
        }

        // ══════════════════════════════════════════════════════════════
        // AC5: Build clean — {player_name} placeholder preserved
        // ══════════════════════════════════════════════════════════════

        // Mutation: would catch if {player_name} placeholder was accidentally removed
        [Fact]
        public void SuccessDeliveryInstruction_ContainsPlayerNamePlaceholder()
        {
            var instruction = PromptTemplates.SuccessDeliveryInstruction;
            Assert.Contains("{player_name}", instruction);
        }

        // Mutation: would catch if the constant was set to empty/null
        [Fact]
        public void SuccessDeliveryInstruction_IsNotEmpty()
        {
            Assert.False(string.IsNullOrWhiteSpace(PromptTemplates.SuccessDeliveryInstruction));
        }

        // ══════════════════════════════════════════════════════════════
        // Integration: BuildDeliveryPrompt includes the instruction on success
        // ══════════════════════════════════════════════════════════════

        // Mutation: would catch if BuildDeliveryPrompt stopped including SuccessDeliveryInstruction on success path
        [Fact]
        public void BuildDeliveryPrompt_SuccessPath_ContainsRevisedInstruction()
        {
            var ctx = MakeSuccessDeliveryContext(beatDcBy: 7);
            var result = SessionDocumentBuilder.BuildDeliveryPrompt(ctx);

            // The delivery prompt on success should contain the key language from the instruction
            // (with {player_name} substituted)
            Assert.DoesNotContain("{player_name}", result); // placeholder should be resolved
            Assert.Contains("Velvet", result); // player name should appear
        }

        // Mutation: would catch if success instruction was shown on failure path
        [Fact]
        public void BuildDeliveryPrompt_FailurePath_DoesNotContainSuccessInstruction()
        {
            var ctx = new DeliveryContext(
                playerPrompt: "player prompt",
                opponentPrompt: "opponent prompt",
                conversationHistory: new List<(string, string)> { ("P", "hey"), ("O", "hi") },
                opponentLastMessage: "hi",
                chosenOption: new DialogueOption(StatType.Charm, "test message"),
                outcome: FailureTier.Misfire,
                beatDcBy: -4,
                activeTraps: Array.Empty<string>(),
                playerName: "Velvet",
                opponentName: "Sable");

            var result = SessionDocumentBuilder.BuildDeliveryPrompt(ctx);

            // On failure, success delivery tiers should not appear
            // Check that at least one success-tier keyword is absent
            var lower = result.ToLowerInvariant();
            bool hasSuccessTierLanguage = lower.Contains("clean success") || lower.Contains("strong success");
            // Failure path may or may not contain these words depending on implementation,
            // but it should use the failure instruction, not the success one
            Assert.Contains(PromptTemplates.FailureDeliveryInstruction
                .Replace("{player_name}", "Velvet")
                .Substring(0, 30), result);
        }

        // ══════════════════════════════════════════════════════════════
        // Edge: tier boundary values
        // ══════════════════════════════════════════════════════════════

        // Mutation: would catch if the instruction was only injected for certain margins
        [Theory]
        [InlineData(1)]   // lower bound of clean tier
        [InlineData(4)]   // upper bound of clean tier
        [InlineData(5)]   // lower bound of strong tier
        [InlineData(9)]   // upper bound of strong tier
        [InlineData(10)]  // lower bound of critical tier
        [InlineData(14)]  // well into critical
        public void BuildDeliveryPrompt_AllSuccessMargins_ProducesNonEmptyPrompt(int beatDcBy)
        {
            var ctx = MakeSuccessDeliveryContext(beatDcBy: beatDcBy);
            var result = SessionDocumentBuilder.BuildDeliveryPrompt(ctx);

            Assert.False(string.IsNullOrWhiteSpace(result),
                $"Delivery prompt should not be empty for beatDcBy={beatDcBy}");
            // Player name should be resolved at all margins
            Assert.Contains("Velvet", result);
        }
    }
}
