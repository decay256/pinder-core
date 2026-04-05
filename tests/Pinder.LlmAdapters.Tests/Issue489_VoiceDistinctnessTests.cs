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
    /// Tests for Issue #489: voice distinctness — explicit texting style constraint
    /// before option generation.
    /// </summary>
    public class Issue489_VoiceDistinctnessTests
    {
        // ── Helpers ──

        private static DialogueContext MakeContext(
            string playerTextingStyle = "",
            int currentInterest = 10,
            int currentTurn = 1)
        {
            return new DialogueContext(
                playerPrompt: "player prompt",
                opponentPrompt: "opponent prompt",
                conversationHistory: new List<(string, string)>(),
                opponentLastMessage: "hey",
                activeTraps: Array.Empty<string>(),
                currentInterest: currentInterest,
                playerName: "Velvet",
                opponentName: "Sable",
                currentTurn: currentTurn,
                playerTextingStyle: playerTextingStyle);
        }

        // ── CharacterProfile.TextingStyleFragment ──

        [Fact]
        public void CharacterProfile_TextingStyleFragment_DefaultsToEmpty()
        {
            var stats = new StatBlock(
                new Dictionary<StatType, int> { { StatType.Charm, 5 }, { StatType.Rizz, 3 }, { StatType.Honesty, 2 }, { StatType.Chaos, 1 }, { StatType.Wit, 4 }, { StatType.SelfAwareness, 0 } },
                new Dictionary<ShadowStatType, int>());
            var profile = new CharacterProfile(stats, "prompt", "Test", new TimingProfile(0, 1f, 0f, "neutral"), 1);

            Assert.Equal(string.Empty, profile.TextingStyleFragment);
        }

        [Fact]
        public void CharacterProfile_TextingStyleFragment_StoresValueWhenProvided()
        {
            var stats = new StatBlock(
                new Dictionary<StatType, int> { { StatType.Charm, 5 }, { StatType.Rizz, 3 }, { StatType.Honesty, 2 }, { StatType.Chaos, 1 }, { StatType.Wit, 4 }, { StatType.SelfAwareness, 0 } },
                new Dictionary<ShadowStatType, int>());
            var profile = new CharacterProfile(stats, "prompt", "Test", new TimingProfile(0, 1f, 0f, "neutral"), 1,
                textingStyleFragment: "lowercase-with-intent, precise, ironic");

            Assert.Equal("lowercase-with-intent, precise, ironic", profile.TextingStyleFragment);
        }

        [Fact]
        public void CharacterProfile_TextingStyleFragment_NullCoercesToEmpty()
        {
            var stats = new StatBlock(
                new Dictionary<StatType, int> { { StatType.Charm, 5 }, { StatType.Rizz, 3 }, { StatType.Honesty, 2 }, { StatType.Chaos, 1 }, { StatType.Wit, 4 }, { StatType.SelfAwareness, 0 } },
                new Dictionary<ShadowStatType, int>());
            var profile = new CharacterProfile(stats, "prompt", "Test", new TimingProfile(0, 1f, 0f, "neutral"), 1,
                textingStyleFragment: null);

            Assert.Equal(string.Empty, profile.TextingStyleFragment);
        }

        // ── DialogueContext.PlayerTextingStyle ──

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
        }

        [Fact]
        public void DialogueContext_PlayerTextingStyle_StoresValue()
        {
            var ctx = MakeContext(playerTextingStyle: "lowercase, ellipses, ironic");
            Assert.Equal("lowercase, ellipses, ironic", ctx.PlayerTextingStyle);
        }

        // ── SessionDocumentBuilder texting style injection ──

        [Fact]
        public void BuildDialogueOptionsPrompt_InjectsTextingStyleBeforeTask_WhenProvided()
        {
            var ctx = MakeContext(playerTextingStyle: "lowercase-with-intent, precise, ironic");
            string result = SessionDocumentBuilder.BuildDialogueOptionsPrompt(ctx);

            // TEXTING STYLE block must appear
            Assert.Contains("YOUR TEXTING STYLE — follow this exactly, no deviations:", result);
            Assert.Contains("lowercase-with-intent, precise, ironic", result);

            // TEXTING STYLE must appear before YOUR TASK
            int styleIdx = result.IndexOf("YOUR TEXTING STYLE", StringComparison.Ordinal);
            int taskIdx = result.IndexOf("YOUR TASK", StringComparison.Ordinal);
            Assert.True(styleIdx < taskIdx,
                "TEXTING STYLE block must appear before YOUR TASK");
        }

        [Fact]
        public void BuildDialogueOptionsPrompt_OmitsTextingStyle_WhenEmpty()
        {
            var ctx = MakeContext(playerTextingStyle: "");
            string result = SessionDocumentBuilder.BuildDialogueOptionsPrompt(ctx);

            Assert.DoesNotContain("YOUR TEXTING STYLE", result);
        }

        [Fact]
        public void BuildDialogueOptionsPrompt_OmitsTextingStyle_WhenDefault()
        {
            // Default constructor — no playerTextingStyle param
            var ctx = new DialogueContext(
                playerPrompt: "p",
                opponentPrompt: "o",
                conversationHistory: new List<(string, string)>(),
                opponentLastMessage: "hi",
                activeTraps: Array.Empty<string>(),
                currentInterest: 10,
                playerName: "V",
                opponentName: "S",
                currentTurn: 1);
            string result = SessionDocumentBuilder.BuildDialogueOptionsPrompt(ctx);

            Assert.DoesNotContain("YOUR TEXTING STYLE", result);
        }

        // ── Voice check in DialogueOptionsInstruction ──

        [Fact]
        public void DialogueOptionsInstruction_ContainsVoiceCheck()
        {
            Assert.Contains("Before writing each option, verify: does this sound exactly like",
                PromptTemplates.DialogueOptionsInstruction);
            Assert.Contains("the texting style above? If not, rewrite it.",
                PromptTemplates.DialogueOptionsInstruction);
        }

        // ── Texting style verbatim injection ──

        [Fact]
        public void BuildDialogueOptionsPrompt_InjectsStyleVerbatim()
        {
            string style = "all lowercase. no caps ever. ellipses instead of periods...\nshort sentences. dry humor.";
            var ctx = MakeContext(playerTextingStyle: style);
            string result = SessionDocumentBuilder.BuildDialogueOptionsPrompt(ctx);

            // The style text should appear verbatim in the output
            Assert.Contains(style, result);
        }
    }
}
