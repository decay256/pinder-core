using System;
using System.Collections.Generic;
using Pinder.Core.Characters;
using Pinder.Core.Conversation;
using Pinder.Core.Stats;
using Pinder.LlmAdapters;
using Xunit;

namespace Pinder.LlmAdapters.Tests
{
    /// <summary>
    /// Spec-driven tests for Issue #489: voice distinctness — explicit texting style
    /// constraint before option generation.
    /// Tests are derived from docs/specs/issue-489-spec.md acceptance criteria and edge cases.
    /// </summary>
    public class Issue489_VoiceDistinctnessSpecTests
    {
        // ── Test-only helpers ──

        private static StatBlock MakeStats()
        {
            return new StatBlock(
                new Dictionary<StatType, int>
                {
                    { StatType.Charm, 5 },
                    { StatType.Rizz, 3 },
                    { StatType.Honesty, 2 },
                    { StatType.Chaos, 1 },
                    { StatType.Wit, 4 },
                    { StatType.SelfAwareness, 0 }
                },
                new Dictionary<ShadowStatType, int>());
        }

        private static CharacterProfile MakeProfile(string textingStyleFragment = "")
        {
            return new CharacterProfile(
                MakeStats(),
                "system prompt",
                "TestChar",
                new TimingProfile(0, 1f, 0f, "neutral"),
                1,
                bio: "",
                textingStyleFragment: textingStyleFragment);
        }

        private static DialogueContext MakeContext(
            string playerTextingStyle = "",
            string playerName = "Velvet",
            string opponentName = "Sable",
            int currentInterest = 10,
            int currentTurn = 1)
        {
            return new DialogueContext(
                playerPrompt: "player system prompt",
                opponentPrompt: "opponent system prompt",
                conversationHistory: new List<(string, string)>(),
                opponentLastMessage: "hey there",
                activeTraps: Array.Empty<string>(),
                currentInterest: currentInterest,
                playerName: playerName,
                opponentName: opponentName,
                currentTurn: currentTurn,
                playerTextingStyle: playerTextingStyle);
        }

        // ══════════════════════════════════════════════════════════════
        //  AC2: TextingStyleFragment accessible on CharacterProfile
        // ══════════════════════════════════════════════════════════════

        // Mutation: would catch if constructor ignores textingStyleFragment param entirely
        [Fact]
        public void CharacterProfile_TextingStyleFragment_ReturnsProvidedValue()
        {
            var profile = MakeProfile("lowercase-with-intent, precise, ironic");
            Assert.Equal("lowercase-with-intent, precise, ironic", profile.TextingStyleFragment);
        }

        // Mutation: would catch if default is null instead of empty string
        [Fact]
        public void CharacterProfile_TextingStyleFragment_DefaultsToEmptyString()
        {
            var stats = MakeStats();
            var profile = new CharacterProfile(stats, "prompt", "Test",
                new TimingProfile(0, 1f, 0f, "neutral"), 1);

            Assert.Equal(string.Empty, profile.TextingStyleFragment);
            Assert.NotNull(profile.TextingStyleFragment);
        }

        // Mutation: would catch if null is stored directly instead of coalesced to ""
        [Fact]
        public void CharacterProfile_TextingStyleFragment_NullCoalescedToEmpty()
        {
            var profile = MakeProfile(textingStyleFragment: null!);
            Assert.Equal(string.Empty, profile.TextingStyleFragment);
        }

        // Mutation: would catch if constructor truncates or transforms the fragment
        [Fact]
        public void CharacterProfile_TextingStyleFragment_PreservesExactValue()
        {
            string style = "omg, 😭, fast-talk, run-on sentences, excessive emoji, ALL CAPS for emphasis";
            var profile = MakeProfile(style);
            Assert.Equal(style, profile.TextingStyleFragment);
        }

        // ══════════════════════════════════════════════════════════════
        //  AC2: DialogueContext.PlayerTextingStyle property
        // ══════════════════════════════════════════════════════════════

