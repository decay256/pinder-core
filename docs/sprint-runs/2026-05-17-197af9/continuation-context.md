# Sprint 2026-05-17-197af9 — Continuation Context

**Trigger:** orchestrator at ~165k of 180k context budget after merging #929.
Handing off per CONTEXT-BUDGET-GUARD (rule 26) §0.4.

## State at handoff (2026-05-17T23:05 UTC)

- Sprint id: `2026-05-17-197af9`
- Yaml sha (model-routing): `257f980a0ac94034cbd5af7fafc3ce281388dac6457a3a94abbd0965e161c0b5`
- Yaml path: `/root/projects/eigentakt/model-routing.yaml`
- Pricing snapshot: `/root/projects/pinder-core/docs/sprint-runs/2026-05-17-197af9/pricing-snapshot.jsonl` (copied from 2026-05-16-d1d40c, sprint_id relabeled)
- Orphan recovery log: 3 manual recoveries logged (correlation_ids
  `2026-05-17-197af9-929-backend-engineer-65990e6c`,
  `-438bf19e`,
  `-r1-manual`) — all closed via `spawn-recover.sh --source operator-provided`.
- Provider preflight: PASSED at sprint start (Anthropic 4 rungs + OpenRouter 2 rungs verified live).

## Tickets — current status

### Merged (1)

- **`core#929`** [test-infra] — PR #952 merged at 2026-05-17T23:04:44Z, sha `660677e0`.
  Cleared 65 baseline failures in Pinder.LlmAdapters.Tests across 4 clusters. Real
  production bug fixed (PadDialogueOptionsToThree → ToFour). 3 attempts:
  - Rung 0 attempt 1: token_underrun flake (0 in/0 out, 4m29s, source dumped).
  - Rung 0 attempt 2 (out-of-band same-rung retry): token_underrun flake but
    committed 2 fixes (65 → 54 fails).
  - Rung 1 (deepseek-v4-pro): token_underrun flake at 13m56s but
    cleared 54 → 9 fails. Orchestrator finished remaining 9 inline (5 trivial
    edits) and pushed cluster-4 commits on top.
  - Code review: Rung 2 (sonnet-4-6), APPROVE, 0 blockers, 7m13s, 14.2k tokens.

### Closed-as-duplicate at triage (1)

- **`core#880`** — duplicate of #929 (same Pinder.LlmAdapters.Tests baseline
  cluster, older filing). Closed inline via `gh issue close 880 --reason "not planned"`.

### Open / in-flight (0)

- None.

### Remaining to drain (26)

