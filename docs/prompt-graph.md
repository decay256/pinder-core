# Prompt Graph: The Two-Session Model

> Supersession note (#1332): in current code, "session" means an
> engine-owned, role-isolated prompt pipeline. It does not mean provider-side
> persistent conversation state. The authoritative ownership contract is
> [`docs/specs/issue-1332-datee-prerequisite-architecture.md`](specs/issue-1332-datee-prerequisite-architecture.md).

The Pinder conversation pipeline uses a **Two-Session Model** (implemented via a shared Game-Master / Puppeteer orchestrator). This isolates the player character's internal monologue and options generation from the datee's context, preventing "voice bleed" where one character starts sounding like the other, and keeping the player's unchosen options hidden from the datee's history.

## High-Level Flow

```text
┌─────────────────┐       ┌─────────────────┐
│ Avatar Session  │       │ Datee Session   │
│ (Role Pipeline) │       │ (Role Pipeline) │
└────────┬────────┘       └────────┬────────┘
         │                         │
         │                         │
         ▼                         │
 [1] GetDialogueOptionsAsync       │
 (Dynamic phase goal + stem)       │
 (Ephemeral Branch)                │
         │                         │
         ▼                         │
 [2] Player Selects Option         │
         │                         │
         ▼                         │
 [3] Commit Step (Deterministic)   │
 (Delivery overlay: Tier degrade)  │
         │                         │
         ▼                         │
 [4] Overlays (Ephemeral Branch)   │
 (Trap → Shadow → Horniness)       │
         │                         │
         ▼                         │
 [5] Final Delivered Message       │
         │                         │
         ├─────────────────────────►
         │                         │
         │                         ▼
         │                 [6] Emotional Director / Reaction
         │                 (Neurosis + Injected Backstory + Phase Goal)
         │                         │
         │                         ▼
         │                 [7] GetDateeResponseAsync
         │                 (Prior history + current delivered message + direction)
         │                         │
         ◄─────────────────────────┤
         │                         │
         ▼                         ▼
        └────────── History append: player/DATEE pair after success
```

## System Prompt Architecture

System prompts (`AssembledSystemPrompt`) are intentionally streamlined to keep token footprints lean and prevent prompt bleed:
- **Static Base**: Lean character bio, active archetype, texting style tendencies, and high-level comedy dating RPG framing.
- **Pruned Elements**: Monolithic C# engine rulebooks, raw 11-bullet psychiatric diagnosis dumps, and static 20-category backstory tables have been removed from the static system prompt.
- **Dynamic Turn-by-Turn Enrichment**: Psychological neurosis, dramatic phase goals, and specific backstory quirks are dynamically selected per-turn by `EmotionStemSelector` and the Emotional Director.

## 4 Dramatic Arc Phases

Conversation progression is guided dynamically across 4 macro phases:
1. **Phase 1 (Setup)**: Testing if the match can hold a conversation, establishing high-status positioning vs. playful intrigue, and probing vibe/humor with punchy 1-a.m. dating texts without giving away personal history.
2. **Phase 2 (Escalation)**: Taking mundane real character facts or quirks and improvising flattering, high-status, or intriguing lies/flexes on the fly to tease or impress the match.
3. **Phase 3 (Turning Point)**: Creating genuine intimate tension through a cracked facade — admitting underlying insecurity or reality as a vulnerable slip or tired honesty, followed by a flirtatious pivot back to the match.
4. **Phase 4 (Resolution)**: Closing the hookup or meeting logistics on their terms — 100% focused on sealing the date/hookup.

Tone and behavior across all phases are modulated through each character's psychiatric diagnosis and emotional writing direction rather than hardcoded universal cynicism.

## 1. Avatar Session (Player Side)

The **Avatar Session** generates the player's dialogue options.
- **Context**: Player's lean system prompt, texting style, datee's *public* profile (name, bio), conversation history, shadow state, active traps, and the active dynamic phase goal / backstory stem from `EmotionStemSelector`.
- **Action**: Generates 3 dialogue options containing the **full, sendable line**. 
- **Ephemeral Pruning**: Option generation happens on an ephemeral branch. The prompt, the unchosen options, and the option generation text itself are **never** committed to the main session history. This ensures the datee has no knowledge of what the player *could* have said.

## 2. The Commit Step (No Delivery LLM Call)

As of the #1125 delivery collapse, **there is no creative `DeliverMessageAsync` LLM call**.
- The player picks an option.
- The chosen option's full line is taken verbatim on a success.
- On a failure, the line is degraded deterministically via `DeliveryOverlay.Apply` (based on the failure tier).
- **Clean History Rule**: Only the final delivered line can become visible history. During DATEE generation it is supplied once as the current event, then the player/DATEE visible-history pair is appended only after the DATEE reply succeeds.

## 3. Ephemeral Overlays

If active, several LLM overlays can rewrite the message *in place* before it is delivered:
- **Trap Overlay** (`ApplyTrapOverlayAsync`)
- **Shadow Corruption** (`ApplyShadowCorruptionAsync`)
- **Horniness Overlay** (`ApplyHorninessOverlayAsync`)

*Invariant: Horniness must run LAST (HORNINESS-OVERLAY-MUST-BE-LAST-TEXT-LAYER).*

These calls are stateless string-in/string-out transformations. They do not maintain conversation history, preventing them from leaking into subsequent turns.

## 4. Datee Session

The **Datee Session** generates the datee's response to the player's delivered message.
- **Context**: Datee's lean system prompt, datee's resistance level, prior completed visible exchanges, the player's *final delivered message* as the current event (with any failure/overlay contexts attached as metadata), and dynamic writing direction from the Emotional Director.
- **Emotional Director / Reaction Pass**: Evaluates the character's internal neurosis (from `psychiatric_diagnosis`), the current emotional turn event, and the active dramatic phase goal to produce actionable writing direction (`ego_game`, `improvised_flex_or_slip`, `texting_tactics`).
- **Bleed Isolation**: The datee session is completely isolated from the avatar session. It never sees the avatar's internal states, unchosen options, or the original pristine intended text (if it was degraded/corrupted). It only sees what was actually "sent".

## Related Specs
- Detailed turn flow: [`ARCHITECTURE.md`](ARCHITECTURE.md)
- Integration details: [`unity-integration.md`](unity-integration.md)
