You are a backend engineer subagent applying a SMALL, SCOPED fix to address code-review blockers on an already-open PR, in the EXISTING git worktree, then re-verifying green. Follow the project's DoD discipline exactly.

## Workspace setup (use the EXISTING worktree — do NOT recreate it)

```bash
unset GITHUB_TOKEN
cd /tmp/work-1123
git status
git branch --show-current   # must print fix/1123-symmetric-two-session-gm
```

Do NOT run `git worktree add` (already exists). Do NOT touch /root/projects/pinder-web/pinder-core directly. Do NOT reset/stash. Use /usr/bin/dotnet (8.0.128) directly; do NOT prepend /root/.openclaw/agents-extra/pinder/bin/ to PATH (broken shim).

## Role spec

Read and follow /root/projects/eigentakt/agents/backend-engineer.md (DoD discipline).

## Lessons (named) — LESSONS_LEARNED.md

- BUILD-PIPELINE-DISCIPLINE: DoD evidence must include `dotnet build` output, not just tests.
- SUBMODULE-SYNC-AFTER-REBASE: `git submodule update --init` after any rebase before building.
- FILE-SIZE-LIMIT-AND-DRY: keep files ≤400 soft / 600 hard lines.

## AGENTS.md (project rules)

CI = LOCAL ONLY (verify with `dotnet build` + `dotnet test`; never gate on GitHub Actions). Scope = pinder-core engine only; do NOT touch Unity client or pinder-web frontend. Do NOT expand scope beyond the review fix.

## The fix — PR #1134 (#1123 Symmetric two-session GM) code-review blocker

A code reviewer ran fresh eyes over PR #1134 and posted **Verdict: CHANGES_REQUESTED** with exactly ONE blocking issue plus minor non-blocking notes. Fix the blocker, add the missing coverage, re-verify, and push.

### BLOCKING (must fix)
**`src/Pinder.Core/Conversation/GameSession.Helpers.cs` (~L204-211): the public `GameSession.CreateSnapshot()` instance method omits `_avatarHistory`.** It currently passes `_dateeHistory` only as the trailing history argument, so `session.CreateSnapshot().AvatarHistory` is silently ALWAYS empty — an asymmetry vs the datee path. Fix it so the call passes `_avatarHistory` as the final argument, mirroring the datee history exactly, e.g.:
`…, _comboTracker.HasTripleBonus, _dateeHistory, _avatarHistory);`
Verify the snapshot factory signature (CreateSnapshot in TurnOrchestrator.Helpers.cs / wherever it lives) actually accepts the avatar-history param in that position — match the exact signature; do not guess the arg order. Confirm by reading the called method.

### Required new coverage
Add a round-trip test (mirroring the existing Issue788 datee-history snapshot assertion) that drives history onto the avatar session, calls the PUBLIC `GameSession.CreateSnapshot()`, and asserts the returned snapshot's `AvatarHistory` is populated (NOT empty) and round-trips. This guards the public-API symmetry the reviewer found uncovered.

### Non-blocking (do NOT fix in this PR — note in Research Log / file follow-ups only if cheap)
- New `DateeCard`/`PlayerAvatarCard` are injected but not yet read by any prompt builder (the "opposing char as public card" wiring lands in #1124). Leave as-is; note it.
- `SendStatefulAsync` hardcodes `PLAYER`/`DATEE` flatten labels in a fallback-only path (spec wants `AVATAR:`/`DATEE:`). Cosmetic; leave unless trivial.
- `NullLlmAdapter` empty-content placeholder avatar entries — consistent with its datee sibling; leave.

### Verify green
`dotnet build Pinder.Core.sln` 0 errors (capture output). `dotnet test Pinder.Core.sln` — previous green was 4431 passed / 0 failed / 27 skipped; your new round-trip test ADDS one (expect 4432). If ANY test fails, run that scope 3× and report deterministic-vs-flake and whether it also fails on origin/main. Do NOT mislabel a regression as a flake.

### Commit + push (PR #1134 already open — just push more commits to the branch)
Commit the fix + test with a message referencing the review (e.g. `fix(#1123): CreateSnapshot() omitted avatar history (review blocker) + round-trip test`). Push to the existing branch `fix/1123-symmetric-two-session-gm` (this updates PR #1134 in place). Do NOT open a new PR. Do NOT merge. Do NOT push to main. Append a `completed` JSONL line to /tmp/work-1123/agent.log with the new HEAD SHA.

Report back: the exact change you made (file + line + before/after), the new test you added, build result, full `dotnet test` summary (passed/failed/skipped), the new HEAD commit SHA, and whether you touched anything beyond the blocker.
