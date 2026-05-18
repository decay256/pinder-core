You are a backend engineer subagent in the Pinder dev swarm. Implement ticket **#948** in pinder-web: all sessions show "outcome unknown" because `user_sessions.outcome` is never written.

## Workspace isolation
```bash
cd /root/projects/pinder-web
git fetch origin
git worktree add /tmp/work-948 origin/main
cd /tmp/work-948
git checkout -b fix/948-session-outcome-persistence
git submodule update --init pinder-core
```

## Cold-start
1. Read `/root/projects/eigentakt/agents/backend-engineer.md`.
2. Read `/root/projects/pinder-web/AGENTS.md` AND `/root/projects/pinder-core/AGENTS.md`.
3. `gh issue view 948 --repo decay256/pinder-core --json number,title,body,comments`.

## Diagnosis (do this first)

Per the ticket:
- `user_sessions.outcome` is NULL for all 26 staging rows AND all prod rows.
- `user_sessions.ended_at` is also NULL for all 26.
- Engine state `game_sessions.outcome` exists but isn't propagated.

Grep:
```bash
cd /tmp/work-948
grep -rn "user_sessions\|UserSession\|UpdateOutcome\|EndSession\|MarkEnded" src/Pinder.GameApi/Data/ src/Pinder.GameApi/Services/ 2>&1 | head -40
grep -rn "outcome\s*=\|Outcome\s*=\|ended_at\|EndedAt" src/Pinder.GameApi/Data/GameSessionRepository.cs src/Pinder.GameApi/Services/ 2>&1 | head -30
grep -rn "GameEndedException\|GameSession.Outcome" src/Pinder.GameApi/ /root/projects/pinder-web/pinder-core/src/ 2>&1 | head -30
```

Find:
- Where does game-end fire (engine throws `GameEndedException`)?
- Does anything catch it AND `UPDATE user_sessions SET outcome=..., ended_at=...`?
- The `UserSessionSummaryDto.Outcome` map (likely in `SessionsController.cs`) — what does it return when `outcome` is NULL?

The recently-merged #942 (pinder-web PR #656 sha c9c4f2a) added `GameEndedException` handling in pinder-web. Read that diff:
```bash
git log --oneline --all | head -20
git show c9c4f2a --stat
```

The fix likely belongs at the same site as #942's catch block — extend it to UPDATE `user_sessions.outcome` + `ended_at`.

## Goal

Make every game-end (Date / Ghosted / Unmatched / Aborted) atomically:
1. Persist engine state (already does).
2. Set `user_sessions.outcome` to the enum string.
3. Set `user_sessions.ended_at` to the current UTC timestamp.

Also: make the session-list DTO never return 'unknown'. NULL `outcome` on an in-progress session should map to **'In progress'**, not 'unknown' or null.

## Two-phase fix

### Phase A — write the outcome
File: probably `src/Pinder.GameApi/Data/GameSessionRepository.cs` or wherever the engine-state write happens. Likely involves:
- A new repository method `MarkSessionEndedAsync(sessionId, outcome, endedAt)` that does the SQL `UPDATE user_sessions SET outcome=@outcome, ended_at=@endedAt WHERE session_id=@id`.
- Wire it into the GameEndedException catch site (added by #942). Same transaction if practical.

If there's a Dapper/EF migration needed (e.g., the `outcome` column type), don't change schema in this PR — file a follow-up.

### Phase B — display fallback
File: `src/Pinder.GameApi/Controllers/SessionsController.cs` (the outcome enum-to-string map). When `outcome` is NULL:
- If `ended_at` is also NULL → return **"In progress"**
- If `ended_at` is set but `outcome` is NULL → return **"Aborted"** (orphan; shouldn't happen post-fix but defensive)
- Otherwise → use the enum value: "Dated", "Ghosted", "Unmatched", "Aborted"

The previous 'Unmatched' default for null was misleading per the ticket.

## Acceptance criteria

- Every game-end event writes outcome + ended_at atomically.
- Session-list endpoint returns one of: 'In progress', 'Dated', 'Ghosted', 'Unmatched', 'Aborted' — never 'unknown'.
- Regression test in `src/Pinder.GameApi.Tests/` simulates a Ghosted end → asserts the row has non-NULL outcome+ended_at AND the list DTO renders 'Ghosted'.
- Regression test for in-progress: asserts list DTO renders 'In progress' when outcome is NULL.

## Backfill — out of scope

The 26 NULL rows in staging+prod stay NULL for now. Filing a follow-up ticket for backfill is fine; don't write a migration in this PR.

## Tests

- `src/Pinder.GameApi.Tests/Issue948_SessionOutcomePersistenceTests.cs` — two cases: in-progress + completed (Ghosted).
- Co-located mirror test in frontend if SessionList component has tests.

## Build evidence

```bash
cd /tmp/work-948
dotnet build -c Release 2>&1 | tee /tmp/948-build.log | tail -10
dotnet test -c Release --no-build 2>&1 | tee /tmp/948-test.log | tail -30
# Issue948 tests MUST pass; baseline ~56 stake_llm_failed unchanged.
if [ -d frontend ] && [ -f frontend/package.json ]; then
  cd frontend && npm test -- --run 2>&1 | tee /tmp/948-fe-test.log | tail -20 && cd ..
fi
```

## Commit + push

Explicit pathspecs:
```bash
git add <each file explicitly>
git status
git commit -m "fix(#948): persist user_sessions.outcome and ended_at on game-end

- New GameSessionRepository.MarkSessionEndedAsync writes outcome + ended_at
- Wired into the GameEndedException catch site (added by #942)
- SessionsController.UserSessionSummaryDto now returns 'In progress' instead
  of 'unknown' when outcome is NULL on an in-progress session
- New Issue948_SessionOutcomePersistenceTests cover both paths"
git push origin fix/948-session-outcome-persistence
```

## PR

```bash
gh pr create --repo decay256/pinder-web --base main --head fix/948-session-outcome-persistence \
  --title "fix(#948): persist user_sessions.outcome on game-end + show 'In progress' for live sessions" \
  --body "Closes #948.

## Summary
- Persist user_sessions.outcome and ended_at when a game ends.
- Show 'In progress' (not 'unknown') for live sessions in the session list.

## DoD evidence
\`\`\`
$(tail -5 /tmp/948-build.log)
$(tail -5 /tmp/948-test.log)
\`\`\`

## Backfill follow-up
The 26 NULL rows in staging+prod will stay NULL until a separate backfill ticket. Filed as part of this run."
```

## Workflow rules
- Do NOT merge.
- Do NOT touch the DB schema (no migrations). If schema changes are needed, STOP and report.
- Pathspecs only.

## Logging
```bash
bash /root/projects/eigentakt/scripts/log.sh backend-engineer "#948" "session-outcome-persistence" "started" "Persist user_sessions.outcome + ended_at on game-end; 'In progress' fallback"
```
After PR:
```bash
bash /root/projects/eigentakt/scripts/log.sh backend-engineer "#948" "session-outcome-persistence" "completed" "PR #N opened" "<sha>"
```

## Output requirements

End with:
- `## Diagnostic findings` — where game-end fires, why outcome wasn't persisted, the missing UPDATE.
- `## Implementation summary` — repository method + catch site wiring + DTO fallback.
- `## DoD Evidence` — PR URL, build tail, test tail.
- `## Research Log`.
- `## Filed follow-ups` — backfill ticket (file it inline: `gh issue create --repo decay256/pinder-core --title "[chore][P2] Backfill NULL user_sessions.outcome for pre-#948 sessions"`).

All upstream events follow the response-style rules in USER.md — short, lead with the result, no tables.
