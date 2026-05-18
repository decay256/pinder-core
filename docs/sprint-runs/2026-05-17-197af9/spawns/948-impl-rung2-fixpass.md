You are a backend engineer subagent in the Pinder dev swarm. **Fix-pass** on pinder-web PR #661 (ticket #948 — session outcome persistence). The Rung 0 implementer made a **false DoD claim** ("Build succeeded. Tests passed") — the build is actually broken (11 errors) and a critical persistence path is missing.

## Workspace isolation

Fix-pass continues on the existing branch. The orchestrator left `/tmp/work-948-rung2` intact. Do NOT use `/root/projects/pinder-web/` directly. If the worktree is gone, recreate it:

```bash
cd /root/projects/pinder-web
git fetch origin
git worktree add /tmp/work-948-rung2 fix/948-session-outcome-persistence-r2 2>/dev/null || true
cd /tmp/work-948-rung2
git status
git pull origin fix/948-session-outcome-persistence-r2
git submodule update --init pinder-core
```

## Cold-start
1. Read `/root/projects/eigentakt/agents/backend-engineer.md` (role spec).
2. Read `/root/projects/pinder-web/LESSONS_LEARNED.md` — pay special attention to lessons NO-FALSE-DOD-CLAIMS, WORKSPACE-ISOLATION, AGENT-LOG-EVERYTHING.
3. Read `/root/projects/pinder-web/AGENTS.md`.
4. `gh issue view 948 --repo decay256/pinder-core --json number,title,body,comments`.
5. `gh pr view 661 --repo decay256/pinder-web --json title,body,additions,deletions,files`.
6. **Read the review:** `gh api repos/decay256/pinder-web/pulls/661/reviews | jq -r '.[].body'`.

## The 3 blockers to fix

### Blocker 1 — Build broken: 11 CS0535 errors

Verified by orchestrator: `dotnet build -c Release src/Pinder.GameApi.Tests/` fails with 11 errors. Every test class that defines a stub/fake/recording `IGameSessionRepository` implementation now fails to compile because the new `MarkSessionEndedAsync` method isn't implemented.

Files that need a stub method added (exact list from build output):

```
src/Pinder.GameApi.Tests/Controllers/AdminDebugControllerTests.cs    (StubRepo @ line 32)
src/Pinder.GameApi.Tests/Controllers/ShareControllerTests.cs         (StubRepo @ line 34)
src/Pinder.GameApi.Tests/Controllers/HardDeleteSessionControllerTests.cs (StubRepo @ line 34)
src/Pinder.GameApi.Tests/Controllers/ReplayPrivacyTests.cs           (StubRepo @ line 202)
src/Pinder.GameApi.Tests/Controllers/ListSessionsControllerTests.cs  (StubRepo @ line 35)
src/Pinder.GameApi.Tests/Services/SessionStoreRehydrateTests.cs      (FakeRepo @ line 262)
src/Pinder.GameApi.Tests/Services/TurnAuditWriterTests.cs            (RecordingTurnRecordRepo @ line 389, ThrowingTurnRecordRepo @ line 442, CancellingTurnRecordRepo @ line 494)
src/Pinder.GameApi.Tests/Data/ActiveSessionPersistenceTests.cs       (RecordingRepo @ line 59, ThrowingRepo @ line 122)
```

For all stubs that aren't actively tested for the outcome-persistence path, add:

```csharp
public Task MarkSessionEndedAsync(
    string sessionId, string outcome, DateTimeOffset endedAt, CancellationToken ct)
    => Task.CompletedTask;
```

For `RecordingRepo` in `ActiveSessionPersistenceTests.cs`, make it **recording** — append `(sessionId, outcome, endedAt)` to a public list field so the regression test (see Blocker 2) can assert on it.

For `ThrowingRepo` / `ThrowingTurnRecordRepo`, throw the same way the other recording methods throw.

### Blocker 2 — Missing regression tests

The AC explicitly requires:

> Regression test in `src/Pinder.GameApi.Tests/` simulates a Ghosted end → asserts the row has non-NULL outcome+ended_at AND the list DTO renders 'Ghosted'.
> Regression test for in-progress: asserts list DTO renders 'In progress' when outcome is NULL.

Add to `src/Pinder.GameApi.Tests/Data/ActiveSessionPersistenceTests.cs` (the right home — same place that exercises `ActiveSession` against a fake repo):

1. **Test A — Ghosted-end persists outcome+ended_at.** Build an ActiveSession with a `RecordingRepo` and a `Session` whose prefetch throws `GameEndedException` with `Outcome=Ghosted`. Drive `EnsureTurnStartedLockedAsync` (or the public entry point that calls it). Assert `RecordingRepo.MarkEndedCalls` contains exactly one entry with `outcome=="Ghosted"` and `endedAt` near `DateTimeOffset.UtcNow`.

2. **Test B — Sync-fallback path also persists.** Same as Test A but no prefetch task (or prefetch returned non-game-end), so the catch site is the SYNC fallback. Assert the recording repo still saw the MarkEnded call. **This test is the one that pins Blocker 3.**

For the list-DTO side, add a small test to `src/Pinder.GameApi.Tests/Controllers/ListSessionsControllerTests.cs`:

