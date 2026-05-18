You are a code reviewer subagent in the Pinder dev swarm. Review PR **#955** (fix for P0 bug #942) in pinder-core.

## Workspace
```bash
rm -rf /tmp/review-942
git clone --branch rung-2-sonnet-4-6/942-core \
  https://github.com/decay256/pinder-core /tmp/review-942
cd /tmp/review-942
```

## Cold-start
1. Read eigentakt code-reviewer spec at `/root/projects/eigentakt/agents/code-reviewer.md`.
2. Read `/root/projects/pinder-core/LESSONS_LEARNED.md` — focus on REGRESSION-TESTS-ON-BUGS, APPROVED-WORK-IS-IMMUTABLE, BUILD-PIPELINE-DISCIPLINE.
3. Read `/root/projects/pinder-core/AGENTS.md`.
4. `gh pr view 955 --repo decay256/pinder-core --json number,title,body,additions,deletions,files,commits`.
5. `gh issue view 942 --repo decay256/pinder-core --json number,title,body,comments` — full P0 bug body + both comments.

## What you're reviewing

PR #955 fixes a **P0 bug** where `GameSession.StartTurnAsync` was mutating session state (setting `_ended=true`, `_outcome=Ghosted`, calling `_playerShadows.ApplyGrowth(Dread, 1, "Ghosted")`) before throwing `GameEndedException(Ghosted)`. Combined with a prefetch-then-sync-fallback path in `ActiveSession.cs`, this produced phantom turns with `is_success=true` but `delta_from_roll=-3` after the user got ghosted.

Strategy: **B (Reorder).** The implementer moved the outcome check to throw with zero prior mutation. Shadow growth events are now constructed inline as descriptive strings inside the `GameEndedException` payload, not via `ApplyGrowth`. Callers are responsible for `session.MarkEnded(ex.Outcome)` after catching.

Diff shape: 320 added / 18 removed across:
- `src/Pinder.Core/Conversation/GameSession.cs` — the transactional refactor.
- `src/Pinder.Core/Conversation/InvariantViolationException.cs` — NEW file for the invariant guard.
- `tests/Pinder.Core.Tests/Issue942_StartTurnTransactionalTests.cs` — NEW 9-test repro pack.
- `tests/Pinder.Core.Tests/Integration/FullConversationIntegrationTest.cs` — 9-line update.
- `tests/Pinder.Core.Tests/ShadowGrowthEventTests.cs` — 5-line update.
- `tests/Pinder.Core.Tests/ShadowGrowthSpecTests.cs` — 7-line update.

## Heuristic checklist (apply in order)

### 1. AC coverage from issue #942
- [ ] `GameSession.StartTurnAsync` is transactional: if it throws `GameEndedException`, no observable state mutation on the session. Confirmed by repro test.
- [ ] Per-turn invariant guard: `roll.is_success == true` ⇒ `interest.delta_from_roll >= 0`. Log + reject (do NOT just throw and crash the request) if violated.
- [ ] Reverse-verification test: snapshot before, call StartTurnAsync, assert ThrowsAsync, snapshot after, Assert.Equal.

The web side of the AC (`ActiveSession.ResolveTurnAsync` sync-fallback fix) is explicitly deferred to a follow-up PR per the PR body; do NOT block on its absence here. Verify the PR body acknowledges this.

### 2. Strategy B correctness (THE LOAD-BEARING CHECK)

Open `src/Pinder.Core/Conversation/GameSession.cs` and verify, for **every** `throw new GameEndedException(...)` site reached from `StartTurnAsync` (there are multiple: Ghosted, Unmatched, DateSecured paths):

- No assignment to `_ended`, `_outcome`, `_currentTurn`, `_turnNumber`, `_activeTraps`, `_pendingTraps`, `_lastRoll`, etc. happens on any code path that could reach the throw.
- No method call that mutates `_playerShadows` (e.g. `ApplyGrowth`, `DrainGrowthEvents`) happens before the throw.
- Shadow events passed to the exception constructor are constructed as fresh `List<string>` / inline strings, NOT pulled from `_playerShadows.GrowthEvents`.
- If the implementer added a helper method like `BuildShadowGrowthDescription` that's read-only, verify it doesn't sneak in a mutation.

