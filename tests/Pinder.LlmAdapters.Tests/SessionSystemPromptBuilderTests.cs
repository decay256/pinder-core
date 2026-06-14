using System;
using Xunit;

namespace Pinder.LlmAdapters.Tests
{
    /// <summary>
    /// #1124: BOTH sessions share ONE canonical GM puppeteer system-prompt
    /// template; only the injected character-spec block differs. The legacy
    /// combined Build(player, datee) path has been removed.
    /// </summary>
    [Collection("PromptTraceSingleton")]
    public class SessionSystemPromptBuilderTests
    {
        private const string PlayerAvatarPrompt = "You are Velvet. Lowercase-with-intent. Ironic. Level 7 Veteran.";
        private const string DateePrompt = "You are Sable. Fast-talking. Uses omg and emoji. Level 5 Journeyman.";

        [Fact]
        public void BuildPlayerAvatar_ContainsCharacterPrompt()
        {
            var result = SessionSystemPromptBuilder.BuildPlayerAvatar(PlayerAvatarPrompt);
            Assert.Contains(PlayerAvatarPrompt, result);
        }

        [Fact]
        public void BuildDatee_ContainsCharacterPrompt()
        {
            var result = SessionSystemPromptBuilder.BuildDatee(DateePrompt);
            Assert.Contains(DateePrompt, result);
        }

        [Fact]
        public void Build_ContainsGameVision()
        {
            var result = SessionSystemPromptBuilder.BuildPlayerAvatar(PlayerAvatarPrompt);
            Assert.Contains("comedy dating RPG", result);
        }

        [Fact]
        public void Build_ContainsWorldDescription()
        {
            var result = SessionSystemPromptBuilder.BuildDatee(DateePrompt);
            Assert.Contains("dating server", result);
        }

        [Fact]
        public void Build_ContainsMetaContract()
        {
            var result = SessionSystemPromptBuilder.BuildPlayerAvatar(PlayerAvatarPrompt);
            Assert.Contains("break character", result);
        }

        [Fact]
        public void Build_ContainsWritingRules()
        {
            var result = SessionSystemPromptBuilder.BuildDatee(DateePrompt);
            Assert.Contains("texting register", result);
        }

        [Fact]
        public void Build_ContainsNarrativeDoctrineHeader()
        {
            // #1153: the GM base is now a single field; the doctrine sub-header
            // lives inside the assembled GameMasterPrompt prose.
            var result = SessionSystemPromptBuilder.BuildPlayerAvatar(PlayerAvatarPrompt);
            Assert.Contains("WRITING RULES", result);
        }

        [Fact]
        public void Build_ContainsGmPuppeteerFraming()
        {
            // #1124: both sessions carry the shared GM puppeteer framing.
            var playerResult = SessionSystemPromptBuilder.BuildPlayerAvatar(PlayerAvatarPrompt);
            var dateeResult = SessionSystemPromptBuilder.BuildDatee(DateePrompt);
            Assert.Contains("== GAME MASTER ==", playerResult);
            Assert.Contains("== GAME MASTER ==", dateeResult);
            Assert.Contains("EXACTLY ONE character", playerResult);
            Assert.Contains("EXACTLY ONE character", dateeResult);
        }

        [Fact]
        public void Build_HasSharedSections()
        {
            // #1153: GM base collapsed into one field; GAME MASTER header and the
            // character-spec header bracket the shared base.
            var result = SessionSystemPromptBuilder.BuildPlayerAvatar(PlayerAvatarPrompt);
            Assert.Contains("== GAME MASTER ==", result);
            Assert.Contains(SessionSystemPromptBuilder.CharacterSpecHeader, result);
        }

        [Fact]
        public void Build_CharacterSpecComesLast()
        {
            // Static GM base first; the variable character-spec block at the tail,
            // so the cacheable prefix stays stable (#1123 caching).
            var result = SessionSystemPromptBuilder.BuildPlayerAvatar(PlayerAvatarPrompt);
            var gmIdx = result.IndexOf("== GAME MASTER ==", StringComparison.Ordinal);
            var specIdx = result.IndexOf(SessionSystemPromptBuilder.CharacterSpecHeader, StringComparison.Ordinal);

            Assert.True(gmIdx >= 0, "GAME MASTER framing should be present");
            Assert.True(gmIdx < specIdx, "Static base should precede the character-spec block");
            // The character spec is the final block — its prompt text appears after it.
            Assert.Contains(PlayerAvatarPrompt, result.Substring(specIdx));
        }

