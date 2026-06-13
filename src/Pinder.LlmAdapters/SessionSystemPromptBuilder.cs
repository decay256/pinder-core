using System;
using Pinder.Core.Text;

namespace Pinder.LlmAdapters
{
    /// <summary>
    /// Assembles a session-level system prompt for the Game Master puppeteer.
    ///
    /// #1124: BOTH sessions (player-avatar and datee) share ONE canonical GM
    /// system-prompt template. The GM is a puppeteer that portrays exactly ONE
    /// character; the per-session character spec (role description + the
    /// assembled character profile) is injected as the FINAL block under
    /// "== CHARACTER YOU CONTROL ==". The static GM base — puppeteer framing,
    /// game vision, world rules, narrative doctrine, dramatic craft, and the
    /// shared conversation-dynamic sections — is byte-for-byte identical across
    /// both sessions, so the ONLY difference between the two built prompts is
    /// the injected character-spec block.
    ///
    /// Section ordering places the STATIC, session-invariant material FIRST and
    /// the VARIABLE character-spec block LAST. This keeps the stable cacheable
    /// prefix (GM base + game definition) ahead of the per-character delta and
    /// the volatile running transcript, so #1123's prompt caching still pays off.
    /// </summary>
    public static class SessionSystemPromptBuilder
    {
        /// <summary>
        /// Shared GM puppeteer framing. Static and identical for both sessions:
        /// the GM is told it portrays exactly the one character defined at the
        /// end of the prompt and knows/controls no other character.
        /// </summary>
        internal const string GmRoleFraming =
            "You are the Game Master for this session, acting as a puppeteer who portrays " +
            "EXACTLY ONE character: the character defined in the CHARACTER block at the very " +
            "end of this prompt. You do not know, voice, or control any other character — " +
            "you only ever speak and act as your assigned character. Everything below this line is " +
            "shared world and craft guidance that applies to whichever single character you have " +
            "been assigned. Stay fully in that character's voice for every turn.";

        /// <summary>
        /// Header that marks the start of the per-session character-spec block.
        /// Everything BEFORE this marker is the shared, identical GM base.
        /// </summary>
        public const string CharacterSpecHeader = "== CHARACTER YOU CONTROL ==";

        /// <summary>
        /// Build the GM system prompt for the player-avatar session.
        /// The injected character spec is the player avatar's role + assembled profile.
        /// </summary>
        public static string BuildPlayerAvatar(string playerAvatarPrompt, GameDefinition? gameDef = null)
        {
            var result = BuildPlayerAvatarEx(playerAvatarPrompt, gameDef);
            InMemoryPromptTraceService.Instance.RecordTrace("dialogue-options-system", result);
            return result.Text;
        }

        public static PromptTraceResult BuildPlayerAvatarEx(string playerAvatarPrompt, GameDefinition? gameDef = null)
        {
            if (playerAvatarPrompt == null) throw new ArgumentNullException(nameof(playerAvatarPrompt));
            var def = gameDef ?? GameDefinition.PinderDefaults;

            var sb = new AnnotatedStringBuilder();
            AppendGmBase(sb, def);
            AppendCharacterSpec(sb, def.PlayerRoleDescription, "player_role_description",
                playerAvatarPrompt, "player-profile");

            return new PromptTraceResult(sb.ToString(), sb.Spans);
        }

        /// <summary>
        /// Build the GM system prompt for the datee session.
        /// The injected character spec is the datee's role + assembled profile.
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
            AppendGmBase(sb, def);
            AppendCharacterSpec(sb, def.DateeRoleDescription, "datee_role_description",
                dateePrompt, "datee-profile");

            return new PromptTraceResult(sb.ToString(), sb.Spans);
        }

        /// <summary>
        /// Appends the shared, session-invariant GM base. This block is produced
        /// identically for BOTH sessions — the cacheable static prefix. Optional
        /// sections are gated on non-empty content so an empty game definition
        /// still yields an identical base across both sessions.
        /// </summary>
        private static void AppendGmBase(AnnotatedStringBuilder sb, GameDefinition def)
        {
            // --- Puppeteer framing (static; shared across both sessions) ---
            sb.Append("== GAME MASTER ==\n\n");
            sb.AppendLine(GmRoleFraming, "session-system-prompt-builder", "gm-role-framing");

            // --- STATIC, session-invariant game definition sections ---
            sb.Append("\n== GAME VISION ==\n\n");
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

            // Shared conversation-dynamic sections. Under the single-puppeteer
            // model the GM needs the full dynamic regardless of which character
            // it portrays, so these live in the shared base (not split per role).
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

            if (!string.IsNullOrWhiteSpace(def.PlayerProbing))
            {
                sb.Append("\n== PLAYER PROBING ==\n\n");
                sb.AppendLine(def.PlayerProbing.TrimEnd(), "game-definition.yaml", "player_probing");
            }
        }

        /// <summary>
        /// Appends the per-session character-spec block — the ONLY difference
        /// between the two built prompts. Placed LAST to keep the shared base as
        /// the stable cacheable prefix.
        /// </summary>
        private static void AppendCharacterSpec(
            AnnotatedStringBuilder sb,
            string roleDescription,
            string roleKey,
            string characterPrompt,
            string profileKey)
        {
            sb.Append("\n" + CharacterSpecHeader + "\n\n");
            sb.AppendLine(roleDescription.TrimEnd(), "game-definition.yaml", roleKey);
            sb.Append("\n");
            sb.AppendLine(characterPrompt.TrimEnd(), "character-profile", profileKey);
            sb.Append("\n");
        }
    }
}
