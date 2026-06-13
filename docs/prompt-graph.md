# Prompt Graph: The Two-Session Model

The Pinder conversation pipeline uses a **Two-Session Model** (implemented via a shared Game-Master / Puppeteer orchestrator). This isolates the player character's internal monologue and options generation from the datee's context, preventing "voice bleed" where one character starts sounding like the other, and keeping the player's unchosen options hidden from the datee's history.

## High-Level Flow

```text
┌─────────────────┐       ┌─────────────────┐
│ Avatar Session  │       │ Datee Session   │
│ (Stateful GM)   │       │ (Stateful GM)   │
└────────┬────────┘       └────────┬────────┘
         │                         │
         │                         │
         ▼                         │
 [1] GetDialogueOptionsAsync       │
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
         │                 [6] GetDateeResponseAsync
         │                 (Reads delivered message)
         │                         │
         ◄─────────────────────────┤
         │                         │
         ▼                         ▼
  History Append            History Append
```

## 1. Avatar Session (Player Side)

The **Avatar Session** generates the player's dialogue options.
- **Context**: Player's system prompt, texting style, datee's *public* profile (name, bio), conversation history, shadow state, active traps.
- **Action**: Generates 3-4 dialogue options containing the **full, sendable line**. 
- **Ephemeral Pruning**: Option generation happens on an ephemeral branch. The prompt, the unchosen options, and the option generation text itself are **never** committed to the main session history. This ensures the datee has no knowledge of what the player *could* have said.

## 2. The Commit Step (No Delivery LLM Call)

As of the #1125 delivery collapse, **there is no creative `DeliverMessageAsync` LLM call**.
- The player picks an option.
- The chosen option's full line is taken verbatim on a success.
- On a failure, the line is degraded deterministically via `DeliveryOverlay.Apply` (based on the failure tier).
- **Clean History Rule**: Only the final committed line persists in the avatar's conversation history. The datee only ever sees this final committed line.

## 3. Ephemeral Overlays

If active, several LLM overlays can rewrite the message *in place* before it is delivered:
- **Trap Overlay** (`ApplyTrapOverlayAsync`)
- **Shadow Corruption** (`ApplyShadowCorruptionAsync`)
- **Horniness Overlay** (`ApplyHorninessOverlayAsync`)

*Invariant: Horniness must run LAST (HORNINESS-OVERLAY-MUST-BE-LAST-TEXT-LAYER).*

These calls are stateless string-in/string-out transformations. They do not maintain conversation history, preventing them from leaking into subsequent turns.

## 4. Datee Session

The **Datee Session** generates the datee's response to the player's delivered message.
- **Context**: Datee's system prompt, datee's resistance level, full conversation history, the player's *final delivered message* (with any failure/overlay contexts attached as metadata).
- **Bleed Isolation**: The datee session is completely isolated from the avatar session. It never sees the avatar's internal states, unchosen options, or the original pristine intended text (if it was degraded/corrupted). It only sees what was actually "sent".

## Related Specs
- Detailed turn flow: [`ARCHITECTURE.md`](ARCHITECTURE.md)
- Integration details: [`unity-integration.md`](unity-integration.md)
