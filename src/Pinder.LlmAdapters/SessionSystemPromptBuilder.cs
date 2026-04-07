using System;

namespace Pinder.LlmAdapters
{
    /// <summary>
    /// Assembles a session-level system prompt from character profiles and game definition.
    /// The output contains 5 clearly delineated sections: game vision, world rules,
    /// player character, opponent character, and meta contract (with writing rules).
    /// </summary>
    public static class SessionSystemPromptBuilder
    {
        /// <summary>
        /// Build the full session system prompt.
        /// </summary>
        /// <param name="playerPrompt">
        /// Player's assembled character system prompt (from CharacterProfile.AssembledSystemPrompt).
        /// </param>
        /// <param name="opponentPrompt">
        /// Opponent's assembled character system prompt (from CharacterProfile.AssembledSystemPrompt).
        /// </param>
        /// <param name="gameDef">
        /// Game definition containing vision, world rules, meta contract.
        /// When null, GameDefinition.PinderDefaults is used.
        /// </param>
        /// <returns>A single string containing the full session system prompt.</returns>
        public static string Build(
            string playerPrompt,
            string opponentPrompt,
            GameDefinition? gameDef = null)
        {
            if (playerPrompt == null)
                throw new ArgumentNullException(nameof(playerPrompt));
            if (opponentPrompt == null)
                throw new ArgumentNullException(nameof(opponentPrompt));

            var def = gameDef ?? GameDefinition.PinderDefaults;

            return string.Concat(
                "== GAME VISION ==\n\n",
                def.Vision.TrimEnd(),
                "\n\n== WORLD RULES ==\n\n",
                def.WorldDescription.TrimEnd(),
                "\n\n== PLAYER CHARACTER ==\n\n",
                playerPrompt.TrimEnd(),
                "\n\n== OPPONENT CHARACTER ==\n\n",
                opponentPrompt.TrimEnd(),
                "\n\n== META CONTRACT ==\n\n",
                def.MetaContract.TrimEnd(),
                "\n\n",
                def.WritingRules.TrimEnd(),
                "\n\n== DRAMATIC CRAFT ==\n\n",
                def.DramaticCraft != null ? def.DramaticCraft.BuildSection().TrimEnd() : "",
                string.IsNullOrWhiteSpace(def.TextingPsychology) ? "" : "\n\n== TEXTING PSYCHOLOGY ==\n\n" + def.TextingPsychology.TrimEnd(),
                string.IsNullOrWhiteSpace(def.RevelationOverStatement) ? "" : "\n\n== REVELATION OVER STATEMENT ==\n\n" + def.RevelationOverStatement.TrimEnd(),
                string.IsNullOrWhiteSpace(def.OpponentFriction) ? "" : "\n\n== OPPONENT RESISTANCE ==\n\n" + def.OpponentFriction.TrimEnd(),
                string.IsNullOrWhiteSpace(def.OpponentCuriosity) ? "" : "\n\n== OPPONENT CURIOSITY ==\n\n" + def.OpponentCuriosity.TrimEnd(),
                string.IsNullOrWhiteSpace(def.ConversationArcProgression) ? "" : "\n\n== CONVERSATION ARC ==\n\n" + def.ConversationArcProgression.TrimEnd(),
                "\n");
        }

        /// <summary>
        /// Build a system prompt containing only the player's character profile and game definition.
        /// Used to prevent voice bleed when generating dialogue options or delivering messages.
        /// </summary>
        public static string BuildPlayer(string playerPrompt, GameDefinition? gameDef = null)
        {
            if (playerPrompt == null) throw new ArgumentNullException(nameof(playerPrompt));
            var def = gameDef ?? GameDefinition.PinderDefaults;

            return string.Concat(
                "== GAME VISION ==\n\n",
                def.Vision.TrimEnd(),
                "\n\n== WORLD RULES ==\n\n",
                def.WorldDescription.TrimEnd(),
                "\n\n== PLAYER CHARACTER ==\n\n",
                def.PlayerRoleDescription.TrimEnd(),
                "\n\n",
                playerPrompt.TrimEnd(),
                "\n\n== META CONTRACT ==\n\n",
                def.MetaContract.TrimEnd(),
                "\n\n",
                def.WritingRules.TrimEnd(),
                "\n\n== DRAMATIC CRAFT ==\n\n",
                def.DramaticCraft != null ? def.DramaticCraft.BuildSection().TrimEnd() : "",
                string.IsNullOrWhiteSpace(def.TextingPsychology) ? "" : "\n\n== TEXTING PSYCHOLOGY ==\n\n" + def.TextingPsychology.TrimEnd(),
                string.IsNullOrWhiteSpace(def.RevelationOverStatement) ? "" : "\n\n== REVELATION OVER STATEMENT ==\n\n" + def.RevelationOverStatement.TrimEnd(),
                string.IsNullOrWhiteSpace(def.OpponentFriction) ? "" : "\n\n== OPPONENT RESISTANCE ==\n\n" + def.OpponentFriction.TrimEnd(),
                string.IsNullOrWhiteSpace(def.OpponentCuriosity) ? "" : "\n\n== OPPONENT CURIOSITY ==\n\n" + def.OpponentCuriosity.TrimEnd(),
                string.IsNullOrWhiteSpace(def.ConversationArcProgression) ? "" : "\n\n== CONVERSATION ARC ==\n\n" + def.ConversationArcProgression.TrimEnd(),
                "\n");
        }

        /// <summary>
        /// Build a system prompt containing only the opponent's character profile and game definition.
        /// Used to prevent voice bleed when generating opponent responses.
        /// </summary>
        public static string BuildOpponent(string opponentPrompt, GameDefinition? gameDef = null)
        {
            if (opponentPrompt == null) throw new ArgumentNullException(nameof(opponentPrompt));
            var def = gameDef ?? GameDefinition.PinderDefaults;

            return string.Concat(
                "== GAME VISION ==\n\n",
                def.Vision.TrimEnd(),
                "\n\n== WORLD RULES ==\n\n",
                def.WorldDescription.TrimEnd(),
                "\n\n== OPPONENT CHARACTER ==\n\n",
                def.OpponentRoleDescription.TrimEnd(),
                "\n\n",
                opponentPrompt.TrimEnd(),
                "\n\n== META CONTRACT ==\n\n",
                def.MetaContract.TrimEnd(),
                "\n\n",
                def.WritingRules.TrimEnd(),
                "\n\n== DRAMATIC CRAFT ==\n\n",
                def.DramaticCraft != null ? def.DramaticCraft.BuildSection().TrimEnd() : "",
                string.IsNullOrWhiteSpace(def.TextingPsychology) ? "" : "\n\n== TEXTING PSYCHOLOGY ==\n\n" + def.TextingPsychology.TrimEnd(),
                string.IsNullOrWhiteSpace(def.RevelationOverStatement) ? "" : "\n\n== REVELATION OVER STATEMENT ==\n\n" + def.RevelationOverStatement.TrimEnd(),
                string.IsNullOrWhiteSpace(def.OpponentFriction) ? "" : "\n\n== OPPONENT RESISTANCE ==\n\n" + def.OpponentFriction.TrimEnd(),
                string.IsNullOrWhiteSpace(def.OpponentCuriosity) ? "" : "\n\n== OPPONENT CURIOSITY ==\n\n" + def.OpponentCuriosity.TrimEnd(),
                string.IsNullOrWhiteSpace(def.ConversationArcProgression) ? "" : "\n\n== CONVERSATION ARC ==\n\n" + def.ConversationArcProgression.TrimEnd(),
                "\n");
        }
    }
}