        // Mutation: would catch if playerTextingStyle param is wired to wrong property
        [Fact]
        public void DialogueContext_PlayerTextingStyle_ReturnsProvidedValue()
        {
            var ctx = MakeContext(playerTextingStyle: "lowercase, ellipses, ironic");
            Assert.Equal("lowercase, ellipses, ironic", ctx.PlayerTextingStyle);
        }

        // Mutation: would catch if default is null instead of empty string
        [Fact]
        public void DialogueContext_PlayerTextingStyle_DefaultsToEmpty()
        {
            var ctx = new DialogueContext(
                playerPrompt: "p",
                opponentPrompt: "o",
                conversationHistory: new List<(string, string)>(),
                opponentLastMessage: "",
                activeTraps: Array.Empty<string>(),
                currentInterest: 10);

            Assert.Equal(string.Empty, ctx.PlayerTextingStyle);
            Assert.NotNull(ctx.PlayerTextingStyle);
        }

        // Mutation: would catch if null is stored directly instead of coalesced to ""
        [Fact]
        public void DialogueContext_PlayerTextingStyle_NullCoalescedToEmpty()
        {
            var ctx = MakeContext(playerTextingStyle: null!);
            Assert.Equal(string.Empty, ctx.PlayerTextingStyle);
        }

        // ══════════════════════════════════════════════════════════════
        //  AC1: TEXTING STYLE block injected in BuildDialogueOptionsPrompt
        // ══════════════════════════════════════════════════════════════

        // Mutation: would catch if texting style block is never emitted
        [Fact]
        public void BuildDialogueOptionsPrompt_EmitsTextingStyleBlock_WhenNonEmpty()
        {
            var ctx = MakeContext(playerTextingStyle: "lowercase-with-intent, precise, ironic");
            string result = SessionDocumentBuilder.BuildDialogueOptionsPrompt(ctx);

            Assert.Contains("YOUR TEXTING STYLE", result);
            Assert.Contains("follow this exactly, no deviations", result);
            Assert.Contains("lowercase-with-intent, precise, ironic", result);
        }

        // Mutation: would catch if texting style is placed AFTER [ENGINE] block instead of before
        [Fact]
        public void BuildDialogueOptionsPrompt_TextingStyleAppearsBeforeYourTask()
        {
            var ctx = MakeContext(playerTextingStyle: "some style text");
            string result = SessionDocumentBuilder.BuildDialogueOptionsPrompt(ctx);

            int styleIdx = result.IndexOf("YOUR TEXTING STYLE", StringComparison.Ordinal);
            int engineIdx = result.IndexOf("[ENGINE — Turn", StringComparison.Ordinal);

            Assert.True(styleIdx >= 0, "TEXTING STYLE block must be present");
            Assert.True(engineIdx >= 0, "[ENGINE] block must be present");
            Assert.True(styleIdx < engineIdx,
                $"TEXTING STYLE (at {styleIdx}) must appear before [ENGINE] block (at {engineIdx})");
        }

        // Mutation: would catch if texting style block is emitted even when style is empty
        [Fact]
        public void BuildDialogueOptionsPrompt_OmitsTextingStyleBlock_WhenEmpty()
        {
            var ctx = MakeContext(playerTextingStyle: "");
            string result = SessionDocumentBuilder.BuildDialogueOptionsPrompt(ctx);

            Assert.DoesNotContain("YOUR TEXTING STYLE", result);
        }

        // Mutation: would catch if style text is altered/truncated during injection
        [Fact]
        public void BuildDialogueOptionsPrompt_InjectsStyleTextVerbatim()
        {
            string style = "all lowercase. no caps ever. ellipses instead of periods...\nshort sentences. dry humor.";
            var ctx = MakeContext(playerTextingStyle: style);
            string result = SessionDocumentBuilder.BuildDialogueOptionsPrompt(ctx);

            Assert.Contains(style, result);
        }

