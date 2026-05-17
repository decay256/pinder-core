# Sprint 2026-05-17-f57876 — Cleanup Drain

**Authorization:** Daniel — "Make a cleanup sprint." (2026-05-17 12:34 UTC, #pinder)
Standing-yes for the full drain per swarm-drain SELF-UNBLOCK-BY-DEFAULT.

## Theme

Sprint `2026-05-16-d1d40c` left a tail of small chores and three pairs of
duplicate issues. None individually merit a sprint; together they're a
focused mechanical drain. **No architectural decisions, no breaking changes,
no epic scope** — everything in scope is either a duplicate close, a
≤1-file deletion, a wire-the-i18n-key-the-reviewer-flagged, or a
short-tail follow-up that was deferred from its parent PR.

Companion goal: prove the new NEVER-EXIT-MID-DRAIN + structured-completion
contract holds across a higher-cardinality (20-action) drain. Last sprint
hit zero violations; this one should too.

## Scope — 20 actions

### Lane A — Duplicate closes (6 issues, ~5 min total, zero PRs)

These three pairs are pure duplicates; the first version stays open, the
other two close-as-duplicate via `gh issue close --reason "not planned"`
with a one-line comment pointing at the survivor.

- **core#886** [infra] Wire prompt-content CI grep gate — **duplicate of core#885**
- **core#888** [infra] Wire prompt-content CI grep gate — **duplicate of core#885**
- **web#588** [admin] Expose prompt yamls in /admin editor — **duplicate of web#587**
- **web#589** [admin] Expose prompt yamls in /admin editor — **duplicate of web#587**

Action: orchestrator closes these four duplicates inline (no implementer
subagent needed — pure `gh issue close` + comment). The four survivors
(core#885, web#587) are also out-of-scope for *this* sprint but are
flagged in §Carryover.

### Lane B — pinder-core mechanical chores (5 tickets)

5. **core#935** — Retire orphan i18n key `turn_result.active_trap_prefix`.
   Delete from `data/i18n/en/ui-turn-result.yaml`, rebuild i18n, verify
   `grep -r active_trap_prefix` returns nothing under `frontend/`. ≤20min.
6. **core#917** — Move new `// Fields covered by TurnSnapshot:` comment
   block from between using-directives and class-docstring to the
   matching field-manifest comment in SessionSnapshot.cs. Pure
   formatting; tests untouched. ≤20min.
7. **core#915** — Convert `OpponentDefenseSnapshot` + `OpponentDefenseEntry`
   from `sealed class` to `sealed record`. Functionally equivalent under
   System.Text.Json. Verify `OpponentDefenseSnapshotDtoTests` still
   matches expected serialized shape. ≤30min.
8. **core#877** — Add `<param name="allowInsecureBaseUrl">` xmldoc to
   `Pinder.RemoteAssets.Configuration` ctor. Mirror the language already
   on the `BaseUrl` property xmldoc. ≤15min.
9. **core#883** — Delete dead-code `ArchetypeYamlLoader.LoadFromYaml()`
   (only caller is the orphaned `Issue372_ArchetypeYamlLoaderTests`).
   Delete the test too if the whole class becomes unreferenced. ≤30min.

### Lane C — pinder-web mechanical chores (9 tickets)

10. **web#624** [#598 follow-up] Exhaustiveness guard on
    `deriveCollapsedHeader` (add `default: const _exhaustive: never = kind`)
    + reconcile Unicode minus vs ASCII. ≤30min.
11. **web#620** [#601 follow-up] Delete unused `roll_formula.matchup_*`
    i18n keys from atoms.yaml + roll_formula.yaml. Verify no live grep
    references. Mark with #601-fork note in PR body explaining why
    delete rather than wire. ≤20min.
12. **web#616** [#594 follow-up] Two items: (a) delete unused
    `opponent_card.defends_with_collapse_aria` i18n key OR wire it (pick
    delete unless trivial wire opportunity); (b) reconcile row
    `aria-label` to use the same alias the `<StatChip>` uses. ≤45min.
13. **web#614** [#592 follow-up] Wire `MODIFIER_LABEL` map to
    `t('modifier.<key>')` instead of hardcoded English. Mirror existing
    i18n-build pipeline; add the keys to `roll_formula.yaml` or
    `atoms.yaml`. ≤45min.
14. **web#608** [#600 follow-up] Extend `build-i18n.mjs` watch mode to
    also watch `frontend/src/i18n/local/en/*.yaml`. Add the second
    `chokidar.watch` registration; verify by adding a key to a local
    overlay file and seeing the dev build rebuild. ≤30min.
15. **web#607** [#600 follow-up] Wire `DcDeltaChip` + `OverrideChip` to
    atoms.yaml. Mirror the `CountdownChip` pattern (the ticket's gold
    reference). ≤45min.
16. **web#606** [#600 follow-up] Add runtime guard against EventBox
    summary re-truncation. Two pieces: (a) a regression test that
    asserts `EventBoxSummary` does not carry the `truncate` class;
    (b) optional runtime assert in dev (warn if computed class list
    contains `truncate`). ≤45min.
17. **web#631** Remove deprecated `SteeringRoll.modifier` and
    `SteeringRoll.dc` aliases after SPA migration to the new
    `attacker_modifier` / `final_dc` etc. fields. Verify no SPA caller
    still reads the legacy fields (grep frontend); update wire DTO
    contract test. ≤45min.

### Lane D — Cross-repo cleanup tracker (1 issue)

18. **No corresponding GitHub issue for the L3/L4 carryover.** See §Open
    questions Q1.

---

## Dependency graph

All Lane B + Lane C tickets are independent of each other (each touches
a different file or a non-overlapping section of `RollEventBox`-family
components). They can run in any order. Lane A is independent and runs
first (5 min).

**One soft ordering preference:** run #631 (steering wire-DTO legacy
removal) last in Lane C in case Daniel wants to bump the
`SteeringRoll.modifier`/`.dc` deprecation window further. Otherwise no
constraints.

---

## Sprint plan — sequential, single rung

This sprint is small enough to skip the rung-escalation game. **Every
ticket defaults to Rung 0** (cheap models). C# tickets in Lane B
(#915, #883) go to Rung 1 default per L3 (OpenRouter streaming is
unreliable for C# substantive work) — except #915 which is a pure
type-keyword swap and can stay Rung 0. The four xmldoc/comment-move
tickets (#917, #877, plus the deleter-tickets) are pure-text and stay
Rung 0.

| Lane | Tickets | Rung | Est. wall time |
|------|---------|------|----------------|
| A    | 4 dupe-closes (inline) | n/a — orchestrator inline | 5 min |
| B    | core#935, #917, #915, #877, #883 | Rung 0 (R1 for #883 if dotnet build flakes) | ~2h |
| C    | web#624, #620, #616, #614, #608, #607, #606, #631 | Rung 0 | ~5h |
| **Total wall** | 13 PRs + 4 dupe-closes | | **~7h sequential** |

Phase 7 docs pass: not required for any ticket in scope (all internal
refactors / dead-code deletes / i18n cleanup / xmldoc). Phase 8
scoreboard normal.

---

## Hard rules in effect (per swarm-drain skill v2026.5.7)

- **NEVER-EXIT-MID-DRAIN** (new rule 18): orchestrator never ends a turn
  while drain work remains. Only legal terminal output is the
  **Structured completion contract** YAML block after `final-scoreboard`.
- **EIGENTAKT-IMPLEMENTER-EXPLICIT-PATHSPECS** (L2 from previous sprint):
  every implementer prompt MUST forbid `git add .` / `git add -A` and
  list explicit file paths.
- **EIGENTAKT-CHECK-TRACKED-BEFORE-GITIGNORE** (L1 from previous sprint):
  before any subagent is instructed to .gitignore or delete a file, the
  orchestrator MUST verify the file is not tracked in `origin/main`.
- **L4 DI/wiring integration test** (carryover): not applicable in this
  sprint — none of the in-scope tickets are plumbing.

---

## Open questions

**Q1 — L3/L4 promotion to `pinder-web/LESSONS_LEARNED.md`.** The
previous sprint deferred promoting two eigentakt lessons (L3 OpenRouter
streaming unreliability, L4 DI/wiring reverse-verification) from
`pinder-core/docs/sprint-runs/2026-05-15-9056ee/lessons.md` to
`pinder-web/LESSONS_LEARNED.md`. Sprint 2026-05-16-d1d40c made a
deliberate scope decision NOT to promote, on the grounds that those are
eigentakt/orchestrator lessons rather than project-domain lessons, per
the AGENTS.md convention. **Confirm scope decision for this sprint:**

- **A: Keep deferred.** Leave them in the sprint-runs lessons file
  only; eigentakt lessons don't live in project LESSONS_LEARNED.md.
  **(Recommendation.)**
- **B: Promote.** Add a "Process / orchestration lessons" section to
  `pinder-web/LESSONS_LEARNED.md` and copy L3 + L4 in.
- **C: Promote with rephrasing.** Promote, but rewrite to be project-
  agnostic (drop eigentakt-specific phrasing).

Default if no answer by sprint start: A.

**Q2 — Workflow-scope PAT for core#885 + web#587.** Both surviving CI/
admin tickets are blocked on the orchestrator's `gh` PAT lacking
`workflow` scope. **Out of scope for this sprint.** Flagged here so
Daniel can decide whether to issue a new PAT or move those tickets to
a human-driven sprint.

---

## Carryover (kept open, NOT pulled in)

### pinder-core — open, deferred

- **#871** [arch] Finish #843 prompt-yaml epic Phases 2-5 — URGENT but
  too big for cleanup scope. Needs its own dedicated sprint.
- **#929 / #880 / #884** — test-infra debt (65 LlmAdapters failures +
  flaky Issue527 test). Needs a focused test-infra sprint.
- **#927** Surface final_verdict + final_tier on RollCheckResult — engine
  change, not pure chore.
- **#921 / #920** [#901 follow-ups] — blocked on #901 Phase 2/3 landing.
- **#925 / #924** — "revisit when stable", stay parked.
- **#885** [infra] CI grep gate — blocked on workflow-scope PAT (Q2).
- **#915** is IN-SCOPE this sprint (Lane B).

### pinder-web — open, deferred

- **#619** [#601 follow-up] `option_roll` kind hardcoded — needs
  ModifierBagRollFormula handover from RollFormula; better done as part
  of the Phase 5 Storybook ticket (#612), not as standalone cleanup.
- **#621** [#601 follow-up] No jsdom render test — repo's design choice
  per the ticket body itself ("no urgent action"). Keep open but
  out-of-scope as won't-fix-by-design candidate; revisit if the project
  ever adopts jsdom.
- **#612** [#592 Phase 5] Storybook coverage — bigger than cleanup; full
  Phase-5 deliverable. Needs its own scope.
- **#585** [infra] data-drift CI workflow — blocked on workflow-scope PAT (Q2).
- **#587** [admin] Expose prompt yamls in /admin editor — Phase 5 epic
  deliverable C; needs design pass + admin editor surface design, not
  cleanup.

---

## Sediment check

Verified before kickoff (no implementer subagent yet):

- pinder-core local clones (`/root/projects/pinder-core`,
  `/root/.openclaw/agents-extra/pinder/`) both at `1c68f4a` (sprint
  d1d40c closure commit).
- pinder-web local clone (`/root/projects/pinder-web`) at `6a57124`
  (#611 Phase-4 cleanup merge).
- No `/tmp/work-*` worktrees present.
- No stale `feat/*` branches on either local clone (the d1d40c sprint
  closed clean).
- No open PRs on either repo from the previous sprint scope.

Clean ground.
