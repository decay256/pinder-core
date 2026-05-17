> **SUPERSEDED 2026-05-17 21:45 UTC** by sprint `2026-05-17-bignight`. Daniel asked for a longer sprint; the 13-ticket version was rewritten. See `docs/sprint-runs/2026-05-17-bignight/kickoff.md`.


# Sprint 2026-05-17-280cc8 — Staging-Test Fallout: User-Visible Fixes

**Authorization:** Daniel — "list open tickets and plan a sprint." (2026-05-17 20:47 UTC, #pinder).
Standing-yes for the drain envelope per swarm-drain SELF-UNBLOCK-BY-DEFAULT.

## Theme

Daniel deployed to staging, played a 3-turn test session
(`b79b6331-6698-466e-9fb1-8d859b7cb515`, reuben vs velvet), and surfaced
19 distinct issues — 7 P0/P1 ranging from "math broken on a success
roll" to "session list shows nothing useful." Combined with the
text-on-success-doesn't-make-sense and stake-never-references issues, the
SPA is currently barely usable for an end-to-end playtest.

This sprint focuses **strictly on what blocks the next playtest**.
Anything purely cosmetic, stylistic, or "revisit when stable" stays in
backlog. The 36 open issues triage cleanly:

- 14 in-scope (P0/P1, user-visible, ready to fix)
- 6 deferred this sprint (P2 polish — wait until P0/P1 land + a fresh test)
- 6 cleanup follow-ups (already deferred-by-design; reviewer notes)
- 4 long-tail epic / PAT-blocked
- 5 test-infra debt (own sprint when scheduled)

## Sprint goal

After this sprint merges and re-deploys:
1. A 5-turn playthrough surfaces ZERO outcome/interest math anomalies.
2. The session list shows real outcomes (not "unknown") and working
   replay links for finished sessions.
3. Every EventBox tells the player something useful at a glance.
4. The psychological stake actually shows up in the dialogue.
5. The opening message says the opponent's name, not "scene".

---

## Scope — 14 tickets

### Lane A — P0 root-cause + wire-DTO audit pass (4 tickets, pinder-core; #946 closed)

The P0 must land first. The 4 P1 wire-field tickets are best done as
ONE audit-pass PR (or 2 closely-paced PRs) because they touch the same
DTO mapper.

1. **core#942** [P0] GameEndedException(Ghosted) in turn-prefetch
   state-mutates session; sync fallback then runs a phantom successful
   turn with negative interest. *Estimated 3-4h. Rung 2 default (C#
   substantive, per L3).* **Must merge before re-test.**
2. **core#943** [P1] `roll.tier` wire field absent on successful rolls.
   *Estimated 1h. Likely additive on RollCheckResult + DTO mapper.*
3. **core#944** [P1] `turn_record.trap_activated` missing (not null)
   on turn 3. *Likely fallout of #942 — investigate after #942 lands;
   close as fixed-by-#942 if so, or file a 30-minute serializer fix.*
4. **core#945** [P1] `OfferedOption.dc` / `modifier` null on every
   offered option; breaks `ModifierBagRollFormula` pre-pick.
   *Estimated 1.5h. Wire serializer.*
5. ~~**core#946** [P1] Stat `Chaos` offered on turn 3.~~
   **CLOSED as not-a-bug 2026-05-17** — codebase grep confirmed Chaos
   is canonical (StatType has 6 values; pinder-web already maps it).
   No action needed this sprint.

### Lane B — P1 engine + DB integrity (3 tickets, pinder-core)

These are non-DTO engine bugs that don't depend on #942 but block end-
to-end playtest credibility.

6. **core#948** [P1] All sessions show "outcome unknown" — outcome
   column NULL for every row. *DB write-path bug. ~2h: trace where
   `GameSession.Outcome` is set vs where `user_sessions.outcome` is
   written.*
7. **core#950** [P1] Psychological stake never surfaces in chat
   options. *Could be prompt-engineering OR stake-content quality.
   Investigate first: extract the generated stake from session
   `b79b6331-...`, decide if it's concrete enough; either fix the
   stake-generator prompt or the option-generator prompt. ≤2h.*
8. **core#951** [P1] Opening message contains literal "scene" instead
   of character name. *Grep + substitution-rule fix. ≤45min.*

### Lane C — P1 SPA EventBox surface (6 tickets, pinder-web)

These are tightly related and best landed as ONE coordinated PR per
the dep graph below. Visual parity check on staging before merge.

9. **web#647** [P1] EventBox renders a box for text-only modifications
   with no game consequence (e.g. Meta-Prefix Strip). *Visibility table
   change. Foundational for #648/#649.*
10. **web#648** [P1] Folded EventBox header uninformative (`Horniness
    Check` → `Horniness Miss by 8: Catastrophic!`). *Generalised
    folded-header helper across all event kinds. Depends on #647 for
    visibility table.*
11. **web#655** [P1] Shadow check EventBox doesn't surface the shadow
    type. *Specific case of #648 — most likely collapses into the
    #648 PR. File it separately so the test fixture is explicit.*
12. **web#649** [P1] Expanded EventBox missing consequence + roll
    breakdown + text-mod sections. *Restructure expanded layout.
    Likely needs core#943/#945 to land first so the wire payload has
    every field the expanded surface needs.*
13. **web#652** [P1] Main-roll formula placement: should fold under
    success/miss EventBox, not after intended-message text.
    *Layout-only. Independent. Could be a separate small PR.*
14. **web#650** [P1] Weakness-window hit lacks global FoldableHintBanner
    trigger. *Wiring on the SPA side; core#926 already exposes the
    field. ~1h.*

### Lane D — pinder-web data integrity (1 ticket)

15. **web#651** [P1] Replays unavailable for all sessions. *Companion
    to core#948 — once core writes outcome + share_token correctly,
    SPA needs to surface the replay link from the session-list DTO.
    ~45min after #948 lands.*

### **Total: 13 tickets in scope.** (#946 dropped — not-a-bug.)

---

## Dependency graph

```
core#942 (P0 ghost-decay) ─── must merge BEFORE the next playtest is meaningful
   │
   ├─ enables retest validation of #943, #944, #945
   │
   └─ #944 (trap_activated null) likely closes-as-fixed-by-#942

core#948 (outcome NULL) ────→ web#651 (replays unavailable)
                              │ companion ticket; web fix easier after core write-path works

core#943 (roll.tier)      \
core#945 (option dc/mod)   ──→ web#648 / #649 / #652 / #655
                          /     SPA EventBox surface depends on full wire payload

web#647 (silent diffs) ───→ web#648 ───→ web#655 (specific case)
                            └─→ web#649 (expanded structure)
                            └─→ web#652 (formula placement; independent of #647 but co-located)

web#650 (weakness banner) ── independent (core#926 already merged)
core#950 (stake surfacing) ── independent
core#951 (scene placeholder) ── independent
```

---

## Execution order (sequential, sprint-pacing)

| Order | Ticket | Lane | Est | Rung default | Notes |
|-------|--------|------|-----|--------------|-------|
| 1     | **core#942** | A | 3-4h | R2 | Foundation; everything downstream verifies against it |
| 2     | core#948 | B | 2h | R2 | Independent; can run in parallel-feel but sequential per skill |
| 3     | core#951 | B | 45m | R1 | Small, atomic |
| 4     | core#943 | A | 1h | R1 | DTO additive |
| 5     | core#945 | A | 1.5h | R1 | DTO additive |
| 6     | core#944 | A | 30m | R0 | Audit-first: probably closes as fixed-by-#942 |
| 8     | core#950 | B | 2h | R2 | Prompt + stake-generator audit |
| 9     | web#651 | D | 45m | R0 | Depends on #948 |
| 10    | web#647 | C | 1h | R0 | Visibility table foundation |
| 11    | web#648 + web#655 | C | 2h | R0 | Likely one PR — folded-header surface |
| 12    | web#650 | C | 1h | R0 | Weakness banner; independent |
| 13    | web#652 | C | 1h | R0 | Layout |
| 14    | web#649 | C | 2.5h | R0 | Expanded structure — last; pulls everything together |

**Total estimate: ~21 hours sequential work** (Chaos hour reclaimed). Realistic completion
window (sequential drain with sub-agent latency overhead): one focused
day with the orchestrator running unattended.

---

## Hard rules in effect

- **NEVER-EXIT-MID-DRAIN** (skill rule 18) — orchestrator must not end
  a turn until `final-scoreboard` has been sent.
- **EIGENTAKT-IMPLEMENTER-EXPLICIT-PATHSPECS** (L2 from f57876) —
  explicit `git add` pathspecs only.
- **EIGENTAKT-CHECK-TRACKED-BEFORE-GITIGNORE** (L1 from f57876).
- **EIGENTAKT-COLOCATED-MIRROR-TESTS-IN-SCOPE** (L1 from f57876) —
  co-located test mirrors are part of the source change.
- **EIGENTAKT-VERIFY-TICKET-AGAINST-CODEBASE** (L2 from f57876) —
  every implementer spawn re-validates the ticket's premise against
  the current codebase (`grep`, `git ls-tree`) before accepting the
  spec.
- **EIGENTAKT-DEAD-CODE-NEEDS-CROSS-REPO-GREP** (L5 from f57876) —
  any dead-code candidate is verified by grepping BOTH `pinder-core`
  AND `pinder-web/src,tests/`.
- **L4 DI/wiring integration test required** on plumbing tickets
  (#945 in particular).
- **C# substantive work → Rung 2 default** per L3.
- **DEPLOY-AFTER-MERGE** — after all 14 tickets merge, run
  `./deploy.sh --staging` to verify the next test session is clean.

---

## Open questions

**Q1 — Chaos stat: RESOLVED 2026-05-17.** Codebase grep confirms
Chaos is canonical (StatType enum has 6 values; pinder-web i18n,
StatChip, and guardrail tests already map it). **#946 closed as
not-a-bug.** Original observation was correct behaviour, not a
regression. Sprint scope reduced to 13 tickets.

**Q2 — Stake-surfacing: RESOLVED 2026-05-17.** Investigation pulled
the actual stake from `llm_exchanges[0].system_prompt` — it's vivid
and concrete (Pret A Manger, €42 Camino map, 17 tabs about the band,
deleted thesis, etc.). 1 of 9 offered options across 3 turns
referenced a stake line; gap is in the option-generator's use, not
stake content quality. **Both A + B accepted** per Daniel:

- **A:** Tighten `engine-options-block` prompt to: 'OPTION_C MUST
  quote or paraphrase one of the numbered stake lines verbatim.'
- **B:** Per-session stake-line reference-count tracker; per-turn
  prompt renders 'already referenced / untouched' lists; engine state
  carries `stakeLineReferenceCount: int[]`.
- Integration test: 5-turn synthetic playthrough hits ≥3 distinct
  stake-line references.
- Telemetry: emit `stake_lines_referenced_this_session` in turn audit.

Full ACs on #950.

---

## Out of scope (backlog, not pulled in)

### pinder-core — long-tail / blocked
- **#871** prompt-yaml epic Phases 2-5 (URGENT but its own sprint).
- **#880 / #884 / #929** — 65 pre-existing LlmAdapters test failures
  + flaky Issue527 test. Own sprint when scheduled.
- **#885** CI grep gate (workflow-scope PAT).
- **#920 / #921** [#901 follow-ups] blocked on #901 Phase 2/3.
- **#924 / #925** "revisit when stable".
- **#927** engine-side final_verdict surfacing — important but not
  user-blocking next test.
- **#941** [#883 follow-up] migrate Program.cs to PromptCatalog —
  blocked on #871 Phase 5.
- **#947** [P2] Anthropic prompt cache not hitting via OpenRouter —
  cost/perf, not user-blocking.
- **#949** [P2] Stake prompt → bullet list — cosmetic; revisit when
  #950 lands and we know the stake content is concrete.

### pinder-web — backlog
- **#585** data-drift CI workflow (workflow-scope PAT).
- **#587** admin editor for prompt yamls (#871 Phase 5 deliverable).
- **#612** Storybook coverage for RollEventBox (Phase 5 — own ticket
  size).
- **#619** RollFormula 'option_roll' hardcoded (Phase 5 cleanup).
- **#621** No jsdom render test (deferred-by-design per the ticket).
- **#646** text_diffs discriminator audit — P2 chore; do after #647
  + #648 land.
- **#653** intended === delivered collapse — cosmetic; nice-to-have.
- **#654** re-estimate generation time — UX polish; nice-to-have.

---

## Sediment check

Verified before kickoff:
- pinder-core `main` at `a0fd2c2` (sprint f57876 close + #883 hot-fix +
  L5 lesson append).
- pinder-web `main` at `ae2e4ef` (submodule bump → pinder-core@a0fd2c2).
- No open PRs in either repo.
- No `/tmp/work-*` worktrees.
- Staging stack healthy.
- Prod stack healthy (verified during last deploy's end-of-staging
  cross-stack invariant).

Clean ground.

---

## Companion goals

1. **Validate the NEVER-EXIT-MID-DRAIN + structured-completion contract
   on a complex 13-ticket sprint** with cross-repo + sequential dep
   graph. Previous sprint f57876 ran 17 actions in ~3h and held the
   contract; this one is bigger, with real C# engine work and a real
   P0.

2. **Re-test on staging after merge.** Final scoreboard must include
   a "re-deployed to staging" confirmation and a smoke-session check
   that #942 (the P0) doesn't reproduce.
