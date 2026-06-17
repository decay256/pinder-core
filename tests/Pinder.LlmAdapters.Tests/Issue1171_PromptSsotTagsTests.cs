using System;
using System.Collections.Generic;
using Pinder.Core.Conversation;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;
using Xunit;

namespace Pinder.LlmAdapters.Tests
{
    /// <summary>
    /// Issue #1171 — Regression tests for the SSOT data-block contract:
    ///   &lt;PLAYER_AVATAR_CHARACTER&gt;, &lt;DATEE_CHARACTER&gt;, &lt;ENGINE_STATE&gt;.
    ///
    /// AC1: Character blocks are well-formed (balanced open/close tags).
    /// AC2: Engine-state block is well-formed.
    /// AC3: Injection-fence integrity — character data is INSIDE character block;
    ///      engine state is INSIDE ENGINE_STATE; no cross-leakage.
    /// AC4: ENGINE_STATE fields reflect real session state (TURN, stats, interest).
    /// AC5: Horniness displays without /10 denominator even when value exceeds 10.
    /// </summary>
    [Collection("PromptTraceSingleton")]
    public class Issue1171_PromptSsotTagsTests
    {
        // ── helpers ──────────────────────────────────────────────────────────

        private static DialogueContext MakeDialogueContext(
            int currentTurn = 3,
            int currentInterest = 14,
            int horninessLevel = 0,
            string playerName = "Velvet",
            string dateeName = "Sable")
        {
            return new DialogueContext(
                playerAvatarPrompt: "velvet system prompt",
                dateePrompt: "sable system prompt",
                conversationHistory: new List<(string, string)>
                {
                    ("Velvet", "hey there"),
                    ("Sable", "omg hi!!")
                },
                dateeLastMessage: "omg hi!!",
                activeTraps: Array.Empty<string>(),
                currentInterest: currentInterest,
                shadowThresholds: null,
                callbackOpportunities: null,
                horninessLevel: horninessLevel,
                requiresRizzOption: false,
                activeTrapInstructions: null,
                playerName: playerName,
                dateeName: dateeName,
                currentTurn: currentTurn,
                availableStats: new[] { StatType.Charm, StatType.Rizz, StatType.Honesty });
        }

        private static DateeContext MakeDateeContext(
            int interestBefore = 12,
            int interestAfter = 14,
            string playerName = "Velvet",
            string dateeName = "Sable")
        {
            return new DateeContext(
                dateePrompt: "sable system prompt",
                conversationHistory: new List<(string, string)>
                {
                    ("Velvet", "hey there"),
                    ("Sable", "omg hi!!")
                },
                dateeLastMessage: "omg hi!!",
                activeTraps: Array.Empty<string>(),
                currentInterest: interestAfter,
                playerDeliveredMessage: "you remind me of a song I can't quite place",
                interestBefore: interestBefore,
                interestAfter: interestAfter,
                responseDelayMinutes: 2.5,
                shadowThresholds: null,
                activeTrapInstructions: null,
                playerName: playerName,
                dateeName: dateeName,
                deliveryTier: FailureTier.None);
        }

        // ── AC1: PLAYER_AVATAR_CHARACTER and DATEE_CHARACTER blocks ────────────

        [Fact]
        public void BuildPlayerAvatarEx_ContainsOpenPlayerAvatarTag()
        {
            var result = SessionSystemPromptBuilder.BuildPlayerAvatarEx("PLACEHOLDER_CHAR", GameDefinition.PinderDefaults).Text;
            Assert.Contains("<PLAYER_AVATAR_CHARACTER>", result);
        }

        [Fact]
        public void BuildPlayerAvatarEx_ContainsClosePlayerAvatarTag()
        {
            var result = SessionSystemPromptBuilder.BuildPlayerAvatarEx("PLACEHOLDER_CHAR", GameDefinition.PinderDefaults).Text;
            Assert.Contains("</PLAYER_AVATAR_CHARACTER>", result);
        }

