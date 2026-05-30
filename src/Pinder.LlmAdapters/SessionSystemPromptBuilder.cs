using System;
using Pinder.Core.Text;

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
                // Build() is the legacy joint prompt for callers that want both
                // roles in one system block. Per #867 LESSONS_LEARNED, this method
                // retains all sections (only BuildPlayer is trimmed).
                string.IsNullOrWhiteSpace(def.OpponentFriction) ? "" : "\n\n== OPPONENT RESISTANCE ==\n\n" + def.OpponentFriction.TrimEnd(),
                string.IsNullOrWhiteSpace(def.OpponentCuriosity) ? "" : "\n\n== OPPONENT CURIOSITY ==\n\n" + def.OpponentCuriosity.TrimEnd(),
                string.IsNullOrWhiteSpace(def.ConversationArcProgression) ? "" : "\n\n== CONVERSATION ARC ==\n\n" + def.ConversationArcProgression.TrimEnd(),
                string.IsNullOrWhiteSpace(def.PlayerProbing) ? "" : "\n\n== PLAYER PROBING ==\n\n" + def.PlayerProbing.TrimEnd(),
                "\n");
        }

        /// <summary>
        /// Build a system prompt containing only the player's character profile and game definition.
        /// Used to prevent voice bleed when generating dialogue options or delivering messages.
        /// </summary>
        public static string BuildPlayer(string playerPrompt, GameDefinition? gameDef = null)
        {
            var result = BuildPlayerEx(playerPrompt, gameDef);
            InMemoryPromptTraceService.Instance.RecordTrace("dialogue-options-system", result);
            return result.Text;
        }

        public static PromptTraceResult BuildPlayerEx(string playerPrompt, GameDefinition? gameDef = null)
        {
            if (playerPrompt == null) throw new ArgumentNullException(nameof(playerPrompt));
            var def = gameDef ?? GameDefinition.PinderDefaults;

            var sb = new AnnotatedStringBuilder();

            sb.Append("== GAME VISION ==\n\n");
            sb.AppendLine(def.Vision.TrimEnd(), "game-definition.yaml", "vision");
            sb.Append("\n== WORLD RULES ==\n\n");
            sb.AppendLine(def.WorldDescription.TrimEnd(), "game-definition.yaml", "world_description");
            sb.Append("\n== PLAYER CHARACTER ==\n\n");
            sb.AppendLine(def.PlayerRoleDescription.TrimEnd(), "game-definition.yaml", "player_role_description");
            sb.Append("\n");
            sb.AppendLine(playerPrompt.TrimEnd(), "character-profile", "player-profile");

            sb.Append("\n== META CONTRACT ==\n\n");
            sb.AppendLine(def.MetaContract.TrimEnd(), "game-definition.yaml", "meta_contract");
            sb.Append("\n");
            sb.AppendLine(def.WritingRules.TrimEnd(), "game-definition.yaml", "writing_rules");

            if (def.DramaticCraft != null)
            {
                sb.Append("\n== DRAMATIC CRAFT ==\n\n");
                sb.AppendLine(def.DramaticCraft.BuildSection().TrimEnd(), "game-definition.yaml", "dramatic_craft");
            }

            if (!string.IsNullOrWhiteSpace(def.TextingPsychology))
            {
                sb.Append("\n== TEXTING PSYCHOLOGY ==\n\n");
                sb.AppendLine(def.TextingPsychology.TrimEnd(), "game-definition.yaml", "texting_psychology");
            }

            if (!string.IsNullOrWhiteSpace(def.RevelationOverStatement))
            {
                sb.Append("\n== REVELATION OVER STATEMENT ==\n\n");
                sb.AppendLine(def.RevelationOverStatement.TrimEnd(), "game-definition.yaml", "revelation_over_statement");
            }

            if (!string.IsNullOrWhiteSpace(def.ConversationArcProgression))
            {
                sb.Append("\n== CONVERSATION ARC ==\n\n");
                sb.AppendLine(def.ConversationArcProgression.TrimEnd(), "game-definition.yaml", "conversation_arc_progression");
            }

            if (!string.IsNullOrWhiteSpace(def.PlayerProbing))
            {
                sb.Append("\n== PLAYER PROBING ==\n\n");
                sb.AppendLine(def.PlayerProbing.TrimEnd(), "game-definition.yaml", "player_probing");
            }

            sb.Append("\n");

            return new PromptTraceResult(sb.ToString(), sb.Spans);
        }

        /// <summary>
        /// Build a system prompt containing only the opponent's character profile and game definition.
        /// Used to prevent voice bleed when generating opponent responses.
        /// </summary>
        public static string BuildOpponent(string opponentPrompt, GameDefinition? gameDef = null)
        {
            var result = BuildOpponentEx(opponentPrompt, gameDef);
            InMemoryPromptTraceService.Instance.RecordTrace("opponent-system", result);
            return result.Text;
        }

        public static PromptTraceResult BuildOpponentEx(string opponentPrompt, GameDefinition? gameDef = null)
        {
            if (opponentPrompt == null) throw new ArgumentNullException(nameof(opponentPrompt));
            var def = gameDef ?? GameDefinition.PinderDefaults;

            var sb = new AnnotatedStringBuilder();

            sb.Append("== GAME VISION ==\n\n");
            sb.AppendLine(def.Vision.TrimEnd(), "game-definition.yaml", "vision");
            sb.Append("\n== WORLD RULES ==\n\n");
            sb.AppendLine(def.WorldDescription.TrimEnd(), "game-definition.yaml", "world_description");
            sb.Append("\n== OPPONENT CHARACTER ==\n\n");
            sb.AppendLine(def.OpponentRoleDescription.TrimEnd(), "game-definition.yaml", "opponent_role_description");
            sb.Append("\n");
            sb.AppendLine(opponentPrompt.TrimEnd(), "character-profile", "opponent-profile");

            sb.Append("\n== META CONTRACT ==\n\n");
            sb.AppendLine(def.MetaContract.TrimEnd(), "game-definition.yaml", "meta_contract");
            sb.Append("\n");
            sb.AppendLine(def.WritingRules.TrimEnd(), "game-definition.yaml", "writing_rules");

            if (def.DramaticCraft != null)
            {
                sb.Append("\n== DRAMATIC CRAFT ==\n\n");
                sb.AppendLine(def.DramaticCraft.BuildSection().TrimEnd(), "game-definition.yaml", "dramatic_craft");
            }

            if (!string.IsNullOrWhiteSpace(def.TextingPsychology))
            {
                sb.Append("\n== TEXTING PSYCHOLOGY ==\n\n");
                sb.AppendLine(def.TextingPsychology.TrimEnd(), "game-definition.yaml", "texting_psychology");
            }

            if (!string.IsNullOrWhiteSpace(def.RevelationOverStatement))
            {
                sb.Append("\n== REVELATION OVER STATEMENT ==\n\n");
                sb.AppendLine(def.RevelationOverStatement.TrimEnd(), "game-definition.yaml", "revelation_over_statement");
            }

            if (!string.IsNullOrWhiteSpace(def.OpponentFriction))
            {
                sb.Append("\n== OPPONENT RESISTANCE ==\n\n");
                sb.AppendLine(def.OpponentFriction.TrimEnd(), "game-definition.yaml", "opponent_friction");
            }

            if (!string.IsNullOrWhiteSpace(def.OpponentCuriosity))
            {
                sb.Append("\n== OPPONENT CURIOSITY ==\n\n");
                sb.AppendLine(def.OpponentCuriosity.TrimEnd(), "game-definition.yaml", "opponent_curiosity");
            }

            if (!string.IsNullOrWhiteSpace(def.ConversationArcProgression))
            {
                sb.Append("\n== CONVERSATION ARC ==\n\n");
                sb.AppendLine(def.ConversationArcProgression.TrimEnd(), "game-definition.yaml", "conversation_arc_progression");
            }

            sb.Append("\n");

            return new PromptTraceResult(sb.ToString(), sb.Spans);
        }
    }
}
