# Future Scope (Deferred from Prototype)

This document collects engine-level features that are **out of scope for the current
prototype** but were considered substantial enough to record for a future version.
They were closed on the issue tracker as `deferred` rather than `wontfix` — the
intent is "not now," not "never."

If you pick one of these up later: re-open the original GitHub issue (or open a
fresh one and link back), and remove the corresponding entry here once it lands.

---

## #144 — Energy system has infrastructure but no defined consumer

**Status when deferred:** the engine has plumbing for an energy resource on
`GameSession` / character state, but nothing in the gameplay loop currently
*spends* energy. No actions, abilities, or events gate on it.

**Why deferred:** without a defined consumer, the field is dead weight in the
snapshot schema and adds confusion to anyone reading the engine. The prototype
doesn't need a stamina/exhaustion mechanic to validate the core loop.

**What a future version should decide first:**
- Which actions / scene types cost energy, and how much?
- How does it regenerate (per turn / rest / scene transition)?
- Does it interact with the existing horniness / shadow / steering subsystems,
  or is it orthogonal?
- Is it player-visible, or a hidden pacing knob?

Until that's answered, leave the field unused or remove it.

---

## #261 — ConversationRegistry: multi-session manager + cross-chat shadow bleed

**Status when deferred:** the engine handles a single `GameSession` /
conversation at a time. There's no registry that tracks multiple concurrent
conversations belonging to one character, and no mechanism for "shadow bleed"
(emotional / narrative state leaking between sessions involving the same
character).

**Why deferred:** the prototype's UX is single-session. Multi-session bookkeeping
is a significant architectural addition (storage layout, session lifecycle,
identity model) that doesn't pay off until there's a multiplayer or
multi-partner scenario to drive it.

**What a future version will need:**
- A registry abstraction owning N active sessions per character.
- A defined model for what state is per-session vs. per-character.
- "Shadow bleed" semantics: which subset of session state propagates back to
  the character and forward into other sessions, and on what trigger
  (session end? turn boundary?).
- Persistence story for inactive sessions (warm vs. cold).

---

## #262 — Level-up: build-point allocation and stat progression

**Status when deferred:** characters do not level up. There's no XP-to-level
curve, no build-point pool granted on level-up, and no UI/engine path for
allocating points to stats.

**Why deferred:** the prototype validates the conversation / scene / dice loop.
Long-term character progression is a separate design axis and would require
balancing work that's premature without playtest data from the core loop.

**What a future version will need:**
- An XP source — what awards XP, and how much?
- A level curve (linear / quadratic / table-driven).
- Stat caps and build-point cost rules.
- An allocation flow (auto / player-driven / hybrid) and where it surfaces
  in the client.
- Snapshot schema additions for level, XP, unspent build points.

---

## #263 — Prestige reset (§10 of the design doc)

**Status when deferred:** §10 of the design describes a prestige mechanic — a
voluntary reset that trades current progression for a persistent meta-bonus.
None of it is implemented.

**Why deferred:** prestige is an end-game retention feature. The prototype has
no end-game yet, so a reset mechanic has nothing meaningful to reset *from*.
Implementing it now would mean designing balance numbers against an unfinished
progression curve.

**What a future version will need:**
- The level-up system (#262) landed first — prestige resets level / build
  points / unlocks.
- A defined prestige currency or modifier and where it persists.
- Rules for what carries over and what's wiped (inventory? relationships?
  conversation history?).
- A trigger condition (level cap? quest completion? player-initiated at any
  time?).

---

## Cross-cutting note

#262 → #263 is a natural sequence (level-up first, prestige builds on it).
#261 is independent and gated on multi-character / multiplayer scope (see
also pinder-core #264, *Character server model — upload/download interface
for multiplayer*, which remains open).
#144 is a small cleanup-or-design call that doesn't depend on any of the above.
