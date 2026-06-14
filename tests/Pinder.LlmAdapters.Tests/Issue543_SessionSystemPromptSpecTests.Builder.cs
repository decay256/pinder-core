using System;
using Xunit;

namespace Pinder.LlmAdapters.Tests
{
    [Collection("PromptTraceSingleton")]
    public partial class Issue543_SessionSystemPromptSpecTests
    {
        #region AC3: GameDefinition.PinderDefaults hardcoded fallback

        // What: AC3 — PinderDefaults.Name is "Pinder"
        [Fact]
        public void PinderDefaults_NameIsPinder()
        {
            Assert.Equal("Pinder", GameDefinition.PinderDefaults.Name);
        }

        // What: AC3 — PinderDefaults.GameMasterPrompt describes comedy dating RPG
        [Fact]
        public void PinderDefaults_GameMasterPromptDescribesComedyDatingRpg()
        {
            var gm = GameDefinition.PinderDefaults.GameMasterPrompt;
            Assert.Contains("comedy dating RPG", gm);
            Assert.Contains("sentient penis", gm);
        }

        // What: AC3 — PinderDefaults.GameMasterPrompt carries the GM base header
        [Fact]
        public void PinderDefaults_GameMasterPromptHasGameMasterHeader()
        {
            Assert.Contains("== GAME MASTER ==", GameDefinition.PinderDefaults.GameMasterPrompt);
        }

        // What: AC3 — PinderDefaults.PlayerAvatarRoleDescription describes player role
        [Fact]
        public void PinderDefaults_PlayerAvatarRoleDescriptionIsNotEmpty()
        {
            Assert.False(string.IsNullOrWhiteSpace(GameDefinition.PinderDefaults.PlayerAvatarRoleDescription));
        }

        // What: AC3 — PinderDefaults.DateeRoleDescription describes datee role
        [Fact]
        public void PinderDefaults_DateeRoleDescriptionMentionsDatee()
        {
            Assert.Contains("datee", GameDefinition.PinderDefaults.DateeRoleDescription,
                StringComparison.OrdinalIgnoreCase);
        }

        // What: AC3 — PinderDefaults.GameMasterPrompt specifies never break character
        [Fact]
        public void PinderDefaults_GameMasterPromptMentionsBreakCharacter()
        {
            Assert.Contains("break character", GameDefinition.PinderDefaults.GameMasterPrompt,
                StringComparison.OrdinalIgnoreCase);
        }

        // What: AC3 — PinderDefaults.GameMasterPrompt specifies texting register
        [Fact]
        public void PinderDefaults_GameMasterPromptMentionsTextingRegister()
        {
            Assert.Contains("texting register", GameDefinition.PinderDefaults.GameMasterPrompt,
                StringComparison.OrdinalIgnoreCase);
        }

        // What: AC3 — PinderDefaults returns the same instance (static property)
        [Fact]
        public void PinderDefaults_ReturnsSameInstance()
        {
            var a = GameDefinition.PinderDefaults;
            var b = GameDefinition.PinderDefaults;
            Assert.Same(a, b);
        }

        // What: AC3 — All required fields are non-null
        [Fact]
        public void PinderDefaults_AllFieldsNonNull()
        {
            var gd = GameDefinition.PinderDefaults;
            Assert.NotNull(gd.Name);
            Assert.NotNull(gd.GameMasterPrompt);
            Assert.NotNull(gd.PlayerAvatarRoleDescription);
            Assert.NotNull(gd.DateeRoleDescription);
        }

        #endregion

        #region AC4: Single-field GM base, character-spec block last (#1153)

        // What: AC4 — the built prompt contains the GM base header and the character spec header.
        [Fact]
        public void Build_ContainsGmBaseAndCharacterSpecHeaders()
        {
            var result = SessionSystemPromptBuilder.BuildPlayerAvatar("player text");
            Assert.Contains("== GAME MASTER ==", result);
            Assert.Contains(SessionSystemPromptBuilder.CharacterSpecHeader, result);
        }

        // What: AC4 — GM base first, character-spec block last.
        [Fact]
        public void Build_GmBasePrecedesCharacterSpec()
        {
            var result = SessionSystemPromptBuilder.BuildPlayerAvatar("player text");
            var gmIdx = result.IndexOf("== GAME MASTER ==", StringComparison.Ordinal);
            var specIdx = result.IndexOf(SessionSystemPromptBuilder.CharacterSpecHeader, StringComparison.Ordinal);

            Assert.True(gmIdx >= 0, "GM base header must be present");
            Assert.True(gmIdx < specIdx, "GM base must precede the character-spec block");
        }

