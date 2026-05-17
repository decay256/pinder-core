# Sprint 2026-05-17-197af9 — Big-Night Drain

**Authorization:** Daniel — *"Prep a sprint, make it long!"* (2026-05-17 21:45 UTC, #pinder).
Standing-yes for the full drain envelope per SELF-UNBLOCK-BY-DEFAULT.

## Theme

Three drain threads in one overnight run:

1. **Staging-test fallout** — every P0/P1 ticket Daniel surfaced during
   the 2026-05-17 staging deploy + 3-turn playtest. 12 tickets across
   `pinder-core` (engine, prompts, DB integrity) and `pinder-web` (SPA
   EventBox surface). Closes out the entire test-review.
2. **Test-infra debt mini-pass** — the LlmAdapters baseline failures
   that have been sitting on `main` for weeks. 3 tickets that unblock
   reviewer signal (without it, every implementer's "tests green"
   evidence is muddied by the baseline noise).
3. **Long-tail follow-up chores** — small mechanical tickets deferred
   from previous sprints. Each is `≤45 min`, none are blocked, and
   knocking them out clears the backlog tail so the next sprint starts
   with a clean board.

This is intentionally **longer than yesterday's two drains combined**
to keep the orchestrator running through the night and gather real
eigentakt calibration data (trigger thresholds, cache-hit ratios,
flake-retry rates) for tomorrow's Phase 0.5 `trigger-calibration.json`
review.

## Sprint goal

After this sprint merges and re-deploys to staging:

- A 5–10 turn playthrough surfaces ZERO of the outcome/interest math
  anomalies, EventBox UX dead-ends, or stake-surfacing gaps from the
  2026-05-17 test review.
- Session-list shows real outcomes (not "unknown") and working replay
  links for finished sessions.
- The psychological stake actually shows up across multiple options
  per session; opening message uses the opponent's name.
- pinder-core `dotnet test` baseline is green (or the residual flake
  is isolated and documented).
- Backlog tail is short enough that the next sprint can focus on the
  one big remaining epic (#871 prompt-yaml Phases 2-5).

---

## Scope — 28 tickets across 3 threads + 1 deploy step

The eigentakt orchestrator runs every implementable ticket through
the refiner (Phase 0.5) before triage closes; the refiner is fully
automatic and will note `## Refiner assumptions` on any ticket that
needs disambiguation. **No tickets in this scope are expected to
need pre-sprint operator input.** Refiner picks defaults.

### Thread 1 — Staging-test fallout (12 tickets)

The P0 is foundational; everything in this thread re-verifies against
it.

- **`core#942`** [P0] GameEndedException(Ghosted) in turn-prefetch
  state-mutates session; sync fallback runs a phantom successful turn
  with negative interest. **Foundation.**
- `core#943` [P1] `roll.tier` wire field absent on successful rolls;
  breaks SPA per-kind FailureTierDisplay.Label.
- `core#944` [P1] `turn_record.trap_activated` missing (not null) on
  turn 3. Audit-first; may close as fixed-by-#942.
- `core#945` [P1] `OfferedOption` wire DTO emits dc=null and
  modifier=null on every option; breaks ModifierBagRollFormula
  pre-pick rendering.
- `core#948` [P1] All sessions show "outcome unknown" — `outcome`
  column NULL for every row in pinder_staging.
- `core#950` [P1] Psychological stake never surfaces in chat —
  zero references across 3-turn test. Resolved scope: do A+B both
  (stronger OPTION_C prompt + per-session stake-line budget tracker
  + telemetry). See ticket comment for full ACs.
- `core#951` [P1] Opening message contains literal "scene" instead
  of the opponent's name.
- `web#647` [P1] EventBox renders a box for text-only mods with no
  game consequence (e.g. Meta-Prefix Strip); should be silent.
  Foundational for #648/#649.
- `web#648` [P1] Folded EventBox header uninformative ("Horniness
  Check" → "Horniness Miss by 8: Catastrophic!"). Generalised
  folded-header helper across all event kinds. Likely absorbs #655.
- `web#649` [P1] Expanded EventBox lacks: consequence in plain
  language, roll/stats/formula breakdown, and what text was modified.
- `web#650` [P1] Weakness-window hit lacks global FoldableHintBanner
  trigger.
- `web#651` [P1] Replays unavailable for all sessions. Companion to
  core#948.
- `web#652` [P1] Main-roll formula should fold UNDER the
  success/miss EventBox, not after the intended-message text.
- `web#655` [P1] Shadow check folded header doesn't surface the
  shadow type (Dread/Denial). Likely collapses into #648.

### Thread 2 — Test-infra debt (3 tickets)

These have been sitting on `main` long enough that every implementer
gets noise in its DoD evidence. Knock them out so reviewer signal is
clean for the next sprint.

- `core#929` [bug] Pinder.LlmAdapters.Tests: 65 pre-existing failures
  across Anthropic/SessionDocumentBuilder/Issue211/372/489/491/544/
  864/865/Issue311/OpenAi.
- `core#880` [chore] 63 tests failing on main in
  `Pinder.LlmAdapters.Tests` — pre-existing tech debt (likely the
  same failures as #929 from a different filing angle; refiner will
  reconcile and dedupe).
- `core#884` [flaky-test] `Issue527_SessionRunnerBioFormatTests`
  flakes when run alongside YamlDotNet-loading tests.

### Thread 3 — Long-tail mechanical chores (13 tickets)

All small, all independent, all `≤45 min`. Lane is intentionally
fat so the orchestrator has a long Rung-0 tail to gather calibration
data.

**pinder-core:**

- `core#920` [#901 Phase-2 prep] RollResult.Check nullability vs
  constructor default.
- `core#921` [#901 follow-up] Broaden TierLadderAuditTest regex once
  Phase 2/3 land. (May close as still-blocked if Phase 2/3 isn't
  scheduled — refiner decides.)
- `core#924` [#906 follow-up] Mixed enum serialization shape on
  RollResult.
- `core#925` [#906 follow-up] DefendingRollStat naming in
  TurnSnapshot — revisit when stable. (May close as still-blocked
  — refiner decides.)
- `core#927` [#598 follow-up] Surface `final_verdict` +
  `final_tier` on RollCheckResult (engine-side).
- `core#947` [P2] Anthropic prompt cache not hitting on OpenRouter
  — cache_read=0 every turn; input token usage grows turn-over-turn.
  **Companion goal:** the Phase 6.5 cache-audit artifact (hazard #28
  CACHE-PREFIX-STABILITY) is the right tool to verify this is fixed.
- `core#949` [P2] LlmStakeGenerator default prompt should ask for
  bullet list.

**pinder-web:**

- `web#646` [P2] `turn_record.text_diffs[*]` discriminator field
  audit — `layer` vs `kind`.
- `web#653` [P2] When delivered = intended (no overlays / no diff),
  show "intended = delivered" instead of duplicating the text.
- `web#654` [P2] Estimated message-generation time should
  re-estimate if elapsed exceeds the estimate.
- `web#619` [#601 follow-up] RollFormula `option_roll` kind
  hardcoded; remove when ModifierBagRollFormula takes over.
- `web#621` [#601 follow-up] No jsdom render test for RollFormula
  with real `defendingStat`. (Repo policy is "deferred to Playwright
  e2e" per the ticket itself — refiner may close as won't-fix.)
- `web#612` [#592 Phase 5] Storybook / component-gallery coverage
  for RollEventBox.

### Post-merge step (always run, not a ticket)

After all 28 tickets are merged (or closed-as-superseded /
won't-fix), re-deploy to staging:

```
cd /root/projects/pinder-web && \
  git submodule update --init --remote pinder-core && \
  git add pinder-core && \
  git commit -m "chore: bump pinder-core submodule to <sha> (post-bignight-sprint)" && \
  git push origin main && \
  ./deploy.sh --staging
```

(The orchestrator handles the submodule bump and deploy from inside
Phase 7 hygiene. Cross-stack invariants per #537/#538 fire; abort if
prod stack regresses.)

---

## Out of scope (backlog, NOT pulled in)

- `core#871` [arch] Prompt-yaml epic Phases 2-5 — URGENT but its
  own dedicated sprint. Not implementable in one drain without
  reshaping the option-stream pipeline.
- `core#885` [infra] CI grep gate — needs workflow-scope PAT.
- `core#941` [#883 follow-up] Delete LoadFromYaml after
  PromptCatalog migration — blocked on #871 Phase 5.
- `web#585` [infra] Data-drift CI workflow — needs workflow-scope
  PAT.
- `web#587` [admin] Expose prompt yamls in /admin editor — #871
  Phase 5 deliverable C.

---

## Hard rules in effect (eigentakt SKILL.md, rules 1-28)

- **Anti-Stall Invariants** (top of SKILL.md): always start at Rung 0;
  refiner is fully automatic; no pre-flight "shall I proceed?" upstream
  message; pick the most reversible default and log it; only documented
  pauses (provider preflight fail, rebase conflict needing implementer
  rework, destructive infra, manual ops).
- **ORCHESTRATOR-MUST-BE-SUBAGENT** (rule 24): orchestrator runs via
  `sessions_spawn` with `runtime: subagent`, `context: isolated`,
  `mode: session`. Auto-abort with `ORCHESTRATOR-INLINE-DETECTED` if
  invoked inline.
- **CONTEXT-BUDGET-GUARD** (rule 26): 180k hard abort, writes
  `continuation-context.md`. Parent (pinder agent) handles the
  `context-budget-abort` upstream event by respawning with
  `continuation_mode: true`.
- **FLAKE-RETRY-BEFORE-ESCALATE** (rule 25): empty-token / killed-
  no-progress / stream-cut runs retry at the same rung up to 2 times
  before escalation. Calibration data.
- **MODEL-ROUTING-MUST-RESOLVE-BEFORE-SPAWN** (rule 23): every
  spawn via `scripts/spawn-with-routing.sh`. Phase 0.5 must run
  `scripts/load-routing.sh` first.
- **CACHE-PREFIX-STABILITY** (rule 28): Gate 4 prefix lint in
  `spawn-with-routing.sh`; Phase 6.5 writes `cache-audit.md`. This
  sprint directly tests #947's cache-hit hypothesis.
- **LOGGING-GATE-WITH-RECOVERY** (rule 27): `attempt-end` is gated
  at the wrapper layer; `scripts/spawn-recover.sh` is the supported
  escape.
- **PER-TICKET RUNG ISOLATION**: every ticket starts fresh at Rung 0
  regardless of how the previous ticket escalated.
- **EIGENTAKT-IMPLEMENTER-EXPLICIT-PATHSPECS** (L2 from f57876):
  explicit `git add` pathspecs only.
- **EIGENTAKT-CHECK-TRACKED-BEFORE-GITIGNORE** (L1 from f57876).
- **EIGENTAKT-COLOCATED-MIRROR-TESTS-IN-SCOPE** (L1 from f57876):
  co-located test mirrors are part of the source change.
- **EIGENTAKT-VERIFY-TICKET-AGAINST-CODEBASE** (L2 from f57876).
- **EIGENTAKT-DEAD-CODE-NEEDS-CROSS-REPO-GREP** (L5 from f57876):
  grep both pinder-core AND pinder-web/src,tests/ before declaring
  any symbol unused.

---

## Pre-answered Qs from earlier sprint plan

These were carried into this sprint from the superseded `280cc8`
kickoff. Refiner will reconcile against the live ticket bodies if
needed.

**Chaos stat (carried-over Q1, RESOLVED):** Pinder is a 6-stat
system. `Charm`, `Rizz`, `Honesty`, `Chaos`, `Wit`, `SelfAwareness`.
Source: `StatType.cs:7-13` enum. Pinned in pinder AGENTS.md.

**Stake surfacing (carried-over Q2, RESOLVED):** Do A+B together —
stronger OPTION_C prompt + per-session stake-line budget tracker +
turn-audit telemetry (`stake_lines_referenced_this_session: [...]`).
Integration test: 5-turn synthetic playthrough hits ≥3 distinct
stake-line references. Full ACs on `core#950` comment.

---

## Sediment check (verified 2026-05-17 21:45 UTC)

- pinder-core main: `1e68cb5` (sprint 280cc8 kickoff superseded by
  this one; previous d1d40c + f57876 close commits intact).
- pinder-web main: `ae2e4ef` (submodule → pinder-core@a0fd2c2).
- No open PRs in either repo.
- No `/tmp/work-*` worktrees.
- Staging stack healthy (last verified ~5h ago; orchestrator's
  Phase 4.5 hygiene sweep will re-verify before draining).
- Prod stack healthy (cross-stack invariant from previous deploy).
- Cron watchdog `pinder-sprint-watchdog` enabled, every 15min, silent
  when audit reports DONE. Will fire if this sprint stalls overnight.

---

## Companion goals — calibration

This sprint is **explicitly long** to gather calibration data. Phase
6.5 will write:

- `trigger-calibration.json` — observed vs seed thresholds for every
  trigger that fired (token-explosion, wall-clock-overrun,
  3-strike-review). Daniel reviews next morning; if `approved_by_human:
  true`, the next sprint applies them.
- `cache-audit.md` (hazard #28) — per-role × per-rung cache-hit
  ratios. Directly addresses `core#947` (prompt cache not hitting):
  if the audit shows green for implementer/reviewer roles, the
  hypothesis was wrong; if red, the prefix lint will tell us where
  the drift is.
- The PR-history sediment (failed-attempt branches, draft PRs,
  superseded labels) stays preserved until human signoff per
  PRESERVE-SEDIMENT-UNTIL-SIGNOFF.

---

## Phase 8 final scoreboard expectations

Structured completion block per skill §"Structured completion
contract":

```yaml
sprint_status: done
sprint_id: 2026-05-17-197af9
tickets_merged: [...]
tickets_skipped: [{number: N, reason: "..."}]
open_prs: []
final_scoreboard_sent: true
follow_ups_filed: [...]
lessons_captured: [...]
hygiene: { start: {...}, end: {...} }
questions_queue_size: 0    # refiner is fully automatic; this should be 0
duration_minutes: <int>
blocker: null
```

Plus the standard scoreboard.md + lessons.md + cache-audit.md +
trigger-calibration.json artifacts under
`docs/sprint-runs/2026-05-17-197af9/`.

---

## Estimated wall-time

- 12 staging-test fallout tickets (Thread 1): ~10h (one P0 + 11
  P1s, several SPA tickets that may run in parallel inside one PR).
- 3 test-infra tickets (Thread 2): ~4h (the LlmAdapters baseline
  is real triage work; #884 may resolve via a test-isolation fix).
- 13 long-tail chores (Thread 3): ~8h.
- Phase 6.5 + Phase 7 + Phase 8 + post-merge deploy: ~2h.
- **Total: ~24h sequential** with eigentakt's per-ticket Rung 0
  default + flake-retry budget + reviewer offset.

Realistic completion in real time: 6-10 hours wall with sub-agent
latency overhead, depending on how many tickets escalate to Rung 2+
and how many refiners produce closable-as-won't-fix decisions on the
"revisit when stable" chores.

Cron watchdog at 15-min intervals will surface any stall before
morning.