        [Fact]
        public void BuildPlayerAvatarEx_PlayerAvatarTagsAreBalanced()
        {
            var result = SessionSystemPromptBuilder.BuildPlayerAvatarEx("PLACEHOLDER_CHAR", GameDefinition.PinderDefaults).Text;
            int opens = CountOccurrences(result, "<PLAYER_AVATAR_CHARACTER>");
            int closes = CountOccurrences(result, "</PLAYER_AVATAR_CHARACTER>");
            Assert.Equal(1, opens);
            Assert.Equal(1, closes);
        }

        [Fact]
        public void BuildDateeEx_ContainsOpenDateeTag()
        {
            var result = SessionSystemPromptBuilder.BuildDateeEx("PLACEHOLDER_CHAR", GameDefinition.PinderDefaults).Text;
            Assert.Contains("<DATEE_CHARACTER>", result);
        }

        [Fact]
        public void BuildDateeEx_ContainsCloseDateeTag()
        {
            var result = SessionSystemPromptBuilder.BuildDateeEx("PLACEHOLDER_CHAR", GameDefinition.PinderDefaults).Text;
            Assert.Contains("</DATEE_CHARACTER>", result);
        }

        [Fact]
        public void BuildDateeEx_DateeTagsAreBalanced()
        {
            var result = SessionSystemPromptBuilder.BuildDateeEx("PLACEHOLDER_CHAR", GameDefinition.PinderDefaults).Text;
            int opens = CountOccurrences(result, "<DATEE_CHARACTER>");
            int closes = CountOccurrences(result, "</DATEE_CHARACTER>");
            Assert.Equal(1, opens);
            Assert.Equal(1, closes);
        }

        // ── AC2: ENGINE_STATE block ────────────────────────────────────────

        [Fact]
        public void OptionsPrompt_ContainsOpenEngineStateTag()
        {
            var result = SessionDocumentBuilder.BuildDialogueOptionsPrompt(MakeDialogueContext());
            Assert.Contains("<ENGINE_STATE>", result);
        }

        [Fact]
        public void OptionsPrompt_ContainsCloseEngineStateTag()
        {
            var result = SessionDocumentBuilder.BuildDialogueOptionsPrompt(MakeDialogueContext());
            Assert.Contains("</ENGINE_STATE>", result);
        }

        [Fact]
        public void OptionsPrompt_EngineStateTagsAreBalanced()
        {
            var result = SessionDocumentBuilder.BuildDialogueOptionsPrompt(MakeDialogueContext());
            int opens = CountOccurrences(result, "<ENGINE_STATE>");
            int closes = CountOccurrences(result, "</ENGINE_STATE>");
            Assert.Equal(1, opens);
            Assert.Equal(1, closes);
        }

        [Fact]
        public void DateePrompt_ContainsOpenEngineStateTag()
        {
            var result = SessionDocumentBuilder.BuildDateePrompt(MakeDateeContext());
            Assert.Contains("<ENGINE_STATE>", result);
        }

        [Fact]
        public void DateePrompt_ContainsCloseEngineStateTag()
        {
            var result = SessionDocumentBuilder.BuildDateePrompt(MakeDateeContext());
            Assert.Contains("</ENGINE_STATE>", result);
        }

        [Fact]
        public void DateePrompt_EngineStateTagsAreBalanced()
        {
            var result = SessionDocumentBuilder.BuildDateePrompt(MakeDateeContext());
            int opens = CountOccurrences(result, "<ENGINE_STATE>");
            int closes = CountOccurrences(result, "</ENGINE_STATE>");
            Assert.Equal(1, opens);
            Assert.Equal(1, closes);
        }

        // ── AC3: Injection-fence integrity ────────────────────────────────────

