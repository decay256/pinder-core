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
    public class SessionDocumentBuilderTests
    {
        // ── Helpers ──

        private static DialogueContext MakeDialogueContext(
            IReadOnlyList<(string Sender, string Text)> conversationHistory = null,
            string opponentLastMessage = "",
            string[] activeTraps = null,
            int currentInterest = 10,
            int currentTurn = 1,
            string playerName = "P",
            string opponentName = "O",
            Dictionary<ShadowStatType, int> shadowThresholds = null,
            List<CallbackOpportunity> callbackOpportunities = null,
            string[] activeTrapInstructions = null,
            int horninessLevel = 0,
            bool requiresRizzOption = false)
        {
            return new DialogueContext(
                playerPrompt: "player prompt",
                opponentPrompt: "opponent prompt",
                conversationHistory: conversationHistory ?? new List<(string, string)>(),
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

        // ── AC2: Conversation history formatting ──

        [Fact]
        public void BuildDialogueOptionsPrompt_OpponentProfileInUserMessage_NotSystem()
        {
            var result = SessionDocumentBuilder.BuildDialogueOptionsPrompt(
                MakeDialogueContext(playerName: "GERALD_42", opponentName: "VELVET"));

            // Opponent profile appears as informational context in user message
            Assert.Contains("OPPONENT PROFILE", result);
            Assert.Contains("opponent prompt", result);
            Assert.Contains("NOT who you are", result);
        }

        [Fact]
        public void BuildDialogueOptionsPrompt_EmptyOpponentPrompt_OmitsOpponentProfile()
        {
            var ctx = new DialogueContext(
                playerPrompt: "player prompt",
                opponentPrompt: "",
                conversationHistory: new List<(string, string)>(),
                opponentLastMessage: "",
                activeTraps: Array.Empty<string>(),
                currentInterest: 10,
                playerName: "P",
                opponentName: "O");

            var result = SessionDocumentBuilder.BuildDialogueOptionsPrompt(ctx);

            Assert.DoesNotContain("OPPONENT PROFILE", result);
        }

        [Fact]
        public void BuildDialogueOptionsPrompt_OpponentProfileBeforeConversationHistory()
        {
            var result = SessionDocumentBuilder.BuildDialogueOptionsPrompt(
                MakeDialogueContext(playerName: "GERALD_42", opponentName: "VELVET"));

            int opponentIdx = result.IndexOf("OPPONENT PROFILE");
            int historyIdx = result.IndexOf("[CONVERSATION_START]");
            Assert.True(opponentIdx < historyIdx,
                "Opponent profile should appear before conversation history");
        }

        [Fact]
        public void BuildDialogueOptionsPrompt_EmptyHistory_ContainsConversationStartAndCurrentTurn()
        {
            var result = SessionDocumentBuilder.BuildDialogueOptionsPrompt(
                MakeDialogueContext(playerName: "GERALD_42", opponentName: "VELVET"));

            Assert.Contains("[CONVERSATION_START]", result);
            Assert.Contains("[CURRENT_TURN]", result);

            // No turn markers between start and current
            int startIdx = result.IndexOf("[CONVERSATION_START]");
            int currentIdx = result.IndexOf("[CURRENT_TURN]");
            string between = result.Substring(
                startIdx + "[CONVERSATION_START]".Length,
                currentIdx - startIdx - "[CONVERSATION_START]".Length);
            Assert.DoesNotContain("[T", between.Trim());
        }

        [Fact]
        public void BuildDialogueOptionsPrompt_ThreeTurnHistory_CorrectTurnMarkers()
        {
            var history = new List<(string, string)>
            {
                ("GERALD_42", "Hey"),
                ("VELVET", "Hi"),
                ("GERALD_42", "How are you?"),
                ("VELVET", "Good"),
                ("GERALD_42", "Cool"),
                ("VELVET", "Indeed")
            };

            var result = SessionDocumentBuilder.BuildDialogueOptionsPrompt(
                MakeDialogueContext(conversationHistory: history, opponentLastMessage: "Indeed",
                    currentInterest: 12, currentTurn: 4, playerName: "GERALD_42", opponentName: "VELVET"));

            Assert.Contains("[T1|PLAYER|GERALD_42] \"Hey\"", result);
            Assert.Contains("[T1|OPPONENT|VELVET] \"Hi\"", result);
            Assert.Contains("[T2|PLAYER|GERALD_42] \"How are you?\"", result);
            Assert.Contains("[T2|OPPONENT|VELVET] \"Good\"", result);
            Assert.Contains("[T3|PLAYER|GERALD_42] \"Cool\"", result);
            Assert.Contains("[T3|OPPONENT|VELVET] \"Indeed\"", result);
        }

        [Fact]
        public void BuildDialogueOptionsPrompt_EightTurnHistory_AllTurnsPresent()
        {
            var history = new List<(string, string)>();
            for (int i = 0; i < 16; i++)
            {
                string sender = i % 2 == 0 ? "PLAYER_A" : "OPP_B";
                history.Add((sender, $"Message {i}"));
            }

            var result = SessionDocumentBuilder.BuildDialogueOptionsPrompt(
                MakeDialogueContext(conversationHistory: history, opponentLastMessage: "Message 15",
                    currentTurn: 9, playerName: "PLAYER_A", opponentName: "OPP_B"));

            for (int turn = 1; turn <= 8; turn++)
            {
                Assert.Contains($"[T{turn}|PLAYER|PLAYER_A]", result);
                Assert.Contains($"[T{turn}|OPPONENT|OPP_B]", result);
            }
        }

        [Fact]
        public void BuildDialogueOptionsPrompt_OddEntries_HandlesLonePlayerMessage()
        {
            var history = new List<(string, string)>
            {
                ("GERALD", "Hey"),
                ("VELVET", "Hi"),
                ("GERALD", "So anyway...")
            };

            var result = SessionDocumentBuilder.BuildDialogueOptionsPrompt(
                MakeDialogueContext(conversationHistory: history, opponentLastMessage: "Hi",
                    currentTurn: 2, playerName: "GERALD", opponentName: "VELVET"));

            Assert.Contains("[T1|PLAYER|GERALD] \"Hey\"", result);
            Assert.Contains("[T1|OPPONENT|VELVET] \"Hi\"", result);
            Assert.Contains("[T2|PLAYER|GERALD] \"So anyway...\"", result);
        }

        // ── AC1: All 4 builder methods ──

        [Fact]
        public void BuildDialogueOptionsPrompt_ActiveTraps_FormattedCorrectly()
        {
            var result = SessionDocumentBuilder.BuildDialogueOptionsPrompt(
                MakeDialogueContext(activeTraps: new[] { "Cringe", "Spiral" },
                    currentInterest: 12, playerName: "GERALD", opponentName: "VELVET"));

            Assert.Contains("Active traps: Cringe, Spiral", result);
        }

        [Fact]
        public void BuildDialogueOptionsPrompt_NoTraps_NoTrapsMentioned()
        {
            var result = SessionDocumentBuilder.BuildDialogueOptionsPrompt(
                MakeDialogueContext(playerName: "GERALD", opponentName: "VELVET"));

            // With no active traps, the output should not mention any trap names
            Assert.DoesNotContain("Active traps:", result);
        }

        [Fact]
        public void BuildDialogueOptionsPrompt_ContainsTaskInstruction()
        {
            var result = SessionDocumentBuilder.BuildDialogueOptionsPrompt(
                MakeDialogueContext(playerName: "GERALD", opponentName: "VELVET"));

            Assert.Contains("[ENGINE — Turn", result);
            Assert.Contains("Generate exactly 4 dialogue options for GERALD", result);
        }

        [Fact]
        public void BuildDeliveryPrompt_Success_ContainsSuccessInstruction()
        {
            var option = new DialogueOption(StatType.Wit, "Something clever");

            var result = SessionDocumentBuilder.BuildDeliveryPrompt(
                MakeDeliveryContext(chosenOption: option, outcome: FailureTier.None, beatDcBy: 4,
                    playerName: "GERALD", opponentName: "VELVET"));

            Assert.Contains("[ENGINE — DELIVERY]", result);
            Assert.Contains("Beat DC by 4", result);
            Assert.Contains("Stat: WIT", result);
            Assert.Contains("Output only the message text", result);
        }

        [Fact]
        public void BuildDeliveryPrompt_Misfire_ContainsFailureInstruction()
        {
            var option = new DialogueOption(StatType.Charm, "Tell me more");

            var result = SessionDocumentBuilder.BuildDeliveryPrompt(
                MakeDeliveryContext(chosenOption: option, outcome: FailureTier.Misfire, beatDcBy: -4,
                    activeTrapInstructions: new[] { "You are aware of how you're coming across." },
                    playerName: "GERALD", opponentName: "VELVET"));

            Assert.Contains("FAILED", result);
            Assert.Contains("missed DC by 4", result);
            Assert.Contains("MISFIRE", result);
            Assert.Contains("corrupt the CONTENT, not the delivery", result);
            Assert.Contains("Active trap instructions:", result);
            Assert.Contains("You are aware of how you're coming across.", result);
        }

        [Fact]
        public void BuildDeliveryPrompt_NullTrapInstructions_OmitsTrapSection()
        {
            var option = new DialogueOption(StatType.Honesty, "I'm just honest");

            var result = SessionDocumentBuilder.BuildDeliveryPrompt(
                MakeDeliveryContext(chosenOption: option, outcome: FailureTier.Fumble, beatDcBy: -1,
                    playerName: "GERALD", opponentName: "VELVET"));

            Assert.DoesNotContain("Active trap instructions:", result);
        }

        [Fact]
        public void BuildOpponentPrompt_ContainsAllSections()
        {
            var history = new List<(string, string)>
            {
                ("GERALD", "Hey"),
                ("VELVET", "Hi")
            };

            var result = SessionDocumentBuilder.BuildOpponentPrompt(
                MakeOpponentContext(conversationHistory: history, playerDeliveredMessage: "How are you?",
                    interestBefore: 10, interestAfter: 12, responseDelayMinutes: 3.5,
                    playerName: "GERALD", opponentName: "VELVET"));

            Assert.Contains("PLAYER'S LAST MESSAGE", result);
            Assert.Contains("\"How are you?\"", result);
            Assert.Contains("[ENGINE — OPPONENT]", result);
            Assert.Contains("Interest moved from 10 to 12 (+2)", result);
            Assert.Contains("Interest 12/25", result);
            Assert.Contains("RESPONSE TIMING", result);
            Assert.Contains("3.5 minutes", result);
            Assert.Contains("Engaged but not sold", result);
            Assert.Contains("[RESPONSE]", result);
            Assert.Contains("[SIGNALS]", result);
        }

        [Fact]
        public void BuildOpponentPrompt_NegativeDelta_FormattedCorrectly()
        {
            var result = SessionDocumentBuilder.BuildOpponentPrompt(
                MakeOpponentContext(playerDeliveredMessage: "Bye",
                    interestBefore: 12, interestAfter: 9, responseDelayMinutes: 5.0));

            Assert.Contains("Interest moved from 12 to 9 (-3)", result);
        }

        [Fact]
        public void BuildOpponentPrompt_SmallDelay_ShowsLessThanOneMinute()
        {
            var result = SessionDocumentBuilder.BuildOpponentPrompt(
                MakeOpponentContext(responseDelayMinutes: 0.5));

            Assert.Contains("less than 1 minute", result);
        }

        [Fact]
        public void BuildOpponentPrompt_InterestBehaviourBlock_HighInterest()
        {
            var result = SessionDocumentBuilder.BuildOpponentPrompt(
                MakeOpponentContext(interestBefore: 10, interestAfter: 18));

            Assert.Contains("Interested but holding back", result);
        }

        [Fact]
        public void BuildOpponentPrompt_InterestBehaviourBlock_LowInterest()
        {
            var result = SessionDocumentBuilder.BuildOpponentPrompt(
                MakeOpponentContext(interestBefore: 5, interestAfter: 3));

            Assert.Contains("Reconsidering", result);
        }

        [Fact]
        public void BuildInterestChangeBeatPrompt_CrossedAbove15_ShowsInvestedReaction()
        {
            var result = SessionDocumentBuilder.BuildInterestChangeBeatPrompt(
                "VELVET", 14, 16, InterestState.VeryIntoIt);

            Assert.Contains("VELVET", result);
            Assert.Contains("14", result);
            Assert.Contains("16", result);
            Assert.Contains("becoming more invested", result);
        }

        [Fact]
        public void BuildInterestChangeBeatPrompt_CrossedBelow8_ShowsCooling()
        {
            var result = SessionDocumentBuilder.BuildInterestChangeBeatPrompt(
                "VELVET", 9, 6, InterestState.Interested);

            Assert.Contains("pulling back", result);
        }

        [Fact]
        public void BuildInterestChangeBeatPrompt_DateSecured_ShowsMeetUp()
        {
            var result = SessionDocumentBuilder.BuildInterestChangeBeatPrompt(
                "VELVET", 23, 25, InterestState.DateSecured);

            Assert.Contains("meeting up", result);
        }

        [Fact]
        public void BuildInterestChangeBeatPrompt_Unmatched_ShowsUnmatching()
        {
            var result = SessionDocumentBuilder.BuildInterestChangeBeatPrompt(
                "VELVET", 2, 0, InterestState.Unmatched);

            Assert.Contains("unmatching", result);
        }

        // ── AC4: CacheBlockBuilder ──

        [Fact]
        public void BuildCachedSystemBlocks_ReturnsTwoBlocksWithEphemeralCache()
        {
            var blocks = CacheBlockBuilder.BuildCachedSystemBlocks("player prompt", "opponent prompt");

            Assert.Equal(2, blocks.Length);
            Assert.Equal("text", blocks[0].Type);
            Assert.Equal("player prompt", blocks[0].Text);
            Assert.NotNull(blocks[0].CacheControl);
            Assert.Equal("ephemeral", blocks[0].CacheControl!.Type);

            Assert.Equal("text", blocks[1].Type);
            Assert.Equal("opponent prompt", blocks[1].Text);
            Assert.NotNull(blocks[1].CacheControl);
            Assert.Equal("ephemeral", blocks[1].CacheControl!.Type);
        }

        [Fact]
        public void BuildOpponentOnlySystemBlocks_ReturnsOneBlockWithEphemeralCache()
        {
            var blocks = CacheBlockBuilder.BuildOpponentOnlySystemBlocks("opponent prompt");

            Assert.Single(blocks);
            Assert.Equal("text", blocks[0].Type);
            Assert.Equal("opponent prompt", blocks[0].Text);
            Assert.NotNull(blocks[0].CacheControl);
            Assert.Equal("ephemeral", blocks[0].CacheControl!.Type);
        }

        [Fact]
        public void BuildCachedSystemBlocks_EmptyPrompts_ReturnsBlocksWithEmptyText()
        {
            var blocks = CacheBlockBuilder.BuildCachedSystemBlocks("", "");

            Assert.Equal(2, blocks.Length);
            Assert.Equal("", blocks[0].Text);
            Assert.Equal("", blocks[1].Text);
        }

        [Fact]
        public void BuildCachedSystemBlocks_NullPlayerPrompt_Throws()
        {
            Assert.Throws<ArgumentNullException>(() =>
                CacheBlockBuilder.BuildCachedSystemBlocks(null!, "opponent"));
        }

        [Fact]
        public void BuildCachedSystemBlocks_NullOpponentPrompt_Throws()
        {
            Assert.Throws<ArgumentNullException>(() =>
                CacheBlockBuilder.BuildCachedSystemBlocks("player", null!));
        }

        [Fact]
        public void BuildOpponentOnlySystemBlocks_NullPrompt_Throws()
        {
            Assert.Throws<ArgumentNullException>(() =>
                CacheBlockBuilder.BuildOpponentOnlySystemBlocks(null!));
        }

        // ── Error conditions ──

        [Fact]
        public void BuildDialogueOptionsPrompt_NullContext_Throws()
        {
            Assert.Throws<ArgumentNullException>(() =>
                SessionDocumentBuilder.BuildDialogueOptionsPrompt((DialogueContext)null!));
        }

        [Fact]
        public void BuildDeliveryPrompt_NullContext_Throws()
        {
            Assert.Throws<ArgumentNullException>(() =>
                SessionDocumentBuilder.BuildDeliveryPrompt((DeliveryContext)null!));
        }

        [Fact]
        public void BuildOpponentPrompt_NullContext_Throws()
        {
            Assert.Throws<ArgumentNullException>(() =>
                SessionDocumentBuilder.BuildOpponentPrompt((OpponentContext)null!));
        }

        [Fact]
        public void BuildInterestChangeBeatPrompt_NullOpponentName_Throws()
        {
            Assert.Throws<ArgumentNullException>(() =>
                SessionDocumentBuilder.BuildInterestChangeBeatPrompt(
                    null!, 10, 12, InterestState.Interested));
        }

        // ── PromptTemplates existence check ──

        [Fact]
        public void PromptTemplates_AllFieldsAreDefined()
        {
            Assert.False(string.IsNullOrEmpty(PromptTemplates.DialogueOptionsInstruction));
            Assert.False(string.IsNullOrEmpty(PromptTemplates.SuccessDeliveryInstruction));
            Assert.False(string.IsNullOrEmpty(PromptTemplates.FailureDeliveryInstruction));
            Assert.False(string.IsNullOrEmpty(PromptTemplates.OpponentResponseInstruction));
            Assert.False(string.IsNullOrEmpty(PromptTemplates.InterestBeatInstruction));
        }

        [Fact]
        public void PromptTemplates_DialogueOptionsInstruction_ContainsKeyContent()
        {
            Assert.Contains("[STAT: X]", PromptTemplates.DialogueOptionsInstruction);
            Assert.Contains("[CALLBACK:", PromptTemplates.DialogueOptionsInstruction);
            Assert.Contains("[COMBO:", PromptTemplates.DialogueOptionsInstruction);
            Assert.Contains("[TELL_BONUS:", PromptTemplates.DialogueOptionsInstruction);
            Assert.Contains("exactly 4", PromptTemplates.DialogueOptionsInstruction);
        }

        [Fact]
        public void PromptTemplates_FailureDeliveryInstruction_ContainsAllTiers()
        {
            Assert.Contains("FUMBLE", PromptTemplates.FailureDeliveryInstruction);
            Assert.Contains("MISFIRE", PromptTemplates.FailureDeliveryInstruction);
            Assert.Contains("TROPE_TRAP", PromptTemplates.FailureDeliveryInstruction);
            Assert.Contains("CATASTROPHE", PromptTemplates.FailureDeliveryInstruction);
            Assert.Contains("LEGENDARY", PromptTemplates.FailureDeliveryInstruction);
        }

        [Fact]
        public void PromptTemplates_OpponentResponseInstruction_ContainsSignalsBlock()
        {
            Assert.Contains("[SIGNALS]", PromptTemplates.OpponentResponseInstruction);
            Assert.Contains("[RESPONSE]", PromptTemplates.OpponentResponseInstruction);
            Assert.Contains("TELL:", PromptTemplates.OpponentResponseInstruction);
            Assert.Contains("WEAKNESS:", PromptTemplates.OpponentResponseInstruction);
        }
    }
}
