# Lessons — sprint 2026-05-16-d1d40c

_Final. Captured during the run; promoted from `lessons-draft.md` at Phase 7 close._

_Scope: all three lessons below are about eigentakt / orchestrator behaviour, not pinder-{core,web} game/architecture. They are intentionally NOT promoted to the project LESSONS_LEARNED.md files — see `~/.openclaw/agents-extra/pinder/AGENTS.md` for where this kind of lesson lives in this org._

## L1 — `agent.log` is project-tracked, not orchestrator-private

**Observation:** during #909 fixup, the orchestrator told a subagent to add `agent.log` to `.gitignore` along with `.eigentakt-bin/`. Wrong: `agent.log` is a tracked file in `origin/main` with active history (`6c140ea docs(sprint 2026-05-15-9056ee): preserve prompts/ + agent.log`, etc.). The project convention is: orchestrator appends to `agent.log` and commits those appends in separate `chore(agent-log,sprint-runs):` commits.

**Failure mode:** the implementer leaked their *local* appends into a feature commit. The orchestrator misread that as "agent.log shouldn't be in repo at all" and told a fixup subagent to .gitignore it + delete it from the PR. Net result was a -1284 line deletion of legitimate project history hiding inside a "cleanup" commit. Required a second fixup pass.

**Fix:** Before telling any subagent to .gitignore a file or remove it from a PR, the orchestrator MUST check whether the file is tracked in `origin/main` (`git ls-tree origin/main <path>`). If tracked: treat it as project content; the only valid cleanup is restoring it to its `origin/main` state for *this PR's purposes*, while orchestrator-driven appends ride in separate commits per project convention.

**Class:** Same shape as the L4 reverse-verification idea but applied to the orchestrator's own cleanup instructions: before instructing destructive cleanup, verify the assumption that the artifact is orchestrator-private.

**Lesson id:** EIGENTAKT-CHECK-TRACKED-BEFORE-GITIGNORE

## L2 — Implementer tasks must specify explicit `git add` pathspecs

**Observation:** 3 of 4 implementer/fixup spawns in this sprint committed `agent.log` to a feature PR, despite explicit "do not commit `agent.log`" instructions in the task. Pattern: `agent.log` is listed in `.gitignore` AND tracked. The orchestrator appends to it while the implementer works. The implementer then runs `git add -A` or `git add .` (the natural git workflow) and the modification gets staged because it's a tracked file (gitignore doesn't apply to tracked files).

**Failure mode:** prose instructions like "don't commit X" are not enforceable. The implementer plans in good faith and `git add .` is a habit, not a deliberate choice. With Rung 0 (Gemma) this fails reliably.

**Fix:** implementer task templates must say:

> Use explicit pathspecs only when staging:
> ```bash
> git add path/to/file1 path/to/file2   # exact files only
> ```
> Never `git add .`, `git add -A`, or `git add --update` in this worktree. The orchestrator appends to `agent.log` during your run and you'd inadvertently include those appends.

**Lesson id:** EIGENTAKT-IMPLEMENTER-EXPLICIT-PATHSPECS

**Drain cost:** 3 fixup passes in #909 sprint (1× artifact-leak, then 2× cleaning up the cleanup); 1× expected for #916; not yet measured for #919 onward but rule will be applied.

**Companion fix:** add an `eigentakt-mark IMPL_DONE` hook that diffs `git status --porcelain` against an allow-list and refuses to commit if anything outside the implementer's declared file scope is staged. Out of scope this sprint; track as eigentakt repo follow-up.

---

## L3 — OpenRouter Gemma availability cascade on #603

**Sprint:** 2026-05-16-d1d40c. **Ticket:** #603 (pinder-web frontend mirror of FailureTierDisplay.Label per-kind).

**Pattern observed (3 consecutive spawns):**

1. **Rung-0 attempt 1** (`openrouter/google/gemma-4-31b-it`): 22s runtime, 286.5k tokens in, 465 tokens out, **no output**. Classic `token_underrun` flake — the model streamed back almost nothing despite consuming a full prompt. Worktree was created but no file edits. OpenRouter side-channel error, no FailoverError surfaced.
2. **Rung-0 attempt 2** (same model, same-rung retry per EIGENTAKT-OPENROUTER-FLAKE-RETRY): 15s, **0 tokens in/out**, `FailoverError: API rate limit reached`. OpenRouter rejected the call outright. Same-rung retry budget exhausted.
3. **Rung-1 escalation** (`openrouter/deepseek/deepseek-v4-pro`): 9m42s, runtime reported 0/0 tokens (metering gap, NOT a real underrun), produced complete file changes on a detached HEAD with no commit and a mid-sentence final reply ("Now run the full test suite.").

**Two distinct flakes, one ticket:**
- `token_underrun` and `runtime_short_circuit` (Gemma-on-OpenRouter) cluster: model returns near-zero useful output despite consuming context. Suggests upstream serving issue at Gemma 4 31B.
- `rate_limit` (Gemma-on-OpenRouter): provider-level throttling. Both attempts in the same minute.
- `runtime metering gap` (DeepSeek-v4-pro-on-OpenRouter): OpenClaw's runtime hook reports 0/0 tokens but work was clearly done. Stats line is unreliable on this provider/model combo today.

**Salvage cost vs. respawn cost:**

The Rung-1 subagent did 95% of the work on a detached HEAD but stopped mid-DoD with one failing guardrail test (`displayNames.guardrail.test.ts ALLOWED_PATHS` missed the new helper file). Orchestrator inline-fixed the guardrail (~3 lines added), committed, pushed, and opened the PR. Total salvage exec budget: ~5 minutes.

**Decision rule:** when an escalated implementer produces complete file changes but stops on a trivial gate (guardrail test, missing branch creation, missing PR open), **the orchestrator may salvage inline iff** (a) the gap is mechanical and (b) the file changes can be reviewed end-to-end before commit. Do NOT salvage inline if the file changes are partial or the logic itself is uncertain — that's a respawn, not a salvage. This salvage path is the same pattern as the #610 inline-fixup recorded earlier in this sprint (commit 9bce3b0).

**What this means for future Gemma-on-OpenRouter spawns:**
- Token underruns are not yet rare. If a sprint sees 3+ in a row from Gemma-on-OpenRouter, the provider's Gemma-4-31b serving cluster is likely degraded. Skip Rung 0 for the sprint by escalating at first strikeout (TRIGGER-CONSERVATISM-UNTIL-CALIBRATED — this would be a calibration-driven decision recorded in `trigger-calibration.json`, not a mid-sprint default-bump per EIGENTAKT-PER-TICKET-RUNG-ISOLATION).
- Runtime metering gap on DeepSeek-v4-pro means the `attempt-end` cost row is `cost_usd: null`. Calibration data from these runs is unusable for budget tuning. Forward-fix: query OpenRouter's `/api/v1/generation/{id}` post-hoc once OpenClaw exposes the generation id.

**Anchor name:** EIGENTAKT-INLINE-SALVAGE-WHEN-MECHANICAL.
