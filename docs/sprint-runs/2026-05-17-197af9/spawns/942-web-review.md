You are a code reviewer subagent in the Pinder dev swarm. Review PR **#656** in `decay256/pinder-web` (the web-side half of P0 #942).

## Workspace
```bash
rm -rf /tmp/review-942-web
git clone --branch rung-2-sonnet-4-6/942-web --recurse-submodules \
  https://github.com/decay256/pinder-web /tmp/review-942-web
cd /tmp/review-942-web
```

## Cold-start
1. Read `/root/projects/eigentakt/agents/code-reviewer.md`.
2. Read `/root/projects/pinder-web/LESSONS_LEARNED.md` (esp. §10 submodule drift, §13 commit order).
3. Read `/root/projects/pinder-web/AGENTS.md`.
4. `gh pr view 656 --repo decay256/pinder-web --json number,title,body,additions,deletions,files,commits`.
5. `gh issue view 942 --repo decay256/pinder-core --json number,title,body,comments` — full P0 context.
6. Read merged core PR for caller-contract context: `gh pr view 955 --repo decay256/pinder-core --json body` (the changes that produced the GameEndedException.ShadowGrowthEvents payload).

## What you're reviewing

PR #656 is the **web half** of P0 #942. The core engine half merged as `1be887c`. This PR:

1. Bumps the pinder-core submodule to `1be887c`.
2. In `src/Pinder.GameApi/Services/ActiveSession.cs::EnsureTurnStartedLockedAsync`, adds a typed `catch (GameEndedException ex)` block BEFORE the generic `catch (Exception ex)`. On match: `MarkEnded` on the engine, set `_ended=true` on `ActiveSession`, parse `ex.ShadowGrowthEvents` and apply each growth via `PlayerShadowTracker.ApplyGrowth`, then re-throw so the existing `SessionsController` catch-and-wire path runs.
3. New test file `Issue942_PrefetchSurfacesGhostedTests.cs` (~287 lines) covering: outcome surfaced, `IsEnded == true`, Dread delta == 1 after Ghost catch.
4. Updates two pre-existing tests (`Issue122_TypedEndedExceptionTests` line ~228 assertion, `Issue306PrefetchNextTurnTests` — assertion adjustments).

Diff size: 409 lines / 8 removed across 5 files.

## Heuristic checklist (apply in order)

### 1. AC coverage from issue #942 + spawn task

- [ ] Submodule bumped to `1be887c`. `git -C pinder-core log -1 --oneline` must show the transactional fix commit.
- [ ] `ActiveSession.EnsureTurnStartedLockedAsync` catches `GameEndedException` and does NOT fall through to the sync re-run.
- [ ] After the catch: `session.MarkEnded(ex.Outcome)` called, growth from `ex.ShadowGrowthEvents` reapplied.
- [ ] Re-throw routes the exception to `SessionsController` which already handles `GameEndedException` → wire response.
- [ ] Issue #942 will close on merge of this PR (it's still open after core PR #955 because growth was deferred).

### 2. The catch path correctness (THE LOAD-BEARING CHECK)

Open `src/Pinder.GameApi/Services/ActiveSession.cs` and verify:

- The typed `catch (GameEndedException ex)` is placed BEFORE the generic `catch (Exception ex)` — otherwise C# catch ordering means the generic catch swallows it first. **If wrong order: BLOCKER.**
- `Session.MarkEnded(ex.Outcome)` is called. **If missing: BLOCKER** — without this, the engine state never records the end.
- Shadow growth reapplication: for each entry in `ex.ShadowGrowthEvents`, parse and call `PlayerShadowTracker.ApplyGrowth(stat, amount, reason)`. **If missing or skipped: BLOCKER** — that's the entire reason this PR exists.
- `throw;` (re-throw) at the end of the catch block so the outer `SessionsController` catch runs. **If swallowed or rethrown as different exception: probably BLOCKER**, since the wire response shape depends on the controller catching the original.

### 3. Parsing fragility — the yellow flag

