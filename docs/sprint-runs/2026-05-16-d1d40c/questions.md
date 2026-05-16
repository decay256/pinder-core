# Sprint 2026-05-16-d1d40c — Questions Queue

## Q1 — [#625] What semantic value does `leveraged_stat` carry for a steering roll? — **RESOLVED 2026-05-16**

**Daniel's answer: E (none of the offered options).** The steering wire DTO does NOT carry a single `leveraged_stat`. Instead, it carries BOTH stat-group averages plus the DC math so the SPA can render the actual roll formula:

- `attacker_group: ["Charm", "Wit", "SelfAwareness"]`
- `attacker_modifier: int` (effective average modifier on player side, = current `SteeringMod`)
- `defender_group: ["SelfAwareness", "Rizz", "Honesty"]`
- `defender_modifier: int` (effective average modifier on opponent side)
- `dc_base: int` (= 16 today)
- `final_dc: int` (= `dc_base + defender_modifier`, = current `SteeringDC`)

**Stat naming — hard rule.** Must be consistent across all entries and all wire DTOs. Daniel offered two acceptable conventions (short-CAPS or full-upper); the codebase already uses neither — it uses **TitleCase `StatType.ToString()`** (`"Charm"`, `"Wit"`, `"SelfAwareness"`, `"Rizz"`, `"Honesty"`) in `OpponentDefenseSnapshotDto` and existing `SteeringRollResult`. The SKILL rule ("use whichever convention the rest of the codebase already uses") selects TitleCase. Orchestrator surfaced this back to Daniel in #pinder · dev log at sprint resume; proceeding with TitleCase unless overridden.

**Tickets filed:**
- `decay256/pinder-core#932` — expose `AttackerGroup` / `DefenderGroup` / `DcBase` on `SteeringRollResult` (prerequisite).
- `decay256/pinder-web#629` — wire DTO carries the six new fields (supersedes #625).
- #625 closed as superseded by #629.

**Dep graph update:** #597 now depends on web#629 (not #625). #629 depends on core#932.

---

### Original Q1 framing (for the record)

**Context:** #625 asks for a `leveraged_stat: string` field on the `SteeringRoll` wire DTO, so the SPA's collapsed event-box can show it as the Subject. The current `deriveSteeringHeader` falls back to `'—'`.

**Problem:** the steering mechanic uses an *average* of three stats:
- Player side: average of CHARM + WIT + SelfAwareness effective modifiers.
- Opponent (DC) side: 16 + average of SelfAwareness + RIZZ + HONESTY effective modifiers.

There is no single "leveraged stat" today. The issue body says "opponent's leveraged stat" but the opponent has 3 stats contributing to steering DC.

**Options the orchestrator considered:**
- **A: `SelfAwareness`.** The only stat that appears on *both* sides of the steering check (player's avg + opponent's DC avg). Thematic centre of steering (aware social maneuvering toward meeting up). Hard-coded; never varies per turn.
- **B: Opponent's highest-mod defending stat among {SA, RIZZ, HONESTY}.** Per-turn, dynamic. Makes the Subject change meaningfully across turns.
- **C: Whatever stat drove the *option* the player just played (the option-roll's attacker stat).** Most semantically connected to *this* turn's player action, but conceptually weird since steering is a separate roll.
- **D: Drop the requirement entirely.** Steering events have a fixed subject like "Steering check" / "Conversation pivot" — a *label*, not a per-turn stat. `'—'` fallback stays but the event-box subject reads as something more meaningful.

**Recommendation:** **D** is most honest — steering doesn't *have* a leveraged stat in the mechanic. Option B is second-best if you want dynamism. Reversibility: schema changes are reversible but expensive once wire-shipped; the most reversible is D (no wire change at all, frontend uses a hard-coded label).

**Blast radius if deferred:** #625 stays open; #597 ("unify text-modifying events, single pipeline-ordered event stack") explicitly depends on #625 in the kickoff dep graph. So #597 also blocks until this is answered. #599 (ghost-risk banner) and #611 (Phase 4 cleanup) are NOT blocked by #625.

**Path forward in this sprint (updated 2026-05-16):**
- ~~Skip #625 and #597 for now.~~ Superseded — see RESOLVED block above.
- Continue draining: #610 (Phase 3 wiring, Q1=B — PR #628 in review), #603 (per-kind tier label), #599 (ghost-risk banner — unblocked by the now-merged #617), #611 (cleanup), core#932 (R0/R2), web#629 (R0), #596, #597 (now needs web#629).
- ~~Surface this question at sprint review or when Daniel is next available.~~ Resolved 2026-05-16.
