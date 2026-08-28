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
                playerAvatarPrompt: "player prompt",
                dateePrompt: "datee prompt",
                conversationHistory: new List<(string, string)>(),
                dateeLastMessage: "hey",
                activeTraps: Array.Empty<string>(),
                currentInterest: currentInterest,
                playerName: "Velvet",
                dateeName: "Sable",
                currentTurn: currentTurn,
                playerTextingStyle: playerTextingStyle, availableStats: new[] { Pinder.Core.Stats.StatType.Charm, Pinder.Core.Stats.StatType.Rizz, Pinder.Core.Stats.StatType.Honesty,  });
        }

        // ── CharacterProfile.TextingStyleFragment ──

        [Fact]
        public void CharacterProfile_TextingStyleFragment_DefaultsToEmpty()
        {
            var stats = new StatBlock(
                new Dictionary<StatType, int> { { StatType.Charm, 5 }, { StatType.Rizz, 3 }, { StatType.Honesty, 2 }, { StatType.Chaos, 1 }, { StatType.Wit, 4 }, { StatType.SelfAwareness, 0 } },
                new Dictionary<ShadowStatType, int>());
            var profile = new CharacterProfile(stats, "prompt", "Test", new TimingProfile(0, 1f, 0f, "neutral"), 1,
                characterId: Guid.Parse("48900000-0000-0000-0000-000000000001"));

            Assert.Equal(string.Empty, profile.TextingStyleFragment);
        }

        [Fact]
        public void CharacterProfile_TextingStyleFragment_StoresValueWhenProvided()
        {
            var stats = new StatBlock(
                new Dictionary<StatType, int> { { StatType.Charm, 5 }, { StatType.Rizz, 3 }, { StatType.Honesty, 2 }, { StatType.Chaos, 1 }, { StatType.Wit, 4 }, { StatType.SelfAwareness, 0 } },
                new Dictionary<ShadowStatType, int>());
            var profile = new CharacterProfile(stats, "prompt", "Test", new TimingProfile(0, 1f, 0f, "neutral"), 1,
                textingStyleFragment: "lowercase-with-intent, precise, ironic",
                characterId: Guid.Parse("48900000-0000-0000-0000-000000000002"));

            Assert.Equal("lowercase-with-intent, precise, ironic", profile.TextingStyleFragment);
        }

        [Fact]
        public void CharacterProfile_TextingStyleFragment_NullCoercesToEmpty()
        {
            var stats = new StatBlock(
                new Dictionary<StatType, int> { { StatType.Charm, 5 }, { StatType.Rizz, 3 }, { StatType.Honesty, 2 }, { StatType.Chaos, 1 }, { StatType.Wit, 4 }, { StatType.SelfAwareness, 0 } },
                new Dictionary<ShadowStatType, int>());
            var profile = new CharacterProfile(stats, "prompt", "Test", new TimingProfile(0, 1f, 0f, "neutral"), 1,
                textingStyleFragment: null,
                characterId: Guid.Parse("48900000-0000-0000-0000-000000000003"));

            Assert.Equal(string.Empty, profile.TextingStyleFragment);
        }

        // ── DialogueContext.PlayerTextingStyle ──

        [Fact]
        public void DialogueContext_PlayerTextingStyle_DefaultsToEmpty()
        {
            var ctx = new DialogueContext(
                playerAvatarPrompt: "p",
                dateePrompt: "o",
                conversationHistory: new List<(string, string)>(),
                dateeLastMessage: "",
                activeTraps: Array.Empty<string>(),
                currentInterest: 10, availableStats: new[] { Pinder.Core.Stats.StatType.Charm, Pinder.Core.Stats.StatType.Rizz, Pinder.Core.Stats.StatType.Honesty,  }, playerName: "P", dateeName: "O");

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
            Assert.Contains("YOUR TEXTING STYLE", result);
            Assert.Contains("loose expressive influences", result);
            Assert.Contains("lowercase-with-intent, precise, ironic", result);
            Assert.DoesNotContain("follow this exactly", result, StringComparison.OrdinalIgnoreCase);

            // TEXTING STYLE must appear before ENGINE block
            int styleIdx = result.IndexOf("YOUR TEXTING STYLE", StringComparison.Ordinal);
            int engineIdx = result.IndexOf("[ENGINE — Turn", StringComparison.Ordinal);
            Assert.True(styleIdx < engineIdx,
                "TEXTING STYLE block must appear before [ENGINE] block");
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
                playerAvatarPrompt: "p",
                dateePrompt: "o",
                conversationHistory: new List<(string, string)>(),
                dateeLastMessage: "hi",
                activeTraps: Array.Empty<string>(),
                currentInterest: 10,
                playerName: "V",
                dateeName: "S",
                currentTurn: 1, availableStats: new[] { Pinder.Core.Stats.StatType.Charm, Pinder.Core.Stats.StatType.Rizz, Pinder.Core.Stats.StatType.Honesty,  });
            string result = SessionDocumentBuilder.BuildDialogueOptionsPrompt(ctx);

            Assert.DoesNotContain("YOUR TEXTING STYLE", result);
        }

        // ── Voice check in DialogueOptionsInstruction ──

        [Fact]
        public void DialogueOptionsInstruction_TreatsVoiceAsSoftInfluence()
        {
            string instruction = PromptTemplates.DialogueOptionsInstruction;
            Assert.Contains("Treat the texting style above as loose expressive influences", instruction);
            Assert.Contains("do not mechanically reproduce", instruction);
            Assert.DoesNotContain("sound exactly like", instruction, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("MUST be maintained consistently", instruction, StringComparison.OrdinalIgnoreCase);
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
