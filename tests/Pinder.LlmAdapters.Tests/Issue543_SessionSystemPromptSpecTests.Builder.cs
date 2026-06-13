using System;
using Xunit;

namespace Pinder.LlmAdapters.Tests
{
    public partial class Issue543_SessionSystemPromptSpecTests
    {
        #region AC3: GameDefinition.PinderDefaults hardcoded fallback

        // What: AC3 — PinderDefaults.Name is "Pinder"
        // Mutation: would catch if Name was set to wrong value or empty
        [Fact]
        public void PinderDefaults_NameIsPinder()
        {
            Assert.Equal("Pinder", GameDefinition.PinderDefaults.Name);
        }

        // What: AC3 — PinderDefaults.Vision describes comedy dating RPG with sentient penises
        // Mutation: would catch if Vision lacked core game identity
        [Fact]
        public void PinderDefaults_VisionDescribesComedyDatingRpg()
        {
            var vision = GameDefinition.PinderDefaults.Vision;
            Assert.Contains("comedy dating RPG", vision);
            Assert.Contains("sentient penis", vision);
        }

        // What: AC3 — PinderDefaults.WorldDescription describes absurdist world
        // Mutation: would catch if WorldDescription was empty or generic
        [Fact]
        public void PinderDefaults_WorldDescriptionIsNotEmpty()
        {
            Assert.False(string.IsNullOrWhiteSpace(GameDefinition.PinderDefaults.WorldDescription));
        }

        // What: AC3 — PinderDefaults.PlayerAvatarRoleDescription describes player role
        // Mutation: would catch if player role was empty
        [Fact]
        public void PinderDefaults_PlayerAvatarRoleDescriptionIsNotEmpty()
        {
            Assert.False(string.IsNullOrWhiteSpace(GameDefinition.PinderDefaults.PlayerAvatarRoleDescription));
        }

        // What: AC3 — PinderDefaults.DateeRoleDescription describes datee role
        // Mutation: would catch if datee role was empty
        [Fact]
        public void PinderDefaults_DateeRoleDescriptionMentionsDatee()
        {
            Assert.Contains("datee", GameDefinition.PinderDefaults.DateeRoleDescription,
                StringComparison.OrdinalIgnoreCase);
        }

        // What: AC3 — PinderDefaults.NarrativeDoctrine specifies never break character
        // Mutation: would catch if NarrativeDoctrine lacked immersion rules
        [Fact]
        public void PinderDefaults_MetaContractMentionsBreakCharacter()
        {
            Assert.Contains("break character", GameDefinition.PinderDefaults.NarrativeDoctrine,
                StringComparison.OrdinalIgnoreCase);
        }

        // What: AC3 — PinderDefaults.NarrativeDoctrine specifies texting register
        // Mutation: would catch if NarrativeDoctrine lacked texting register directive
        [Fact]
        public void PinderDefaults_WritingRulesMentionsTextingRegister()
        {
            Assert.Contains("texting register", GameDefinition.PinderDefaults.NarrativeDoctrine,
                StringComparison.OrdinalIgnoreCase);
        }

        // What: AC3 — PinderDefaults returns the same instance (static property)
        // Mutation: would catch if PinderDefaults created a new instance on each access
        [Fact]
        public void PinderDefaults_ReturnsSameInstance()
        {
            var a = GameDefinition.PinderDefaults;
            var b = GameDefinition.PinderDefaults;
            Assert.Same(a, b);
        }

        // What: AC3 — All 7 fields are non-null
        // Mutation: would catch if any default field was accidentally left null
        [Fact]
        public void PinderDefaults_AllFieldsNonNull()
        {
            var gd = GameDefinition.PinderDefaults;
            Assert.NotNull(gd.Name);
            Assert.NotNull(gd.Vision);
            Assert.NotNull(gd.WorldDescription);
            Assert.NotNull(gd.PlayerAvatarRoleDescription);
            Assert.NotNull(gd.DateeRoleDescription);
            Assert.NotNull(gd.NarrativeDoctrine);
        }