        // What: AC4 — the GM base prefix is sourced from gameDef.GameMasterPrompt
        [Fact]
        public void Build_GmBaseSectionContainsGameMasterPrompt()
        {
            var custom = new GameDefinition(
                "G", "UNIQUE_GM_PROMPT_XYZ", "P", "O");
            var result = SessionSystemPromptBuilder.BuildPlayerAvatar("player", custom);

            var specIdx = result.IndexOf(SessionSystemPromptBuilder.CharacterSpecHeader, StringComparison.Ordinal);
            var gmBase = result.Substring(0, specIdx);
            Assert.Contains("UNIQUE_GM_PROMPT_XYZ", gmBase);
        }

        // What: AC4 — character-spec block contains the player avatar prompt verbatim
        [Fact]
        public void Build_CharacterSpecContainsPlayerPromptVerbatim()
        {
            var playerText = "You are Velvet. Lowercase-with-intent. Ironic. Level 7 Veteran.";
            var result = SessionSystemPromptBuilder.BuildPlayerAvatar(playerText);

            var specIdx = result.IndexOf(SessionSystemPromptBuilder.CharacterSpecHeader, StringComparison.Ordinal);
            var specSection = result.Substring(specIdx);
            Assert.Contains(playerText, specSection);
        }

        // What: AC4 — datee session character-spec block contains the datee prompt verbatim
        [Fact]
        public void Build_CharacterSpecContainsDateePromptVerbatim()
        {
            var dateeText = "You are Sable. Fast-talking. Uses omg and emoji. Level 5.";
            var result = SessionSystemPromptBuilder.BuildDatee(dateeText);

            var specIdx = result.IndexOf(SessionSystemPromptBuilder.CharacterSpecHeader, StringComparison.Ordinal);
            var specSection = result.Substring(specIdx);
            Assert.Contains(dateeText, specSection);
        }

        // What: AC4 — the GM base body is rendered verbatim from the single field
        [Fact]
        public void Build_GmBaseContainsFullGameMasterPromptBody()
        {
            var custom = new GameDefinition(
                "G", "UNIQUE_BODY_123 UNIQUE_BODY_456", "P", "O");

            var result = SessionSystemPromptBuilder.BuildPlayerAvatar("p", custom);
            var specIdx = result.IndexOf(SessionSystemPromptBuilder.CharacterSpecHeader, StringComparison.Ordinal);
            var gmBase = result.Substring(0, specIdx);
            Assert.Contains("UNIQUE_BODY_123", gmBase);
            Assert.Contains("UNIQUE_BODY_456", gmBase);
        }

        #endregion

        #region AC5: Unit test — prompt contains character + GM base content

        // What: AC5 — full integration: a built prompt contains the expected content sources
        [Fact]
        public void Build_WithKnownInputs_ContainsAllContent()
        {
            var playerAvatarPrompt = "You are Velvet. Lowercase-with-intent. Ironic. Level 7 Veteran.";
            var gameDef = GameDefinition.PinderDefaults;

            var result = SessionSystemPromptBuilder.BuildPlayerAvatar(playerAvatarPrompt, gameDef);

            // Character prompt appears verbatim
            Assert.Contains(playerAvatarPrompt, result);
            // GM base content present
            Assert.Contains("comedy dating RPG", result);
            Assert.Contains(gameDef.GameMasterPrompt.Trim(), result);
            // Meta contract content present
            Assert.Contains("break character", result);
            // GM puppeteer framing present
            Assert.Contains("== GAME MASTER ==", result);
        }

        #endregion

        #region Edge cases: null gameDef, empty prompts, null prompts

        // What: Edge case — null gameDef defaults to PinderDefaults
        [Fact]
        public void Build_NullGameDef_UsesPinderDefaults()
        {
            var result = SessionSystemPromptBuilder.BuildPlayerAvatar("player", null);
            Assert.Contains("comedy dating RPG", result);
            Assert.Contains("== GAME MASTER ==", result);
        }

        // What: Edge case — null gameDef (omitted param) defaults to PinderDefaults
        [Fact]
        public void Build_OmittedGameDef_UsesPinderDefaults()
        {
            var result = SessionSystemPromptBuilder.BuildPlayerAvatar("player");
            Assert.Contains("comedy dating RPG", result);
        }

        // What: Edge case — null player prompt throws ArgumentNullException
        [Fact]
        public void Build_NullPlayerPrompt_ThrowsArgumentNullException()
        {
            var ex = Assert.Throws<ArgumentNullException>(() =>
                SessionSystemPromptBuilder.BuildPlayerAvatar(null!));
            Assert.Equal("playerAvatarPrompt", ex.ParamName);
        }

