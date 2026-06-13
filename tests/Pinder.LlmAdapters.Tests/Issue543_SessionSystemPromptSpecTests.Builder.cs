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

        // What: AC3 — PinderDefaults.PlayerRoleDescription describes player role
        // Mutation: would catch if player role was empty
        [Fact]
        public void PinderDefaults_PlayerRoleDescriptionIsNotEmpty()
        {
            Assert.False(string.IsNullOrWhiteSpace(GameDefinition.PinderDefaults.PlayerRoleDescription));
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
            Assert.NotNull(gd.PlayerRoleDescription);
            Assert.NotNull(gd.DateeRoleDescription);
            Assert.NotNull(gd.NarrativeDoctrine);
        }

        #endregion

        #region AC4: SessionSystemPromptBuilder.Build — 5 sections in order

        // What: AC4 — Build produces 5 section headers
        // Mutation: would catch if any section header was missing
        [Fact]
        public void Build_ContainsAllFiveSectionHeaders()
        {
            var result = SessionSystemPromptBuilder.Build("player text", "datee text");
            Assert.Contains("== GAME VISION ==", result);
            Assert.Contains("== WORLD RULES ==", result);
            Assert.Contains("== PLAYER CHARACTER ==", result);
            Assert.Contains("== DATEE CHARACTER ==", result);
            Assert.Contains("== NARRATIVE DOCTRINE ==", result);
        }

        // What: AC4 — Sections are in correct order: Vision < World < Player < Datee < Meta
        // Mutation: would catch if section order was swapped (e.g. player before world)
        [Fact]
        public void Build_SectionsInCorrectOrder()
        {
            var result = SessionSystemPromptBuilder.Build("player text", "datee text");
            var visionIdx = result.IndexOf("== GAME VISION ==");
            var worldIdx = result.IndexOf("== WORLD RULES ==");
            var playerIdx = result.IndexOf("== PLAYER CHARACTER ==");
            var dateeIdx = result.IndexOf("== DATEE CHARACTER ==");
            var metaIdx = result.IndexOf("== NARRATIVE DOCTRINE ==");

            // Variable character sections come LAST: static game/doctrine material first,
            // then PLAYER CHARACTER and DATEE CHARACTER at the tail.
            Assert.True(visionIdx < worldIdx, "GAME VISION must precede WORLD RULES");
            Assert.True(worldIdx < metaIdx, "WORLD RULES must precede NARRATIVE DOCTRINE");
            Assert.True(metaIdx < playerIdx, "NARRATIVE DOCTRINE must precede PLAYER CHARACTER");
            Assert.True(playerIdx < dateeIdx, "PLAYER CHARACTER must precede DATEE CHARACTER");
        }

        // What: AC4 — GAME VISION section sourced from gameDef.Vision
        // Mutation: would catch if Vision content was not placed in correct section
        [Fact]
        public void Build_GameVisionSectionContainsGameDefVision()
        {
            var custom = new GameDefinition(
                "G", "UNIQUE_VISION_XYZ", "W", "P", "O", "ND");
            var result = SessionSystemPromptBuilder.Build("player", "datee", custom);

            var visionIdx = result.IndexOf("== GAME VISION ==");
            var worldIdx = result.IndexOf("== WORLD RULES ==");
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
            var result = SessionSystemPromptBuilder.Build("player", "datee", custom);

            var worldIdx = result.IndexOf("== WORLD RULES ==");
            var playerIdx = result.IndexOf("== PLAYER CHARACTER ==");
            var worldSection = result.Substring(worldIdx, playerIdx - worldIdx);
            Assert.Contains("UNIQUE_WORLD_ABC", worldSection);
        }

        // What: AC4 — PLAYER CHARACTER section contains playerAvatarPrompt verbatim
        // Mutation: would catch if playerAvatarPrompt was modified, trimmed, or summarized
        [Fact]
        public void Build_PlayerSectionContainsPlayerPromptVerbatim()
        {
            var playerText = "You are Velvet. Lowercase-with-intent. Ironic. Level 7 Veteran.";
            var result = SessionSystemPromptBuilder.Build(playerText, "opp");

            var playerIdx = result.IndexOf("== PLAYER CHARACTER ==");
            var dateeIdx = result.IndexOf("== DATEE CHARACTER ==");
            var playerSection = result.Substring(playerIdx, dateeIdx - playerIdx);
            Assert.Contains(playerText, playerSection);
        }

        // What: AC4 — DATEE CHARACTER section contains dateePrompt verbatim
        // Mutation: would catch if dateePrompt was modified or placed in player section
        [Fact]
        public void Build_DateeSectionContainsDateePromptVerbatim()
        {
            var dateeText = "You are Sable. Fast-talking. Uses omg and emoji. Level 5.";
            var result = SessionSystemPromptBuilder.Build("player", dateeText);

            // DATEE CHARACTER is now the final section; it runs to the end of the prompt.
            var dateeIdx = result.IndexOf("== DATEE CHARACTER ==");
            var dateeSection = result.Substring(dateeIdx);
            Assert.Contains(dateeText, dateeSection);
        }

        // What: AC4 — NARRATIVE DOCTRINE section contains the merged doctrine body
        // Mutation: would catch if NarrativeDoctrine was omitted from the doctrine section
        [Fact]
        public void Build_MetaContractSectionContainsBothMetaAndWritingRules()
        {
            var custom = new GameDefinition(
                "G", "V", "W", "P", "O",
                "UNIQUE_META_CONTRACT_123 UNIQUE_WRITING_RULES_456");

            var result = SessionSystemPromptBuilder.Build("p", "o", custom);
            var metaIdx = result.IndexOf("== NARRATIVE DOCTRINE ==");
            var afterMeta = result.Substring(metaIdx);
            Assert.Contains("UNIQUE_META_CONTRACT_123", afterMeta);
            Assert.Contains("UNIQUE_WRITING_RULES_456", afterMeta);
        }

        #endregion

        #region AC5: Unit test — prompt contains both character names, game vision, world rules

        // What: AC5 — full integration: Build with known inputs contains all expected content
        // Mutation: would catch if any of the 5 content sources was dropped
        [Fact]
        public void Build_WithKnownInputs_ContainsAllContent()
        {
            var playerAvatarPrompt = "You are Velvet. Lowercase-with-intent. Ironic. Level 7 Veteran.";
            var dateePrompt = "You are Sable. Fast-talking. Uses omg and emoji. Level 5 Journeyman.";
            var gameDef = GameDefinition.PinderDefaults;

            var result = SessionSystemPromptBuilder.Build(playerAvatarPrompt, dateePrompt, gameDef);

            // Player prompt appears verbatim
            Assert.Contains(playerAvatarPrompt, result);
            // Datee prompt appears verbatim
            Assert.Contains(dateePrompt, result);
            // Game vision content present
            Assert.Contains("comedy dating RPG", result);
            // World description content present
            Assert.False(string.IsNullOrWhiteSpace(
                GameDefinition.PinderDefaults.WorldDescription));
            Assert.Contains(gameDef.WorldDescription.Trim(), result);
            // Meta contract content present
            Assert.Contains("break character", result);
        }

        #endregion

        #region Edge cases: null gameDef, empty prompts, null prompts

        // What: Edge case — null gameDef defaults to PinderDefaults
        // Mutation: would catch if null gameDef threw instead of using defaults
        [Fact]
        public void Build_NullGameDef_UsesPinderDefaults()
        {
            var result = SessionSystemPromptBuilder.Build("player", "datee", null);
            // Should use PinderDefaults, which contains "Pinder" and "comedy dating RPG"
            Assert.Contains("comedy dating RPG", result);
            Assert.Contains("== GAME VISION ==", result);
        }

        // What: Edge case — null gameDef (omitted param) defaults to PinderDefaults
        // Mutation: would catch if default param value was not null/PinderDefaults
        [Fact]
        public void Build_OmittedGameDef_UsesPinderDefaults()
        {
            var result = SessionSystemPromptBuilder.Build("player", "datee");
            Assert.Contains("comedy dating RPG", result);
        }

        // What: Edge case — null playerAvatarPrompt throws ArgumentNullException
        // Mutation: would catch if null check on playerAvatarPrompt was removed
        [Fact]
        public void Build_NullPlayerPrompt_ThrowsArgumentNullException()
        {
            var ex = Assert.Throws<ArgumentNullException>(() =>
                SessionSystemPromptBuilder.Build(null!, "datee"));
            Assert.Equal("playerAvatarPrompt", ex.ParamName);
        }

        // What: Edge case — null dateePrompt throws ArgumentNullException
        // Mutation: would catch if null check on dateePrompt was removed
        [Fact]
        public void Build_NullDateePrompt_ThrowsArgumentNullException()
        {
            var ex = Assert.Throws<ArgumentNullException>(() =>
                SessionSystemPromptBuilder.Build("player", null!));
            Assert.Equal("dateePrompt", ex.ParamName);
        }

        // What: Edge case — empty string prompts produce valid output with section headers
        // Mutation: would catch if empty strings were treated as null
        [Fact]
        public void Build_EmptyPrompts_ProducesAllSections()
        {
            var result = SessionSystemPromptBuilder.Build("", "");
            Assert.Contains("== GAME VISION ==", result);
            Assert.Contains("== WORLD RULES ==", result);
            Assert.Contains("== PLAYER CHARACTER ==", result);
            Assert.Contains("== DATEE CHARACTER ==", result);
            Assert.Contains("== NARRATIVE DOCTRINE ==", result);
        }

        // What: Edge case — empty GameDefinition fields produce sections with empty bodies
        // Mutation: would catch if empty fields threw or were skipped
        [Fact]
        public void Build_EmptyGameDefFields_ProducesAllSections()
        {
            var emptyDef = new GameDefinition("", "", "", "", "", "");
            var result = SessionSystemPromptBuilder.Build("player", "datee", emptyDef);
            Assert.Contains("== GAME VISION ==", result);
            Assert.Contains("== NARRATIVE DOCTRINE ==", result);
            Assert.Contains("player", result);
            Assert.Contains("datee", result);
        }

        #endregion

        #region Edge case: very large prompts

        // What: Edge case — large character prompts are not truncated
        // Mutation: would catch if implementation truncated long prompts
        [Fact]
        public void Build_LargePrompts_NotTruncated()
        {
            var largePlayer = new string('A', 10000);
            var largeDatee = new string('B', 10000);
            var result = SessionSystemPromptBuilder.Build(largePlayer, largeDatee);
            Assert.Contains(largePlayer, result);
            Assert.Contains(largeDatee, result);
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
player_role_description: p
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

        #region Cross-cutting: Build does not mix player/datee sections

        // What: Player prompt must not appear in datee section and vice versa
        // Mutation: would catch if playerAvatarPrompt and dateePrompt params were swapped
        [Fact]
        public void Build_PlayerAndDateePromptsInCorrectSections()
        {
            var playerAvatarPrompt = "PLAYER_UNIQUE_MARKER_111";
            var dateePrompt = "DATEE_UNIQUE_MARKER_222";
            var result = SessionSystemPromptBuilder.Build(playerAvatarPrompt, dateePrompt);

            var playerIdx = result.IndexOf("== PLAYER CHARACTER ==");
            var dateeIdx = result.IndexOf("== DATEE CHARACTER ==");

            // PLAYER CHARACTER and DATEE CHARACTER are the final two sections.
            var playerSection = result.Substring(playerIdx, dateeIdx - playerIdx);
            var dateeSection = result.Substring(dateeIdx);

            // Player marker in player section, not in datee section
            Assert.Contains("PLAYER_UNIQUE_MARKER_111", playerSection);
            Assert.DoesNotContain("PLAYER_UNIQUE_MARKER_111", dateeSection);

            // Datee marker in datee section, not in player section
            Assert.Contains("DATEE_UNIQUE_MARKER_222", dateeSection);
            Assert.DoesNotContain("DATEE_UNIQUE_MARKER_222", playerSection);
        }

        #endregion

        #region Custom GameDefinition flows through Build

        // What: Custom GameDefinition values appear instead of PinderDefaults
        // Mutation: would catch if Build always used PinderDefaults regardless of gameDef param
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

            var result = SessionSystemPromptBuilder.Build("p", "o", custom);

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