Pick up in this order. Thread 2 done (just #884 left); then Thread 1
foundation; then Thread 1 rest; then Thread 3 long tail.

**Thread 2 — Test-infra debt (1 left of 3):**
- `core#884` [flaky-test] Issue527_SessionRunnerBioFormatTests flakes when run
  alongside YamlDotNet-loading tests.

**Thread 1 — Staging-test fallout (13 left of 12; +1 because audit-first #944
may close as fixed-by-#942):**
- **`core#942`** [P0] GameEndedException(Ghosted) in turn-prefetch state-mutates
  session — FOUNDATION. Everything else in Thread 1 re-verifies against this.
- `core#943` [P1] `roll.tier` wire field absent on successful rolls.
- `core#944` [P1] `turn_record.trap_activated` missing on turn 3 — AUDIT FIRST;
  may close as fixed-by-#942 once #942 is on staging.
- `core#945` [P1] OfferedOption wire DTO emits dc=null and modifier=null.
- `core#948` [P1] All sessions show "outcome unknown" — outcome column NULL.
- `core#950` [P1] Psychological stake never surfaces in chat (do A+B both per
  resolved scope on ticket comment).
- `core#951` [P1] Opening message contains literal "scene" instead of
  opponent's name.
- `web#647` [P1] EventBox renders box for text-only mods (foundational for
  #648/#649).
- `web#648` [P1] Folded EventBox header uninformative (likely absorbs #655).
- `web#649` [P1] Expanded EventBox lacks consequence/roll/formula breakdown.
- `web#650` [P1] Weakness-window hit lacks global FoldableHintBanner trigger.
- `web#651` [P1] Replays unavailable for all sessions (companion to core#948).
- `web#652` [P1] Main-roll formula should fold UNDER the success/miss EventBox.
- `web#655` [P1] Shadow check folded header doesn't surface shadow type
  (likely collapses into #648).

**Thread 3 — Long-tail chores (13):**
- `core#920` [#901 Phase-2 prep] RollResult.Check nullability vs constructor default.
- `core#921` [#901 follow-up] Broaden TierLadderAuditTest regex — refiner may
  close as still-blocked.
- `core#924` [#906 follow-up] Mixed enum serialization shape on RollResult.
- `core#925` [#906 follow-up] DefendingRollStat naming — refiner may close as
  still-blocked.
- `core#927` [#598 follow-up] Surface final_verdict + final_tier on
  RollCheckResult.
- `core#947` [P2] Anthropic prompt cache not hitting on OpenRouter. **Companion
  goal:** the Phase 6.5 cache-audit artifact directly tests this.
- `core#949` [P2] LlmStakeGenerator default prompt should ask for bullet list.
- `web#646` [P2] turn_record.text_diffs[*] discriminator audit (layer vs kind).
- `web#653` [P2] Show "intended = delivered" instead of duplicate text.
- `web#654` [P2] Estimated message-gen time should re-estimate.
- `web#619` [#601 follow-up] RollFormula option_roll kind hardcoded.
- `web#621` [#601 follow-up] No jsdom render test — per repo policy deferred
  to Playwright e2e; refiner may close as won't-fix.
- `web#612` [#592 Phase 5] Storybook coverage for RollEventBox.

### Post-merge step (always, not a ticket)

After all merged: deploy submodule bump from `/root/projects/pinder-web`:
```
cd /root/projects/pinder-web && \
  git submodule update --init --remote pinder-core && \
  git add pinder-core && \
  git commit -m "chore: bump pinder-core submodule to <sha> (post-bignight-sprint)" && \
  git push origin main && \
  ./deploy.sh --staging
```

## Follow-ups filed mid-sprint

- **`core#953`** — [bug][test-infra] Pinder.Rules.Tests: 46 pre-existing
  failures (same shape as #929 but on Pinder.Rules; discovered during #929 PR
  validation; surface for next hygiene pass).

## Hygiene state

- pinder-core main: `5a62a99` at start → after #929 merge, post-pull HEAD is
  the new merge commit. Continuation orch should `git pull` first.
- pinder-web main: `ae2e4ef` (unchanged; submodule still pinned at pre-sprint).
- Worktrees: clean (no /tmp/work-* directories).
- Open PRs: 0 in pinder-core, 0 in pinder-web.
- Stashes: 5 pre-existing in pinder-core, 1 pre-existing in pinder-web — all
  preserved per PRESERVE-SEDIMENT-UNTIL-SIGNOFF.

## Reviewer comment from #952 — NON-BLOCKING but noted

> `pinder-web/src/Pinder.GameApi.Tests/Controllers/GetTurnTests.cs:49` asserts
> `Assert.Equal(3, options.GetArrayLength())` — will fail when pinder-web bumps
> its submodule. Needs a follow-up PR in pinder-web.

The continuation orchestrator should either:
- Fold this fix into the eventual post-merge submodule bump (single line edit),
  OR
- File as a tiny follow-up ticket and let it ride in Thread 3 next round.
  Recommended: fold into the bump commit.

## Calibration notes (for Phase 6.5)

- Three OpenRouter token_underrun flakes back-to-back on #929 (one Rung 0
  initial, one Rung 0 retry, one Rung 1). All produced real work via tool
  calls; the stats line just reported 0/0 wrongly. **The `subagent_announce`
  Stats line is currently unreliable for OpenRouter completions** — orchestrator
  must verify state via `git log`/`gh pr view` not via Stats.
- Multi-cluster test-cleanup tickets (#929 had 65 fails across 4 clusters) are
  beyond Rung 0 (gemma-4-31b-it) capacity. Worth examining whether to bump
  default_rung for `[test-infra]`-labelled tickets to Rung 1 in
  model-routing.yaml after more calibration data.
- Refiner pre-pass: this sprint orchestrator skipped per-ticket refiner runs
  (kickoff said "no tickets need pre-sprint operator input"). Worked fine for
  #929. Continuation orchestrator may continue this pattern unless a ticket
  looks genuinely ambiguous.

## How to resume

1. Read this file. Read `/root/projects/pinder-core/docs/sprint-runs/2026-05-17-197af9/kickoff.md`.
2. Pull main: `cd /root/projects/pinder-core && git pull origin main`.
3. Verify routing yaml unchanged: `sha256sum /root/projects/eigentakt/model-routing.yaml` should equal `257f980a0ac94034cbd5af7fafc3ce281388dac6457a3a94abbd0965e161c0b5`. If drift, re-run `scripts/load-routing.sh`.
4. Process next ticket: **`core#884`** (Thread 2 cleanup) → then **`core#942`** (Thread 1 P0 foundation).
5. After Thread 1 P0 is on staging, run audit step for `core#944` (may close as
   fixed-by-#942 instead of implementing).
6. Continue through ordering above. Cron watchdog every 15min will surface stalls.

## Files of interest

- `/root/projects/pinder-core/docs/sprint-runs/2026-05-17-197af9/kickoff.md`
- `/root/projects/pinder-core/docs/sprint-runs/2026-05-17-197af9/spawns/929-impl.md` (template for new spawns; reuse structure)
- `/root/projects/pinder-core/docs/sprint-runs/2026-05-17-197af9/spawns/929-review.md`
- `/root/projects/pinder-core/docs/sprint-runs/2026-05-17-197af9/pricing-snapshot.jsonl`
- `/root/projects/pinder-core/agent.log` — full event stream
