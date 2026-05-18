You are a backend engineer subagent in the Pinder dev swarm. Implement ticket **web#651** in pinder-web: replays for all sessions are unavailable from the session list.

## Workspace isolation
```bash
cd /root/projects/pinder-web
git fetch origin
git worktree add /tmp/work-651-r2 origin/main
cd /tmp/work-651-r2
git checkout -b fix/651-replay-availability-r2
git submodule update --init pinder-core
```

## Cold-start
1. Read `/root/projects/eigentakt/agents/backend-engineer.md`.
2. Read `/root/projects/pinder-web/LESSONS_LEARNED.md`.
3. Read `/root/projects/pinder-web/AGENTS.md`.
4. `gh issue view 651 --repo decay256/pinder-web --json number,title,body,comments`.

## Context — upstream is already fixed

The companion bug pinder-core#948 (outcome+ended_at persistence on game-end) has now merged. Verify by checking `git log --oneline -10 src/Pinder.GameApi/Services/ActiveSession.cs` for the `PersistAndApplyGameEndAsync` helper. So the outcome side is good.

The remaining gap for replays is the **share_token** — currently it's only generated via explicit `POST /sessions/{id}/share-token` (admin-only). The session-list DTO doesn't expose it, and game-end doesn't auto-generate it. The SPA shows "replay unavailable" for every row because no row has a token.

## Goal

After this fix:
1. **share_token auto-generated on game-end.** When `PersistAndApplyGameEndAsync` runs (i.e., outcome is being persisted), also generate + persist a share_token if none exists.
2. **Session-list DTO exposes share_token** (or a derived replay URL) for the session OWNER. Other users should NOT see other people's share tokens via this endpoint.
3. **Replay button visibility logic** updated in `frontend/src/pages/SessionListPage.tsx`: show only when `share_token` is populated, hide otherwise (`null` → hide, not "unavailable").
4. **Graceful "pending" state.** If a session is ended (outcome != null) but share_token is still null (edge case: the fix only auto-generates for game-ends post-deploy, not historical sessions), surface "replay generation pending" rather than the generic "unavailable."

## Diagnosis

```bash
cd /tmp/work-651-r2
grep -n "PersistAndApplyGameEndAsync\|MarkSessionEndedAsync\|RotateShareTokenAsync" src/Pinder.GameApi/Services/ActiveSession.cs src/Pinder.GameApi/Data/GameSessionRepository.cs 2>&1 | head -20
grep -n "share_token\|ShareToken\|replay_url\|ReplayUrl" src/Pinder.GameApi/Models/ 2>&1 | head -10
grep -n "share_token\|ShareToken\|replay" frontend/src/pages/SessionListPage.tsx 2>&1 | head -20
```

