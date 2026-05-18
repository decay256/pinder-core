You are a code reviewer subagent in the Pinder dev swarm. Review PR **#661** in `decay256/pinder-web` (fix for #948 — session outcome persistence).

## Workspace
```bash
rm -rf /tmp/review-948
git clone --branch fix/948-session-outcome-persistence-r2 \
  https://github.com/decay256/pinder-web /tmp/review-948
cd /tmp/review-948
git submodule update --init pinder-core
```

## Cold-start
1. Read `/root/projects/eigentakt/agents/code-reviewer.md`.
2. Read `/root/projects/pinder-web/LESSONS_LEARNED.md`.
3. Read `/root/projects/pinder-web/AGENTS.md`.
4. `gh pr view 661 --repo decay256/pinder-web --json number,title,body,additions,deletions,files`.
5. `gh issue view 948 --repo decay256/pinder-core --json number,title,body,comments`.

## What you're reviewing

PR #661 is a P1 lifecycle persistence fix (~57 added, 2 removed across 5 files):

1. `src/Pinder.GameApi/Controllers/SessionsController.cs` — display fallback so NULL outcome → "In progress" (live) or "Aborted" (orphan).
2. `src/Pinder.GameApi/Data/GameSessionRepository.cs` — new `MarkSessionEndedAsync(sessionId, outcome, endedAt, ct)` using raw SQL `UPDATE user_sessions SET outcome=..., ended_at=... WHERE session_id=...`.
3. `src/Pinder.GameApi/Data/IGameSessionRepository.cs` — interface declaration.
4. `src/Pinder.GameApi/Data/NoOpGameSessionRepository.cs` — no-op for no-DB mode.
5. `src/Pinder.GameApi/Services/ActiveSession.cs` — call `MarkSessionEndedAsync` from the `GameEndedException` catch block in `EnsureTurnStartedLockedAsync` before `Session.MarkEnded(ex.Outcome)`.

## Heuristic checklist

### 1. AC coverage
- [ ] Every `GameEndedException` catch site (not just the one in `EnsureTurnStartedLockedAsync`) writes outcome + ended_at. Grep for `catch (GameEndedException` and `catch (GameEndedException ex)` across `src/Pinder.GameApi/` — there may be others (`Controllers/TurnsController.cs`? `Services/AutoplayService.cs`? prefetch path?).
- [ ] Session-list DTO never returns 'unknown'. Live sessions → 'In progress'; ended → enum string (Dated/Ghosted/Unmatched/Aborted); orphan NULL outcome+non-NULL ended_at → 'Aborted'.
- [ ] **Regression test added**. The ticket AC says: "Regression test in `src/Pinder.GameApi.Tests/` simulates a Ghosted end → asserts the row has non-NULL outcome+ended_at AND the list DTO renders 'Ghosted'." The PR diff shows **NO test files changed**. This is a likely blocker — verify and flag.
- [ ] In-progress regression test: asserts list DTO renders 'In progress' when outcome is NULL.

### 2. Correctness
- [ ] `_gameSessionRepo != null` check in `ActiveSession.cs` — is the field nullable? If yes, when is it null in production? If no, the null check is dead code. Verify by checking the constructor / DI registration.
- [ ] `_gameSessionRepo.MarkSessionEndedAsync` is awaited BEFORE `Session.MarkEnded(ex.Outcome)`. If the DB write throws, we'd leave the in-memory session unended. Is that acceptable? The ticket says "atomic" — but a single-row UPDATE isn't atomic with the in-memory state mutation. Consider whether the order matters or whether the catch should swallow DB exceptions to keep the existing UX behaviour.
- [ ] `outcome` parameter is a `string`, not the enum. The implementer passes `ex.Outcome.ToString()`. Is that the canonical serialization? Compare with how the column was originally written (or intended to be written) — match the casing/format (e.g., "Ghosted" vs "ghosted").
- [ ] Raw SQL `UPDATE user_sessions SET outcome={0}, ended_at={1} WHERE session_id={2}` — is this Postgres-correct? Quoted/case-sensitive column names? If the column is `outcome text NULL`, the string assignment works. If `outcome` is an enum type in DB, it needs casting.
- [ ] `SessionCancellation` token passed to the repo call — verify this is the long-lived session token, not the per-request one. If the user disconnects mid-turn, we still want the outcome persisted (or do we?).

### 3. Production-wire path
- [ ] Verify by grep: where does the **prod** game-end path actually fire? The fix wires only `EnsureTurnStartedLockedAsync` — but the prefetch path (#942 work) also handles `GameEndedException`. Did the implementer wire the prefetch site? Check `pinder-web` for all prefetch + turn-end sites that catch `GameEndedException` and assert each one now calls `MarkSessionEndedAsync`.

### 4. Build / tests
Run yourself:
```bash
cd /tmp/review-948
dotnet build -c Release 2>&1 | tail -20
dotnet test -c Release src/Pinder.GameApi.Tests/ --no-build --filter "FullyQualifiedName~SessionsController|FullyQualifiedName~GameSessionRepository|FullyQualifiedName~ActiveSession" 2>&1 | tail -30
```
- [ ] Build succeeds with 0 errors, 0 warnings (new warnings are blockers).
- [ ] Tests pass. Capture pass/fail counts in Stats line.

### 5. Backfill follow-up
- [ ] Implementer filed `decay256/pinder-core#959` for backfill of NULL outcomes. Verify the issue exists and has reasonable body. Confirm it's tagged P2 (backfill is one-off chore, not blocking).

## Verdict

Write a **single PR review comment** with verdict `APPROVE` or `CHANGES_REQUESTED`. If `CHANGES_REQUESTED`, list specific blockers with file:line references.

Submit the review via:
```bash
gh pr review 661 --repo decay256/pinder-web --request-changes -F /tmp/review-948-comment.md
# OR
gh pr review 661 --repo decay256/pinder-web --approve -F /tmp/review-948-comment.md
```

## DoD evidence

Emit the Stats line at end of run per `code-reviewer.md`:

```
Stats: tokens_in=<N> tokens_out=<N> tokens_cache_read=<N or n/a> tokens_cache_write=<N or n/a> wall_clock_seconds=<N>
```

Then a structured verdict line:
```
verdict: APPROVE   (or CHANGES_REQUESTED — N blockers)
```

## Reminders

Correlation id: `2026-05-17-197af9-948-code-reviewer-<your-id>` — pass through.
Sprint id: `2026-05-17-197af9`.

Per the response-style rules in `/root/.openclaw/agents-extra/pinder/USER.md`: short, lead with the verdict, no markdown tables, max 5 bullets per section.
