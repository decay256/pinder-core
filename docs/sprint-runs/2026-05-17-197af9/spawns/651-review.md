You are a code reviewer subagent in the Pinder dev swarm. Review pinder-web PR **#664** (fix for #651 — replay availability via auto-share-token).

## Workspace isolation
```bash
rm -rf /tmp/review-651
git clone --branch fix/651-replay-availability-r2 \
  https://github.com/decay256/pinder-web /tmp/review-651
cd /tmp/review-651
git submodule update --init pinder-core
```

## Cold-start
1. Read `/root/projects/eigentakt/agents/code-reviewer.md`.
2. Read `/root/projects/pinder-web/LESSONS_LEARNED.md`.
3. Read `/root/projects/pinder-web/AGENTS.md`.
4. `gh pr view 664 --repo decay256/pinder-web --json title,body,additions,deletions,files`.
5. `gh issue view 651 --repo decay256/pinder-web --json number,title,body,comments`.

## What you're reviewing

PR #664 is a P1 cross-stack fix (+325/-15 across 19 files):

- **Phase A (backend, game-end auto-token):** `IGameSessionRepository.EnsureShareTokenAsync` added (delegates to existing idempotent `RotateShareTokenAsync`). Called in `PersistAndApplyGameEndAsync` after `MarkSessionEndedAsync`. NoOp impl + 11 test stubs updated.
- **Phase B (backend, DTO):** `UserSessionSummaryRow.ShareToken` (nullable) projected in `ListSessionsAsync` LINQ + mapped in `SessionsController`. `SessionSummary` DTO surfaces `share_token` wire field (owner-list only; admin list explicitly omitted).
- **Phase C (frontend):** `types.ts` adds `share_token?: string | null`. `SessionsPage` renders "View Replay" when present, "Replay generation pending" for ended-but-tokenless, hides for in-progress. `getReplayAction` pure helper + 4 vitest tests. New `sessions_page.replay_pending` i18n key.

## Heuristic checklist

### 1. AC coverage
- [ ] Phase A — `EnsureShareTokenAsync` is idempotent (verify by reading the implementation: if a token already exists, return it; otherwise generate). The PR body claims it delegates to `RotateShareTokenAsync` — verify that delegation doesn't ROTATE (replace) an existing token, since that would invalidate existing share links. Read `RotateShareTokenAsync` to confirm its semantics; if it does rotate-always, the delegation is buggy.
- [ ] Phase A — wired into `PersistAndApplyGameEndAsync` in `ActiveSession.cs`. Both prefetch and sync-fallback paths get the share-token write (since they share the helper).
- [ ] Phase B — `share_token` exposed in `ListSessionsAsync` (owner-only). Verify `ListSessionsDebugAsync` (admin) is NOT changed, OR if it was, that's defensible (admins seeing tokens is OK).
- [ ] Phase B — owner-scoping logic: confirm the `userSub` filter is still in place; the new field doesn't widen access.
- [ ] Phase C — three visibility states correctly rendered: in-progress (no replay UI), ended-with-token (link to replay), ended-without-token (pending message).

### 2. Correctness
- [ ] `IGameSessionRepository.MarkSessionEndedAsync` is **still present** in the interface (a prior Rung 0 attempt accidentally deleted it). Grep the new interface file and confirm the method is there.
- [ ] All 11 test stub classes that implement `IGameSessionRepository` now have `EnsureShareTokenAsync` (mirror of the pattern from #948 fix-pass). Look at the file list — the implementer claims 14 backend files; 11 should be test stubs.
- [ ] `getReplayAction` pure helper logic: enumerates the three states correctly. The tests should pin all three.
- [ ] i18n key: `sessions_page.replay_pending` is added to the locale files (verify the locale json/yaml under `frontend/src/i18n/` or wherever the i18n source is).

### 3. Build + tests
```bash
cd /tmp/review-651
dotnet build -c Release src/Pinder.GameApi.Tests/Pinder.GameApi.Tests.csproj 2>&1 | tail -5
# Expect: 0 errors. The pre-existing #663 (useTurnSource TS error) is irrelevant for backend build.
dotnet test -c Release src/Pinder.GameApi.Tests/Pinder.GameApi.Tests.csproj --no-build \
  --filter "FullyQualifiedName~ShareToken|FullyQualifiedName~ListSessions|FullyQualifiedName~ActiveSession|FullyQualifiedName~ReplayPrivacy" 2>&1 | tail -10
# Expect: green (84/84 per PR body).
cd frontend
npm install --silent 2>&1 | tail -3
npm test -- SessionsPage getReplayAction 2>&1 | tail -10
# Expect: green (~6-10 tests including the 4 new ones).
```

- [ ] Build: 0 errors backend, frontend test runner green.
- [ ] All filtered tests pass.

### 4. PR hygiene
- [ ] `Closes #651` in PR body.
- [ ] Commit message describes A/B/C.

## Verdict

`APPROVE` if all checks pass. `CHANGES_REQUESTED` with specific blockers otherwise.

```bash
gh pr review 664 --repo decay256/pinder-web --approve -b "<body>"
# OR
gh pr review 664 --repo decay256/pinder-web --request-changes -b "<body>"
```

## DoD evidence

```
Stats: tokens_in=<N> tokens_out=<N> tokens_cache_read=<N or n/a> tokens_cache_write=<N or n/a> wall_clock_seconds=<N>
verdict: APPROVE   (or CHANGES_REQUESTED — N blockers)
```

## Reminders

Correlation id: `2026-05-17-197af9-651-code-reviewer-<your-id>`.
Sprint id: `2026-05-17-197af9`.

Per the response-style rules in `/root/.openclaw/agents-extra/pinder/USER.md`: short, lead with the verdict, no markdown tables, max 5 bullets per section.
