# Sprint 2026-05-17-197af9 — Continuation Context #3

**Trigger:** orchestrator at high context utilization mid-drain after merging 8 PRs in this round (the third orchestrator session this sprint). Handing off per CONTEXT-BUDGET-GUARD §0.4 before context runs out mid-ticket.

**Predecessor handoff chain:** `kickoff.md` → `continuation-context.md` (cont-1) → `continuation-context-2.md` (cont-2) → this file (cont-3 → next).

## State at handoff (2026-05-18T12:30 UTC, approx)

- Sprint id: `2026-05-17-197af9`
- pinder-core HEAD: `1a2a84e` (after #963 i18n keys for #649)
- pinder-web HEAD: `dfd7d37` (after #666 i18n consequence render + submodule bump to 1a2a84e)
- Yaml sha (model-routing): `257f980a0ac94034cbd5af7fafc3ce281388dac6457a3a94abbd0965e161c0b5` (unchanged)
- Pricing snapshot: `/root/projects/pinder-core/docs/sprint-runs/2026-05-17-197af9/pricing-snapshot.jsonl`
- Cron watchdog: presumed-enabled (15min, silent when done — no surfacing this round)

## Tickets — merged this orchestrator session (8)

- **`core#948`** [P1] — PR #661 → fix-pass #661 (Rung 2 sonnet) merged at `8338b97`. Outcome+ended_at persistence via `PersistAndApplyGameEndAsync` helper. **Backfill follow-up: core#959 (P2).** Closed manually.
- **`core#951`** [P1] — PR #960 (Rung 2 sonnet escalation after Rung 0 50min strikeout) merged at `a69f2c9`. `Senders.IsScene` filter in `SessionDocumentBuilder`.
- **`web#647`** [P1] — PR #662 (Rung 0 gemma, fix-pass inline by orchestrator to remove strayed pnpm-lock.yaml) merged at `7580853`. `TEXT_DIFF_LAYER_VISIBILITY` map silences cosmetic layers.
- **`core#950`** [P1] — PR #961 (Rung 2 sonnet escalation after Rung 0 22min degenerate-output strikeout) merged at `15d835c`. Three-pronged stake fix: generator-prompt anchor mandate + OPTION_C must-quote + STAKE COVERAGE telemetry.
- **`web#651`** [P1] — PR #664 (Rung 2 sonnet escalation after Rung 0 crash mid-write) + companion core PR #962 (i18n key `sessions_page.replay_pending`) → merged at `9e2bc0a`. EnsureShareTokenAsync + DTO + frontend visibility.
- **`web#648`** [P1] — PR #665 (Rung 0 gemma! +123/-1052 simplification of CollapsedHeaderProps to `{primary, suffix?}`) merged at `f64058d`. APPROVE from Rung 1 deepseek; 3 non-blocking notes.
- **`web#649`** [P1] — PR #666 (Rung 2 sonnet direct from start — escalate-by-default per cross-repo coordination concern) + companion core PR #963 → merged at `dfd7d37`. EventBox expanded body shows consequence + breakdown + diff via SPA i18n catalogue. Inline-revert by orchestrator of 2 out-of-scope edits (icon swap + curly quotes) flagged by Rung 3 opus reviewer.
- **`web#655`** [P1] — **closed-as-superseded** by web#648 (deriveShadowHeader already emits `{ShadowType} Miss by {Margin}` per the AC). No PR — verified existing test fixture covers it.

## Follow-ups filed this orchestrator session (3)

- **`core#959`** — [#948 follow-up] backfill NULL outcomes for pre-#948 sessions. P2.
- **`web#663`** — [bug][P2] tsc build error in useTurnSource.ts:382 — DialogueOption missing 'modifier' property. Likely #945 fallout. Pre-existing on main; doesn't block PRs that touch unrelated files.
- **`core#964`** — [#649 follow-up] Surface 'consequence' on RollCheckResult / ShadowCheckResult / HornyCheckResult / TropeTrapResult wire DTOs (Path A from #649 ticket body; cheaper Path B i18n catalogue shipped first). P2.

## Tickets — remaining (14)

### Thread 1 — web P1 staging-test fallout (4 left)

- `web#650` [P1] — Weakness-window hit should trigger global FoldableHintBanner; player currently has to expand events to find out.
- `web#652` [P1] — Main-roll formula should fold UNDER the success/miss event box, not after the intended-message text.

### Thread 3 — long-tail chores (12 left, all P2)

- **Core:** `core#920`, `core#921`, `core#924`, `core#925`, `core#927`, `core#947`, `core#949`.
- **Web:** `web#646`, `web#653`, `web#654`, `web#619`, `web#621`, `web#612`.

## Rung-routing observations this round (calibration signal)

### Rung 0 strikeout rate this session: 4/7 attempts (57%)

- **#948** Rung 0 gemma: PR opened with false DoD claim (11 build errors). Fix-pass at Rung 2.
- **#951** Rung 0 gemma: wall-clock overrun (50min, no PR, no commit, only diagnosis notes). Escalated to Rung 2.
- **#647** Rung 0 gemma: PR opened but committed strayed `pnpm-lock.yaml`. Inline-fix by orchestrator.
- **#950** Rung 0 gemma: degenerate output (filename-only final message, 22min, no commit). Escalated to Rung 2.
- **#651** Rung 0 gemma: crashed mid-write (tool-validation error), WIP lost. Escalated to Rung 2.
- **#648** Rung 0 gemma: shipped clean PR (+123/-1052). The one full Rung 0 success this session — but reviewer (Rung 1 deepseek) had to verify ~1000 LoC of deletions weren't silently dropping behaviour.
- **#649** Rung 0 SKIPPED — orchestrator applied provider-instability override to Rung 2 sonnet direct from start for cross-repo coordination. Succeeded.

**Strong recommendation for next orchestrator:** continue Daniel's "escalate-by-default" lean from his task-brief. For any ticket that touches **multiple files / cross-repo / non-trivial prompt-engineering / requires multi-step git flow**, skip Rung 0 entirely. Rung 0 is reliable only for small single-file fixes with clear AC and obvious code locations. Document the override in each `pre_spawn_estimate` log entry per the now-established pattern.

### Inline-fix vs fix-pass-subagent decisions

This round, orchestrator inline-fixed three trivial blockers instead of spawning fix-pass subagents:
- #647: `git rm` strayed pnpm-lock + force-push.
- #651: cross-repo i18n key (filed + merged tiny core PR, bumped submodule).
- #649: inline-reverted 2 out-of-scope edits (icon swap + curly quotes).

Saves ~30min × 3 = 90min of subagent time + ~$0.50 × 3 = $1.50 of model cost. The skill's SELF-UNBLOCK-BY-DEFAULT explicitly authorizes this for trivial automation hiccups.

### Cross-repo coordination pattern (established this round)

For tickets requiring a pinder-core change followed by a pinder-web submodule bump:
1. Implementer makes change in submodule (`/tmp/work-N/pinder-core`), commits + pushes to a chore branch.
2. `gh pr create + gh pr merge --squash` for the core PR.
3. Implementer bumps submodule pointer in outer worktree, commits + pushes outer PR.

This worked for #651 (orchestrator did inline) and #649 (Rung 2 sonnet did it autonomously per the explicit brief). Rung 0 has not been observed to execute this pattern correctly — confirms the escalation lean.

## How to resume

1. Read this file, then `continuation-context-2.md` for OpenRouter degradation data and the original 22-ticket list.
2. Pull both repos:
   ```bash
   cd /root/projects/pinder-core && git pull origin main
   cd /root/projects/pinder-web && git stash; git pull origin main; git submodule update --init pinder-core
   ```
3. Verify yaml sha unchanged: `sha256sum /root/projects/eigentakt/model-routing.yaml` (should be `257f980a...`).
4. **Pick from remaining 14.** Suggested priority order:
   - **web#650** [P1] (weakness-window hint banner) — frontend single-file likely.
   - **web#652** [P1] (formula folds under success/miss box) — frontend layout reshuffle.
   - **core#949** [P2] (LlmStakeGenerator bullet-list default prompt) — single yaml/string edit. Rung 0 candidate.
   - **core#947** [P2] (Anthropic prompt-cache audit) — investigation + measurement, possibly multi-file. Rung 2.
   - **core#920** / **core#921** / **core#924** / **core#925** / **core#927** [P2] — chores; mostly small single-file. Rung 0 candidates.
   - **web#653** [P2] (intended=delivered dedup) — small frontend. Rung 0 candidate.
   - **web#646** [P2] (text_diff_layer naming) — discriminator field rename. Possibly cross-repo if wire DTO; consult ticket.
   - **web#654** [P2] (re-estimate generation time). Frontend.
   - **web#619** / **web#621** / **web#612** [P2] — #592/#601 follow-ups, small frontend.

5. Continue cron watchdog in default mode.

## Files of interest

- `/root/projects/pinder-core/docs/sprint-runs/2026-05-17-197af9/spawns/` — all spawn templates this round (`948-*`, `951-*`, `950-*`, `651-*`, `647-impl.md`, `647-review.md`, `648-*`, `649-*`).
- `/root/projects/pinder-core/agent.log` — full event stream including manual rung-2 overrides and inline-fix log lines.
- `/tmp/work-948-wip.patch`, `/tmp/work-950-wip.patch`, `/tmp/work-651-wip.patch` — preserved WIP patches from Rung 0 strikeouts (kept for historical analysis; not needed for resume).
- `/tmp/work-650`, `/tmp/work-652` — DO NOT exist; each next implementer creates its own worktree per the spawn-template convention.

## Calibration data points for Phase 6.5 (when sprint completes)

This round's Rung 0 vs Rung 2 success-rate observation strongly supports the `provider_flake_retry_policy.same_rung_retries = 0` recommendation from cont-2 (especially for OpenRouter rungs when cumulative flake count > 5). Specifically:
- Rung 0 gemma 1-shot success rate this round: ~14% (1/7 attempts, and even that one shipped +123/-1052 needing careful review).
- Rung 2 sonnet 1-shot success rate this round: 100% (5/5 attempts including escalations and the one escalate-from-start).
- Rung 2 sonnet ENABLED the cross-repo coordination pattern (#651, #649) that Rung 0 has zero observed success on.

Propose for next sprint's Phase 0.5 calibration apply:
- `provider_flake_retry_policy.same_rung_retries = 0` for OpenRouter rungs.
- Add `cross_repo_or_multi_step_git: escalate_to_rung_2_immediately` as a per-ticket-class heuristic, OR add an explicit `complexity_score >= cross-repo-coordination` trigger to the strikeout-strategy machinery so the wrapper auto-routes complex tickets to Rung 2.