        // ── #1124 KEY ACCEPTANCE: identical-template-except-spec ──────────────

        [Fact]
        public void BothSessions_ShareIdenticalBase_OnlyCharacterSpecDiffers()
        {
            // The single shared GM template means the two built prompts must be
            // byte-for-byte identical UP TO the character-spec header. Everything
            // before "== CHARACTER YOU CONTROL ==" is the shared cacheable base.
            var def = GameDefinition.PinderDefaults;
            var playerResult = SessionSystemPromptBuilder.BuildPlayerAvatar(PlayerAvatarPrompt, def);
            var dateeResult = SessionSystemPromptBuilder.BuildDatee(DateePrompt, def);

            var header = SessionSystemPromptBuilder.CharacterSpecHeader;
            var playerSpecIdx = playerResult.IndexOf(header, StringComparison.Ordinal);
            var dateeSpecIdx = dateeResult.IndexOf(header, StringComparison.Ordinal);

            Assert.True(playerSpecIdx > 0, "player prompt must contain the character-spec header");
            Assert.True(dateeSpecIdx > 0, "datee prompt must contain the character-spec header");

            var playerBase = playerResult.Substring(0, playerSpecIdx);
            var dateeBase = dateeResult.Substring(0, dateeSpecIdx);

            // The shared GM base is identical across both sessions.
            Assert.Equal(dateeBase, playerBase);

            // And the ONLY difference is the injected character spec.
            Assert.Contains(PlayerAvatarPrompt, playerResult.Substring(playerSpecIdx));
            Assert.Contains(DateePrompt, dateeResult.Substring(dateeSpecIdx));
            Assert.DoesNotContain(DateePrompt, playerResult);
            Assert.DoesNotContain(PlayerAvatarPrompt, dateeResult);
        }

        [Fact]
        public void LegacyBuild_IsRemoved()
        {
            // #1124: the legacy combined Build(player, datee) path is deleted.
            // Reflection guard so the symbol's removal is asserted in tests.
            var method = typeof(SessionSystemPromptBuilder).GetMethod(
                "Build",
                new[] { typeof(string), typeof(string), typeof(GameDefinition) });
            Assert.Null(method);

            var anyBuild = typeof(SessionSystemPromptBuilder).GetMethod("Build");
            Assert.Null(anyBuild);
        }

        [Fact]
        public void Build_NullGameDef_UsesPinderDefaults()
        {
            var result = SessionSystemPromptBuilder.BuildPlayerAvatar(PlayerAvatarPrompt, null);
            Assert.Contains("Pinder", result);
            Assert.Contains("comedy dating RPG", result);
        }

        [Fact]
        public void Build_CustomGameDef_UsesProvidedValues()
        {
            var custom = new GameDefinition(
                "CustomGame",
                "Custom vision text Custom world desc Custom meta contract Custom writing rules",
                "Custom player role",
                "Custom datee role");

            var result = SessionSystemPromptBuilder.BuildPlayerAvatar(PlayerAvatarPrompt, custom);
            Assert.Contains("Custom vision text", result);
            Assert.Contains("Custom world desc", result);
            Assert.Contains("Custom meta contract", result);
            Assert.Contains("Custom writing rules", result);
        }

        [Fact]
        public void BuildPlayerAvatar_NullPrompt_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() =>
                SessionSystemPromptBuilder.BuildPlayerAvatar(null!));
        }

        [Fact]
        public void BuildDatee_NullPrompt_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() =>
                SessionSystemPromptBuilder.BuildDatee(null!));
        }

        [Fact]
        public void Build_EmptyPrompts_ProducesValidOutput()
        {
            var playerResult = SessionSystemPromptBuilder.BuildPlayerAvatar("");
            var dateeResult = SessionSystemPromptBuilder.BuildDatee("");
            Assert.Contains(SessionSystemPromptBuilder.CharacterSpecHeader, playerResult);
            Assert.Contains(SessionSystemPromptBuilder.CharacterSpecHeader, dateeResult);
        }

        [Fact]
        public void Build_MetaContractIncludesWritingRules()
        {
            var custom = new GameDefinition(
                "G", "MetaSection WritingSection", "P", "O");

            var result = SessionSystemPromptBuilder.BuildPlayerAvatar("p", custom);
            var specIdx = result.IndexOf(SessionSystemPromptBuilder.CharacterSpecHeader, StringComparison.Ordinal);
            var gmBase = result.Substring(0, specIdx);
            Assert.Contains("MetaSection", gmBase);
            Assert.Contains("WritingSection", gmBase);
        }
    }
}