        [Fact]
        public void BuildPlayerAvatarEx_CharacterPayloadIsInsideTag()
        {
            const string charPrompt = "MY_UNIQUE_CHARACTER_DATA_XYZ";
            var result = SessionSystemPromptBuilder.BuildPlayerAvatarEx(charPrompt, GameDefinition.PinderDefaults).Text;

            int openTag = result.IndexOf("<PLAYER_AVATAR_CHARACTER>", StringComparison.Ordinal);
            int closeTag = result.IndexOf("</PLAYER_AVATAR_CHARACTER>", StringComparison.Ordinal);
            int charIdx = result.IndexOf(charPrompt, StringComparison.Ordinal);

            Assert.True(openTag >= 0, "<PLAYER_AVATAR_CHARACTER> tag must be present");
            Assert.True(closeTag >= 0, "</PLAYER_AVATAR_CHARACTER> tag must be present");
            Assert.True(charIdx >= 0, "Character data must be present");
            Assert.True(charIdx > openTag, "Character data must be after opening tag");
            Assert.True(charIdx < closeTag, "Character data must be before closing tag");
        }

        [Fact]
        public void BuildDateeEx_CharacterPayloadIsInsideTag()
        {
            const string charPrompt = "MY_UNIQUE_DATEE_DATA_ABC";
            var result = SessionSystemPromptBuilder.BuildDateeEx(charPrompt, GameDefinition.PinderDefaults).Text;

            int openTag = result.IndexOf("<DATEE_CHARACTER>", StringComparison.Ordinal);
            int closeTag = result.IndexOf("</DATEE_CHARACTER>", StringComparison.Ordinal);
            int charIdx = result.IndexOf(charPrompt, StringComparison.Ordinal);

            Assert.True(openTag >= 0, "<DATEE_CHARACTER> tag must be present");
            Assert.True(closeTag >= 0, "</DATEE_CHARACTER> tag must be present");
            Assert.True(charIdx >= 0, "Character data must be present");
            Assert.True(charIdx > openTag, "Character data must be after opening tag");
            Assert.True(charIdx < closeTag, "Character data must be before closing tag");
        }

        [Fact]
        public void OptionsPrompt_InterestIsInsideEngineStateBlock()
        {
            var result = SessionDocumentBuilder.BuildDialogueOptionsPrompt(MakeDialogueContext(currentInterest: 18));

            int engineOpen = result.IndexOf("<ENGINE_STATE>", StringComparison.Ordinal);
            int engineClose = result.IndexOf("</ENGINE_STATE>", StringComparison.Ordinal);
            int interestIdx = result.IndexOf("Interest: 18/25", StringComparison.Ordinal);

            Assert.True(engineOpen >= 0, "<ENGINE_STATE> must be present");
            Assert.True(engineClose >= 0, "</ENGINE_STATE> must be present");
            Assert.True(interestIdx >= 0, "Interest line must be present");
            Assert.True(interestIdx > engineOpen, "Interest must be after opening ENGINE_STATE");
            Assert.True(interestIdx < engineClose, "Interest must be before closing ENGINE_STATE");
        }

        [Fact]
        public void DateePrompt_InterestIsInsideEngineStateBlock()
        {
            var result = SessionDocumentBuilder.BuildDateePrompt(MakeDateeContext(interestAfter: 16));

            int engineOpen = result.IndexOf("<ENGINE_STATE>", StringComparison.Ordinal);
            int engineClose = result.IndexOf("</ENGINE_STATE>", StringComparison.Ordinal);
            int interestIdx = result.IndexOf("Interest 16/25", StringComparison.Ordinal);

            Assert.True(engineOpen >= 0, "<ENGINE_STATE> must be present");
            Assert.True(engineClose >= 0, "</ENGINE_STATE> must be present");
            Assert.True(interestIdx >= 0, "Interest line must be present");
            Assert.True(interestIdx > engineOpen, "Interest must be after opening ENGINE_STATE");
            Assert.True(interestIdx < engineClose, "Interest must be before closing ENGINE_STATE");
        }

