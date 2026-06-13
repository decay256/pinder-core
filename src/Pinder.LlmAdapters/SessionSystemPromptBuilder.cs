using System;
using Pinder.Core.Text;

namespace Pinder.LlmAdapters
{
    /// <summary>
    /// Assembles a session-level system prompt from character profiles and game definition.
    /// Section ordering places the STATIC, session-invariant material (game vision, world
    /// rules, narrative doctrine, dramatic craft, structural sections) FIRST, and the
    /// CHARACTER-SPECIFIC / VARIABLE material (the assembled player/datee profiles) LAST.
    /// Keeping the variable character block at the tail maximizes the stable, cacheable
    /// prompt prefix across sessions and keeps role-specific content closest to the turn.
    /// </summary>
    public static class SessionSystemPromptBuilder
    {
        /// <summary>
        /// Build the full session system prompt.
        /// </summary>
        /// <param name="playerPrompt">
        /// Player's assembled character system prompt (from CharacterProfile.AssembledSystemPrompt).
        /// </param>
        /// <param name="dateePrompt">
        /// Datee's assembled character system prompt (from CharacterProfile.AssembledSystemPrompt).
        /// </param>
        /// <param name="gameDef">
        /// Game definition containing vision, world rules, meta contract.
        /// When null, GameDefinition.PinderDefaults is used.
        /// </param>
        /// <returns>A single string containing the full session system prompt.</returns>
        public static string Build(
            string playerPrompt,
            string dateePrompt,
            GameDefinition? gameDef = null)
        {
            if (playerPrompt == null)
                throw new ArgumentNullException(nameof(playerPrompt));
            if (dateePrompt == null)
                throw new ArgumentNullException(nameof(dateePrompt));

            var def = gameDef ?? GameDefinition.PinderDefaults;

            return string.Concat(
                // --- STATIC, session-invariant sections first (cacheable prefix) ---
                "== GAME VISION ==\n\n",
                def.Vision.TrimEnd(),
                "\n\n== WORLD RULES ==\n\n",
                def.WorldDescription.TrimEnd(),
                "\n\n== NARRATIVE DOCTRINE ==\n\n",
                def.NarrativeDoctrine.TrimEnd(),
                "\n\n== DRAMATIC CRAFT ==\n\n",
                def.DramaticCraft != null ? def.DramaticCraft.BuildSection().TrimEnd() : "",
                // Build() is the legacy joint prompt for callers that want both
                // roles in one system block. Per #867 LESSONS_LEARNED, this method
                // retains all sections (only BuildPlayer is trimmed).
                string.IsNullOrWhiteSpace(def.DateeFriction) ? "" : "\n\n== DATEE RESISTANCE ==\n\n" + def.DateeFriction.TrimEnd(),
                string.IsNullOrWhiteSpace(def.DateeCuriosity) ? "" : "\n\n== DATEE CURIOSITY ==\n\n" + def.DateeCuriosity.TrimEnd(),
                string.IsNullOrWhiteSpace(def.ConversationArcProgression) ? "" : "\n\n== CONVERSATION ARC ==\n\n" + def.ConversationArcProgression.TrimEnd(),
                string.IsNullOrWhiteSpace(def.PlayerProbing) ? "" : "\n\n== PLAYER PROBING ==\n\n" + def.PlayerProbing.TrimEnd(),
                // --- CHARACTER-SPECIFIC / VARIABLE sections LAST ---
                "\n\n== PLAYER CHARACTER ==\n\n",
                playerPrompt.TrimEnd(),
                "\n\n== DATEE CHARACTER ==\n\n",
                dateePrompt.TrimEnd(),
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

            // --- STATIC, session-invariant sections first (cacheable prefix) ---
            sb.Append("== GAME VISION ==\n\n");
            sb.AppendLine(def.Vision.TrimEnd(), "game-definition.yaml", "vision");
            sb.Append("\n== WORLD RULES ==\n\n");
            sb.AppendLine(def.WorldDescription.TrimEnd(), "game-definition.yaml", "world_description");

            sb.Append("\n== NARRATIVE DOCTRINE ==\n\n");
            sb.AppendLine(def.NarrativeDoctrine.TrimEnd(), "game-definition.yaml", "narrative_doctrine");

            if (def.DramaticCraft != null)
            {
                sb.Append("\n== DRAMATIC CRAFT ==\n\n");
                sb.AppendLine(def.DramaticCraft.BuildSection().TrimEnd(), "game-definition.yaml", "dramatic_craft");
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

            // --- CHARACTER-SPECIFIC / VARIABLE section LAST ---
            sb.Append("\n== PLAYER CHARACTER ==\n\n");
            sb.AppendLine(def.PlayerRoleDescription.TrimEnd(), "game-definition.yaml", "player_role_description");
            sb.Append("\n");
            sb.AppendLine(playerPrompt.TrimEnd(), "character-profile", "player-profile");

            sb.Append("\n");

            return new PromptTraceResult(sb.ToString(), sb.Spans);
        }

        /// <summary>
        /// Build a system prompt containing only the datee's character profile and game definition.
        /// Used to prevent voice bleed when generating datee responses.
        /// </summary>
        public static string BuildDatee(string dateePrompt, GameDefinition? gameDef = null)
        {
            var result = BuildDateeEx(dateePrompt, gameDef);
            InMemoryPromptTraceService.Instance.RecordTrace("datee-system", result);
            return result.Text;
        }

        public static PromptTraceResult BuildDateeEx(string dateePrompt, GameDefinition? gameDef = null)
        {
            if (dateePrompt == null) throw new ArgumentNullException(nameof(dateePrompt));
            var def = gameDef ?? GameDefinition.PinderDefaults;

            var sb = new AnnotatedStringBuilder();

            // --- STATIC, session-invariant sections first (cacheable prefix) ---
            sb.Append("== GAME VISION ==\n\n");
            sb.AppendLine(def.Vision.TrimEnd(), "game-definition.yaml", "vision");
            sb.Append("\n== WORLD RULES ==\n\n");
            sb.AppendLine(def.WorldDescription.TrimEnd(), "game-definition.yaml", "world_description");

            sb.Append("\n== NARRATIVE DOCTRINE ==\n\n");
            sb.AppendLine(def.NarrativeDoctrine.TrimEnd(), "game-definition.yaml", "narrative_doctrine");

            if (def.DramaticCraft != null)
            {
                sb.Append("\n== DRAMATIC CRAFT ==\n\n");
                sb.AppendLine(def.DramaticCraft.BuildSection().TrimEnd(), "game-definition.yaml", "dramatic_craft");
            }

            if (!string.IsNullOrWhiteSpace(def.DateeFriction))
            {
                sb.Append("\n== DATEE RESISTANCE ==\n\n");
                sb.AppendLine(def.DateeFriction.TrimEnd(), "game-definition.yaml", "datee_friction");
            }

            if (!string.IsNullOrWhiteSpace(def.DateeCuriosity))
            {
                sb.Append("\n== DATEE CURIOSITY ==\n\n");
                sb.AppendLine(def.DateeCuriosity.TrimEnd(), "game-definition.yaml", "datee_curiosity");
            }

            if (!string.IsNullOrWhiteSpace(def.ConversationArcProgression))
            {
                sb.Append("\n== CONVERSATION ARC ==\n\n");
                sb.AppendLine(def.ConversationArcProgression.TrimEnd(), "game-definition.yaml", "conversation_arc_progression");
            }

            // --- CHARACTER-SPECIFIC / VARIABLE section LAST ---
            sb.Append("\n== DATEE CHARACTER ==\n\n");
            sb.AppendLine(def.DateeRoleDescription.TrimEnd(), "game-definition.yaml", "datee_role_description");
            sb.Append("\n");
            sb.AppendLine(dateePrompt.TrimEnd(), "character-profile", "datee-profile");

            sb.Append("\n");

            return new PromptTraceResult(sb.ToString(), sb.Spans);
        }
    }
}