Find:
- The session-list DTO (likely `SessionSummary` per the #948 fix). Does it have `share_token` field? If not, add (nullable, ONLY for the owner — assume the existing list endpoint already does owner-only filtering since other endpoints do).
- The frontend's replay button render logic.

## Implementation

### Phase A — auto-generate share_token on game-end

In `src/Pinder.GameApi/Services/ActiveSession.cs` (or wherever `PersistAndApplyGameEndAsync` lives):

After `MarkSessionEndedAsync` succeeds, call `RotateShareTokenAsync(sessionId, ct)` if the session doesn't already have one. To avoid stomping an existing token, the simplest path is: introduce a new repo method `EnsureShareTokenAsync(sessionId, ct)` that:

```csharp
// Pseudo:
// SELECT share_token FROM user_sessions WHERE session_id = @id;
// if (token != null) return token;
// var newToken = GenerateToken(); // mirror RotateShareTokenAsync's generation
// UPDATE user_sessions SET share_token = @newToken WHERE session_id = @id AND share_token IS NULL;
// return newToken;
```

Or reuse `RotateShareTokenAsync` but only call it if the existing token is null. Look at `RotateShareTokenAsync` to see if it's idempotent on already-tokened rows. If it always rotates (i.e. replaces), use the conditional approach to avoid breaking existing shared replays.

### Phase B — session-list DTO surfaces share_token

In `src/Pinder.GameApi/Controllers/SessionsController.cs`:

`ListSessionsAsync` (NOT `ListSessionsDebugAsync` — debug is admin scope) — add `share_token` to the projection. This endpoint is owner-scoped (the user only sees their own sessions), so exposing share_token here is safe.

Update `SessionSummary` DTO to include `share_token` (nullable string, lowercase wire field per existing convention).

### Phase C — frontend visibility logic

In `frontend/src/pages/SessionListPage.tsx`:

- Find the row that renders "replay unavailable" / "View Replay" button.
- Change visibility logic to: `if (row.share_token) → render <Link to=\`/replay/${row.share_token}\`>View Replay</Link>`; `else if (row.outcome != null && row.outcome != 'In progress' && row.share_token == null) → render "Replay generation pending"`; `else → hide the column / render nothing`.

### Tests

1. **Backend test** in `src/Pinder.GameApi.Tests/`:
   - `ActiveSession.PersistAndApplyGameEndAsync` generates share_token if missing — assert repo `EnsureShareTokenAsync` (or equivalent) was called and returned non-null.
   - Doesn't stomp existing token — assert if `share_token` is already populated, it's not changed.
2. **Backend test** for the controller: session-list DTO includes `share_token` for the requesting owner.
3. **Frontend test** in `frontend/src/pages/__tests__/SessionListPage.test.tsx` (or wherever existing tests live): renders three states (in-progress → no replay UI; ended-with-token → View Replay link; ended-without-token → "Replay generation pending").

## Build + test

```bash
cd /tmp/work-651-r2
dotnet build -c Release src/Pinder.GameApi.Tests/Pinder.GameApi.Tests.csproj 2>&1 | tail -5
dotnet test -c Release src/Pinder.GameApi.Tests/Pinder.GameApi.Tests.csproj --no-build --filter "FullyQualifiedName~SessionList|FullyQualifiedName~ActiveSession|FullyQualifiedName~ShareToken" 2>&1 | tail -10
cd frontend
npm install --silent 2>&1 | tail -3
npm test -- SessionListPage 2>&1 | tail -10
```

- Build: 0 errors.
- Tests: green (or no regressions vs main).

**Note:** `tsc` on main currently has a pre-existing error in `useTurnSource.ts:382` (web#663). Do NOT fix it in this PR — it's separately tracked. As long as your build produces no NEW errors, you're good.

## Commit + push + PR

```bash
git add -A
git commit -m "fix(#651): auto-generate share_token on game-end + expose in session-list DTO + frontend visibility

Closes #651.

- ActiveSession.PersistAndApplyGameEndAsync now calls EnsureShareTokenAsync (idempotent — preserves existing token).
- SessionSummary DTO exposes share_token to the owner via ListSessionsAsync.
- SessionListPage renders 'View Replay' link when share_token populated, 'Replay generation pending' for ended-but-tokenless edge case, nothing for in-progress.
- 5 new tests pin all three behaviours.

DoD: build clean, all targeted tests pass."
git push -u origin fix/651-replay-availability-r2
gh pr create --repo decay256/pinder-web --base main --head fix/651-replay-availability-r2 \
  --title "fix(#651): replay availability on session list" \
  --body "Closes #651.

## What changed
<bullets>

## Tests
<names>

## DoD
- Build: clean (pre-existing #663 TS error unchanged)
- Tests: <pass/fail counts>"
```

## DoD evidence

```
Stats: tokens_in=<N> tokens_out=<N> tokens_cache_read=<N or n/a> tokens_cache_write=<N or n/a> wall_clock_seconds=<N>
result: pr-opened  pr=<N>  sha=<commit-sha>  build=clean  tests=<N>/<N>
```

## Reminders

Correlation id: `2026-05-17-197af9-651-backend-engineer-<your-id>`.
Sprint id: `2026-05-17-197af9`.
Worktree: `/tmp/work-651-r2`.

Per WORKSPACE-ISOLATION: only operate inside `/tmp/work-651-r2/`. Never edit `/root/projects/pinder-web/` directly.

Per NO-FALSE-DOD-CLAIMS: run the build and inspect the tail before claiming DoD.

Per the response-style rules in `/root/.openclaw/agents-extra/pinder/USER.md`: short, lead with the result, no markdown tables.
