# Sprint 2026-05-16-d1d40c — Event-Rendering Completion: Phase 3 Wiring + Wire-Gap Closures

**Authorization:** Daniel — "Plan the remaining stuff as new sprint." (2026-05-16 12:26 UTC)
Q1 answer: **B** (restore visual flair on ModifierBagRollFormula via prop flags).
Standing-yes for the full drain per SOUL.md drain rule once user gives the explicit go.

## Theme

The previous sprint built the unified event-box foundation (RollEventBox, collapsed contract,
atoms library, wire fields) but stopped short of wiring it into production UI — three blocking
decisions (Q1) and one missing wire field (GhostProbabilityPerTurn) held back the last tier.

This sprint completes the loop:
1. **Phase 3 wiring** — migrate TurnResultDisplay to RollEventBox per-kind, restoring visual
   flair (SVG dice, Nat-1/20, D20Pair) via the new prop flags (Q1=B answer).
2. **Wire-gap closures** — GhostProbabilityPerTurn and SteeringRoll.leveraged_stat finally
   plumbed through GameApi DTOs, unblocking the last two deferred tickets (#599, #597).
3. **Event unification Phase 3B** — Drop Annotations frame, pipeline-ordered event stack,
   trap countdown/ghost-risk banner — all three now unblocked.
4. **Test-infra stabilization** — 72 pre-existing failures on pinder-core main due to
   missing `horniness_time_modifiers` key; fix before any C# work starts.

---

## Scope — 12 tickets

### pinder-core (3 → 4 with Q1=E supersession)

- **#909** [bug][test-infra] Pinder.LlmAdapters.Tests has 72 pre-existing failures on main
  (`"game-definition.yaml is missing required key: horniness_time_modifiers"`)
- **#916** [chore] TurnStart.OpponentDefenseSnapshot lacks `[JsonPropertyName]` on outer key
- **#919** [chore] Delete dead `SteeringEngine.RollD20`
- **#932** ★ [feat] Expose `AttackerGroup` / `DefenderGroup` / `DcBase` on `SteeringRollResult`
  (companion to web#629 per Q1=E 2026-05-16; prereq for web#629)

### pinder-web (9)

- **#613** [chore] Export new EventBox components from `frontend/src/components/eventbox/index.ts`
- **#617** ★ [wire-gap] Plumb `GhostProbabilityPerTurn` through GameApi DTO (blocks #599)
- ~~**#625**~~ — superseded by **#629** per Q1=E (2026-05-16). #625 closed.
- **#629** ★ [wire-gap] SteeringRoll wire DTO carries `attacker_group` + `defender_group` +
  dc math (six new fields). TitleCase stat naming. Closes #625. **Depends on core#932.**
- **#610** ★ [#592 Phase 3] Migrate `TurnResultDisplay` to `RollEventBox` per-kind
  — **Q1=B implementation**: extend `ModifierBagRollFormula` with `dieGraphic: 'svg' | 'text'`
  and `flairOnNat: boolean` (default off); option-roll kind passes `dieGraphic='svg'` and
  `flairOnNat=true`; drop `miss_margin` "Beat by X" tail (no flag — math already in formula).
- **#603** [follow-up #904] Frontend mirror of `FailureTierDisplay.Label` per-kind
- **#596** Drop the "Annotations" frame: combo / triple / tell / callback as events,
  route through configurable event widget
- **#597** Unify text-modifying events: trap-overlay as event, diffs inside expandable,
  single pipeline-ordered event stack
- **#599** Active-trap countdown via `ActiveTrapBadge`; ghost-risk banner + red frame at Bored
- **#611** [#592 Phase 4] Delete legacy `RollFormula` / `SteeringBlock` / inline templates /
  duplicated i18n keys

★ = foundational (others depend on it)

---

## Dependency graph

```
pinder-core
  #909 (test-infra) ──── independent
  #916 (JsonPropertyName) ── independent
  #919 (dead code) ─────── independent

pinder-web
  #613 (index.ts export) ─── independent, trivial

  #617 (GhostProbabilityPerTurn wire) ──→ #599 (ghost-risk banner)

  ~~#625 (SteeringRoll.leveraged_stat)~~ — superseded by web#629 per Q1=E (2026-05-16).
  core#932 (SteeringRollResult AttackerGroup/DefenderGroup/DcBase) ──→ web#629
  web#629 (wire DTO carries attacker_group + defender_group + dc math) ──→ #597

  #610 (Phase 3 RollEventBox wiring, Q1=B) ──┬─→ #596 (drop Annotations frame)
                                              ├─→ #597 (pipeline-ordered stack)
                                              ├─→ #611 (Phase 4 cleanup)
                                              └─→ (closes prod gap that made #596/#597 deferred)

  #603 (per-kind tier label frontend) ── depends on core#904 (already merged)
  #596 needs #610
  #597 needs #610 + web#629 (was #625; Q1=E supersession)
  #599 needs #617
  #611 needs #610
```

---

## Sprint plan (rung-paced eigentakt)

### Rung 0 — bugs, quick wins, additive DTOs

1. **core#909** — Fix 72 test failures on main (missing `horniness_time_modifiers` key).
   Must land before any C# ticket to keep build output clean. ≤1h.
2. **core#916** — Add `[JsonPropertyName]` to `OpponentDefenseSnapshot` outer key. ≤30min.
3. **core#919** — Delete `SteeringEngine.RollD20` dead method. ≤20min.
4. **web#613** — Add missing exports to `frontend/src/components/eventbox/index.ts`. ≤20min.
5. **web#617** — Plumb `GhostProbabilityPerTurn` through `GameApi` DTO mapper.
   Follow the `OpponentDefenseSnapshot` pattern from PR #615. ≤1h.
6. ~~**web#625** — Plumb `SteeringRoll.leveraged_stat` through GameApi DTO.~~
   **Superseded** by web#629 per Q1=E (2026-05-16). New plan:
6a. **core#932** — Expose `AttackerGroup` / `DefenderGroup` / `DcBase` on `SteeringRollResult`
    (per-group modifiers are currently locals in `SteeringEngine`; surface them on the result
    so the web DTO can map them through). Additive constructor. DI/wiring integration test
    required (L4). ≤2h. **Must merge before web#629.**
6b. **web#629** — `SteeringRoll` wire DTO carries `attacker_group` + `defender_group` +
    `attacker_modifier` + `defender_modifier` + `dc_base` + `final_dc`. TitleCase stat naming
    (`"Charm"`, `"Wit"`, `"SelfAwareness"`, `"Rizz"`, `"Honesty"`). Closes #625 (superseded).
    Depends on core#932. ≤2h.

### Rung 1 — foundations + wiring

7. **web#610** ★ — Phase 3: migrate `TurnResultDisplay` to `RollEventBox` per-kind.
   Implement Q1=B prop shape on `ModifierBagRollFormula`. Per-kind snapshot tests required.
   Visual parity check on staging for option-roll path before merge. ≤4h.
8. **web#603** — Frontend mirror of per-kind tier label (`FailureTierDisplay.Label`).
   Reads from core#904 wire output already on main. ≤1.5h.

### Rung 2 — substantive surfaces on the foundation

9. **web#596** — Drop the Annotations frame; route combo/triple/tell/callback through
   the unified configurable event widget. Needs #610 merged. ≤3h.
10. **web#597** — Single pipeline-ordered event stack; trap-overlay as event; diffs in
    expandable. Needs #610 + #625. ≤4h.
11. **web#599** — `ActiveTrapBadge` + ghost-risk banner + red frame at Bored.
    Needs #617. ≤3h.
12. **web#611** — Phase 4: delete legacy `RollFormula`, `SteeringBlock`, inline templates,
    dead i18n keys. Needs #610 merged. ≤2h.

---

## New policy in effect for this sprint

- **Split-first on Rung-0 strikeout** (eigentakt commit `4dae567`, 2026-05-16): when a
  ticket fails at Rung 0, the orchestrator moves directly to split-first escalation as a
  fallback rather than blindly retrying at the same rung.
- **OpenRouter same-rung retry before split-first** (eigentakt commit `4dae567`, 2026-05-16):
  one same-rung retry on OpenRouter stream-cut before triggering split-first. Applies to
  OpenRouter-routed rungs only; Rung 2 (direct Anthropic) is not affected.
- **Per-ticket rung isolation** — every ticket starts fresh at Rung 0 default. No mid-sprint
  default bumps (L3 anti-pattern corrected in previous sprint).
- **C# substantive work → Rung 2 default** — per L3 (previous sprint lessons): OpenRouter
  streaming is unreliable for substantive C# refactor work on this codebase. Tickets that
  are clearly non-trivial C# (not pure DTO additions) should default to Rung 2 (direct
  Anthropic) rather than burning two Rung 1 stream-cut retries first. Applies to: any pinder-core
  ticket that touches logic (not just additive DTO fields).
- **DI/wiring integration test required** — per L4 (previous sprint): any ticket where the
  value-add is plumbing a new component into an existing production path MUST include an
  integration test that exercises the production entry point and asserts the new behavior.
  Reverse-verification pattern: reviewer temporarily reverts the wiring change; integration
  test must fail. Applies to: #617, #625, and any future plumbing ticket.
- **Parallelism rules from AGENTS.md** (4 vCPU, load ≤ 2.8, max 2 implementer subagents
  in flight, orchestrator single-threaded).

---

## Open questions before kickoff

**Q1 (resolved — B):** Restore visual flair on `ModifierBagRollFormula` via prop flags.
  ↳ No further action needed; implementation spec in #610 body + questions.md.

**Q1-new (resolved 2026-05-16 — E):** What does `leveraged_stat` carry for steering?
  ↳ Resolved: not a single stat. Wire DTO carries `attacker_group` + `defender_group`
  + the four per-side modifiers + dc math. TitleCase naming. Tickets filed: core#932 (prereq)
  + web#629 (supersedes #625). #625 closed.

**Q2 — #625 cross-repo scope:** RESOLVED. core#932 is the companion ticket;
  it's filed and sequenced ahead of web#629. No "check first" needed — the per-group
  modifiers are confirmed-locals-inside-SteeringEngine; they must be promoted to fields on
  `SteeringRollResult`.

**Q3 — #611 scope (no question, just a flag):**
  `#619` ([chore] RollFormula `option_roll` kind hardcoded) is CLOSED as obsolete by #611 —
  the delete-legacy pass removes the hardcoded string along with the component. No action
  required but confirm implementer reads this.

---

## Carryover from previous sprint — sediment to clean BEFORE start

No stale worktrees or partial branches found on either repo (both at clean main, no lingering
`rung-0-failed/*` or `rung-1-partial/*` branches on origin).

**Docs pass from sprint 2026-05-15-9056ee is NOT YET DONE** — the previous sprint ended
by preserving prompts/ and questions.md but did not formally walk
`docs/documentation-checklist.md`. Two items are explicitly flagged for promotion to
`pinder-web/LESSONS_LEARNED.md`:
- **L4 (DI/wiring discipline + reverse-verification)** — staged for Phase 7 write-back.
- **L3 (OpenRouter streaming unreliability)** — staged for Phase 7 write-back.

These should be written before or as the very first step of this sprint's Phase 7 docs pass.
(They're captured in `pinder-core/docs/sprint-runs/2026-05-15-9056ee/lessons.md` L3 and L4.)

---

## Backlog items triaged out of this sprint

### pinder-core — backlog (keep open, do not pull in)
- **#920** [#901 Phase-2 prep] RollResult.Check nullability — needed only if #901 Phase 2 is
  in scope; it is not this sprint.
- **#921** [chore] Broaden TierLadderAuditTest regex — wait for Phase 2/3 of #901 to land.
- **#924** [chore] Mixed enum serialization shape on RollResult — "revisit when stable."
- **#925** [chore] DefendingRollStat naming — "revisit when stable."
- **#927** [#598 follow-up] Surface final_verdict + final_tier on RollCheckResult — good
  engineering, but frontend has a working workaround in #598; no blocker. Revisit when #901
  Phase 2 is scheduled.
- **#915** [chore] OpponentDefenseSnapshot sealed class vs sealed record — cosmetic.

### pinder-core — drop (sprint-internal hygiene, no longer relevant)
- **#917** [chore] Comment block placement in TurnStart.cs — pure formatting, zero
  behavioral value. Closing with: "Resolved — comment placement is cosmetic; not worth
  a dedicated PR."

### pinder-web — backlog
- **#603** is IN-SCOPE (see above). All others:
- **#606** Runtime guard against EventBox summary re-truncation — defensive, revisit in future.
- **#607** DcDeltaChip + OverrideChip to atoms.yaml — polish pass; backlog.
- **#608** build-i18n.mjs watch mode — developer ergonomics; backlog.
- **#612** Storybook coverage for RollEventBox — nice-to-have; backlog.
- **#614** Wire MODIFIER_LABEL to i18n — chore; backlog.
- **#616** Unused i18n key + aria-label/StatChip drift — chore; backlog.
- **#620** roll_formula.matchup_* unused i18n keys — chore; backlog.
- **#621** No jsdom render test for RollFormula with real defendingStat — backlog.
- **#624** Exhaustiveness guard on deriveCollapsedHeader + Unicode minus — backlog.

### pinder-web — drop (obsoleted by in-scope work)
- **#619** RollFormula `option_roll` kind hardcoded — entirely superseded by #611 (Phase 4
  delete-legacy pass removes the file). Close with: "Obsoleted by #611 (Phase 4 cleanup)."
