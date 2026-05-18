You are a backend engineer subagent in the Pinder dev swarm. Implement the **web-side half** of ticket **#942** as one PR in pinder-web.

## Context — read this first

The core engine half of #942 has already merged (pinder-core sha `1be887c`, PR #955). Key change: `GameSession.StartTurnAsync` no longer mutates session state when it throws `GameEndedException`. Callers must now:

1. Call `session.MarkEnded(ex.Outcome)` after catching `GameEndedException` from `StartTurnAsync`.
2. Apply growth from `ex.ShadowGrowthEvents` (list of descriptive strings like `"Dread +1 (Ghosted)"`) so spec AC#44 trigger 8 (Ghosted → +1 Dread) still records on the tracker.

Without (2), Ghosted-induced Dread growth is silently dropped. This is the production-visible piece.

Per the staging-test review on #942, the prefetch wrapper at `ActiveSession.cs:1848` currently catches `GameEndedException` and then silently re-runs `StartTurnAsync` via the sync fallback at `ActiveSession.cs:1402`. That re-run produced the phantom-turn bug. The fix is to surface the `GameEndedException.Outcome` to the SPA via the existing session-ended wire path (the SPA's normal end-of-game flow), not re-run.

## Workspace isolation (CRITICAL)
```bash
cd /root/projects/pinder-web
git fetch origin
git worktree add /tmp/work-rung-2-942-web origin/main
cd /tmp/work-rung-2-942-web
git checkout -b rung-2-sonnet-4-6/942-web
```

## Cold-start
1. Read eigentakt backend-engineer spec at `/root/projects/eigentakt/agents/backend-engineer.md`.
2. Read `/root/projects/pinder-web/LESSONS_LEARNED.md` if it exists (else `/root/projects/pinder-core/LESSONS_LEARNED.md`) — focus on REGRESSION-TESTS-ON-BUGS, BUILD-PIPELINE-DISCIPLINE, WORKSPACE-ISOLATION, SUBMODULE-SYNC-AFTER-REBASE.
3. Read `/root/projects/pinder-web/AGENTS.md`.
4. `gh issue view 942 --repo decay256/pinder-core --json number,title,body,comments` — full P0 bug body + both comments.
5. `gh pr view 955 --repo decay256/pinder-core --json title,body,files,additions,deletions` — the merged core PR's diff for context. Pay attention to `GameSession.cs` changes around the throw sites and the new `GameEndedException.ShadowGrowthEvents` payload.

## First step: submodule bump

The pinder-web repo embeds pinder-core as a git submodule at `pinder-core/`. Update it:

```bash
cd /tmp/work-rung-2-942-web
git submodule update --init --remote pinder-core
git -C pinder-core checkout 1be887c
git -C pinder-core log -1 --oneline   # must show: 1be887c fix(#942): make StartTurnAsync transactional...
git add pinder-core
```

Don't commit yet — bundle this into your first commit alongside the wire-up.

## Pathspec discipline
- Explicit pathspecs only (no `git add .` / `-A` / `-u`).
- Don't commit `agent.log`, `.eigentakt-bin/`, build artifacts.

## What you're fixing in pinder-web

**File:** `/tmp/work-rung-2-942-web/src/Pinder.GameApi/Services/ActiveSession.cs`

**Two sites:**

1. **Line ~1848** (`ResolveTurnAsync` prefetch wrapper). Currently catches `GameEndedException` from the speculative prefetch and lets the flow fall through to the sync fallback. After this PR:
   - On catch: call `session.MarkEnded(ex.Outcome)`.
   - Reapply shadow growth from `ex.ShadowGrowthEvents` by **parsing the strings** (format: `"<StatType> +<N> (<Reason>)"` — see `GameSession.cs` for the exact format). For each parsed event, call `session.PlayerShadows.ApplyGrowth(statType, amount, reason)`.
     - If parsing is fragile, you can also pattern-match on the leading word (`Charm`, `Rizz`, `Honesty`, `Chaos`, `Wit`, `SelfAwareness` are the 6 stats; shadow pairings are `Madness/Despair/Denial/Fixation/Dread/Overthinking`).
     - **NOTE:** A follow-up ticket #956 will add `GameEndedException.ShadowGrowthEffects` as a typed record list, eliminating the parsing. For now, parse — but write the parse helper in a way that's easy to swap out.
   - Do NOT silently fall through to the sync path. Surface the outcome to the SPA via the same wire path used when the engine throws normally (controller-side session-ended response).

2. **Line ~1402** (`EnsureTurnStartedLockedAsync` drain fallback). Same logic.

For both: if the **caller** of `ResolveTurnAsync` is `SessionsController` (it is — see `Controllers/SessionsController.cs:482-520`), make sure the new catch-and-surface path produces the same response shape the SPA already handles for normal game-end.

## Pinder stat canon (memorize)

Pinder is a **6-stat** system. Stats: `Charm`, `Rizz`, `Honesty`, `Chaos`, `Wit`, `SelfAwareness`. Shadow pairings: `Madness/Despair/Denial/Fixation/Dread/Overthinking`. **`Chaos` is canonical** (not a hallucination). Source: `pinder-core/src/Pinder.Core/Stats/StatType.cs`. Use these exact names when parsing.

