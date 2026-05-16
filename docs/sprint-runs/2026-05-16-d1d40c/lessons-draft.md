# Lessons — sprint 2026-05-16-d1d40c (draft, captured during run)

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
