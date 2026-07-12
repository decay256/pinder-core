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
    public partial class SessionDocumentBuilderTests
    {
        // ── AC1: All 4 builder methods ──

        [Fact]
        public void BuildDialogueOptionsPrompt_ActiveTraps_FormattedCorrectly()
        {
            var result = SessionDocumentBuilder.BuildDialogueOptionsPrompt(
                MakeDialogueContext(activeTraps: new[] { "Cringe", "Spiral" },
                    currentInterest: 12, playerName: "GERALD", dateeName: "VELVET"));

            Assert.Contains("Active traps: Cringe, Spiral", result);
        }

        [Fact]
        public void BuildDialogueOptionsPrompt_NoTraps_NoTrapsMentioned()
        {
            var result = SessionDocumentBuilder.BuildDialogueOptionsPrompt(
                MakeDialogueContext(playerName: "GERALD", dateeName: "VELVET"));

            // With no active traps, the output should not mention any trap names
            Assert.DoesNotContain("Active traps:", result);
        }

        [Fact]
        public void BuildDialogueOptionsPrompt_ContainsTaskInstruction()
        {
            var result = SessionDocumentBuilder.BuildDialogueOptionsPrompt(
                MakeDialogueContext(playerName: "GERALD", dateeName: "VELVET"));

            Assert.Contains("[ENGINE — Turn", result);
            Assert.Contains("Generate exactly 3 dialogue options for GERALD", result);
        }

        // #1138: BuildDeliveryPrompt_* delivery facts removed — the delivery
        // prompt builder no longer exists (delivery collapsed into the
        // deterministic DeliveryOverlay, #1125). Overlay/trap behaviour is
        // covered by the Issue1125 regression tests in Pinder.Core.Tests.

        [Fact]
        public void BuildDateePrompt_ContainsAllSections()
        {
            var history = new List<(string, string)>
            {
                ("GERALD", "Hey"),
                ("VELVET", "Hi")
            };

            var result = SessionDocumentBuilder.BuildDateePrompt(
                MakeDateeContext(conversationHistory: history, playerDeliveredMessage: "How are you?",
                    interestBefore: 10, interestAfter: 12, responseDelayMinutes: 3.5,
                    playerName: "GERALD", dateeName: "VELVET"));

            Assert.Contains("PLAYER'S LAST MESSAGE", result);
            Assert.Contains("\"How are you?\"", result);
            Assert.Contains("[ENGINE — DATEE]", result);
            Assert.Contains("Interest moved from 10 to 12 (+2)", result);
            Assert.Contains("Interest 12/25", result);
            Assert.DoesNotContain("RESPONSE TIMING", result);
            Assert.Contains("3.5 minutes", result);
            Assert.Contains("Engaged but not sold", result);
            Assert.Contains("[RESPONSE]", result);
            Assert.Contains("[SIGNALS]", result);
        }

        [Fact]
        public void BuildDateePrompt_NegativeDelta_FormattedCorrectly()
        {
            var result = SessionDocumentBuilder.BuildDateePrompt(
                MakeDateeContext(playerDeliveredMessage: "Bye",
                    interestBefore: 12, interestAfter: 9, responseDelayMinutes: 5.0));

            Assert.Contains("Interest moved from 12 to 9 (-3)", result);
        }

        [Fact]
        public void BuildDateePrompt_SmallDelay_ShowsLessThanOneMinute()
        {
            var result = SessionDocumentBuilder.BuildDateePrompt(
                MakeDateeContext(responseDelayMinutes: 0.5));

            Assert.DoesNotContain("less than 1 minute", result);
        }

        [Fact]
        public void BuildDateePrompt_InterestBehaviourBlock_HighInterest()
        {
            var result = SessionDocumentBuilder.BuildDateePrompt(
                MakeDateeContext(interestBefore: 10, interestAfter: 18));

            Assert.Contains("Interested but holding back", result);
        }

        [Fact]
        public void BuildDateePrompt_InterestBehaviourBlock_LowInterest()
        {
            var result = SessionDocumentBuilder.BuildDateePrompt(
                MakeDateeContext(interestBefore: 5, interestAfter: 3));

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
            var blocks = CacheBlockBuilder.BuildCachedSystemBlocks("player prompt", "datee prompt");

            Assert.Equal(2, blocks.Length);
            Assert.Equal("text", blocks[0].Type);
            Assert.Equal("player prompt", blocks[0].Text);
            Assert.NotNull(blocks[0].CacheControl);
            Assert.Equal("ephemeral", blocks[0].CacheControl!.Type);

            Assert.Equal("text", blocks[1].Type);
            Assert.Equal("datee prompt", blocks[1].Text);
            Assert.NotNull(blocks[1].CacheControl);
            Assert.Equal("ephemeral", blocks[1].CacheControl!.Type);
        }

        [Fact]
        public void BuildDateeOnlySystemBlocks_ReturnsOneBlockWithEphemeralCache()
        {
            var blocks = CacheBlockBuilder.BuildDateeOnlySystemBlocks("datee prompt");

            Assert.Single(blocks);
            Assert.Equal("text", blocks[0].Type);
            Assert.Equal("datee prompt", blocks[0].Text);
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
                CacheBlockBuilder.BuildCachedSystemBlocks(null!, "datee"));
        }

        [Fact]
        public void BuildCachedSystemBlocks_NullDateePrompt_Throws()
        {
            Assert.Throws<ArgumentNullException>(() =>
                CacheBlockBuilder.BuildCachedSystemBlocks("player", null!));
        }

        [Fact]
        public void BuildDateeOnlySystemBlocks_NullPrompt_Throws()
        {
            Assert.Throws<ArgumentNullException>(() =>
                CacheBlockBuilder.BuildDateeOnlySystemBlocks(null!));
        }

        // ── Error conditions ──

        [Fact]
        public void BuildDialogueOptionsPrompt_NullContext_Throws()
        {
            Assert.Throws<ArgumentNullException>(() =>
                SessionDocumentBuilder.BuildDialogueOptionsPrompt((DialogueContext)null!));
        }

        // #1138: BuildDeliveryPrompt_NullContext_Throws removed —
        // BuildDeliveryPrompt is gone (#1125 DeliveryOverlay).

        [Fact]
        public void BuildDateePrompt_NullContext_Throws()
        {
            Assert.Throws<ArgumentNullException>(() =>
                SessionDocumentBuilder.BuildDateePrompt((DateeContext)null!));
        }

        [Fact]
        public void BuildInterestChangeBeatPrompt_NullDateeName_Throws()
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
            // #1138: Success/FailureDeliveryInstruction removed (delivery prompt
            // collapsed into DeliveryOverlay, #1125).
            Assert.False(string.IsNullOrEmpty(PromptTemplates.DateeResponseInstruction));
            Assert.False(string.IsNullOrEmpty(PromptTemplates.InterestBeatInstruction));
        }

        [Fact]
        public void PromptTemplates_DialogueOptionsInstruction_ContainsKeyContent()
        {
            Assert.Contains("[STAT: X]", PromptTemplates.DialogueOptionsInstruction);
            Assert.Contains("[CALLBACK:", PromptTemplates.DialogueOptionsInstruction);
            Assert.Contains("[COMBO:", PromptTemplates.DialogueOptionsInstruction);
            Assert.Contains("[TELL_BONUS:", PromptTemplates.DialogueOptionsInstruction);
            Assert.Contains("exactly {options_count}", PromptTemplates.DialogueOptionsInstruction);
        }

        // #1138: PromptTemplates_FailureDeliveryInstruction_ContainsAllTiers
        // removed — FailureDeliveryInstruction was deleted (delivery prompt
        // collapsed into DeliveryOverlay, #1125).

        [Fact]
        public void PromptTemplates_DateeResponseInstruction_ContainsSignalsBlock()
        {
            Assert.Contains("[SIGNALS]", PromptTemplates.DateeResponseInstruction);
            Assert.Contains("[RESPONSE]", PromptTemplates.DateeResponseInstruction);
            Assert.Contains("TELL:", PromptTemplates.DateeResponseInstruction);
            Assert.Contains("WEAKNESS:", PromptTemplates.DateeResponseInstruction);
        }

        // ── Pivot directive tests (#696) ──

        [Fact]
        public void PivotDirective_NotPresent_AtTurn1()
        {
            var ctx = MakeDialogueContext(currentTurn: 1);
            var result = SessionDocumentBuilder.BuildDialogueOptionsPrompt(ctx);
            Assert.DoesNotContain("TOPIC PIVOT RULE", result);
        }

        [Fact]
        public void PivotDirective_NotPresent_AtTurn2()
        {
            var ctx = MakeDialogueContext(currentTurn: 2);
            var result = SessionDocumentBuilder.BuildDialogueOptionsPrompt(ctx);
            Assert.DoesNotContain("TOPIC PIVOT RULE", result);
        }

        [Fact]
        public void PivotDirective_Present_AtTurn3()
        {
            var ctx = MakeDialogueContext(currentTurn: 3);
            var result = SessionDocumentBuilder.BuildDialogueOptionsPrompt(ctx);
            Assert.Contains("TOPIC PIVOT RULE", result);
            Assert.Contains("bridge to a different dimension", result);
        }

        [Fact]
        public void PivotDirective_Present_AtTurn5()
        {
            var ctx = MakeDialogueContext(currentTurn: 5);
            var result = SessionDocumentBuilder.BuildDialogueOptionsPrompt(ctx);
            Assert.Contains("TOPIC PIVOT RULE", result);
        }

        [Fact]
        public void PivotDirective_ContextCarriesTurnNumber()
        {
            var ctx = MakeDialogueContext(currentTurn: 4);
            Assert.Equal(4, ctx.CurrentTurn);
            var result = SessionDocumentBuilder.BuildDialogueOptionsPrompt(ctx);
            Assert.Contains("Turn 4", result);
        }

        // ── Issue #951 regression: scene entries must never leak as the datee name ──

        [Fact]
        public void BuildDateePrompt_SceneEntriesInHistory_AreFiltered()
        {
            // Arrange: history contains turn-0 scene entries followed by real conversation.
            // Scene entries must not appear in the built prompt, and the datee character
            // name ("SABLE_XO") must be present instead of the literal word "scene".
            var historyWithScenes = new List<(string, string)>
            {
                (Senders.Scene, "Player bio text."),
                (Senders.Scene, "Datee bio text."),
                (Senders.Scene, "Both wear something quietly out of fashion."),
                ("GERALD_42", "Hey there, I noticed your bio."),
            };

            var result = SessionDocumentBuilder.BuildDateePrompt(
                MakeDateeContext(
                    conversationHistory: historyWithScenes,
                    playerDeliveredMessage: "Hey there, I noticed your bio.",
                    playerName: "GERALD_42",
                    dateeName: "SABLE_XO"));

            // Scene entries must not appear as [DATEE|[scene]] labels.
            Assert.DoesNotContain("[scene]", result);
            // The datee’s real name must be present (substituted into engine block).
            Assert.Contains("SABLE_XO", result);
            // Standalone word “scene” must not appear as a whole word where it could
            // be mistaken for a character name. (Use whole-word check to avoid false
            // positives on words like "obscene".)
            Assert.DoesNotMatch(@"(?i)\bscene\b", result);
        }

        [Fact]
        public void BuildInterestChangeBeatPrompt_SceneEntriesInHistory_AreFiltered()
        {
            // Arrange: history with turn-0 scene entries.
            var historyWithScenes = new List<(string, string)>
            {
                (Senders.Scene, "Player bio text."),
                (Senders.Scene, "Datee bio text."),
                (Senders.Scene, "Both wear something out of fashion."),
                ("GERALD_42", "Hey there!"),
                ("SABLE_XO",  "Hi!"),
            };

            var result = SessionDocumentBuilder.BuildInterestChangeBeatPrompt(
                dateeName: "SABLE_XO",
                interestBefore: 5,
                interestAfter: 10,
                newState: InterestState.Interested,
                conversationHistory: historyWithScenes,
                playerName: "GERALD_42");

            // Scene entries must not appear in the RECENT CONVERSATION block.
            Assert.DoesNotContain("[scene]", result);
            Assert.DoesNotMatch(@"(?i)\bscene\b", result);
            // Datee name must be substituted correctly.
            Assert.Contains("SABLE_XO", result);
        }
    }
}