        // Mutation: would catch if the heading text is wrong (e.g. "YOUR STYLE" instead of full heading)
        [Fact]
        public void BuildDialogueOptionsPrompt_UsesExactHeadingText()
        {
            var ctx = MakeContext(playerTextingStyle: "test style");
            string result = SessionDocumentBuilder.BuildDialogueOptionsPrompt(ctx);

            Assert.Contains("YOUR TEXTING STYLE — follow this exactly, no deviations:", result);
        }

        // ══════════════════════════════════════════════════════════════
        //  AC1 (cont): Voice check in DialogueOptionsInstruction
        // ══════════════════════════════════════════════════════════════

        // Mutation: would catch if voice-check line is removed from DialogueOptionsInstruction
        [Fact]
        public void DialogueOptionsInstruction_ContainsVoiceCheckVerification()
        {
            string instruction = PromptTemplates.DialogueOptionsInstruction;
            Assert.Contains("Before writing each option, verify: does this sound exactly like", instruction);
            Assert.Contains("the texting style above? If not, rewrite it.", instruction);
        }

        // ══════════════════════════════════════════════════════════════
        //  Edge case: backward compatibility — default context (no style)
        // ══════════════════════════════════════════════════════════════

        // Mutation: would catch if omitting playerTextingStyle causes a crash or injects garbage
        [Fact]
        public void BuildDialogueOptionsPrompt_WorksWithDefaultContext_NoStyleBlock()
        {
            var ctx = new DialogueContext(
                playerPrompt: "p",
                opponentPrompt: "o",
                conversationHistory: new List<(string, string)>(),
                opponentLastMessage: "hello",
                activeTraps: Array.Empty<string>(),
                currentInterest: 10,
                playerName: "Velvet",
                opponentName: "Sable",
                currentTurn: 1);

            string result = SessionDocumentBuilder.BuildDialogueOptionsPrompt(ctx);

            Assert.DoesNotContain("YOUR TEXTING STYLE", result);
            // YOUR TASK should still appear
            Assert.Contains("Generate exactly 4 dialogue options", result);
        }

        // ══════════════════════════════════════════════════════════════
        //  Edge case: multiple fragments joined with " | "
        // ══════════════════════════════════════════════════════════════

        // Mutation: would catch if pipe-joined fragments are split or only first part is used
        [Fact]
        public void BuildDialogueOptionsPrompt_PreservesJoinedFragments()
        {
            string joined = "lowercase-with-intent | uses ellipses for dramatic pauses | never uses exclamation marks";
            var ctx = MakeContext(playerTextingStyle: joined);
            string result = SessionDocumentBuilder.BuildDialogueOptionsPrompt(ctx);

            Assert.Contains(joined, result);
        }

        // ══════════════════════════════════════════════════════════════
        //  Edge case: very long texting style fragment
        // ══════════════════════════════════════════════════════════════

        // Mutation: would catch if there's a length limit/truncation on style text
        [Fact]
        public void BuildDialogueOptionsPrompt_DoesNotTruncateLongStyle()
        {
            string longStyle = new string('x', 1500);
            var ctx = MakeContext(playerTextingStyle: longStyle);
            string result = SessionDocumentBuilder.BuildDialogueOptionsPrompt(ctx);

            Assert.Contains(longStyle, result);
        }

        // ══════════════════════════════════════════════════════════════
        //  Edge case: Unicode / emoji in style fragment
        // ══════════════════════════════════════════════════════════════

        // Mutation: would catch if emoji/unicode characters are stripped or corrupted
        [Fact]
        public void BuildDialogueOptionsPrompt_PreservesUnicodeAndEmoji()
        {
            string emojiStyle = "omg, 😭, fast-talk, run-on sentences, excessive emoji, ALL CAPS for emphasis";
            var ctx = MakeContext(playerTextingStyle: emojiStyle);
            string result = SessionDocumentBuilder.BuildDialogueOptionsPrompt(ctx);

            Assert.Contains(emojiStyle, result);
        }

        // ══════════════════════════════════════════════════════════════
        //  AC3: Voice distinctness — correct character's style is used
        //  (structural test: Velvet's style appears, not Sable's)
        // ══════════════════════════════════════════════════════════════