The implementer parses strings like `"Dread +1 (Ghosted)"` to reconstruct `(ShadowStatType.Dread, 1, "Ghosted")` because `GameEndedException.ShadowGrowthEvents` is `IReadOnlyList<string>` (#956 will fix this).

Verify:
- The parse helper (`TryParseShadowGrowthEvent` or similar) handles all 6 shadow stats: `Madness`, `Despair`, `Denial`, `Fixation`, `Dread`, `Overthinking`. **Cross-reference with `pinder-core/src/Pinder.Core/Stats/StatType.cs` — the canonical enum.** If any shadow is missing from the parser's switch, NEW UNBOUNDED GROWTH SOURCE will silently drop when that shadow is the one Ghosted produces.
- The parse helper handles non-Ghosted growth events too (any future `GameEndedException` carrying growth — the helper shouldn't be hardcoded to Ghosted/Dread).
- The parse failure mode is "log and skip" (safe degrade), not "throw" (would mask the original GameEndedException).
- The parse helper has a unit test.

### 4. The two updated pre-existing tests — YELLOW FLAG

For each:

**`Issue122_TypedEndedExceptionTests.cs`** — the prior assertion was probably `Assert.Equal(GameOutcome.Unmatched, gameSession.Outcome)` or similar, which now fails because the engine's transactional contract doesn't set Outcome on throw. The updated test should:
- Either assert the engine throws but doesn't yet have `_outcome` set (current contract), OR
- Assert the post-`MarkEnded` engine state (after the catch path runs).

Either is legitimate. If the test was just removed without replacement, that's a BLOCKER.

**`Issue306PrefetchNextTurnTests.cs`** — this test uses a `FaultingPrefetchAdapter` that throws `InvalidOperationException`, NOT `GameEndedException`. The new catch-path should not match. If the test was updated to expect different behavior on a generic-exception prefetch fault, verify the change is justified.

For each: read the git diff, classify as `bug-encoded` / `contract-encoded` / `legitimate-update` / `regression-mask`, document in your verdict.

### 5. No production code touched in unrelated paths

`git log -p HEAD~2..HEAD -- src/` should show changes only in `ActiveSession.cs` and (optionally) a new helper class for the parser. **No `Controllers/SessionsController.cs` changes** — the design intentionally reuses the existing controller catch. If the implementer modified the controller too: yellow flag, may be a sign of unnecessary scope creep.

### 6. Self-verify
```bash
cd /tmp/review-942-web
dotnet build -c Release 2>&1 | tail -5
dotnet test src/Pinder.GameApi.Tests/Pinder.GameApi.Tests.csproj --filter "FullyQualifiedName~Issue942" --nologo 2>&1 | tail -5
dotnet test src/Pinder.GameApi.Tests/Pinder.GameApi.Tests.csproj --nologo 2>&1 | tail -5
```

Build: 0 errors required.
Issue942 tests: all pass required.
Full suite: same pass/fail count as main (56 pre-existing baseline failures are documented; new regressions are a BLOCKER).

### 7. Submodule sanity (LESSONS_LEARNED §10 / §13)

- `git diff origin/main -- pinder-core` must show `a0fd2c2 → 1be887c` and nothing else (no `-dirty` suffix, no extra commits leaked from a local clone).
- `git -C pinder-core status` in the worktree should be clean — no uncommitted submodule edits.
- Submodule bump should be in a separate commit from the code change (per §13 commit order). Verify there are two commits, not one.

## Output requirements

End your final reply with EXACTLY:

```
## Review verdict
verdict: APPROVE | CHANGES_REQUESTED
blockers: <count>
non_blocking: <count>

## Blockers
- (one per line, or "none")

## Non-blocking suggestions
- (one per line, or "none")

## The two updated tests — verdict per file
- Issue122_TypedEndedExceptionTests.cs: <bug-encoded | contract-encoded | legitimate-update | regression-mask> — <one-line justification>
- Issue306PrefetchNextTurnTests.cs: <bug-encoded | contract-encoded | legitimate-update | regression-mask> — <one-line justification>

## Shadow growth parser coverage
- Stats covered: <list>
- Stats missing (BLOCKER if any are emitted by the engine): <list or "none">

## Self-verify
- Build: <result>
- Issue942 tests: <pass/fail counts>
- Full suite: <pass/fail/skip counts vs baseline>
- Submodule: <clean | issues>
```

Then post the review via `gh pr review 656 --repo decay256/pinder-web --approve --body "..."` or `--request-changes --body "..."`. If self-approve is blocked (gh token identity matches PR author): fall back to `--comment` with `verdict: APPROVE/CHANGES_REQUESTED` stated clearly in the body.

## Logging to agent.log
Entry:
```bash
bash /root/projects/eigentakt/scripts/log.sh code-reviewer "#942-web" "PR-656-review" "started" "P0 web-half review (Rung 2 implementer); catch-path correctness + parsing coverage focus"
```
Exit:
```bash
bash /root/projects/eigentakt/scripts/log.sh code-reviewer "#942-web" "PR-656-review" "completed" "<APPROVE|CHANGES_REQUESTED> posted"
```

## DO NOT
- Do not merge.
- Do not push commits.
- Do not edit the PR's branch directly.
- Do not approve if the catch path lacks `MarkEnded` or growth reapplication — those are the entire fix.