Then verify the **success** path (post-outcome-check) still calls the mutations it should (so we haven't accidentally regressed normal turn behavior). Run the full test suite — if any pre-existing GameSession test failed, that's a real regression.

### 3. The three "updated pre-existing tests" — YELLOW FLAG

The PR updates `Integration/FullConversationIntegrationTest.cs`, `ShadowGrowthEventTests.cs`, and `ShadowGrowthSpecTests.cs` "because they relied on the old mutating behavior."

**For each of these three test files:**
- Read the git diff. What did the test originally assert?
- Was the original assertion **encoding the bug** (e.g. "after StartTurnAsync throws, Dread should be 1 because ApplyGrowth ran") or **encoding correct end-state behavior** (e.g. "after we land in Ghosted, eventually Dread is 1")?
- If it encoded the bug: the update is correct. Note in your verdict.
- If it encoded correct end-state behavior the implementer is now bypassing: this is a **BLOCKER**. The fix is leaking responsibility to callers in a way that breaks contracts.

This is the most likely place this PR goes wrong. Be thorough.

### 4. Invariant guard placement

The guard belongs at the persist boundary, not at the throw boundary. Verify:
- `if (roll.is_success && interest.delta_from_roll < 0)` fires BEFORE the bad turn record is persisted.
- The reaction is `log + reject` (return early, don't persist, surface to caller) — NOT `throw new InvariantViolationException(...)` which would crash the request and lose user trust.
- The guard has a unit test that constructs a synthetic violating turn record and asserts it gets rejected.

If the implementer chose `throw` over `log+reject`: that's a **non-blocking** style note (the spawn task allowed either, picking the more reversible). Flag it but don't block.

### 5. Reverse-verification test (the AC test)

In `Issue942_StartTurnTransactionalTests.cs`, find the test named approximately `StartTurnAsync_ThrowsGameEnded_LeavesSessionStateUnchanged`. Verify:
- It builds a session in the Bored-about-to-Ghost state.
- It captures a snapshot BEFORE (interest, turnNumber, activeTraps, shadowState — at minimum these four).
- It calls `Assert.ThrowsAsync<GameEndedException>`.
- It captures a snapshot AFTER.
- It asserts the snapshots are equal.

If the snapshot omits any of the four critical fields: **CHANGES_REQUESTED** with the missing field name.

### 6. No production code touched in unrelated paths

`git log -p HEAD -- src/` should show changes only in `GameSession.cs` and the new `InvariantViolationException.cs`. No other `src/` files. Reject if anything else is touched.

### 7. Self-verify
```bash
dotnet build -c Release 2>&1 | tail -5
dotnet test tests/Pinder.Core.Tests/Pinder.Core.Tests.csproj --filter "FullyQualifiedName~Issue942" --nologo 2>&1 | tail -5
dotnet test tests/Pinder.Core.Tests/Pinder.Core.Tests.csproj --nologo 2>&1 | tail -5
```

Build: 0 errors required. Issue942 tests: all pass required. Full suite: 0 failed required.

## Output requirements

End your final reply with EXACTLY this structured block:

```
## Review verdict
verdict: APPROVE | CHANGES_REQUESTED
blockers: <count>
non_blocking: <count>

## Blockers
- (one per line, or "none")

## Non-blocking suggestions
- (one per line, or "none")

## The three updated tests — verdict per file
- FullConversationIntegrationTest.cs: <bug-encoded | contract-encoded | mixed> — <one-line justification>
- ShadowGrowthEventTests.cs: <bug-encoded | contract-encoded | mixed> — <one-line justification>
- ShadowGrowthSpecTests.cs: <bug-encoded | contract-encoded | mixed> — <one-line justification>

## Self-verify
- Build: <result>
- Issue942 tests: <pass/fail counts>
- Full suite: <pass/fail/skip counts>
```

Then post the review via `gh pr review 955 --repo decay256/pinder-core --approve --body "<verdict body>"` (for APPROVE) or `--request-changes --body "..."` (for CHANGES_REQUESTED). If self-approve is blocked because the gh token identity matches the PR author, fall back to `--comment` with the same body and the verdict line clearly stated — that's the SELF-APPROVE-BLOCKED pattern.

## Logging to agent.log

Task entry:
```bash
bash /root/projects/eigentakt/scripts/log.sh code-reviewer "#942" "PR-955-review" "started" "P0 transactional fix review (Rung 2 implementer); 3 pre-existing tests updated need scrutiny"
```

Task exit:
```bash
bash /root/projects/eigentakt/scripts/log.sh code-reviewer "#942" "PR-955-review" "completed" "<APPROVE|CHANGES_REQUESTED> posted"
```

## DO NOT
- Do not merge.
- Do not push commits.
- Do not edit the PR's branch directly.
- Do not approve if any heuristic check finds a real blocker.
- Do not block on the web-side fix's absence — that's a follow-up PR by design.