        #endregion

        #region AC4: Shared GM template — sections in order (#1124)

        // What: AC4 — the shared GM base produces its core section headers.
        // Mutation: would catch if any section header was missing.
        [Fact]
        public void Build_ContainsSharedSectionHeaders()
        {
            var result = SessionSystemPromptBuilder.BuildPlayerAvatar("player text");
            Assert.Contains("== GAME MASTER ==", result);
            Assert.Contains("== GAME VISION ==", result);
            Assert.Contains("== WORLD RULES ==", result);
            Assert.Contains("== NARRATIVE DOCTRINE ==", result);
            Assert.Contains(SessionSystemPromptBuilder.CharacterSpecHeader, result);
        }

        // What: AC4 — static GM base first, character-spec block last.
        // Mutation: would catch if section order was swapped.
        [Fact]
        public void Build_SectionsInCorrectOrder()
        {
            var result = SessionSystemPromptBuilder.BuildPlayerAvatar("player text");
            var gmIdx = result.IndexOf("== GAME MASTER ==", StringComparison.Ordinal);
            var visionIdx = result.IndexOf("== GAME VISION ==", StringComparison.Ordinal);
            var worldIdx = result.IndexOf("== WORLD RULES ==", StringComparison.Ordinal);
            var metaIdx = result.IndexOf("== NARRATIVE DOCTRINE ==", StringComparison.Ordinal);
            var specIdx = result.IndexOf(SessionSystemPromptBuilder.CharacterSpecHeader, StringComparison.Ordinal);

            Assert.True(gmIdx < visionIdx, "GAME MASTER must precede GAME VISION");
            Assert.True(visionIdx < worldIdx, "GAME VISION must precede WORLD RULES");
            Assert.True(worldIdx < metaIdx, "WORLD RULES must precede NARRATIVE DOCTRINE");
            Assert.True(metaIdx < specIdx, "NARRATIVE DOCTRINE must precede the character-spec block");
        }

        // What: AC4 — GAME VISION section sourced from gameDef.Vision
        // Mutation: would catch if Vision content was not placed in correct section
        [Fact]
        public void Build_GameVisionSectionContainsGameDefVision()
        {
            var custom = new GameDefinition(
                "G", "UNIQUE_VISION_XYZ", "W", "P", "O", "ND");
            var result = SessionSystemPromptBuilder.BuildPlayerAvatar("player", custom);

            var visionIdx = result.IndexOf("== GAME VISION ==", StringComparison.Ordinal);
            var worldIdx = result.IndexOf("== WORLD RULES ==", StringComparison.Ordinal);
            var visionSection = result.Substring(visionIdx, worldIdx - visionIdx);
            Assert.Contains("UNIQUE_VISION_XYZ", visionSection);
        }

        // What: AC4 — WORLD RULES section sourced from gameDef.WorldDescription
        // Mutation: would catch if WorldDescription was placed in wrong section
        [Fact]
        public void Build_WorldRulesSectionContainsWorldDescription()
        {
            var custom = new GameDefinition(
                "G", "V", "UNIQUE_WORLD_ABC", "P", "O", "ND");
            var result = SessionSystemPromptBuilder.BuildPlayerAvatar("player", custom);

            var worldIdx = result.IndexOf("== WORLD RULES ==", StringComparison.Ordinal);
            var metaIdx = result.IndexOf("== NARRATIVE DOCTRINE ==", StringComparison.Ordinal);
            var worldSection = result.Substring(worldIdx, metaIdx - worldIdx);
            Assert.Contains("UNIQUE_WORLD_ABC", worldSection);
        }

        // What: AC4 — character-spec block contains the player avatar prompt verbatim
        // Mutation: would catch if playerAvatarPrompt was modified, trimmed, or summarized
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
        // Mutation: would catch if dateePrompt was modified or placed elsewhere
        [Fact]
        public void Build_CharacterSpecContainsDateePromptVerbatim()
        {
            var dateeText = "You are Sable. Fast-talking. Uses omg and emoji. Level 5.";
            var result = SessionSystemPromptBuilder.BuildDatee(dateeText);

            var specIdx = result.IndexOf(SessionSystemPromptBuilder.CharacterSpecHeader, StringComparison.Ordinal);
            var specSection = result.Substring(specIdx);
            Assert.Contains(dateeText, specSection);
        }