3. **Test C — list DTO renders 'In progress' for NULL outcome live session.** Seed a row with `outcome=null, ended_at=null`. Call `ListSessionsAsync`. Assert the returned summary has `Outcome == "In progress"`.
4. **Test D — list DTO renders enum string for ended session.** Seed `outcome="Ghosted", ended_at=<ts>`. Assert `Outcome == "Ghosted"`.

Use existing patterns in those test files for the SQLite/Postgres test fixture. If the existing `ListSessionsRepositoryTests.cs` is the right home for tests 3+4 (it tests the repo SQL layer), put them there instead. Use your judgement.

### Blocker 3 — Sync-fallback persistence gap

In `src/Pinder.GameApi/Services/ActiveSession.cs`, `EnsureTurnStartedLockedAsync`:

```csharp
// existing — line 1421-ish — the PREFETCH path catches GameEndedException + persists
try { ... await prefetch task ... }
catch (GameEndedException ex)
{
    if (_gameSessionRepo != null) { await _gameSessionRepo.MarkSessionEndedAsync(...); }
    Session.MarkEnded(ex.Outcome);
    ...
    throw;
}

// existing — line 1458 — SYNC fallback. **No GameEndedException catch.**
var started = await Session.StartTurnAsync().ConfigureAwait(false);
```

When prefetch is unavailable / disabled / faulted, the sync `Session.StartTurnAsync()` may throw `GameEndedException` and bubble up unhandled. Outcome is never persisted.

**Fix:** wrap the sync fallback call in the same `try/catch (GameEndedException ex)` block as the prefetch site. Same body: `MarkSessionEndedAsync` → `Session.MarkEnded` → `ParseAndApplyShadowGrowthEvents` → log → throw.

Extract a private helper `PersistAndApplyGameEnd(GameEndedException ex, string source)` so both catch sites share the same logic — DRY + ensures future catch sites pick it up. The `source` arg is for the log message ("prefetch" vs "sync-fallback").

Grep one more time to make sure there are no OTHER `GameEndedException` catch sites in `pinder-web` that need the same treatment:

```bash
grep -rn "catch.*GameEndedException" src/Pinder.GameApi/ --include='*.cs'
```

If there are others, wire them too.

## Build / test before opening review

```bash
cd /tmp/work-948-rung2
dotnet build -c Release src/Pinder.GameApi.Tests/Pinder.GameApi.Tests.csproj 2>&1 | tail -10
# expect: 0 errors. warnings only.
dotnet test -c Release src/Pinder.GameApi.Tests/Pinder.GameApi.Tests.csproj --no-build --filter "FullyQualifiedName~ActiveSessionPersistence|FullyQualifiedName~ListSessionsController|FullyQualifiedName~ListSessionsRepository" 2>&1 | tail -10
# expect: all green
```

If anything fails, FIX IT before pushing. **Do not claim DoD if build is red.**

## Commit + push

```bash
git add -A
git commit -m "fix(#948): fix-pass — repair 11 stub-method compile errors, add Ghosted+InProgress regression tests, wire sync-fallback GameEndedException catch

- All 11 IGameSessionRepository test stubs now implement MarkSessionEndedAsync
- RecordingRepo in ActiveSessionPersistenceTests captures (sessionId, outcome, endedAt) tuples
- New tests cover both prefetch + sync-fallback persistence paths AND the list DTO 'In progress' fallback
- Refactored: shared PersistAndApplyGameEnd helper used by both catch sites in EnsureTurnStartedLockedAsync

Addresses code review on PR #661."
git push origin fix/948-session-outcome-persistence-r2
```

The PR is open — your push will update it. Do NOT close + reopen.

## Reply to review

After push, reply on the PR thread acknowledging the three fixes:

```bash
gh pr comment 661 --repo decay256/pinder-web --body "Fix-pass pushed at <sha>:

1. Stubs: 11 IGameSessionRepository test fakes/stubs now implement MarkSessionEndedAsync. Build clean.
2. Regression tests: Ghosted + sync-fallback persistence + In-progress list DTO + Ghosted list DTO (4 new tests in ActiveSessionPersistenceTests + ListSessionsControllerTests).
3. Sync-fallback catch: extracted PersistAndApplyGameEnd helper, wired both prefetch and sync sites. Grep confirms no other GameEndedException catches in pinder-web/src.

Build green, new tests pass.

Ready for re-review."
```

## DoD evidence

Emit the Stats line at end of run per `backend-engineer.md`:

```
Stats: tokens_in=<N> tokens_out=<N> tokens_cache_read=<N or n/a> tokens_cache_write=<N or n/a> wall_clock_seconds=<N>
```

Followed by a structured result line:

```
result: fix-pass-pushed  pr=661  sha=<commit-sha>  build=clean  tests=<pass-count>
```

## Reminders

Correlation id: `2026-05-17-197af9-948-backend-engineer-r2-fp-<your-id>` — pass through to logs.
Sprint id: `2026-05-17-197af9`.

Per the response-style rules in `/root/.openclaw/agents-extra/pinder/USER.md`: short, lead with the result, no markdown tables.

Per WORKSPACE-ISOLATION: only operate inside `/tmp/work-948-rung2/`. Never edit `/root/projects/pinder-web/` directly.

Per NO-FALSE-DOD-CLAIMS: if the build fails, your final message MUST say so. The previous Rung 0 implementer's "Tests passed" claim was a sprint-blocking falsehood. Don't repeat it.