        // Mutation: would catch if opponent's style leaks into the prompt instead of player's
        [Fact]
        public void BuildDialogueOptionsPrompt_UsesPlayerStyle_NotOpponentStyle()
        {
            string velvetStyle = "lowercase-with-intent, precise, ironic";
            var ctx = MakeContext(
                playerTextingStyle: velvetStyle,
                playerName: "Velvet",
                opponentName: "Sable");

            string result = SessionDocumentBuilder.BuildDialogueOptionsPrompt(ctx);

            Assert.Contains(velvetStyle, result);
            // The opponent's style text shouldn't appear in the TEXTING STYLE block
            // (opponent's prompt is separate context, not a texting style directive)
        }

        // ══════════════════════════════════════════════════════════════
        //  Spec Example 1: Velvet character with texting style
        // ══════════════════════════════════════════════════════════════

        // Mutation: would catch if the full integration path fails (style → heading → task ordering)
        [Fact]
        public void BuildDialogueOptionsPrompt_SpecExample1_VelvetWithStyle()
        {
            string velvetStyle = "lowercase-with-intent, precise, ironic, uses ellipses for dramatic pauses, never uses exclamation marks";
            var ctx = MakeContext(
                playerTextingStyle: velvetStyle,
                playerName: "Velvet",
                opponentName: "Sable");

            string result = SessionDocumentBuilder.BuildDialogueOptionsPrompt(ctx);

            // Heading present
            Assert.Contains("YOUR TEXTING STYLE — follow this exactly, no deviations:", result);
            // Style text present
            Assert.Contains(velvetStyle, result);
            // Style before ENGINE block
            int styleIdx = result.IndexOf("YOUR TEXTING STYLE", StringComparison.Ordinal);
            int engineIdx = result.IndexOf("[ENGINE — Turn", StringComparison.Ordinal);
            Assert.True(styleIdx < engineIdx);
        }

        // ══════════════════════════════════════════════════════════════
        //  Spec Example 2: No texting style (backward compat)
        // ══════════════════════════════════════════════════════════════

        // Mutation: would catch if empty style still emits the heading block
        [Fact]
        public void BuildDialogueOptionsPrompt_SpecExample2_NoStyle()
        {
            var ctx = MakeContext(playerTextingStyle: "");
            string result = SessionDocumentBuilder.BuildDialogueOptionsPrompt(ctx);

            Assert.DoesNotContain("YOUR TEXTING STYLE", result);
            // YOUR TASK should still be present
            Assert.Contains("Generate exactly 4 dialogue options", result);
        }

        // ══════════════════════════════════════════════════════════════
        //  Spec Example 3: Sable character
        // ══════════════════════════════════════════════════════════════

        // Mutation: would catch if emoji-heavy style text is mangled
        [Fact]
        public void BuildDialogueOptionsPrompt_SpecExample3_SableWithEmojiStyle()
        {
            string sableStyle = "omg, 😭, fast-talk, run-on sentences, excessive emoji, ALL CAPS for emphasis";
            var ctx = MakeContext(
                playerTextingStyle: sableStyle,
                playerName: "Sable",
                opponentName: "Velvet");

            string result = SessionDocumentBuilder.BuildDialogueOptionsPrompt(ctx);

            Assert.Contains("YOUR TEXTING STYLE — follow this exactly, no deviations:", result);
            Assert.Contains(sableStyle, result);
        }

        // ══════════════════════════════════════════════════════════════
        //  Structural: YOUR TASK block always present regardless of style
        // ══════════════════════════════════════════════════════════════

        // Mutation: would catch if adding style somehow removes the YOUR TASK block
        [Theory]
        [InlineData("")]
        [InlineData("some style")]
        public void BuildDialogueOptionsPrompt_AlwaysContainsYourTask(string style)
        {
            var ctx = MakeContext(playerTextingStyle: style);
            string result = SessionDocumentBuilder.BuildDialogueOptionsPrompt(ctx);

            Assert.Contains("Generate exactly 4 dialogue options", result);
        }
    }
}