        // What: AC4 — NARRATIVE DOCTRINE section contains the merged doctrine body
        // Mutation: would catch if NarrativeDoctrine was omitted from the doctrine section
        [Fact]
        public void Build_MetaContractSectionContainsBothMetaAndWritingRules()
        {
            var custom = new GameDefinition(
                "G", "V", "W", "P", "O",
                "UNIQUE_META_CONTRACT_123 UNIQUE_WRITING_RULES_456");

            var result = SessionSystemPromptBuilder.BuildPlayerAvatar("p", custom);
            var metaIdx = result.IndexOf("== NARRATIVE DOCTRINE ==", StringComparison.Ordinal);
            var afterMeta = result.Substring(metaIdx);
            Assert.Contains("UNIQUE_META_CONTRACT_123", afterMeta);
            Assert.Contains("UNIQUE_WRITING_RULES_456", afterMeta);
        }

        #endregion

        #region AC5: Unit test — prompt contains character name, game vision, world rules

        // What: AC5 — full integration: a built prompt contains the expected content sources
        // Mutation: would catch if any of the content sources was dropped
        [Fact]
        public void Build_WithKnownInputs_ContainsAllContent()
        {
            var playerAvatarPrompt = "You are Velvet. Lowercase-with-intent. Ironic. Level 7 Veteran.";
            var gameDef = GameDefinition.PinderDefaults;

            var result = SessionSystemPromptBuilder.BuildPlayerAvatar(playerAvatarPrompt, gameDef);

            // Character prompt appears verbatim
            Assert.Contains(playerAvatarPrompt, result);
            // Game vision content present
            Assert.Contains("comedy dating RPG", result);
            // World description content present
            Assert.False(string.IsNullOrWhiteSpace(
                GameDefinition.PinderDefaults.WorldDescription));
            Assert.Contains(gameDef.WorldDescription.Trim(), result);
            // Meta contract content present
            Assert.Contains("break character", result);
            // GM puppeteer framing present
            Assert.Contains("== GAME MASTER ==", result);
        }

        #endregion

        #region Edge cases: null gameDef, empty prompts, null prompts

        // What: Edge case — null gameDef defaults to PinderDefaults
        // Mutation: would catch if null gameDef threw instead of using defaults
        [Fact]
        public void Build_NullGameDef_UsesPinderDefaults()
        {
            var result = SessionSystemPromptBuilder.BuildPlayerAvatar("player", null);
            Assert.Contains("comedy dating RPG", result);
            Assert.Contains("== GAME VISION ==", result);
        }

        // What: Edge case — null gameDef (omitted param) defaults to PinderDefaults
        // Mutation: would catch if default param value was not null/PinderDefaults
        [Fact]
        public void Build_OmittedGameDef_UsesPinderDefaults()
        {
            var result = SessionSystemPromptBuilder.BuildPlayerAvatar("player");
            Assert.Contains("comedy dating RPG", result);
        }

        // What: Edge case — null player prompt throws ArgumentNullException
        // Mutation: would catch if null check on playerAvatarPrompt was removed
        [Fact]
        public void Build_NullPlayerPrompt_ThrowsArgumentNullException()
        {
            var ex = Assert.Throws<ArgumentNullException>(() =>
                SessionSystemPromptBuilder.BuildPlayerAvatar(null!));
            Assert.Equal("playerAvatarPrompt", ex.ParamName);
        }

        // What: Edge case — null datee prompt throws ArgumentNullException
        // Mutation: would catch if null check on dateePrompt was removed
        [Fact]
        public void Build_NullDateePrompt_ThrowsArgumentNullException()
        {
            var ex = Assert.Throws<ArgumentNullException>(() =>
                SessionSystemPromptBuilder.BuildDatee(null!));
            Assert.Equal("dateePrompt", ex.ParamName);
        }