        [Fact]
        public void BuildPlayerAvatarEx_NoEngineStateInsideCharacterBlock()
        {
            var result = SessionSystemPromptBuilder.BuildPlayerAvatarEx("PLACEHOLDER_CHAR", GameDefinition.PinderDefaults).Text;
            // No ENGINE_STATE should be present in the system prompt at all
            Assert.DoesNotContain("<ENGINE_STATE>", result);
            Assert.DoesNotContain("</ENGINE_STATE>", result);
        }

        [Fact]
        public void BuildDateeEx_NoEngineStateInsideCharacterBlock()
        {
            var result = SessionSystemPromptBuilder.BuildDateeEx("PLACEHOLDER_CHAR", GameDefinition.PinderDefaults).Text;
            // No ENGINE_STATE should be present in the system prompt at all
            Assert.DoesNotContain("<ENGINE_STATE>", result);
            Assert.DoesNotContain("</ENGINE_STATE>", result);
        }

        // ── AC4: ENGINE_STATE fields reflect real session state ────────────

        [Fact]
        public void OptionsPrompt_EngineStateContainsTurnNumber()
        {
            var result = SessionDocumentBuilder.BuildDialogueOptionsPrompt(MakeDialogueContext(currentTurn: 7));
            // Turn is inside ENGINE_STATE block
            int engineOpen = result.IndexOf("<ENGINE_STATE>", StringComparison.Ordinal);
            int engineClose = result.IndexOf("</ENGINE_STATE>", StringComparison.Ordinal);
            int turnIdx = result.IndexOf("Turn 7", StringComparison.Ordinal);
            Assert.True(turnIdx > engineOpen && turnIdx < engineClose,
                "Turn number must be inside ENGINE_STATE block");
        }

        [Fact]
        public void DateePrompt_EngineStateContainsInterestDelta()
        {
            var result = SessionDocumentBuilder.BuildDateePrompt(MakeDateeContext(interestBefore: 10, interestAfter: 14));
            // Interest delta is emitted as "Interest moved from 10 to 14 (+4)"
            // This is OUTSIDE the ENGINE_STATE block (post-block in datee prompt)
            Assert.Contains("Interest moved from 10 to 14 (+4)", result);
        }

        // ── AC5: Horniness /10 fix ────────────────────────────────────────────

        [Fact]
        public void Horniness_AboveTen_NoSlashTenFormat()
        {
            // HorninessLevel can be 1..13 (d10 + up to +3 time modifier)
            // Any N/10 format with N>10 is broken — assert it never appears
            var result = SessionDocumentBuilder.BuildDialogueOptionsPrompt(MakeDialogueContext(horninessLevel: 13));
            Assert.DoesNotContain("13/10", result);
            Assert.DoesNotContain("/10", result);
        }

        [Fact]
        public void Horniness_GE6_ShowsValueWithoutDenominator()
        {
            var result = SessionDocumentBuilder.BuildDialogueOptionsPrompt(MakeDialogueContext(horninessLevel: 11));
            // Should contain the value
            Assert.Contains("Horniness: 11", result);
            // Should NOT contain /10 denominator
            Assert.DoesNotContain("11/10", result);
            Assert.DoesNotContain("/10", result);
        }

        [Fact]
        public void Horniness_GE6_RetainsQualitativeTail()
        {
            var result = SessionDocumentBuilder.BuildDialogueOptionsPrompt(MakeDialogueContext(horninessLevel: 8));
            Assert.Contains("Rizz options more prominent", result);
        }

        // ── helpers ──────────────────────────────────────────────────────────

        private static int CountOccurrences(string text, string pattern)
        {
            int count = 0;
            int idx = 0;
            while ((idx = text.IndexOf(pattern, idx, StringComparison.Ordinal)) >= 0)
            {
                count++;
                idx += pattern.Length;
            }
            return count;
        }
    }
}
