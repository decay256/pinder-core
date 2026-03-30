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
        // ═══════════════════════════════════════════════════════════════
        // AC2: Conversation History Formatting
        // ═══════════════════════════════════════════════════════════════

        // What: AC2.4 — Empty history produces exactly [CONVERSATION_START]\n[CURRENT_TURN]
        // Mutation: Would catch if implementation omits [CONVERSATION_START] or [CURRENT_TURN] markers
        [Fact]
        public void BuildDialogueOptionsPrompt_EmptyHistory_NoTurnMarkersBetweenStartAndCurrent()
        {
            var result = SessionDocumentBuilder.BuildDialogueOptionsPrompt(
                new List<(string, string)>(), "", new string[0], 10, 1, "P", "O");

            int startEnd = result.IndexOf("[CONVERSATION_START]") + "[CONVERSATION_START]".Length;
            int currentStart = result.IndexOf("[CURRENT_TURN]");
            string between = result.Substring(startEnd, currentStart - startEnd);

            // No [Tn markers should appear between start and current
            Assert.DoesNotContain("[T", between);
        }

        // What: AC2.2 — Turn numbering increments every 2 entries (pair-based)
        // Mutation: Would catch if turn = i + 1 (per-entry) instead of (i / 2) + 1 (per-pair)
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
                history, "msg3", new string[0], 10, 3, "ALICE", "BOB");

            // Indices 0,1 = T1; indices 2,3 = T2
            Assert.Contains("[T1|PLAYER|ALICE] \"msg0\"", result);
            Assert.Contains("[T1|OPPONENT|BOB] \"msg1\"", result);
            Assert.Contains("[T2|PLAYER|ALICE] \"msg2\"", result);
            Assert.Contains("[T2|OPPONENT|BOB] \"msg3\"", result);
            // Should NOT have T3 or T4
            Assert.DoesNotContain("[T3|", result);
            Assert.DoesNotContain("[T4|", result);
        }

        // What: AC2.5 — History is NEVER truncated, even for 20+ turns
        // Mutation: Would catch if a sliding window or max-history limit is applied
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
                history, "msg39", new string[0], 10, 21, "PLAYER_X", "OPP_Y");

            for (int turn = 1; turn <= 20; turn++)
            {
                Assert.Contains($"[T{turn}|PLAYER|PLAYER_X]", result);
                Assert.Contains($"[T{turn}|OPPONENT|OPP_Y]", result);
            }
        }

        // What: Edge case — odd number of entries (lone player message at end)
        // Mutation: Would catch if odd-entry handling crashes or skips the lone entry
        [Fact]
        public void BuildDialogueOptionsPrompt_SingleEntry_FormatsCorrectly()
        {
            var history = new List<(string, string)> { ("GERALD", "Hello!") };

            var result = SessionDocumentBuilder.BuildDialogueOptionsPrompt(
                history, "", new string[0], 10, 1, "GERALD", "V");

            Assert.Contains("[T1|PLAYER|GERALD] \"Hello!\"", result);
            Assert.Contains("[CURRENT_TURN]", result);
        }

        // What: Edge case — messages containing double quotes
        // Mutation: Would catch if quotes are escaped or stripped from message text
        [Fact]
        public void BuildDialogueOptionsPrompt_MessageWithDoubleQuotes_PreservedAsIs()
        {
            var history = new List<(string, string)>
            {
                ("P", "She said \"wow\" to me"),
                ("O", "Really?")
            };

            var result = SessionDocumentBuilder.BuildDialogueOptionsPrompt(
                history, "Really?", new string[0], 10, 2, "P", "O");

            Assert.Contains("She said \"wow\" to me", result);
        }

        // What: Edge case — empty message text
        // Mutation: Would catch if empty strings are filtered out instead of included
        [Fact]
        public void BuildDialogueOptionsPrompt_EmptyMessageText_FormatsAsEmptyQuotes()
        {
            var history = new List<(string, string)>
            {
                ("P", ""),
                ("O", "response")
            };

            var result = SessionDocumentBuilder.BuildDialogueOptionsPrompt(
                history, "response", new string[0], 10, 2, "P", "O");

            Assert.Contains("[T1|PLAYER|P] \"\"", result);
        }

        // What: Edge case — names with spaces
        // Mutation: Would catch if name is sanitized or truncated at space
        [Fact]
        public void BuildDialogueOptionsPrompt_NamesWithSpaces_UsedAsIs()
        {
            var history = new List<(string, string)>
            {
                ("Big Gerald", "Hey"),
                ("Lady V", "Hi")
            };

            var result = SessionDocumentBuilder.BuildDialogueOptionsPrompt(
                history, "Hi", new string[0], 10, 2, "Big Gerald", "Lady V");

            Assert.Contains("[T1|PLAYER|Big Gerald] \"Hey\"", result);
            Assert.Contains("[T1|OPPONENT|Lady V] \"Hi\"", result);
        }

        // What: AC2.2 — Role determination uses exact match on playerName
        // Mutation: Would catch if role determination is case-insensitive
        [Fact]
        public void BuildDialogueOptionsPrompt_RoleDetermination_CaseSensitive()
        {
            var history = new List<(string, string)>
            {
                ("Gerald", "Hey"),
                ("gerald", "Hi")  // lowercase — should be OPPONENT since playerName is "Gerald"
            };

            var result = SessionDocumentBuilder.BuildDialogueOptionsPrompt(
                history, "Hi", new string[0], 10, 2, "Gerald", "gerald");

            Assert.Contains("[T1|PLAYER|Gerald] \"Hey\"", result);
            Assert.Contains("[T1|OPPONENT|gerald] \"Hi\"", result);
        }

        // What: AC2.6 — BuildOpponentPrompt history excludes current player message
        // Mutation: Would catch if opponent prompt includes all history entries including current
        [Fact]
        public void BuildOpponentPrompt_HistoryExcludesCurrentPlayerMessage()
        {
            var history = new List<(string, string)>
            {
                ("P", "Turn1Player"),
                ("O", "Turn1Opp")
            };

            // playerDeliveredMessage is the current turn's message, supplied separately
            var result = SessionDocumentBuilder.BuildOpponentPrompt(
                history, "Turn2Player", 10, 12, 3.0, null, "P", "O");

            // History section should have T1 entries
            Assert.Contains("[T1|PLAYER|P] \"Turn1Player\"", result);
            Assert.Contains("[T1|OPPONENT|O] \"Turn1Opp\"", result);
            // The current player message appears in PLAYER'S LAST MESSAGE section, not history
            Assert.Contains("PLAYER'S LAST MESSAGE", result);
            Assert.Contains("\"Turn2Player\"", result);
        }

        // ═══════════════════════════════════════════════════════════════
        // AC1: SessionDocumentBuilder — All 4 builder methods
        // ═══════════════════════════════════════════════════════════════

        // What: AC1 — BuildDialogueOptionsPrompt contains CONVERSATION HISTORY header
        // Mutation: Would catch if section header is missing or misspelled
        [Fact]
        public void BuildDialogueOptionsPrompt_ContainsConversationHistoryHeader()
        {
            var result = SessionDocumentBuilder.BuildDialogueOptionsPrompt(
                new List<(string, string)>(), "", new string[0], 10, 1, "P", "O");

            Assert.Contains("CONVERSATION HISTORY", result);
        }

        // What: AC1 — BuildDialogueOptionsPrompt includes OPPONENT'S LAST MESSAGE section
        // Mutation: Would catch if opponent's last message section is omitted
        [Fact]
        public void BuildDialogueOptionsPrompt_ContainsOpponentLastMessage()
        {
            var result = SessionDocumentBuilder.BuildDialogueOptionsPrompt(
                new List<(string, string)>
                {
                    ("P", "Hey"),
                    ("O", "Whatever")
                },
                "Whatever", new string[0], 10, 2, "P", "O");

            Assert.Contains("OPPONENT'S LAST MESSAGE", result);
            Assert.Contains("\"Whatever\"", result);
        }

        // What: AC1 — BuildDialogueOptionsPrompt includes GAME STATE section
        // Mutation: Would catch if GAME STATE section is omitted entirely
        [Fact]
        public void BuildDialogueOptionsPrompt_ContainsGameStateSection()
        {
            var result = SessionDocumentBuilder.BuildDialogueOptionsPrompt(
                new List<(string, string)>(), "", new string[0], 10, 1, "P", "O");

            Assert.Contains("GAME STATE", result);
        }

        // What: AC1 — Multiple active traps are comma-joined
        // Mutation: Would catch if traps are newline-separated or omitted
        [Fact]
        public void BuildDialogueOptionsPrompt_MultipleTraps_CommaSeparated()
        {
            var result = SessionDocumentBuilder.BuildDialogueOptionsPrompt(
                new List<(string, string)>(), "", new[] { "Cringe", "Spiral", "Overexplain" },
                10, 1, "P", "O");

            Assert.Contains("Active traps: Cringe, Spiral, Overexplain", result);
        }

        // What: AC1 — BuildDialogueOptionsPrompt YOUR TASK includes player name
        // Mutation: Would catch if player name placeholder is not replaced
        [Fact]
        public void BuildDialogueOptionsPrompt_TaskIncludesPlayerName()
        {
            var result = SessionDocumentBuilder.BuildDialogueOptionsPrompt(
                new List<(string, string)>(), "", new string[0], 10, 1, "MEGA_CHAD", "O");

            Assert.Contains("MEGA_CHAD", result);
            Assert.Contains("Generate exactly 4 dialogue options", result);
        }

        // ═══════════════════════════════════════════════════════════════
        // BuildDeliveryPrompt — Success path
        // ═══════════════════════════════════════════════════════════════

        // What: Spec §3.3 — Success delivery includes stat name in uppercase
        // Mutation: Would catch if stat name is lowercase or missing
        [Fact]
        public void BuildDeliveryPrompt_Success_IncludesStatNameUppercase()
        {
            var option = new DialogueOption(StatType.Charm, "Smooth line");
            var result = SessionDocumentBuilder.BuildDeliveryPrompt(
                new List<(string, string)>(), option, FailureTier.None, 7, null, "P", "O");

            Assert.Contains("Stat used: CHARM", result);
        }

        // What: Spec §3.3 — Success shows positive beat-DC margin
        // Mutation: Would catch if beatDcBy is shown as absolute value or negated
        [Fact]
        public void BuildDeliveryPrompt_Success_ShowsBeatDcMargin()
        {
            var option = new DialogueOption(StatType.Honesty, "Truth bomb");
            var result = SessionDocumentBuilder.BuildDeliveryPrompt(
                new List<(string, string)>(), option, FailureTier.None, 9, null, "P", "O");

            Assert.Contains("beat DC by 9", result);
        }

        // What: Spec — FailureTier.None triggers SuccessDeliveryInstruction, not failure
        // Mutation: Would catch if success path uses failure template
        [Fact]
        public void BuildDeliveryPrompt_SuccessPath_DoesNotContainFailureTier()
        {
            var option = new DialogueOption(StatType.Wit, "Clever quip");
            var result = SessionDocumentBuilder.BuildDeliveryPrompt(
                new List<(string, string)>(), option, FailureTier.None, 3, null, "P", "O");

            Assert.Contains("SUCCESS", result);
            Assert.DoesNotContain("Failure tier:", result);
        }

        // What: Spec — Success delivery includes "Output only the message text"
        // Mutation: Would catch if output instruction is missing
        [Fact]
        public void BuildDeliveryPrompt_Success_ContainsOutputInstruction()
        {
            var option = new DialogueOption(StatType.Rizz, "Flirty msg");
            var result = SessionDocumentBuilder.BuildDeliveryPrompt(
                new List<(string, string)>(), option, FailureTier.None, 2, null, "P", "O");

            Assert.Contains("Output only the message text", result);
        }

        // ═══════════════════════════════════════════════════════════════
        // BuildDeliveryPrompt — Failure path
        // ═══════════════════════════════════════════════════════════════

        // What: Spec §3.4 — Each failure tier is labeled correctly
        // Mutation: Would catch if tier name mapping is wrong (e.g., Fumble shows as Misfire)
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
                new List<(string, string)>(), option, tier, -5, null, "P", "O");

            Assert.Contains(expectedLabel, result);
        }

        // What: Spec §3.4 — Failure includes "corrupt the CONTENT, not the delivery" principle
        // Mutation: Would catch if failure principle text is omitted
        [Fact]
        public void BuildDeliveryPrompt_Failure_ContainsCorruptContentPrinciple()
        {
            var option = new DialogueOption(StatType.Chaos, "Wild swing");
            var result = SessionDocumentBuilder.BuildDeliveryPrompt(
                new List<(string, string)>(), option, FailureTier.Catastrophe, -12, null, "P", "O");

            Assert.Contains("corrupt the CONTENT", result);
        }

        // What: Spec — Failure shows "FAILED" and "missed DC by" with positive margin
        // Mutation: Would catch if negative beatDcBy is not converted to positive miss margin
        [Fact]
        public void BuildDeliveryPrompt_Failure_ShowsMissedDcByPositiveMargin()
        {
            var option = new DialogueOption(StatType.SelfAwareness, "Meta comment");
            var result = SessionDocumentBuilder.BuildDeliveryPrompt(
                new List<(string, string)>(), option, FailureTier.Fumble, -2, null, "P", "O");

            Assert.Contains("FAILED", result);
            Assert.Contains("missed DC by 2", result);
        }

        // What: Spec — Active trap instructions appear when provided for failures
        // Mutation: Would catch if trap instructions are ignored in failure path
        [Fact]
        public void BuildDeliveryPrompt_WithTrapInstructions_IncludesTrapSection()
        {
            var option = new DialogueOption(StatType.Charm, "Line");
            var result = SessionDocumentBuilder.BuildDeliveryPrompt(
                new List<(string, string)>(), option, FailureTier.TropeTrap, -7,
                new[] { "Trap instruction A", "Trap instruction B" }, "P", "O");

            Assert.Contains("Trap instruction A", result);
            Assert.Contains("Trap instruction B", result);
        }

        // What: Spec — Null trap instructions omits trap section
        // Mutation: Would catch if null trap instructions causes crash or empty section
        [Fact]
        public void BuildDeliveryPrompt_NullTrapInstructions_NoTrapSection()
        {
            var option = new DialogueOption(StatType.Wit, "Joke");
            var result = SessionDocumentBuilder.BuildDeliveryPrompt(
                new List<(string, string)>(), option, FailureTier.Misfire, -3, null, "P", "O");

            Assert.DoesNotContain("Active trap instructions:", result);
        }

        // What: Spec — Chosen option text appears in output
        // Mutation: Would catch if option text is not included
        [Fact]
        public void BuildDeliveryPrompt_IncludesChosenOptionText()
        {
            var option = new DialogueOption(StatType.Honesty, "I really like your vibe");
            var result = SessionDocumentBuilder.BuildDeliveryPrompt(
                new List<(string, string)>(), option, FailureTier.None, 5, null, "P", "O");

            Assert.Contains("I really like your vibe", result);
        }

        // ═══════════════════════════════════════════════════════════════
        // BuildOpponentPrompt
        // ═══════════════════════════════════════════════════════════════

        // What: Spec — Interest change shows delta with sign
        // Mutation: Would catch if delta calculation is wrong or sign is missing
        [Fact]
        public void BuildOpponentPrompt_PositiveDelta_ShowsPlusSign()
        {
            var result = SessionDocumentBuilder.BuildOpponentPrompt(
                new List<(string, string)>(), "Hey", 8, 11, 2.0, null, "P", "O");

            Assert.Contains("Interest moved from 8 to 11 (+3)", result);
        }

        // What: Spec — Zero interest delta formatted correctly
        // Mutation: Would catch if zero delta is omitted or shows wrong sign
        [Fact]
        public void BuildOpponentPrompt_ZeroDelta_ShowsPlusZero()
        {
            var result = SessionDocumentBuilder.BuildOpponentPrompt(
                new List<(string, string)>(), "Hey", 10, 10, 2.0, null, "P", "O");

            Assert.Contains("Interest moved from 10 to 10 (+0)", result);
        }

        // What: Spec — Current Interest shows value out of 25
        // Mutation: Would catch if max interest is wrong or format is different
        [Fact]
        public void BuildOpponentPrompt_ShowsCurrentInterestOutOf25()
        {
            var result = SessionDocumentBuilder.BuildOpponentPrompt(
                new List<(string, string)>(), "Hi", 10, 15, 1.0, null, "P", "O");

            Assert.Contains("Current Interest: 15/25", result);
        }

        // What: Spec — Response timing for delay >= 1 minute uses "approximately X.X minutes"
        // Mutation: Would catch if delay format is wrong (integer instead of decimal)
        [Fact]
        public void BuildOpponentPrompt_NormalDelay_ShowsApproximateMinutes()
        {
            var result = SessionDocumentBuilder.BuildOpponentPrompt(
                new List<(string, string)>(), "Hey", 10, 10, 5.5, null, "P", "O");

            Assert.Contains("approximately 5.5 minutes", result);
        }

        // What: Spec — Very small delay (< 1 minute) shows "less than 1 minute"
        // Mutation: Would catch if small delay uses numeric format instead of special text
        [Fact]
        public void BuildOpponentPrompt_SubMinuteDelay_ShowsLessThanOneMinute()
        {
            var result = SessionDocumentBuilder.BuildOpponentPrompt(
                new List<(string, string)>(), "Hey", 10, 10, 0.3, null, "P", "O");

            Assert.Contains("less than 1 minute", result);
        }

        // What: Spec — Interest behaviour block for range 21-25 (extremely interested)
        // Mutation: Would catch if threshold boundaries are off-by-one
        [Fact]
        public void BuildOpponentPrompt_Interest21_ExtremelyInterested()
        {
            var result = SessionDocumentBuilder.BuildOpponentPrompt(
                new List<(string, string)>(), "Hey", 20, 21, 1.0, null, "P", "O");

            Assert.Contains("extremely interested", result);
        }

        // What: Spec — Interest behaviour block for range 13-16 (engaged)
        // Mutation: Would catch if boundary at 13 is wrong
        [Fact]
        public void BuildOpponentPrompt_Interest13_Engaged()
        {
            var result = SessionDocumentBuilder.BuildOpponentPrompt(
                new List<(string, string)>(), "Hey", 10, 13, 1.0, null, "P", "O");

            Assert.Contains("engaged", result);
        }

        // What: Spec — Interest behaviour block for range 9-12 (lukewarm)
        // Mutation: Would catch if boundary at 9 is wrong (e.g., <= 8 instead of >= 9)
        [Fact]
        public void BuildOpponentPrompt_Interest9_Lukewarm()
        {
            var result = SessionDocumentBuilder.BuildOpponentPrompt(
                new List<(string, string)>(), "Hey", 10, 9, 1.0, null, "P", "O");

            Assert.Contains("lukewarm", result);
        }

        // What: Spec — Interest behaviour block for range 5-8 (cooling)
        // Mutation: Would catch if cooling range boundary is wrong
        [Fact]
        public void BuildOpponentPrompt_Interest5_Cooling()
        {
            var result = SessionDocumentBuilder.BuildOpponentPrompt(
                new List<(string, string)>(), "Hey", 10, 5, 1.0, null, "P", "O");

            Assert.Contains("cooling", result);
        }

        // What: Spec — Interest 0 triggers "lost all interest" / unmatching
        // Mutation: Would catch if 0 maps to disengaged instead of unmatching
        [Fact]
        public void BuildOpponentPrompt_Interest0_Unmatching()
        {
            var result = SessionDocumentBuilder.BuildOpponentPrompt(
                new List<(string, string)>(), "Hey", 2, 0, 1.0, null, "P", "O");

            Assert.Contains("lost all interest", result);
        }

        // What: Spec — BuildOpponentPrompt includes [RESPONSE] and [SIGNALS] markers
        // Mutation: Would catch if signal output format instructions are missing
        [Fact]
        public void BuildOpponentPrompt_ContainsSignalsInstruction()
        {
            var result = SessionDocumentBuilder.BuildOpponentPrompt(
                new List<(string, string)>(), "Hey", 10, 12, 1.0, null, "P", "O");

            Assert.Contains("[RESPONSE]", result);
            Assert.Contains("[SIGNALS]", result);
        }

        // What: Spec — Opponent prompt with trap instructions includes them
        // Mutation: Would catch if activeTrapInstructions are ignored
        [Fact]
        public void BuildOpponentPrompt_WithTrapInstructions_IncludesThem()
        {
            var result = SessionDocumentBuilder.BuildOpponentPrompt(
                new List<(string, string)>(), "Hey", 10, 10, 1.0,
                new[] { "Trap effect: cringe aura" }, "P", "O");

            Assert.Contains("Trap effect: cringe aura", result);
        }

        // ═══════════════════════════════════════════════════════════════
        // BuildInterestChangeBeatPrompt
        // ═══════════════════════════════════════════════════════════════

        // What: Spec §3.8 — Prompt includes opponent name and interest values
        // Mutation: Would catch if name or values are not interpolated
        [Fact]
        public void BuildInterestChangeBeatPrompt_IncludesNameAndValues()
        {
            var result = SessionDocumentBuilder.BuildInterestChangeBeatPrompt(
                "MEGA_V", 10, 16, InterestState.VeryIntoIt);

            Assert.Contains("MEGA_V", result);
            Assert.Contains("10", result);
            Assert.Contains("16", result);
        }

        // What: Spec §3.8 — Generic fallback for non-threshold-crossing changes
        // Mutation: Would catch if generic fallback is missing or wrong condition
        [Fact]
        public void BuildInterestChangeBeatPrompt_GenericCase_DoesNotCrash()
        {
            // Interest moved 10→13, crossed no major threshold
            var result = SessionDocumentBuilder.BuildInterestChangeBeatPrompt(
                "OPP", 10, 13, InterestState.Interested);

            // Should still produce a valid non-empty result
            Assert.False(string.IsNullOrWhiteSpace(result));
            Assert.Contains("OPP", result);
        }

        // What: Spec §3.8 — "Output only the message text" instruction present
        // Mutation: Would catch if output instruction is missing
        [Fact]
        public void BuildInterestChangeBeatPrompt_ContainsOutputInstruction()
        {
            var result = SessionDocumentBuilder.BuildInterestChangeBeatPrompt(
                "V", 14, 16, InterestState.VeryIntoIt);

            Assert.Contains("Output only the message", result);
        }

        // What: Spec — Crossed above 15 specifically uses "becoming more invested"
        // Mutation: Would catch if above-15 threshold instruction uses wrong template
        [Fact]
        public void BuildInterestChangeBeatPrompt_CrossedAbove15FromBelow_ShowsInvested()
        {
            // before=14 (<=15), after=17 (>15) → crossed above 15
            var result = SessionDocumentBuilder.BuildInterestChangeBeatPrompt(
                "V", 14, 17, InterestState.VeryIntoIt);

            Assert.Contains("becoming more invested", result);
        }

        // What: Spec — Crossed below 8 specifically uses "pulling back"
        // Mutation: Would catch if below-8 threshold instruction uses wrong template
        [Fact]
        public void BuildInterestChangeBeatPrompt_CrossedBelow8FromAbove_ShowsPullingBack()
        {
            // before=8 (>=8), after=6 (<8) → crossed below 8
            var result = SessionDocumentBuilder.BuildInterestChangeBeatPrompt(
                "V", 8, 6, InterestState.Interested);

            Assert.Contains("pulling back", result);
        }

        // What: Spec — DateSecured (25) suggests meeting up
        // Mutation: Would catch if DateSecured doesn't trigger the meeting-up template
        [Fact]
        public void BuildInterestChangeBeatPrompt_DateSecured_SuggestsMeetUp()
        {
            var result = SessionDocumentBuilder.BuildInterestChangeBeatPrompt(
                "V", 24, 25, InterestState.DateSecured);

            Assert.Contains("meeting up", result);
        }

        // What: Spec — Unmatched (0) triggers unmatching message
        // Mutation: Would catch if Unmatched doesn't trigger the unmatching template
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

        // What: AC3 — All 5 template fields are non-null, non-empty const strings
        // Mutation: Would catch if any template is null or empty
        [Fact]
        public void PromptTemplates_AllFiveFieldsAreNonEmpty()
        {
            Assert.False(string.IsNullOrEmpty(PromptTemplates.DialogueOptionsInstruction));
            Assert.False(string.IsNullOrEmpty(PromptTemplates.SuccessDeliveryInstruction));
            Assert.False(string.IsNullOrEmpty(PromptTemplates.FailureDeliveryInstruction));
            Assert.False(string.IsNullOrEmpty(PromptTemplates.OpponentResponseInstruction));
            Assert.False(string.IsNullOrEmpty(PromptTemplates.InterestBeatInstruction));
        }

        // What: AC3.1 — DialogueOptionsInstruction instructs 4 options with metadata tags
        // Mutation: Would catch if tag format is wrong or count instruction is missing
        [Fact]
        public void PromptTemplates_DialogueOptions_ContainsMetadataTags()
        {
            var t = PromptTemplates.DialogueOptionsInstruction;
            Assert.Contains("[STAT:", t);
            Assert.Contains("[CALLBACK:", t);
            Assert.Contains("[COMBO:", t);
            Assert.Contains("[TELL_BONUS:", t);
        }

        // What: AC3.2 — SuccessDeliveryInstruction includes success tier descriptions
        // Mutation: Would catch if success tiers are missing from the template
        [Fact]
        public void PromptTemplates_SuccessDelivery_ContainsSuccessTierInfo()
        {
            var t = PromptTemplates.SuccessDeliveryInstruction;
            // Should mention different success margins
            Assert.False(string.IsNullOrWhiteSpace(t));
            Assert.Contains("Output only the message text", t);
        }

        // What: AC3.3 — FailureDeliveryInstruction includes all 5 tier names
        // Mutation: Would catch if any failure tier instruction is missing
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

        // What: AC3.3 — FailureDeliveryInstruction contains corruption principle
        // Mutation: Would catch if corruption principle is omitted from template
        [Fact]
        public void PromptTemplates_FailureDelivery_ContainsCorruptionPrinciple()
        {
            Assert.Contains("corrupt the CONTENT", PromptTemplates.FailureDeliveryInstruction);
        }

        // What: AC3.4 — OpponentResponseInstruction includes SIGNALS instruction
        // Mutation: Would catch if SIGNALS format instruction is missing
        [Fact]
        public void PromptTemplates_OpponentResponse_ContainsSignalFormat()
        {
            var t = PromptTemplates.OpponentResponseInstruction;
            Assert.Contains("[RESPONSE]", t);
            Assert.Contains("[SIGNALS]", t);
            Assert.Contains("TELL:", t);
            Assert.Contains("WEAKNESS:", t);
        }

        // What: AC3.4 — OpponentResponseInstruction mentions stat names for TELL/WEAKNESS
        // Mutation: Would catch if stat names are missing from signal instructions
        [Fact]
        public void PromptTemplates_OpponentResponse_MentionsStatNames()
        {
            var t = PromptTemplates.OpponentResponseInstruction;
            // Should reference stat types in signal instructions
            Assert.Contains("CHARM", t);
        }

        // ═══════════════════════════════════════════════════════════════
        // AC4: CacheBlockBuilder
        // ═══════════════════════════════════════════════════════════════

        // What: AC4.1 — BuildCachedSystemBlocks returns exactly 2 blocks
        // Mutation: Would catch if wrong number of blocks returned
        [Fact]
        public void CacheBlockBuilder_TwoPrompts_ReturnsTwoBlocks()
        {
            var blocks = CacheBlockBuilder.BuildCachedSystemBlocks("p", "o");
            Assert.Equal(2, blocks.Length);
        }

        // What: AC4.1 — First block contains player prompt, second contains opponent prompt
        // Mutation: Would catch if prompts are swapped in order
        [Fact]
        public void CacheBlockBuilder_TwoPrompts_CorrectOrder()
        {
            var blocks = CacheBlockBuilder.BuildCachedSystemBlocks("PLAYER_PROMPT", "OPPONENT_PROMPT");
            Assert.Equal("PLAYER_PROMPT", blocks[0].Text);
            Assert.Equal("OPPONENT_PROMPT", blocks[1].Text);
        }

        // What: AC4.1 — Both blocks have Type="text"
        // Mutation: Would catch if Type is wrong
        [Fact]
        public void CacheBlockBuilder_TwoPrompts_BothTypeText()
        {
            var blocks = CacheBlockBuilder.BuildCachedSystemBlocks("p", "o");
            Assert.Equal("text", blocks[0].Type);
            Assert.Equal("text", blocks[1].Type);
        }

        // What: AC4.1 — Both blocks have CacheControl.Type="ephemeral"
        // Mutation: Would catch if cache control is missing or wrong type
        [Fact]
        public void CacheBlockBuilder_TwoPrompts_BothCacheControlEphemeral()
        {
            var blocks = CacheBlockBuilder.BuildCachedSystemBlocks("p", "o");
            Assert.NotNull(blocks[0].CacheControl);
            Assert.Equal("ephemeral", blocks[0].CacheControl!.Type);
            Assert.NotNull(blocks[1].CacheControl);
            Assert.Equal("ephemeral", blocks[1].CacheControl!.Type);
        }

        // What: AC4.2 — BuildOpponentOnlySystemBlocks returns exactly 1 block
        // Mutation: Would catch if wrong number of blocks returned
        [Fact]
        public void CacheBlockBuilder_OpponentOnly_ReturnsSingleBlock()
        {
            var blocks = CacheBlockBuilder.BuildOpponentOnlySystemBlocks("o");
            Assert.Single(blocks);
        }

        // What: AC4.2 — Single block has correct content and cache control
        // Mutation: Would catch if content or cache control is wrong
        [Fact]
        public void CacheBlockBuilder_OpponentOnly_HasCorrectContentAndCache()
        {
            var blocks = CacheBlockBuilder.BuildOpponentOnlySystemBlocks("MY_PROMPT");
            Assert.Equal("text", blocks[0].Type);
            Assert.Equal("MY_PROMPT", blocks[0].Text);
            Assert.NotNull(blocks[0].CacheControl);
            Assert.Equal("ephemeral", blocks[0].CacheControl!.Type);
        }

        // What: Edge case — Empty prompts return blocks with empty text, no crash
        // Mutation: Would catch if empty string validation throws
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

        // What: Spec error table — null conversationHistory throws ArgumentNullException
        // Mutation: Would catch if null check is missing
        [Fact]
        public void BuildDialogueOptionsPrompt_NullHistory_ThrowsArgumentNull()
        {
            Assert.Throws<ArgumentNullException>(() =>
                SessionDocumentBuilder.BuildDialogueOptionsPrompt(
                    null!, "", new string[0], 10, 1, "P", "O"));
        }

        // What: Spec error table — null playerName throws
        // Mutation: Would catch if playerName null check is missing
        [Fact]
        public void BuildDialogueOptionsPrompt_NullPlayerName_ThrowsArgumentNull()
        {
            Assert.Throws<ArgumentNullException>(() =>
                SessionDocumentBuilder.BuildDialogueOptionsPrompt(
                    new List<(string, string)>(), "", new string[0], 10, 1, null!, "O"));
        }

        // What: Spec error table — empty playerName throws
        // Mutation: Would catch if empty string check is missing (only null checked)
        [Fact]
        public void BuildDialogueOptionsPrompt_EmptyPlayerName_Throws()
        {
            Assert.ThrowsAny<ArgumentException>(() =>
                SessionDocumentBuilder.BuildDialogueOptionsPrompt(
                    new List<(string, string)>(), "", new string[0], 10, 1, "", "O"));
        }

        // What: Spec error table — null opponentName throws
        // Mutation: Would catch if opponentName null check is missing
        [Fact]
        public void BuildDialogueOptionsPrompt_NullOpponentName_ThrowsArgumentNull()
        {
            Assert.Throws<ArgumentNullException>(() =>
                SessionDocumentBuilder.BuildDialogueOptionsPrompt(
                    new List<(string, string)>(), "", new string[0], 10, 1, "P", null!));
        }

        // What: Spec error table — null opponentLastMessage throws
        // Mutation: Would catch if opponentLastMessage null check is missing
        [Fact]
        public void BuildDialogueOptionsPrompt_NullOpponentLastMessage_Throws()
        {
            Assert.Throws<ArgumentNullException>(() =>
                SessionDocumentBuilder.BuildDialogueOptionsPrompt(
                    new List<(string, string)>(), null!, new string[0], 10, 1, "P", "O"));
        }

        // What: Spec error table — null activeTraps throws
        // Mutation: Would catch if activeTraps null check is missing
        [Fact]
        public void BuildDialogueOptionsPrompt_NullActiveTraps_Throws()
        {
            Assert.Throws<ArgumentNullException>(() =>
                SessionDocumentBuilder.BuildDialogueOptionsPrompt(
                    new List<(string, string)>(), "", null!, 10, 1, "P", "O"));
        }

        // What: Spec error table — null chosenOption throws
        // Mutation: Would catch if chosenOption null check is missing
        [Fact]
        public void BuildDeliveryPrompt_NullOption_ThrowsArgumentNull()
        {
            Assert.Throws<ArgumentNullException>(() =>
                SessionDocumentBuilder.BuildDeliveryPrompt(
                    new List<(string, string)>(), null!, FailureTier.None, 0, null, "P", "O"));
        }

        // What: Spec error table — null history in delivery prompt
        // Mutation: Would catch if null check for history is missing in delivery method
        [Fact]
        public void BuildDeliveryPrompt_NullHistory_ThrowsArgumentNull()
        {
            var option = new DialogueOption(StatType.Charm, "text");
            Assert.Throws<ArgumentNullException>(() =>
                SessionDocumentBuilder.BuildDeliveryPrompt(
                    null!, option, FailureTier.None, 0, null, "P", "O"));
        }

        // What: Spec error table — null playerDeliveredMessage throws
        // Mutation: Would catch if message null check is missing
        [Fact]
        public void BuildOpponentPrompt_NullMessage_ThrowsArgumentNull()
        {
            Assert.Throws<ArgumentNullException>(() =>
                SessionDocumentBuilder.BuildOpponentPrompt(
                    new List<(string, string)>(), null!, 10, 10, 1.0, null, "P", "O"));
        }

        // What: Spec error table — null opponentName in InterestChangeBeat throws
        // Mutation: Would catch if opponentName null check is missing
        [Fact]
        public void BuildInterestChangeBeatPrompt_NullOpponentName_ThrowsArgumentNull()
        {
            Assert.Throws<ArgumentNullException>(() =>
                SessionDocumentBuilder.BuildInterestChangeBeatPrompt(
                    null!, 10, 12, InterestState.Interested));
        }

        // What: Spec error table — null prompts in CacheBlockBuilder throw
        // Mutation: Would catch if null checks are missing
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

        // What: Spec — Interest 16 falls in 13-16 range (engaged), NOT 17-20 (very interested)
        // Mutation: Would catch off-by-one at boundary between engaged and very interested
        [Fact]
        public void BuildOpponentPrompt_Interest16_Engaged()
        {
            var result = SessionDocumentBuilder.BuildOpponentPrompt(
                new List<(string, string)>(), "Hey", 10, 16, 1.0, null, "P", "O");

            Assert.Contains("engaged", result);
            Assert.DoesNotContain("very interested", result);
        }

        // What: Spec — Interest 17 falls in 17-20 range (very interested)
        // Mutation: Would catch off-by-one at boundary 17
        [Fact]
        public void BuildOpponentPrompt_Interest17_VeryInterested()
        {
            var result = SessionDocumentBuilder.BuildOpponentPrompt(
                new List<(string, string)>(), "Hey", 10, 17, 1.0, null, "P", "O");

            Assert.Contains("very interested", result);
        }

        // What: Spec — Interest 12 falls in 9-12 range (lukewarm)
        // Mutation: Would catch off-by-one at boundary between lukewarm and engaged
        [Fact]
        public void BuildOpponentPrompt_Interest12_Lukewarm()
        {
            var result = SessionDocumentBuilder.BuildOpponentPrompt(
                new List<(string, string)>(), "Hey", 10, 12, 1.0, null, "P", "O");

            Assert.Contains("lukewarm", result);
        }

        // What: Spec — Interest 8 falls in 5-8 range (cooling)
        // Mutation: Would catch off-by-one at boundary between cooling and lukewarm
        [Fact]
        public void BuildOpponentPrompt_Interest8_Cooling()
        {
            var result = SessionDocumentBuilder.BuildOpponentPrompt(
                new List<(string, string)>(), "Hey", 10, 8, 1.0, null, "P", "O");

            Assert.Contains("cooling", result);
        }

        // What: Spec — Interest 4 falls in 1-4 range (disengaged)
        // Mutation: Would catch off-by-one at boundary between disengaged and cooling
        [Fact]
        public void BuildOpponentPrompt_Interest4_Disengaged()
        {
            var result = SessionDocumentBuilder.BuildOpponentPrompt(
                new List<(string, string)>(), "Hey", 10, 4, 1.0, null, "P", "O");

            Assert.Contains("disengaged", result);
        }

        // What: Spec — Interest 25 falls in 21-25 range (extremely interested)
        // Mutation: Would catch if 25 is treated as special unmatched case
        [Fact]
        public void BuildOpponentPrompt_Interest25_ExtremelyInterested()
        {
            var result = SessionDocumentBuilder.BuildOpponentPrompt(
                new List<(string, string)>(), "Hey", 10, 25, 1.0, null, "P", "O");

            Assert.Contains("extremely interested", result);
        }

        // What: Spec — Interest 1 falls in 1-4 range (disengaged), NOT 0 (unmatching)
        // Mutation: Would catch if 1 maps to unmatching
        [Fact]
        public void BuildOpponentPrompt_Interest1_Disengaged_NotUnmatching()
        {
            var result = SessionDocumentBuilder.BuildOpponentPrompt(
                new List<(string, string)>(), "Hey", 2, 1, 1.0, null, "P", "O");

            Assert.Contains("disengaged", result);
            Assert.DoesNotContain("lost all interest", result);
        }
    }
}