        // What: Edge case — empty string prompts produce valid output with section headers
        // Mutation: would catch if empty strings were treated as null
        [Fact]
        public void Build_EmptyPrompts_ProducesAllSections()
        {
            var result = SessionSystemPromptBuilder.BuildPlayerAvatar("");
            Assert.Contains("== GAME MASTER ==", result);
            Assert.Contains("== GAME VISION ==", result);
            Assert.Contains("== WORLD RULES ==", result);
            Assert.Contains("== NARRATIVE DOCTRINE ==", result);
            Assert.Contains(SessionSystemPromptBuilder.CharacterSpecHeader, result);
        }

        // What: Edge case — empty GameDefinition fields produce sections with empty bodies
        // Mutation: would catch if empty fields threw or were skipped
        [Fact]
        public void Build_EmptyGameDefFields_ProducesAllSections()
        {
            var emptyDef = new GameDefinition("", "", "", "", "", "");
            var result = SessionSystemPromptBuilder.BuildPlayerAvatar("player", emptyDef);
            Assert.Contains("== GAME VISION ==", result);
            Assert.Contains("== NARRATIVE DOCTRINE ==", result);
            Assert.Contains("player", result);
        }

        #endregion

        #region Edge case: very large prompts

        // What: Edge case — large character prompts are not truncated
        // Mutation: would catch if implementation truncated long prompts
        [Fact]
        public void Build_LargePrompts_NotTruncated()
        {
            var largePlayer = new string('A', 10000);
            var result = SessionSystemPromptBuilder.BuildPlayerAvatar(largePlayer);
            Assert.Contains(largePlayer, result);
        }

        #endregion

        #region Edge case: LoadFrom missing multiple keys

        // What: Error condition — missing all keys except name
        // Mutation: would catch if validation only checked first key
        [Fact]
        public void LoadFrom_MissingWritingRules_ThrowsFormatExceptionNamingKey()
        {
            var yaml = @"
name: Test
vision: v
world_description: w
player_avatar_role_description: p
datee_role_description: o
global_dc_bias: 0
horniness_time_modifiers:
  morning: 3
  afternoon: 0
  evening: 2
  overnight: 5
";
            var ex = Assert.Throws<FormatException>(() =>
                GameDefinition.LoadFrom(yaml));
            Assert.Contains("narrative_doctrine", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        // What: Error condition — completely empty YAML
        // Mutation: would catch if empty content was accepted without error
        [Fact]
        public void LoadFrom_EmptyString_ThrowsFormatException()
        {
            Assert.Throws<FormatException>(() =>
                GameDefinition.LoadFrom(""));
        }

        #endregion

        #region Cross-cutting: shared base identical except character spec (#1124)

        // What: the two sessions share ONE template; only the spec block differs.
        // Mutation: would catch if the shared base diverged between sessions or
        // if player/datee prompts leaked across sessions.
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
        // Mutation: would catch if builder always used PinderDefaults regardless of gameDef param
        [Fact]
        public void Build_CustomGameDef_OverridesPinderDefaults()
        {
            var custom = new GameDefinition(
                "CustomGame",
                "CUSTOM_VISION_TEXT",
                "CUSTOM_WORLD_TEXT",
                "CUSTOM_PLAYER_ROLE",
                "CUSTOM_DATEE_ROLE",
                "CUSTOM_META_CONTRACT CUSTOM_WRITING_RULES");

            var result = SessionSystemPromptBuilder.BuildPlayerAvatar("p", custom);

            Assert.Contains("CUSTOM_VISION_TEXT", result);
            Assert.Contains("CUSTOM_WORLD_TEXT", result);
            Assert.Contains("CUSTOM_META_CONTRACT", result);
            Assert.Contains("CUSTOM_WRITING_RULES", result);
            // Ensure Pinder defaults are NOT present
            Assert.DoesNotContain("comedy dating RPG", result);
        }

        #endregion
    }
}
