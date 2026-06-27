using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
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
    /// 
    /// #1208: This builder implements the COMPLIANT immutable-first ordering pattern (see docs/prompt-cache-ordering.md).
    /// </summary>
    public static class SessionSystemPromptBuilder
    {
        public const int RequiredBackstoryFactCount = 20;
        public const int RequiredPsychologicalStakeCount = 15;

        public const string CharacterProfileToken = "{character_profile}";
        public const string BackstoryFactsToken = "{backstory_facts}";
        public const string PsychologicalStakesToken = "{psychological_stakes}";

        public static string CompilePrompt(string template, PromptCompilationInput input)
        {
            if (template == null)
            {
                throw new ArgumentNullException(nameof(template));
            }
            if (input == null)
            {
                throw new ArgumentNullException(nameof(input));
            }

            // Validate character profile
            if (string.IsNullOrWhiteSpace(input.CharacterProfile))
            {
                throw new PromptCompilationException("Character profile (anatomy) cannot be empty or whitespace.");
            }

            // Validate backstory facts
            if (input.BackstoryFacts == null)
            {
                throw new PromptCompilationException("Backstory facts cannot be null.");
            }
            if (input.BackstoryFacts.Count != RequiredBackstoryFactCount)
            {
                throw new PromptCompilationException($"Backstory facts count expected {RequiredBackstoryFactCount}, actual count {input.BackstoryFacts.Count}.");
            }
            foreach (var fact in input.BackstoryFacts)
            {
                if (string.IsNullOrWhiteSpace(fact))
                {
                    throw new PromptCompilationException("Backstory fact cannot be null or whitespace.");
                }
            }

            // Validate psychological stakes
            if (input.PsychologicalStakes == null)
            {
                throw new PromptCompilationException("Psychological stakes cannot be null.");
            }
            if (input.PsychologicalStakes.Count != RequiredPsychologicalStakeCount)
            {
                throw new PromptCompilationException($"Psychological stakes count expected {RequiredPsychologicalStakeCount}, actual count {input.PsychologicalStakes.Count}.");
            }
            foreach (var stake in input.PsychologicalStakes)
            {
                if (string.IsNullOrWhiteSpace(stake))
                {
                    throw new PromptCompilationException("Psychological stake cannot be null or whitespace.");
                }
            }

            // Single-pass replacement over template
            return Regex.Replace(template, @"\{[A-Za-z0-9_]+\}", match =>
            {
                string token = match.Value;
                if (token == CharacterProfileToken)
                {
                    return input.CharacterProfile;
                }
                else if (token == BackstoryFactsToken)
                {
                    return string.Join("\n", input.BackstoryFacts.Select((f, i) => (i + 1) + ". " + f));
                }
                else if (token == PsychologicalStakesToken)
                {
                    return string.Join("\n", input.PsychologicalStakes.Select((s, i) => (i + 1) + ". " + s));
                }
                else
                {
                    throw new PromptCompilationException("unknown token '" + token + "'");
                }
            });
        }

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
            AppendCharacterSpec(sb, def.PlayerAvatarRoleDescription, "player_avatar_role_description",
                playerAvatarPrompt, "player-profile", "PLAYER_AVATAR_CHARACTER");

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
                dateePrompt, "datee-profile", "DATEE_CHARACTER");

            return new PromptTraceResult(sb.ToString(), sb.Spans);
        }

        /// <summary>
        /// Appends the shared, session-invariant GM base (#1153). The entire GM
        /// base is now a single pre-assembled field; it is emitted verbatim as
        /// the cacheable static prefix produced identically for BOTH sessions.
        /// </summary>
        private static void AppendGmBase(AnnotatedStringBuilder sb, GameDefinition def)
        {
            sb.AppendLine(def.GameMasterPrompt.TrimEnd(), "game-definition.yaml", "game_master_prompt");
        }

        /// <summary>
        /// Appends the per-session character-spec block — the ONLY difference
        /// between the two built prompts. Placed LAST to keep the shared base as
        /// the stable cacheable prefix.
        /// The characterTag parameter wraps the character payload in the
        /// SSOT injection-fence declared in game-definition.yaml §5:
        ///   PLAYER_AVATAR_CHARACTER or DATEE_CHARACTER.
        /// </summary>
        private static void AppendCharacterSpec(
            AnnotatedStringBuilder sb,
            string roleDescription,
            string roleKey,
            string characterPrompt,
            string profileKey,
            string characterTag)
        {
            sb.Append("\n" + CharacterSpecHeader + "\n\n");
            sb.AppendLine(roleDescription.TrimEnd(), "game-definition.yaml", roleKey);
            sb.Append("\n");
            sb.Append($"<{characterTag}>\n");
            sb.AppendLine(characterPrompt.TrimEnd(), "character-profile", profileKey);
            sb.Append($"</{characterTag}>\n");
            sb.Append("\n");
        }
    }
}