## Tests

Add to `src/Pinder.GameApi.Tests/Services/`:

1. **`Issue942_PrefetchSurfacesGhostedTests.cs`** (new file):
   - **Test 1:** Build an `ActiveSession` wrapping a `GameSession` in the about-to-Ghost state. Trigger `ResolveTurnAsync` via the path that prefetches. Assert that the response shape matches the existing session-ended wire format (compare to an integration fixture if one exists; else assert key fields).
   - **Test 2:** After the prefetch catches, assert `session.IsEnded == true`, `session.Outcome == Ghosted`.
   - **Test 3:** After the prefetch catches, assert `session.PlayerShadows.GetDelta(ShadowStatType.Dread) == 1` (the growth from `ex.ShadowGrowthEvents` was reapplied).

2. **Update existing test:** `Issue122_TypedEndedExceptionTests.cs` (or whichever test asserts the current prefetch-throws-then-falls-through behavior). The pre-existing assertion is now wrong; update it to reflect the new "prefetch catches, applies, surfaces" contract.

## Build evidence

After fixing:
```bash
cd /tmp/work-rung-2-942-web
dotnet build -c Release 2>&1 | tail -5      # must be 0 errors
dotnet test src/Pinder.GameApi.Tests/Pinder.GameApi.Tests.csproj --no-build 2>&1 | tail -10
```

Then run the deploy build per `AGENTS.md`:
```bash
dotnet publish src/Pinder.GameApi/Pinder.GameApi.csproj -c Release 2>&1 | tail -10
```

## Frontend tests (if applicable)

The pinder-web SPA might have Jest tests covering the session-ended response. If `package.json` exists in the repo root or `frontend/`:
```bash
cd /tmp/work-rung-2-942-web/frontend 2>/dev/null && pnpm test 2>&1 | tail -10
```

Don't break existing frontend tests. If you don't touch the SPA-visible wire shape, you shouldn't need to update anything frontend-side.

## Commit + PR

Atomic commits — recommended split:
1. `chore(#942): bump pinder-core submodule to 1be887c (transactional StartTurnAsync)` — submodule bump alone.
2. `fix(#942): surface GameEndedException from prefetch instead of re-running sync` — the ActiveSession.cs changes + new tests.

PR:
```bash
gh pr create --repo decay256/pinder-web --base main --head rung-2-sonnet-4-6/942-web \
  --title "fix(#942): web-side prefetch surfaces Ghosted instead of re-running sync" \
  --fill
```

Body MUST include:
- `Closes #942` (this completes the P0 — both halves are now landed).
- `## DoD Evidence` block with: submodule bump confirmation (`git -C pinder-core log -1 --oneline`), build tail, test tail, publish tail.
- `## Caller-contract checklist` confirming both `MarkEnded` + growth reapplication happen on the catch path.

## Acceptance criteria (from ticket)

- [ ] `ActiveSession.ResolveTurnAsync` sync-fallback: if speculative prefetch threw `GameEndedException`, surface the outcome to the SPA via existing session-ended wire path; do NOT silently re-run.
- [ ] Submodule bumped to `1be887c`.
- [ ] Tests cover the prefetch-catches-and-surfaces path.
- [ ] `session.PlayerShadows.GetDelta(Dread) == 1` after a Ghost catch (proves growth reapplication works).
- [ ] All existing tests still pass.

## DO NOT
- Do not merge.
- Do not push to main.
- Do not modify unrelated files.
- Do not work in `/root/projects/pinder-web/` — only in `/tmp/work-rung-2-942-web/`.
- Do not change the pinder-core submodule contents. Only bump the pointer to `1be887c`.

## Logging to agent.log

Entry (note: pinder-web has its own agent.log; use the pinder-core one for orchestrator continuity):
```bash
bash /root/projects/eigentakt/scripts/log.sh backend-engineer "#942-web" "P0-web-half" "started" "Implementing prefetch surfaces Ghosted + growth reapplication; bumping submodule to 1be887c"
```

Exit (after PR opened):
```bash
bash /root/projects/eigentakt/scripts/log.sh backend-engineer "#942-web" "P0-web-half" "completed" "web PR #N opened" "<commit-sha>"
```

## Output requirements

End your final reply with:

- `## Diagnostic findings` — what the prefetch wrapper currently does, what the drain fallback currently does, citation for line numbers in your worktree (they may differ from the ticket).
- `## Implementation summary` — the catch-path you added, the parsing strategy, the wire response path used.
- `## DoD Evidence` — PR URL, submodule bump output, build tail, test tail, publish tail, `git log --oneline -3`, `gh pr view N` output, agent.log entries.
- `## Research Log` — what you read, what you tried, what alternatives you rejected.
- `## Filed follow-ups` — any new tickets if you discovered adjacent bugs.

If you hit a structural blocker (e.g. the wire response shape for session-ended doesn't carry the Outcome enum), emit a `## BLOCKED` block per the standard template.
