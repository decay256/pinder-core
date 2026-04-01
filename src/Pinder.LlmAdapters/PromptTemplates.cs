namespace Pinder.LlmAdapters
{
    /// <summary>
    /// Static instruction templates sourced from character-construction.md §3.2–3.8.
    /// Each template uses {placeholder} tokens filled by SessionDocumentBuilder at call time.
    /// </summary>
    public static class PromptTemplates
    {
        /// <summary>§3.2 — Instructs the LLM to generate exactly 4 dialogue options with metadata tags.</summary>
        public const string DialogueOptionsInstruction =
@"Generate exactly 4 dialogue options for {player_name}.

Each option must:
1. Be tagged with one of: CHARM, RIZZ, HONESTY, CHAOS, WIT, SELF_AWARENESS
2. Show what the character INTENDS to say — this is their internal intended message, before any roll outcome is applied
3. Reflect the player's personality and current shadow state (not the opponent's)
4. Vary in tone and risk — include at least one safe and one bold option
5. If a callback opportunity exists, make 1–2 options reference an earlier topic naturally
6. If a combo is available, one option should use the completing stat
7. Take the opponent's profile into account — their personality, archetypes, and texting style should inform what would land or fail

For each option include metadata:
[STAT: X] [CALLBACK: turn_N or none] [COMBO: name or none] [TELL_BONUS: yes/no]

Keep options concise. One to three sentences. Match the opponent's register.

Output EXACTLY this format for each option (no deviations):

OPTION_1
[STAT: CHARM] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
""The exact text the character would send""

OPTION_2
[STAT: HONESTY] [CALLBACK: turn_2] [COMBO: The Reveal] [TELL_BONUS: yes]
""The exact text the character would send""

(continue for OPTION_3 and OPTION_4)

Rules:
- STAT must be one of: CHARM, RIZZ, HONESTY, CHAOS, WIT, SELF_AWARENESS
- Text must be in double quotes on the line immediately after the metadata
- No extra text before OPTION_1 or after the last option";

        /// <summary>§3.3 — Deliver the intended message on a successful roll.</summary>
        public const string SuccessDeliveryInstruction =
@"Write as {player_name}.
Deliver this message as the character would actually send it.
- On a clean success (margin 1–5): deliver it essentially as written, with natural voice
- On a strong success (margin 6–10): add a small flourish or timing that makes it land better
- On a critical success / Nat 20: deliver it at peak — perfectly timed, resonant, exactly right

Keep it in character. Keep the lowercase voice. Don't explain the success.
Output only the message text.";

        /// <summary>§3.4 — Degrade the intended message according to failure tier.</summary>
        public const string FailureDeliveryInstruction =
@"You are writing as {player_name}. This is THEIR message, in THEIR voice.
Do NOT write as the opponent. The failure corrupts what {player_name} says.

The player chose option: ""{intended_message}""
Stat used: {stat}
They rolled FAILED — missed DC by {miss_margin}.
Failure tier: {tier}

Failure principle: corrupt the CONTENT, not the delivery. The message always sends intentionally.
Words are what betray you. The character means to say one thing and something else comes out.

Tier-specific instructions:
{tier_instruction}

TIER INSTRUCTIONS:
FUMBLE (miss 1–2): Slight fumble. The intended message mostly gets through but with one awkward word
  choice, an unnecessary hedge, or a small detail that undermines it. Still readable.

MISFIRE (miss 3–5): The message goes sideways. Key information gets garbled, tone shifts
  unexpectedly, or a strange tangent appears mid-sentence. The intent is still guessable but
  the execution is off.

TROPE_TRAP (miss 6–9): A stat-specific social trope failure activates. The message transforms
  into a recognisable bad-texting archetype (oversharing, going unhinged, being pretentious,
  spiraling, etc.). The trap is now active.

CATASTROPHE (miss 10+): Spectacular disaster. The intended message has been completely hijacked
  by the character's worst impulse. What comes out is the thing they would NEVER want to send.
  Still sounds like them — their disaster is their own.

LEGENDARY (Nat 1): Maximum humiliation. The character's deepest embarrassing quality
  surfaces fully. This should be funny, specific, and feel earned by the build.

{active_trap_llm_instructions}

Output only the message text. No explanation. The character sent this.";

        /// <summary>
        /// §3.5 — Generate opponent response with optional [SIGNALS] block.
        /// Uses {placeholder} tokens for dynamic content.
        /// </summary>
        public const string OpponentResponseInstruction =
@"Generate your next message.
- Match your personality exactly as established in the system prompt
- React authentically to what was just said
- If Interest dropped: show cooling without being melodramatic
- If Interest rose: show warming without being gushing
- Keep it to 1–3 sentences. Match the register.

Output format — use EXACTLY this structure:

[RESPONSE]
""your actual message text""

Occasionally (when it feels natural, roughly 30–40% of turns), include a signals block after your response:

[SIGNALS]
TELL: {STAT_NAME} ({description of what reveals the tell})
WEAKNESS: {STAT_NAME} -{reduction} ({description of the opening})

Rules for signals:
- TELL line format: TELL: CHARM|RIZZ|HONESTY|CHAOS|WIT|SELF_AWARENESS (brief description)
- WEAKNESS line format: WEAKNESS: CHARM|RIZZ|HONESTY|CHAOS|WIT|SELF_AWARENESS -2 or -3 (brief description)
- Both lines are independently optional within a [SIGNALS] block
- Only include signals when the conversation naturally reveals them — do not force them";

        /// <summary>§3.8 — Generate a narrative beat when interest crosses a threshold.</summary>
        public const string InterestBeatInstruction =
@"{opponent_name}'s Interest just moved from {interest_before} to {interest_after}.

{threshold_instruction}

Output only the message or gesture text.";

        // Threshold-specific sub-instructions for InterestBeatInstruction
        internal const string InterestBeatAbove15 =
@"Generate a brief reaction — one sentence or a small gesture — showing {opponent_name} becoming more invested. Subtle. Not a proclamation. A shift in energy.";

        internal const string InterestBeatBelow8 =
@"Generate a brief cooling signal — one sentence or a small gesture — showing {opponent_name} pulling back slightly. Not dramatic. Just a temperature change.";

        internal const string InterestBeatDateSecured =
@"Generate a brief moment where {opponent_name} suggests or implies meeting up. In character. Not ""do you want to go on a date?"" — something specific to them.";

        internal const string InterestBeatUnmatched =
@"Generate {opponent_name} unmatching — one final message or simply going silent. In character. No villain speech. Just a door closing.";

        internal const string InterestBeatGeneric =
@"Generate a brief reaction from {opponent_name} reflecting the change in interest. Subtle and in character.";
    }
}
