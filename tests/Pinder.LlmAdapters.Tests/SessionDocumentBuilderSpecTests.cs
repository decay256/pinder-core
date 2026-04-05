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
    public class SessionDocumentBuilderSpecTests
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
        // AC2: Conversation History Formatting
        // ═══════════════════════════════════════════════════════════════

        [Fact]
        public void BuildDialogueOptionsPrompt_EmptyHistory_NoTurnMarkersBetweenStartAndCurrent()
        {
            var result = SessionDocumentBuilder.BuildDialogueOptionsPrompt(MakeDialogueContext());

            int startEnd = result.IndexOf("[CONVERSATION_START]") + "[CONVERSATION_START]".Length;
            int currentStart = result.IndexOf("[CURRENT_TURN]");
            string between = result.Substring(startEnd, currentStart - startEnd);

            Assert.DoesNotContain("[T", between);
        }

        [Fact]
        public void BuildDialogueOptionsPrompt_FourEntries_TurnNumbersIncrementByPair()
        {
            var history = new List<(string, string)>
            {
                ("ALICE", "msg0"),
                ("BOB", "msg1"),
                ("ALICE", "msg2"),
                ("BOB", "msg3")
            };

            var result = SessionDocumentBuilder.BuildDialogueOptionsPrompt(
                MakeDialogueContext(conversationHistory: history, opponentLastMessage: "msg3",
                    currentTurn: 3, playerName: "ALICE", opponentName: "BOB"));

            Assert.Contains("[T1|PLAYER|ALICE] \"msg0\"", result);
            Assert.Contains("[T1|OPPONENT|BOB] \"msg1\"", result);
            Assert.Contains("[T2|PLAYER|ALICE] \"msg2\"", result);
            Assert.Contains("[T2|OPPONENT|BOB] \"msg3\"", result);
            Assert.DoesNotContain("[T3|", result);
            Assert.DoesNotContain("[T4|", result);
        }

        [Fact]
        public void BuildDialogueOptionsPrompt_TwentyTurnHistory_AllTurnsPresent()
        {
            var history = new List<(string, string)>();
            for (int i = 0; i < 40; i++)
            {
                string sender = i % 2 == 0 ? "PLAYER_X" : "OPP_Y";
                history.Add((sender, $"msg{i}"));
            }

            var result = SessionDocumentBuilder.BuildDialogueOptionsPrompt(
                MakeDialogueContext(conversationHistory: history, opponentLastMessage: "msg39",
                    currentTurn: 21, playerName: "PLAYER_X", opponentName: "OPP_Y"));

            for (int turn = 1; turn <= 20; turn++)
            {
                Assert.Contains($"[T{turn}|PLAYER|PLAYER_X]", result);
                Assert.Contains($"[T{turn}|OPPONENT|OPP_Y]", result);
            }
        }

        [Fact]
        public void BuildDialogueOptionsPrompt_SingleEntry_FormatsCorrectly()
        {
            var history = new List<(string, string)> { ("GERALD", "Hello!") };

            var result = SessionDocumentBuilder.BuildDialogueOptionsPrompt(
                MakeDialogueContext(conversationHistory: history, playerName: "GERALD", opponentName: "V"));

            Assert.Contains("[T1|PLAYER|GERALD] \"Hello!\"", result);
            Assert.Contains("[CURRENT_TURN]", result);
        }

        [Fact]
        public void BuildDialogueOptionsPrompt_MessageWithDoubleQuotes_PreservedAsIs()
        {
            var history = new List<(string, string)>
            {
                ("P", "She said \"wow\" to me"),
                ("O", "Really?")
            };

            var result = SessionDocumentBuilder.BuildDialogueOptionsPrompt(
                MakeDialogueContext(conversationHistory: history, opponentLastMessage: "Really?", currentTurn: 2));

            Assert.Contains("She said \"wow\" to me", result);
        }

        [Fact]
        public void BuildDialogueOptionsPrompt_EmptyMessageText_FormatsAsEmptyQuotes()
        {
            var history = new List<(string, string)>
            {
                ("P", ""),
                ("O", "response")
            };

            var result = SessionDocumentBuilder.BuildDialogueOptionsPrompt(
                MakeDialogueContext(conversationHistory: history, opponentLastMessage: "response", currentTurn: 2));

            Assert.Contains("[T1|PLAYER|P] \"\"", result);
        }

        [Fact]
        public void BuildDialogueOptionsPrompt_NamesWithSpaces_UsedAsIs()
        {
            var history = new List<(string, string)>
            {
                ("Big Gerald", "Hey"),
                ("Lady V", "Hi")
            };

            var result = SessionDocumentBuilder.BuildDialogueOptionsPrompt(
                MakeDialogueContext(conversationHistory: history, opponentLastMessage: "Hi",
                    currentTurn: 2, playerName: "Big Gerald", opponentName: "Lady V"));

            Assert.Contains("[T1|PLAYER|Big Gerald] \"Hey\"", result);
            Assert.Contains("[T1|OPPONENT|Lady V] \"Hi\"", result);
        }

        [Fact]
        public void BuildDialogueOptionsPrompt_RoleDetermination_CaseSensitive()
        {
            var history = new List<(string, string)>
            {
                ("Gerald", "Hey"),
                ("gerald", "Hi")
            };

            var result = SessionDocumentBuilder.BuildDialogueOptionsPrompt(
                MakeDialogueContext(conversationHistory: history, opponentLastMessage: "Hi",
                    currentTurn: 2, playerName: "Gerald", opponentName: "gerald"));

            Assert.Contains("[T1|PLAYER|Gerald] \"Hey\"", result);
            Assert.Contains("[T1|OPPONENT|gerald] \"Hi\"", result);
        }

        [Fact]
        public void BuildOpponentPrompt_HistoryExcludesCurrentPlayerMessage()
        {
            var history = new List<(string, string)>
            {
                ("P", "Turn1Player"),
                ("O", "Turn1Opp")
            };

            var result = SessionDocumentBuilder.BuildOpponentPrompt(
                MakeOpponentContext(conversationHistory: history, playerDeliveredMessage: "Turn2Player",
                    interestBefore: 10, interestAfter: 12, responseDelayMinutes: 3.0));

            Assert.Contains("[T1|PLAYER|P] \"Turn1Player\"", result);
            Assert.Contains("[T1|OPPONENT|O] \"Turn1Opp\"", result);
            Assert.Contains("PLAYER'S LAST MESSAGE", result);
            Assert.Contains("\"Turn2Player\"", result);
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
            Assert.Contains("Generate exactly 4 dialogue options", result);
        }

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

            Assert.Contains("corrupt the CONTENT", result);
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

        // ═══════════════════════════════════════════════════════════════
        // BuildOpponentPrompt
        // ═══════════════════════════════════════════════════════════════

        [Fact]
        public void BuildOpponentPrompt_PositiveDelta_ShowsPlusSign()
        {
            var result = SessionDocumentBuilder.BuildOpponentPrompt(
                MakeOpponentContext(interestBefore: 8, interestAfter: 11, responseDelayMinutes: 2.0));

            Assert.Contains("Interest moved from 8 to 11 (+3)", result);
        }

        [Fact]
        public void BuildOpponentPrompt_ZeroDelta_ShowsPlusZero()
        {
            var result = SessionDocumentBuilder.BuildOpponentPrompt(
                MakeOpponentContext(interestBefore: 10, interestAfter: 10, responseDelayMinutes: 2.0));

            Assert.Contains("Interest moved from 10 to 10 (+0)", result);
        }

        [Fact]
        public void BuildOpponentPrompt_ShowsCurrentInterestOutOf25()
        {
            var result = SessionDocumentBuilder.BuildOpponentPrompt(
                MakeOpponentContext(interestBefore: 10, interestAfter: 15));

            Assert.Contains("Interest 15/25", result);
        }

        [Fact]
        public void BuildOpponentPrompt_NormalDelay_ShowsApproximateMinutes()
        {
            var result = SessionDocumentBuilder.BuildOpponentPrompt(
                MakeOpponentContext(responseDelayMinutes: 5.5));

            Assert.Contains("approximately 5.5 minutes", result);
        }

        [Fact]
        public void BuildOpponentPrompt_SubMinuteDelay_ShowsLessThanOneMinute()
        {
            var result = SessionDocumentBuilder.BuildOpponentPrompt(
                MakeOpponentContext(responseDelayMinutes: 0.3));

            Assert.Contains("less than 1 minute", result);
        }

        [Fact]
        public void BuildOpponentPrompt_Interest21_ExtremelyInterested()
        {
            var result = SessionDocumentBuilder.BuildOpponentPrompt(
                MakeOpponentContext(interestBefore: 20, interestAfter: 21));

            Assert.Contains("Basically sold", result);
        }

        [Fact]
        public void BuildOpponentPrompt_Interest13_Engaged()
        {
            var result = SessionDocumentBuilder.BuildOpponentPrompt(
                MakeOpponentContext(interestBefore: 10, interestAfter: 13));

            Assert.Contains("Engaged but not sold", result);
        }

        [Fact]
        public void BuildOpponentPrompt_Interest9_Lukewarm()
        {
            var result = SessionDocumentBuilder.BuildOpponentPrompt(
                MakeOpponentContext(interestBefore: 10, interestAfter: 9));

            Assert.Contains("Skeptical", result);
        }

        [Fact]
        public void BuildOpponentPrompt_Interest5_Cooling()
        {
            var result = SessionDocumentBuilder.BuildOpponentPrompt(
                MakeOpponentContext(interestBefore: 10, interestAfter: 5));

            Assert.Contains("Skeptical", result);
        }

        [Fact]
        public void BuildOpponentPrompt_Interest0_Unmatching()
        {
            var result = SessionDocumentBuilder.BuildOpponentPrompt(
                MakeOpponentContext(interestBefore: 2, interestAfter: 0));

            Assert.Contains("Unmatched", result);
        }

        [Fact]
        public void BuildOpponentPrompt_ContainsSignalsInstruction()
        {
            var result = SessionDocumentBuilder.BuildOpponentPrompt(
                MakeOpponentContext(interestBefore: 10, interestAfter: 12));

            Assert.Contains("[RESPONSE]", result);
            Assert.Contains("[SIGNALS]", result);
        }

        [Fact]
        public void BuildOpponentPrompt_WithTrapInstructions_IncludesThem()
        {
            var result = SessionDocumentBuilder.BuildOpponentPrompt(
                MakeOpponentContext(activeTrapInstructions: new[] { "Trap effect: cringe aura" }));

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
            Assert.Contains("corrupt the CONTENT", PromptTemplates.FailureDeliveryInstruction);
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

        // ═══════════════════════════════════════════════════════════════
        // Interest behaviour boundary tests
        // ═══════════════════════════════════════════════════════════════

        [Fact]
        public void BuildOpponentPrompt_Interest16_Engaged()
        {
            var result = SessionDocumentBuilder.BuildOpponentPrompt(
                MakeOpponentContext(interestBefore: 10, interestAfter: 16));

            Assert.Contains("Interested but holding back", result);
        }

        [Fact]
        public void BuildOpponentPrompt_Interest17_VeryInterested()
        {
            var result = SessionDocumentBuilder.BuildOpponentPrompt(
                MakeOpponentContext(interestBefore: 10, interestAfter: 17));

            Assert.Contains("Interested but holding back", result);
        }

        [Fact]
        public void BuildOpponentPrompt_Interest12_Lukewarm()
        {
            var result = SessionDocumentBuilder.BuildOpponentPrompt(
                MakeOpponentContext(interestBefore: 10, interestAfter: 12));

            Assert.Contains("Engaged but not sold", result);
        }

        [Fact]
        public void BuildOpponentPrompt_Interest8_Cooling()
        {
            var result = SessionDocumentBuilder.BuildOpponentPrompt(
                MakeOpponentContext(interestBefore: 10, interestAfter: 8));

            Assert.Contains("Skeptical", result);
        }

        [Fact]
        public void BuildOpponentPrompt_Interest4_Disengaged()
        {
            var result = SessionDocumentBuilder.BuildOpponentPrompt(
                MakeOpponentContext(interestBefore: 10, interestAfter: 4));

            Assert.Contains("Reconsidering", result);
        }

        [Fact]
        public void BuildOpponentPrompt_Interest25_ExtremelyInterested()
        {
            var result = SessionDocumentBuilder.BuildOpponentPrompt(
                MakeOpponentContext(interestBefore: 10, interestAfter: 25));

            Assert.Contains("resistance dissolved", result);
        }

        [Fact]
        public void BuildOpponentPrompt_Interest1_Disengaged_NotUnmatching()
        {
            var result = SessionDocumentBuilder.BuildOpponentPrompt(
                MakeOpponentContext(interestBefore: 2, interestAfter: 1));

            Assert.Contains("Reconsidering", result);
            Assert.DoesNotContain("Unmatched", result);
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
            Assert.Contains("sharpen, not expand", t);
            Assert.Contains("every idea in the delivered version should have a counterpart in the intended version", t);
        }

        [Fact]
        public void PromptTemplates_SuccessDelivery_StrongSuccessAllowsOneAddition()
        {
            var t = PromptTemplates.SuccessDeliveryInstruction;
            Assert.Contains("add ONE word or phrase that makes the existing sentiment more precise", t);
        }

        [Fact]
        public void PromptTemplates_SuccessDelivery_StrongSuccessProhibitsNewIdeas()
        {
            var t = PromptTemplates.SuccessDeliveryInstruction;
            Assert.Contains("must not: add new sentences that introduce ideas not in the intended message", t);
        }
    }
}
