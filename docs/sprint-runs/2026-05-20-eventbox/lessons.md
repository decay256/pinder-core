# Sprint 2026-05-20-eventbox — lessons

## L1 — DeepSeek V4 Pro stream-cuts cluster at 12–16m for medium frontend work

Three Rung-0 implementer runs in this sprint cut mid-stream at 12:29s
(#973), 14:40s (#672 attempt 1), 12:38s (#672 attempt 2), 13:37s
(#653), and 16:07s (#612). The pattern:

- Output truncates mid-sentence.
- Stats: `tokens 0 (in 0 / out 0)` (zero-token-stats runtime gap).
- Real wall-clock time consumed.
- Work itself was complete or near-complete in the worktree; only the
  PR-opening / final-commit step was missing.

This is more consistent than "occasional flake." Same-rung retry per
FLAKE-RETRY-BEFORE-ESCALATE worked once (#672 attempt 2) but cut
again at a similar mark — suggesting this is a hard ceiling on
DeepSeek-via-OpenRouter for tasks that exceed ~10m of edit + test
loop work, not a transient outage.

**Forward fix:**
- Bake commit-checkpoint discipline into the `frontend-engineer.md`
  role spec: "after every milestone (skeleton, 5/10/20 fixtures,
  tests passing, build clean), `git add -A && git commit -m '<note>'
  && git push` before moving on."
- The explicit "commit early and often" instruction in the #612 task
  prompt was ignored — needs to be in the role spec itself, not the
  per-ticket prompt.
- Consider lifting the default rung for ANY pinder-web ticket
  estimated >100 LoC to Rung 1 (Gemini 3.5 Flash direct) and
  observing whether the stream-cut pattern disappears.

Lesson ID: `EIGENTAKT-DEEPSEEK-V4-PRO-12M-CEILING-2026-05-20`.

## L2 — Worktree setup MUST init submodule + run i18n:build (pinder-web)

The #612 implementer ran tests in `/tmp/work-612` without
`git submodule update --init --recursive` (which populates
`pinder-core/data/i18n/en/events.yaml`) AND without `npm run
i18n:build` (which regenerates `frontend/src/i18n/generated/en.ts`).
Result: tests failed with `events[kind].summary_variants` undefined
because the stale generated file didn't include `triple_hit` (added
just hours earlier in PR #674). The implementer reported this as
"pre-existing failures" — they weren't.

**Forward fix:** the workspace-setup block in every pinder-web
implementer task must read:

```bash
cd /root/projects/pinder-web
git fetch origin
git worktree add /tmp/work-<N> origin/main
cd /tmp/work-<N>
git submodule update --init --recursive
git checkout -b <branch>
cd frontend && npm run i18n:build && cd ..
```

Add this to `agents/frontend-engineer.md` as the canonical
pinder-web setup block.

Lesson ID: `EIGENTAKT-PINDER-WEB-WORKTREE-NEEDS-SUBMODULE-AND-I18N-BUILD`.

## L3 — Implementers misdiagnose test failures with confidence

DeepSeek V4 Pro on #612 reported "2 remaining failures are
pre-existing in `TurnResultDisplay.annotations.test.tsx` (unrelated
`summary_variants` TypeError)" — confidently wrong on two axes:

- The failures were NOT pre-existing (verified by running the same
  test against origin/main — passed).
- The cause was NOT unrelated to the implementer's own work (it was
  workspace setup; see L2).

A reviewer or orchestrator who took the implementer's claim at face
value would have either shipped broken code or wasted time on a
false trail. The fix is to **never trust a "pre-existing failures"
claim without verifying it against origin/main**, ideally with a
clean checkout.

**Forward fix:** add to `code-reviewer.md`: "if the implementer
claims any test failure is 'pre-existing,' the reviewer MUST verify
the same test passes on origin/main with a fresh checkout before
treating the claim as legitimate."

Lesson ID: `EIGENTAKT-VERIFY-PRE-EXISTING-FAILURE-CLAIMS`.

## L4 — Orchestrator-finishes-work-after-stream-cut is the cheap path

When a stream-cut implementer left committed/pushed work but no PR
(#672 attempts 1+2, #612, #653, partially #672), the orchestrator
finishing the job manually (commit if needed → push → open PR →
verify CI/tests/build → log spawn-recover with operator-provided)
took 3–5 minutes per recovery. Spawning a fresh implementer with the
same prompt would have cost another 10–15m of wall-clock plus
re-burn of tokens.

The criterion for orchestrator-takeover-vs-respawn:
- **Take over** when the work is committed (or trivially committable)
  AND tests/build verify clean AND the remaining work is mechanical
  (commit + push + open PR + spawn reviewer).
- **Respawn** when the work is missing material content, has
  unresolved errors, or requires non-mechanical judgement.

This is squarely inside SELF-UNBLOCK-BY-DEFAULT. Document the
pattern explicitly.

Lesson ID: `EIGENTAKT-ORCHESTRATOR-FINISHES-MECHANICAL-WORK-AFTER-FLAKE`.

## L5 — Cross-repo PR pair: merge-core-then-rebase-web works

Tickets #672 and #973 needed coordinated changes across pinder-core
and pinder-web. The flow that worked:

1. Implementer opens both PRs (one per repo) with the web PR's
   submodule pointer on the unmerged feature branch.
2. Orchestrator reviews + merges the pinder-core PR first.
3. Orchestrator bumps the pinder-web branch's submodule pointer to
   the new merged-main commit (`git submodule update --remote
   pinder-core` then explicit `git checkout <merged-sha>` in the
   submodule, then commit on the web branch).
4. Push the web branch (CI re-runs on the bumped commit).
5. Spawn the web reviewer.
6. Merge the web PR.

Two PRs, sequential merge, no force-pushing main, no rebase-onto-
moved-base headaches. Pattern is robust enough to document as the
standard cross-repo flow.

Lesson ID: `EIGENTAKT-CROSS-REPO-PR-PAIR-MERGE-CORE-THEN-REBASE-WEB`.

## L6 — Obsolete tickets get closed-with-explanatory-comment, not implemented

Tickets #619 and #621 (both pinder-web "#601 follow-up" chores)
referenced a `RollFormula.tsx` component that had already been
deleted in PR #636 (#611 Phase 4) several days before this sprint.
The hardcode the tickets called out was deleted with the file; the
missing render-test concern was overtaken by `ModifierBagRollFormula`
having its own unit-test suite plus the `EventBox.regression.test.tsx`
landed this sprint by #672.

Closing with a clear explanatory comment ("Obsolete after PR #636,
specifically because X / verified by Y") + no code change is the
correct disposition. Spawning an implementer for ghost-work would
have wasted ~15m wall-clock on confused output. Skip-classify these
at Phase 1, not after a wasted Phase 4 spin.

**Forward fix:** Phase 1 triage should explicitly grep the codebase
for the file each follow-up ticket references; if the file is gone,
close as obsolete before the sprint even reaches Phase 4.

Lesson ID: `EIGENTAKT-OBSOLETE-FOLLOWUP-TICKETS-CLOSE-IN-TRIAGE`.

## L7 — Orchestrator-pin yaml change mid-sprint is non-disruptive

Daniel committed an exploratory orchestrator-pin change to
`model-routing.yaml` (Opus 4.7 → Gemini 3.5 Flash direct) at
2026-05-20T11:18Z, during the #672 spawn cycle. The yaml-sha gate
on `spawn-with-routing.sh` correctly aborted the next spawn with
exit 11. Re-running `load-routing.sh` cleared the gate and the
sprint continued without disruption.

The exploratory change applies to FUTURE orchestrator spawns; the
in-flight orchestrator (this run, on Opus) was unaffected. The gate
mechanism worked exactly as designed — small operational friction
(one extra command) in exchange for catching real config drift
deterministically. Keep the gate.

Lesson ID: `EIGENTAKT-YAML-SHA-GATE-MID-SPRINT-WORKS`.

## Summary

- 9 tickets in scope; 7 shipped via PR (#968 #964 #973 #672 #669 #653 #672-core #612), 2 closed as obsolete (#619 #621). All 9 issues now CLOSED.
- 10 PRs merged: core #975 #977 #978; web #673 #674 #675 #676 #677 (plus the closing-comment-only path for #619/#621).
- 1 follow-up filed: pinder-core #976 (engine-side i18n population for the #964 wire field).
- 5 stream-cuts on DeepSeek V4 Pro at Rung 0 — all recovered via orchestrator-takeover; no escalation to Rung 1 needed for any ticket.
- 0 questions queued.
- Wall-clock total: ~2h40m (10:10Z → 12:50Z).
