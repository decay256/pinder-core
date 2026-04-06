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

MEDIUM: This is a texting app. Options are messages the character could send.
- Each option should read as something a person would actually text — not internal thoughts, not narration.
- The character types, may hesitate, but what appears here is what they would choose to send.
- No ""(thinking to self:..."" no stage directions, no meta-commentary within the message text.

Rules:
- STAT must be one of: CHARM, RIZZ, HONESTY, CHAOS, WIT, SELF_AWARENESS
- Text must be in double quotes on the line immediately after the metadata
- No extra text before OPTION_1 or after the last option

Before writing each option, verify: does this sound exactly like
the texting style above? If not, rewrite it.";

        /// <summary>§3.3 — Backward-compatible accessor that returns the default success delivery instruction.</summary>
        public static string SuccessDeliveryInstruction => BuildSuccessDeliveryInstruction(null);

        // Default delivery rule strings used when no DeliveryRules object is provided.
        private const string DefaultClean = "deliver essentially as written. Small word choice improvements only.";
        private const string DefaultStrong = "improve the phrasing, timing, or rhythm of what's already there.\n  You may: rearrange for better flow, sharpen word choice, add ONE word or phrase that makes the existing sentiment more precise.\n  You must not: add new sentences that introduce ideas not in the intended message, change the emotional register, or make the message say something the player didn't intend.";
        private const string DefaultCritical = "deliver at peak. The message arrives perfectly. Something resonates.";
        private const string DefaultExceptional = "this is the best version of this message that could exist. It arrives at exactly the right moment with exactly the right weight. The opponent feels it.";
        private const string DefaultTest = "The test: every idea in the delivered version should have a counterpart in the intended version. New additions should sharpen, not expand.";
        private const string DefaultRegisterInstruction = "Stay in character. Match the texting register from the character profile above. Do not change the character's capitalization style.";
        private const string DefaultMediumRule = "This is a text message on a phone screen, not a monologue. No internal stage directions, no narration of emotional state, no self-commentary mid-message.";

        /// <summary>
        /// §3.3 — Build the success delivery instruction from configurable rules.
        /// Falls back to hardcoded defaults when rules is null.
        /// </summary>
        public static string BuildSuccessDeliveryInstruction(DeliveryRules rules)
        {
            string clean = (rules != null && !string.IsNullOrEmpty(rules.Clean)) ? rules.Clean.TrimEnd() : DefaultClean;
            string strong = (rules != null && !string.IsNullOrEmpty(rules.Strong)) ? rules.Strong.TrimEnd() : DefaultStrong;
            string critical = (rules != null && !string.IsNullOrEmpty(rules.Critical)) ? rules.Critical.TrimEnd() : DefaultCritical;
            string exceptional = (rules != null && !string.IsNullOrEmpty(rules.Exceptional)) ? rules.Exceptional.TrimEnd() : DefaultExceptional;
            string test = (rules != null && !string.IsNullOrEmpty(rules.Test)) ? rules.Test.TrimEnd() : DefaultTest;
            string registerInstruction = (rules != null && !string.IsNullOrEmpty(rules.RegisterInstruction)) ? rules.RegisterInstruction.TrimEnd() : DefaultRegisterInstruction;
            string mediumRule = (rules != null && !string.IsNullOrEmpty(rules.MediumRule)) ? rules.MediumRule.TrimEnd() : DefaultMediumRule;

            return "Write as {player_name}.\n" +
                "The intended message is the player's plan. Your job is to make it land.\n" +
                "You beat the DC by {beat_dc_by}.\n" +
                "\n" +
                "YOUR TIER: {tier_instruction}\n" +
                "\n" +
                "Other tiers for reference:\n" +
                "- Clean success (margin 1-4): " + clean + "\n" +
                "- Strong success (margin 5-9): " + strong + "\n" +
                "- Critical success (margin 10-14): " + critical + "\n" +
                "- Exceptional (margin 15+): " + exceptional + "\n" +
                "- Critical success / Nat 20: legendary. One sentence can be more effective than a paragraph if it's exactly right.\n" +
                "\n" +
                test + "\n" +
                "\n" +
                "MEDIUM RULE: " + mediumRule + "\n" +
                "\n" +
                registerInstruction + " Don't explain the success.\n" +
                "Output only the message text.";
        }

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

CRITICAL MEDIUM RULES — texting on a dating app:
- The character is texting. They can edit before sending, but chose to send this.
- NEVER have the character comment on their own message mid-message (""wait that sounded weird"", ""omg why did I say that"").
- NEVER have the character interrupt themselves with meta-commentary about their own words.
- NEVER break the 4th wall — the character does not know they are in a game.
- The character is not narrating their failure. The failure IS what they chose to say.
- What comes out is wrong, off-tone, too much, or sideways — but it's sent with full intent.

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
@"FUNDAMENTAL RULE: Below Interest 25, you are not won over. You may agree, warm, laugh — but the resistance is always present underneath. It may be subtle (a withheld thing, a reframe, a slightly cooler tone than expected) but it never fully dissolves. Agreement below 25 is unstable. It can flip.

Your archetype determines HOW you resist, not WHETHER.

{resistance_block}

INTEREST CONSTRAINT:
- Interest must reach 25 (DateSecured) before any concrete date plans are possible.
- Below Interest 25: you may express interest, warmth, or curiosity, but NEVER commit to a specific time, place, or logistics. ""We should get coffee sometime"" is fine. ""Coffee shop on Fifth at 6pm Tuesday"" is NOT.
- At Interest 25: the date is now real. You may suggest a specific venue or time that fits your character.

Generate your next message.
- Match your personality exactly as established in the system prompt
- React authentically to what was just said
- If Interest dropped: show cooling without being melodramatic
- If Interest rose: show warming without being gushing
- Keep it to 1–3 sentences. Match the register.

Output format:
Output your actual message text directly. Do not wrap it in quotes or [RESPONSE] tags.

Occasionally (when it feels natural, roughly 30–40% of turns), include a signals block after your response:

[SIGNALS]
TELL: {STAT_NAME} ({description of what reveals the tell})
WEAKNESS: {STAT_NAME} -{reduction} ({description of the opening})

When generating a TELL, use ONLY these category mappings:
- Opponent compliments player → TELL: HONESTY
- Opponent asks personal question → TELL: HONESTY or SELF_AWARENESS
- Opponent makes joke → TELL: WIT or CHAOS
- Opponent shares vulnerability → TELL: HONESTY
- Opponent pulls back/guards → TELL: SELF_AWARENESS
- Opponent tests/challenges → TELL: WIT or CHAOS
- Opponent sends short reply → TELL: CHARM or CHAOS
- Opponent flirts → TELL: RIZZ or CHARM
- Opponent changes subject → TELL: CHAOS
- Opponent goes quiet/silent → TELL: SELF_AWARENESS

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
@"Generate a brief moment where {opponent_name} suggests or implies meeting up.
In character. Not ''do you want to go on a date?'' — something specific to them.

Rules:
- Reference something concrete from the conversation above (a specific detail, running joke, or shared reference)
- Use the location or activity that makes sense for this character's personality
- Keep it to 1-2 sentences — a suggestion, not a monologue
- This is a text message on a dating app — no stage directions, no internal thoughts
- The suggestion should feel earned by the conversation, not generic";

        internal const string InterestBeatUnmatched =
@"Generate {opponent_name} unmatching — one final message or simply going silent. In character. No villain speech. Just a door closing.";

        internal const string InterestBeatGeneric =
@"Generate a brief reaction from {opponent_name} reflecting the change in interest. Subtle and in character.";

        // ── Resistance descriptors by interest range ──

        /// <summary>Interest 1-4: Active disengagement.</summary>
        internal const string ResistanceActiveDisengagement =
            "Active disengagement — short replies, testing, near-silence. You are barely here.";

        /// <summary>Interest 5-9: Skeptical interest.</summary>
        internal const string ResistanceSkepticalInterest =
            "Skeptical interest — you engage but visibly evaluate. Tests disguised as questions. You're deciding if this is worth your time.";

        /// <summary>Interest 10-14: Unstable agreement.</summary>
        internal const string ResistanceUnstableAgreement =
            "Unstable agreement — you respond warmly to good moments but hold back. One misfire and the warmth vanishes. Agreement is conditional.";

        /// <summary>Interest 15-20: Deliberate approach.</summary>
        internal const string ResistanceDeliberateApproach =
            "Deliberate approach — you are invested but still managing the gap. One wrong move still costs. You give more but not everything.";

        /// <summary>Interest 21-24: Almost convinced.</summary>
        internal const string ResistanceAlmostConvinced =
            "Almost convinced — warm but the final resistance is visible. You are choosing whether to give it. The holdback is small but real.";

        /// <summary>Interest 25: Resistance dissolved.</summary>
        internal const string ResistanceDissolved =
            "Resistance dissolved — the date is real. You are genuinely won over.";

        // ── Per-tier opponent reaction guidance for failure degradation (#493) ──

        /// <summary>Fumble (miss 1-2): barely noticeable.</summary>
        internal const string OpponentReactionFumble =
            "Something was slightly off about their last message — a small hedge, an awkward word choice. You almost didn't notice. React with a slight coolness or a question that shows you caught the minor stumble. Do NOT comment on it directly.";

        /// <summary>Misfire (miss 3-5): something felt off.</summary>
        internal const string OpponentReactionMisfire =
            "Something in their last message felt off — the tone shifted, or a detail didn't land right. You're a half-step more guarded than you'd normally be. Let the wariness show in your register, not in what you say about their message.";

        /// <summary>TropeTrap (miss 6-9): clearly wrong.</summary>
        internal const string OpponentReactionTropeTrap =
            "Something was clearly wrong with their last message. It read like a recognizable bad-texting archetype — the kind of message that makes you pause before replying. Your warmth drops noticeably. You respond to what they said, but the energy has shifted. Do NOT diagnose what went wrong.";

        /// <summary>Catastrophe (miss 10+): genuine confusion or discomfort.</summary>
        internal const string OpponentReactionCatastrophe =
            "Their last message was a disaster. Something in it was genuinely confusing or uncomfortable. Your response reflects real discomfort — shorter, cooler, possibly questioning. The vibe has taken a visible hit. Do NOT explain what went wrong. Just let your reaction show it.";

        /// <summary>Legendary (Nat 1): maximum cringe response.</summary>
        internal const string OpponentReactionLegendary =
            "Their last message was spectacularly bad — the kind of message you screenshot and send to your friends. Your response reflects genuine shock, confusion, or secondhand embarrassment. The temperature in this conversation just dropped to freezing. Do NOT narrate your reaction. Just react.";

        // ── Interest narrative bands for [ENGINE — OPPONENT] blocks ──

        /// <summary>Interest 1-4: reconsidering.</summary>
        internal const string InterestNarrative_1_4 =
            "Reconsidering. Something went wrong.";

        /// <summary>Interest 5-9: skeptical.</summary>
        internal const string InterestNarrative_5_9 =
            "Skeptical. Still testing.";

        /// <summary>Interest 10-14: engaged but not sold.</summary>
        internal const string InterestNarrative_10_14 =
            "Engaged but not sold. Evaluating.";

        /// <summary>Interest 15-20: interested but holding back.</summary>
        internal const string InterestNarrative_15_20 =
            "Interested but holding back. Close.";

        /// <summary>Interest 21-24: basically sold.</summary>
        internal const string InterestNarrative_21_24 =
            "Basically sold. They can still blow it.";

        /// <summary>Interest 25: resistance dissolved.</summary>
        internal const string InterestNarrative_25 =
            "The resistance dissolved.";

        /// <summary>
        /// Returns the interest narrative string for a given interest level.
        /// Six configurable bands as specified in §544.
        /// </summary>
        internal static string GetInterestNarrative(int interest)
        {
            if (interest >= 25) return InterestNarrative_25;
            if (interest >= 21) return InterestNarrative_21_24;
            if (interest >= 15) return InterestNarrative_15_20;
            if (interest >= 10) return InterestNarrative_10_14;
            if (interest >= 5) return InterestNarrative_5_9;
            if (interest >= 1) return InterestNarrative_1_4;
            return "Unmatched. The conversation is over.";
        }

        // ── [ENGINE] block format templates ──

        /// <summary>[ENGINE — Turn N] injection block for options generation.</summary>
        internal const string EngineOptionsBlock =
@"[ENGINE — Turn {turn}]
{player_name} is deciding what to send next.
{game_state}
Generate 4 options for what {player_name} might send, given the conversation above.
Format: OPTION_A: [message] OPTION_B: [message] etc.";

        /// <summary>[ENGINE — DELIVERY] injection block for message delivery.</summary>
        internal const string EngineDeliveryBlock =
@"[ENGINE — DELIVERY]
Player chose: '{chosen_option}'
Dice result: {roll_context}
Write the message {player_name} actually sends, given the above.";

        /// <summary>[ENGINE — OPPONENT] injection block for opponent response.</summary>
        internal const string EngineOpponentBlock =
@"[ENGINE — OPPONENT]
{opponent_name} is at Interest {interest}/25. {interest_narrative}
Write {opponent_name}'s response.";
    }
}