        // What: Edge case — null datee prompt throws ArgumentNullException
        [Fact]
        public void Build_NullDateePrompt_ThrowsArgumentNullException()
        {
            var ex = Assert.Throws<ArgumentNullException>(() =>
                SessionSystemPromptBuilder.BuildDatee(null!));
            Assert.Equal("dateePrompt", ex.ParamName);
        }

        // What: Edge case — empty string prompts produce valid output with headers
        [Fact]
        public void Build_EmptyPrompts_ProducesAllSections()
        {
            var result = SessionSystemPromptBuilder.BuildPlayerAvatar("");
            Assert.Contains("== GAME MASTER ==", result);
            Assert.Contains(SessionSystemPromptBuilder.CharacterSpecHeader, result);
        }

        // What: Edge case — empty GameDefinition fields produce output with the spec block
        [Fact]
        public void Build_EmptyGameDefFields_ProducesCharacterSpec()
        {
            var emptyDef = new GameDefinition("", "", "", "");
            var result = SessionSystemPromptBuilder.BuildPlayerAvatar("player", emptyDef);
            Assert.Contains(SessionSystemPromptBuilder.CharacterSpecHeader, result);
            Assert.Contains("player", result);
        }

        #endregion

        #region Edge case: very large prompts

        // What: Edge case — large character prompts are not truncated
        [Fact]
        public void Build_LargePrompts_NotTruncated()
        {
            var largePlayer = new string('A', 10000);
            var result = SessionSystemPromptBuilder.BuildPlayerAvatar(largePlayer);
            Assert.Contains(largePlayer, result);
        }

        #endregion

        #region Edge case: LoadFrom missing keys

        // What: Error condition — missing datee_role_description throws naming key
        [Fact]
        public void LoadFrom_MissingDateeRole_ThrowsFormatExceptionNamingKey()
        {
            var yaml = @"
name: Test
game_master_prompt: gm
player_avatar_role_description: p
global_dc_bias: 0
horniness_time_modifiers:
  morning: 3
  afternoon: 0
  evening: 2
  overnight: 5
";
            var ex = Assert.Throws<FormatException>(() =>
                GameDefinition.LoadFrom(yaml));
            Assert.Contains("datee_role_description", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        // What: Error condition — completely empty YAML
        [Fact]
        public void LoadFrom_EmptyString_ThrowsFormatException()
        {
            Assert.Throws<FormatException>(() =>
                GameDefinition.LoadFrom(""));
        }

        #endregion

        #region Cross-cutting: shared base identical except character spec (#1124)

        // What: the two sessions share ONE template; only the spec block differs.
        [Fact]
        public void Build_SharedBaseIdentical_OnlyCharacterSpecDiffers()
        {
            var playerAvatarPrompt = "PLAYER_UNIQUE_MARKER_111";
            var dateePrompt = "DATEE_UNIQUE_MARKER_222";
            var def = GameDefinition.PinderDefaults;

            var playerResult = SessionSystemPromptBuilder.BuildPlayerAvatar(playerAvatarPrompt, def);
            var dateeResult = SessionSystemPromptBuilder.BuildDatee(dateePrompt, def);

            var header = SessionSystemPromptBuilder.CharacterSpecHeader;
            var playerBase = playerResult.Substring(0, playerResult.IndexOf(header, StringComparison.Ordinal));
            var dateeBase = dateeResult.Substring(0, dateeResult.IndexOf(header, StringComparison.Ordinal));

            Assert.Equal(dateeBase, playerBase);

            Assert.Contains("PLAYER_UNIQUE_MARKER_111", playerResult);
            Assert.DoesNotContain("DATEE_UNIQUE_MARKER_222", playerResult);
            Assert.Contains("DATEE_UNIQUE_MARKER_222", dateeResult);
            Assert.DoesNotContain("PLAYER_UNIQUE_MARKER_111", dateeResult);
        }

        #endregion

        #region Custom GameDefinition flows through builders

        // What: Custom GameDefinition values appear instead of PinderDefaults
        [Fact]
        public void Build_CustomGameDef_OverridesPinderDefaults()
        {
            var custom = new GameDefinition(
                "CustomGame",
                "CUSTOM_GM_PROMPT_TEXT CUSTOM_DOCTRINE",
                "CUSTOM_PLAYER_ROLE",
                "CUSTOM_DATEE_ROLE");

            var result = SessionSystemPromptBuilder.BuildPlayerAvatar("p", custom);

            Assert.Contains("CUSTOM_GM_PROMPT_TEXT", result);
            Assert.Contains("CUSTOM_DOCTRINE", result);
            // Ensure Pinder defaults are NOT present
            Assert.DoesNotContain("comedy dating RPG", result);
        }

        #endregion
    }
}
