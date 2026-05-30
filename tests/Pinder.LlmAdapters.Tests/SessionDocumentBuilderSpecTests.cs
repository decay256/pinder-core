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
    /// <summary>
    /// Spec-based tests for SessionDocumentBuilder, PromptTemplates, and CacheBlockBuilder.
    /// Tests derived from docs/specs/issue-207-spec.md.
    /// </summary>
    public partial class SessionDocumentBuilderSpecTests
    {
        // ── Helpers ──

        private static DialogueContext MakeDialogueContext(
            IReadOnlyList<(string Sender, string Text)> conversationHistory = null,
            string opponentLastMessage = "",
            string[] activeTraps = null,
            int currentInterest = 10,
            int currentTurn = 1,
            string playerName = "P",
            string opponentName = "O")
        {
            return new DialogueContext(
                playerPrompt: "player prompt",
                opponentPrompt: "opponent prompt",
                conversationHistory: conversationHistory ?? new List<(string, string)>(),
                opponentLastMessage: opponentLastMessage,
                activeTraps: activeTraps ?? Array.Empty<string>(),
                currentInterest: currentInterest,
                playerName: playerName,
                opponentName: opponentName,
                currentTurn: currentTurn);
        }

        private static DeliveryContext MakeDeliveryContext(
            IReadOnlyList<(string Sender, string Text)> conversationHistory = null,
            DialogueOption chosenOption = null,
            FailureTier outcome = FailureTier.None,
            int beatDcBy = 0,
            string[] activeTrapInstructions = null,
            string playerName = "P",
            string opponentName = "O",
            Dictionary<ShadowStatType, int> shadowThresholds = null)
        {
            return new DeliveryContext(
                playerPrompt: "player prompt",
                opponentPrompt: "opponent prompt",
                conversationHistory: conversationHistory ?? new List<(string, string)>(),
                opponentLastMessage: "",
                chosenOption: chosenOption ?? new DialogueOption(StatType.Charm, "default"),
                outcome: outcome,
                beatDcBy: beatDcBy,
                activeTraps: Array.Empty<string>(),
                shadowThresholds: shadowThresholds,
                activeTrapInstructions: activeTrapInstructions,
                playerName: playerName,
                opponentName: opponentName);
        }

        private static OpponentContext MakeOpponentContext(
            IReadOnlyList<(string Sender, string Text)> conversationHistory = null,
            string playerDeliveredMessage = "Hey",
            int interestBefore = 10,
            int interestAfter = 10,
            double responseDelayMinutes = 1.0,
            string[] activeTrapInstructions = null,
            string playerName = "P",
            string opponentName = "O",
            Dictionary<ShadowStatType, int> shadowThresholds = null)
        {
            return new OpponentContext(
                playerPrompt: "player prompt",
                opponentPrompt: "opponent prompt",
                conversationHistory: conversationHistory ?? new List<(string, string)>(),
                opponentLastMessage: "",
                activeTraps: Array.Empty<string>(),
                currentInterest: interestAfter,
                playerDeliveredMessage: playerDeliveredMessage,
                interestBefore: interestBefore,
                interestAfter: interestAfter,
                responseDelayMinutes: responseDelayMinutes,
                shadowThresholds: shadowThresholds,
                activeTrapInstructions: activeTrapInstructions,
                playerName: playerName,
                opponentName: opponentName);
        }

        // ═══════════════════════════════════════════════════════════════
        // AC1: SessionDocumentBuilder — All 4 builder methods
        // ═══════════════════════════════════════════════════════════════

        [Fact]
        public void BuildDialogueOptionsPrompt_ContainsConversationHistoryHeader()
        {
            var result = SessionDocumentBuilder.BuildDialogueOptionsPrompt(MakeDialogueContext());
            Assert.Contains("[CONVERSATION_START]", result);
        }

        [Fact]
        public void BuildDialogueOptionsPrompt_ContainsOpponentLastMessage()
        {
            var history = new List<(string, string)>
            {
                ("P", "Hey"),
                ("O", "Whatever")
            };

            var result = SessionDocumentBuilder.BuildDialogueOptionsPrompt(
                MakeDialogueContext(conversationHistory: history, opponentLastMessage: "Whatever", currentTurn: 2));

            // Opponent's last message is part of conversation history
            Assert.Contains("\"Whatever\"", result);
        }

        [Fact]
        public void BuildDialogueOptionsPrompt_ContainsGameStateSection()
        {
            var result = SessionDocumentBuilder.BuildDialogueOptionsPrompt(MakeDialogueContext());
            Assert.Contains("[ENGINE — Turn", result);
        }

        [Fact]
        public void BuildDialogueOptionsPrompt_MultipleTraps_CommaSeparated()
        {
            var result = SessionDocumentBuilder.BuildDialogueOptionsPrompt(
                MakeDialogueContext(activeTraps: new[] { "Cringe", "Spiral", "Overexplain" }));

            Assert.Contains("Active traps: Cringe, Spiral, Overexplain", result);
        }

        [Fact]
        public void BuildDialogueOptionsPrompt_TaskIncludesPlayerName()
        {
            var result = SessionDocumentBuilder.BuildDialogueOptionsPrompt(
                MakeDialogueContext(playerName: "MEGA_CHAD"));

            Assert.Contains("MEGA_CHAD", result);
            Assert.Contains("Generate exactly 3 dialogue options", result);
        }

        // ═══════════════════════════════════════════════════════════════
        // AC3: PromptTemplates
        // ═══════════════════════════════════════════════════════════════

        [Fact]
        public void PromptTemplates_AllFiveFieldsAreNonEmpty()
        {
            Assert.False(string.IsNullOrEmpty(PromptTemplates.DialogueOptionsInstruction));
            Assert.False(string.IsNullOrEmpty(PromptTemplates.SuccessDeliveryInstruction));
            Assert.False(string.IsNullOrEmpty(PromptTemplates.FailureDeliveryInstruction));
            Assert.False(string.IsNullOrEmpty(PromptTemplates.OpponentResponseInstruction));
            Assert.False(string.IsNullOrEmpty(PromptTemplates.InterestBeatInstruction));
        }

        [Fact]
        public void PromptTemplates_DialogueOptions_ContainsMetadataTags()
        {
            var t = PromptTemplates.DialogueOptionsInstruction;
            Assert.Contains("[STAT:", t);
            Assert.Contains("[CALLBACK:", t);
            Assert.Contains("[COMBO:", t);
            Assert.Contains("[TELL_BONUS:", t);
        }

        [Fact]
        public void PromptTemplates_SuccessDelivery_ContainsSuccessTierInfo()
        {
            var t = PromptTemplates.SuccessDeliveryInstruction;
            Assert.False(string.IsNullOrWhiteSpace(t));
            Assert.Contains("Output only the message text", t);
        }

        [Fact]
        public void PromptTemplates_FailureDelivery_ContainsAllFiveTiers()
        {
            var t = PromptTemplates.FailureDeliveryInstruction;
            Assert.Contains("FUMBLE", t);
            Assert.Contains("MISFIRE", t);
            Assert.Contains("TROPE_TRAP", t);
            Assert.Contains("CATASTROPHE", t);
            Assert.Contains("LEGENDARY", t);
        }

        [Fact]
        public void PromptTemplates_FailureDelivery_ContainsCorruptionPrinciple()
        {
            Assert.Contains("The CONTENT breaks", PromptTemplates.FailureDeliveryInstruction);
        }

        [Fact]
        public void PromptTemplates_OpponentResponse_ContainsSignalFormat()
        {
            var t = PromptTemplates.OpponentResponseInstruction;
            Assert.Contains("[RESPONSE]", t);
            Assert.Contains("[SIGNALS]", t);
            Assert.Contains("TELL:", t);
            Assert.Contains("WEAKNESS:", t);
        }

        [Fact]
        public void PromptTemplates_OpponentResponse_MentionsStatNames()
        {
            var t = PromptTemplates.OpponentResponseInstruction;
            Assert.Contains("CHARM", t);
        }

        // ═══════════════════════════════════════════════════════════════
        // AC4: CacheBlockBuilder
        // ═══════════════════════════════════════════════════════════════

        [Fact]
        public void CacheBlockBuilder_TwoPrompts_ReturnsTwoBlocks()
        {
            var blocks = CacheBlockBuilder.BuildCachedSystemBlocks("p", "o");
            Assert.Equal(2, blocks.Length);
        }

        [Fact]
        public void CacheBlockBuilder_TwoPrompts_CorrectOrder()
        {
            var blocks = CacheBlockBuilder.BuildCachedSystemBlocks("PLAYER_PROMPT", "OPPONENT_PROMPT");
            Assert.Equal("PLAYER_PROMPT", blocks[0].Text);
            Assert.Equal("OPPONENT_PROMPT", blocks[1].Text);
        }

        [Fact]
        public void CacheBlockBuilder_TwoPrompts_BothTypeText()
        {
            var blocks = CacheBlockBuilder.BuildCachedSystemBlocks("p", "o");
            Assert.Equal("text", blocks[0].Type);
            Assert.Equal("text", blocks[1].Type);
        }

        [Fact]
        public void CacheBlockBuilder_TwoPrompts_BothCacheControlEphemeral()
        {
            var blocks = CacheBlockBuilder.BuildCachedSystemBlocks("p", "o");
            Assert.NotNull(blocks[0].CacheControl);
            Assert.Equal("ephemeral", blocks[0].CacheControl!.Type);
            Assert.NotNull(blocks[1].CacheControl);
            Assert.Equal("ephemeral", blocks[1].CacheControl!.Type);
        }

        [Fact]
        public void CacheBlockBuilder_OpponentOnly_ReturnsSingleBlock()
        {
            var blocks = CacheBlockBuilder.BuildOpponentOnlySystemBlocks("o");
            Assert.Single(blocks);
        }

        [Fact]
        public void CacheBlockBuilder_OpponentOnly_HasCorrectContentAndCache()
        {
            var blocks = CacheBlockBuilder.BuildOpponentOnlySystemBlocks("MY_PROMPT");
            Assert.Equal("text", blocks[0].Type);
            Assert.Equal("MY_PROMPT", blocks[0].Text);
            Assert.NotNull(blocks[0].CacheControl);
            Assert.Equal("ephemeral", blocks[0].CacheControl!.Type);
        }

        [Fact]
        public void CacheBlockBuilder_EmptyPrompts_ReturnsBlocksWithEmptyText()
        {
            var blocks = CacheBlockBuilder.BuildCachedSystemBlocks("", "");
            Assert.Equal(2, blocks.Length);
            Assert.Equal("", blocks[0].Text);
            Assert.Equal("", blocks[1].Text);
            Assert.NotNull(blocks[0].CacheControl);
        }

        // ═══════════════════════════════════════════════════════════════
        // Error Conditions
        // ═══════════════════════════════════════════════════════════════

        [Fact]
        public void BuildDialogueOptionsPrompt_NullContext_ThrowsArgumentNull()
        {
            Assert.Throws<ArgumentNullException>(() =>
                SessionDocumentBuilder.BuildDialogueOptionsPrompt((DialogueContext)null!));
        }

        [Fact]
        public void BuildDeliveryPrompt_NullContext_ThrowsArgumentNull()
        {
            Assert.Throws<ArgumentNullException>(() =>
                SessionDocumentBuilder.BuildDeliveryPrompt((DeliveryContext)null!));
        }

        [Fact]
        public void BuildOpponentPrompt_NullContext_ThrowsArgumentNull()
        {
            Assert.Throws<ArgumentNullException>(() =>
                SessionDocumentBuilder.BuildOpponentPrompt((OpponentContext)null!));
        }

        [Fact]
        public void BuildInterestChangeBeatPrompt_NullOpponentName_ThrowsArgumentNull()
        {
            Assert.Throws<ArgumentNullException>(() =>
                SessionDocumentBuilder.BuildInterestChangeBeatPrompt(
                    null!, 10, 12, InterestState.Interested));
        }

        [Fact]
        public void CacheBlockBuilder_NullPlayerPrompt_Throws()
        {
            Assert.Throws<ArgumentNullException>(() =>
                CacheBlockBuilder.BuildCachedSystemBlocks(null!, "o"));
        }

        [Fact]
        public void CacheBlockBuilder_NullOpponentPrompt_Throws()
        {
            Assert.Throws<ArgumentNullException>(() =>
                CacheBlockBuilder.BuildCachedSystemBlocks("p", null!));
        }

        [Fact]
        public void CacheBlockBuilder_OpponentOnly_NullPrompt_Throws()
        {
            Assert.Throws<ArgumentNullException>(() =>
                CacheBlockBuilder.BuildOpponentOnlySystemBlocks(null!));
        }
    }
}
