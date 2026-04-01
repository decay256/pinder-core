using System.Collections.Generic;
using Pinder.Core.Conversation;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;
using Pinder.LlmAdapters.Anthropic;
using Pinder.LlmAdapters.Anthropic.Dto;
using Xunit;

namespace Pinder.LlmAdapters.Tests.Anthropic
{
    /// <summary>
    /// Tests for Issue #241: Legendary fail delivery generates wrong character voice.
    /// Verifies that failure delivery uses player-only system blocks and
    /// explicitly identifies the player character in the prompt.
    /// </summary>
    public class Issue241_LegendaryFailVoiceTests
    {
        // ==========================================================
        // AC1: FailureDeliveryInstruction explicitly identifies player role
        // ==========================================================

        [Fact]
        public void AC1_FailureInstruction_contains_player_name_token()
        {
            var instruction = PromptTemplates.FailureDeliveryInstruction;
            Assert.Contains("{player_name}", instruction);
        }

        [Fact]
        public void AC1_FailureInstruction_contains_write_as_player_framing()
        {
            var instruction = PromptTemplates.FailureDeliveryInstruction;
            Assert.Contains("You are writing as {player_name}", instruction);
        }

        [Fact]
        public void AC1_FailureInstruction_contains_do_not_write_as_opponent()
        {
            var instruction = PromptTemplates.FailureDeliveryInstruction;
            Assert.Contains("Do NOT write as the opponent", instruction);
        }

        [Fact]
        public void AC1_FailureInstruction_identity_framing_appears_before_tier_instructions()
        {
            var instruction = PromptTemplates.FailureDeliveryInstruction;
            int identityIndex = instruction.IndexOf("You are writing as {player_name}");
            int tierIndex = instruction.IndexOf("Failure principle:");
            Assert.True(identityIndex >= 0, "Identity framing not found");
            Assert.True(tierIndex >= 0, "Tier instructions not found");
            Assert.True(identityIndex < tierIndex,
                "Identity framing must appear before failure tier instructions");
        }

        // ==========================================================
        // AC1 (defense in depth): SuccessDeliveryInstruction also has player_name
        // ==========================================================

        [Fact]
        public void AC1_SuccessInstruction_contains_player_name_token()
        {
            var instruction = PromptTemplates.SuccessDeliveryInstruction;
            Assert.Contains("{player_name}", instruction);
        }

        // ==========================================================
        // AC2: BuildPlayerOnlySystemBlocks returns single block
        // ==========================================================

        [Fact]
        public void AC2_BuildPlayerOnlySystemBlocks_returns_single_block()
        {
            var blocks = CacheBlockBuilder.BuildPlayerOnlySystemBlocks("You are Sable...");
            Assert.Single(blocks);
        }

        [Fact]
        public void AC2_BuildPlayerOnlySystemBlocks_contains_player_prompt_text()
        {
            const string prompt = "You are Sable, a Scorpio sun with Love Bomber energy.";
            var blocks = CacheBlockBuilder.BuildPlayerOnlySystemBlocks(prompt);
            Assert.Equal("text", blocks[0].Type);
            Assert.Equal(prompt, blocks[0].Text);
        }

        [Fact]
        public void AC2_BuildPlayerOnlySystemBlocks_has_ephemeral_cache_control()
        {
            var blocks = CacheBlockBuilder.BuildPlayerOnlySystemBlocks("prompt");
            Assert.NotNull(blocks[0].CacheControl);
            Assert.Equal("ephemeral", blocks[0].CacheControl!.Type);
        }

        [Fact]
        public void AC2_BuildPlayerOnlySystemBlocks_throws_on_null()
        {
            Assert.Throws<System.ArgumentNullException>(() =>
                CacheBlockBuilder.BuildPlayerOnlySystemBlocks(null!));
        }

        // ==========================================================
        // AC4: BuildDeliveryPrompt substitutes {player_name} in failure path
        // ==========================================================

        [Fact]
        public void AC4_Legendary_fail_delivery_prompt_contains_player_name_in_identity()
        {
            var history = new List<(string, string)>
            {
                ("Sable", "hey there"),
                ("Brick", "hello")
            };
            var option = new DialogueOption(
                StatType.Charm, "omg you actually work in M&A?? that's so hot",
                callbackTurnNumber: null, comboName: null,
                hasTellBonus: false, hasWeaknessWindow: false);

            string prompt = SessionDocumentBuilder.BuildDeliveryPrompt(
                history, option, FailureTier.Legendary, beatDcBy: 0,
                activeTrapInstructions: null,
                playerName: "Sable", opponentName: "Brick");

            // Player identity must be substituted (not raw token)
            Assert.Contains("You are writing as Sable", prompt);
            Assert.Contains("The failure corrupts what Sable says", prompt);
            Assert.DoesNotContain("{player_name}", prompt);
        }

        [Fact]
        public void AC4_Success_delivery_prompt_contains_player_name()
        {
            var history = new List<(string, string)>
            {
                ("Sable", "hey there"),
                ("Brick", "hello")
            };
            var option = new DialogueOption(
                StatType.Charm, "you're really interesting",
                callbackTurnNumber: null, comboName: null,
                hasTellBonus: false, hasWeaknessWindow: false);

            string prompt = SessionDocumentBuilder.BuildDeliveryPrompt(
                history, option, FailureTier.None, beatDcBy: 5,
                activeTrapInstructions: null,
                playerName: "Sable", opponentName: "Brick");

            Assert.Contains("Write as Sable", prompt);
            Assert.DoesNotContain("{player_name}", prompt);
        }

        [Fact]
        public void AC4_Catastrophe_fail_delivery_prompt_contains_player_identity()
        {
            var history = new List<(string, string)>();
            var option = new DialogueOption(
                StatType.Honesty, "I think we could be great",
                callbackTurnNumber: null, comboName: null,
                hasTellBonus: false, hasWeaknessWindow: false);

            string prompt = SessionDocumentBuilder.BuildDeliveryPrompt(
                history, option, FailureTier.Catastrophe, beatDcBy: 0,
                activeTrapInstructions: null,
                playerName: "Blaze", opponentName: "Jade");

            Assert.Contains("You are writing as Blaze", prompt);
            Assert.Contains("Do NOT write as the opponent", prompt);
        }

        [Fact]
        public void AC4_Failure_prompt_with_active_traps_still_has_player_identity()
        {
            var history = new List<(string, string)>();
            var option = new DialogueOption(
                StatType.Wit, "clever joke",
                callbackTurnNumber: null, comboName: null,
                hasTellBonus: false, hasWeaknessWindow: false);

            string prompt = SessionDocumentBuilder.BuildDeliveryPrompt(
                history, option, FailureTier.TropeTrap, beatDcBy: 0,
                activeTrapInstructions: new[] { "Overthinking trap active" },
                playerName: "Sable", opponentName: "Brick");

            Assert.Contains("You are writing as Sable", prompt);
            Assert.Contains("Overthinking trap active", prompt);
        }
    }
}
