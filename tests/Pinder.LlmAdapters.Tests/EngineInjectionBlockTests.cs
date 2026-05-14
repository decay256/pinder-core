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
    /// Verifies AC from Issue #544: options, delivery, and opponent injection formats.
    /// </summary>
    public class EngineInjectionBlockTests
    {
        // ── Helpers ──

        private static DialogueContext MakeDialogueContext(
            int currentTurn = 3,
            string playerName = "Velvet",
            string opponentName = "Sable",
            int currentInterest = 14,
            IReadOnlyList<(string Sender, string Text)>? conversationHistory = null,
            string opponentLastMessage = "hey",
            Dictionary<ShadowStatType, int>? shadowThresholds = null,
            List<CallbackOpportunity>? callbackOpportunities = null,
            int horninessLevel = 0,
            bool requiresRizzOption = false,
            string[]? activeTrapInstructions = null,
            string[]? activeTraps = null,
            string playerTextingStyle = "")
        {
            return new DialogueContext(
                playerPrompt: "velvet system prompt",
                opponentPrompt: "sable system prompt",
                conversationHistory: conversationHistory ?? new List<(string, string)>
                {
                    ("Velvet", "hey there"),
                    ("Sable", "omg hi!!"),
                    ("Velvet", "how's your night going"),
                    ("Sable", "so good lol")
                },
                opponentLastMessage: opponentLastMessage,
                activeTraps: activeTraps ?? Array.Empty<string>(),
                currentInterest: currentInterest,
                shadowThresholds: shadowThresholds,
                callbackOpportunities: callbackOpportunities,
                horninessLevel: horninessLevel,
                requiresRizzOption: requiresRizzOption,
                activeTrapInstructions: activeTrapInstructions,
                playerName: playerName,
                opponentName: opponentName,
                currentTurn: currentTurn,
                playerTextingStyle: playerTextingStyle);
        }

        private static DeliveryContext MakeDeliveryContext(
            DialogueOption? chosenOption = null,
            FailureTier outcome = FailureTier.None,
            int beatDcBy = 5,
            string playerName = "Velvet",
            string opponentName = "Sable",
            Dictionary<ShadowStatType, int>? shadowThresholds = null,
            string[]? activeTrapInstructions = null)
        {
            return new DeliveryContext(
                playerPrompt: "velvet system prompt",
                opponentPrompt: "sable system prompt",
                conversationHistory: new List<(string, string)>
                {
                    ("Velvet", "hey there"),
                    ("Sable", "omg hi!!")
                },
                opponentLastMessage: "omg hi!!",
                chosenOption: chosenOption ?? new DialogueOption(StatType.Wit, "you remind me of a song I can't quite place"),
                outcome: outcome,
                beatDcBy: beatDcBy,
                activeTraps: Array.Empty<string>(),
                shadowThresholds: shadowThresholds,
                activeTrapInstructions: activeTrapInstructions,
                playerName: playerName,
                opponentName: opponentName);
        }

        private static OpponentContext MakeOpponentContext(
            int interestBefore = 12,
            int interestAfter = 14,
            string playerDeliveredMessage = "you remind me of a song I can't quite place",
            double responseDelayMinutes = 2.5,
            string playerName = "Velvet",
            string opponentName = "Sable",
            Dictionary<ShadowStatType, int>? shadowThresholds = null,
            string[]? activeTrapInstructions = null,
            FailureTier deliveryTier = FailureTier.None)
        {
            return new OpponentContext(
                playerPrompt: "velvet system prompt",
                opponentPrompt: "sable system prompt",
                conversationHistory: new List<(string, string)>
                {
                    ("Velvet", "hey there"),
                    ("Sable", "omg hi!!")
                },
                opponentLastMessage: "omg hi!!",
                activeTraps: Array.Empty<string>(),
                currentInterest: interestAfter,
                playerDeliveredMessage: playerDeliveredMessage,
                interestBefore: interestBefore,
                interestAfter: interestAfter,
                responseDelayMinutes: responseDelayMinutes,
                shadowThresholds: shadowThresholds,
                activeTrapInstructions: activeTrapInstructions,
                playerName: playerName,
                opponentName: opponentName,
                deliveryTier: deliveryTier);
        }

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
            Assert.Contains("Generate 4 options for what Velvet might send", result);
        }

        [Fact]
        public void OptionsPrompt_ContainsFormatInstruction()
        {
            var result = SessionDocumentBuilder.BuildDialogueOptionsPrompt(MakeDialogueContext());
            Assert.Contains("Format: OPTION_A: [message] OPTION_B: [message] etc.", result);
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
        // ═══════════════════════════════════════════════════════════════

        [Fact]
        public void DeliveryPrompt_ContainsEngineDeliveryBlock()
        {
            var result = SessionDocumentBuilder.BuildDeliveryPrompt(MakeDeliveryContext());
            Assert.Contains("[ENGINE — DELIVERY]", result);
        }

        [Fact]
        public void DeliveryPrompt_ContainsChosenOption()
        {
            var result = SessionDocumentBuilder.BuildDeliveryPrompt(MakeDeliveryContext());
            Assert.Contains("Player chose: 'you remind me of a song I can't quite place'", result);
        }

        [Fact]
        public void DeliveryPrompt_Success_ContainsDiceResult()
        {
            var result = SessionDocumentBuilder.BuildDeliveryPrompt(MakeDeliveryContext(beatDcBy: 7));
            Assert.Contains("Dice result:", result);
        }

        [Fact]
        public void DeliveryPrompt_Success_ContainsWriteInstruction()
        {
            var result = SessionDocumentBuilder.BuildDeliveryPrompt(MakeDeliveryContext());
            Assert.Contains("Write the message Velvet actually sends", result);
        }

        [Fact]
        public void DeliveryPrompt_Failure_ContainsFailureContext()
        {
            var result = SessionDocumentBuilder.BuildDeliveryPrompt(
                MakeDeliveryContext(outcome: FailureTier.Misfire, beatDcBy: -4));
            Assert.Contains("Dice result:", result);
            Assert.Contains("MISFIRE", result);
        }

        [Fact]
        public void DeliveryPrompt_CleanSuccess_HasLandedNarrative()
        {
            var result = SessionDocumentBuilder.BuildDeliveryPrompt(
                MakeDeliveryContext(beatDcBy: 3));
            Assert.Contains("landed", result);
        }

        [Fact]
        public void DeliveryPrompt_CriticalSuccess_HasBestVersionNarrative()
        {
            var result = SessionDocumentBuilder.BuildDeliveryPrompt(
                MakeDeliveryContext(beatDcBy: 12));
            Assert.Contains("best version", result);
        }

        // ═══════════════════════════════════════════════════════════════
        // AC2: Roll context from YAML — RollContextBuilder
        // ═══════════════════════════════════════════════════════════════

        [Fact]
        public void DeliveryPrompt_WithRollContextBuilder_UsesCustomContext()
        {
            var flavors = new Dictionary<string, string>
            {
                { "§7.success-scale.5-9", "Custom: strong success flavor text" }
            };
            var builder = new RollContextBuilder(flavors);

            var result = SessionDocumentBuilder.BuildDeliveryPrompt(
                MakeDeliveryContext(beatDcBy: 7), builder);

            Assert.Contains("Custom: strong success flavor text", result);
        }

        [Fact]
        public void DeliveryPrompt_WithRollContextBuilder_FallsBackForMissing()
        {
            var flavors = new Dictionary<string, string>();
            var builder = new RollContextBuilder(flavors);

            var result = SessionDocumentBuilder.BuildDeliveryPrompt(
                MakeDeliveryContext(beatDcBy: 2), builder);

            // Should use hardcoded fallback
            Assert.Contains(RollContextBuilder.FallbackCleanSuccess, result);
        }

        // ═══════════════════════════════════════════════════════════════
        // AC3: Opponent injection format — [ENGINE — OPPONENT]
        // ═══════════════════════════════════════════════════════════════

        [Fact]
        public void OpponentPrompt_ContainsEngineOpponentBlock()
        {
            var result = SessionDocumentBuilder.BuildOpponentPrompt(MakeOpponentContext());
            Assert.Contains("[ENGINE — OPPONENT]", result);
        }

        [Fact]
        public void OpponentPrompt_ContainsOpponentNameAndInterest()
        {
            var result = SessionDocumentBuilder.BuildOpponentPrompt(
                MakeOpponentContext(interestAfter: 14));
            Assert.Contains("Sable is at Interest 14/25", result);
        }

        [Fact]
        public void OpponentPrompt_ContainsWriteInstruction()
        {
            var result = SessionDocumentBuilder.BuildOpponentPrompt(MakeOpponentContext());
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
        public void OpponentPrompt_InterestNarrativeMatchesBand(int interest, string expectedFragment)
        {
            var result = SessionDocumentBuilder.BuildOpponentPrompt(
                MakeOpponentContext(interestAfter: interest));
            Assert.Contains(expectedFragment, result);
        }

        [Fact]
        public void OpponentPrompt_Interest0_ShowsUnmatched()
        {
            var result = SessionDocumentBuilder.BuildOpponentPrompt(
                MakeOpponentContext(interestBefore: 2, interestAfter: 0));
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

        [Fact]
        public void DeliveryPrompt_ConversationHistoryPrecedesEngineBlock()
        {
            var result = SessionDocumentBuilder.BuildDeliveryPrompt(MakeDeliveryContext());

            int historyIdx = result.IndexOf("[CONVERSATION_START]");
            int engineIdx = result.IndexOf("[ENGINE — DELIVERY]");
            Assert.True(historyIdx >= 0);
            Assert.True(engineIdx >= 0);
            Assert.True(historyIdx < engineIdx);
        }

        [Fact]
        public void OpponentPrompt_EngineBlockPresentInOutput()
        {
            var result = SessionDocumentBuilder.BuildOpponentPrompt(MakeOpponentContext());

            int engineIdx = result.IndexOf("[ENGINE — OPPONENT]");
            Assert.True(engineIdx >= 0, "[ENGINE — OPPONENT] block must be present");
        }

        // ── Helper ──

        private static string? FindYamlPath(string relativePath)
        {
            string? dir = AppDomain.CurrentDomain.BaseDirectory;
            while (dir != null)
            {
                string candidate = System.IO.Path.Combine(dir, relativePath);
                if (System.IO.File.Exists(candidate)) return candidate;
                dir = System.IO.Path.GetDirectoryName(dir);
            }
            return null;
        }
    }
}
